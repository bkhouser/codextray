using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class ResetCreditTrackerTests
{
    [Fact]
    public void FirstObservationEstablishesBaselineWithoutNotification()
    {
        var tracker = new ResetCreditTracker();

        var detection = tracker.Observe([Credit("First", "2026-09-30T00:00:00Z")], 1);

        Assert.False(detection.HasNew);
    }

    [Fact]
    public void DetectsNewCreditWhenCountDoesNotChange()
    {
        var tracker = new ResetCreditTracker();
        var first = Credit("First", "2026-09-30T00:00:00Z");
        var replacement = Credit("Replacement", "2026-10-15T00:00:00Z");
        tracker.Observe([first], 1);

        var detection = tracker.Observe([replacement], 1);

        Assert.True(detection.HasNew);
        Assert.Equal(replacement, Assert.Single(detection.NewCredits));
        Assert.Equal(1, detection.TotalNew);
    }

    [Fact]
    public void DoesNotRepeatNotificationForPreviouslySeenCredit()
    {
        var tracker = new ResetCreditTracker();
        var credit = Credit("First", "2026-09-30T00:00:00Z");
        tracker.Observe([credit], 1);
        tracker.Observe([], 0);

        var detection = tracker.Observe([credit], 1);

        Assert.False(detection.HasNew);
    }

    [Fact]
    public void DetectsCountIncreaseWhenCreditDetailsAreUnavailable()
    {
        var tracker = new ResetCreditTracker();
        tracker.Observe([], 2);

        var detection = tracker.Observe([], 4);

        Assert.True(detection.HasNew);
        Assert.Equal(2, detection.AdditionalUnspecifiedCredits);
        Assert.Equal(2, detection.TotalNew);
    }

    private static QuotaResetCredit Credit(string title, string expiry) => new(
        title,
        Description: null,
        ResetType: "codexRateLimits",
        ExpiresAtUtc: DateTimeOffset.Parse(expiry));
}
