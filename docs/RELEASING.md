# Local release and publication checklist

Version 1.0.0 is the reviewed first stable public-source candidate. Its clean local repository has independent history; no GitHub repository, upload or public release is created by preparation. See PUBLISHING.md for the handoff. Preserve older private development history separately.

## Reproducible candidate

Use Windows 11 and SDK 10.0.403 (or a servicing patch selected by global.json):

```powershell
dotnet restore CodexTray.slnx --locked-mode
dotnet build CodexTray.slnx -c Release --no-restore
dotnet test tests/CodexTray.Tests/CodexTray.Tests.csproj -c Release --no-build --no-restore
dotnet list CodexTray.slnx package --vulnerable --include-transitive
dotnet publish src/CodexTray/CodexTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RestoreLockedMode=true --output artifacts/publish
```

The x64 single-file package includes .NET 10.0.14; rebuild promptly for runtime security servicing. Ordinary and RID-specific lockfiles are separate. If deliberately changing package/runtime/build properties, regenerate and review the appropriate lockfiles; do not disable locked restore in release CI.

Include LICENSE, README.md and PRIVACY.md with binaries, omit PDB/debug/source-path files from downloadable packages, and generate SHA-256 checksums. Release builds disable debug metadata to avoid embedded local PDB paths; Debug builds retain normal debugging support. Published binaries are not Authenticode-signed. The SDK version and runtime servicing are not covered by NuGet package advisory checks alone. Recheck [Microsoft servicing](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on release day.

## Clean public source

Run scripts/New-PublicationSnapshot.ps1 into a fresh ignored directory. It copies only deliberately tracked/staged current source/docs, without .git or historical commits; untracked files are excluded, credential/build paths and linked source paths are rejected, and private integration design is omitted. Stage intended new source/docs first. Review the inventory and run a maintained secret scanner (redacted output), dependency audit, and clean build. The exporter does not sanitize arbitrary future source contents.

Create any future public initial history from the reviewed snapshot, using an approved public/noreply author address. Preserve the original private repository. Its old smoke reports contain personal account usage and commit metadata even though the current file is sanitized. A clean secret scan does not detect every kind of personal data.

## Gates requiring an actual publication decision

- Choose owner/repository name and authorize publication.
- Create clean public initial history; review copyright attribution and metadata.
- Enable private vulnerability reporting, branch protection, and security notifications.
- Run the included least-privilege, commit-pinned Windows CI and weekly dependency checks on GitHub. CI never runs authenticated LiveSmoke.
- Perform clean-machine standard-user start/exit, second instance, startup toggle, moved-path, signed-out, reconnect, high-DPI/multiple-monitor and keyboard/screen-reader acceptance checks. Synthetic local checks are not a substitute for all configurations.
- Decide signing/distribution policy. Never ask users to bypass security warnings blindly.
- Record test/audit dates, exact runtime, checksums, limitations, version and tag.

The local implementation/security findings are addressed as recorded in SECURITY_REVIEW.md. Public release validation and hosting settings remain external gates, not claims of completed publication.
