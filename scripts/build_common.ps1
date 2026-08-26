# Shared helpers for Clickra MSIX build scripts.
# Dot-sourced by build_msix.ps1, build_msix_continue.ps1, and build_native_msix.ps1
# so the three tracks keep a single copy of environment setup, build steps, and signing.
$ErrorActionPreference = "Stop"

# ------------------------------------------------------------------
# Shared build environment setup
# ------------------------------------------------------------------
$script:Root = Get-Location
$script:PackagingDir = "$($script:Root)/packaging/msix"
$script:LayoutDir = "$($script:PackagingDir)/Layout"
$script:PublishDir = "$($script:Root)/publish"

# ------------------------------------------------------------------
# Shared build functions
# ------------------------------------------------------------------

function Invoke-NativePublish {
    param(
        [string]$Project,
        [string]$OutputDir,
        [string]$ExtraArgs = ""
    )
    $cmd = "dotnet publish $Project -c Release -r win-x64 -o `"$OutputDir`" --self-contained true $ExtraArgs"
    Write-Host "[Build] Publishing $Project..." -ForegroundColor Gray
    Invoke-Expression $cmd
    Assert-NativeSuccess
}

function Invoke-FluentPublish {
    param(
        [string]$ExtraArgs = ""
    )
    Write-Host "[Build] Publishing Fluent GUI (framework-dependent)..." -ForegroundColor Gray
    dotnet publish src/Clickra.Fluent/Clickra.Fluent.csproj -c Release --self-contained false $ExtraArgs
    Assert-NativeSuccess
}

function Copy-FluentPublishOutput {
    param(
        [string]$LayoutDir,
        [string]$FluentPublishSource
    )
    $fluentExclude = @(
        "*.pdb",
        "DirectML.dll",
        "onnxruntime.dll",
        "Microsoft.Windows.AI.MachineLearning.dll",
        "Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll"
    )
    Get-ChildItem $FluentPublishSource -File |
        Where-Object {
            $name = $_.Name
            -not ($fluentExclude | Where-Object { $name -like $_ })
        } |
        ForEach-Object {
            Copy-Item $_.FullName "$LayoutDir/"
        }
}function Copy-IconAssets {
    param(
        [string]$PackagingDir,
        [string]$LayoutDir
    )
    if (Test-Path "$PackagingDir/Assets/StoreLogo.png") {
        Copy-Item "$PackagingDir/Assets/StoreLogo.png" "$LayoutDir/app.png"
    }
    if (Test-Path "src/resources/app.ico") {
        Copy-Item "src/resources/app.ico" "$LayoutDir/"
    }
    if (Test-Path "src/resources/menu-*.ico") {
        Copy-Item "src/resources/menu-*.ico" "$LayoutDir/"
    }
}

function Sync-WindowsAppRuntimeDependency {
    param(
        [string]$ProjectPath,
        [string]$ManifestPath
    )

    [xml]$project = Get-Content $ProjectPath
    $sdkReference = $project.Project.ItemGroup.PackageReference |
        Where-Object Include -eq "Microsoft.WindowsAppSDK" |
        Select-Object -First 1
    $sdkVersion = [version]$sdkReference.Version
    if ($sdkVersion.Major -lt 2) {
        throw "Automatic Windows App Runtime alignment requires Windows App SDK 2.0 or newer."
    }

    [xml]$manifest = Get-Content $ManifestPath
    $dependency = $manifest.Package.Dependencies.PackageDependency |
        Where-Object Name -like "Microsoft.WindowsAppRuntime.*" |
        Select-Object -First 1
    $dependency.Name = "Microsoft.WindowsAppRuntime.$($sdkVersion.Major)"
    $dependency.MinVersion = "$($sdkVersion.ToString(3)).0"
    $manifest.Save($ManifestPath)

    Write-Host "[Build] Windows App Runtime aligned to $($dependency.Name) $($dependency.MinVersion)" -ForegroundColor Gray
}

function Remove-ForceDir {
    param([string]$Path, [switch]$TolerateLocks)
    if (Test-Path $Path) {
        $ea = if ($TolerateLocks) { 'SilentlyContinue' } else { 'Stop' }
        Remove-Item -Recurse -Force $Path -ErrorAction $ea
    }
}

function Clear-BuildArtifacts {
    param([string]$Root, [switch]$TolerateLocks)
    Remove-ForceDir "$Root/packaging/msix/Layout" -TolerateLocks:$TolerateLocks
    Remove-ForceDir "$Root/publish" -TolerateLocks:$TolerateLocks
    Remove-ForceDir "$Root/src/Clickra.Fluent/bin/Release" -TolerateLocks:$TolerateLocks
    Remove-ForceDir "$Root/src/Clickra.Fluent/obj/Release" -TolerateLocks:$TolerateLocks
    New-Item -ItemType Directory -Path "$Root/packaging/msix/Layout" -Force | Out-Null
}

function Copy-AssemblyLayout {
    param(
        [string]$Root,
        [string]$PackagingDir,
        [string]$LayoutDir,
        [string]$PublishDir,
        [string[]]$ExtraBinaries = @()
    )
    Copy-Item "$PackagingDir/AppxManifest.xml" "$LayoutDir/"
    Sync-WindowsAppRuntimeDependency `
        -ProjectPath "$Root/src/Clickra.Fluent/Clickra.Fluent.csproj" `
        -ManifestPath "$LayoutDir/AppxManifest.xml"
    Copy-Item -Recurse "$PackagingDir/Assets" "$LayoutDir/"
    Copy-Item -Recurse "$PackagingDir/Strings" "$LayoutDir/"
    Copy-Item "$PublishDir/cli/Clickra.exe" "$LayoutDir/"
    Copy-Item "$PublishDir/shell/ClickraShell.dll" "$LayoutDir/"
    foreach ($bin in $ExtraBinaries) {
        Copy-Item "$PublishDir/$bin" "$LayoutDir/"
    }
    Copy-Item "src/Clickra.Fluent/Assets/AppIcon.png" "$LayoutDir/Assets/AppIcon.png"
    Copy-FluentPublishOutput -LayoutDir $LayoutDir -FluentPublishSource "src/Clickra.Fluent/bin/Release/net8.0-windows10.0.26100.0/win-x64/publish"
    Copy-IconAssets -PackagingDir $PackagingDir -LayoutDir $LayoutDir
}

function Test-LayoutComplete {
    param([string]$LayoutDir)
    $required = @(
        "Clickra.exe",
        "Clickra.Fluent.exe",
        "ClickraShell.dll",
        "AppxManifest.xml"
    )
    $missing = $required | Where-Object { -not (Test-Path "$LayoutDir/$_") }
    if ($missing) {
        throw "Layout incomplete — missing required files: $($missing -join ', ')"
    }
    Write-Host "[Build] Layout verified — all required files present." -ForegroundColor Gray
}

function New-AndSignMsix {
    param(
        [string]$Root,
        [string]$PackagingDir,
        [string]$LayoutDir
    )
    Test-LayoutComplete -LayoutDir $LayoutDir
    Write-Host "[Build] Creating MSIX Package..." -ForegroundColor Gray
    $msixPath = "$Root/Clickra.msix"
    if (Test-Path $msixPath) { Remove-Item $msixPath }
    & "makeappx.exe" pack /d "$LayoutDir" /p $msixPath /o
    Assert-NativeSuccess
    Invoke-SignMsixPackage `
        -ManifestPath "$PackagingDir/AppxManifest.xml" `
        -MsixPath $msixPath `
        -PfxPath "$PackagingDir/ClickraDev.pfx" `
        -DevCertScriptPath "$PSScriptRoot/setup/create_dev_cert.ps1"
    return $msixPath
}

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
