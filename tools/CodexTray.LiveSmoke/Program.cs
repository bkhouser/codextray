using System.Text.Json;
using CodexTray.Core;

var discovery = new CodexExecutableLocator().Find();
if (!discovery.Found || discovery.ExecutablePath is null)
{
    Console.Error.WriteLine(discovery.ToActionableMessage());
    return 1;
}

await using var process = new AppServerProcessFactory().Start(discovery.ExecutablePath);
await using var session = new AppServerProtocolSession(process.StandardOutput, process.StandardInput, leaveOpen: true);
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await session.StartAsync(timeout.Token);
    var payload = await session.SendRequestAsync("account/rateLimits/read", cancellationToken: timeout.Token);
    if (!QuotaParser.TryParse(payload, DateTimeOffset.Now, out var snapshot) || snapshot is null)
    {
        Console.Error.WriteLine("The App Server returned no quota data. Sign in to Codex and try again.");
        return 2;
    }

    var safeOutput = new
    {
        status = "OK",
        codexPath = discovery.ExecutablePath,
        buckets = snapshot.Buckets.Select(bucket => new
        {
            bucket.Id,
            bucket.DisplayName,
            bucket.PlanType,
            primary = SafeWindow(bucket.Primary),
            secondary = SafeWindow(bucket.Secondary)
        }),
        credits = snapshot.Credits is null ? null : new
        {
            snapshot.Credits.HasCredits,
            snapshot.Credits.Unlimited,
            snapshot.Credits.Balance
        },
        snapshot.AvailableResetCredits,
        resetCredits = snapshot.ResetCredits.Select(reset => new
        {
            reset.Title,
            reset.ResetType,
            reset.ExpiresAtUtc
        }),
        snapshot.UpdatedAt
    };
    Console.WriteLine(JsonSerializer.Serialize(safeOutput, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (AppServerRpcException exception)
{
    Console.Error.WriteLine($"App Server request failed (code {exception.Code?.ToString() ?? "unknown"}).");
    return 3;
}
finally
{
    await process.StopAsync(CancellationToken.None);
}

static object? SafeWindow(QuotaWindow? window) => window is null
    ? null
    : new
    {
        window.UsedPercent,
        window.RemainingPercent,
        window.WindowDurationMins,
        window.ResetsAtUtc,
        localReset = window.ResetsAtUtc?.ToLocalTime()
    };
