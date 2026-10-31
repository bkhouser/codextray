using CodexTray.Core;

namespace CodexTray;

internal sealed class ApiBillingSettingsForm : Form
{
    private readonly TextBox _key = new() { UseSystemPasswordChar = true, MaxLength = 4096, Width = 400 };
    private readonly TextBox _organization = new() { Width = 400, MaxLength = 128 };
    private readonly NumericUpDown _allowance = new() { DecimalPlaces = 2, Maximum = 100000, Width = 150 };
    private readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "M/d/yyyy h:mm tt", Width = 250 };
    private readonly DateTimePicker _end = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "M/d/yyyy h:mm tt", Width = 250 };
    private readonly CheckBox _confirm = new() { AutoSize = true, Text = "Amount and dates match my current API credit grant" };
    private readonly ApiBillingConfiguration? _existing;
    public ApiBillingConfiguration? Result { get; private set; }
    public bool Remove { get; private set; }

    public ApiBillingSettingsForm(ApiBillingConfiguration? existing, string storagePath)
    {
        _existing = existing;
        Text = "Pro API credit monitoring";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 700);
        Shown += (_, _) => Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height - 32);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
        void Label(string text) => panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(430,0), Margin = new Padding(0,8,0,4) });
        Label("Optional: Admin keys have broad organization privileges. Enter one only if you trust this app. Codex quota monitoring needs no key. This monitor estimates allowance from reported spending; it cannot verify grants or renewals.");
        Label(existing is null ? "Admin API key" : "Admin API key (leave blank to keep saved key)");
        panel.Controls.Add(_key);
        Label("API organization ID (the organization receiving your Pro credits)");
        panel.Controls.Add(_organization);
        Label("Actual credit grant for this cycle (USD; first grant may be prorated)");
        panel.Controls.Add(_allowance);
        Label("Credit cycle starts — Windows local time"); panel.Controls.Add(_start);
        Label("Credits expire / next renewal — Windows local time"); panel.Controls.Add(_end);
        Label("Use the exact dates from billing. If only a date is provided, the time you enter is an estimate. Confirm the next grant each cycle; unused credits do not roll over.");
        panel.Controls.Add(_confirm);
        Label("Save & connect stores this key encrypted for your Windows user at:\n" + storagePath);
        Label("Alerts: 25%, 10%, 5%, exhausted, and within 24 hours of expiry. Balances are estimates and billing data may lag.");
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var save = new Button { Text = "Save && connect", AutoSize = true };
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var remove = new Button { Text = "Remove setup", AutoSize = true, Enabled = existing is not null };
        remove.Click += (_, _) => { Remove = true; DialogResult = DialogResult.OK; };
        buttons.Controls.AddRange([save, cancel, remove]); panel.Controls.Add(buttons);
        Controls.Add(panel); AcceptButton = save; CancelButton = cancel;
        _start.Value = existing?.Settings.CycleStartUtc.LocalDateTime ?? DateTime.Now;
        _end.Value = existing?.Settings.CycleEndUtc.LocalDateTime ?? DateTime.Now.AddMonths(1);
        _organization.Text = existing?.Settings.OrganizationId ?? "";
        _allowance.Value = Math.Clamp(existing?.Settings.Allowance ?? 0, 0, _allowance.Maximum);
    }

    private void Save()
    {
        try
        {
            if (!_confirm.Checked) throw new ArgumentException("Confirm the grant amount and billing dates before connecting.");
            var start = LocalTime(_start.Value);
            var end = LocalTime(_end.Value);
            var settings = new ApiBillingSettings(_organization.Text.Trim(), _allowance.Value, start, end);
            settings.Validate();
            if (start > DateTimeOffset.Now || end <= DateTimeOffset.Now) throw new ArgumentException("Enter the current active credit cycle.");
            var key = string.IsNullOrWhiteSpace(_key.Text) ? _existing?.ApiKey ?? "" : _key.Text.Trim();
            if (!ApiBillingClient.IsValidAdminKey(key))
                throw new ArgumentException("Enter an Admin API key, not a project API key.");
            Result = new() { ApiKey = key, Settings = settings };
            if (_existing?.Settings == settings)
            {
                Result.Pending = _existing.Pending.ToList(); Result.WarningBand = _existing.WarningBand; Result.ExpiryWarned = _existing.ExpiryWarned; Result.CycleEndWarned = _existing.CycleEndWarned;
            }
            _key.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private static DateTimeOffset LocalTime(DateTime value)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
            throw new ArgumentException("That local time is skipped or repeated by daylight saving. Choose an unambiguous time.");
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }
}
