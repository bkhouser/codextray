using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexTray.Core;

/// <summary>Read-only organization cost access. Injected HttpClient handlers must disable redirects.</summary>
public sealed class ApiBillingClient(HttpClient httpClient)
{
    public const int MaximumResponseBytes = 1024 * 1024;
    public static bool IsValidAdminKey(string? value) => value is { Length: >= 10 and <= 512 }
        && value.StartsWith("sk-admin-", StringComparison.Ordinal) && value.All(ApiBillingSettings.IsIdentifierCharacter);
    private const int MaximumPages = 8;
    private const string Endpoint = "https://api.openai.com/v1/organization/costs";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Use this factory for production; automatic redirects must never carry an Admin credential.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = 32
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<ApiBillingSnapshot> GetAsync(string adminKey, ApiBillingSettings settings,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(now);
        if (!IsValidAdminKey(adminKey))
            throw new ApiBillingException("Enter a valid Admin API key locally.");
        using var deadline = new CancellationTokenSource(RequestTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var start = settings.CycleStartUtc.ToUnixTimeSeconds();
            var end = (now < settings.CycleEndUtc ? now : settings.CycleEndUtc).ToUnixTimeSeconds();
            // Preserve exact cycle/query instants: the API contract filters [start, end).
            // Daily bucket metadata may span those instants; do not infer unfiltered costs
            // or prorate monetary amounts from metadata boundaries alone.
            var baseQuery = string.Create(CultureInfo.InvariantCulture,
                $"{Endpoint}?start_time={start}&end_time={end}&bucket_width=1d&limit=180");
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            var ranges = new List<(long Start, long End)>();
            string? cursor = null;
            decimal spent = 0;
            for (var page = 0; page < MaximumPages; page++)
            {
                operation.Token.ThrowIfCancellationRequested();
                var uri = baseQuery + (cursor is null ? "" : "&page=" + Uri.EscapeDataString(cursor));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminKey);
                request.Headers.Add("OpenAI-Organization", settings.OrganizationId);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await SendBoundedAsync(request, operation.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new ApiBillingException("API billing redirects are not allowed.");
                if (!response.IsSuccessStatusCode)
                    throw new ApiBillingException(response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API billing access was denied. Verify the Admin key and organization locally.",
                        HttpStatusCode.TooManyRequests => "API billing is rate limited. Try again later.",
                        _ => "API billing is unavailable. The remaining balance is unknown."
                    });
                using var document = await ReadDocumentAsync(response.Content, operation.Token).ConfigureAwait(false);
                var root = document.RootElement;
                ValidateObject(root);
                if (!IsString(root, "object", "page") || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                    || data.GetArrayLength() > 180
                    || !root.TryGetProperty("has_more", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !root.TryGetProperty("next_page", out var next) || next.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw InvalidData();
                foreach (var bucket in data.EnumerateArray())
                {
                    ValidateObject(bucket);
                    if (!IsString(bucket, "object", "bucket")
                        || !ReadInteger(bucket, "start_time", out var bucketStart) || !ReadInteger(bucket, "end_time", out var bucketEnd)
                        || bucketStart < 0 || bucketEnd <= bucketStart || bucketStart >= end || bucketEnd <= start
                        || ranges.Any(range => bucketStart < range.End && bucketEnd > range.Start)
                        || !bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                        throw InvalidData();
                    ranges.Add((bucketStart, bucketEnd));
                    foreach (var result in results.EnumerateArray())
                    {
                        ValidateObject(result);
                        if (!IsString(result, "object", "organization.costs.result")
                            || !result.TryGetProperty("amount", out var amount)) throw InvalidData();
                        ValidateObject(amount);
                        if (!amount.TryGetProperty("currency", out var currency) || currency.ValueKind != JsonValueKind.String
                            || !string.Equals(currency.GetString(), "usd", StringComparison.OrdinalIgnoreCase)
                            || !amount.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number
                            || !value.TryGetDecimal(out var number)) throw InvalidData();
                        spent = checked(spent + number);
                    }
                }
                if (!more.GetBoolean())
                {
                    if (next.ValueKind != JsonValueKind.Null) throw InvalidData();
                    return new ApiBillingSnapshot(settings, spent, now);
                }
                cursor = next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                if (string.IsNullOrEmpty(cursor) || cursor.Length > 2048 || !cursors.Add(cursor)) throw InvalidData();
            }
            throw new ApiBillingException("API billing pagination was incomplete. The remaining balance is unknown.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("API billing refresh canceled.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new ApiBillingException("API billing timed out. The remaining balance is unknown.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OverflowException)
        {
            throw InvalidData();
        }
    }

    private async Task<HttpResponseMessage> SendBoundedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var send = httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try { return await send.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            _ = send.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) throw InvalidData();
        using var stream = await content.ReadAsStreamAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaximumResponseBytes + 1 - (int)bytes.Length)), cancellationToken)
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            bytes.Write(buffer, 0, count);
            if (bytes.Length > MaximumResponseBytes) throw InvalidData();
        }
        return JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static void ValidateObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidData();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!keys.Add(property.Name)) throw InvalidData();
    }
    private static bool IsString(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
    private static bool ReadInteger(JsonElement element, string property, out long number)
    {
        number = 0;
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
    }
    private static ApiBillingException InvalidData() => new("API billing data is unavailable or invalid. The remaining balance is unknown.");
}
