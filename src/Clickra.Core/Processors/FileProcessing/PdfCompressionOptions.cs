using System;

namespace Clickra.Core.Processors;

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
}
