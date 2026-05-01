# Clickra MSIX Build Script
$ErrorActionPreference = "Stop"

$root = Get-Location
$packagingDir = "$root/packaging/msix"
$layoutDir = "$packagingDir/Layout"
$publishDir = "$root/publish"

Write-Host "🏗️  Starting Clickra MSIX Build..." -ForegroundColor Cyan

# 1. Clean up
if (Test-Path $layoutDir) { Remove-Item -Recurse -Force $layoutDir }
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $layoutDir | Out-Null

# 2. Build Binaries (NativeAOT)
Write-Host "📦 Publishing CLI and Shell Extension..." -ForegroundColor Gray
dotnet publish src/Clickra.CLI/Clickra.csproj -c Release -r win-x64 -o "$publishDir/cli" --self-contained true
dotnet publish src/ClickraShell/ClickraShell.csproj -c Release -r win-x64 -o "$publishDir/shell" --self-contained true

# 3. Assemble Layout
Write-Host "📂 Assembling Layout..." -ForegroundColor Gray
Copy-Item "$packagingDir/AppxManifest.xml" "$layoutDir/"
Copy-Item -Recurse "$packagingDir/Assets" "$layoutDir/"
Copy-Item -Recurse "$packagingDir/Strings" "$layoutDir/"

# Copy Binaries
Copy-Item "$publishDir/cli/Clickra.exe" "$layoutDir/"
Copy-Item "$publishDir/shell/ClickraShell.dll" "$layoutDir/"

# Use StoreLogo as the context menu app.png (32x32 is ideal)
if (Test-Path "$packagingDir/Assets/StoreLogo.png") {
    Copy-Item "$packagingDir/Assets/StoreLogo.png" "$layoutDir/app.png"
}

# 4. Compile Resources (MakePri)
Write-Host "📑 Compiling Resource Index (PRI)..." -ForegroundColor Gray
& "makepri.exe" createconfig /cf "$layoutDir/priconfig.xml" /dq zh-TW /pv 10.0.0 /o
& "makepri.exe" new /pr "$layoutDir" /cf "$layoutDir/priconfig.xml" /of "$layoutDir/resources.pri" /o

# 5. Create Appx Package
Write-Host "📦 Creating MSIX Package..." -ForegroundColor Gray
$msixPath = "$root/Clickra.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath }
& "makeappx.exe" pack /d "$layoutDir" /p $msixPath /o

# 6. Signing (Optional)
$certPath = "$packagingDir/ClickraDev.cer"
$pfxPath = "$packagingDir/ClickraDev.pfx"

if (Test-Path $pfxPath) {
    Write-Host "🖋️  Signing Package..." -ForegroundColor Gray
    & "signtool.exe" sign /fd SHA256 /a /f $pfxPath /p "1234" $msixPath
} else {
    Write-Host "⚠️  No PFX found at $pfxPath. Package is unsigned." -ForegroundColor Yellow
    Write-Host "   To sign, create a cert with: New-SelfSignedCertificate -Type Custom -Subject 'CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138' -KeyUsage DigitalSignature -FriendlyName 'Clickra Dev' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')" -ForegroundColor Gray
}

Write-Host "`n✅ Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
