using System.Collections.Concurrent;
using System.Text.Json;

namespace CodexTray.Core;

public sealed class AppServerRpcException(int? code, string message) : Exception(message)
{
    public int? Code { get; } = code;
}

public sealed record ProtocolDiagnostic(ProtocolLineKind Kind, int LineLength);

public sealed class AppServerProtocolSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    public const int MaximumProtocolLineLength = 1024 * 1024;
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly bool _leaveOpen;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<ParsedProtocolLine>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _terminalError;
    private Task? _readerTask;
    private long _nextId;
    private int _started;
    private int _disposed;

    public AppServerProtocolSession(TextReader reader, TextWriter writer, bool leaveOpen = false)
    {
        _reader = reader;
        _writer = writer;
        _leaveOpen = leaveOpen;
    }

    public event Action<string, JsonElement?>? NotificationReceived;

    public event Action<ProtocolDiagnostic>? DiagnosticReceived;

    /// <summary>Faults when the transport ends or a request times out, even if the child is still alive.</summary>
    public Task Completion => _completion.Task;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The App Server session has already been started.");
        }

        try
        {
            _readerTask = ReadLoopAsync(_lifetime.Token);
            await SendRequestWithIdAsync(
                0,
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "codextray",
                        title = "CodexTray",
                        version = typeof(AppServerProtocolSession).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
                    }
                },
                DefaultRequestTimeout,
                cancellationToken).ConfigureAwait(false);
            await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Unlike ordinary requests, initialization cannot be abandoned while keeping
            // the transport usable: the peer and client may disagree on handshake state.
            EndSession(exception);
            if (_readerTask is { } readerTask)
            {
                try
                {
                    await readerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception) when (_lifetime.IsCancellationRequested)
                {
                    // Keep cleanup bounded if an injected reader ignores cancellation.
                }
            }
            throw;
        }
    }

    public Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        return SendRequestWithIdAsync(id, method, parameters, timeout ?? DefaultRequestTimeout, cancellationToken);
    }

    public async Task SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        using var deadline = new CancellationTokenSource(DefaultRequestTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, deadline.Token);
        var message = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["params"] = parameters ?? new { }
        };
        var writeStarted = false;
        var writeCompleted = false;
        try
        {
            ThrowIfEnded();
            await WriteMessageAsync(message, operation.Token, () => writeStarted = true).ConfigureAwait(false);
            writeCompleted = true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var failure = new TimeoutException("Codex App Server notification timed out.");
            EndSession(failure);
            throw failure;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation before writing is caller-scoped. Once a write has begun, an
            // incomplete JSONL frame may have reached the pipe, so the transport is unsafe.
            if (writeStarted && !writeCompleted) EndSession(exception);
            throw;
        }
        catch (Exception exception)
        {
            EndSession(exception);
            throw;
        }
    }

    private async Task<JsonElement> SendRequestWithIdAsync(
        long id,
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfEnded();
        using var deadline = new CancellationTokenSource(timeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, deadline.Token);
        var completion = new TaskCompletionSource<ParsedProtocolLine>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Request ID {id} is already active.");
        }

        var writeStarted = false;
        var writeCompleted = false;
        try
        {
            // Close the race with EOF between the first check and registration.
            ThrowIfEnded();
            var message = new Dictionary<string, object?>
            {
                ["method"] = method,
                ["id"] = id
            };
            if (parameters is not null)
            {
                message["params"] = parameters;
            }

            await WriteMessageAsync(message, operation.Token, () => writeStarted = true).ConfigureAwait(false);
            writeCompleted = true;
            var response = await completion.Task.WaitAsync(operation.Token).ConfigureAwait(false);
            if (response.Error is { } error)
            {
                throw new AppServerRpcException(error.Code, error.Message);
            }

            return response.Payload ?? EmptyObject();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var failure = new TimeoutException("Codex App Server request timed out.");
            EndSession(failure);
            throw failure;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // A fully written request can be abandoned safely; its late response is ignored.
            if (writeStarted && !writeCompleted) EndSession(exception);
            throw;
        }
        catch (Exception exception) when (exception is not AppServerRpcException)
        {
            EndSession(exception);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken, Action writeStarted)
    {
        var json = JsonSerializer.Serialize(message);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            writeStarted();
            await AwaitWriteAsync(_writer.WriteLineAsync(json.AsMemory(), cancellationToken), cancellationToken).ConfigureAwait(false);
            await AwaitWriteAsync(_writer.FlushAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task AwaitWriteAsync(Task write, CancellationToken cancellationToken)
    {
        try
        {
            // A TextWriter may ignore cancellation. The session must still have a finite deadline.
            await write.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ = write.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? terminalError = null;
        try
        {
            var reader = new BoundedLineReader(_reader, MaximumProtocolLineLength);
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    terminalError = new EndOfStreamException("Codex App Server closed its output stream.");
                    break;
                }

                var parsed = AppServerProtocol.ParseLine(line);
                switch (parsed.Kind)
                {
                    case ProtocolLineKind.Response when parsed.Id is { } id:
                        if (_pending.TryGetValue(id, out var completion))
                        {
                            completion.TrySetResult(parsed);
                        }
                        else
                        {
                            DiagnosticReceived?.Invoke(new ProtocolDiagnostic(ProtocolLineKind.Unrelated, line.Length));
                        }

                        break;
                    case ProtocolLineKind.Notification when parsed.Method is { } method:
                        NotificationReceived?.Invoke(method, parsed.Payload);
                        break;
                    default:
                        DiagnosticReceived?.Invoke(new ProtocolDiagnostic(parsed.Kind, line.Length));
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            terminalError = exception;
        }
        finally
        {
            EndSession(terminalError ?? new OperationCanceledException("Codex App Server session ended."));
        }
    }

    private void ThrowIfEnded()
    {
        if (Volatile.Read(ref _terminalError) is { } failure)
        {
            throw new IOException("Codex App Server session is no longer available.", failure);
        }
    }

    private void EndSession(Exception failure)
    {
        if (Interlocked.CompareExchange(ref _terminalError, failure, null) is not null)
        {
            return;
        }

        _completion.TrySetException(failure);
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(failure);
        }

        _lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        EndSession(new OperationCanceledException("Codex App Server session disposed."));
        if (!_leaveOpen)
        {
            _writer.Dispose();
            _reader.Dispose();
        }

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        _lifetime.Dispose();
        // An uncooperative writer can still be completing after the deadline. Do not dispose
        // the semaphore under its finally block; it owns no OS resources unless WaitHandle is used.
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
