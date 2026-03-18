param(
    [string]$Version = "2.3.0",
    [string]$OutputDir = ".\\Include",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$releaseTag = "v$Version"
$downloadBase = "https://github.com/majd/ipatool/releases/download/$releaseTag"
$targets = @(
    @{ Arch = "amd64"; BaseName = "ipatool-$Version-windows-amd64" },
    @{ Arch = "arm64"; BaseName = "ipatool-$Version-windows-arm64" }
)

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

foreach ($target in $targets) {
    $baseName = $target.BaseName
    $exeName = "$baseName.exe"
    $archiveName = "$baseName.tar.gz"
    $destination = Join-Path $OutputDir $exeName

    if ((Test-Path $destination) -and -not $Force) {
        Write-Host "Skip existing: $destination"
        continue
    }

    $exeUrl = "$downloadBase/$exeName"
    try {
        Write-Host "Downloading $exeUrl"
        Invoke-WebRequest -Uri $exeUrl -OutFile $destination -MaximumRedirection 10
        Write-Host "Saved: $destination"
        continue
    }
    catch {
        Write-Host "Direct .exe not found for $baseName, fallback to .tar.gz"
    }

    $archiveUrl = "$downloadBase/$archiveName"
    $tmpArchive = Join-Path $env:TEMP $archiveName
    $tmpExtractDir = Join-Path $env:TEMP ("ipatool-extract-" + [Guid]::NewGuid().ToString("N"))

    Write-Host "Downloading $archiveUrl"
    Invoke-WebRequest -Uri $archiveUrl -OutFile $tmpArchive -MaximumRedirection 10

    New-Item -ItemType Directory -Path $tmpExtractDir -Force | Out-Null
    tar -xzf $tmpArchive -C $tmpExtractDir

    # Different releases may package as ipatool.exe / ipatool / versioned exe name.
    $allExtractedFiles = Get-ChildItem -Path $tmpExtractDir -Recurse -File
    $extractedExe = $allExtractedFiles |
        Where-Object {
            $_.Name -ieq "ipatool.exe" -or
            $_.Name -ieq "ipatool" -or
            $_.Name -ieq $exeName
        } |
        Select-Object -First 1

    if (-not $extractedExe) {
        $extractedExe = $allExtractedFiles |
            Where-Object { $_.Name -match "^ipatool.*(\.exe)?$" } |
            Select-Object -First 1
    }

    if (-not $extractedExe) {
        $fileList = ($allExtractedFiles | Select-Object -ExpandProperty FullName) -join "`n"
        throw "ipatool binary not found in archive: $archiveUrl`nExtracted files:`n$fileList"
    }

    Copy-Item -Path $extractedExe.FullName -Destination $destination -Force
    Write-Host "Saved: $destination"

    Remove-Item -Path $tmpArchive -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $tmpExtractDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Done. Source tag: https://github.com/majd/ipatool/releases/tag/$releaseTag"

