using System.Text;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class ApiBillingStoreTests
{
    [Theory]
    [InlineData("sk-admin-")]
    [InlineData("sk-proj-synthetic")]
    [InlineData("sk-admin-invalid!key")]
    [InlineData("sk-admin-invalid\nkey")]
    public void InvalidCredentialShapesAreRejectedConsistently(string key) => Assert.False(ApiBillingClient.IsValidAdminKey(key));

    [Fact]
    public void CredentialAndPendingAlertsRoundtripEncryptedAndCanBeRemoved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexTrayBilling-" + Guid.NewGuid());
        try
        {
            var store = new ApiBillingStore(directory);
            var key = string.Concat("sk-admin-", new string('x', 32));
            var value = new ApiBillingConfiguration
            {
                ApiKey = key,
                Settings = new("org-synthetic", 100m, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(20)),
                WarningBand = 2, ExpiryWarned = true, CycleEndWarned = true,
                Pending = [new("API warning", "Synthetic alert")]
            };
            store.Save(value);
            Assert.DoesNotContain(key, Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)));
            var restored = Assert.IsType<ApiBillingConfiguration>(store.Load());
            Assert.Equal(key, restored.ApiKey);
            Assert.Equal(value.Settings, restored.Settings);
            Assert.Equal(2, restored.WarningBand);
            Assert.True(restored.ExpiryWarned);
            Assert.True(restored.CycleEndWarned);
            Assert.Single(restored.Pending);
            Assert.DoesNotContain(key, restored.ToString()!);
            store.Delete();
            Assert.Null(store.Load());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100, 0)] [InlineData(25, 1)] [InlineData(10, 2)] [InlineData(5, 3)] [InlineData(0, 4)]
    public void WarningsClassifyLowRemainingAllowance(decimal remaining, int band) =>
        Assert.Equal(band, ApiCreditWarnings.Band(remaining));
}
