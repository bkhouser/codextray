# Privacy and local data

CodexTray reads account identity and quota data through the local Codex App Server without reading auth.json, browser cookies, or Codex authentication tokens. Optional API billing uses only the Admin key you explicitly enter and save locally. The app has no telemetry sender or listening network server. App Server owns authentication and network traffic for the Codex connection under its own policies.

## Received and displayed data

Account type, ID or email, plan, and optional workspace routing identify an alert scope. Only a SHA-256 fingerprint of those identity fields is retained; raw email and account identifiers are not written by CodexTray. Quota percentages, reset timestamps, plan/bucket names, balances, and banked-reset details appear in the window and topmost cards. Screenshots and screen sharing can expose them. Opaque reset-credit IDs are discarded.

If App Server cannot provide usable identity, quota still displays but notifications are disabled. Fingerprinting uses the identity fields available from App Server; indistinguishable workspaces under the same returned identity cannot be separated reliably.

## Local files

Optional Pro API monitoring receives organization spending through the official Costs API. The locally entered Admin key, confirmed grant/cycle details, and pending billing alerts are encrypted in api-billing.dat (and an encrypted temporary file during saving), separately from Codex state. The key is used only for read-only HTTPS requests to api.openai.com. The billing organization is configured independently of the signed-in Codex account. Use Remove setup to delete it. See [API billing](docs/API_BILLING.md).

Under %LOCALAPPDATA%\CodexTray:

- notifications.dat: current account fingerprint, minimal tracker baselines, hashed credit fingerprints with multiplicities, threshold bands, and up to 32 pending cards. Windows DPAPI encrypts this file for the current Windows user. It can contain quota values and notification text after decryption.
- notifications.dat.tmp: encrypted temporary file, possibly left by interrupted replacement.
- codextray.log and codextray.log.previous: bounded diagnostic events, exception type names, and selected metadata. Logs rotate around one megabyte each.
- settings.json/settings.json.tmp: legacy threshold settings from older builds; no longer read or updated by the current app.
- .instance.lock: an empty lifetime lock file protecting saved state from concurrent copies in different Windows sessions; it contains no credentials or account data. A leftover file does not prevent restart after the owning process exits.

Pending cards survive normal restart and reboot. Only the currently selected account scope is saved: selecting another verified account replaces previous account state. During sign-out, disconnect, or unknown identity, cards are hidden until identity is verified again. File corruption, another Windows user, or save failure can prevent recovery. The log records notification_state_save_failed if saving fails.

DPAPI is not protection from software already running as the same Windows user. Logs can include installation search paths/usernames and are not guaranteed anonymous. The logger is not a general secret scrubber. The app never uploads these files.

Windows can redirect LocalAppData for a packaged development host. The Windows startup option uses one current-user registry value.

## Sharing and removal

LiveSmoke prints real usage and executable paths; review and redact before sharing. Use synthetic bug-report data. Never share authentication files, raw responses, or unreviewed logs.

Disable Start with Windows, choose Exit, then delete application files. Optionally delete %LOCALAPPDATA%\CodexTray to remove all CodexTray state. This does not sign you out of Codex or remove Codex's own data. See [troubleshooting](docs/TROUBLESHOOTING.md).
