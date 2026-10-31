namespace CodexTray.Core;

public sealed record PendingNotice(string Title, string Message);
public sealed class NotificationState
{
    public int Version { get; set; } = 1;
    public string? AccountScope { get; set; }
    public ResetCreditState Credits { get; set; } = new();
    public Dictionary<string, QuotaObservation> Quota { get; set; } = new();
    public Dictionary<string, int> Thresholds { get; set; } = new();
    public List<PendingNotice> Pending { get; set; } = [];
}

public sealed class NotificationLedger
{
    public NotificationState State { get; private set; }
    public ResetCreditTracker Credits { get; private set; } = new();
    public QuotaReplenishmentTracker Quota { get; private set; } = new();
    public ThresholdTracker Thresholds { get; private set; } = new();
    public NotificationLedger(NotificationState? saved = null) => State = saved ?? new();

    public bool SelectAccount(string? scope)
    {
        if (scope is null) return false;
        if (State.AccountScope != scope) State = new() { AccountScope = scope };
        Credits = new(State.Credits);
        Quota = new(State.Quota);
        Thresholds = new(State.Thresholds);
        return true;
    }

    public void Enqueue(string title, string message)
    {
        var notice = new PendingNotice(title[..Math.Min(128, title.Length)], message[..Math.Min(2048, message.Length)]);
        if (State.Pending.Contains(notice)) return;
        if (State.Pending.Count < 32) State.Pending.Add(notice);
        else State.Pending[^1] = new("More quota updates", "Additional quota updates occurred. Open CodexTray for the current balances.");
    }

    public void Dismiss()
    {
        if (State.Pending.Count > 0) State.Pending.RemoveAt(0);
    }
}
