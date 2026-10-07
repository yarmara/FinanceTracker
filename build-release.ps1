$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET 8 SDK was not found." -ForegroundColor Red
    Write-Host "Install .NET 8 SDK and run this script again."
    exit 1
}

$version = & dotnet --version
Write-Host "Using .NET SDK: $version"

$out = Join-Path $PSScriptRoot "publish\win-x64"
if (-not (Test-Path $out)) {
    New-Item -ItemType Directory -Path $out | Out-Null
}
# Keep FinanceTracker_backup_*.zip files created by the application.
# dotnet publish overwrites the application files in place.
Get-ChildItem $out -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike "FinanceTracker_backup_*.zip" } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

& dotnet publish .\FinanceTracker.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Build completed:" -ForegroundColor Green
Write-Host (Join-Path $out "FinanceTracker.exe")
