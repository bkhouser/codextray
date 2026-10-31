namespace CodexTray.Core;

public sealed record ApiBillingSettings(
    string OrganizationId,
    decimal Allowance,
    DateTimeOffset CycleStartUtc,
    DateTimeOffset CycleEndUtc)
{
    public bool IsCurrentAt(DateTimeOffset now) => now >= CycleStartUtc && now < CycleEndUtc;

    public void Validate()
    {
        if (string.IsNullOrEmpty(OrganizationId) || OrganizationId.Length > 128
            || !OrganizationId.StartsWith("org-", StringComparison.Ordinal)
            || OrganizationId.Length <= 4 || !OrganizationId.All(IsIdentifierCharacter))
            throw new ArgumentException("Enter a valid organization ID (org-…).");
        if (Allowance <= 0)
            throw new ArgumentException("Enter a positive confirmed USD allowance.");
        if (CycleStartUtc.Offset != TimeSpan.Zero || CycleEndUtc.Offset != TimeSpan.Zero
            || CycleStartUtc.ToUnixTimeSeconds() < 0 || CycleEndUtc <= CycleStartUtc)
            throw new ArgumentException("Enter a valid cycle start and end in UTC.");
    }

    public void Validate(DateTimeOffset now)
    {
        Validate();
        if (now >= CycleEndUtc)
            throw new ApiBillingException("The API credit cycle expired. Update the cycle after confirming the actual grant.");
        if (now < CycleStartUtc || now.ToUnixTimeSeconds() <= CycleStartUtc.ToUnixTimeSeconds())
            throw new ApiBillingException("The API credit cycle has not started yet.");
    }

    internal static bool IsIdentifierCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}

/// <summary>An estimate from organization-wide reported costs, not a verified grant balance.</summary>
public sealed record ApiBillingSnapshot(ApiBillingSettings Settings, decimal Spent, DateTimeOffset UpdatedAt)
{
    public decimal Remaining => Spent <= 0 ? Settings.Allowance : Spent >= Settings.Allowance ? 0 : Settings.Allowance - Spent;
    public decimal RemainingPercent => Settings.Allowance > 0 ? Math.Clamp(Remaining / Settings.Allowance * 100m, 0m, 100m) : 0;
}

public sealed class ApiBillingException(string message) : Exception(message);
