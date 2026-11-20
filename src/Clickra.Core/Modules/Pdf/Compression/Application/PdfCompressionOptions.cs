using System;

namespace Clickra.Core.Modules.Pdf.Compression.Application;

public static class PdfCompressionOptions
{
    public static bool TryParseLevel(string? value, out PdfCompressionLevel level)
    {
        level = PdfCompressionLevel.Balanced;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "small":
            case "screen":
            case "compact":
            case "小檔":
            case "小文件":
                level = PdfCompressionLevel.Small;
                return true;
            case "balanced":
            case "ebook":
            case "平衡":
                level = PdfCompressionLevel.Balanced;
                return true;
            case "high":
            case "highquality":
            case "high-quality":
            case "printer":
            case "quality":
            case "高品質":
                level = PdfCompressionLevel.HighQuality;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Authoritative preset numbers for a compression level. These are the values
    /// applied when only a level is chosen; individual option keys can still override them.
    /// Embedded fonts are never removed: a PDF must look the same on every computer
    /// (docs/specs/pdf_compression_future_roadmap.md, 2026-11-20).</summary>
    public static (int TargetDpi, int JpegQuality, bool RemoveMetadata) GetPreset(PdfCompressionLevel level) =>
        level switch
        {
            PdfCompressionLevel.Small => (120, 75, true),
            PdfCompressionLevel.HighQuality => (0, 85, false), // 0 = skip downsampling
            _ => (150, 80, false)
        };

    /// <summary>Maps a 0-2 dashboard slider position to the compression level it selects.</summary>
    public static PdfCompressionLevel FromSliderLevel(int level) =>
        level switch
        {
            0 => PdfCompressionLevel.Small,
            2 => PdfCompressionLevel.HighQuality,
            _ => PdfCompressionLevel.Balanced
        };

    /// <summary>The lower-case "level" option name for a compression level.</summary>
    public static string ToOptionName(PdfCompressionLevel level) =>
        level switch
        {
            PdfCompressionLevel.Small => "small",
            PdfCompressionLevel.HighQuality => "high",
            _ => "balanced"
        };

    /// <summary>The localization key shared with the UI for displaying the level label.</summary>
    public static string GetLabelKey(PdfCompressionLevel level) =>
        level switch
        {
            PdfCompressionLevel.Small => "setting_pdf_compress_level_small",
            PdfCompressionLevel.HighQuality => "setting_pdf_compress_level_high",
            _ => "setting_pdf_compress_level_std"
        };
}
