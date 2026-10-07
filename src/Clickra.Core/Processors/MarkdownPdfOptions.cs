using System;
using System.Collections.Generic;

namespace Clickra.Core.Processors;

/// <summary>Shared one-shot Markdown-to-PDF conversion options used by every product surface.</summary>
public static class MarkdownPdfOptions
{
    public const string ThemeKey = "markdown_theme";
    public const string PaperKey = "markdown_paper";
    public const string TextSizeKey = "markdown_text_size";
    public const string CodeThemeKey = "markdown_code_theme";

    public const string ThemeDefault = "default";
    public const string ThemeMinimal = "minimal";
    public const string ThemeAcademic = "academic";

    public const string PaperA4 = "a4";
    public const string PaperLetter = "letter";

    public const string TextSmall = "small";
    public const string TextStandard = "standard";
    public const string TextLarge = "large";

    public const string CodeDark = "dark";
    public const string CodeLight = "light";

    public static Dictionary<string, object> Create(
        string theme = ThemeDefault,
        string paper = PaperA4,
        string textSize = TextStandard,
        string codeTheme = CodeDark) => new()
    {
        [ThemeKey] = NormalizeTheme(theme),
        [PaperKey] = NormalizePaper(paper),
        [TextSizeKey] = NormalizeTextSize(textSize),
        [CodeThemeKey] = NormalizeCodeTheme(codeTheme)
    };

    public static string GetTheme(IReadOnlyDictionary<string, object>? options) =>
        NormalizeTheme(Read(options, ThemeKey, ThemeDefault));

    public static string GetPaper(IReadOnlyDictionary<string, object>? options) =>
        NormalizePaper(Read(options, PaperKey, PaperA4));

    public static string GetTextSize(IReadOnlyDictionary<string, object>? options) =>
        NormalizeTextSize(Read(options, TextSizeKey, TextStandard));

    public static string GetCodeTheme(IReadOnlyDictionary<string, object>? options) =>
        NormalizeCodeTheme(Read(options, CodeThemeKey, CodeDark));

    private static string Read(IReadOnlyDictionary<string, object>? options, string key, string fallback) =>
        options is not null && options.TryGetValue(key, out object? value)
            ? value?.ToString() ?? fallback
            : fallback;

    private static string NormalizeTheme(string value) => value.Trim().ToLowerInvariant() switch
    {
        ThemeMinimal => ThemeMinimal,
        ThemeAcademic => ThemeAcademic,
        _ => ThemeDefault
    };

    private static string NormalizePaper(string value) =>
        value.Trim().Equals(PaperLetter, StringComparison.OrdinalIgnoreCase) ? PaperLetter : PaperA4;

    private static string NormalizeTextSize(string value) => value.Trim().ToLowerInvariant() switch
    {
        TextSmall => TextSmall,
        TextLarge => TextLarge,
        _ => TextStandard
    };

    private static string NormalizeCodeTheme(string value) =>
        value.Trim().Equals(CodeLight, StringComparison.OrdinalIgnoreCase) ? CodeLight : CodeDark;
}
