# Troubleshooting

## Launch and update

Double-click start.bat, or start a published executable. The source launcher always builds before launch; it never silently falls back to an outdated binary after failure. If the executable is locked, exit CodexTray from its tray menu and retry. A second unchanged launch opens the existing window.

Source builds need the SDK selected by global.json (10.0.403, latestPatch). The local validation SDK under artifacts/toolchain is used by start.bat when present; it is not part of source snapshots. Framework-dependent binaries need .NET 10 Desktop Runtime 10.0.14 or newer supported patch. The staged self-contained x64 package includes that runtime.

When start.bat selects the local SDK, it also selects that SDK's runtime for the launched executable. The quota window sizes to content normally and scrolls only when the available display area cannot contain it.

Windows may hide the icon in the overflow menu; drag it onto the visible tray. Closing the quota window hides it; right-click the tray icon and choose Exit to stop the app.

## Quota unavailable

Sign in through Codex Desktop or CLI. Use a trusted codex.exe with app-server support. CODEXTRAY_CODEX_PATH, if set, must be an existing absolute .exe path; invalid overrides prevent fallback. PATH entries must be absolute. Do not point the app at downloaded/untrusted executables.

Refresh now serializes reads; metering can be delayed. Failed streams, closed stdout, oversized lines and timed-out requests reconnect automatically. Do not terminate unrelated Codex processes. Logs contain sanitized event codes, not raw responses.

## Notifications

The first verified account observation baselines silently. Subsequent restarts restore encrypted state and uncleared cards. Changes while closed can be detected from saved baselines. If account/read cannot identify the account, alerts are disabled to prevent cross-account comparisons.

Spending/expiry counts alone do not alert; quota must show a qualifying rebound. A 97% to 100% rebound alerts when the reset timestamp also advances. Small corrections without that evidence may intentionally not alert. See [architecture](ARCHITECTURE.md) for exact rules.

Cards appear at the primary display's lower-right working area. Dismiss, Escape or close advances the queue. Long text scrolls; later cards wait. Cards hide while disconnected or signed out and return after verification. Switching verified account scope discards the previous account's cards. The queue is bounded; overflow becomes a summary.

If persistence fails, inspect notification_state_save_failed in the local log. Deleting notifications.dat resets baselines and clears pending cards. Threshold balloons still depend on Windows notification settings. Never spend a real reset just to test this app.

## Startup and removal

Start with Windows writes the quoted current executable path to HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexTray. Disable it before moving/deleting the app, then enable from the new location. A staged build does not automatically replace the currently running build or its startup entry.

Disable startup, Exit, and remove application files to uninstall. See [PRIVACY.md](../PRIVACY.md) for optional state/log removal.

## Reports

Include version/commit, Windows build, runtime/SDK, launch method, and reproducible steps. Use synthetic account values. Review LiveSmoke output and logs locally before sharing.
