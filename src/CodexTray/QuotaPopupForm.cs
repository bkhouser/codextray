using CodexTray.Core;

namespace CodexTray;

internal sealed class QuotaPopupForm : Form
{
    private const int PopupClientWidth = 360;
    private const int ContentWidth = 320;
    private const int MaximumResetRows = 5;
    private const int MaximumAdditionalBuckets = 3;
    private readonly FlowLayoutPanel _content;
    private int _layoutContentWidth = ContentWidth;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? ApiBillingSummary { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? ApiBillingDetail { get; set; }

    public QuotaPopupForm()
    {
        Text = "Codex quota";
        ShowInTaskbar = false;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 249, 251);
        ClientSize = new Size(PopupClientWidth, 180);

        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = BackColor
        };
        Controls.Add(_content);
        FormClosing += (_, eventArgs) =>
        {
            if (eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        };
    }

    public void UpdateStatus(MonitorStatus status)
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        _layoutContentWidth = Math.Max(40, Math.Min((int)(ContentWidth * DeviceDpi / 96f), area.Width - _content.Padding.Horizontal - 32));
        _content.SuspendLayout();
        try
        {
            ClearContent();

            if (status.State != MonitorConnectionState.Connected || status.Snapshot is null)
            {
                AddOfflineStatus(status);
            }
            else
            {
                AddQuotaStatus(status.Snapshot, DateTimeOffset.Now);
            }
            if (ApiBillingSummary is { } summary)
            {
                AddDivider();
                AddLabel(summary, 10, FontStyle.Bold, Color.FromArgb(50, 55, 61), bottom: 3);
                if (ApiBillingDetail is { } detail) AddLabel(detail, 8.5f, FontStyle.Regular, Color.DimGray);
            }
        }
        finally
        {
            _content.ResumeLayout(performLayout: true);
        }

        ResizeToContent();
    }

    public void ShowNearCursor()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var x = Math.Clamp(Cursor.Position.X - Width / 2, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width));
        var y = Math.Max(workingArea.Top, Cursor.Position.Y - Height - 12);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    private void AddOfflineStatus(MonitorStatus status)
    {
        AddLabel(status.Message, 10, FontStyle.Regular, Color.FromArgb(80, 85, 92), bottom: 6);
        if (status.LastSuccessfulUpdate is { } previous)
        {
            AddLabel($"Last updated {previous.LocalDateTime:g}", 8.5f, FontStyle.Regular, Color.DimGray);
        }
    }

    private void AddQuotaStatus(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var included = snapshot.IncludedBucket;
        var includedWindow = included?.Primary ?? included?.Secondary;
        if (includedWindow is null)
        {
            AddLabel("Quota unavailable", 22, FontStyle.Bold, Color.FromArgb(80, 85, 92), bottom: 1);
            AddLabel("Reset time unavailable", 11, FontStyle.Regular, Color.DimGray, bottom: 5);
        }
        else
        {
            var remaining = decimal.Round(includedWindow.RemainingPercent, 0, MidpointRounding.AwayFromZero);
            AddLabel($"{remaining:0}% left", 27, FontStyle.Bold, UsageColor(includedWindow.UsedPercent), bottom: 0);
            AddLabel(QuotaFormatting.ResetCountdownWithDate(includedWindow.ResetsAtUtc, now), 12, FontStyle.Regular, Color.FromArgb(70, 75, 82), bottom: 6);
        }

        AddAdditionalBuckets(snapshot, included, now);
        AddDivider();

        AddLabel($"Credits: {QuotaFormatting.FormatBalance(snapshot.Credits)}", 9.5f, FontStyle.Regular, Color.FromArgb(50, 55, 61), bottom: 5);
        AddResetCredits(snapshot, now);
        AddLabel($"Updated {snapshot.UpdatedAt.LocalDateTime:t}", 8.5f, FontStyle.Regular, Color.DimGray);
    }

    private void AddAdditionalBuckets(QuotaSnapshot snapshot, QuotaBucket? included, DateTimeOffset now)
    {
        var additional = snapshot.Buckets
            .Where(bucket => !ReferenceEquals(bucket, included))
            .Take(MaximumAdditionalBuckets + 1)
            .ToList();
        if (additional.Count == 0)
        {
            return;
        }

        AddLabel("Other quotas", 9, FontStyle.Bold, Color.FromArgb(70, 75, 82), bottom: 2);
        foreach (var bucket in additional.Take(MaximumAdditionalBuckets))
        {
            var window = bucket.Primary ?? bucket.Secondary;
            var summary = window is null
                ? "unavailable"
                : $"{window.RemainingPercent:0}% left - {LowercaseFirst(QuotaFormatting.ResetCountdown(window.ResetsAtUtc, now))}";
            AddLabel($"{bucket.DisplayName}: {summary}", 8.5f, FontStyle.Regular, Color.FromArgb(70, 75, 82), bottom: 1);
        }

        if (additional.Count > MaximumAdditionalBuckets)
        {
            AddLabel($"+{snapshot.Buckets.Count - MaximumAdditionalBuckets - 1} more quota bucket(s)", 8.5f, FontStyle.Regular, Color.DimGray, bottom: 1);
        }
    }

    private void AddResetCredits(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var available = snapshot.AvailableResetCredits ?? snapshot.ResetCredits.Count;
        var label = available == 1 ? "1 reset available" : $"{available} resets available";
        AddLabel(label, 9.5f, FontStyle.Bold, Color.FromArgb(50, 55, 61), bottom: available > 0 ? 2 : 5);

        foreach (var reset in snapshot.ResetCredits.Take(MaximumResetRows))
        {
            AddLabel(
                $"- {reset.Title} - {LowercaseFirst(QuotaFormatting.ExpiryCountdownWithDate(reset.ExpiresAtUtc, now))}",
                8.5f,
                FontStyle.Regular,
                Color.FromArgb(70, 75, 82),
                bottom: 1);
        }

        var unlisted = Math.Max(0, available - Math.Min(snapshot.ResetCredits.Count, MaximumResetRows));
        if (unlisted > 0)
        {
            AddLabel($"+{unlisted} more", 8.5f, FontStyle.Regular, Color.DimGray, bottom: 3);
        }
        else if (available > 0)
        {
            SetBottomMargin(3);
        }
    }

    private void AddDivider()
    {
        _content.Controls.Add(new Panel
        {
            Width = _layoutContentWidth,
            Height = 1,
            BackColor = Color.FromArgb(220, 223, 228),
            Margin = new Padding(0, 4, 0, 7)
        });
    }

    private void AddLabel(string text, float size, FontStyle style, Color color, int bottom = 0)
    {
        _content.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(_layoutContentWidth, 0),
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            Margin = new Padding(0, 0, 0, bottom)
        });
    }

    private void ClearContent()
    {
        foreach (Control control in _content.Controls.Cast<Control>().ToArray())
        {
            var ownedFont = control is Label ? control.Font : null;
            control.Dispose();
            ownedFont?.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearContent();
        base.Dispose(disposing);
    }

    private void SetBottomMargin(int bottom)
    {
        if (_content.Controls.Count == 0)
        {
            return;
        }

        var control = _content.Controls[_content.Controls.Count - 1];
        control.Margin = new Padding(control.Margin.Left, control.Margin.Top, control.Margin.Right, bottom);
    }

    private void ResizeToContent()
    {
        _content.PerformLayout();
        var contentHeight = _content.Controls.Cast<Control>()
            .Sum(control => control.Height + control.Margin.Vertical)
            + _content.Padding.Vertical;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var border = SizeFromClientSize(Size.Empty);
        var maxHeight = Math.Max(40, area.Height - border.Height - 16);
        _content.AutoScroll = contentHeight > maxHeight;
        ClientSize = new Size(Math.Min((int)(PopupClientWidth * DeviceDpi / 96f), Math.Max(40, area.Width - border.Width - 16)), Math.Min(Math.Max(110, contentHeight), maxHeight));
    }

    private static Color UsageColor(decimal usedPercent) => usedPercent switch
    {
        >= 90m => Color.FromArgb(191, 45, 45),
        >= 70m => Color.FromArgb(176, 117, 0),
        _ => Color.FromArgb(26, 128, 72)
    };

    private static string LowercaseFirst(string value) => value.Length == 0
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];
}
