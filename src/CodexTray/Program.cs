using CodexTray.Core;

namespace CodexTray;

internal static class Program
{
    private const string MutexName = @"Local\CodexTray.SingleInstance";
    private const string ActivationEventName = @"Local\CodexTray.Activate";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (!SingleInstanceGuard.TryAcquire(MutexName, out var guard) || guard is null)
        {
            SignalExistingInstance();
            if (args.Contains("--configure-api-billing", StringComparer.Ordinal))
                MessageBox.Show("Use Pro API credit setup in the running tray menu. If that option is missing, exit the older copy and launch this one again.", "CodexTray");
            return;
        }

        using (guard)
        {
            if (!UserDataLock.TryAcquire(AppPaths.DataDirectory, out var dataLock) || dataLock is null)
            {
                MessageBox.Show("CodexTray cannot acquire its per-user data lock. Another Windows session may already be running it, or the local data folder is unavailable. Close other copies before changing billing setup.", "CodexTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (dataLock)
            using (var activationEvent = new EventWaitHandle(
                       initialState: false,
                       EventResetMode.AutoReset,
                       ActivationEventName))
            {
                if (args.Contains("--configure-api-billing", StringComparer.Ordinal)
                    && !ApiBillingSetup.Run(new ApiBillingStore(AppPaths.DataDirectory))) return;
                Application.Run(new TrayApplicationContext(activationEvent));
            }
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance may still be starting up.
        }
    }
}
