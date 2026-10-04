$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function Assert-Png {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing PNG asset: $Path"
    }

    $bitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Path))
    try {
        if ($bitmap.Width -ne $Width -or $bitmap.Height -ne $Height) {
            throw "Unexpected PNG dimensions for ${Path}: $($bitmap.Width)x$($bitmap.Height), expected ${Width}x${Height}"
        }
        if ($bitmap.PixelFormat -notmatch '32bpp') {
            throw "PNG asset is not 32-bit: $Path ($($bitmap.PixelFormat))"
        }
        if ($bitmap.GetPixel(0, 0).A -ne 0) {
            throw "PNG asset must keep a transparent outer corner: $Path"
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Assert-SameFile {
    param(
        [string]$Expected,
        [string]$Actual
    )

    $expectedHash = (Get-FileHash -LiteralPath $Expected -Algorithm SHA256).Hash
    $actualHash = (Get-FileHash -LiteralPath $Actual -Algorithm SHA256).Hash
    if ($expectedHash -ne $actualHash) {
        throw "Asset drift detected: $Actual no longer matches $Expected"
    }
}

function Assert-IcoFrames {
    param(
        [string]$Path,
        [int[]]$ExpectedSizes
    )

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Path))
    if ($bytes.Length -lt 6) {
        throw "ICO header is truncated: $Path"
    }

    $count = [BitConverter]::ToUInt16($bytes, 4)
    if ($count -ne $ExpectedSizes.Count) {
        throw "Unexpected ICO frame count for ${Path}: $count, expected $($ExpectedSizes.Count)"
    }

    $actualSizes = @()
    for ($i = 0; $i -lt $count; $i++) {
        $offset = 6 + (16 * $i)
        $width = [int]$bytes[$offset]
        $height = [int]$bytes[$offset + 1]
        if ($width -eq 0) { $width = 256 }
        if ($height -eq 0) { $height = 256 }
        if ($width -ne $height) {
            throw "ICO frame is not square in ${Path}: ${width}x${height}"
        }
        $actualSizes += $width
    }

    if (($actualSizes -join ',') -ne ($ExpectedSizes -join ',')) {
        throw "Unexpected ICO frame ladder for ${Path}: $($actualSizes -join ','), expected $($ExpectedSizes -join ',')"
    }
}

function Assert-TransparentOutsideCenteredIcon {
    param([System.Drawing.Bitmap]$Wide)

    for ($y = 0; $y -lt 150; $y++) {
        for ($x = 0; $x -lt 310; $x++) {
            $insideIcon = $x -ge 91 -and $x -lt 219 -and $y -ge 11 -and $y -lt 139
            if (-not $insideIcon -and $Wide.GetPixel($x, $y).A -ne 0) {
                throw "Wide logo canvas is not transparent outside the centered primary icon at ${x},${y}"
            }
        }
    }
}

function Assert-CenteredIconMatchesPrimary {
    param(
        [System.Drawing.Bitmap]$Wide,
        [System.Drawing.Bitmap]$Primary
    )

    for ($y = 0; $y -lt 128; $y++) {
        for ($x = 0; $x -lt 128; $x++) {
            if ($Wide.GetPixel($x + 91, $y + 11).ToArgb() -ne $Primary.GetPixel($x, $y).ToArgb()) {
                throw "Wide logo center does not match the canonical 128px primary icon at ${x},${y}"
            }
        }
    }
}

function Assert-WideLogoComposition {
    param(
        [string]$WidePath,
        [string]$Primary128Path
    )

    $wide = [System.Drawing.Bitmap]::FromFile((Resolve-Path $WidePath))
    $primary = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Primary128Path))
    try {
        if ($wide.Width -ne 310 -or $wide.Height -ne 150) {
            throw "Unexpected wide logo dimensions: $($wide.Width)x$($wide.Height)"
        }

        Assert-TransparentOutsideCenteredIcon $wide
        Assert-CenteredIconMatchesPrimary $wide $primary
    }
    finally {
        $wide.Dispose()
        $primary.Dispose()
    }
}

function Assert-ManifestIconWiring {
    param(
        [string]$Path,
        [string]$StoreLogo,
        [string]$Square44,
        [string]$Square150,
        [string]$WideLogo
    )

    [xml]$manifest = Get-Content -LiteralPath $Path -Raw
    $propertiesLogo = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='Logo']")
    $visualElements = $manifest.SelectSingleNode("//*[local-name()='VisualElements']")
    $defaultTile = $manifest.SelectSingleNode("//*[local-name()='DefaultTile']")

    if ($null -eq $propertiesLogo -or $propertiesLogo.InnerText -ne $StoreLogo) {
        throw "Unexpected Store logo wiring in ${Path}: expected $StoreLogo"
    }
    if ($null -eq $visualElements -or $visualElements.GetAttribute('Square44x44Logo') -ne $Square44) {
        throw "Unexpected Square44 logo wiring in ${Path}: expected $Square44"
    }
    if ($visualElements.GetAttribute('Square150x150Logo') -ne $Square150) {
        throw "Unexpected Square150 logo wiring in ${Path}: expected $Square150"
    }
    if ($null -eq $defaultTile -or $defaultTile.GetAttribute('Wide310x150Logo') -ne $WideLogo) {
        throw "Unexpected wide logo wiring in ${Path}: expected $WideLogo"
    }
}

function Assert-SparseManifestIconWiring {
    param([string]$Path)

    [xml]$manifest = Get-Content -LiteralPath $Path -Raw
    $propertiesLogo = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='Logo']")
    $visualElements = $manifest.SelectSingleNode("//*[local-name()='VisualElements']")
    $shellCommand = $manifest.SelectSingleNode("//*[local-name()='ShellExplorerCommand']")

    if ($null -eq $propertiesLogo -or $propertiesLogo.InnerText -ne 'app.png') {
        throw "Sparse package Properties Logo must remain on A1 app.png"
    }
    if ($null -eq $visualElements -or $visualElements.GetAttribute('Square44x44Logo') -ne 'app.png' -or
        $visualElements.GetAttribute('Square150x150Logo') -ne 'app.png') {
        throw "Sparse package square logos must remain on A1 app.png"
    }
    if ($null -eq $shellCommand -or $shellCommand.GetAttribute('Icon') -ne 'app.png') {
        throw "Sparse shell command icon must remain on A1 app.png"
    }
}

function Assert-BuildIconWiring {
    param([string]$Path)

    $source = Get-Content -LiteralPath $Path -Raw
    if ($source -notmatch 'Copy-Item\s+"src/resources/app\.png"\s+"\$LayoutDir/app\.png"') {
        throw "Copy-IconAssets must stage A1 src/resources/app.png as layout app.png"
    }
    if ($source -match 'Copy-Item\s+"\$PackagingDir/Assets/StoreLogo\.png"\s+"\$LayoutDir/app\.png"') {
        throw "Store A3 must not leak into sparse/system layout app.png"
    }
}

$primarySizes = @(16, 24, 32, 44, 48, 64, 128, 150, 256, 1024)
foreach ($size in $primarySizes) {
    Assert-Png "packaging/brand_assets/primary/clickra-icon-primary-$size.png" $size $size
}

$capabilitySizes = @(50, 256, 1024)
foreach ($size in $capabilitySizes) {
    Assert-Png "packaging/brand_assets/capability/clickra-icon-capability-$size.png" $size $size
}

Assert-Png "src/resources/app.png" 256 256
Assert-Png "src/Clickra.Fluent/Assets/AppIcon.png" 256 256
Assert-Png "packaging/msix/Assets/Square44x44Logo.png" 44 44
Assert-Png "packaging/msix/Assets/Square150x150Logo.png" 150 150
Assert-Png "packaging/msix/Assets/StoreLogo.png" 50 50
Assert-Png "packaging/msix/Assets/Wide310x150Logo.png" 310 150

$unplatedTargetSizes = @(16, 24, 32, 44, 48, 256)
foreach ($size in $unplatedTargetSizes) {
    $asset = "packaging/msix/Assets/Square44x44Logo.targetsize-${size}_altform-unplated.png"
    Assert-Png $asset $size $size
    Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary-$size.png" $asset
}

$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
Assert-IcoFrames "packaging/brand_assets/primary/clickra-icon-primary.ico" $icoSizes
Assert-IcoFrames "src/resources/app.ico" $icoSizes
Assert-IcoFrames "src/Clickra.Fluent/Assets/AppIcon.ico" $icoSizes

Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary-256.png" "src/resources/app.png"
Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary-256.png" "src/Clickra.Fluent/Assets/AppIcon.png"
Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary.ico" "src/resources/app.ico"
Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary.ico" "src/Clickra.Fluent/Assets/AppIcon.ico"
Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary-44.png" "packaging/msix/Assets/Square44x44Logo.png"
Assert-SameFile "packaging/brand_assets/primary/clickra-icon-primary-150.png" "packaging/msix/Assets/Square150x150Logo.png"
Assert-SameFile "packaging/brand_assets/capability/clickra-icon-capability-50.png" "packaging/msix/Assets/StoreLogo.png"
Assert-WideLogoComposition "packaging/msix/Assets/Wide310x150Logo.png" "packaging/brand_assets/primary/clickra-icon-primary-128.png"

foreach ($manifest in @('packaging/msix/AppxManifest.xml', 'packaging/msix/AppxManifest.Fluent.xml')) {
    Assert-ManifestIconWiring $manifest 'Assets\StoreLogo.png' 'Assets\Square44x44Logo.png' 'Assets\Square150x150Logo.png' 'Assets\Wide310x150Logo.png'
}
Assert-SparseManifestIconWiring 'src/resources/AppxManifest.xml'
Assert-BuildIconWiring 'scripts/build_common.ps1'

Write-Host "Icon asset verification passed."
