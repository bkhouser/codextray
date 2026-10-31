using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class ThresholdTrackerTests
{
    [Fact]
    public void NotifiesOnlyWhenCrossingAThreshold()
    {
        var tracker = new ThresholdTracker();

        Assert.Null(tracker.Observe("codex", 65));
        Assert.Equal(70, tracker.Observe("codex", 70));
        Assert.Null(tracker.Observe("codex", 82));
        Assert.Equal(90, tracker.Observe("codex", 90));
        Assert.Null(tracker.Observe("codex", 95));
        Assert.Null(tracker.Observe("codex", 68));
        Assert.Equal(70, tracker.Observe("codex", 71));
    }

    [Fact]
    public void PersistedBandPreventsRepeatAfterRestart()
    {
        var tracker = new ThresholdTracker(new Dictionary<string, int> { ["codex"] = 90 });

        Assert.Null(tracker.Observe("codex", 93));
    }
}
