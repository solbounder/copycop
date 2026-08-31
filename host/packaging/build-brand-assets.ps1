param(
    [string]$MasterPath = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($MasterPath)) {
    $MasterPath = Join-Path $repositoryRoot "assets\branding\copycop-logo-master.png"
}
$MasterPath = (Resolve-Path -LiteralPath $MasterPath).Path

$brandingDirectory = Join-Path $repositoryRoot "assets\branding"
$guiAssetDirectory = Join-Path $repositoryRoot "host\CopyCop.Gui\Assets"
$packagingDirectory = Join-Path $repositoryRoot "host\packaging"
New-Item -ItemType Directory -Path $brandingDirectory, $guiAssetDirectory -Force | Out-Null

Add-Type -AssemblyName System.Drawing

function Save-ResizedPng {
    param(
        [System.Drawing.Image]$Source,
        [int]$Size,
        [string]$Path
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($Source, 0, 0, $Size, $Size)
        } finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
}

function Write-BigEndianUInt32 {
    param(
        [System.IO.BinaryWriter]$Writer,
        [uint32]$Value
    )

    $bytes = [System.BitConverter]::GetBytes($Value)
    if ([System.BitConverter]::IsLittleEndian) {
        [Array]::Reverse($bytes)
    }
    $Writer.Write($bytes)
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    "copycop-brand-assets-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    $source = [System.Drawing.Image]::FromFile($MasterPath)
    try {
        $sizes = @(16, 32, 48, 64, 128, 256, 512, 1024)
        $pngPaths = @{}
        foreach ($size in $sizes) {
            $path = Join-Path $temporaryDirectory "copycop-$size.png"
            Save-ResizedPng -Source $source -Size $size -Path $path
            $pngPaths[$size] = $path
        }

        Copy-Item -LiteralPath $pngPaths[512] -Destination (
            Join-Path $brandingDirectory "copycop-logo-512.png") -Force
        Copy-Item -LiteralPath $pngPaths[256] -Destination (
            Join-Path $guiAssetDirectory "copycop-logo.png") -Force

        $icoSizes = @(16, 32, 48, 64, 128, 256)
        $icoPath = Join-Path $guiAssetDirectory "copycop.ico"
        $icoStream = [System.IO.File]::Create($icoPath)
        try {
            $icoWriter = [System.IO.BinaryWriter]::new($icoStream)
            try {
                $icoWriter.Write([uint16]0)
                $icoWriter.Write([uint16]1)
                $icoWriter.Write([uint16]$icoSizes.Count)
                $offset = 6 + 16 * $icoSizes.Count
                $icoPayloads = @()
                foreach ($size in $icoSizes) {
                    $payload = [System.IO.File]::ReadAllBytes($pngPaths[$size])
                    $icoPayloads += ,$payload
                    $dimension = if ($size -ge 256) { 0 } else { $size }
                    $icoWriter.Write([byte]$dimension)
                    $icoWriter.Write([byte]$dimension)
                    $icoWriter.Write([byte]0)
                    $icoWriter.Write([byte]0)
                    $icoWriter.Write([uint16]1)
                    $icoWriter.Write([uint16]32)
                    $icoWriter.Write([uint32]$payload.Length)
                    $icoWriter.Write([uint32]$offset)
                    $offset += $payload.Length
                }
                foreach ($payload in $icoPayloads) {
                    $icoWriter.Write($payload)
                }
            } finally {
                $icoWriter.Dispose()
            }
        } finally {
            $icoStream.Dispose()
        }

        $icnsChunks = [ordered]@{
            "icp4" = 16
            "icp5" = 32
            "icp6" = 64
            "ic07" = 128
            "ic08" = 256
            "ic09" = 512
            "ic10" = 1024
        }
        $icnsPayloads = @()
        $icnsLength = 8
        foreach ($entry in $icnsChunks.GetEnumerator()) {
            $payload = [System.IO.File]::ReadAllBytes($pngPaths[$entry.Value])
            $icnsPayloads += [PSCustomObject]@{ Type = $entry.Key; Data = $payload }
            $icnsLength += 8 + $payload.Length
        }

        $icnsPath = Join-Path $packagingDirectory "CopyCop.icns"
        $icnsStream = [System.IO.File]::Create($icnsPath)
        try {
            $icnsWriter = [System.IO.BinaryWriter]::new($icnsStream)
            try {
                $icnsWriter.Write([System.Text.Encoding]::ASCII.GetBytes("icns"))
                Write-BigEndianUInt32 -Writer $icnsWriter -Value $icnsLength
                foreach ($chunk in $icnsPayloads) {
                    $icnsWriter.Write([System.Text.Encoding]::ASCII.GetBytes($chunk.Type))
                    Write-BigEndianUInt32 -Writer $icnsWriter -Value (8 + $chunk.Data.Length)
                    $icnsWriter.Write($chunk.Data)
                }
            } finally {
                $icnsWriter.Dispose()
            }
        } finally {
            $icnsStream.Dispose()
        }
    } finally {
        $source.Dispose()
    }
} finally {
    $resolvedTemporaryDirectory = (Resolve-Path -LiteralPath $temporaryDirectory).Path
    $temporaryRoot = [System.IO.Path]::GetTempPath().TrimEnd('\') + '\'
    $insideTemporaryRoot = $resolvedTemporaryDirectory.StartsWith(
        $temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)
    $hasExpectedName = ([System.IO.Path]::GetFileName($resolvedTemporaryDirectory)).StartsWith(
        "copycop-brand-assets-", [System.StringComparison]::Ordinal)
    if (-not $insideTemporaryRoot -or -not $hasExpectedName) {
        throw "Refusing to remove unexpected temporary directory: $resolvedTemporaryDirectory"
    }
    Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force
}

Write-Output "CopyCop brand assets generated from $MasterPath"
