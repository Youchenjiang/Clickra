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
Write-Host "[Build] Publishing CLI (NativeAOT)..." -ForegroundColor Gray
dotnet publish src/Clickra.CLI/Clickra.csproj -c Release -r win-x64 -o "$publishDir/cli" --self-contained true
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed" }

Write-Host "[Build] Publishing Shell Extension (NativeAOT)..." -ForegroundColor Gray
dotnet publish src/ClickraShell/ClickraShell.csproj -c Release -r win-x64 -o "$publishDir/shell" --self-contained true
if ($LASTEXITCODE -ne 0) { throw "Shell publish failed" }

Write-Host "[Build] Publishing UI Launcher (NativeAOT)..." -ForegroundColor Gray
dotnet publish src/ClickraLauncher/ClickraLauncher.csproj -c Release -r win-x64 -o "$publishDir/launcher" --self-contained true
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed" }

Write-Host "[Build] Publishing Fluent GUI (framework-dependent, WindowsPackageType=None)..." -ForegroundColor Gray
dotnet publish src/Clickra.Fluent/Clickra.Fluent.csproj -c Release --self-contained false -p:WindowsPackageType=None
if ($LASTEXITCODE -ne 0) { throw "Fluent publish failed" }

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
Add-WindowsSdkToolsToPath
Add-VsInstallerToPath

Copy-Item "$packagingDir/AppxManifest.xml" "$layoutDir/"

# Sync runtime dependency
. "$PSScriptRoot/build_msix.ps1" -WhatIf 2>$null  # just for the function
# Inline the dependency sync
[xml]$project = Get-Content "$root/src/Clickra.Fluent/Clickra.Fluent.csproj"
$sdkReference = $project.Project.ItemGroup.PackageReference |
    Where-Object Include -eq "Microsoft.WindowsAppSDK" |
    Select-Object -First 1
$sdkVersion = [version]$sdkReference.Version

[xml]$manifest = Get-Content "$layoutDir/AppxManifest.xml"
$dependency = $manifest.Package.Dependencies.PackageDependency |
    Where-Object Name -like "Microsoft.WindowsAppRuntime.*" |
    Select-Object -First 1
$dependency.Name = "Microsoft.WindowsAppRuntime.$($sdkVersion.Major)"
$dependency.MinVersion = "$($sdkVersion.ToString(3)).0"
$manifest.Save("$layoutDir/AppxManifest.xml")
Write-Host "[Build] Windows App Runtime aligned to $($dependency.Name) $($dependency.MinVersion)" -ForegroundColor Gray

Copy-Item -Recurse "$packagingDir/Assets" "$layoutDir/"
Copy-Item -Recurse "$packagingDir/Strings" "$layoutDir/"
Copy-Item "$publishDir/cli/Clickra.exe" "$layoutDir/"
Copy-Item "$publishDir/shell/ClickraShell.dll" "$layoutDir/"
Copy-Item "$publishDir/launcher/ClickraLauncher.exe" "$layoutDir/"
Copy-Item "src/Clickra.Fluent/Assets/AppIcon.png" "$layoutDir/Assets/AppIcon.png"

# Fluent publish output
$fluentPublishSource = "src/Clickra.Fluent/bin/Release/net8.0-windows10.0.26100.0/win-x64/publish"
$fluentExclude = @(
    "*.pdb",
    "DirectML.dll",
    "onnxruntime.dll",
    "Microsoft.Windows.AI.MachineLearning.dll",
    "Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll"
)
Get-ChildItem $fluentPublishSource -File |
    Where-Object {
        $name = $_.Name
        -not ($fluentExclude | Where-Object { $name -like $_ })
    } |
    ForEach-Object {
        Copy-Item $_.FullName "$layoutDir/"
    }

if (Test-Path "$packagingDir/Assets/StoreLogo.png") {
    Copy-Item "$packagingDir/Assets/StoreLogo.png" "$layoutDir/app.png"
}
if (Test-Path "src/resources/app.ico") {
    Copy-Item "src/resources/app.ico" "$layoutDir/"
}
if (Test-Path "src/resources/menu-*.ico") {
    Copy-Item "src/resources/menu-*.ico" "$layoutDir/"
}

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
