param([string]$Destination = 'artifacts/publication-source')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destinationPath = [IO.Path]::GetFullPath((Join-Path $root $Destination))
if (-not $destinationPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Destination must be inside this checkout.'
}
if (Test-Path -LiteralPath $destinationPath) { throw 'Destination already exists; choose a fresh directory.' }
Push-Location $root
try {
    # Only deliberately tracked/staged paths are candidates. Untracked files never enter
    # a public export, even when a developer has forgotten an ignore rule.
    $files = @(git -c core.quotepath=false ls-files --cached)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate source files.' }
    foreach ($relative in $files) {
        if ($relative -eq 'docs/COMMAND_CENTER_STOP.md') { continue } # private integration design
        if ($relative -match '(^|/)(\.git|artifacts|app|bin|obj|TestResults)/' -or
            $relative -match '(^|/)(\.env($|\.)|auth\.json$)|\.(dat|log|pfx|p12|key|pem)$') {
            throw "A private/generated path is tracked; review it before exporting: $relative"
        }
        $source = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $ancestor = Get-Item -LiteralPath $source
        while ($ancestor.FullName -ne $root) {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked source path requires manual review.' }
            $ancestor = if ($ancestor -is [IO.FileInfo]) { $ancestor.Directory } else { $ancestor.Parent }
            if ($null -eq $ancestor) { throw 'Source path escaped checkout.' }
        }
        $target = [IO.Path]::GetFullPath((Join-Path $destinationPath $relative))
        if (-not $target.StartsWith($destinationPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe relative source path.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    Write-Output $destinationPath
    Write-Output 'Review and scan this snapshot before creating a fresh public history. Original private history is unchanged.'
}
finally { Pop-Location }
