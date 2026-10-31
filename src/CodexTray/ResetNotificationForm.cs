using CodexTray.Core;

namespace CodexTray;

internal sealed class ResetNotificationForm : Form
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly FlowLayoutPanel _content;
    private readonly Button _dismiss;
    private readonly Font _titleFont = new("Segoe UI", 11, FontStyle.Bold);
    private readonly Font _bodyFont = new("Segoe UI", 10);
    private PendingNotice? _shown;
    private readonly bool _positionAtTop;
    public event Action? Dismissed;

    public ResetNotificationForm(bool positionAtTop = false)
    {
        _positionAtTop = positionAtTop;
        Text = "CodexTray notification";
        AccessibleName = Text;
        ShowInTaskbar = false;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        ClientSize = new Size(420, 180);
        _content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        _title = new Label { AutoSize = true, Font = _titleFont, ForeColor = Color.FromArgb(26,128,72) };
        _message = new Label { AutoSize = true, Font = _bodyFont, Margin = new Padding(0,12,0,12) };
        _dismiss = new Button { AutoSize = true, Text = "Dismiss", AccessibleName = "Dismiss notification", TabIndex = 0 };
        _dismiss.Click += (_, _) => Dismissed?.Invoke();
        _content.Controls.AddRange([_title, _message, _dismiss]);
        Controls.Add(_content);
        AcceptButton = _dismiss;
        CancelButton = _dismiss;
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Dismissed?.Invoke(); } };
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowNotice(PendingNotice? notice)
    {
        if (notice is null) { Hide(); _shown = null; return; }
        if (notice == _shown && Visible) return;
        _shown = notice;
        _title.Text = notice.Title;
        _message.Text = notice.Message;
        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        var width = Math.Min((int)(420 * DeviceDpi / 96f), area.Width - 32);
        _title.MaximumSize = _message.MaximumSize = new Size(Math.Max(100, width - 64), 0);
        _content.PerformLayout();
        var height = _title.Height + _message.Height + _dismiss.Height + 85;
        ClientSize = new Size(Math.Max(100,width), Math.Min(height, Math.Max(100,area.Height - 80)));
        Location = new Point(Math.Max(area.Left, area.Right - Width - 16), _positionAtTop ? area.Top + 16 : Math.Max(area.Top, area.Bottom - Height - 16));
        Show();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _titleFont.Dispose(); _bodyFont.Dispose(); }
    }
}
