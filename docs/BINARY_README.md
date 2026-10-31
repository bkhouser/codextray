# CodexTray — local Windows x64 candidate

1. Extract the complete package into a stable folder you control.
2. Sign in to Codex Desktop or CLI with App Server support.
3. Run CodexTray.exe. This self-contained package includes .NET 10.0.14; no separate runtime installation is required.
4. Left-click the tray gauge to view quota. Right-click for refresh, dashboard, startup and Exit. Windows may hide the icon in the overflow menu.

The app is unsigned. Verify the supplied SHA-256 checksum and trust the source before running it; do not bypass Windows security warnings blindly.

Quit any older CodexTray instance before trying this candidate, otherwise single-instance handling opens the older app. This candidate does not automatically update an existing installation or startup entry. Disable Start with Windows in the old copy before moving to a new location, then re-enable it from this copy if desired.

Quota is read-only. Reset/replenishment cards persist in encrypted current-user storage until dismissed. All displayed reset times use Windows local time. See PRIVACY.md, SECURITY.md and docs/TROUBLESHOOTING.md for behavior and limitations.

Unofficial community utility, not affiliated with or endorsed by OpenAI. MIT license included. This is a locally validated candidate, not a signed or publicly released product. Clean-machine/high-DPI/multi-monitor acceptance remains outstanding.
