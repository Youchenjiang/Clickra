[CmdletBinding()]
param(
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== Building Clickra Desktop (Avalonia 11 NativeAOT) ===" -ForegroundColor Cyan

$projectPath = Join-Path $repoRoot "src/Clickra.Desktop/Clickra.Desktop.csproj"
$outputDir = Join-Path $repoRoot "publish/desktop"

dotnet publish "$projectPath" -c Release -r win-x64 --self-contained true -o "$outputDir" -warnaserror
if ($LASTEXITCODE -ne 0) {
    throw "Clickra.Desktop NativeAOT publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $outputDir "Clickra.Desktop.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected binary missing: $exePath"
}

$sizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
Write-Host "✅ Standalone NativeAOT desktop executable successfully produced!" -ForegroundColor Green
Write-Host "   Path: $exePath" -ForegroundColor White
Write-Host "   Size: $sizeMb MB (NFR limit: <= 35 MB)" -ForegroundColor White

if ($Launch) {
    Write-Host "Launching Clickra Desktop..." -ForegroundColor Cyan
    Start-Process $exePath
}
