using System.Text.Json;

namespace CodexTray.Core;

public enum ProtocolLineKind
{
    Response,
    Notification,
    ServerRequest,
    Unrelated,
    Malformed
}

public sealed record RpcError(int? Code, string Message);

public sealed record ParsedProtocolLine(
    ProtocolLineKind Kind,
    long? Id = null,
    string? Method = null,
    JsonElement? Payload = null,
    RpcError? Error = null);

public static class AppServerProtocol
{
    public static ParsedProtocolLine ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new ParsedProtocolLine(ProtocolLineKind.Unrelated);
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ParsedProtocolLine(ProtocolLineKind.Unrelated);
            }

            var hasId = TryGetId(root, out var id);
            var method = root.TryGetProperty("method", out var methodElement)
                && methodElement.ValueKind == JsonValueKind.String
                    ? methodElement.GetString()
                    : null;

            if (hasId && root.TryGetProperty("result", out var result))
            {
                return new ParsedProtocolLine(ProtocolLineKind.Response, id, Payload: result.Clone());
            }

            if (hasId && root.TryGetProperty("error", out var error))
            {
                return new ParsedProtocolLine(ProtocolLineKind.Response, id, Error: ParseError(error));
            }

            if (hasId && method is not null)
            {
                return new ParsedProtocolLine(
                    ProtocolLineKind.ServerRequest,
                    id,
                    method,
                    CloneProperty(root, "params"));
            }

            if (method is not null)
            {
                return new ParsedProtocolLine(
                    ProtocolLineKind.Notification,
                    Method: method,
                    Payload: CloneProperty(root, "params"));
            }

            return new ParsedProtocolLine(ProtocolLineKind.Unrelated);
        }
        catch (JsonException)
        {
            return new ParsedProtocolLine(ProtocolLineKind.Malformed);
        }
    }

    private static bool TryGetId(JsonElement root, out long id)
    {
        if (root.TryGetProperty("id", out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out id))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out id))
            {
                return true;
            }
        }

        id = default;
        return false;
    }

    private static JsonElement? CloneProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) ? element.Clone() : null;

    private static RpcError ParseError(JsonElement error)
    {
        int? code = error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("code", out var codeElement)
            && codeElement.ValueKind == JsonValueKind.Number
            && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : null;
        var message = error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? "App Server request failed."
                : "App Server request failed.";
        return new RpcError(code, message);
    }
}
