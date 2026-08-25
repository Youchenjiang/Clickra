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
if (Test-Path $layoutDir) { Remove-Item -Recurse -Force $layoutDir }
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
$fluentBuildDir = "$root/src/Clickra.Fluent/bin/Release"
$fluentObjDir = "$root/src/Clickra.Fluent/obj/Release"
if (Test-Path $fluentBuildDir) { Remove-Item -Recurse -Force $fluentBuildDir }
if (Test-Path $fluentObjDir) { Remove-Item -Recurse -Force $fluentObjDir }
New-Item -ItemType Directory -Path $layoutDir | Out-Null

# 2. Build Binaries
Invoke-NativePublish "src/Clickra.CLI/Clickra.csproj" "$publishDir/cli"
Invoke-NativePublish "src/ClickraShell/ClickraShell.csproj" "$publishDir/shell"
Invoke-FluentPublish

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
Copy-Item "$packagingDir/AppxManifest.xml" "$layoutDir/"
Sync-WindowsAppRuntimeDependency `
    -ProjectPath "$root/src/Clickra.Fluent/Clickra.Fluent.csproj" `
    -ManifestPath "$layoutDir/AppxManifest.xml"
Copy-Item -Recurse "$packagingDir/Assets" "$layoutDir/"
Copy-Item -Recurse "$packagingDir/Strings" "$layoutDir/"

# Copy Binaries
Copy-Item "$publishDir/cli/Clickra.exe" "$layoutDir/"
Copy-Item "$publishDir/shell/ClickraShell.dll" "$layoutDir/"
Copy-Item "src/Clickra.Fluent/Assets/AppIcon.png" "$layoutDir/Assets/AppIcon.png"

# Copy Fluent GUI output from the SDK-generated publish folder; its deps.json
# includes WinUI and Bootstrap.Net entries that are missing from the custom -o output.
Copy-FluentPublishOutput -LayoutDir $layoutDir -FluentPublishSource "src/Clickra.Fluent/bin/Release/net8.0-windows10.0.26100.0/win-x64/publish"

$runtimeSource = "src/Clickra.Fluent/bin/Release/net8.0-windows10.0.26100.0/win-x64/publish/runtimes/win-x64/native"
if (Test-Path $runtimeSource) {
    $runtimeTarget = "$layoutDir/runtimes/win-x64/native"
    New-Item -ItemType Directory -Path $runtimeTarget -Force | Out-Null
    Copy-Item "$runtimeSource/Microsoft.WindowsAppRuntime.Bootstrap.dll" "$runtimeTarget/" -ErrorAction SilentlyContinue
    Copy-Item "$runtimeSource/WebView2Loader.dll" "$runtimeTarget/" -ErrorAction SilentlyContinue
}

Copy-IconAssets -PackagingDir $packagingDir -LayoutDir $layoutDir

# 4. Create Appx Package
Write-Host "[Build] Creating MSIX Package..." -ForegroundColor Gray
$msixPath = "$root/Clickra.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath }
& "makeappx.exe" pack /d "$layoutDir" /p $msixPath /o
Assert-NativeSuccess

# 5. Signing
Invoke-SignMsixPackage `
    -ManifestPath "$packagingDir/AppxManifest.xml" `
    -MsixPath $msixPath `
    -PfxPath "$packagingDir/ClickraDev.pfx" `
    -DevCertScriptPath "$PSScriptRoot/setup/create_dev_cert.ps1"

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
