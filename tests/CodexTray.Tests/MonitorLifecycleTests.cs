using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class MonitorLifecycleTests
{
    [Fact]
    public async Task RestartsAfterUnexpectedExitAndStopsChildOnDispose()
    {
        var factory = new FakeProcessFactory();
        await using var monitor = new CodexQuotaMonitor(
            new FixedLocator(),
            factory,
            pollInterval: TimeSpan.FromHours(1),
            backoffProvider: _ => TimeSpan.FromMilliseconds(10));

        monitor.Start();
        await WaitUntilAsync(() => factory.Processes.Count >= 2, TimeSpan.FromSeconds(5));

        Assert.True(factory.Processes[0].HasExited);
        Assert.False(factory.Processes[1].HasExited);
        Assert.Equal(MonitorConnectionState.Connected, monitor.Status.State);
        Assert.Equal(18m, monitor.Status.Snapshot!.IncludedBucket!.Primary!.UsedPercent);

        await monitor.DisposeAsync();

        Assert.True(factory.Processes[1].StopCalls > 0);
        Assert.True(factory.Processes[1].HasExited);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class FixedLocator : ICodexExecutableLocator
    {
        public CodexDiscoveryResult Find() => new("fake-codex.exe", ["fake-codex.exe"]);
    }

    private sealed class FakeProcessFactory : IAppServerProcessFactory
    {
        private readonly object _gate = new();

        public List<FakeProcess> Processes { get; } = [];

        public IAppServerProcess Start(string executablePath)
        {
            lock (_gate)
            {
                var process = new FakeProcess(exitAfterFirstQuota: Processes.Count == 0);
                Processes.Add(process);
                return process;
            }
        }
    }

    private sealed class FakeProcess : IAppServerProcess
    {
        private readonly LineChannel _clientToServer = new();
        private readonly LineChannel _serverToClient = new();
        private readonly LineChannel _stderr = new();
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _exitAfterFirstQuota;
        private int _stopCalls;

        public FakeProcess(bool exitAfterFirstQuota)
        {
            _exitAfterFirstQuota = exitAfterFirstQuota;
            _ = RunServerAsync();
        }

        public TextReader StandardOutput => _serverToClient.Reader;

        public TextWriter StandardInput => _clientToServer.Writer;

        public TextReader StandardError => _stderr.Reader;

        public bool HasExited => _exit.Task.IsCompleted;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCalls);
            Exit();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Exit();
            return ValueTask.CompletedTask;
        }

        private async Task RunServerAsync()
        {
            try
            {
                while (!HasExited)
                {
                    var line = await _clientToServer.ReadAsync();
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var method = root.GetProperty("method").GetString();
                    if (method == "initialize")
                    {
                        await _serverToClient.WriteAsync(JsonSerializer.Serialize(new
                        {
                            id = root.GetProperty("id").GetInt64(),
                            result = new { }
                        }));
                    }
                    else if (method == "account/read")
                    {
                        await _serverToClient.WriteAsync(JsonSerializer.Serialize(new
                        {
                            id = root.GetProperty("id").GetInt64(),
                            result = new { account = new { type = "chatgpt", email = "synthetic@example.invalid" } }
                        }));
                    }
                    else if (method == "account/rateLimits/read")
                    {
                        await _serverToClient.WriteAsync(JsonSerializer.Serialize(new
                        {
                            id = root.GetProperty("id").GetInt64(),
                            result = new
                            {
                                rateLimits = new
                                {
                                    primary = new { usedPercent = 18, windowDurationMins = 300 }
                                }
                            }
                        }));
                        if (_exitAfterFirstQuota)
                        {
                            await Task.Delay(20);
                            Exit();
                        }
                    }
                }
            }
            catch (Exception) when (HasExited)
            {
            }
        }

        private void Exit()
        {
            if (_exit.TrySetResult())
            {
                _clientToServer.Complete();
                _serverToClient.Complete();
                _stderr.Complete();
            }
        }
    }
}
