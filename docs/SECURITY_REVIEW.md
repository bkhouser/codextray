# Publication and security review

Latest public-preparation review: version 1.0.0, October 31, 2026 — see [PUBLIC_REVIEW.md](PUBLIC_REVIEW.md) for the cross-session/state and initialization fixes, clean-history boundary, 127-test validation and remaining hosted/binary gates. The material below preserves the earlier findings and remediation history.

Reviewed and remediated October 29, 2026. Original baseline: a0a63c5 plus uncommitted quota-replenishment work. Scope: app/core sources, launcher, manifest, dependencies, tests, documentation, all three original reachable commits and commit metadata.

This is a source/dependency review with synthetic validation, not a penetration-test certification or audit of upstream Codex internals.

## Findings and disposition

| Finding | Priority | Local remediation |
| --- | --- | --- |
| R1: .NET 9 nearing end of support, old runtime | High release | Migrated all projects to .NET 10; SDK 10.0.403 verified against Microsoft's SHA-512; runtime floor/bundled runtime 10.0.14. |
| R2: personal usage and author metadata in old Git history | High publication | Current reports sanitized; clean source snapshot prepared without .git/history. Original private history preserved, not suitable for direct public push. |
| R3: malformed numeric fields, closed readers and hung writes could stall monitoring | Medium | JSON-kind guards; streaming bounds; whole-request deadlines; session failure drives reconnect even with a living child. Synthetic failure/recovery tests. |
| R4: removed controls/fonts and clip regions not disposed | Medium | Explicit disposal on refresh/window teardown and scoped clip region. Repeated-refresh UI regression verifies old controls are disposed. |
| R5: identical credit metadata collapses new credits | Medium | Multiplicity-aware hashed identities, bounded history and count reconciliation; count-only baselines absorb later detail revelation. Regression coverage. |
| R6: session-only alerts and cross-account comparisons | Medium | DPAPI-encrypted baselines/pending cards; verified hashed account scope around each read; account-change generation guards; signed-out/unknown scope hides cards. |
| R7: 94/95 oscillation repeats alerts and small full rebound is missed | Medium | Hysteresis and reset-timestamp evidence. 94/95 oscillation and 97/100 new-window regressions. Heuristic limitations remain explicit. |
| R8: unbounded input/state, weak override validation, fixed inaccessible cards, stale launcher, no release checks | Lower | Protocol/parser/state/queue bounds; absolute .exe override and PATH validation; adaptive keyboard-dismissible card; rebuild-on-launch; lockfiles, pinned CI, Dependabot and clean-source tooling. |

Final review additionally corrected semantic workspace-JSON canonicalization and validation of corrupt nested persisted state (null quota observations/case-colliding threshold keys).

## Verified controls

- asInvoker; no app elevation, no shell-built App Server command.
- Only initialize/initialized, account/read (refreshToken=false) and account/rateLimits/read requests. No purchases, resets, login/logout or filesystem-edit RPC.
- No application listener, telemetry transport, authentication-file reads, raw-response persistence or server-provided URL navigation.
- Account identity is hashed; raw identity is not logged/persisted. Pending state is encrypted for the Windows user.
- Per-user startup path quoted. Persistence uses temporary-file replacement.
- Stdout lines limited to 1 MiB and stderr to 16 KiB during reading; diagnostics rate-limited. Parser/state sizes capped.
- Logs store reviewed event metadata and exception types, not exception messages or raw response text. They can contain installation paths and are not guaranteed anonymous.
- Windows CI definition is read-only, SHA-pinned and does not use a signed-in account. It has not run on GitHub because publication is not authorized.

## Evidence

See [SMOKE_TEST_REPORT.md](../SMOKE_TEST_REPORT.md) for final build/test totals and artifact evidence. The local run uses SDK 10.0.403/runtime 10.0.14, isolated output, synthetic data, a rendered notification check, and encrypted-state tests. All four projects' current NuGet advisory checks report no vulnerable dependencies. Production app/core have no external package references for ordinary builds; publish tooling includes the SDK's locked ILLink package.

Gitleaks 8.30.1, verified against the official archive checksum and GitHub asset digest, scanned original history with no secret findings. Clean source is scanned separately. This does not make historical private usage or personal author metadata appropriate for publication; those are omitted from the source snapshot, not erased from the private repository. No actual reset was consumed.

[Microsoft servicing baseline](https://dotnet.microsoft.com/en-us/download/dotnet/10.0); [OpenAI App Server protocol](https://learn.chatgpt.com/docs/app-server).

## Remaining limits and publication gates

October 30 addition: optional Pro API billing introduces a separate Admin credential and fixed-endpoint read-only HTTPS access. Key entry is local/masked and saved with DPAPI only after confirmation. A focused review corrected single-instance/configuration races, nested-dialog notification mutation, offline expiry warnings, and inconsistent key validation. See API_BILLING.md for scope and estimation limitations; the earlier 67-test baseline predates this addition.

- No full penetration test, upstream/OS audit, or guarantee that all vulnerabilities are absent.
- Only available account fields can separate scopes; indistinguishable workspace identity cannot be resolved locally. Account/plan identity changes intentionally rebaseline.
- Identical credit replacements at unchanged counts are inherently ambiguous; bounded history may eventually forget old credits. Replenishment is heuristic and can include scheduled resets/corrections.
- DPAPI does not defend against same-user malware; save failure/corruption can lose pending state. Error logs identify save failure.
- Trusted executable discovery still has no signature/provenance enforcement. Owned-child shutdown retains a force-kill fallback; the proposed external cooperative stop contract is not implemented.
- Candidate binaries are unsigned. Clean-machine, high-DPI/multi-monitor and screen-reader acceptance remain release-day gates.
- A clean public initial history, GitHub security settings, hosted CI validation and explicit publication approval are still required. Nothing was uploaded, pushed, or publicly created.

MIT license approved by the owner. Code findings are remediated locally; external publication gates are deliberately not represented as completed.
