using System;
using System.Collections.Generic;
using System.IO;

namespace Clickra.Core.Processors;

/// <summary>Shared one-shot Markdown presentation options used by Markdown renderers.</summary>
public static class MarkdownPdfOptions
{
    /// <summary>Option key selecting the document theme.</summary>
    public const string ThemeKey = "markdown_theme";
    /// <summary>Option key selecting the paper size.</summary>
    public const string PaperKey = "markdown_paper";
    /// <summary>Option key selecting the text-size preset.</summary>
    public const string TextSizeKey = "markdown_text_size";
    /// <summary>Option key selecting the code-block theme.</summary>
    public const string CodeThemeKey = "markdown_code_theme";
    /// <summary>Option key containing the optional document template path.</summary>
    public const string TemplatePathKey = "markdown_template_path";

    /// <summary>Identifier for the standard Clickra theme.</summary>
    public const string ThemeDefault = "default";
    /// <summary>Identifier for the minimal document theme.</summary>
    public const string ThemeMinimal = "minimal";
    /// <summary>Identifier for the academic document theme.</summary>
    public const string ThemeAcademic = "academic";

    /// <summary>Identifier for A4 paper.</summary>
    public const string PaperA4 = "a4";
    /// <summary>Identifier for US Letter paper.</summary>
    public const string PaperLetter = "letter";

    /// <summary>Identifier for the small text preset.</summary>
    public const string TextSmall = "small";
    /// <summary>Identifier for the standard text preset.</summary>
    public const string TextStandard = "standard";
    /// <summary>Identifier for the large text preset.</summary>
    public const string TextLarge = "large";

    /// <summary>Identifier for dark code-block styling.</summary>
    public const string CodeDark = "dark";
    /// <summary>Identifier for light code-block styling.</summary>
    public const string CodeLight = "light";

    /// <summary>Creates normalized one-shot rendering options for Markdown conversions.</summary>
    public static Dictionary<string, object> Create(
        string theme = ThemeDefault,
        string paper = PaperA4,
        string textSize = TextStandard,
        string codeTheme = CodeDark,
        string? templatePath = null)
    {
        var options = new Dictionary<string, object>
        {
            [ThemeKey] = NormalizeTheme(theme),
            [PaperKey] = NormalizePaper(paper),
            [TextSizeKey] = NormalizeTextSize(textSize),
            [CodeThemeKey] = NormalizeCodeTheme(codeTheme)
        };
        if (!string.IsNullOrWhiteSpace(templatePath)) options[TemplatePathKey] = Path.GetFullPath(templatePath);
        return options;
    }

    /// <summary>Gets the normalized theme identifier or the default theme.</summary>
    public static string GetTheme(IReadOnlyDictionary<string, object>? options) =>
        NormalizeTheme(Read(options, ThemeKey, ThemeDefault));

    /// <summary>Gets the normalized paper size or A4.</summary>
    public static string GetPaper(IReadOnlyDictionary<string, object>? options) =>
        NormalizePaper(Read(options, PaperKey, PaperA4));

    /// <summary>Gets the normalized body text-size selection.</summary>
    public static string GetTextSize(IReadOnlyDictionary<string, object>? options) =>
        NormalizeTextSize(Read(options, TextSizeKey, TextStandard));

    /// <summary>Gets the normalized code-block color theme.</summary>
    public static string GetCodeTheme(IReadOnlyDictionary<string, object>? options) =>
        NormalizeCodeTheme(Read(options, CodeThemeKey, CodeDark));

    /// <summary>Returns the absolute custom template path, if supplied.</summary>
    public static string? GetTemplatePath(IReadOnlyDictionary<string, object>? options) =>
        options is not null && options.TryGetValue(TemplatePathKey, out object? value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? Path.GetFullPath(value!.ToString()!)
            : null;

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
