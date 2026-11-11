using System;

namespace Clickra.Core;

/// <summary>
/// UI font family selector for localized presentation text and thumbnail rendering.
/// Ensures CJK text renders with system fonts containing appropriate glyphs.
/// </summary>
public static class LocalizedUiFontSelector
{
    public static string GetTextFontName(string language)
    {
        string normalized = Localization.NormalizeLanguageCode(language);
        if (normalized.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft JhengHei UI";
        }

        if (normalized.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft YaHei UI";
        }

        if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "Yu Gothic UI";
        }

        if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return "Malgun Gothic";
        }

        return "Segoe UI";
    }
}
