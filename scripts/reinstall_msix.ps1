# Clickra dev reinstall helper
# Replaces the manual Settings flow (force-stop / uninstall / install) for dev test
# cycles: stop -> remove -> install -> (optional) launch -> report version.
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File scripts/reinstall_msix.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/reinstall_msix.ps1 -Launch
#   powershell -ExecutionPolicy Bypass -File scripts/reinstall_msix.ps1 -RemoveOnly
#   powershell -ExecutionPolicy Bypass -File scripts/reinstall_msix.ps1 -NoStop   # test remove-while-running
param(
    [switch]$Launch,        # launch the app after install
    [switch]$NoStop,        # skip stopping processes first (test deployment auto-termination)
    [switch]$RemoveOnly,    # only remove, do not install
    [string]$MsixPath = ""  # path to the msix (default: Clickra.msix at repo root)
)

$ErrorActionPreference = "Stop"
$packageName = "Clickra"

if ([string]::IsNullOrWhiteSpace($MsixPath)) {
    $MsixPath = Join-Path (Get-Location) "Clickra.msix"
}
if (-not (Test-Path $MsixPath)) {
    throw "msix not found: $MsixPath"
}
$MsixPath = (Resolve-Path $MsixPath).Path

# 1. Stop related processes (default; skipped with -NoStop)
if (-not $NoStop) {
    Get-Process -Name "Clickra.Fluent", "ClickraLauncher", "Clickra" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 2. Remove existing package (deployment auto-terminates running processes)
$pkg = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if ($pkg) {
    Write-Host "[Remove] $($pkg.PackageFullName)" -ForegroundColor Gray
    Remove-AppxPackage -Package $pkg.PackageFullName
} else {
    Write-Host "[Remove] not installed, skipping" -ForegroundColor Gray
}

if ($RemoveOnly) {
    Write-Host "[Done] removed" -ForegroundColor Green
    return
}

# 3. Install
Write-Host "[Install] $MsixPath" -ForegroundColor Gray
Add-AppxPackage -Path $MsixPath
$pkg = Get-AppxPackage -Name $packageName
if (-not $pkg) { throw "package not found after install" }
Write-Host "[OK] installed v$($pkg.Version) @ $($pkg.InstallLocation)" -ForegroundColor Green

# 4. Launch (optional)
if ($Launch) {
    $aumid = $pkg.PackageFamilyName + "!App"
    Start-Process ("shell:AppsFolder\" + $aumid)
    Start-Sleep -Seconds 6
    $proc = Get-Process -Name "Clickra.Fluent", "Clickra" -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "[Launch] running: $($proc.Name -join ', ') (PID $($proc.Id -join ', '))" -ForegroundColor Green
    } else {
        Write-Host "[Launch] WARNING: no process detected" -ForegroundColor Yellow
    }
}
