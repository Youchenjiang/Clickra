using System;
using System.Collections.Generic;

namespace Clickra.Core.Modules.Pdf.Compression.Application;

public class PdfCompressionSettings
{
    public PdfCompressionLevel Level { get; set; } = PdfCompressionLevel.Balanced;
    /// <summary>Content-stream minification is lossless and always on; there is no option to turn it off.</summary>
    public bool MinifyContent { get; } = true;
    public bool DeduplicateFonts { get; set; } = true;
    /// <summary>Drops XMP metadata, page thumbnails and PieceInfo (Small only). Fonts are never removed.</summary>
    public bool RemoveMetadata { get; set; }
    public int TargetDpi { get; set; } = 150; // 0 means no downsampling
    public int JpegQuality { get; set; } = 80;

    public static PdfCompressionSettings Parse(Dictionary<string, object>? options)
    {
        var settings = new PdfCompressionSettings();
        if (options == null)
            return settings;

        // 1. Level parsing
        string levelValue = "balanced";
        if (options.TryGetValue("level", out var levelObj) && levelObj != null)
        {
            levelValue = levelObj.ToString() ?? levelValue;
        }
        if (!PdfCompressionOptions.TryParseLevel(levelValue, out PdfCompressionLevel parsedLevel))
        {
            throw new ArgumentException($"Unsupported PDF compression level: {levelValue}");
        }

        settings.Level = parsedLevel;

        // Preset numbers come from one table so the UI mapping and the processor cannot drift.
        var (targetDpi, jpegQuality, removeMetadata) = PdfCompressionOptions.GetPreset(parsedLevel);
        settings.TargetDpi = targetDpi;
        settings.JpegQuality = jpegQuality;
        settings.RemoveMetadata = removeMetadata;
        settings.DeduplicateFonts = true;

        // 2. Individual custom options overrides. The old "strip_fonts" and "minify_content"
        // keys are ignored on purpose: fonts are never removed and minification is always on.
        if (options.TryGetValue("target_dpi", out var dpi) && int.TryParse(dpi?.ToString(), out int d))
            settings.TargetDpi = Math.Max(0, d);

        if (options.TryGetValue("jpeg_quality", out var jq) && int.TryParse(jq?.ToString(), out int q))
            settings.JpegQuality = Math.Max(1, Math.Min(100, q));

        return settings;
    }
}
