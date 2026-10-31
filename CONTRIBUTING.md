# Contributing

Use Windows 11 and SDK 10.0.403 or a compatible servicing patch selected by global.json. Build and test from the repository root.

```powershell
dotnet restore CodexTray.slnx --locked-mode
dotnet build CodexTray.slnx -c Release --no-restore
dotnet test tests/CodexTray.Tests/CodexTray.Tests.csproj -c Release --no-build --no-restore
```

Exit your own running CodexTray before building over its executable, or use --artifacts-path with an isolated directory. Never stop unrelated Codex processes. Tests include Windows STA UI and current-user DPAPI checks with synthetic data; a test card may briefly appear. They do not use account credentials. LiveSmoke is optional, authenticated and private; do not run it in CI.

## Expectations

- Discuss larger changes first; keep pull requests focused.
- Preserve read-only quota behavior. Never add account mutations, purchases or reset consumption implicitly.
- Keep protocol/decision logic in Core; Windows UI, encryption and registry behavior in the app.
- Treat malformed/missing data as unavailable, not zero.
- Maintain account boundaries, bounded resources and safe shutdown.
- Use synthetic fixtures and regressions; do not log raw responses, identity, credentials or arbitrary exception messages.
- Replenishment is evidence of a rebound, not proof of banked-reset use.
- Dispose owned controls, fonts, graphics, icons, processes and cancellation resources.
- Update docs and changelog and state validation limitations honestly.

Dependency changes must include reviewed lockfiles. Normal build locks and win-x64 publish locks are separate. Runtime updates also require reviewing global.json and RuntimeFrameworkVersion, refreshing locks and rebuilding self-contained artifacts.

The included CI uses read-only default permissions, immutable Action revisions, locked restore, synthetic tests, runtime-servicing checks, NuGet audit and checksum-pinned Gitleaks. CodeQL has security-events write only in its analysis job. Dependabot checks weekly. Security scanners do not replace manual review; dependency audits do not cover the OS or external Codex binary.

Use the issue template for bugs and [SECURITY.md](SECURITY.md) for security concerns. Never attach private usage dumps or authentication files. Contributions are under the [MIT license](LICENSE); retain attribution and disclose third-party code/licenses. Be respectful and support disagreements with technical evidence.
