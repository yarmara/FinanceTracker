$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET 8 SDK was not found." -ForegroundColor Red
    exit 1
}

$out = Join-Path $PSScriptRoot "publish\framework-dependent"
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
    --self-contained false `
    -o $out

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Build completed: $out" -ForegroundColor Green
