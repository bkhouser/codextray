using System.Text;
using CodexTray.Core;

namespace CodexTray.Tests;

public sealed class UiRegressionTests
{
    [Fact]
    public void MalformedEncryptedStateIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexTrayTests-" + Guid.NewGuid());
        try
        {
            var store = new NotificationStore(directory);
            var state = new NotificationState();
            state.Quota["codex"] = null!;
            Assert.True(store.Save(state));
            Assert.Empty(store.Load().Quota);
            state = new();
            state.Thresholds["codex"] = 70;
            state.Thresholds["CODEX"] = 90;
            Assert.True(store.Save(state));
            Assert.Empty(store.Load().Thresholds);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void NotificationStoreEncryptsAndRestoresPendingNotices()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexTrayTests-" + Guid.NewGuid());
        try
        {
            var store = new NotificationStore(directory);
            var ledger = new NotificationLedger();
            ledger.SelectAccount("synthetic-account");
            ledger.Enqueue("Synthetic private notice", "Pending after restart");
            Assert.True(store.Save(ledger.State));
            var bytes = File.ReadAllBytes(Path.Combine(directory, "notifications.dat"));
            Assert.DoesNotContain("Synthetic private notice", Encoding.UTF8.GetString(bytes));
            Assert.Equal(ledger.State.Pending, store.Load().Pending);
            File.WriteAllBytes(Path.Combine(directory, "notifications.dat"), [1,2,3]);
            Assert.Empty(store.Load().Pending);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RefreshDisposesOldControlsAndNotificationHasKeyboardDismissal()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new QuotaPopupForm();
                var status = new MonitorStatus(MonitorConnectionState.Connecting, "Synthetic status");
                form.UpdateStatus(status);
                var panel = Assert.IsType<FlowLayoutPanel>(Assert.Single(form.Controls.Cast<Control>()));
                var old = panel.Controls.Cast<Control>().ToArray();
                form.UpdateStatus(status);
                Assert.All(old, control => Assert.True(control.IsDisposed));
                for (var i = 0; i < 200; i++) form.UpdateStatus(status);
                form.UpdateStatus(status with { Message = string.Join(' ', Enumerable.Repeat("Synthetic long diagnostic", 1000)) });
                Assert.True(form.Height <= Screen.FromPoint(Cursor.Position).WorkingArea.Height);
                Assert.True(panel.AutoScroll);
                form.UpdateStatus(status);
                Assert.False(panel.AutoScroll);
                using var card = new ResetNotificationForm();
                var dismissed = false;
                card.Dismissed += () => dismissed = true;
                Assert.IsType<Button>(card.CancelButton);
                card.ShowNotice(new PendingNotice("Synthetic reset available", "A synthetic quota window increased to 100% remaining. This test card can be dismissed with the button or Escape."));
                Application.DoEvents();
                if (Environment.GetEnvironmentVariable("CODEXTRAY_QA_DIR") is { } qa)
                {
                    Directory.CreateDirectory(qa);
                    using var bitmap = new Bitmap(card.Width, card.Height);
                    card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, card.Size));
                    bitmap.Save(Path.Combine(qa, "notification.png"));
                    var now = DateTimeOffset.Now;
                    var bucket = new QuotaBucket("codex", "Included quota", null, new(25, 300, now.AddHours(3)), new(40, 10080, now.AddDays(4)), null, null);
                    var snapshot = new QuotaSnapshot([bucket], new(true, false, 125, "125"), 2,
                        [new("Codex quota reset", null, "codexRateLimits", now.AddDays(2)), new("Codex quota reset", null, "codexRateLimits", now.AddDays(6))], now);
                    form.UpdateStatus(new(MonitorConnectionState.Connected, "Synthetic example", snapshot));
                    form.Show();
                    Application.DoEvents();
                    using var quotaBitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(quotaBitmap, new Rectangle(Point.Empty, form.Size));
                    quotaBitmap.Save(Path.Combine(qa, "quota-window.png"));
                    form.Hide();
                }
                card.CancelButton!.PerformClick();
                Assert.True(dismissed);
                using var setup = new ApiBillingSettingsForm(null, @"C:\Synthetic\CodexTray\api-billing.dat");
                setup.Show();
                Application.DoEvents();
                var setupPanel = Assert.IsType<FlowLayoutPanel>(Assert.Single(setup.Controls.Cast<Control>()));
                Assert.Contains(setupPanel.Controls.Cast<Control>(), c => c is TextBox box && box.UseSystemPasswordChar && box.Text.Length == 0);
                if (Environment.GetEnvironmentVariable("CODEXTRAY_QA_DIR") is { } setupQa)
                {
                    using var bitmap = new Bitmap(setup.Width, setup.Height);
                    setup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, setup.Size));
                    bitmap.Save(Path.Combine(setupQa, "api-setup.png"));
                }
                setup.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI check timed out");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
