namespace CodexTray.Core;

public sealed record QuotaReplenishment(string BucketId, string BucketDisplayName, string WindowName,
    int? WindowDurationMins, decimal RemainingPercent);
public sealed record QuotaObservation(decimal Remaining, DateTimeOffset? ResetsAt, bool NearFullArmed);

public sealed class QuotaReplenishmentTracker
{
    public Dictionary<string, QuotaObservation> State { get; }
    public QuotaReplenishmentTracker(Dictionary<string, QuotaObservation>? state = null) =>
        State = state ?? new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<QuotaReplenishment> Observe(IReadOnlyList<QuotaBucket> buckets)
    {
        var events = new List<QuotaReplenishment>();
        foreach (var bucket in buckets.Take(64))
        {
            ObserveWindow(bucket, "primary", bucket.Primary, events);
            ObserveWindow(bucket, "secondary", bucket.Secondary, events);
        }
        return events;
    }

    private void ObserveWindow(QuotaBucket bucket, string name, QuotaWindow? window, List<QuotaReplenishment> events)
    {
        if (window is null) return;
        var key = bucket.Id + "\u001f" + name;
        var remaining = window.RemainingPercent;
        var armed = remaining <= 90m;
        if (State.TryGetValue(key, out var previous))
        {
            armed |= previous.NearFullArmed;
            var increase = remaining - previous.Remaining;
            var newWindow = previous.ResetsAt is { } oldReset && window.ResetsAtUtc is { } nextReset && nextReset > oldReset;
            var detected = increase >= 15m
                || (armed && remaining >= 95m && increase >= 3m)
                || (newWindow && remaining >= 95m && increase > 0);
            if (detected)
            {
                events.Add(new(bucket.Id, bucket.DisplayName, name, window.WindowDurationMins, remaining));
                armed = false;
            }
        }
        if (State.Count >= 128 && !State.ContainsKey(key)) State.Remove(State.Keys.First());
        State[key] = new(remaining, window.ResetsAtUtc, armed);
    }
}
