# Security policy

CodexTray 1.0.0 is designated as the first stable release for its documented scope, pending public publication. Security fixes are intended for the latest 1.0.x version; earlier 0.x versions were local development builds and are not supported public releases. The publication review is in [docs/SECURITY_REVIEW.md](docs/SECURITY_REVIEW.md); it records local remediations and remaining release gates, not a security certification.

## Reporting a vulnerability

Do not put credentials, personal quota data, raw App Server responses, or exploit details in a public issue.

When this repository is published, use GitHub's **Security → Report a vulnerability** if private vulnerability reporting has been enabled. If that option is absent, open a public issue containing only a request for a private reporting channel. Wait for a private channel before providing sensitive details. No private reporting endpoint or response-time guarantee is currently configured.

A useful private report includes the affected commit/version, Windows and .NET versions, preconditions, synthetic reproduction steps, expected and actual behavior, and impact.

## Trust model

CodexTray runs as the current Windows user and starts a local Codex App Server with that user's permissions. It does not sandbox or verify the signature of that executable. Only configure a trusted executable in CODEXTRAY_CODEX_PATH; use an absolute path and trusted PATH directories. Do not run CodexTray as administrator.

The app issues initialization, account-identity reads (refreshToken=false), and quota-read messages over redirected stdio. It has no network listener, login form, payment operation, or reset-consumption operation. Codex App Server owns authentication and may communicate with OpenAI and use its own configuration; its security and behavior are separate dependencies.

Incoming data is bounded JSON, not executable code. Malformed fields are rejected/ignored and failed sessions reconnect; see the review for limits. Pending notifications and baselines use current-user DPAPI encryption. A compromised local executable or the current Windows account is outside the app's protection boundary.

## Dependency maintenance

Optional Pro API billing stores a user-entered Admin key encrypted with current-user DPAPI and makes only read-only HTTPS Costs API requests. Setup requires an explicit local save; it shares single-instance ownership with the tray app. Configuration changes invalidate in-flight billing results. Redirects/cookies are disabled, errors never include keys or response bodies, and parsing/time/pagination are bounded. See [API billing](docs/API_BILLING.md) for its estimate and renewal limits.

The app and core have no third-party NuGet package references. Test dependencies still require auditing. A clean package audit does not audit the .NET runtime, Windows, or the installed Codex executable.

Before a release, use a supported and patched .NET runtime, audit transitive packages, review executable provenance, and complete the [release checklist](docs/RELEASING.md). The current baseline is .NET 10.0.14 with SDK 10.0.403. Self-contained packages must be rebuilt for servicing updates.
