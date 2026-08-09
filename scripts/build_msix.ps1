# Clickra MSIX Build Script
$ErrorActionPreference = "Stop"

$root = Get-Location
$packagingDir = "$root/packaging/msix"
$layoutDir = "$packagingDir/Layout"
$publishDir = "$root/publish"

# Add Windows SDK tools to PATH (dynamically locate the newest version)
$kitsRoots = @(
    "C:\Program Files (x86)\Windows Kits\10\bin",
    "C:\Windows Kits\10\bin"
)
$foundSdk = $false
foreach ($rootPath in $kitsRoots) {
    if (Test-Path $rootPath) {
        $sortedDirs = Get-ChildItem -Path $rootPath -Directory | 
                      Where-Object { $_.Name -like "10.*" } | 
                      Sort-Object { [version]$_.Name } -Descending
        
        foreach ($dir in $sortedDirs) {
            $candidatePath = Join-Path $dir.FullName "x64"
            if (Test-Path "$candidatePath\makepri.exe") {
                $env:Path = "$candidatePath;$env:Path"
                Write-Host "[SDK] Found Windows SDK: $candidatePath" -ForegroundColor Gray
                $foundSdk = $true
                break
            }
        }
    }
    if ($foundSdk) { break }
}
if (-not $foundSdk) {
    Write-Warning "[SDK] Could not locate Windows SDK tools path."
}

# Ensure the Visual Studio Installer directory is on PATH so vcvarsall.bat can
# find vswhere.exe. When it is missing (e.g. building from Git Bash), the
# 'vswhere.exe not recognized' error text leaks into .NET NativeAOT's
# findvcvarsall.bat output and produces a broken link.exe command (MSB3073,
# exit code 123).
$pfx86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if ([string]::IsNullOrEmpty($pfx86)) { $pfx86 = 'C:\Program Files (x86)' }
$vsInstallerDir = Join-Path $pfx86 'Microsoft Visual Studio\Installer'
if ((Test-Path "$vsInstallerDir\vswhere.exe") -and ($env:Path -notlike "*$vsInstallerDir*")) {
    $env:Path = "$vsInstallerDir;$env:Path"
    Write-Host "[SDK] Added VS Installer to PATH for vswhere.exe" -ForegroundColor Gray
}

Write-Host "[Build] Starting Clickra MSIX Build..." -ForegroundColor Cyan

# 1. Clean up
if (Test-Path $layoutDir) { Remove-Item -Recurse -Force $layoutDir }
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Path $layoutDir | Out-Null

# 2. Build Binaries (NativeAOT)
Write-Host "[Build] Publishing CLI and Shell Extension..." -ForegroundColor Gray
dotnet publish src/Clickra.CLI/Clickra.csproj -c Release -r win-x64 -o "$publishDir/cli" --self-contained true
dotnet publish src/ClickraShell/ClickraShell.csproj -c Release -r win-x64 -o "$publishDir/shell" --self-contained true

# 3. Assemble Layout
Write-Host "[Build] Assembling Layout..." -ForegroundColor Gray
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
# Copy ICO for classic menu support
if (Test-Path "src/resources/app.ico") {
    Copy-Item "src/resources/app.ico" "$layoutDir/app.ico"
}

# 4. Compile Resources (MakePri)
Write-Host "[Build] Compiling Resource Index (PRI)..." -ForegroundColor Gray
& "makepri.exe" createconfig /cf "$layoutDir/priconfig.xml" /dq zh-TW /pv 10.0.0 /o
& "makepri.exe" new /pr "$layoutDir" /cf "$layoutDir/priconfig.xml" /of "$layoutDir/resources.pri" /o

# 5. Create Appx Package
Write-Host "[Build] Creating MSIX Package..." -ForegroundColor Gray
$msixPath = "$root/Clickra.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath }
& "makeappx.exe" pack /d "$layoutDir" /p $msixPath /o

# 6. Signing (Optional)
$pfxPath = "$packagingDir/ClickraDev.pfx"

# Read publisher from AppxManifest.xml to ensure certificate matches identity
[xml]$manifest = Get-Content "$packagingDir/AppxManifest.xml"
$publisher = $manifest.Package.Identity.Publisher

if (Test-Path $pfxPath) {
    try {
        $certObj = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, "1234")
        $pfxSubject = $certObj.Subject
        $certObj.Dispose()

        if ($pfxSubject -ne $publisher) {
            Write-Host "[Sign] PFX subject mismatch, regenerating certificate..." -ForegroundColor Yellow
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$root/scripts/setup/create_dev_cert.ps1"
        }
    } catch {
        Write-Warning "[Sign] Failed to validate PFX: $_"
    }
} else {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -like "*$publisher*" | Select-Object -First 1
    if (-not $cert) {
        Write-Host "[Sign] No certificate found, generating..." -ForegroundColor Gray
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$root/scripts/setup/create_dev_cert.ps1"
    }
}

if (Test-Path $pfxPath) {
    Write-Host "[Sign] Signing with PFX..." -ForegroundColor Gray
    & "signtool.exe" sign /fd SHA256 /a /f $pfxPath /p "1234" $msixPath
} else {
    if ($cert) {
        Write-Host "[Sign] Signing with certificate from store..." -ForegroundColor Gray
        & "signtool.exe" sign /fd SHA256 /a /sha1 $cert.Thumbprint $msixPath
    } else {
        Write-Warning "[Sign] No certificate found. Package is unsigned."
    }
}

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green