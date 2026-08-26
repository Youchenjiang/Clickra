# Continue MSIX build after partial clean - handle locked files gracefully
# Thin wrapper: cleans up with error tolerance, then delegates to build_msix.ps1.
param()
$ErrorActionPreference = "Continue"
. "$PSScriptRoot/build_common.ps1"

# Clean up tolerating locked files (the main build script will recreate everything)
Write-Host "[Build] Pre-cleanup (tolerating locked files)..." -ForegroundColor Gray
Clear-BuildArtifacts -Root $script:Root -TolerateLocks

# Delegate the actual build to the standard script
. "$PSScriptRoot/build_msix.ps1"
