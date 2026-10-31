using System.Globalization;

namespace CodexTray.Core;

public static class QuotaFormatting
{
    public static string WindowLabel(int? minutes)
    {
        if (minutes is null or <= 0)
        {
            return "quota";
        }

        return minutes.Value switch
        {
            < 60 => $"{minutes}-minute quota",
            _ when minutes.Value % 10_080 == 0 => Plural(minutes.Value / 10_080, "week") + " quota",
            _ when minutes.Value % 1_440 == 0 => Plural(minutes.Value / 1_440, "day") + " quota",
            _ when minutes.Value % 60 == 0 => Plural(minutes.Value / 60, "hour") + " quota",
            _ => $"{minutes}-minute quota"
        };
    }

    public static string FormatReset(DateTimeOffset? resetUtc, TimeZoneInfo? timeZone = null)
    {
        if (resetUtc is null)
        {
            return "Reset time unavailable";
        }

        var local = TimeZoneInfo.ConvertTime(resetUtc.Value, timeZone ?? TimeZoneInfo.Local);
        return $"Resets {local:ddd, MMM d 'at' h:mm tt}";
    }

    public static string ResetCountdown(
        DateTimeOffset? resetUtc,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        if (resetUtc is null)
        {
            return "Reset time unavailable";
        }

        return Countdown(resetUtc.Value, now, "Resets", includeDate: false, includeTimeWithinOneDay: false, timeZone);
    }

    public static string ResetCountdownWithDate(
        DateTimeOffset? resetUtc,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        if (resetUtc is null)
        {
            return "Reset time unavailable";
        }

        return Countdown(resetUtc.Value, now, "Resets", includeDate: true, includeTimeWithinOneDay: true, timeZone);
    }

    public static string ExpiryCountdown(
        DateTimeOffset? expiryUtc,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        if (expiryUtc is null)
        {
            return "No expiration shown";
        }

        return Countdown(expiryUtc.Value, now, "Expires", includeDate: false, includeTimeWithinOneDay: false, timeZone);
    }

    public static string ExpiryCountdownWithDate(
        DateTimeOffset? expiryUtc,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        if (expiryUtc is null)
        {
            return "No expiration shown";
        }

        return Countdown(expiryUtc.Value, now, "Expires", includeDate: true, includeTimeWithinOneDay: true, timeZone);
    }

    public static string FormatBalance(CreditBalance? credits)
    {
        if (credits?.Unlimited == true)
        {
            return "Unlimited";
        }

        return credits?.Balance is { } balance
            ? decimal.Ceiling(balance).ToString("#,0", CultureInfo.InvariantCulture)
            : "Unavailable";
    }

    public static string FriendlyBucketName(string id, string? suppliedName)
    {
        if (!string.IsNullOrWhiteSpace(suppliedName))
        {
            return suppliedName.Trim();
        }

        if (string.Equals(id, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return "Included quota";
        }

        var words = id.Replace('_', ' ').Replace('-', ' ').Trim();
        if (words.Length == 0)
        {
            return "Additional quota";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.ToLowerInvariant());
    }

    private static string Plural(int value, string unit) => value == 1 ? $"1-{unit}" : $"{value}-{unit}";

    private static string Countdown(
        DateTimeOffset target,
        DateTimeOffset now,
        string verb,
        bool includeDate,
        bool includeTimeWithinOneDay,
        TimeZoneInfo? timeZone)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var localTarget = TimeZoneInfo.ConvertTime(target, zone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        string relative;

        if (target <= now)
        {
            relative = $"{verb} soon";
        }
        else
        {
            var calendarDays = (localTarget.Date - localNow.Date).Days;
            relative = calendarDays switch
            {
                <= 0 => $"{verb} today",
                1 => $"{verb} in 1 day",
                _ => $"{verb} in {calendarDays} days"
            };
        }

        if (!includeDate)
        {
            return relative;
        }

        var daysFromToday = (localTarget.Date - localNow.Date).Days;
        var dateFormat = includeTimeWithinOneDay && daysFromToday is >= 0 and <= 1
            ? "M/d h:mm tt"
            : "M/d";
        return $"{relative} ({localTarget.ToString(dateFormat, CultureInfo.InvariantCulture)})";
    }
}
