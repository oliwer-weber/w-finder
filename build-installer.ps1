# Build and package the Quip installer for every supported Revit version.
# Prerequisites: .NET 8 SDK (Revit 2025/2026), .NET 10 SDK (Revit 2027+), Inno Setup 6.
#
# Supported versions are NOT listed here. They are read from the
#   #dim SupportedVersions[N] {...}
# line in installer\QuipSetup.iss, which is the single source of truth.
# To add Revit 2028, edit that one line in the .iss; nothing changes in this script.
#
# For each version:
#   - if C:\Program Files\Autodesk\Revit <YYYY>\RevitAPI.dll exists, run
#     dotnet build -c Release -p:RevitVersion=<YYYY>   (output: bin\Release\<YYYY>\)
#   - otherwise skip it with a warning (that version is left out of the installer).
# Any real build error stops the script. Zero successful builds also stops it.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$issPath = "installer\QuipSetup.iss"

# --- Read supported versions from the .iss --------------------------------
$dimLine = Select-String -Path $issPath -Pattern '^\s*#dim\s+SupportedVersions\[(\d+)\]\s*\{(.*)\}' | Select-Object -First 1
if (-not $dimLine) {
    Write-Host "ERROR: Could not find the '#dim SupportedVersions[N] {...}' line in $issPath." -ForegroundColor Red
    exit 1
}
$declaredSize = [int]$dimLine.Matches[0].Groups[1].Value
$supportedVersions = @([regex]::Matches($dimLine.Matches[0].Groups[2].Value, '"(\d{4})"') | ForEach-Object { $_.Groups[1].Value })
if ($supportedVersions.Count -eq 0 -or $supportedVersions.Count -ne $declaredSize) {
    Write-Host "ERROR: SupportedVersions in $issPath declares size $declaredSize but lists $($supportedVersions.Count) version(s)." -ForegroundColor Red
    Write-Host "Fix that line so the [N] matches the number of years in the braces." -ForegroundColor Red
    exit 1
}
Write-Host "Supported Revit versions: $($supportedVersions -join ', ')" -ForegroundColor Cyan

# --- Pick a dotnet that has the .NET 10 SDK --------------------------------
# A .NET 10 SDK builds every version (net8 and net10). It may be installed
# machine-wide (PATH dotnet) or user-local via dotnet-install.ps1, which does
# not touch PATH. Prefer whichever one actually has a 10.x SDK.
$dotnet = "dotnet"
$userDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$pathHas10 = (& dotnet --list-sdks 2>$null) -match '^10\.'
if (-not $pathHas10 -and (Test-Path $userDotnet)) {
    $dotnet = $userDotnet
}
Write-Host "Using dotnet: $dotnet" -ForegroundColor Cyan

# --- Build each version ----------------------------------------------------
$builtVersions = @()
foreach ($version in $supportedVersions) {
    $revitApi = "C:\Program Files\Autodesk\Revit $version\RevitAPI.dll"
    if (-not (Test-Path $revitApi)) {
        Write-Host ""
        Write-Host "WARNING: Revit $version not found ($revitApi). Skipping, it will NOT be in the installer." -ForegroundColor Yellow
        continue
    }

    Write-Host ""
    Write-Host "=== Building Quip for Revit $version (Release) ===" -ForegroundColor Cyan
    & $dotnet build -c Release "-p:RevitVersion=$version"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed for Revit $version!" -ForegroundColor Red
        exit 1
    }

    $dll = "bin\Release\$version\Quip.dll"
    if (-not (Test-Path $dll)) {
        Write-Host "ERROR: Build for Revit $version reported success but $dll was not found." -ForegroundColor Red
        exit 1
    }
    Write-Host "Build succeeded: $dll" -ForegroundColor Green
    $builtVersions += $version
}

if ($builtVersions.Count -eq 0) {
    Write-Host ""
    Write-Host "ERROR: No Revit version was built, nothing to package." -ForegroundColor Red
    exit 1
}

# --- Find Inno Setup compiler ---------------------------------------------
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

# Try PATH as fallback
if (-not $iscc) {
    $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
}

if (-not $iscc) {
    Write-Host "ERROR: Inno Setup 6 not found." -ForegroundColor Red
    Write-Host "Download it from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

# --- Compile installer -----------------------------------------------------
# PackageVersions tells the .iss to package only what was built just now, so a
# stale bin\Release\<YYYY>\ from an older run is never shipped by accident.
$packageList = $builtVersions -join ","

Write-Host ""
Write-Host "=== Compiling installer (Revit $($builtVersions -join ', ')) ===" -ForegroundColor Cyan
Write-Host "Using: $iscc"

& $iscc "/DPackageVersions=$packageList" $issPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installer compilation failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Installer: installer\output\QuipSetup.exe (Revit $($builtVersions -join ', '))"
