using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Clickra.Core;

namespace Clickra.Core.Processors;

/// <summary>Loads a versioned, data-only Markdown document template from JSON.</summary>
public static class MarkdownTemplateFile
{
    public const int CurrentVersion = 1;
    public const long MaxFileBytes = 64 * 1024;

    public static MarkdownDocumentTemplate Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Template path is required.", nameof(path));
        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Markdown template file not found.", fullPath);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("Markdown template file is too large.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fullPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        });
        JsonElement root = document.RootElement;
        RequireObject(root, "template");
        EnsureOnly(root, "version", "name", "base", "typography", "layout", "palette");

        int version = RequiredInt(root, "version");
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported Markdown template version: {version}.");

        string baseId = OptionalString(root, "base") ?? MarkdownPdfOptions.ThemeDefault;
        if (baseId is not (MarkdownPdfOptions.ThemeDefault or MarkdownPdfOptions.ThemeMinimal or MarkdownPdfOptions.ThemeAcademic))
            throw new InvalidDataException("Template base must be default, minimal, or academic.");

        MarkdownDocumentTemplate template = MarkdownTemplateCatalog.ResolveBuiltIn(baseId);
        MarkdownTypography typography = root.TryGetProperty("typography", out JsonElement typographyElement)
            ? ReadTypography(typographyElement, template.Typography)
            : template.Typography;
        if (typography.LineHeightPoints < typography.BodySizePoints)
            throw new InvalidDataException("Template lineHeight must be greater than or equal to bodySize.");
        MarkdownLayout layout = root.TryGetProperty("layout", out JsonElement layoutElement)
            ? ReadLayout(layoutElement, template.Layout)
            : template.Layout;
        MarkdownPalette palette = root.TryGetProperty("palette", out JsonElement paletteElement)
            ? ReadPalette(paletteElement, template.Palette)
            : template.Palette;

        string name = OptionalString(root, "name") ?? Path.GetFileNameWithoutExtension(fullPath);
        if (name.Length is < 1 or > 80 || ContainsControlCharacter(name))
            throw new InvalidDataException("Template name must be between 1 and 80 printable characters.");
        return new MarkdownDocumentTemplate("custom:" + name, typography, layout, palette);
    }

    private static MarkdownTypography ReadTypography(JsonElement element, MarkdownTypography source)
    {
        RequireObject(element, "typography");
        EnsureOnly(element, "latinFont", "cjkFont", "monospaceFont", "bodySize", "lineHeight", "headings");
        MarkdownHeadingScale headings = element.TryGetProperty("headings", out JsonElement headingElement)
            ? ReadHeadings(headingElement, source.Headings)
            : source.Headings;
        return source with
        {
            LatinFont = FontName(element, "latinFont", source.LatinFont),
            CjkFont = FontName(element, "cjkFont", source.CjkFont),
            MonospaceFont = FontName(element, "monospaceFont", source.MonospaceFont),
            BodySizePoints = Number(element, "bodySize", source.BodySizePoints, 6, 36),
            LineHeightPoints = Number(element, "lineHeight", source.LineHeightPoints, 8, 72),
            Headings = headings
        };
    }

    private static MarkdownHeadingScale ReadHeadings(JsonElement element, MarkdownHeadingScale source)
    {
        RequireObject(element, "typography.headings");
        EnsureOnly(element, "h1", "h2", "h3", "h4", "h5", "h6");
        return source with
        {
            H1 = Number(element, "h1", source.H1, 8, 72),
            H2 = Number(element, "h2", source.H2, 8, 72),
            H3 = Number(element, "h3", source.H3, 8, 72),
            H4 = Number(element, "h4", source.H4, 8, 72),
            H5 = Number(element, "h5", source.H5, 8, 72),
            H6 = Number(element, "h6", source.H6, 8, 72)
        };
    }

    private static MarkdownLayout ReadLayout(JsonElement element, MarkdownLayout source)
    {
        RequireObject(element, "layout");
        EnsureOnly(element, "margin", "blockGap", "accentH2", "drawH2Bar", "fillTableHeader", "quoteBarWidth");
        return source with
        {
            MarginPoints = Number(element, "margin", source.MarginPoints, 18, 144),
            BlockGapPoints = Number(element, "blockGap", source.BlockGapPoints, 0, 40),
            AccentH2 = Boolean(element, "accentH2", source.AccentH2),
            DrawH2Bar = Boolean(element, "drawH2Bar", source.DrawH2Bar),
            FillTableHeader = Boolean(element, "fillTableHeader", source.FillTableHeader),
            QuoteBarWidthPoints = Number(element, "quoteBarWidth", source.QuoteBarWidthPoints, 0.5, 12)
        };
    }

    private static MarkdownPalette ReadPalette(JsonElement element, MarkdownPalette source)
    {
        RequireObject(element, "palette");
        EnsureOnly(element, "body", "strong", "accent", "softAccent", "border");
        return source with
        {
            Body = Color(element, "body", source.Body),
            Strong = Color(element, "strong", source.Strong),
            Accent = Color(element, "accent", source.Accent),
            SoftAccent = Color(element, "softAccent", source.SoftAccent),
            Border = Color(element, "border", source.Border)
        };
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{name} must be a JSON object.");
    }

    private static void EnsureOnly(JsonElement element, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name))
                throw new InvalidDataException($"Unknown Markdown template property: {property.Name}.");
            if (!seen.Add(property.Name))
                throw new InvalidDataException($"Duplicate Markdown template property: {property.Name}.");
        }
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new InvalidDataException($"Template property '{name}' must be an integer.");
        return result;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Template property '{name}' must be a string.");
        return value.GetString()?.Trim();
    }

    private static string FontName(JsonElement element, string name, string fallback)
    {
        string? value = OptionalString(element, name);
        if (value is null) return fallback;
        if (value.Length is < 1 or > 128 || ContainsControlCharacter(value) || value.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            throw new InvalidDataException($"Template font '{name}' must be an installed font family name, not a path.");
        if (!ClickraFontResolver.TryGetCanonicalTemplateFamily(value, out string canonicalFamily))
            throw new InvalidDataException($"Template font '{name}' is not supported by both PDF and DOCX output.");
        return canonicalFamily;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (char character in value)
        {
            if (char.IsControl(character)) return true;
        }
        return false;
    }

    private static double Number(JsonElement element, string name, double fallback, double min, double max)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double result) || !double.IsFinite(result) || result < min || result > max)
            throw new InvalidDataException($"Template property '{name}' must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");
        return result;
    }

    private static bool Boolean(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"Template property '{name}' must be true or false.")
        };
    }

    private static MarkdownThemeColor Color(JsonElement element, string name, MarkdownThemeColor fallback)
    {
        string? value = OptionalString(element, name);
        if (value is null) return fallback;
        if (value.Length != 7 || value[0] != '#' ||
            !byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            throw new InvalidDataException($"Template color '{name}' must use #RRGGBB format.");
        return new MarkdownThemeColor(r, g, b);
    }
}
