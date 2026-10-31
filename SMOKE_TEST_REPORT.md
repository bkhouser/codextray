# Verification record

## October 31, 2026 — version 1.0.0 public preparation

- Fresh independent review found and corrected cross-session persistent-state ownership and canceled initialization cleanup; public export/package tooling was hardened. See docs/PUBLIC_REVIEW.md.
- Both the working tree and exact exported source passed all 127 synthetic tests; exported all-project Release build had zero warnings/errors. Locked normal and self-contained win-x64 restores/publish passed.
- Current Microsoft release metadata reports SDK 10.0.403 and runtime 10.0.14, matching the candidate. NuGet audit reports no known vulnerable packages in all four projects.
- Verify and CodeQL workflows pass checksum-verified actionlint 1.7.12. Hosted GitHub CI/CodeQL are not executed locally and remain unverified until the first authorized push.
- Official Gitleaks 8.30.1 scans are redacted. Public source/history are checked independently, plus targeted private-email, machine-path and key-literal checks. No detected secrets are included in the reviewed export.
- README screenshot is generated from synthetic quota/reset fixtures and visually inspected. Live account values, credentials, installed app, toolchain, ignored outputs, internal integration design and original Git history are excluded.
- Public history uses one fresh initial commit with the verified GitHub noreply identity and an annotated v1.0.0 tag. Windows package tooling includes app/runtime licenses and SHA-256 manifests. The runtime includes Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.14.
- No public GitHub repository, remote upload, release publication, production restart or credential inspection was performed. Binary signing, clean-machine/high-DPI/multi-monitor/screen-reader acceptance and repository security settings remain explicit publication/release gates.

## October 31, 2026 — version 0.2.0 local commit

- Full post-review synthetic suite: 122 passed, zero failures/skips. Added regressions for caller-scoped cancellation, ambiguous partial writes, temporarily missing credit details, exact partial-day billing query timestamps, and oversized popup content.
- All-project Release build: zero warnings/errors. Locked win-x64 self-contained publish succeeded; executable and Core assembly versions verified as 0.2.0.0.
- Current dependency audit: no known vulnerable NuGet packages in all four projects. Staged-source Gitleaks scan: no findings. Text/whitespace checks passed.
- Review findings and dispositions: docs/REVIEW_0.2.0.md. No outstanding blocking findings from this pass. Billing remains an estimate, not a verified grant balance.
- Scope is a local commit and annotated v0.2.0 tag. No remote or GitHub repository is configured; no push/publication is performed. Running installation and locally saved credentials are not replaced by the commit workflow.

## October 30, 2026 Pro API billing addition

- Full synthetic suite: 110 passed, zero failures/skips. Covers strict Costs API pagination/schema/cancellation, credential-shape validation, DPAPI credential/alert persistence, existing protocol regressions and Windows setup UI.
- Local self-contained .NET 10.0.14 publish succeeded. Masked setup screen rendered and inspected with synthetic data. Gitleaks source scan found no secrets.
- Focused review corrected configuration ownership/races, local expiry notifications during network failure, and consistent key validation. No live Admin key was inspected or included in tests.
- The feature is an estimate from configured grant details and reported costs, not a direct API credit-balance/expiration feed. Live verification requires the user to enter the key and grant details locally.

## October 29, 2026 local .NET 10 release candidate

- SDK: Microsoft .NET 10.0.403 Windows x64 archive, SHA-512 checked against official release metadata.
- Runtime floor: .NET / Windows Desktop 10.0.14; self-contained win-x64 publish succeeds.
- Release build: all projects, zero warnings/errors, isolated artifacts/release-validation output.
- Tests: 67 passed, zero failures/skips; includes protocol parsing/bounds/EOF/timeout recovery, account-switch races, duplicate/count-only reset handling, near-full hysteresis, canonical identity, queue bounds, encrypted persistence/corruption, and Windows control disposal/keyboard button checks.
- Both ordinary and win-x64 publish lockfiles restored in locked mode.
- NuGet advisory query: no vulnerable packages reported across all four projects, including transitive dependencies.
- Gitleaks 8.30.1: checksum/digest-verified official executable; all three original Git commits and the clean source snapshot scanned with no secret findings.
- Clean source snapshot independently completed locked restore, zero-warning Release build, all 67 tests and locked self-contained publish. Included-framework metadata confirms Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.14.
- Synthetic notification rendered and visually inspected: wrapped text and visible Dismiss button, no clipped content at the tested display scale.
- Source-only snapshot excludes .git/history; old private smoke data and personal commit email remain only in the preserved original history.
- Release paths are compiler-mapped to /_/; Release debug metadata is disabled and packages omit PDB files. The final executable was checked for this machine's personal build-path prefix with no match.

No real banked reset was consumed. No installed tray executable, running instance, startup setting, or system .NET installation was replaced. No public repository, remote, push, tag or upload was created. Build artifacts and downloaded validation tools remain ignored locally.

## Limits

The staged production executable has not been launched against the real account in this pass. Clean-machine installation/startup, multiple displays, high DPI, screen-reader usability, Authenticode signing and hosted GitHub CI remain release gates. Tests and secret scans are evidence, not a guarantee of absence of every vulnerability.

## Earlier live verification

The initial implementation completed a local authenticated App Server check and reported readable quota/bucket/reset data. Personal numeric balances, plan labels and expiration timestamps were intentionally removed from this current report. The earlier report is historical evidence, not validation of the new .NET 10 candidate.
