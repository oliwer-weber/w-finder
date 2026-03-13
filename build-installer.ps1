# Build and package Quip installer
# Prerequisites: .NET 8 SDK, Inno Setup 6

$ErrorActionPreference = "Stop"

Write-Host "=== Building Quip (Release) ===" -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Verify main DLL exists
$dll = "bin\Release\net8.0-windows\Quip.dll"
if (-not (Test-Path $dll)) {
    Write-Host "ERROR: $dll not found after build." -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded: $dll" -ForegroundColor Green

# Find Inno Setup compiler
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

Write-Host ""
Write-Host "=== Compiling installer ===" -ForegroundColor Cyan
Write-Host "Using: $iscc"

& $iscc "installer\QuipSetup.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installer compilation failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Done! ===" -ForegroundColor Green
Write-Host "Installer: installer\output\QuipSetup.exe"
