# Pro API allowance monitor

Open the tray menu → Pro API credit setup, or run CodexTray.exe --configure-api-billing. Enter your Admin key in the masked local field. The key never belongs in chat, logs or source control. Save & connect explicitly saves it encrypted for the current Windows user at %LOCALAPPDATA%\CodexTray\api-billing.dat. Cancel writes nothing. Remove setup deletes the saved key and billing warning state.

Enter the organization ID receiving your Pro API credit grant, the actual grant amount in USD, and current credit-cycle start and expiration/renewal times. Times are entered/displayed in your Windows local timezone. Copy grant details from billing; the first grant may be prorated. If billing provides only a date, a time entered here is an estimate, not a verified expiration timestamp.

The separate Pro API section shows estimated dollars and percent remaining, reported organization spending, last successful check, and configured renewal time. The tray gauge continues to represent Codex quota. API billing can work when Codex is disconnected, since its organization is configured separately.

The app reads the official Costs API on startup, Refresh now, and every five minutes. Estimated remaining = confirmed allowance minus reported organization-wide USD costs since the configured start, floored at zero and capped at the allowance. Costs can lag; other grants, refunds, exclusions or purchases can affect the actual balance. Daily cost buckets can make boundaries approximate. This is not an authoritative credit-grant ledger or a spending cap.

Persistent warnings fire once per configured cycle at 25%, 10%, 5%, zero, within 24 hours of expiry, and at cycle end. Skipped thresholds produce one current warning. Cards remain until dismissed, survive restart, and are separate from Codex reset cards. Errors show unavailable/stale data. Corrections do not re-arm previously announced thresholds.

After expiry, the app asks you to confirm the actual next grant and dates rather than assume renewal. Changing the confirmed organization/allowance/cycle resets billing cards and thresholds; rotating only the key preserves them.

Requests use only the fixed HTTPS api.openai.com Costs endpoint; redirects/cookies are disabled. Response size, pagination and duration are bounded. Logs never contain credentials or response bodies. Admin keys have broad organization privileges: protect and revoke this dedicated key when no longer needed. DPAPI does not protect against programs already running as your Windows user. The app performs no purchases, credit claims or billing mutations.

Sources: [Costs API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/costs), [Pro API credit benefit](https://developers.openai.com/api/docs/guides/chatgpt-api-credits).
