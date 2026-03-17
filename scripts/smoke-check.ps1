param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-MsixMetadata {
    $manifestPath = Join-Path $projectRoot "Package.appxmanifest"
    [xml]$manifest = Get-Content -Raw -Path $manifestPath

    $identityNode = Select-Xml -Xml $manifest -XPath "/*[local-name()='Package']/*[local-name()='Identity']" | Select-Object -First 1
    Assert-True ($null -ne $identityNode) "MSIX Identity node missing"

    $identity = $identityNode.Node
    Assert-True ($identity.Name -eq "IPAbuyer.IPAbuyer") "MSIX Name mismatch"
    Assert-True ($identity.Publisher -eq "CN=68F867E4-B304-4B5D-9818-31B1910E0771") "MSIX Publisher mismatch"
    Assert-True ($identity.Version -eq "2026.3.17.0") "MSIX Version mismatch"

    $resourceNode = Select-Xml -Xml $manifest -XPath "/*[local-name()='Package']/*[local-name()='Resources']/*[local-name()='Resource']" | Select-Object -First 1
    $language = if ($null -ne $resourceNode) { $resourceNode.Node.Language } else { "" }
    Assert-True ($language -eq "zh-CN") "MSIX Language mismatch"

    Write-Host "[OK] MSIX metadata verified"
}

function Build-And-VerifyPlatform {
    param(
        [string]$Platform,
        [string]$Rid
    )

    Write-Host "[INFO] Building $Platform / $Configuration"
    dotnet build IPAbuyer.csproj -c $Configuration -p:Platform=$Platform -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $Platform"
    }

    $outputRoot = Join-Path $projectRoot "bin\\$Platform\\$Configuration"
    Assert-True (Test-Path $outputRoot) "Output directory missing: $outputRoot"

    $ipatool = Get-ChildItem -Path $outputRoot -Recurse -Filter "ipatool.exe" -File | Select-Object -First 1
    Assert-True ($null -ne $ipatool) "ipatool.exe missing for platform $Platform"

    $ridMarker = Join-Path $outputRoot "net10.0-windows10.0.19041.0\\$Rid"
    Assert-True (Test-Path $ridMarker) "RID directory missing: $ridMarker"

    Write-Host "[OK] $Platform ipatool: $($ipatool.FullName)"
}

Test-MsixMetadata
Build-And-VerifyPlatform -Platform "x64" -Rid "win-x64"
Build-And-VerifyPlatform -Platform "ARM64" -Rid "win-arm64"

Write-Host "[DONE] Smoke verification passed"
