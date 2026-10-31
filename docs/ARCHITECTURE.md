# Architecture

## Components

1. Program owns a session-local single-instance mutex/activation event and an exclusive per-user data file lock held for its full lifetime. This also prevents simultaneous state writers in different Windows sessions.
2. TrayApplicationContext dispatches monitor updates onto the WinForms thread and owns windows, icons, and notifications.
3. CodexQuotaMonitor launches an owned codex app-server process, initializes JSONL, and polls every 60 seconds.
4. AppServerProtocolSession correlates numeric request IDs and handles interleaved notifications. QuotaParser extracts bounded selected fields.
5. NotificationLedger owns account-scoped trackers and pending cards. NotificationStore persists them with current-user Windows DPAPI and temporary-file replacement.

Core holds parsing, protocol, tracking and identity decisions. The Windows app holds UI, encryption, startup registry and rendering. Tests include synthetic stdio/lifecycle, trackers, encrypted persistence and STA Windows UI checks. LiveSmoke is a separate optional authenticated diagnostic client.

## Account boundary

Each quota read is bracketed by account/read with refreshToken=false. Identity fingerprints must match before publishing. account/updated clears the displayed snapshot immediately and invalidates in-flight results. IDs/emails never enter logs or persisted state in plaintext. Unknown identity permits display but disables alerts; first verified account observation establishes baselines.

Only one account's persisted state is retained. A different verified fingerprint clears trackers and cards. Same-account restart restores pending cards and compares new samples to saved baselines. The fingerprint combines available ID/email, account type, plan and optional workspace routing; upstream identity changes may intentionally rebaseline. Identical returned identity across workspaces cannot distinguish them.

## Notifications

- Usage thresholds: Windows balloon tips at 70% and 90% used; unavailable windows do not count as zero.
- New credits: SHA-256 title/type/expiry fingerprints with multiplicities; count increases cover missing details. History is bounded to 2,048 fingerprints. Identical replacement metadata at unchanged count cannot prove a new credit.
- Replenishment: an increase of at least 15 percentage points; or an armed near-full rebound to at least 95% with at least a 3-point increase; or an upward rebound to at least 95% alongside an advanced reset timestamp. Near-full detection arms at 90% or lower and disarms after notification. Thus 94/95 oscillation does not alert, while 97/100 with a new reset timestamp does.
- Scheduled resets, corrections and applied banked resets can all replenish quota; the alert does not claim a banked reset was consumed.
- Cards stay until Dismiss, Escape or close; queue capped at 32, with overflow coalesced into one summary. Long cards scroll within screen bounds; cards arrive without stealing focus and can be activated for keyboard use.

## Resource and process boundaries

Protocol stdout lines are bounded while reading to 1 MiB; stderr lines to 16 KiB. Oversize/faulted streams, EOF and request timeouts terminate the session and trigger reconnect even when the child remains alive. A request deadline includes lock acquisition, writes, flushes and response wait. Backoff caps at 30 seconds; repetitive diagnostics are rate-limited. Parsing caps buckets at 64, credits at 256 and displayed strings at 256 characters.

Executable override must be an existing fully-qualified .exe; an invalid explicit override fails closed. PATH discovery skips relative entries. App-folder discovery still selects by modification time: only use trusted user-controlled installations. No signature/provenance claim is made.

Popup refresh disposes removed controls and owned fonts; icon drawing disposes clip regions. Shutdown closes child stdin, waits briefly, and can force-kill only its owned process tree. No external stop/control API is implemented.
