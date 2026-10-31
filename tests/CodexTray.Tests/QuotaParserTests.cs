using System.Globalization;
using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class QuotaParserTests
{
    [Fact]
    public void ParsesNormalMainQuotaBucket()
    {
        var snapshot = Parse("""
            {
              "rateLimits": {
                "limitId": "codex",
                "primary": { "usedPercent": 25, "windowDurationMins": 300, "resetsAt": 1730947200 },
                "secondary": { "usedPercent": 40, "windowDurationMins": 10080, "resetsAt": 1731552000 },
                "planType": "plus",
                "credits": { "hasCredits": true, "unlimited": false, "balance": "12.50" }
              },
              "rateLimitResetCredits": {
                "availableCount": 2,
                "credits": [{
                  "id": "opaque-id-is-not-retained",
                  "resetType": "codexRateLimits",
                  "title": "Rate-limit reset",
                  "description": "Reset an eligible Codex window.",
                  "expiresAt": 1784246400
                }]
              }
            }
            """);

        var bucket = Assert.Single(snapshot.Buckets);
        Assert.Equal("codex", bucket.Id);
        Assert.Equal(25m, bucket.Primary!.UsedPercent);
        Assert.Equal(75m, bucket.Primary.RemainingPercent);
        Assert.Equal(10_080, bucket.Secondary!.WindowDurationMins);
        Assert.Equal("plus", bucket.PlanType);
        Assert.Equal(12.50m, snapshot.Credits!.Balance);
        Assert.Equal(2, snapshot.AvailableResetCredits);
        var reset = Assert.Single(snapshot.ResetCredits);
        Assert.Equal("Rate-limit reset", reset.Title);
        Assert.Equal("codexRateLimits", reset.ResetType);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784246400), reset.ExpiresAtUtc);
    }

    [Fact]
    public void RichMultiBucketViewTakesPrecedenceAndPreservesUnknownIds()
    {
        var snapshot = Parse("""
            {
              "rateLimits": { "limitId": "codex", "primary": { "usedPercent": 99 } },
              "rateLimitsByLimitId": {
                "codex": { "limitId": "codex", "primary": { "usedPercent": 20, "windowDurationMins": 60 } },
                "gpt_omega_special": {
                  "limitId": "gpt_omega_special",
                  "limitName": "Omega Special",
                  "primary": { "usedPercent": 42.5, "windowDurationMins": 1440 },
                  "futureField": { "anything": true }
                }
              },
              "unknownTopLevel": 123
            }
            """);

        Assert.Equal(2, snapshot.Buckets.Count);
        Assert.Equal(20m, snapshot.IncludedBucket!.Primary!.UsedPercent);
        var additional = Assert.Single(snapshot.Buckets, bucket => bucket.Id == "gpt_omega_special");
        Assert.Equal("Omega Special", additional.DisplayName);
        Assert.Equal(42.5m, additional.Primary!.UsedPercent);
    }

    [Fact]
    public void NullPrimaryAndSecondaryAreNormal()
    {
        var snapshot = Parse("""{ "rateLimits": { "limitId": "codex", "primary": null, "secondary": null } }""");
        var bucket = Assert.Single(snapshot.Buckets);
        Assert.Null(bucket.Primary);
        Assert.Null(bucket.Secondary);
    }

    [Fact]
    public void MissingCreditsAreNormal()
    {
        var snapshot = Parse("""{ "rateLimits": { "primary": { "usedPercent": 1 } } }""");
        Assert.Null(snapshot.Credits);
    }

    [Fact]
    public void ParsesDecimalCreditBalanceWithoutBinaryFloatingPoint()
    {
        var snapshot = Parse("""
            { "rateLimits": { "primary": { "usedPercent": 1 }, "credits": { "balance": "1234567890.123456789012345678" } } }
            """);

        Assert.Equal(1234567890.123456789012345678m, snapshot.Credits!.Balance);
        Assert.Equal("1234567890.123456789012345678", snapshot.Credits.OriginalBalance);
    }

    [Fact]
    public void ConvertsUnixResetTimestampToRequestedLocalTimeZone()
    {
        var snapshot = Parse("""{ "rateLimits": { "primary": { "usedPercent": 1, "resetsAt": 1704067200 } } }""");
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestEastern", TimeSpan.FromHours(-5), "TestEastern", "TestEastern");

        var formatted = QuotaFormatting.FormatReset(snapshot.IncludedBucket!.Primary!.ResetsAtUtc, zone);

        Assert.Equal("Resets Sun, Dec 31 at 7:00 PM", formatted);
    }

    [Theory]
    [InlineData(15, "15-minute quota")]
    [InlineData(300, "5-hour quota")]
    [InlineData(10080, "1-week quota")]
    [InlineData(20160, "2-week quota")]
    public void DerivesWindowLabelFromDuration(int minutes, string expected)
    {
        Assert.Equal(expected, QuotaFormatting.WindowLabel(minutes));
    }

    [Fact]
    public void CountdownUsesLocalCalendarDaysInsteadOfRollingTwentyFourHours()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestEastern", TimeSpan.FromHours(-4), "TestEastern", "TestEastern");
        var now = DateTimeOffset.Parse("2026-07-16T17:00:00-04:00");
        var expiry = DateTimeOffset.Parse("2026-07-17T20:00:00-04:00");

        Assert.Equal("Expires in 1 day", QuotaFormatting.ExpiryCountdown(expiry, now, zone));
        Assert.Equal("Expires in 1 day (7/17 8:00 PM)", QuotaFormatting.ExpiryCountdownWithDate(expiry, now, zone));
        Assert.Equal("Resets in 1 day (7/17 8:00 PM)", QuotaFormatting.ResetCountdownWithDate(expiry, now, zone));
    }

    [Fact]
    public void CountdownCallsLaterOnTheSameDateToday()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestEastern", TimeSpan.FromHours(-4), "TestEastern", "TestEastern");
        var now = DateTimeOffset.Parse("2026-07-16T17:00:00-04:00");
        var reset = DateTimeOffset.Parse("2026-07-16T23:00:00-04:00");

        Assert.Equal("Resets today", QuotaFormatting.ResetCountdown(reset, now, zone));
        Assert.Equal("Resets today (7/16 11:00 PM)", QuotaFormatting.ResetCountdownWithDate(reset, now, zone));
    }

    [Fact]
    public void BankedResetExpiryMoreThanOneDayAwayOmitsTime()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestEastern", TimeSpan.FromHours(-4), "TestEastern", "TestEastern");
        var now = DateTimeOffset.Parse("2026-07-16T17:00:00-04:00");
        var expiry = DateTimeOffset.Parse("2026-07-18T20:00:00-04:00");

        Assert.Equal("Expires in 2 days (7/18)", QuotaFormatting.ExpiryCountdownWithDate(expiry, now, zone));
    }

    [Theory]
    [InlineData("1978.000001", "1,979")]
    [InlineData("1978", "1,978")]
    [InlineData("0.01", "1")]
    public void RoundsCreditBalanceUpToAWholeNumber(string balance, string expected)
    {
        var parsed = decimal.Parse(balance, CultureInfo.InvariantCulture);
        Assert.Equal(expected, QuotaFormatting.FormatBalance(new CreditBalance(true, false, parsed, balance)));
    }

    private static QuotaSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(QuotaParser.TryParse(document.RootElement, DateTimeOffset.UnixEpoch, out var snapshot));
        return Assert.IsType<QuotaSnapshot>(snapshot);
    }
}
