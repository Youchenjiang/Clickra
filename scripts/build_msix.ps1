# Clickra MSIX Build Script
$ErrorActionPreference = "Stop"

$root = Get-Location
$packagingDir = "$root/packaging/msix"
$layoutDir = "$packagingDir/Layout"
$publishDir = "$root/publish"

function Assert-NativeSuccess {
    if ($LASTEXITCODE -ne 0) {
        throw "Native command failed with exit code $LASTEXITCODE"
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

# Add Windows SDK tools to PATH
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
$fluentBuildDir = "$root/src/Clickra.Fluent/bin/Release"
$fluentObjDir = "$root/src/Clickra.Fluent/obj/Release"
if (Test-Path $fluentBuildDir) { Remove-Item -Recurse -Force $fluentBuildDir }
if (Test-Path $fluentObjDir) { Remove-Item -Recurse -Force $fluentObjDir }
New-Item -ItemType Directory -Path $layoutDir | Out-Null

# 2. Build Binaries
Write-Host "[Build] Publishing CLI (NativeAOT)..." -ForegroundColor Gray
dotnet publish src/Clickra.CLI/Clickra.csproj -c Release -r win-x64 -o "$publishDir/cli" --self-contained true
Assert-NativeSuccess

Write-Host "[Build] Publishing Shell Extension (NativeAOT)..." -ForegroundColor Gray
dotnet publish src/ClickraShell/ClickraShell.csproj -c Release -r win-x64 -o "$publishDir/shell" --self-contained true
Assert-NativeSuccess

Write-Host "[Build] Publishing Fluent GUI (framework-dependent)..." -ForegroundColor Gray
dotnet publish src/Clickra.Fluent/Clickra.Fluent.csproj -c Release --self-contained false
Assert-NativeSuccess

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
$fluentPublishSource = "src/Clickra.Fluent/bin/Release/net8.0-windows10.0.26100.0/win-x64/publish"

# Keep the deps/runtime files, but skip large Windows ML payloads Clickra does not use.
$fluentExclude = @(
    "*.pdb",
    "DirectML.dll",
    "onnxruntime.dll",
    "Microsoft.Windows.AI.MachineLearning.dll",
    "Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll"
)
Get-ChildItem $fluentPublishSource -File | Where-Object {
    $name = $_.Name
    -not ($fluentExclude | Where-Object { $name -like $_ })
} | ForEach-Object {
    Copy-Item $_.FullName "$layoutDir/"
}

$runtimeSource = "$fluentPublishSource/runtimes/win-x64/native"
if (Test-Path $runtimeSource) {
    $runtimeTarget = "$layoutDir/runtimes/win-x64/native"
    New-Item -ItemType Directory -Path $runtimeTarget -Force | Out-Null
    Copy-Item "$runtimeSource/Microsoft.WindowsAppRuntime.Bootstrap.dll" "$runtimeTarget/" -ErrorAction SilentlyContinue
    Copy-Item "$runtimeSource/WebView2Loader.dll" "$runtimeTarget/" -ErrorAction SilentlyContinue
}

# Use StoreLogo as the context menu app.png
if (Test-Path "$packagingDir/Assets/StoreLogo.png") {
    Copy-Item "$packagingDir/Assets/StoreLogo.png" "$layoutDir/app.png"
}
# Copy ICO for classic menu support
if (Test-Path "src/resources/app.ico") {
    Copy-Item "src/resources/app.ico" "$layoutDir/app.ico"
}

# The Fluent publish output already contains the XAML resource index.

# 4. Create Appx Package
Write-Host "[Build] Creating MSIX Package..." -ForegroundColor Gray
$msixPath = "$root/Clickra.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath }
& "makeappx.exe" pack /d "$layoutDir" /p $msixPath /o
Assert-NativeSuccess

# 5. Signing (Optional)
$pfxPath = "$packagingDir/ClickraDev.pfx"

# Read publisher from AppxManifest.xml
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
            Assert-NativeSuccess
        }
    } catch {
        Write-Warning "[Sign] Failed to validate PFX: $_"
    }
} else {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -like "*$publisher*" | Select-Object -First 1
    if (-not $cert) {
        Write-Host "[Sign] No certificate found, generating..." -ForegroundColor Gray
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$root/scripts/setup/create_dev_cert.ps1"
        Assert-NativeSuccess
    }
}

if (Test-Path $pfxPath) {
    Write-Host "[Sign] Signing with PFX..." -ForegroundColor Gray
    & "signtool.exe" sign /fd SHA256 /a /f $pfxPath /p "1234" $msixPath
    Assert-NativeSuccess
} else {
    if ($cert) {
        Write-Host "[Sign] Signing with certificate from store..." -ForegroundColor Gray
        & "signtool.exe" sign /fd SHA256 /a /sha1 $cert.Thumbprint $msixPath
        Assert-NativeSuccess
    } else {
        Write-Warning "[Sign] No certificate found. Package is unsigned."
    }
}

Write-Host "`n[Done] Clickra MSIX Build Complete: $msixPath" -ForegroundColor Green
