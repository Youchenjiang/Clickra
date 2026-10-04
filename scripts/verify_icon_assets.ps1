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

        for ($y = 0; $y -lt 150; $y++) {
            for ($x = 0; $x -lt 310; $x++) {
                $insideIcon = $x -ge 91 -and $x -lt 219 -and $y -ge 11 -and $y -lt 139
                if (-not $insideIcon -and $wide.GetPixel($x, $y).A -ne 0) {
                    throw "Wide logo canvas is not transparent outside the centered primary icon at ${x},${y}"
                }
            }
        }

        for ($y = 0; $y -lt 128; $y++) {
            for ($x = 0; $x -lt 128; $x++) {
                if ($wide.GetPixel($x + 91, $y + 11).ToArgb() -ne $primary.GetPixel($x, $y).ToArgb()) {
                    throw "Wide logo center does not match the canonical 128px primary icon at ${x},${y}"
                }
            }
        }
    }
    finally {
        $wide.Dispose()
        $primary.Dispose()
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

Write-Host "Icon asset verification passed."
