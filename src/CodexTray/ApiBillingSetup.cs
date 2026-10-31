namespace CodexTray;

internal static class ApiBillingSetup
{
    public static bool Run(ApiBillingStore store)
    {
        using var form = new ApiBillingSettingsForm(store.Load(), store.FilePath);
        if (form.ShowDialog() != DialogResult.OK) return false;
        try
        {
            if (form.Remove) store.Delete();
            else if (form.Result is { } value) store.Save(value);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show("Could not save the encrypted API billing setup. Check access to your local application-data folder.", "CodexTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
