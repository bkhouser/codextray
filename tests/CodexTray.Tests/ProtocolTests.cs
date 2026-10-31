using System.Text.Json;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void ParsesNotificationInterleavedWithResponses()
    {
        var notification = AppServerProtocol.ParseLine("""{"method":"account/rateLimits/updated","params":{"rateLimits":{"primary":{"usedPercent":31}}}}""");
        var response = AppServerProtocol.ParseLine("""{"id":1,"result":{"ok":true}}""");

        Assert.Equal(ProtocolLineKind.Notification, notification.Kind);
        Assert.Equal("account/rateLimits/updated", notification.Method);
        Assert.Equal(31, notification.Payload!.Value.GetProperty("rateLimits").GetProperty("primary").GetProperty("usedPercent").GetInt32());
        Assert.Equal(ProtocolLineKind.Response, response.Kind);
        Assert.Equal(1, response.Id);
    }

    [Theory]
    [InlineData("not json", ProtocolLineKind.Malformed)]
    [InlineData("", ProtocolLineKind.Unrelated)]
    [InlineData("[]", ProtocolLineKind.Unrelated)]
    [InlineData("{\"some\":\"object\"}", ProtocolLineKind.Unrelated)]
    public void IgnoresMalformedAndUnrelatedLines(string line, ProtocolLineKind expected)
    {
        Assert.Equal(expected, AppServerProtocol.ParseLine(line).Kind);
    }

    [Fact]
    public async Task SimulatedStdioSessionCorrelatesResponsesAndHandlesInterleavedInput()
    {
        var clientToServer = new LineChannel();
        var serverToClient = new LineChannel();
        await using var session = new AppServerProtocolSession(serverToClient.Reader, clientToServer.Writer, leaveOpen: true);
        var notifications = new List<string>();
        var diagnostics = new List<ProtocolDiagnostic>();
        var cancellationToken = TestContext.Current.CancellationToken;
        session.NotificationReceived += (method, _) => notifications.Add(method);
        session.DiagnosticReceived += diagnostics.Add;

        var fakeServer = Task.Run(async () =>
        {
            var initialize = await clientToServer.ReadAsync(cancellationToken);
            using (var document = JsonDocument.Parse(initialize))
            {
                Assert.Equal("initialize", document.RootElement.GetProperty("method").GetString());
                Assert.Equal(0, document.RootElement.GetProperty("id").GetInt32());
            }

            await serverToClient.WriteAsync("""{"method":"account/rateLimits/updated","params":{}}""", cancellationToken);
            await serverToClient.WriteAsync("""{"id":0,"result":{"userAgent":"fake"}}""", cancellationToken);
            var initialized = await clientToServer.ReadAsync(cancellationToken);
            Assert.Contains("\"method\":\"initialized\"", initialized, StringComparison.Ordinal);

            var request = await clientToServer.ReadAsync(cancellationToken);
            using var requestDocument = JsonDocument.Parse(request);
            var id = requestDocument.RootElement.GetProperty("id").GetInt64();
            Assert.Equal("account/rateLimits/read", requestDocument.RootElement.GetProperty("method").GetString());
            await serverToClient.WriteAsync("malformed output", cancellationToken);
            await serverToClient.WriteAsync("""{"unrelated":true}""", cancellationToken);
            await serverToClient.WriteAsync(JsonSerializer.Serialize(new
            {
                id,
                result = new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 12, windowDurationMins = 300 }
                    }
                }
            }), cancellationToken);
        }, cancellationToken);

        await session.StartAsync(cancellationToken);
        var result = await session.SendRequestAsync("account/rateLimits/read", cancellationToken: cancellationToken);
        await fakeServer;

        Assert.True(QuotaParser.TryParse(result, DateTimeOffset.Now, out var snapshot));
        Assert.Equal(12m, snapshot!.IncludedBucket!.Primary!.UsedPercent);
        Assert.Contains("account/rateLimits/updated", notifications);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Kind == ProtocolLineKind.Malformed);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Kind == ProtocolLineKind.Unrelated);
    }
}
