using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class NotificationRegressionTests
{
    [Fact]
    public void RevealingCountOnlyDetailsDoesNotReannounceExistingCredits()
    {
        var tracker = new ResetCreditTracker();
        tracker.Observe([], 2);
        Assert.False(tracker.Observe([new("A", null, null, null), new("B", null, null, null)], 2).HasNew);
        Assert.Equal(1, tracker.Observe([new("A", null, null, null), new("B", null, null, null), new("C", null, null, null)], 3).TotalNew);
    }

    [Fact]
    public void TemporaryMissingDetailsDoNotHideNewReplacementAfterRestart()
    {
        var tracker = new ResetCreditTracker();
        tracker.Observe([new("A", null, null, null)], 1);
        tracker.Observe([], 1);
        Assert.Equal(0, tracker.State.Unidentified);
        var persisted = JsonSerializer.Deserialize<ResetCreditState>(JsonSerializer.Serialize(tracker.State));
        tracker = new(persisted);
        var replacement = tracker.Observe([new("B", null, null, null)], 1);
        Assert.Equal("B", Assert.Single(replacement.NewCredits).Title);
        Assert.Equal(1, replacement.TotalNew);
    }

    [Fact]
    public void PartialBaselineIdentificationSurvivesMissingMetadataWithoutGrowing()
    {
        var tracker = new ResetCreditTracker();
        tracker.Observe([], 2);
        Assert.False(tracker.Observe([new("A", null, null, null)], 2).HasNew);
        Assert.Equal(1, tracker.State.Unidentified);
        tracker.Observe([], 2);
        Assert.Equal(1, tracker.State.Unidentified);
        Assert.False(tracker.Observe([new("B", null, null, null)], 2).HasNew);
        Assert.Equal(0, tracker.State.Unidentified);
        Assert.True(tracker.Observe([new("C", null, null, null)], 2).HasNew);
    }

    [Fact]
    public void OnlyActualCountIncreaseIntroducesNewUnidentifiedSlots()
    {
        var tracker = new ResetCreditTracker();
        tracker.Observe([new("A", null, null, null)], 1);
        tracker.Observe([], 1);
        Assert.Equal(1, tracker.Observe([], 2).AdditionalUnspecifiedCredits);
        Assert.Equal(1, tracker.State.Unidentified);
        Assert.False(tracker.Observe([new("A", null, null, null), new("B", null, null, null)], 2).HasNew);
        Assert.Equal(0, tracker.State.Unidentified);
    }

    [Fact]
    public void WorkspaceIdentityDoesNotDependOnJsonOrderingOrWhitespace()
    {
        using var a = JsonDocument.Parse("""{"account":{"email":"synthetic@example.invalid"},"workspaceRouting":{"b":2,"a":1}}""");
        using var b = JsonDocument.Parse("""{"account":{"email":"synthetic@example.invalid"},"workspaceRouting": { "a":1.0, "b":2 }}""");
        Assert.Equal(AccountIdentity.GetScope(a.RootElement), AccountIdentity.GetScope(b.RootElement));
    }

    [Fact]
    public void IdenticalMetadataCreditsAreCountedWithMultiplicity()
    {
        var tracker = new ResetCreditTracker();
        var credit = new QuotaResetCredit("Synthetic", null, "reset", DateTimeOffset.UnixEpoch.AddDays(100));
        tracker.Observe([credit], 1);
        Assert.Equal(1, tracker.Observe([credit, credit], 2).TotalNew);
        Assert.False(tracker.Observe([credit, credit], 2).HasNew);
    }

    [Fact]
    public void MissingCreditFieldDoesNotInventAZeroBaseline()
    {
        var tracker = new ResetCreditTracker();
        tracker.Observe([], 2);
        tracker.Observe([], null);
        Assert.False(tracker.Observe([], 2).HasNew);
    }

    [Fact]
    public void NearFullOscillationDoesNotAlert()
    {
        var tracker = new QuotaReplenishmentTracker();
        Assert.Empty(tracker.Observe(Buckets(94)));
        Assert.Empty(tracker.Observe(Buckets(95)));
        Assert.Empty(tracker.Observe(Buckets(94)));
        Assert.Empty(tracker.Observe(Buckets(95)));
    }

    [Fact]
    public void ResetTimestampDetectsSmallFullRebound()
    {
        var tracker = new QuotaReplenishmentTracker();
        tracker.Observe(Buckets(97, 1));
        Assert.Single(tracker.Observe(Buckets(100, 2)));
        Assert.Empty(tracker.Observe(Buckets(100, 2)));
    }

    [Fact]
    public void LargeReboundAlertsAndNearFullMustRearm()
    {
        var tracker = new QuotaReplenishmentTracker();
        tracker.Observe(Buckets(80));
        Assert.Single(tracker.Observe(Buckets(95)));
        Assert.Empty(tracker.Observe(Buckets(94)));
        Assert.Empty(tracker.Observe(Buckets(99)));
        Assert.Empty(tracker.Observe(Buckets(89)));
        Assert.Single(tracker.Observe(Buckets(97)));
    }

    [Fact]
    public void RoundtripPreservesPendingAndBaselinesOnlyForMatchingAccount()
    {
        var ledger = new NotificationLedger();
        Assert.True(ledger.SelectAccount("account-a"));
        ledger.Credits.Observe([], 2);
        ledger.Quota.Observe(Buckets(50));
        ledger.Enqueue("Synthetic", "Pending until dismissed");
        var saved = JsonSerializer.Deserialize<NotificationState>(JsonSerializer.Serialize(ledger.State))!;
        ledger = new(saved);
        Assert.False(ledger.SelectAccount(null));
        Assert.True(ledger.SelectAccount("account-a"));
        Assert.Single(ledger.State.Pending);
        Assert.False(ledger.Credits.Observe([], 2).HasNew);
        Assert.Single(ledger.Quota.Observe(Buckets(100)));
        ledger.Dismiss();
        Assert.Empty(ledger.State.Pending);
        ledger.Enqueue("Private", "Account A");
        ledger.SelectAccount("account-b");
        Assert.Empty(ledger.State.Pending);
        Assert.False(ledger.Credits.Observe([], 10).HasNew);
        Assert.Empty(ledger.Quota.Observe(Buckets(99)));
    }

    [Fact]
    public void QueueAndCreditHistoryAreBounded()
    {
        var ledger = new NotificationLedger();
        for (var i = 0; i < 100; i++) ledger.Enqueue("Notice " + i, new string('x', 3000));
        Assert.Equal(32, ledger.State.Pending.Count);
        Assert.All(ledger.State.Pending, n => Assert.True(n.Message.Length <= 2048));
        var tracker = new ResetCreditTracker();
        for (var i = 0; i < 2100; i++) tracker.Observe([new("Credit " + i, null, null, null)], 1);
        Assert.Equal(2048, tracker.State.Seen.Count);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"invalid\"")]
    [InlineData("true")]
    [InlineData("99999999999999999999999999")]
    public void InvalidOptionalNumericTypesAreIgnored(string value)
    {
        using var doc = JsonDocument.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":" + value + ",\"resetsAt\":" + value + "}},\"rateLimitResetCredits\":{\"availableCount\":" + value + "}}");
        Assert.True(QuotaParser.TryParse(doc.RootElement, DateTimeOffset.UnixEpoch, out var snapshot));
        Assert.Null(snapshot!.Buckets[0].Primary!.WindowDurationMins);
        Assert.Null(snapshot.Buckets[0].Primary!.ResetsAtUtc);
        Assert.Null(snapshot.AvailableResetCredits);
    }

    [Fact]
    public void AccountIdentityIsOpaqueAndUnavailableIdentityIsNotReusable()
    {
        using var a = JsonDocument.Parse("""{"account":{"type":"chatgpt","email":"synthetic@example.invalid","planType":"pro"}}""");
        using var b = JsonDocument.Parse("""{"account":{"type":"chatgpt","email":"other@example.invalid","planType":"pro"}}""");
        using var empty = JsonDocument.Parse("""{"account":{"type":"apiKey"}}""");
        var scope = AccountIdentity.GetScope(a.RootElement);
        Assert.Equal(64, scope!.Length);
        Assert.DoesNotContain("synthetic", scope);
        Assert.NotEqual(scope, AccountIdentity.GetScope(b.RootElement));
        Assert.Null(AccountIdentity.GetScope(empty.RootElement));
    }

    private static QuotaBucket[] Buckets(decimal remaining, int day = 1) =>
        [new("codex", "Synthetic", null, new(100 - remaining, 300, DateTimeOffset.UnixEpoch.AddDays(day)), null, null, null)];
}
