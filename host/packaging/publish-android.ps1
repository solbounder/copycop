param(
    [string]$OutputDirectory,
    [string]$SigningKeyStore,
    [string]$SigningKeyAlias
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repositoryRoot "host\CopyCop.Android\CopyCop.Android.csproj"
$publishDirectory = Join-Path $repositoryRoot "build\android-release"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "host\release\android"
}

$properties = @(
    "-p:AndroidPackageFormats=apk",
    "-p:AndroidCreatePackagePerAbi=false"
)

if (-not [string]::IsNullOrWhiteSpace($SigningKeyStore)) {
    if ([string]::IsNullOrWhiteSpace($SigningKeyAlias)) {
        throw "Bei -SigningKeyStore muss auch -SigningKeyAlias angegeben werden."
    }
    if (([string]::IsNullOrWhiteSpace($env:COPYCOP_ANDROID_STORE_PASSWORD)) -or
        ([string]::IsNullOrWhiteSpace($env:COPYCOP_ANDROID_KEY_PASSWORD))) {
        throw "Für einen Release-Schlüssel müssen COPYCOP_ANDROID_STORE_PASSWORD und COPYCOP_ANDROID_KEY_PASSWORD gesetzt sein."
    }

    $resolvedKeyStore = (Resolve-Path -LiteralPath $SigningKeyStore).Path
    $properties += @(
        "-p:AndroidKeyStore=true",
        "-p:AndroidSigningKeyStore=$resolvedKeyStore",
        "-p:AndroidSigningKeyAlias=$SigningKeyAlias",
        "-p:AndroidSigningStorePass=env:COPYCOP_ANDROID_STORE_PASSWORD",
        "-p:AndroidSigningKeyPass=env:COPYCOP_ANDROID_KEY_PASSWORD"
    )
}

dotnet publish $project -c Release -f net8.0-android -o $publishDirectory @properties
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$signedApk = Get-ChildItem -LiteralPath $publishDirectory -Filter "*-Signed.apk" -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $signedApk) {
    throw "Der Android-Build hat kein signiertes APK erzeugt."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$destination = Join-Path $OutputDirectory "CopyCop-Android.apk"
Copy-Item -LiteralPath $signedApk.FullName -Destination $destination -Force
Write-Output "Android APK: $destination"
