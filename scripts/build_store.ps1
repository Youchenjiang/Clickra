# Clickra Store Submission Build
# Produces two MSIX files ready for Microsoft Store upload:
#   1. Clickra_Main.msix    — AOT only, zero dependency (< 50MB target)
#   2. Clickra_Fluent.msix   — WinUI 3 optional, carries Windows App Runtime
#
# Both packages share the same Publisher and are designed for related-set deployment.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/build_common.ps1"
$root = $script:Root; $packagingDir = $script:PackagingDir; $publishDir = $script:PublishDir

Add-WindowsSdkToolsToPath
Add-VsInstallerToPath

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Clickra Store Submission Build" -ForegroundColor Cyan
Write-Host " Main (AOT) + Optional (Fluent)" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# 1. Clean
Write-Host "[1/6] Cleaning..." -ForegroundColor Gray
Clear-BuildArtifacts -Root $root

# 2. Build AOT binaries
Write-Host "[2/6] Building NativeAOT (CLI + Shell + Launcher)..." -ForegroundColor Gray
Invoke-NativePublish "src/Clickra.CLI/Clickra.csproj" "$publishDir/cli"
Invoke-NativePublish "src/ClickraShell/ClickraShell.csproj" "$publishDir/shell"
Invoke-LauncherPublish -OutputDir "$publishDir/launcher"

# 3. Build Fluent
Write-Host "[3/6] Building Fluent GUI (framework-dependent)..." -ForegroundColor Gray
Invoke-NativePublish "src/Clickra.Fluent/Clickra.Fluent.csproj" "$publishDir/fluent"

# 4. Assemble Main Layout (AOT only)
Write-Host "[4/6] Assembling Main layout..." -ForegroundColor Gray
$mainLayout = "$packagingDir/Layout/StoreMain"
if (Test-Path $mainLayout) { Remove-Item -Recurse -Force $mainLayout }
New-Item -ItemType Directory -Path $mainLayout -Force | Out-Null

Copy-Item "$packagingDir/AppxManifest.xml" "$mainLayout/AppxManifest.xml"
Copy-Item -Recurse "$packagingDir/Assets" "$mainLayout/"
Copy-Item -Recurse "$packagingDir/Strings" "$mainLayout/"
Copy-Item "$publishDir/cli/Clickra.exe" "$mainLayout/"
Copy-Item "$publishDir/launcher/ClickraLauncher.exe" "$mainLayout/"
Copy-Item "$publishDir/shell/ClickraShell.dll" "$mainLayout/"
Copy-IconAssets -PackagingDir $packagingDir -LayoutDir $mainLayout

$mainRequired = @("Clickra.exe", "ClickraLauncher.exe", "ClickraShell.dll", "AppxManifest.xml")
$mainMissing = $mainRequired | Where-Object { -not (Test-Path "$mainLayout/$_") }
if ($mainMissing) { throw "Main layout incomplete: $($mainMissing -join ', ')" }

# 5. Assemble Optional Layout (Fluent only)
Write-Host "[5/6] Assembling Optional layout..." -ForegroundColor Gray
$optionalLayout = "$packagingDir/Layout/StoreOptional"
if (Test-Path $optionalLayout) { Remove-Item -Recurse -Force $optionalLayout }
New-Item -ItemType Directory -Path $optionalLayout -Force | Out-Null

Copy-Item "$packagingDir/AppxManifest.Fluent.xml" "$optionalLayout/AppxManifest.xml"
Copy-Item -Recurse "$packagingDir/Assets" "$optionalLayout/"

$fluentSource = "$publishDir/fluent"
$fluentExclude = @("*.pdb", "DirectML.dll", "onnxruntime.dll",
    "Microsoft.Windows.AI.MachineLearning.dll",
    "Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll")
Get-ChildItem $fluentSource -File |
    Where-Object {
        $name = $_.Name
        -not ($fluentExclude | Where-Object { $name -like $_ })
    } |
    ForEach-Object { Copy-Item $_.FullName "$optionalLayout/" }

if (Test-Path "src/Clickra.Fluent/Assets/AppIcon.png") {
    Copy-Item "src/Clickra.Fluent/Assets/AppIcon.png" "$optionalLayout/Assets/AppIcon.png"
}

$optionalRequired = @("Clickra.Fluent.exe", "AppxManifest.xml")
$optionalMissing = $optionalRequired | Where-Object { -not (Test-Path "$optionalLayout/$_") }
if ($optionalMissing) { throw "Optional layout incomplete: $($optionalMissing -join ', ')" }

# 6. Package and Sign
Write-Host "[6/6] Creating and signing MSIX packages..." -ForegroundColor Gray

# Main MSIX
$mainMsix = "$root/Clickra_Main.msix"
if (Test-Path $mainMsix) { Remove-Item $mainMsix }
& "makeappx.exe" pack /d "$mainLayout" /p $mainMsix /o
Assert-NativeSuccess

# Optional MSIX
$optionalMsix = "$root/Clickra_Fluent.msix"
if (Test-Path $optionalMsix) { Remove-Item $optionalMsix }
& "makeappx.exe" pack /d "$optionalLayout" /p $optionalMsix /o
Assert-NativeSuccess

# Sign both
Invoke-SignMsixPackage `
    -ManifestPath "$mainLayout/AppxManifest.xml" `
    -MsixPath $mainMsix `
    -PfxPath "$packagingDir/ClickraDev.pfx" `
    -DevCertScriptPath "$PSScriptRoot/setup/create_dev_cert.ps1"

Invoke-SignMsixPackage `
    -ManifestPath "$optionalLayout/AppxManifest.xml" `
    -MsixPath $optionalMsix `
    -PfxPath "$packagingDir/ClickraDev.pfx" `
    -DevCertScriptPath "$PSScriptRoot/setup/create_dev_cert.ps1"

# Report
$mainSizeMB = [math]::Round((Get-Item $mainMsix).Length / 1MB, 1)
$optionalSizeMB = [math]::Round((Get-Item $optionalMsix).Length / 1MB, 1)
$totalSizeMB = [math]::Round($mainSizeMB + $optionalSizeMB, 1)

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " Store Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host " Main MSIX:    $mainMsix ($mainSizeMB MB)" -ForegroundColor White
Write-Host " Optional MSIX: $optionalMsix ($optionalSizeMB MB)" -ForegroundColor White
Write-Host " Total:         $totalSizeMB MB" -ForegroundColor White
Write-Host "`n Main package:" -ForegroundColor Cyan
Write-Host "   - ClickraLauncher.exe (entry point)" -ForegroundColor White
Write-Host "   - Clickra.exe (AOT Dashboard)" -ForegroundColor White
Write-Host "   - ClickraShell.dll (Explorer integration)" -ForegroundColor White
Write-Host "   - No Windows App Runtime dependency" -ForegroundColor White
Write-Host "`n Optional package:" -ForegroundColor Cyan
Write-Host "   - Clickra.Fluent.exe (WinUI 3)" -ForegroundColor White
Write-Host "   - Windows App Runtime 2.x dependency" -ForegroundColor White
Write-Host "   - MainPackageDependency → Clickra" -ForegroundColor White
Write-Host "`n Store submission:" -ForegroundColor Cyan
Write-Host "   1. Upload Clickra_Main.msix as primary package" -ForegroundColor White
Write-Host "   2. Create optional package product in Partner Center" -ForegroundColor White
Write-Host "   3. Upload Clickra_Fluent.msix as optional/DLC" -ForegroundColor White
Write-Host "   4. Configure related set in Partner Center" -ForegroundColor White
