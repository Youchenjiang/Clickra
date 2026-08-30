# Clickra MSIX Build Script
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/build_common.ps1"
$root = $script:Root; $packagingDir = $script:PackagingDir; $layoutDir = $script:LayoutDir; $publishDir = $script:PublishDir

Add-WindowsSdkToolsToPath
Add-VsInstallerToPath

Write-Host "[Build] Starting Clickra MSIX Build..." -ForegroundColor Cyan

# 1. Clean up
Clear-BuildArtifacts -Root $root

# 2. Build Binaries
Invoke-NativePublish "src/Clickra.CLI/Clickra.csproj" "$publishDir/cli"
Invoke-NativePublish "src/ClickraShell/ClickraShell.csproj" "$publishDir/shell"
Invoke-LauncherPublish -OutputDir "$publishDir/launcher"

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
Copy-AssemblyLayout -Root $root -PackagingDir $packagingDir -LayoutDir $layoutDir -PublishDir $publishDir

# 4. Create & Sign MSIX
$msixPath = New-AndSignMsix -Root $root -PackagingDir $packagingDir -LayoutDir $layoutDir

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
