# Shared helpers for Clickra MSIX build scripts.
# Dot-sourced by build_msix.ps1 and build_native_msix.ps1 so the two tracks
# keep a single copy of environment setup and package signing.
$ErrorActionPreference = "Stop"

function Assert-NativeSuccess {
    if ($LASTEXITCODE -ne 0) {
        throw "Native command failed with exit code $LASTEXITCODE"
    }
}

# Add Windows SDK tools to PATH.
function Add-WindowsSdkToolsToPath {
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
}

# Ensure the Visual Studio Installer directory is on PATH so vcvarsall.bat can
# find vswhere.exe. When it is missing (e.g. building from Git Bash), the
# 'vswhere.exe not recognized' error text leaks into .NET NativeAOT's
# findvcvarsall.bat output and produces a broken link.exe command (MSB3073,
# exit code 123).
function Add-VsInstallerToPath {
    $pfx86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if ([string]::IsNullOrEmpty($pfx86)) { $pfx86 = 'C:\Program Files (x86)' }
    $vsInstallerDir = Join-Path $pfx86 'Microsoft Visual Studio\Installer'
    if ((Test-Path "$vsInstallerDir\vswhere.exe") -and ($env:Path -notlike "*$vsInstallerDir*")) {
        $env:Path = "$vsInstallerDir;$env:Path"
        Write-Host "[SDK] Added VS Installer to PATH for vswhere.exe" -ForegroundColor Gray
    }
}

# Sign an MSIX package with the dev certificate, generating it when missing.
function Invoke-SignMsixPackage {
    param(
        [string]$ManifestPath,
        [string]$MsixPath,
        [string]$PfxPath,
        [string]$DevCertScriptPath
    )

    # Read publisher from AppxManifest.xml
    [xml]$manifest = Get-Content $ManifestPath
    $publisher = $manifest.Package.Identity.Publisher

    $cert = $null
    if (Test-Path $PfxPath) {
        try {
            $certObj = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, "1234")
            $pfxSubject = $certObj.Subject
            $certObj.Dispose()

            if ($pfxSubject -ne $publisher) {
                Write-Host "[Sign] PFX subject mismatch, regenerating certificate..." -ForegroundColor Yellow
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $DevCertScriptPath
                Assert-NativeSuccess
            }
        } catch {
            Write-Warning "[Sign] Failed to validate PFX: $_"
        }
    } else {
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -like "*$publisher*" | Select-Object -First 1
        if (-not $cert) {
            Write-Host "[Sign] No certificate found, generating..." -ForegroundColor Gray
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $DevCertScriptPath
            Assert-NativeSuccess
        }
    }

    if (Test-Path $PfxPath) {
        Write-Host "[Sign] Signing with PFX..." -ForegroundColor Gray
        & "signtool.exe" sign /fd SHA256 /a /f $PfxPath /p "1234" $MsixPath
        Assert-NativeSuccess
    } else {
        if ($cert) {
            Write-Host "[Sign] Signing with certificate from store..." -ForegroundColor Gray
            & "signtool.exe" sign /fd SHA256 /a /sha1 $cert.Thumbprint $MsixPath
            Assert-NativeSuccess
        } else {
            Write-Warning "[Sign] No certificate found. Package is unsigned."
        }
    }
}
