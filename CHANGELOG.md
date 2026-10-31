# Changelog

## 1.0.0 — 2026-10-31

- Designate the reviewed app as the first stable release for its documented scope; prior versions were local development releases.

- Protect per-user billing/notification state from simultaneous copies in different Windows sessions.
- End failed or canceled App Server initialization cleanly.
- Add public-source export safeguards, CodeQL, validated CI packaging with runtime license notices, synthetic screenshots, threat model and publication guidance.

## 0.2.0 — 2026-10-31

- Added optional Pro API allowance estimates, masked local Admin-key setup with DPAPI storage, five-minute spending updates, persistent low-balance/expiry alerts, and configured local renewal times. Credit grant details must be confirmed each cycle.

- Target .NET 10 LTS with SDK 10.0.403 and runtime floor 10.0.14; stage a self-contained Windows x64 build.
- Recover from malformed/oversized/failed App Server streams, EOF and hung requests, including blocked writes.
- Bound protocol input, parser collections, tracker history, and notification queue.
- Verify account scope around quota reads and invalidate stale in-flight results on account changes.
- Persist account-scoped tracker baselines and uncleared notifications with current-user Windows DPAPI.
- Correct identical-metadata reset additions and suppress near-full oscillation; use reset-time evidence for small full-quota rebounds.
- Make notifications adaptive, scrollable when necessary, and keyboard-dismissible.
- Dispose refreshed UI controls, fonts, and graphics regions.
- Require trusted absolute executable overrides/PATH entries; rebuild from source in start.bat.
- Add dependency lockfiles, pinned Windows CI, secret scanning, Dependabot, clean-source snapshot tooling and regression tests.
- Add MIT license, privacy/security policies, contributor, architecture, troubleshooting and release documentation.

No public release has been made.

## Initial local implementation

Windows tray gauge, compact quota/reset window, read-only local App Server integration, startup option, local-time reset display, persistent-on-screen notifications and synthetic tests.
