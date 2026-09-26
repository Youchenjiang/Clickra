using System;
using System.Collections.Generic;

namespace Clickra.Core.Processors;

public static class ImageCompressionOptions
{
    public const string OptionMin = "min";
    public const string OptionSmall = "small";
    public const string OptionStandard = "std";
    public const string OptionHigh = "high";

    private static readonly IReadOnlyDictionary<string, ImageCompressionLevel> LevelAliases =
        new Dictionary<string, ImageCompressionLevel>(StringComparer.OrdinalIgnoreCase)
        {
            ["min"] = ImageCompressionLevel.Minimum,
            ["minimum"] = ImageCompressionLevel.Minimum,
            ["low"] = ImageCompressionLevel.Minimum,
            ["0"] = ImageCompressionLevel.Minimum,
            ["最小"] = ImageCompressionLevel.Minimum,
            ["small"] = ImageCompressionLevel.Small,
            ["compact"] = ImageCompressionLevel.Small,
            ["1"] = ImageCompressionLevel.Small,
            ["小"] = ImageCompressionLevel.Small,
            ["小檔案"] = ImageCompressionLevel.Small,
            ["小文件"] = ImageCompressionLevel.Small,
            ["std"] = ImageCompressionLevel.Standard,
            ["standard"] = ImageCompressionLevel.Standard,
            ["balanced"] = ImageCompressionLevel.Standard,
            ["medium"] = ImageCompressionLevel.Standard,
            ["2"] = ImageCompressionLevel.Standard,
            ["標準"] = ImageCompressionLevel.Standard,
            ["标准"] = ImageCompressionLevel.Standard,
            ["high"] = ImageCompressionLevel.High,
            ["maximum"] = ImageCompressionLevel.High,
            ["max"] = ImageCompressionLevel.High,
            ["3"] = ImageCompressionLevel.High,
            ["高品質"] = ImageCompressionLevel.High,
            ["高质量"] = ImageCompressionLevel.High
        };

    public static bool TryParseLevel(string? value, out ImageCompressionLevel level)
    {
        level = ImageCompressionLevel.Small;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return LevelAliases.TryGetValue(value.Trim(), out level);
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
