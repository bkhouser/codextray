using System.Security.Cryptography;
using System.Text.Json;
using CodexTray.Core;

namespace CodexTray;

internal sealed class NotificationStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "notifications.dat");

    public NotificationState Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 1_000_000) return new();
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<NotificationState>(bytes);
            if (state is null || state.Version != 1 || state.Credits?.Seen is null
                || state.Quota is null || state.Thresholds is null || state.Pending is null
                || state.Credits.Seen.Count > 2048 || state.Quota.Count > 128 || state.Pending.Count > 32
                || state.Thresholds.Count > 64
                || state.Credits.LastAvailableCount is < 0 or > 10000 || state.Credits.Unidentified is < 0 or > 10000
                || state.Credits.Seen.Any(p => p.Key.Length != 64 || p.Value is < 1 or > 256)
                || state.Quota.Any(p => p.Key.Length > 1024 || p.Value is null || p.Value.Remaining is < 0 or > 100)
                || state.Quota.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Quota.Count
                || state.Thresholds.Any(p => p.Key.Length > 256 || p.Value is not (0 or 70 or 90))
                || state.Thresholds.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Thresholds.Count
                || state.Pending.Any(n => n is null || n.Title is null || n.Message is null || n.Title.Length > 128 || n.Message.Length > 2048))
                return new();
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return new(); }
    }

    public bool Save(NotificationState state)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_path + ".tmp", bytes);
            File.Move(_path + ".tmp", _path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return false; }
    }
}
