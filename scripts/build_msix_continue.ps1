# Continue MSIX build after partial clean - handle locked files gracefully
param()
$ErrorActionPreference = "Continue"
. "$PSScriptRoot/build_common.ps1"

$root = Get-Location
$packagingDir = "$root/packaging/msix"
$layoutDir = "$packagingDir/Layout"
$publishDir = "$root/publish"

# 1. Clean up - tolerate locked files
Write-Host "[Build] Cleaning up..." -ForegroundColor Gray
Clear-BuildArtifacts -Root $root -TolerateLocks

# 2. Build Binaries
Invoke-NativePublish "src/Clickra.CLI/Clickra.csproj" "$publishDir/cli"
Invoke-NativePublish "src/ClickraShell/ClickraShell.csproj" "$publishDir/shell"
Invoke-NativePublish "src/ClickraLauncher/ClickraLauncher.csproj" "$publishDir/launcher"
Invoke-FluentPublish -ExtraArgs "-p:WindowsPackageType=None"

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
Add-WindowsSdkToolsToPath
Add-VsInstallerToPath
Copy-AssemblyLayout -Root $root -PackagingDir $packagingDir -LayoutDir $layoutDir -PublishDir $publishDir -ExtraBinaries @("launcher/ClickraLauncher.exe")

# 4. Create & Sign MSIX
$msixPath = New-AndSignMsix -Root $root -PackagingDir $packagingDir -LayoutDir $layoutDir

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
