using System;
using System.Collections.Generic;

namespace Clickra.Core.Processors;

public class PdfCompressionSettings
{
    public PdfCompressionLevel Level { get; set; } = PdfCompressionLevel.Balanced;
    public bool MinifyContent { get; set; } = true;
    public bool DeduplicateFonts { get; set; } = true;
    public bool StripFonts { get; set; } = false;
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
        switch (parsedLevel)
        {
            case PdfCompressionLevel.Small:
                settings.MinifyContent = true;
                settings.DeduplicateFonts = true;
                settings.StripFonts = true;
                settings.TargetDpi = 120;
                settings.JpegQuality = 75;
                break;
            case PdfCompressionLevel.Balanced:
                settings.MinifyContent = true;
                settings.DeduplicateFonts = true;
                settings.StripFonts = false;
                settings.TargetDpi = 150;
                settings.JpegQuality = 80;
                break;
            case PdfCompressionLevel.HighQuality:
                settings.MinifyContent = true;
                settings.DeduplicateFonts = true;
                settings.StripFonts = false;
                settings.TargetDpi = 0; // 0 means skip downsampling
                settings.JpegQuality = 85;
                break;
            default:
                throw new ArgumentException("Encountered unexpected value.");
        }

        // 2. Individual custom options overrides
        if (options.TryGetValue("strip_fonts", out var sf))
            settings.StripFonts = (sf?.ToString() ?? string.Empty).Equals("true", StringComparison.OrdinalIgnoreCase);

        if (options.TryGetValue("minify_content", out var mc))
            settings.MinifyContent = (mc?.ToString() ?? string.Empty).Equals("true", StringComparison.OrdinalIgnoreCase);

        if (options.TryGetValue("target_dpi", out var dpi) && int.TryParse(dpi?.ToString(), out int d))
            settings.TargetDpi = Math.Max(0, d);

        if (options.TryGetValue("jpeg_quality", out var jq) && int.TryParse(jq?.ToString(), out int q))
            settings.JpegQuality = Math.Max(1, Math.Min(100, q));

        return settings;
    }
}
