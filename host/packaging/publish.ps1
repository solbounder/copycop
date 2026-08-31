param(
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string[]]$RuntimeIdentifiers = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$releaseRoot = Join-Path $repositoryRoot "host\release"
$buildRoot = Join-Path $repositoryRoot "build\host-release"

& (Join-Path $PSScriptRoot "build-brand-assets.ps1")
if (-not $SkipTests) {
    $testProject = Join-Path $repositoryRoot "tests\CopyCop.Cli.Tests\CopyCop.Cli.Tests.csproj"
    dotnet run --project $testProject -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$runtimeMap = @{
    "win-x64"   = @{ Package = "windows-x64"; Gui = "win-x64"; Cli = "win-x64" }
    "linux-x64" = @{ Package = "linux-x64";   Gui = "linux-x64"; Cli = "linux-x64" }
    "osx-x64"   = @{ Package = "macos-x64";   Gui = "osx-x64"; Cli = "osx-x64" }
    "osx-arm64" = @{ Package = "macos-arm64"; Gui = "osx-arm64"; Cli = "osx-arm64" }
}

foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
    Write-Output "Publishing $runtimeIdentifier"
    $guiPublish = Join-Path $buildRoot "$runtimeIdentifier\gui"
    $cliPublish = Join-Path $buildRoot "$runtimeIdentifier\cli"
    New-Item -ItemType Directory -Path $guiPublish, $cliPublish -Force | Out-Null

    $singleFileProperties = @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    $guiProject = Join-Path $repositoryRoot "host\CopyCop.Gui\CopyCop.Gui.csproj"
    dotnet publish $guiProject -c Release -r $runtimeIdentifier --self-contained true @singleFileProperties -o $guiPublish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $cliProject = Join-Path $repositoryRoot "host\copycop-cli\copycop-cli.csproj"
    dotnet publish $cliProject -c Release -r $runtimeIdentifier --self-contained true @singleFileProperties -o $cliPublish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $targetIsWindows = $runtimeIdentifier -eq "win-x64"
    $targetIsMac = $runtimeIdentifier.StartsWith("osx-", [StringComparison]::Ordinal)
    $guiName = if ($targetIsWindows) { "CopyCop.exe" } else { "CopyCop" }
    $cliName = if ($targetIsWindows) { "copycop-cli.exe" } else { "copycop-cli" }
    $guiSource = Join-Path $guiPublish $guiName
    $cliSource = Join-Path $cliPublish $cliName
    if (-not (Test-Path -LiteralPath $guiSource) -or -not (Test-Path -LiteralPath $cliSource)) {
        throw "Expected single-file output is missing for $runtimeIdentifier"
    }

    $mapping = $runtimeMap[$runtimeIdentifier]
    $packageDirectory = Join-Path $releaseRoot "packages\$($mapping.Package)"
    $guiDirectory = Join-Path $releaseRoot "gui\$($mapping.Gui)"
    $cliDirectory = Join-Path $releaseRoot "cli\$($mapping.Cli)"
    New-Item -ItemType Directory -Path $packageDirectory, $guiDirectory, $cliDirectory -Force | Out-Null

    Copy-Item -LiteralPath $guiSource -Destination (Join-Path $guiDirectory $guiName) -Force
    Copy-Item -LiteralPath $cliSource -Destination (Join-Path $cliDirectory $cliName) -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "RELEASE-README.md") -Destination (Join-Path $packageDirectory "RELEASE-README.md") -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "host\THIRD-PARTY-NOTICES.md") -Destination (Join-Path $packageDirectory "THIRD-PARTY-NOTICES.md") -Force

    if ($targetIsMac) {
        $contentsDirectory = Join-Path $packageDirectory "CopyCop.app\Contents"
        $macOsDirectory = Join-Path $contentsDirectory "MacOS"
        $resourcesDirectory = Join-Path $contentsDirectory "Resources"
        New-Item -ItemType Directory -Path $macOsDirectory, $resourcesDirectory -Force | Out-Null
        Copy-Item -LiteralPath $guiSource -Destination (Join-Path $macOsDirectory "CopyCop") -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Info.plist") -Destination (Join-Path $contentsDirectory "Info.plist") -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "CopyCop.icns") -Destination (Join-Path $resourcesDirectory "CopyCop.icns") -Force
        Copy-Item -LiteralPath $cliSource -Destination (Join-Path $packageDirectory "copycop-cli") -Force
    } else {
        Copy-Item -LiteralPath $guiSource -Destination (Join-Path $packageDirectory $guiName) -Force
        Copy-Item -LiteralPath $cliSource -Destination (Join-Path $packageDirectory $cliName) -Force
        if ($runtimeIdentifier -eq "linux-x64") {
            Copy-Item -LiteralPath (Join-Path $repositoryRoot "host\linux\99-copycop.rules") -Destination (Join-Path $packageDirectory "99-copycop.rules") -Force
        }
    }
}

Write-Output "CopyCop packages published under $releaseRoot"
