using System.Collections.Concurrent;
using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class MonitorRecoveryTests
{
    [Theory]
    [InlineData(FailureMode.StdoutEof)]
    [InlineData(FailureMode.OversizedStdout)]
    [InlineData(FailureMode.OversizedStderr)]
    [InlineData(FailureMode.StalledQuota)]
    public async Task ReconnectsWhenTransportFailsWithoutProcessExit(FailureMode mode)
    {
        var factory = new RecoveryProcessFactory(mode);
        await using var monitor = new CodexQuotaMonitor(new FixedLocator(), factory,
            pollInterval: TimeSpan.FromHours(1), backoffProvider: _ => TimeSpan.FromMilliseconds(10));
        monitor.Start();
        await WaitUntilAsync(() => factory.Processes.Length >= 2 && monitor.Status.Snapshot is not null,
            TimeSpan.FromSeconds(15));
        Assert.True(factory.Processes[0].Stopped);
        Assert.False(factory.Processes[1].Stopped);
        Assert.Equal(MonitorConnectionState.Connected, monitor.Status.State);
        Assert.NotNull(monitor.Status.Snapshot!.AccountScope);
        if (mode == FailureMode.OversizedStderr)
        {
            Assert.InRange(factory.Processes[0].EndlessStderr.CharactersRead,
                CodexQuotaMonitor.MaximumStandardErrorLineLength + 1,
                CodexQuotaMonitor.MaximumStandardErrorLineLength + 4096);
        }
    }

    [Fact]
    public async Task AccountChangeClearsOldSnapshotAndRejectsInFlightQuota()
    {
        var factory = new RecoveryProcessFactory(FailureMode.None);
        await using var monitor = new CodexQuotaMonitor(new FixedLocator(), factory,
            pollInterval: TimeSpan.FromHours(1));
        monitor.Start();
        await WaitUntilAsync(() => monitor.Status.Snapshot is not null, TimeSpan.FromSeconds(3));
        var originalScope = monitor.Status.Snapshot!.AccountScope;
        var process = factory.Processes[0];
        process.HoldQuota = true;
        var refresh = monitor.RefreshNowAsync(TestContext.Current.CancellationToken);
        await process.QuotaHeld.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await process.ChangeAccountAsync();
        await WaitUntilAsync(() => monitor.Status.Snapshot is null, TimeSpan.FromSeconds(3));
        Assert.Equal(MonitorConnectionState.Connecting, monitor.Status.State);
        process.ReleaseQuota.TrySetResult();
        await refresh;
        await WaitUntilAsync(() => monitor.Status.Snapshot?.AccountScope is { } scope && scope != originalScope,
            TimeSpan.FromSeconds(3));
        Assert.Equal(7m, monitor.Status.Snapshot!.IncludedBucket!.Primary!.UsedPercent);
    }

    [Fact]
    public async Task LegacyAccountReadMethodStillDisplaysQuotaWithoutAlertIdentity()
    {
        var factory = new RecoveryProcessFactory(FailureMode.LegacyAccount);
        await using var monitor = new CodexQuotaMonitor(new FixedLocator(), factory);
        monitor.Start();
        await WaitUntilAsync(() => monitor.Status.Snapshot is not null, TimeSpan.FromSeconds(3));
        Assert.Equal(MonitorConnectionState.Connected, monitor.Status.State);
        Assert.Null(monitor.Status.Snapshot!.AccountScope);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    public enum FailureMode { None, StdoutEof, OversizedStdout, OversizedStderr, StalledQuota, LegacyAccount }

    private sealed class FixedLocator : ICodexExecutableLocator
    {
        public CodexDiscoveryResult Find() => new("fake-codex.exe", ["fake-codex.exe"]);
    }

    private sealed class RecoveryProcessFactory(FailureMode initialMode) : IAppServerProcessFactory
    {
        private readonly ConcurrentQueue<RecoveryProcess> _processes = new();
        public RecoveryProcess[] Processes => _processes.ToArray();
        public IAppServerProcess Start(string executablePath)
        {
            var process = new RecoveryProcess(_processes.IsEmpty ? initialMode : FailureMode.None);
            _processes.Enqueue(process);
            return process;
        }
    }

    private sealed class RecoveryProcess : IAppServerProcess
    {
        private readonly LineChannel _input = new();
        private readonly LineChannel _output = new();
        private readonly LineChannel _stderr = new();
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly FailureMode _mode;
        private string _email = "original@example.invalid";
        private int _quotaCount;
        public RecoveryProcess(FailureMode mode)
        {
            _mode = mode;
            _ = RunAsync();
        }
        public bool HoldQuota { get; set; }
        public TaskCompletionSource QuotaHeld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseQuota { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProtocolHardeningTests.EndlessReader EndlessStderr { get; } = new();
        public TextReader StandardOutput => _output.Reader;
        public TextWriter StandardInput => _input.Writer;
        public TextReader StandardError => _mode == FailureMode.OversizedStderr ? EndlessStderr : _stderr.Reader;
        public bool HasExited => _exit.Task.IsCompleted;
        public bool Stopped { get; private set; }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            _exit.TrySetResult();
            ReleaseQuota.TrySetResult();
            _input.Complete();
            _output.Complete();
            _stderr.Complete();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
        public Task ChangeAccountAsync()
        {
            _email = "changed@example.invalid";
            return _output.WriteAsync("""{"method":"account/updated","params":{"authMode":"chatgpt"}}""").AsTask();
        }
        private async Task RunAsync()
        {
            try
            {
                while (!HasExited)
                {
                    using var request = JsonDocument.Parse(await _input.ReadAsync());
                    var root = request.RootElement;
                    if (!root.TryGetProperty("id", out var id))
                    {
                        continue;
                    }
                    var requestId = id.GetInt64();
                    var method = root.GetProperty("method").GetString();
                    object result = new { };
                    if (method == "account/read")
                    {
                        Assert.False(root.GetProperty("params").GetProperty("refreshToken").GetBoolean());
                        if (_mode == FailureMode.LegacyAccount)
                        {
                            await _output.WriteAsync(JsonSerializer.Serialize(new { id = requestId, error = new { code = -32601, message = "Method not found" } }));
                            continue;
                        }
                        result = new { account = new { type = "chatgpt", email = _email } };
                    }
                    else if (method == "account/rateLimits/read")
                    {
                        _quotaCount++;
                        if (_mode == FailureMode.StalledQuota)
                        {
                            continue;
                        }
                        if (_mode == FailureMode.StdoutEof)
                        {
                            _output.Complete();
                            continue;
                        }
                        if (_mode == FailureMode.OversizedStdout)
                        {
                            await _output.WriteAsync(new string('x', AppServerProtocolSession.MaximumProtocolLineLength + 1));
                            continue;
                        }
                        var usage = _email.StartsWith("original", StringComparison.Ordinal) ? 18 : 7;
                        if (HoldQuota && _quotaCount == 2)
                        {
                            QuotaHeld.TrySetResult();
                            await ReleaseQuota.Task;
                        }
                        result = new { rateLimits = new { primary = new { usedPercent = usage, windowDurationMins = 300 } } };
                    }
                    await _output.WriteAsync(JsonSerializer.Serialize(new { id = requestId, result }));
                }
            }
            catch (Exception) when (HasExited)
            {
            }
        }
    }
}
