# Threat model

CodexTray is an unelevated, local Windows desktop app. Its job is read-only quota display and local notifications. This model covers CodexTray, its file/process/network boundaries and publication tooling; it does not audit Windows or OpenAI's services.

| Asset or boundary | Controls | Remaining limits |
| --- | --- | --- |
| Codex identity and quota | Local stdio App Server, bracketed identity checks, account-generation invalidation, no raw-response logs | Upstream identity fields determine workspace separation; same returned identity is indistinguishable |
| Optional Admin key | Explicit masked local entry/save, current-user DPAPI, fixed HTTPS endpoint, no redirects/cookies, safe errors | Admin credentials retain broad organization privileges; same-user malware can access memory/DPAPI |
| Pending cards and billing setup | Encrypted files, temporary-file replacement, bounded validated state, lifetime per-user data lock across sessions | Corruption or disk failure can lose recovery; older versions must be closed before upgrade |
| Child stdout/stderr | Streaming line bounds, strict JSON-kind checks, finite requests, reconnect/backoff, rate-limited diagnostics | A trusted installed child can still act with the Windows user's permissions |
| Executable discovery | Absolute .exe override; absolute PATH entries; direct ArgumentList launch | No signature verification; never configure untrusted binaries or run elevated |
| API cost responses | Fixed api.openai.com endpoint, 15-second total deadline, bounded bodies/pages, strict USD/schema checks | Reported spending can lag; allowance is estimated from manually confirmed grant dates/amounts |
| UI | Bounded queues, DPI-aware placement, keyboard dismissal, no timer expiry of pending cards | Topmost cards and screenshots can reveal quota values; screen-reader/multi-monitor acceptance varies |
| App shutdown | Close stdin, wait, then stop only the owned child tree if necessary | No cooperative-only external stop guarantee; no remote-control listener exists |
| Source and releases | Clean public initial history, no keys/state/builds, redacted scans, locked dependencies, pinned least-privilege CI, runtime notices/checksums | Scanners cannot prove absence of all secrets/vulnerabilities; unsigned Windows builds have no publisher identity assurance |

Expected attacker inputs include malformed/oversized local protocol lines, stale responses during account changes, malformed encrypted state, unexpected HTTP data and accidental private files in an export. These must fail safely without displaying an invented fresh balance, executing server-supplied commands or exposing credentials.

A compromised Windows user or malicious selected Codex executable is outside the protection boundary. GitHub workflows do not use a signed-in account, run LiveSmoke, claim credits, consume resets or publish releases. Public repository owners must enable security reporting, push protection and branch protections after creating the repository.
