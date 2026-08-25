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
if (Test-Path $layoutDir) { Remove-Item -Recurse -Force $layoutDir -ErrorAction SilentlyContinue }
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue }
$fluentBuildDir = "$root/src/Clickra.Fluent/bin/Release"
$fluentObjDir = "$root/src/Clickra.Fluent/obj/Release"
if (Test-Path $fluentBuildDir) { Remove-Item -Recurse -Force $fluentBuildDir -ErrorAction SilentlyContinue }
if (Test-Path $fluentObjDir) { Remove-Item -Recurse -Force $fluentObjDir -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null

# 2. Build Binaries
Invoke-NativePublish "src/Clickra.CLI/Clickra.csproj" "$publishDir/cli"
Invoke-NativePublish "src/ClickraShell/ClickraShell.csproj" "$publishDir/shell"
Invoke-NativePublish "src/ClickraLauncher/ClickraLauncher.csproj" "$publishDir/launcher"
Invoke-FluentPublish -ExtraArgs "-p:WindowsPackageType=None"

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
Add-WindowsSdkToolsToPath
Add-VsInstallerToPath

Copy-Item "$packagingDir/AppxManifest.xml" "$layoutDir/"
Sync-WindowsAppRuntimeDependency `
    -ProjectPath "$root/src/Clickra.Fluent/Clickra.Fluent.csproj" `
    -ManifestPath "$layoutDir/AppxManifest.xml"
Copy-Item -Recurse "$packagingDir/Assets" "$layoutDir/"
Copy-Item -Recurse "$packagingDir/Strings" "$layoutDir/"
Copy-Item "$publishDir/cli/Clickra.exe" "$layoutDir/"
Copy-Item "$publishDir/shell/ClickraShell.dll" "$layoutDir/"
Copy-Item "$publishDir/launcher/ClickraLauncher.exe" "$layoutDir/"
Copy-Item "src/Clickra.Fluent/Assets/AppIcon.png" "$layoutDir/Assets/AppIcon.png"
Copy-FluentPublishOutput -LayoutDir $layoutDir -FluentPublishSource "src/Clickra.Fluent/bin/Release/net8.0-windows10.0.26100.0/win-x64/publish"
Copy-IconAssets -PackagingDir $packagingDir -LayoutDir $layoutDir

# 4. Create Appx Package
Write-Host "[Build] Creating MSIX Package..." -ForegroundColor Gray
$msixPath = "$root/Clickra.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath }
& "makeappx.exe" pack /d "$layoutDir" /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

# 5. Signing
Invoke-SignMsixPackage `
    -ManifestPath "$packagingDir/AppxManifest.xml" `
    -MsixPath $msixPath `
    -PfxPath "$packagingDir/ClickraDev.pfx" `
    -DevCertScriptPath "$PSScriptRoot/setup/create_dev_cert.ps1"

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
