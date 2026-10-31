using System.Diagnostics;
using System.Text.Json;

namespace CodexTray.Core;

public sealed class CodexQuotaMonitor : IAsyncDisposable
{
    public const int MaximumStandardErrorLineLength = 16 * 1024;
    private readonly ICodexExecutableLocator _locator;
    private readonly IAppServerProcessFactory _processFactory;
    private readonly ISanitizedLogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly Func<int, TimeSpan> _backoffProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _accountGate = new();
    private long _accountGeneration;
    private int _refreshRequested;
    private AppServerProtocolSession? _session;
    private Task? _runTask;
    private MonitorStatus _status = new(MonitorConnectionState.Connecting, "Starting Codex App Server…");
    private int _started;
    private int _disposed;

    public CodexQuotaMonitor(
        ICodexExecutableLocator? locator = null,
        IAppServerProcessFactory? processFactory = null,
        ISanitizedLogger? logger = null,
        TimeSpan? pollInterval = null,
        Func<int, TimeSpan>? backoffProvider = null)
    {
        _locator = locator ?? new CodexExecutableLocator();
        _processFactory = processFactory ?? new AppServerProcessFactory();
        _logger = logger ?? NullSanitizedLogger.Instance;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(60);
        _backoffProvider = backoffProvider ?? (attempt => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)))));
    }

    public event Action<MonitorStatus>? StatusChanged;

    public MonitorStatus Status => _status;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _runTask = RunAsync(_lifetime.Token);
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        var session = Volatile.Read(ref _session);
        if (session is null)
        {
            Publish(_status with { Message = "Codex App Server is reconnecting…" });
            return;
        }

        await RefreshAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IAppServerProcess? process = null;
            AppServerProtocolSession? session = null;
            Task? stderrTask = null;
            var connectedDuration = Stopwatch.StartNew();
            try
            {
                Publish(new MonitorStatus(MonitorConnectionState.Connecting, "Connecting to Codex App Server…", null, LastSuccessfulUpdate: _status.LastSuccessfulUpdate));
                var discovery = _locator.Find();
                if (!discovery.Found || discovery.ExecutablePath is null)
                {
                    Publish(new MonitorStatus(MonitorConnectionState.Offline, discovery.ToActionableMessage(), _status.Snapshot, LastSuccessfulUpdate: _status.LastSuccessfulUpdate));
                    _logger.Warning("codex_not_found", string.Join("; ", discovery.SearchedLocations));
                    throw new FileNotFoundException("Codex executable was not found.");
                }

                process = _processFactory.Start(discovery.ExecutablePath);
                stderrTask = DrainStandardErrorAsync(process.StandardError, connectionLifetime.Token);
                session = new AppServerProtocolSession(process.StandardOutput, process.StandardInput, leaveOpen: true);
                session.NotificationReceived += OnNotification;
                long lastProtocolDiagnostic = 0;
                session.DiagnosticReceived += diagnostic =>
                {
                    if (TryAcquireDiagnosticSlot(ref lastProtocolDiagnostic))
                    {
                        _logger.Warning("protocol_line_ignored", $"kind={diagnostic.Kind};length={diagnostic.LineLength}");
                    }
                };

                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _session, session);
                Publish(new MonitorStatus(
                    MonitorConnectionState.Connected,
                    "Connected. Reading quota…",
                    null,
                    discovery.ExecutablePath,
                    _status.LastSuccessfulUpdate));
                _logger.Info("app_server_connected");
                await RefreshAsync(session, cancellationToken).ConfigureAwait(false);
                var exitTask = process.WaitForExitAsync(connectionLifetime.Token);
                var stderrWatch = stderrTask;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var delayTask = Task.Delay(_pollInterval, connectionLifetime.Token);
                    var completed = await Task.WhenAny(delayTask, exitTask, session.Completion, stderrWatch).ConfigureAwait(false);
                    if (completed == session.Completion)
                    {
                        await session.Completion.ConfigureAwait(false);
                    }

                    if (completed == stderrWatch)
                    {
                        await stderrWatch.ConfigureAwait(false);
                        // Closing stderr alone is legal; do not repeatedly select its completed task.
                        stderrWatch = Task.Delay(Timeout.InfiniteTimeSpan, connectionLifetime.Token);
                        continue;
                    }
                    if (completed == exitTask)
                    {
                        await exitTask.ConfigureAwait(false);
                        throw new EndOfStreamException("Codex App Server exited unexpectedly.");
                    }

                    await delayTask.ConfigureAwait(false);
                    await RefreshAsync(session, cancellationToken).ConfigureAwait(false);
                    if (connectedDuration.Elapsed >= TimeSpan.FromMinutes(1))
                    {
                        attempt = 0;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (_status.State != MonitorConnectionState.Offline)
                {
                    Publish(new MonitorStatus(
                        MonitorConnectionState.Error,
                        "Codex App Server is unavailable. CodexTray will retry automatically.",
                        _status.Snapshot,
                        _status.CodexPath,
                        _status.LastSuccessfulUpdate));
                }

                _logger.Error("app_server_connection_failed", exception);
            }
            finally
            {
                connectionLifetime.Cancel();
                Volatile.Write(ref _session, null);
                if (session is not null)
                {
                    session.NotificationReceived -= OnNotification;
                    await session.DisposeAsync().ConfigureAwait(false);
                }

                if (process is not null)
                {
                    try
                    {
                        await process.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        _logger.Error("app_server_stop_failed", exception);
                    }

                    await process.DisposeAsync().ConfigureAwait(false);
                }

                if (stderrTask is not null)
                {
                    try
                    {
                        await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Drain errors are already logged. Observe cancellation/faults during teardown.
                        _ = stderrTask.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var delay = _backoffProvider(attempt++);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task RefreshAsync(AppServerProtocolSession session, CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Exchange(ref _refreshRequested, 1);
            return;
        }

        var generation = Interlocked.Read(ref _accountGeneration);
        try
        {
            var accountScope = await ReadAccountScopeAsync(session, cancellationToken).ConfigureAwait(false);
            var payload = await session.SendRequestAsync(
                "account/rateLimits/read",
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var confirmedScope = await ReadAccountScopeAsync(session, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(accountScope, confirmedScope, StringComparison.Ordinal)
                || generation != Interlocked.Read(ref _accountGeneration))
            {
                PublishForAccountGeneration(generation, session, new MonitorStatus(
                    MonitorConnectionState.Connecting, "Account changed. Reading quota…", null,
                    _status.CodexPath, _status.LastSuccessfulUpdate));
                Interlocked.Exchange(ref _refreshRequested, 1);
                return;
            }

            var now = DateTimeOffset.Now;
            if (QuotaParser.TryParse(payload, now, out var snapshot) && snapshot is not null)
            {
                snapshot = snapshot with { AccountScope = accountScope };
                PublishForAccountGeneration(generation, session, new MonitorStatus(
                    MonitorConnectionState.Connected,
                    "Quota is up to date.",
                    snapshot,
                    _status.CodexPath,
                    now));
                _logger.Info("quota_refresh_succeeded");
            }
            else
            {
                PublishForAccountGeneration(generation, session, new MonitorStatus(
                    MonitorConnectionState.SignedOut,
                    "Sign in to Codex to view quota.",
                    null,
                    _status.CodexPath,
                    _status.LastSuccessfulUpdate));
                _logger.Warning("quota_payload_unavailable");
            }
        }
        catch (AppServerRpcException exception) when (LooksLikeSignedOut(exception.Message))
        {
            PublishForAccountGeneration(generation, session, new MonitorStatus(
                MonitorConnectionState.SignedOut,
                "Sign in to Codex to view quota.",
                null,
                _status.CodexPath,
                _status.LastSuccessfulUpdate));
            _logger.Warning("account_signed_out");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PublishForAccountGeneration(generation, session, new MonitorStatus(
                MonitorConnectionState.Error,
                "Quota refresh failed. CodexTray will try again.",
                _status.Snapshot,
                _status.CodexPath,
                _status.LastSuccessfulUpdate));
            _logger.Error("quota_refresh_failed", exception);
        }
        finally
        {
            _refreshLock.Release();
            if (Interlocked.Exchange(ref _refreshRequested, 0) != 0
                && Volatile.Read(ref _disposed) == 0
                && _status.State != MonitorConnectionState.SignedOut
                && ReferenceEquals(session, Volatile.Read(ref _session))
                && !session.Completion.IsCompleted)
            {
                _ = RefreshFromNotificationAsync();
            }
        }
    }

    private static async Task<string?> ReadAccountScopeAsync(AppServerProtocolSession session, CancellationToken cancellationToken)
    {
        try
        {
            var account = await session.SendRequestAsync("account/read", new { refreshToken = false },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (account.ValueKind == JsonValueKind.Object
                && account.TryGetProperty("account", out var identity)
                && identity.ValueKind == JsonValueKind.Null)
            {
                throw new AppServerRpcException(null, "Sign in to Codex to view quota.");
            }

            return AccountIdentity.GetScope(account);
        }
        catch (AppServerRpcException exception) when (exception.Code == -32601)
        {
            // Older App Servers can still display quotas, but have no durable alert identity.
            return null;
        }
    }

    private void PublishForAccountGeneration(long generation, AppServerProtocolSession session, MonitorStatus status)
    {
        lock (_accountGate)
        {
            if (generation == _accountGeneration
                && ReferenceEquals(session, Volatile.Read(ref _session))
                && !session.Completion.IsCompleted)
            {
                Publish(status);
            }
        }
    }

    private void OnNotification(string method, JsonElement? payload)
    {
        if (string.Equals(method, "account/rateLimits/updated", StringComparison.Ordinal))
        {
            _ = RefreshFromNotificationAsync();
            return;
        }

        if (string.Equals(method, "account/updated", StringComparison.Ordinal))
        {
            var signedOut = payload is { ValueKind: JsonValueKind.Object } value
                && value.TryGetProperty("authMode", out var authMode)
                && authMode.ValueKind == JsonValueKind.Null;
            lock (_accountGate)
            {
                Interlocked.Increment(ref _accountGeneration);
                Publish(new MonitorStatus(
                    signedOut ? MonitorConnectionState.SignedOut : MonitorConnectionState.Connecting,
                    signedOut ? "Sign in to Codex to view quota." : "Account changed. Reading quota…",
                    null,
                    _status.CodexPath,
                    _status.LastSuccessfulUpdate));
            }

            if (!signedOut)
            {
                _ = RefreshFromNotificationAsync();
            }
        }
    }

    private async Task RefreshFromNotificationAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            await RefreshNowAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task DrainStandardErrorAsync(TextReader reader, CancellationToken cancellationToken)
    {
        try
        {
            var boundedReader = new BoundedLineReader(reader, MaximumStandardErrorLineLength);
            long lastDiagnostic = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await boundedReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                if (TryAcquireDiagnosticSlot(ref lastDiagnostic))
                {
                    _logger.Warning("app_server_stderr", $"length={line.Length}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("app_server_stderr_read_failed", exception);
            throw;
        }
    }

    private static bool TryAcquireDiagnosticSlot(ref long lastTimestamp)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref lastTimestamp);
        return (previous == 0 || Stopwatch.GetElapsedTime(previous, now) >= TimeSpan.FromSeconds(1))
            && Interlocked.CompareExchange(ref lastTimestamp, now, previous) == previous;
    }

    private void Publish(MonitorStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
    }

    private static bool LooksLikeSignedOut(string message) =>
        message.Contains("sign in", StringComparison.OrdinalIgnoreCase)
        || message.Contains("login", StringComparison.OrdinalIgnoreCase)
        || message.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
        || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime.Dispose();
        // In-flight notification refreshes can still release this managed-only semaphore.
    }
}
