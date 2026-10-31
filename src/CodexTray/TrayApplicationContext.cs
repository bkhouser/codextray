using System.Diagnostics;
using CodexTray.Core;

namespace CodexTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string UsageDashboardUrl = "https://chatgpt.com/codex/cloud/settings/analytics?no_universal_links=1#usage";
    private const string FallbackUsageUrl = "https://chatgpt.com/codex/settings/usage";
    private readonly NotifyIcon _notifyIcon;
    private readonly QuotaPopupForm _popup;
    private readonly ResetNotificationForm _resetNotification;
    private readonly CodexQuotaMonitor _monitor;
    private readonly ISanitizedLogger _logger;
    private readonly StartupManager _startupManager = new();
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly NotificationStore _notificationStore;
    private readonly NotificationLedger _ledger;
    private string? _selectedScope;
    private readonly Action<MonitorStatus> _statusHandler;
    private readonly System.Windows.Forms.Timer _activationTimer;
    private Icon? _currentIcon;
    private MonitorStatus _status = new(MonitorConnectionState.Connecting, "Starting Codex App Server…");
    private bool _isExiting;
    private readonly ApiBillingStore _billingStore = new(AppPaths.DataDirectory);
    private readonly HttpClient _billingHttp = ApiBillingClient.CreateHttpClient();
    private readonly CancellationTokenSource _billingLifetime = new();
    private readonly System.Windows.Forms.Timer _billingTimer = new() { Interval = 300_000 };
    private readonly ResetNotificationForm _billingNotification = new(positionAtTop: true);
    private ApiBillingConfiguration? _billingConfiguration;
    private ApiBillingSnapshot? _billingSnapshot;
    private bool _billingBusy;
    private bool _billingEditing;
    private int _billingGeneration;

    public TrayApplicationContext(EventWaitHandle activationEvent)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        _notificationStore = new NotificationStore(AppPaths.DataDirectory);
        _ledger = new NotificationLedger(_notificationStore.Load());

        _logger = new SanitizedFileLogger(AppPaths.DataDirectory);
        _logger.Info("application_started");
        _monitor = new CodexQuotaMonitor(logger: _logger);

        _popup = new QuotaPopupForm();
        _popup.UpdateStatus(_status);
        _ = _popup.Handle;
        _resetNotification = new ResetNotificationForm();
        _ = _resetNotification.Handle;
        _resetNotification.Dismissed += () =>
        {
            if (_selectedScope is null) return;
            _ledger.Dismiss();
            SaveNotifications();
            _resetNotification.ShowNotice(_ledger.State.Pending.FirstOrDefault());
        };
        _statusHandler = status => RunOnUiThread(() => ApplyStatus(status));
        _monitor.StatusChanged += _statusHandler;

        _refreshItem = new ToolStripMenuItem("Refresh now", null, async (_, _) => await RefreshNowAsync());
        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = false,
            Checked = SafeIsStartupEnabled()
        };
        _startupItem.Click += (_, _) => ToggleStartup();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_refreshItem);
        menu.Items.Add(new ToolStripMenuItem("Open Usage Dashboard", null, (_, _) => OpenUsageDashboard()));
        menu.Items.Add(new ToolStripMenuItem("Pro API credit setup…", null, async (_, _) => await EditBillingAsync()));
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("About", null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _currentIcon = TrayIconFactory.Create(null, Color.FromArgb(117, 124, 132));
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "Codex quota unavailable",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ShowPopup();
            }
        };

        _activationTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _activationTimer.Tick += (_, _) =>
        {
            if (activationEvent.WaitOne(0))
            {
                ShowPopup();
            }
        };
        _activationTimer.Start();
        Application.Idle += ShowOnFirstIdle;

        _monitor.Start();
        _billingConfiguration = _billingStore.Load();
        _billingNotification.Dismissed += () =>
        {
            if (_billingEditing || _billingConfiguration is not { } value || value.Pending.Count == 0) return;
            value.Pending.RemoveAt(0);
            SaveBilling();
            _billingNotification.ShowNotice(value.Pending.FirstOrDefault());
        };
        _billingTimer.Tick += async (_, _) => await RefreshBillingAsync();
        _billingTimer.Start();
        _ = RefreshBillingAsync();
    }

    private void ShowOnFirstIdle(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= ShowOnFirstIdle;
        ShowPopup();
    }

    private void RunOnUiThread(Action action)
    {
        if (_isExiting || _popup.IsDisposed || !_popup.IsHandleCreated)
        {
            return;
        }

        try
        {
            _popup.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (_isExiting || _popup.IsDisposed)
        {
        }
    }

    private void ShowPopup()
    {
        if (_isExiting)
        {
            return;
        }

        try
        {
            _popup.UpdateStatus(_status);
            _popup.ShowNearCursor();
            _logger.Info("popup_shown");
        }
        catch (Exception exception)
        {
            _logger.Error("popup_show_failed", exception);
        }
    }

    private async Task RefreshNowAsync()
    {
        _refreshItem.Enabled = false;
        try
        {
            await _monitor.RefreshNowAsync();
            await RefreshBillingAsync();
        }
        finally
        {
            if (!_isExiting)
            {
                _refreshItem.Enabled = true;
            }
        }
    }

    private void ApplyStatus(MonitorStatus status)
    {
        if (_isExiting)
        {
            return;
        }

        _status = status;
        _popup.UpdateStatus(status);
        UpdateIcon(status);

        if (status.State == MonitorConnectionState.Connected && status.Snapshot is { } snapshot)
        {
            if (!_ledger.SelectAccount(snapshot.AccountScope))
            {
                _selectedScope = null;
                _resetNotification.ShowNotice(null);
                return;
            }
            _selectedScope = snapshot.AccountScope;
            foreach (var bucket in snapshot.Buckets)
            {
                if (bucket.Primary is null && bucket.Secondary is null) continue;
                var used = new[] { bucket.Primary?.UsedPercent, bucket.Secondary?.UsedPercent }
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .DefaultIfEmpty(0m)
                    .Max();
                var crossed = _ledger.Thresholds.Observe(bucket.Id, used);
                if (crossed is not null)
                {
                    _notifyIcon.BalloonTipTitle = "Codex quota alert";
                    _notifyIcon.BalloonTipText = $"{bucket.DisplayName} has reached {crossed}% used.";
                    _notifyIcon.BalloonTipIcon = crossed >= 90 ? ToolTipIcon.Warning : ToolTipIcon.Info;
                    _notifyIcon.ShowBalloonTip(5_000);
                }
            }

            NotifyQuotaReplenishments(snapshot);
            NotifyNewResetCredits(snapshot);

            _ledger.State.Thresholds = new Dictionary<string, int>(_ledger.Thresholds.Snapshot(), StringComparer.OrdinalIgnoreCase);
            SaveNotifications();
            _resetNotification.ShowNotice(_ledger.State.Pending.FirstOrDefault());
        }
        else
        {
            _selectedScope = null;
            _resetNotification.ShowNotice(null);
        }
    }

    private void SaveNotifications()
    {
        if (!_notificationStore.Save(_ledger.State)) _logger.Warning("notification_state_save_failed");
    }

    private async Task RefreshBillingAsync()
    {
        if (_isExiting || _billingBusy || _billingEditing) return;
        var configuration = _billingConfiguration;
        if (configuration is null)
        {
            _popup.ApiBillingSummary = null;
            _popup.ApiBillingDetail = null;
            _popup.UpdateStatus(_status);
            return;
        }
        var generation = _billingGeneration;
        _billingBusy = true;
        try
        {
            _billingNotification.ShowNotice(configuration.Pending.FirstOrDefault());
            var now = DateTimeOffset.UtcNow;
            if (now < configuration.Settings.CycleEndUtc && !configuration.ExpiryWarned
                && configuration.Settings.CycleEndUtc - now <= TimeSpan.FromHours(24))
            {
                QueueBillingNotice(configuration, "Pro API credits expire soon", $"Configured expiry: {configuration.Settings.CycleEndUtc.LocalDateTime:M/d h:mm tt}. Unused credits do not roll over.");
                configuration.ExpiryWarned = true;
                SaveBilling();
                _billingNotification.ShowNotice(configuration.Pending.FirstOrDefault());
            }
            if (!ShowExpiredBillingCycle(configuration, now))
            {
                var snapshot = await new ApiBillingClient(_billingHttp).GetAsync(configuration.ApiKey, configuration.Settings, now, _billingLifetime.Token);
                if (_isExiting || generation != _billingGeneration) return;
                // A request may complete on the other side of the configured expiry boundary.
                if (ShowExpiredBillingCycle(configuration, DateTimeOffset.UtcNow))
                {
                    SaveBilling();
                    _billingNotification.ShowNotice(configuration.Pending.FirstOrDefault());
                    return;
                }
                _billingSnapshot = snapshot;
                _logger.Info("api_billing_refresh_succeeded");
                var band = ApiCreditWarnings.Band(snapshot.RemainingPercent);
                if (band > configuration.WarningBand)
                {
                    QueueBillingNotice(configuration, "Pro API credits running low", $"Estimated ${snapshot.Remaining:0.00} remaining ({snapshot.RemainingPercent:0.0}%). Billing usage may be delayed.");
                    configuration.WarningBand = band;
                }
                _popup.ApiBillingSummary = $"Pro API: ≈${snapshot.Remaining:0.00} left ({snapshot.RemainingPercent:0.0}%)";
                _popup.ApiBillingDetail = $"Renews {configuration.Settings.CycleEndUtc.LocalDateTime:M/d h:mm tt} (local, configured)\nReported ${snapshot.Spent:0.00} / ${configuration.Settings.Allowance:0.00}; checked {snapshot.UpdatedAt.LocalDateTime:t}";
            }
            SaveBilling();
            _billingNotification.ShowNotice(configuration.Pending.FirstOrDefault());
        }
        catch (OperationCanceledException) when (_isExiting) { }
        catch (Exception exception)
        {
            if (_isExiting || generation != _billingGeneration) return;
            _logger.Error("api_billing_refresh_failed", exception);
            if (ShowExpiredBillingCycle(configuration, DateTimeOffset.UtcNow))
            {
                SaveBilling();
                _billingNotification.ShowNotice(configuration.Pending.FirstOrDefault());
                return;
            }
            _popup.ApiBillingSummary = "Pro API credits: update unavailable";
            _popup.ApiBillingDetail = _billingSnapshot is { } old
                ? $"Last estimate ≈${old.Remaining:0.00} ({old.RemainingPercent:0.0}%) at {old.UpdatedAt.LocalDateTime:g}\nCheck the Admin key and organization in API credit setup."
                : "Check the Admin key, organization and cycle in API credit setup.";
        }
        finally
        {
            _billingBusy = false;
            if (!_isExiting)
            {
                _popup.UpdateStatus(_status);
                if (generation != _billingGeneration) _ = RefreshBillingAsync();
            }
        }
    }

    private bool ShowExpiredBillingCycle(ApiBillingConfiguration configuration, DateTimeOffset now)
    {
        if (now < configuration.Settings.CycleEndUtc) return false;
        _billingSnapshot = null;
        _popup.ApiBillingSummary = "Pro API credits: renewal due";
        _popup.ApiBillingDetail = "Confirm the new grant in Pro API credit setup.";
        if (!configuration.CycleEndWarned)
        {
            QueueBillingNotice(configuration, "Pro API credit cycle ended", "Your configured credit cycle has ended. Confirm the new grant and dates in Pro API credit setup.");
            configuration.CycleEndWarned = true;
        }
        return true;
    }

    private async Task EditBillingAsync()
    {
        if (_billingEditing) return;
        _billingEditing = true;
        _billingGeneration++;
        _billingNotification.ShowNotice(null);
        try
        {
            while (_billingBusy && !_isExiting) await Task.Delay(50);
            if (_isExiting) return;
            if (ApiBillingSetup.Run(_billingStore))
            {
                _billingConfiguration = _billingStore.Load();
                _billingSnapshot = null;
                _popup.ApiBillingSummary = null;
                _popup.ApiBillingDetail = null;
            }
        }
        finally { _billingEditing = false; }
        await RefreshBillingAsync();
    }

    private static void QueueBillingNotice(ApiBillingConfiguration configuration, string title, string message)
    {
        var notice = new PendingNotice(title, message);
        if (!configuration.Pending.Contains(notice) && configuration.Pending.Count < 16) configuration.Pending.Add(notice);
    }

    private void SaveBilling()
    {
        if (_billingConfiguration is not { } configuration) return;
        try { _billingStore.Save(configuration); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { _logger.Error("api_billing_state_save_failed", ex); }
    }

    private void NotifyNewResetCredits(QuotaSnapshot snapshot)
    {
        var detection = _ledger.Credits.Observe(
            snapshot.ResetCredits,
            snapshot.AvailableResetCredits);
        if (!detection.HasNew)
        {
            return;
        }

        var (title, message) = BuildNewResetNotification(detection, snapshot, DateTimeOffset.Now);
        _ledger.Enqueue(title, message);
        _logger.Info("new_reset_credits_detected", detection.TotalNew.ToString());
    }

    private void NotifyQuotaReplenishments(QuotaSnapshot snapshot)
    {
        var replenishments = _ledger.Quota.Observe(snapshot.Buckets);
        if (replenishments.Count == 0)
        {
            return;
        }

        var includedBucketId = snapshot.IncludedBucket?.Id;
        var title = replenishments.Any(item => string.Equals(item.BucketId, includedBucketId, StringComparison.OrdinalIgnoreCase))
            ? "Codex quota replenished"
            : "Quota replenished";
        var message = string.Join("; ", replenishments.Select(item =>
        {
            var window = QuotaFormatting.WindowLabel(item.WindowDurationMins);
            var label = string.Equals(item.BucketId, includedBucketId, StringComparison.OrdinalIgnoreCase)
                ? window
                : $"{item.BucketDisplayName} {window}";
            return $"{label}: {item.RemainingPercent:0}% remaining";
        }));

        _ledger.Enqueue(title, message);
        _logger.Info("quota_replenishment_detected", replenishments.Count.ToString());
    }

    private static (string Title, string Message) BuildNewResetNotification(
        ResetCreditDetection detection,
        QuotaSnapshot snapshot,
        DateTimeOffset now)
    {
        var title = detection.TotalNew == 1
            ? "New Codex reset available"
            : $"{detection.TotalNew} new Codex resets available";

        if (detection.TotalNew == 1 && detection.NewCredits.FirstOrDefault() is { } credit)
        {
            var expiry = LowercaseFirst(QuotaFormatting.ExpiryCountdownWithDate(credit.ExpiresAtUtc, now));
            return (title, $"{credit.Title} added — {expiry}.");
        }

        var available = snapshot.AvailableResetCredits ?? snapshot.ResetCredits.Count;
        return (title, $"You now have {available} resets available.");
    }

    private void UpdateIcon(MonitorStatus status)
    {
        var used = status.State == MonitorConnectionState.Connected
            ? status.Snapshot?.IncludedBucket?.Primary?.UsedPercent
                ?? status.Snapshot?.IncludedBucket?.Secondary?.UsedPercent
            : null;
        var color = used switch
        {
            null => Color.FromArgb(117, 124, 132),
            >= 90m => Color.FromArgb(211, 47, 47),
            >= 70m => Color.FromArgb(224, 160, 0),
            _ => Color.FromArgb(33, 150, 83)
        };

        decimal? remaining = used is null ? null : Math.Clamp(100m - used.Value, 0m, 100m);
        var nextIcon = TrayIconFactory.Create(remaining, color);
        _notifyIcon.Icon = nextIcon;
        _currentIcon?.Dispose();
        _currentIcon = nextIcon;
        _notifyIcon.Text = TruncateTooltip(BuildTooltip(status));
    }

    private static string BuildTooltip(MonitorStatus status)
    {
        var window = status.Snapshot?.IncludedBucket?.Primary
            ?? status.Snapshot?.IncludedBucket?.Secondary;
        if (status.State != MonitorConnectionState.Connected || window is null)
        {
            return status.State == MonitorConnectionState.SignedOut
                ? "Codex: sign in to view quota"
                : "Codex: quota unavailable";
        }

        var remaining = decimal.Round(window.RemainingPercent, 0, MidpointRounding.AwayFromZero);
        var reset = LowercaseFirst(QuotaFormatting.ResetCountdown(window.ResetsAtUtc, DateTimeOffset.Now));
        return $"{remaining:0}% remaining -- {reset}";
    }

    private static string TruncateTooltip(string text) => text.Length <= 63 ? text : text[..60] + "…";

    private static string LowercaseFirst(string value) => value.Length == 0
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];

    private void ToggleStartup()
    {
        try
        {
            var next = !_startupManager.IsEnabled;
            _startupManager.SetEnabled(next);
            _startupItem.Checked = next;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            MessageBox.Show(
                "CodexTray could not update your per-user startup setting.",
                "CodexTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private bool SafeIsStartupEnabled()
    {
        try
        {
            return _startupManager.IsEnabled;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void OpenUsageDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UsageDashboardUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            try
            {
                Process.Start(new ProcessStartInfo(FallbackUsageUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                MessageBox.Show(
                    $"Open this address in your browser:\n{FallbackUsageUrl}",
                    "CodexTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }

    private static void ShowAbout()
    {
        MessageBox.Show(
            $"CodexTray {Application.ProductVersion.Split('+')[0]}\n\nDisplays Codex quota using the official Codex App Server.\nOptional Pro API billing uses your locally entered Admin key, encrypted for your Windows user.",
            "About CodexTray",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    protected override void ExitThreadCore()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _billingTimer.Stop();
        _billingTimer.Dispose();
        _billingLifetime.Cancel();
        _billingHttp.Dispose();
        _billingNotification.Dispose();
        Application.Idle -= ShowOnFirstIdle;
        _activationTimer.Stop();
        _activationTimer.Dispose();
        _notifyIcon.Visible = false;
        _monitor.StatusChanged -= _statusHandler;
        _monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _resetNotification.Dispose();
        _popup.Dispose();
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        base.ExitThreadCore();
    }
}
