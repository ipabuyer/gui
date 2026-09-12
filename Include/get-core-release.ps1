[CmdletBinding()]
param(
    [ValidatePattern('^$|^\d+\.\d+\.\d+$')]
    [string]$Version = '',

    [string]$OutputDir = $PSScriptRoot,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$architectures = @('amd64', 'arm64')
$latestReleaseApiUrl = 'https://api.github.com/repos/ipabuyer/IPAbuyer.Core/releases/latest'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ipabuyer-core-$([System.Guid]::NewGuid().ToString('N'))"
$stagedBinaries = @()

function Get-LatestReleaseVersion {
    Write-Host "Resolving the latest IPAbuyer.Core release from $latestReleaseApiUrl"
    $release = Invoke-RestMethod -Uri $latestReleaseApiUrl -Headers @{ 'User-Agent' = 'IPAbuyer core updater' }
    $tagName = [string]$release.tag_name
    if ($tagName -notmatch '^v(?<Version>\d+\.\d+\.\d+)$') {
        throw "Latest release tag has an unsupported format: $tagName"
    }

    return $Matches.Version
}

function Get-AssetSha256 {
    param(
        [Parameter(Mandatory)]
        [string]$ChecksumPath
    )

    # Checksum files follow the sha256sum line format: "<hash>  <filename>".
    $line = (Get-Content -LiteralPath $ChecksumPath -Raw -Encoding ASCII).Trim()
    if ($line -notmatch '^(?<Hash>[A-Fa-f0-9]{64})\s+\*?(?<Name>.+)$') {
        throw "Invalid SHA-256 checksum format: $ChecksumPath"
    }

    return $Matches.Hash.ToLowerInvariant()
}

function Test-PeMachine {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [ValidateSet('amd64', 'arm64')]
        [string]$Architecture
    )

    $expectedMachine = if ($Architecture -eq 'arm64') { 0xAA64 } else { 0x8664 }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        if ($stream.Length -lt 0x40) {
            return $false
        }

        # e_lfanew at 0x3C points to the PE signature; machine type follows at PE+4.
        $stream.Position = 0x3C
        $reader = New-Object System.IO.BinaryReader($stream)
        $peOffset = $reader.ReadInt32()
        if ($peOffset -le 0 -or ($peOffset + 6) -gt $stream.Length) {
            return $false
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            return $false
        }

        return $reader.ReadUInt16() -eq $expectedMachine
    }
    finally {
        $stream.Dispose()
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = Get-LatestReleaseVersion
    }

    $releaseBaseUrl = "https://github.com/ipabuyer/IPAbuyer.Core/releases/download/v$Version"
    Write-Host "Using IPAbuyer.Core v$Version"

    $resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
    [System.IO.Directory]::CreateDirectory($resolvedOutputDir) | Out-Null
    [System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null

    foreach ($architecture in $architectures) {
        $assetName = "ipabuyer-core-$Version-windows-$architecture.dll"
        $checksumName = "$assetName.sha256"
        $assetUrl = "$releaseBaseUrl/$assetName"
        $checksumUrl = "$releaseBaseUrl/$checksumName"
        $architectureTempDir = Join-Path $tempRoot $architecture
        $assetPath = Join-Path $architectureTempDir $assetName
        $checksumPath = Join-Path $architectureTempDir $checksumName
        $stagedPath = Join-Path $architectureTempDir "$assetName.staged"

        [System.IO.Directory]::CreateDirectory($architectureTempDir) | Out-Null

        Write-Host "Downloading $assetUrl"
        Invoke-WebRequest -Uri $assetUrl -OutFile $assetPath
        Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumPath

        $expectedAssetHash = Get-AssetSha256 -ChecksumPath $checksumPath
        $actualAssetHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualAssetHash -ne $expectedAssetHash) {
            throw "SHA-256 mismatch for $assetName. Expected $expectedAssetHash, got $actualAssetHash."
        }

        Write-Host "Verified asset SHA-256: $actualAssetHash"

        if (-not (Test-PeMachine -Path $assetPath -Architecture $architecture)) {
            throw "Downloaded DLL does not match the expected $architecture PE machine type: $assetPath"
        }

        Copy-Item -LiteralPath $assetPath -Destination $stagedPath
        $stagedBinaries += [PSCustomObject]@{
            Architecture    = $architecture
            AssetUrl        = $assetUrl
            AssetHash       = $actualAssetHash
            AssetName       = $assetName
            StagedPath      = $stagedPath
            DestinationPath = Join-Path $resolvedOutputDir "ipabuyer-core-windows-$architecture.dll"
        }
    }

    foreach ($binary in $stagedBinaries) {
        if ((Test-Path -LiteralPath $binary.DestinationPath) -and -not $Force) {
            throw "Destination already exists: $($binary.DestinationPath). Re-run with -Force to replace it."
        }
    }

    foreach ($binary in $stagedBinaries) {
        $destinationTempPath = "$($binary.DestinationPath).$([System.Guid]::NewGuid().ToString('N')).tmp"
        Copy-Item -LiteralPath $binary.StagedPath -Destination $destinationTempPath
        Move-Item -LiteralPath $destinationTempPath -Destination $binary.DestinationPath -Force

        $binaryHash = (Get-FileHash -LiteralPath $binary.DestinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "Installed $($binary.Architecture): $($binary.DestinationPath)"
        Write-Host "DLL SHA-256: $binaryHash"
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
