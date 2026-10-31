using System.Net;
using System.Text;
using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class ApiBillingTests
{
    private static readonly string Key = string.Concat("sk-admin-", "synthetic-key-for-tests");
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Start.AddDays(20);
    private static readonly ApiBillingSettings Settings = new("org-synthetic", 50m, Start, Start.AddMonths(1));
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SumsUsdAcrossCompletePagesAndUsesOnlyFixedOrganizationEndpoint()
    {
        using var handler = new FakeHandler((request, call) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.openai.com", request.RequestUri.Host);
            Assert.Equal("/v1/organization/costs", request.RequestUri.AbsolutePath);
            Assert.Contains("start_time=" + Start.ToUnixTimeSeconds(), request.RequestUri.Query);
            Assert.Contains("end_time=" + Now.ToUnixTimeSeconds(), request.RequestUri.Query);
            Assert.Contains("bucket_width=1d&limit=180", request.RequestUri.Query);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(Key, request.Headers.Authorization.Parameter);
            Assert.Equal("org-synthetic", Assert.Single(request.Headers.GetValues("OpenAI-Organization")));
            if (call == 2) Assert.Contains("page=next%26not_a_parameter%3D1", request.RequestUri.Query);
            return Json(Page(call == 1 ? 10m : 7.25m, call - 1, call == 1 ? "next&not_a_parameter=1" : null));
        });
        using var http = new HttpClient(handler);
        var snapshot = await new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(17.25m, snapshot.Spent);
        Assert.Equal(32.75m, snapshot.Remaining);
        Assert.Equal(65.5m, snapshot.RemainingPercent);
        Assert.Equal(Now, snapshot.UpdatedAt);
    }

    [Fact]
    public async Task ExplicitCompleteEmptyDataIsTheOnlyEmptyZeroBalanceCase()
    {
        using var handler = new FakeHandler((_, _) => Json("""{"object":"page","data":[],"has_more":false,"next_page":null}"""));
        using var http = new HttpClient(handler);
        Assert.Equal(0m, (await new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation)).Spent);
    }

    [Fact]
    public async Task ExactPartialDayQueryInstantsArePreservedWithoutProratingBucketMetadata()
    {
        var settings = Settings with { CycleStartUtc = Start.AddHours(12).AddMinutes(17) };
        var now = Now.AddHours(3).AddMinutes(29);
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Contains("start_time=" + settings.CycleStartUtc.ToUnixTimeSeconds(), request.RequestUri!.Query);
            Assert.Contains("end_time=" + now.ToUnixTimeSeconds(), request.RequestUri.Query);
            // The API contract filters costs by the exact query interval. Bucket metadata
            // alone does not establish whether the service includes out-of-range charges.
            return Json(Page(12m));
        });
        using var http = new HttpClient(handler);
        var snapshot = await new ApiBillingClient(http).GetAsync(Key, settings, now, Cancellation);
        Assert.Equal(12m, snapshot.Spent);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"object\":\"page\",\"data\":null,\"has_more\":false,\"next_page\":null}")]
    [InlineData("{\"object\":\"page\",\"data\":[],\"has_more\":false}")]
    [InlineData("{\"object\":\"page\",\"data\":[],\"has_more\":true,\"next_page\":null}")]
    [InlineData("{\"object\":\"page\",\"data\":[],\"has_more\":false,\"next_page\":\"unread\"}")]
    [InlineData("{\"object\":\"page\",\"data\":[],\"has_more\":false,\"has_more\":true,\"next_page\":null}")]
    public async Task UnknownOrMalformedPageDoesNotBecomeZero(string body)
    {
        using var handler = new FakeHandler((_, _) => Json(body));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"value\":1}")]
    [InlineData("{\"value\":1,\"currency\":\"eur\"}")]
    [InlineData("{\"value\":\"1\",\"currency\":\"usd\"}")]
    [InlineData("{\"value\":null,\"currency\":\"usd\"}")]
    [InlineData("{\"value\":1e999,\"currency\":\"usd\"}")]
    public async Task UnknownAmountsAndOtherCurrenciesFailClosed(string amount)
    {
        var body = Page(1m).Replace("{\"value\":1,\"currency\":\"usd\"}", amount, StringComparison.Ordinal);
        using var handler = new FakeHandler((_, _) => Json(body));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation));
    }

    [Fact]
    public async Task RejectsRepeatedOrIncompletePaginationAndDuplicateBuckets()
    {
        foreach (var repeated in new[] { true, false })
        {
            using var handler = new FakeHandler((_, call) => Json(Page(1m, call - 1, repeated ? "same" : "cursor-" + call)));
            using var http = new HttpClient(handler);
            await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation));
            Assert.Equal(repeated ? 2 : 8, handler.Calls);
        }
        using var duplicate = new FakeHandler((_, call) => Json(Page(1m, 0, call == 1 ? "next" : null)));
        using var duplicateHttp = new HttpClient(duplicate);
        await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(duplicateHttp).GetAsync(Key, Settings, Now, Cancellation));
    }

    [Theory]
    [InlineData("sk-test\r\nInjected: yes")]
    [InlineData("sk-test key")]
    [InlineData("")]
    public async Task RejectsUnsafeCredentialHeadersBeforeNetwork(string key)
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No request expected."));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(http).GetAsync(key, Settings, Now, Cancellation));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InvalidSettingsAndExpiredCycleNeverFetchOrRollover()
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No request expected."));
        using var http = new HttpClient(handler);
        var client = new ApiBillingClient(http);
        foreach (var invalid in new[]
        {
            Settings with { OrganizationId = "org-synthetic\r\nInjected: yes" },
            Settings with { OrganizationId = "org-synthetic other" },
            Settings with { Allowance = 0 },
            Settings with { CycleEndUtc = Start },
            Settings with { CycleStartUtc = Start.ToOffset(TimeSpan.FromHours(1)) }
        })
            await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Key, invalid, Now, Cancellation));
        var expired = await Assert.ThrowsAsync<ApiBillingException>(() => client.GetAsync(Key, Settings, Settings.CycleEndUtc, Cancellation));
        Assert.Contains("Update the cycle", expired.Message);
        Assert.False(Settings.IsCurrentAt(Settings.CycleEndUtc));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBoundsUncooperativeSendAndBodyRead(bool stallBody)
    {
        var stalled = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new TestStream(stall: true);
        using var handler = new AsyncHandler((_, _) => stallBody
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })
            : stalled.Task);
        using var http = new HttpClient(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var call = new ApiBillingClient(http).GetAsync(Key, Settings, Now, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(3), Cancellation));
        stalled.TrySetResult(Json(Page(1m)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseLimitAppliesBeforeAndDuringBodyRead(bool contentLengthKnown)
    {
        using var stream = new TestStream(stall: false);
        using var handler = new FakeHandler((_, _) =>
        {
            var content = new StreamContent(stream);
            if (contentLengthKnown) content.Headers.ContentLength = ApiBillingClient.MaximumResponseBytes + 1;
            return new(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation));
        Assert.Equal(contentLengthKnown ? 0 : ApiBillingClient.MaximumResponseBytes + 1, stream.BytesRead);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task ErrorsDoNotEchoResponseBodiesOrFollowRedirects(HttpStatusCode status)
    {
        using var handler = new FakeHandler((_, _) => new(status) { Content = new StringContent("sensitive-response-body") });
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ApiBillingException>(() => new ApiBillingClient(http).GetAsync(Key, Settings, Now, Cancellation));
        Assert.DoesNotContain("sensitive", error.ToString());
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(-10, 50, 100)]
    [InlineData(80, 0, 0)]
    [InlineData(25, 25, 50)]
    public void DerivedRemainingIsClamped(int spent, int remaining, int percent)
    {
        var snapshot = new ApiBillingSnapshot(Settings, spent, Now);
        Assert.Equal(remaining, snapshot.Remaining);
        Assert.Equal(percent, snapshot.RemainingPercent);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string Page(decimal amount, int day = 0, string? next = null) => JsonSerializer.Serialize(new
    {
        @object = "page",
        data = new[] { new { @object = "bucket", start_time = Start.AddDays(day).ToUnixTimeSeconds(), end_time = Start.AddDays(day + 1).ToUnixTimeSeconds(),
            results = new[] { new { @object = "organization.costs.result", amount = new { value = amount, currency = "usd" } } } } },
        has_more = next is not null,
        next_page = next
    });
    private sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> handle) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request, ++Calls));
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
    private sealed class TestStream(bool stall) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stall) return new(new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
            buffer.Span.Fill((byte)'x');
            BytesRead += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
