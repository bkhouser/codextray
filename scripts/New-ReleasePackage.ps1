param([string]$PublishDirectory = 'artifacts/publish', [string]$OutputDirectory = 'artifacts/package')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = [IO.Path]::GetFullPath((Join-Path $root $PublishDirectory))
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must stay inside checkout.' }
if (Test-Path -LiteralPath $output) { throw 'Package output already exists; choose a fresh directory.' }
$exe = Join-Path $publish 'CodexTray.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Publish the Windows executable first.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$runtime = [string]$props.Project.PropertyGroup.RuntimeFrameworkVersion
$cacheLine = @(dotnet nuget locals global-packages --list --force-english-output) | Where-Object { $_ -match '^global-packages:\s*(.+)$' } | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or $cacheLine -notmatch '^global-packages:\s*(.+)$') { throw 'Cannot locate restored runtime licenses.' }
$cache = $Matches[1].Trim()
$notices = @{
    'DOTNET-LICENSE.txt' = "microsoft.netcore.app.runtime.win-x64/$runtime/LICENSE.TXT"
    'DOTNET-THIRD-PARTY-NOTICES.txt' = "microsoft.netcore.app.runtime.win-x64/$runtime/THIRD-PARTY-NOTICES.TXT"
    'WINDOWSDESKTOP-LICENSE.txt' = "microsoft.windowsdesktop.app.runtime.win-x64/$runtime/LICENSE"
}
foreach ($relative in $notices.Values) { if (-not (Test-Path -LiteralPath (Join-Path $cache $relative))) { throw 'Required runtime license missing; restore/publish the self-contained win-x64 target first.' } }
$package = Join-Path $output 'CodexTray'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination $package
foreach ($name in @('LICENSE','PRIVACY.md','SECURITY.md','THIRD_PARTY_NOTICES.md','CHANGELOG.md','SMOKE_TEST_REPORT.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package }
Copy-Item -LiteralPath (Join-Path $root 'docs/BINARY_README.md') -Destination (Join-Path $package 'README.md')
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $package -Recurse
# Public packages omit private integration design even when built from the private checkout.
$privateDesign = Join-Path $package 'docs/COMMAND_CENTER_STOP.md'
if (Test-Path -LiteralPath $privateDesign) { Remove-Item -LiteralPath $privateDesign }
foreach ($name in $notices.Keys) { Copy-Item -LiteralPath (Join-Path $cache $notices[$name]) -Destination (Join-Path $package $name) }
$binaryHash = (Get-FileHash -LiteralPath (Join-Path $package 'CodexTray.exe') -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS.txt'), "$binaryHash  CodexTray.exe`n")
$archive = Join-Path $output 'CodexTray-win-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $archive
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$archiveHash  CodexTray-win-x64.zip`n")
Write-Output $archive
