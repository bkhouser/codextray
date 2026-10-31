using System.Globalization;
using System.Text.Json;

namespace CodexTray.Core;

public static class QuotaParser
{
    public static bool TryParse(JsonElement payload, DateTimeOffset updatedAt, out QuotaSnapshot? snapshot)
    {
        snapshot = null;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var buckets = new List<QuotaBucket>();
        QuotaBucket? compatibilityBucket = null;

        if (TryGetObject(payload, "rateLimits", out var rateLimits))
        {
            compatibilityBucket = ParseBucket(rateLimits, "codex");
        }

        if (TryGetObject(payload, "rateLimitsByLimitId", out var byId))
        {
            foreach (var property in byId.EnumerateObject().Take(64))
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    buckets.Add(ParseBucket(property.Value, property.Name));
                }
            }
        }

        if (buckets.Count == 0 && compatibilityBucket is not null)
        {
            buckets.Add(compatibilityBucket);
        }

        if (buckets.Count == 0)
        {
            return false;
        }

        var credits = compatibilityBucket?.Credits
            ?? buckets.Select(bucket => bucket.Credits).FirstOrDefault(value => value is not null)
            ?? ParseCredits(payload);

        int? availableCount = null;
        var resetCreditItems = new List<QuotaResetCredit>();
        if (TryGetObject(payload, "rateLimitResetCredits", out var resetCredits))
        {
            if (TryGetInt32(resetCredits, "availableCount", out var count))
            {
                availableCount = count;
            }

            if (resetCredits.TryGetProperty("credits", out var creditsElement)
                && creditsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var credit in creditsElement.EnumerateArray().Take(256))
                {
                    if (credit.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var resetType = GetString(credit, "resetType");
                    resetCreditItems.Add(new QuotaResetCredit(
                        GetString(credit, "title") ?? FriendlyResetTitle(resetType),
                        GetString(credit, "description"),
                        resetType,
                        ParseUnixTimestamp(credit, "expiresAt")));
                }
            }
        }

        snapshot = new QuotaSnapshot(buckets, credits, availableCount, resetCreditItems, updatedAt);
        return true;
    }

    private static QuotaBucket ParseBucket(JsonElement element, string fallbackId)
    {
        var id = GetString(element, "limitId") ?? fallbackId[..Math.Min(fallbackId.Length, 256)];
        var suppliedName = GetString(element, "limitName");
        return new QuotaBucket(
            id,
            QuotaFormatting.FriendlyBucketName(id, suppliedName),
            GetString(element, "planType"),
            ParseWindow(element, "primary"),
            ParseWindow(element, "secondary"),
            ParseCredits(element),
            GetString(element, "rateLimitReachedType"));
    }

    private static QuotaWindow? ParseWindow(JsonElement parent, string propertyName)
    {
        if (!TryGetObject(parent, propertyName, out var element))
        {
            return null;
        }

        if (!TryGetDecimal(element, "usedPercent", out var usedPercent))
        {
            return null;
        }

        int? duration = TryGetInt32(element, "windowDurationMins", out var parsedDuration)
            ? parsedDuration
            : null;
        return new QuotaWindow(
            Math.Clamp(usedPercent, 0m, 100m),
            duration,
            ParseUnixTimestamp(element, "resetsAt"));
    }

    private static DateTimeOffset? ParseUnixTimestamp(JsonElement parent, string propertyName)
    {
        if (TryGetInt64(parent, propertyName, out var seconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private static string FriendlyResetTitle(string? resetType) => resetType switch
    {
        "codexRateLimits" => "Codex quota reset",
        null or "" => "Quota reset",
        _ => QuotaFormatting.FriendlyBucketName(resetType, null)
    };

    private static CreditBalance? ParseCredits(JsonElement parent)
    {
        if (!TryGetObject(parent, "credits", out var credits))
        {
            return null;
        }

        bool? hasCredits = TryGetBoolean(credits, "hasCredits", out var has) ? has : null;
        bool? unlimited = TryGetBoolean(credits, "unlimited", out var unlimitedValue) ? unlimitedValue : null;
        decimal? balance = null;
        string? originalBalance = null;

        if (credits.TryGetProperty("balance", out var balanceElement)
            && balanceElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            originalBalance = balanceElement.ValueKind == JsonValueKind.String
                ? balanceElement.GetString()
                : balanceElement.GetRawText();
            if (decimal.TryParse(originalBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                balance = parsed;
            }
        }

        return new CreditBalance(hasCredits, unlimited, balance, originalBalance);
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { } text ? text[..Math.Min(text.Length, 256)] : null
            : null;

    private static bool TryGetBoolean(JsonElement parent, string name, out bool value)
    {
        if (parent.TryGetProperty(name, out var element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetDecimal(JsonElement parent, string name, out decimal value)
    {
        if (parent.TryGetProperty(name, out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String
                && decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetInt32(JsonElement parent, string name, out int value)
    {
        if (parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt64(JsonElement parent, string name, out long value)
    {
        if (parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
