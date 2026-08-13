# Clickra NativeAOT MSIX Build Script (零依賴軌道)
# 產出 Clickra-Native.msix：只有 Clickra.exe + ClickraShell.dll，
# 不需要 .NET runtime 也不需要 Windows App Runtime，可在乾淨機器直接安裝。
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/build_common.ps1"

$root = Get-Location
$packagingDir = "$root/packaging/msix"
$layoutDir = "$packagingDir/NativeLayout"
$publishDir = "$root/publish-native"

Add-WindowsSdkToolsToPath

Write-Host "[Build] Starting Clickra NativeAOT MSIX Build..." -ForegroundColor Cyan

# 1. Clean up
if (Test-Path $layoutDir) { Remove-Item -Recurse -Force $layoutDir }
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $layoutDir | Out-Null

# 2. Build Binaries (NativeAOT, self-contained — 零 .NET 依賴)
Write-Host "[Build] Publishing CLI (NativeAOT)..."
dotnet publish src/Clickra.CLI/Clickra.csproj -c Release -r win-x64 -o "$publishDir/cli" --self-contained true
Assert-NativeSuccess

Write-Host "[Build] Publishing Shell Extension (NativeAOT)..."
dotnet publish src/ClickraShell/ClickraShell.csproj -c Release -r win-x64 -o "$publishDir/shell" --self-contained true
Assert-NativeSuccess

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..."
Copy-Item "$packagingDir/AppxManifest.Native.xml" "$layoutDir/AppxManifest.xml"
Copy-Item -Recurse "$packagingDir/Assets" "$layoutDir/"
Copy-Item -Recurse "$packagingDir/Strings" "$layoutDir/"
Copy-Item "$publishDir/cli/Clickra.exe" "$layoutDir/"
Copy-Item "$publishDir/shell/ClickraShell.dll" "$layoutDir/"

# 圖示與選單 assets
if (Test-Path "src/resources/app.ico") {
    Copy-Item "src/resources/app.ico" "$layoutDir/app.ico"
}
if (Test-Path "$packagingDir/Assets/StoreLogo.png") {
    Copy-Item "$packagingDir/Assets/StoreLogo.png" "$layoutDir/app.png"
}

# 4. Create Appx Package
Write-Host "[Build] Creating MSIX Package..."
$msixPath = "$root/Clickra-Native.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath }

# Native 套件沒有 Fluent 的 SDK 產出 resources.pri，需自行建立。
& "makepri.exe" createconfig /cf "$layoutDir/priconfig.xml" /dq zh-TW /pv 10.0.0 /o
Assert-NativeSuccess
& "makepri.exe" new /pr "$layoutDir" /cf "$layoutDir/priconfig.xml" /of "$layoutDir/resources.pri" /o
Assert-NativeSuccess
if (-not (Test-Path "$layoutDir/resources.pri")) {
    throw "Resource index resources.pri was not generated."
}

& "makeappx.exe" pack /d "$layoutDir" /p $msixPath /o
Assert-NativeSuccess

# 5. Signing
Invoke-SignMsixPackage `
    -ManifestPath "$layoutDir/AppxManifest.xml" `
    -MsixPath $msixPath `
    -PfxPath "$packagingDir/ClickraDev.pfx" `
    -DevCertScriptPath "$PSScriptRoot/setup/create_dev_cert.ps1"

Write-Host "`n[Done] Clickra NativeAOT MSIX Build Complete: $msixPath" -ForegroundColor Green
