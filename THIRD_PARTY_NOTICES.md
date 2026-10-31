# Third-party components

CodexTray's own source is covered by LICENSE (MIT). Ordinary application builds have no third-party NuGet package dependencies; they use Microsoft .NET and Windows Forms. Microsoft .NET's licenses do not become the app's MIT license.

Self-contained Windows packages redistribute the .NET and Windows Desktop runtimes. scripts/New-ReleasePackage.ps1 copies the actual restored runtime packages' LICENSE and THIRD-PARTY-NOTICES texts into the distribution. Retain DOTNET-LICENSE.txt, DOTNET-THIRD-PARTY-NOTICES.txt and WINDOWSDESKTOP-LICENSE.txt with binary distributions. Framework-dependent builds require users to obtain the runtime separately.

Development/test dependencies include Microsoft.NET.Test.Sdk, xUnit v3, xunit.runner.visualstudio and coverlet.collector, plus their transitive dependencies. Their versions and package hashes are recorded in tests/CodexTray.Tests/packages.lock.json. These test binaries are not included in the Windows app package. NuGet packages carry their own licenses and must be reviewed when dependencies change.

CI downloads Gitleaks and uses first-party GitHub Actions/CodeQL. They run as build/security tools and are not distributed as part of the app. Codex App Server is discovered on the user's machine and is neither bundled nor relicensed by CodexTray.
