# Version 0.2.0 review — October 31, 2026

Scope: all changes since a0a63c5, including .NET 10 migration, protocol recovery, account-scoped notifications, optional API billing, Windows UI, persistence, source launcher, documentation and CI. Review included an independent security/concurrency pass over Core and synthetic regressions. No real credit/reset was consumed and credentials were not inspected.

Corrected in this pass:

- Caller cancellation could terminate a healthy App Server session. Cancellation before a write or after a completed write now preserves unrelated requests; ambiguous interrupted writes and genuine timeouts still fail the transport safely.
- Temporarily missing reset details could reclassify known credits as unidentified and hide a replacement reset. Tracker bookkeeping now preserves known credit coverage across missing-detail samples.
- Source launcher selected the local SDK without selecting its newer runtime for the executable. It now sets the matching runtime environment and works when only the local SDK is available.
- Popup sizing could exceed small/scaled displays or throw when the window was wider than the work area. Widths scale with DPI, placement is bounded, and oversized content scrolls; normal content remains scrollbar-free.
- A billing response completing across the configured expiry boundary could show an expired allowance until the next poll. Expiry is checked again after both success and failure.
- Assembly, About and protocol-client versions now derive from the central 0.2.0 version rather than independent constants. Privacy text now explicitly includes the optional encrypted Admin credential.

The Costs API boundary concern was evaluated against its documented inclusive start_time/exclusive end_time query contract. The client sends exact Unix-second bounds and does not prorate returned daily buckets. Bucket metadata alone cannot establish whether the server included out-of-range costs. The allowance remains explicitly estimated from reported costs; data latency, other grants and uncertain manually supplied billing times remain documented limits, not assertions of an authoritative balance.

Validation: all 122 post-review tests passed; all-project Release build had zero warnings/errors; locked win-x64 self-contained publish succeeded and assembly/executable versions are 0.2.0.0. Dependency audit and staged Gitleaks scan found no vulnerable packages or detected secrets. No blocking findings remain from this pass. Additional evidence is recorded in SMOKE_TEST_REPORT.md. Live hosted CI, public distribution/signing and clean-machine acceptance are not part of this local commit. Original private history remains preserved; it must not be published directly. The owner requested a local commit and v0.2.0 tag only.
