using System.Security.Cryptography;
using System.Text.Json;
using CodexTray.Core;

namespace CodexTray;

// Deliberately a class: generated record ToString must never include the credential.
internal sealed class ApiBillingConfiguration
{
    public string ApiKey { get; set; } = "";
    public ApiBillingSettings Settings { get; set; } = new("", 0, default, default);
    public int WarningBand { get; set; }
    public bool ExpiryWarned { get; set; }
    public bool CycleEndWarned { get; set; }
    public List<PendingNotice> Pending { get; set; } = [];
}

internal sealed class ApiBillingStore(string directory)
{
    public string FilePath => Path.Combine(directory, "api-billing.dat");
    public ApiBillingConfiguration? Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 128_000) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            try
            {
                var value = JsonSerializer.Deserialize<ApiBillingConfiguration>(plain);
                if (value?.Settings is null || !ApiBillingClient.IsValidAdminKey(value.ApiKey)
                    || value.Pending is null || value.Pending.Count > 16 || value.WarningBand is < 0 or > 4
                    || value.Pending.Any(n => n is null || n.Title is null || n.Message is null || n.Title.Length > 128 || n.Message.Length > 2048)) return null;
                value.Settings.Validate();
                return value;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException) { return null; }
    }

    public void Save(ApiBillingConfiguration value)
    {
        Directory.CreateDirectory(directory);
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            File.WriteAllBytes(FilePath + ".tmp", ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Delete()
    {
        File.Delete(FilePath);
        File.Delete(FilePath + ".tmp");
    }
}
