namespace CodexTray.Core;

public sealed record QuotaWindow(
    decimal UsedPercent,
    int? WindowDurationMins,
    DateTimeOffset? ResetsAtUtc)
{
    public decimal RemainingPercent => Math.Clamp(100m - UsedPercent, 0m, 100m);
}

public sealed record CreditBalance(
    bool? HasCredits,
    bool? Unlimited,
    decimal? Balance,
    string? OriginalBalance);

public sealed record QuotaResetCredit(
    string Title,
    string? Description,
    string? ResetType,
    DateTimeOffset? ExpiresAtUtc);

public sealed record QuotaBucket(
    string Id,
    string DisplayName,
    string? PlanType,
    QuotaWindow? Primary,
    QuotaWindow? Secondary,
    CreditBalance? Credits,
    string? RateLimitReachedType);

public sealed record QuotaSnapshot(
    IReadOnlyList<QuotaBucket> Buckets,
    CreditBalance? Credits,
    int? AvailableResetCredits,
    IReadOnlyList<QuotaResetCredit> ResetCredits,
    DateTimeOffset UpdatedAt,
    string? AccountScope = null)
{
    public QuotaBucket? IncludedBucket =>
        Buckets.FirstOrDefault(bucket => string.Equals(bucket.Id, "codex", StringComparison.OrdinalIgnoreCase))
        ?? Buckets.FirstOrDefault();
}

public enum MonitorConnectionState
{
    Connecting,
    Connected,
    Offline,
    SignedOut,
    Error
}

public sealed record MonitorStatus(
    MonitorConnectionState State,
    string Message,
    QuotaSnapshot? Snapshot = null,
    string? CodexPath = null,
    DateTimeOffset? LastSuccessfulUpdate = null);
