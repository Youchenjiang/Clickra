param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\src\resources")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$iconSizes = @(16, 20, 24, 32, 48, 64)
$icons = @(
    @{ Name = "menu-ppt2pdf.ico";      Color = "#D35230"; Kind = "Letter"; Letter = "P" },
    @{ Name = "menu-word2pdf.ico";     Color = "#2B579A"; Kind = "Letter"; Letter = "W" },
    @{ Name = "menu-excel2pdf.ico";    Color = "#217346"; Kind = "Letter"; Letter = "X" },
    @{ Name = "menu-merge-pdf.ico";    Color = "#C43E3E"; Kind = "Merge" },
    @{ Name = "menu-compress-pdf.ico"; Color = "#A4262C"; Kind = "Compress" },
    @{ Name = "menu-img2pdf.ico";      Color = "#7A3E9D"; Kind = "ImageToPage" },
    @{ Name = "menu-img-merge.ico";    Color = "#8E44AD"; Kind = "ImageMerge" },
    @{ Name = "menu-img-stitch.ico";   Color = "#5C4FB5"; Kind = "Stitch" },
    @{ Name = "menu-translate-pdf.ico";Color = "#007C91"; Kind = "Translate" },
    @{ Name = "menu-decrypt-pdf.ico";  Color = "#C47F00"; Kind = "Unlock" },
    @{ Name = "menu-split-pdf.ico";    Color = "#B83232"; Kind = "Split" }
)

function New-Pen([string]$color, [float]$width) {
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml($color), $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Draw-ImageFrame($graphics, [float]$x, [float]$y, [float]$width, [float]$height, $pen) {
    $graphics.DrawRectangle($pen, $x, $y, $width, $height)
    $graphics.DrawEllipse($pen, $x + ($width * 0.62), $y + ($height * 0.18), 4, 4)
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x + 3, $y + $height - 4),
        [System.Drawing.PointF]::new($x + ($width * 0.38), $y + ($height * 0.52)),
        [System.Drawing.PointF]::new($x + ($width * 0.58), $y + ($height * 0.70)),
        [System.Drawing.PointF]::new($x + $width - 3, $y + ($height * 0.40))
    ))
}

function New-IconFrame([int]$size, $definition) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)

    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($definition.Color))
    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $whitePen = New-Pen "#FFFFFF" 4
    $thinWhitePen = New-Pen "#FFFFFF" 3
    try {
        $graphics.FillEllipse($background, 3, 3, 58, 58)

        switch ($definition.Kind) {
            "Letter" {
                $font = [System.Drawing.Font]::new("Segoe UI", 34, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
                try {
                    $format = [System.Drawing.StringFormat]::new()
                    $format.Alignment = [System.Drawing.StringAlignment]::Center
                    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
                    $graphics.DrawString($definition.Letter, $font, $whiteBrush, [System.Drawing.RectangleF]::new(2, 1, 60, 60), $format)
                    $format.Dispose()
                }
                finally { $font.Dispose() }
            }
            "Merge" {
                $graphics.DrawRectangle($thinWhitePen, 15, 14, 24, 31)
                $graphics.DrawRectangle($thinWhitePen, 24, 20, 24, 31)
                $graphics.DrawLine($whitePen, 42, 38, 54, 38)
                $graphics.DrawLine($whitePen, 48, 32, 48, 44)
            }
            "Compress" {
                # Two large arrows collapse toward the centre. Four diagonal arrows become an
                # ambiguous star at Explorer's 16 px menu size.
                $graphics.DrawLine($whitePen, 9, 32, 28, 32)
                $graphics.DrawLine($whitePen, 20, 24, 28, 32)
                $graphics.DrawLine($whitePen, 20, 40, 28, 32)
                $graphics.DrawLine($whitePen, 55, 32, 36, 32)
                $graphics.DrawLine($whitePen, 44, 24, 36, 32)
                $graphics.DrawLine($whitePen, 44, 40, 36, 32)
            }
            "ImageToPage" {
                Draw-ImageFrame $graphics 10 17 27 25 $thinWhitePen
                $graphics.DrawRectangle($thinWhitePen, 33, 22, 20, 27)
                $graphics.DrawLine($thinWhitePen, 38, 31, 49, 31)
                $graphics.DrawLine($thinWhitePen, 38, 37, 49, 37)
            }
            "ImageMerge" {
                Draw-ImageFrame $graphics 9 17 22 25 $thinWhitePen
                Draw-ImageFrame $graphics 33 22 22 25 $thinWhitePen
                $graphics.DrawLine($whitePen, 27, 45, 39, 45)
                $graphics.DrawLine($whitePen, 33, 39, 33, 51)
            }
            "Stitch" {
                $graphics.DrawRectangle($thinWhitePen, 16, 10, 32, 18)
                $graphics.DrawRectangle($thinWhitePen, 16, 36, 32, 18)
                $graphics.DrawLine($whitePen, 32, 27, 32, 38)
                $graphics.DrawLine($whitePen, 27, 33, 32, 38)
                $graphics.DrawLine($whitePen, 37, 33, 32, 38)
            }
            "Translate" {
                # A + 文 is the familiar language/translation symbol and survives 16 px better
                # than small bidirectional arrows.
                $latinFont = [System.Drawing.Font]::new("Segoe UI", 25, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
                $cjkFont = [System.Drawing.Font]::new("Microsoft JhengHei", 25, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
                try {
                    $graphics.DrawString("A", $latinFont, $whiteBrush, 7, 20)
                    $graphics.DrawString("文", $cjkFont, $whiteBrush, 31, 17)
                    $graphics.DrawLine($thinWhitePen, 13, 18, 48, 18)
                    $graphics.DrawLine($thinWhitePen, 43, 13, 48, 18)
                }
                finally {
                    $cjkFont.Dispose()
                    $latinFont.Dispose()
                }
            }
            "Unlock" {
                # The shackle deliberately ends above the body on the right, leaving a visible gap.
                $graphics.DrawArc($whitePen, 24, 8, 30, 34, 180, 180)
                $graphics.DrawLine($whitePen, 24, 25, 24, 36)
                $graphics.FillRectangle($whiteBrush, 14, 36, 38, 19)
                $keyhole = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($definition.Color))
                try {
                    $graphics.FillEllipse($keyhole, 29, 40, 8, 8)
                    $graphics.FillRectangle($keyhole, 31, 46, 4, 6)
                }
                finally { $keyhole.Dispose() }
            }
            "Split" {
                # Two document halves with a strong central gap communicate a completed split.
                $graphics.DrawRectangle($thinWhitePen, 10, 13, 18, 38)
                $graphics.DrawRectangle($thinWhitePen, 36, 13, 18, 38)
                $graphics.DrawLine($whitePen, 25, 32, 9, 32)
                $graphics.DrawLine($whitePen, 15, 26, 9, 32)
                $graphics.DrawLine($whitePen, 15, 38, 9, 32)
                $graphics.DrawLine($whitePen, 39, 32, 55, 32)
                $graphics.DrawLine($whitePen, 49, 26, 55, 32)
                $graphics.DrawLine($whitePen, 49, 38, 55, 32)
            }
        }

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $thinWhitePen.Dispose()
        $whitePen.Dispose()
        $whiteBrush.Dispose()
        $background.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-Ico([string]$path, $definition) {
    $frames = foreach ($size in $iconSizes) {
        [pscustomobject]@{ Size = $size; Bytes = (New-IconFrame $size $definition) }
    }

    $stream = [System.IO.File]::Create($path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $writer.Write([byte]$frame.Size)
            $writer.Write([byte]$frame.Size)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
foreach ($definition in $icons) {
    Write-Ico (Join-Path $OutputDirectory $definition.Name) $definition
}

Write-Host "Generated $($icons.Count) shell menu icons in $OutputDirectory"
