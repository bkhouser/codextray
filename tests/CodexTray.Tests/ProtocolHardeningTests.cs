using System.Text;
using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class ProtocolHardeningTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("\"unexpected\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("1.5")]
    [InlineData("99999999999999999")]
    public void WrongKindOrOutOfRangeErrorCodeDoesNotCrashParser(string code)
    {
        var parsed = AppServerProtocol.ParseLine($$$"""{"id":1,"error":{"code":{{{code}}},"message":"safe"}}""");
        Assert.Equal(ProtocolLineKind.Response, parsed.Kind);
        Assert.Null(parsed.Error!.Code);
        Assert.Equal("safe", parsed.Error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestDeadlineIncludesUncooperativeWriteAndFlush(bool hangFlush)
    {
        var writer = new HangingWriter(hangFlush);
        await using var session = new AppServerProtocolSession(TextReader.Null, writer, leaveOpen: true);
        var request = session.SendRequestAsync("test", timeout: TimeSpan.FromMilliseconds(100), cancellationToken: Cancellation);
        await Assert.ThrowsAsync<TimeoutException>(() => request.WaitAsync(TimeSpan.FromSeconds(3), Cancellation));
        await Assert.ThrowsAsync<TimeoutException>(() => session.Completion);
        await Assert.ThrowsAsync<IOException>(() => session.SendRequestAsync("after-timeout", cancellationToken: Cancellation));
        writer.Release();
    }

    [Fact]
    public async Task QueuedRequestCannotWaitForeverForWriteLock()
    {
        var writer = new HangingWriter(false);
        await using var session = new AppServerProtocolSession(TextReader.Null, writer, leaveOpen: true);
        var first = session.SendRequestAsync("first", timeout: TimeSpan.FromSeconds(3), cancellationToken: Cancellation);
        var queued = session.SendRequestAsync("queued", timeout: TimeSpan.FromMilliseconds(100), cancellationToken: Cancellation);
        await Assert.ThrowsAsync<TimeoutException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2), Cancellation));
        await Assert.ThrowsAnyAsync<Exception>(() => first.WaitAsync(TimeSpan.FromSeconds(2), Cancellation));
        await Assert.ThrowsAsync<TimeoutException>(() => session.Completion);
        writer.Release();
    }

    [Fact]
    public async Task OversizedUnterminatedStdoutIsRejectedDuringStreamingRead()
    {
        var reader = new EndlessReader();
        await using var session = new AppServerProtocolSession(reader, TextWriter.Null, leaveOpen: true);
        await Assert.ThrowsAnyAsync<Exception>(() => session.StartAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(3), Cancellation));
        await Assert.ThrowsAsync<InvalidDataException>(() => session.Completion);
        Assert.InRange(reader.CharactersRead, AppServerProtocolSession.MaximumProtocolLineLength + 1,
            AppServerProtocolSession.MaximumProtocolLineLength + 4096);
    }

    [Fact]
    public async Task EofFailsPendingAndFutureRequestsWithoutWaitingForDeadline()
    {
        var input = new LineChannel();
        var output = new LineChannel();
        await using var session = new AppServerProtocolSession(output.Reader, input.Writer, leaveOpen: true);
        var start = session.StartAsync(Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync("""{"id":0,"result":{}}""", Cancellation);
        await start;
        await input.ReadAsync(Cancellation); // initialized
        var request = session.SendRequestAsync("test", timeout: TimeSpan.FromMinutes(1), cancellationToken: Cancellation);
        await input.ReadAsync(Cancellation);
        output.Complete();
        await Assert.ThrowsAnyAsync<Exception>(() => request.WaitAsync(TimeSpan.FromSeconds(2), Cancellation));
        await Assert.ThrowsAsync<EndOfStreamException>(() => session.Completion);
        await Assert.ThrowsAsync<IOException>(() => session.SendRequestAsync("after-eof", cancellationToken: Cancellation));
    }

    [Fact]
    public async Task MissingResponseEndsTheWholeSession()
    {
        var input = new LineChannel();
        var output = new LineChannel();
        await using var session = new AppServerProtocolSession(output.Reader, input.Writer, leaveOpen: true);
        var start = session.StartAsync(Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync("""{"id":0,"result":{}}""", Cancellation);
        await start;
        await Assert.ThrowsAsync<TimeoutException>(() => session.SendRequestAsync("test", timeout: TimeSpan.FromMilliseconds(100), cancellationToken: Cancellation));
        await Assert.ThrowsAsync<TimeoutException>(() => session.Completion);
    }

    [Fact]
    public async Task MalformedErrorCodeFailsOnlyItsRequestAndReaderContinues()
    {
        var input = new LineChannel();
        var output = new LineChannel();
        await using var session = new AppServerProtocolSession(output.Reader, input.Writer, leaveOpen: true);
        var start = session.StartAsync(Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync("""{"id":0,"result":{}}""", Cancellation);
        await start;
        await input.ReadAsync(Cancellation);
        var failed = session.SendRequestAsync("failed", cancellationToken: Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync("""{"id":1,"error":{"code":{},"message":"safe"}}""", Cancellation);
        await Assert.ThrowsAsync<AppServerRpcException>(() => failed);
        var good = session.SendRequestAsync("good", cancellationToken: Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync("""{"id":2,"result":{"ok":true}}""", Cancellation);
        Assert.True((await good).GetProperty("ok").GetBoolean());
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task PreCanceledRequestsAndNotificationsDoNotEndSession()
    {
        await using var session = new AppServerProtocolSession(TextReader.Null, TextWriter.Null, leaveOpen: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SendRequestAsync("canceled", cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SendNotificationAsync("canceled", cancellationToken: cancellation.Token));
        Assert.False(session.Completion.IsCompleted);
        await session.SendNotificationAsync("still-available", cancellationToken: Cancellation);

        using var completedNotification = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await session.SendNotificationAsync("completed", cancellationToken: completedNotification.Token);
        completedNotification.Cancel();
        Assert.False(session.Completion.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializationCancellationEndsSessionAndReader(bool preCanceled)
    {
        var input = new LineChannel();
        var output = new LineChannel();
        var reader = new CancellationTrackingReader(output.Reader);
        await using var session = new AppServerProtocolSession(reader, input.Writer, leaveOpen: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        if (preCanceled) cancellation.Cancel();
        var start = session.StartAsync(cancellation.Token);
        if (!preCanceled)
        {
            await input.ReadAsync(Cancellation);
            cancellation.Cancel();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Completion);
        await reader.Stopped.WaitAsync(TimeSpan.FromSeconds(2), Cancellation);
        await Assert.ThrowsAsync<IOException>(() => session.SendRequestAsync("after-failed-start", cancellationToken: Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializationRpcOrNotificationFailureEndsSessionAndReader(bool failNotification)
    {
        var input = new LineChannel();
        var output = new LineChannel();
        var reader = new CancellationTrackingReader(output.Reader);
        var writer = new InitializedFailingWriter(input.Writer);
        await using var session = new AppServerProtocolSession(reader, writer, leaveOpen: true);
        var start = session.StartAsync(Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync(failNotification
            ? """{"id":0,"result":{}}"""
            : """{"id":0,"error":{"code":-32600,"message":"Synthetic rejected initialization"}}""", Cancellation);
        if (failNotification)
        {
            await Assert.ThrowsAsync<IOException>(() => start);
            await Assert.ThrowsAsync<IOException>(() => session.Completion);
        }
        else
        {
            await Assert.ThrowsAsync<AppServerRpcException>(() => start);
            await Assert.ThrowsAsync<AppServerRpcException>(() => session.Completion);
        }
        await reader.Stopped.WaitAsync(TimeSpan.FromSeconds(2), Cancellation);
        await Assert.ThrowsAsync<IOException>(() => session.SendRequestAsync("after-failed-start", cancellationToken: Cancellation));
    }

    [Fact]
    public async Task CancelingFullyWrittenRequestPreservesOtherRequestsAndIgnoresLateResponse()
    {
        var input = new LineChannel();
        var output = new LineChannel();
        await using var session = new AppServerProtocolSession(output.Reader, input.Writer, leaveOpen: true);
        var start = session.StartAsync(Cancellation);
        await input.ReadAsync(Cancellation);
        await output.WriteAsync("""{"id":0,"result":{}}""", Cancellation);
        await start;
        await input.ReadAsync(Cancellation);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var canceled = session.SendRequestAsync("abandoned", cancellationToken: cancellation.Token);
        await input.ReadAsync(Cancellation);
        var other = session.SendRequestAsync("other", cancellationToken: Cancellation);
        await input.ReadAsync(Cancellation);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(session.Completion.IsCompleted);

        await output.WriteAsync("""{"id":1,"result":{"late":true}}""", Cancellation);
        await output.WriteAsync("""{"id":2,"result":{"ok":true}}""", Cancellation);
        Assert.True((await other).GetProperty("ok").GetBoolean());
        Assert.False(session.Completion.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationWhileQueuedForWriteLockIsCallerScoped(bool notification)
    {
        var writer = new HangingWriter(false);
        await using var session = new AppServerProtocolSession(TextReader.Null, writer, leaveOpen: true);
        var first = session.SendNotificationAsync("holding-lock", cancellationToken: Cancellation);
        await writer.Blocked.WaitAsync(TimeSpan.FromSeconds(2), Cancellation);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task queued = notification
            ? session.SendNotificationAsync("queued", cancellationToken: cancellation.Token)
            : session.SendRequestAsync("queued", cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(session.Completion.IsCompleted);
        writer.Release();
        await first;
        await session.SendNotificationAsync("still-available", cancellationToken: Cancellation);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationDuringAmbiguousWriteOrFlushStillEndsSession(bool notification, bool hangFlush)
    {
        var writer = new HangingWriter(hangFlush);
        await using var session = new AppServerProtocolSession(TextReader.Null, writer, leaveOpen: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task operation = notification
            ? session.SendNotificationAsync("partial", cancellationToken: cancellation.Token)
            : session.SendRequestAsync("partial", cancellationToken: cancellation.Token);
        await writer.Blocked.WaitAsync(TimeSpan.FromSeconds(2), Cancellation);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Completion);
        await Assert.ThrowsAsync<IOException>(() => session.SendRequestAsync("after-partial-write", cancellationToken: Cancellation));
        writer.Release();
    }

    private sealed class CancellationTrackingReader(TextReader inner) : TextReader
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Stopped => _stopped.Task;
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            try { return await inner.ReadAsync(buffer, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _stopped.TrySetResult();
                throw;
            }
        }
    }

    private sealed class InitializedFailingWriter(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            using var message = JsonDocument.Parse(buffer.ToString());
            if (message.RootElement.GetProperty("method").GetString() == "initialized")
                throw new IOException("Synthetic initialized notification failure.");
            return inner.WriteLineAsync(buffer, cancellationToken);
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    }

    private sealed class HangingWriter(bool hangFlush) : TextWriter
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Blocked => _blocked.Task;
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (hangFlush) return Task.CompletedTask;
            _blocked.TrySetResult();
            return _release.Task;
        }
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (!hangFlush) return Task.CompletedTask;
            _blocked.TrySetResult();
            return _release.Task;
        }
        public void Release() => _release.TrySetResult();
    }

    internal sealed class EndlessReader : TextReader
    {
        public int CharactersRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Fill('x');
            CharactersRead += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }
    }
}
