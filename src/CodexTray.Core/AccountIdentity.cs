using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexTray.Core;

public static class AccountIdentity
{
    public static string? GetScope(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("account", out var account)
            || account.ValueKind != JsonValueKind.Object) return null;
        string? Text(string name) => account.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var identity = Text("id") ?? Text("email")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(identity)) return null;
        var routing = payload.TryGetProperty("workspaceRouting", out var workspace) ? Canonicalize(workspace) : "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\u001f", Text("type"), identity, Text("planType"), routing))));
    }

    private static string Canonicalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
        else value.WriteTo(writer);
    }
}
