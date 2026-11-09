param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot "..\release\packages\windows-x64"),
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\CopyCop")
)

# Installs the published Windows build to a fixed per-user location and keeps a single
# "CopyCop" Start menu entry pointing at it, so Windows search always finds the latest build.

$ErrorActionPreference = "Stop"
$SourceDirectory = (Resolve-Path $SourceDirectory).Path
$guiSource = Join-Path $SourceDirectory "CopyCop.exe"
$cliSource = Join-Path $SourceDirectory "copycop-cli.exe"
if (-not (Test-Path -LiteralPath $guiSource) -or -not (Test-Path -LiteralPath $cliSource)) {
    throw "Published Windows build not found in $SourceDirectory. Run publish.ps1 first."
}

$running = Get-Process -Name "CopyCop", "copycop-cli" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($InstallDirectory, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw "CopyCop is running from $InstallDirectory. Close it and run the install again."
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-Item -LiteralPath $guiSource -Destination (Join-Path $InstallDirectory "CopyCop.exe") -Force
Copy-Item -LiteralPath $cliSource -Destination (Join-Path $InstallDirectory "copycop-cli.exe") -Force
foreach ($doc in "RELEASE-README.md", "THIRD-PARTY-NOTICES.md", "copycop.html") {
    $docSource = Join-Path $SourceDirectory $doc
    if (Test-Path -LiteralPath $docSource) {
        Copy-Item -LiteralPath $docSource -Destination (Join-Path $InstallDirectory $doc) -Force
    }
}

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenu "CopyCop.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallDirectory "CopyCop.exe"
$shortcut.WorkingDirectory = $InstallDirectory
$shortcut.IconLocation = "$(Join-Path $InstallDirectory 'CopyCop.exe'),0"
$shortcut.Description = "CopyCop"
$shortcut.Save()

$commit = (git -C (Join-Path $PSScriptRoot "..\..") log -1 --format="%h %ad" --date=short 2>$null)
Write-Output "CopyCop installed to $InstallDirectory (build of $commit), Start menu entry: $shortcutPath"
