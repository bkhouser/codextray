using System.Security.Cryptography;
using System.Text;

namespace CodexTray.Core;

public sealed record ResetCreditDetection(IReadOnlyList<QuotaResetCredit> NewCredits, int AdditionalUnspecifiedCredits)
{
    public int TotalNew => NewCredits.Count + AdditionalUnspecifiedCredits;
    public bool HasNew => TotalNew > 0;
}

public sealed class ResetCreditState
{
    public Dictionary<string, int> Seen { get; set; } = new();
    public int? LastAvailableCount { get; set; }
    public int Unidentified { get; set; }
}

public sealed class ResetCreditTracker
{
    public ResetCreditState State { get; }
    public ResetCreditTracker(ResetCreditState? state = null) => State = state ?? new();

    public ResetCreditDetection Observe(IReadOnlyList<QuotaResetCredit> credits, int? availableCount)
    {
        // Unknown is not zero. Keep the previous observation when the entire field is absent.
        if (availableCount is null && credits.Count == 0) return new([], 0);
        var count = Math.Clamp(availableCount ?? credits.Count, 0, 10000);
        var initialized = State.LastAvailableCount.HasValue;
        var unidentified = Math.Min(State.Unidentified, count);
        var seenThisSample = new Dictionary<string, int>();
        var additions = new List<QuotaResetCredit>();
        foreach (var credit in credits.Take(256))
        {
            var key = Identity(credit);
            var ordinal = seenThisSample.GetValueOrDefault(key) + 1;
            seenThisSample[key] = ordinal;
            if (ordinal > State.Seen.GetValueOrDefault(key))
            {
                if (initialized)
                {
                    if (unidentified > 0) unidentified--;
                    else additions.Add(credit);
                }
                State.Seen[key] = ordinal;
            }
        }
        // Bound history. Recent identities retain multiplicity, including temporary disappearance.
        while (State.Seen.Count > 2048) State.Seen.Remove(State.Seen.Keys.First());
        var increase = Math.Max(0, count - (State.LastAvailableCount ?? count));
        var unspecified = Math.Min(Math.Max(0, increase - additions.Count), Math.Max(0, count - credits.Count));
        State.LastAvailableCount = count;
        var missingDetails = Math.Max(0, count - credits.Count);
        // Missing metadata is not a new count-only baseline. Only the first observation
        // and newly detected unspecified credits introduce unidentified slots.
        State.Unidentified = initialized ? Math.Min(missingDetails, unidentified + unspecified) : missingDetails;
        return new(additions, initialized ? unspecified : 0);
    }

    private static string Identity(QuotaResetCredit credit) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\u001f", credit.ResetType, credit.Title,
            credit.ExpiresAtUtc?.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)))));
}
