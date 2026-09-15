using System;

namespace Clickra.Core.Processors;

public static class ImageCompressionOptions
{
    public const string OptionMin = "min";
    public const string OptionSmall = "small";
    public const string OptionStandard = "std";
    public const string OptionHigh = "high";

    public static bool TryParseLevel(string? value, out ImageCompressionLevel level)
    {
        level = ImageCompressionLevel.Small;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "min":
            case "minimum":
            case "low":
            case "0":
            case "最小":
                level = ImageCompressionLevel.Minimum;
                return true;
            case "small":
            case "compact":
            case "1":
            case "小":
            case "小檔案":
            case "小文件":
                level = ImageCompressionLevel.Small;
                return true;
            case "std":
            case "standard":
            case "balanced":
            case "medium":
            case "2":
            case "標準":
            case "标准":
                level = ImageCompressionLevel.Standard;
                return true;
            case "high":
            case "maximum":
            case "max":
            case "3":
            case "高品質":
            case "高质量":
                level = ImageCompressionLevel.High;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Maps a 0-3 slider position to the image compression level it selects.</summary>
    public static ImageCompressionLevel FromSliderLevel(int level) =>
        level switch
        {
            0 => ImageCompressionLevel.Minimum,
            2 => ImageCompressionLevel.Standard,
            3 => ImageCompressionLevel.High,
            _ => ImageCompressionLevel.Small
        };

    /// <summary>The lower-case option name for an image compression level.</summary>
    public static string ToOptionName(ImageCompressionLevel level) =>
        level switch
        {
            ImageCompressionLevel.Minimum => OptionMin,
            ImageCompressionLevel.Standard => OptionStandard,
            ImageCompressionLevel.High => OptionHigh,
            _ => OptionSmall
        };

    /// <summary>The recommended JPEG/WebP quality percentage (1-100) for a level.</summary>
    public static int GetQuality(ImageCompressionLevel level) =>
        level switch
        {
            ImageCompressionLevel.Minimum => 50,
            ImageCompressionLevel.Standard => 85,
            ImageCompressionLevel.High => 95,
            _ => 75
        };

    /// <summary>The localization key shared with the UI for displaying the level label.</summary>
    public static string GetLabelKey(ImageCompressionLevel level) =>
        level switch
        {
            ImageCompressionLevel.Minimum => "setting_pdf_compress_level_min",
            ImageCompressionLevel.Standard => "setting_pdf_compress_level_std",
            ImageCompressionLevel.High => "setting_pdf_compress_level_high",
            _ => "setting_pdf_compress_level_small"
        };
}
