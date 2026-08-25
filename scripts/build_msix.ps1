# Clickra MSIX Build Script
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/build_common.ps1"

$root = Get-Location
$packagingDir = "$root/packaging/msix"
$layoutDir = "$packagingDir/Layout"
$publishDir = "$root/publish"

Add-WindowsSdkToolsToPath
Add-VsInstallerToPath

Write-Host "[Build] Starting Clickra MSIX Build..." -ForegroundColor Cyan

# 1. Clean up
Clear-BuildArtifacts -Root $root

# 2. Build Binaries
Invoke-NativePublish "src/Clickra.CLI/Clickra.csproj" "$publishDir/cli"
Invoke-NativePublish "src/ClickraShell/ClickraShell.csproj" "$publishDir/shell"
Invoke-FluentPublish

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
Copy-AssemblyLayout -Root $root -PackagingDir $packagingDir -LayoutDir $layoutDir -PublishDir $publishDir

# 4. Create & Sign MSIX
$msixPath = New-AndSignMsix -Root $root -PackagingDir $packagingDir -LayoutDir $layoutDir

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
