using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Clickra.Core;

namespace Clickra.Core.Processors;

/// <summary>Extracts renderer-neutral Markdown presentation settings from a Word DOCX template.</summary>
public static class MarkdownDocxTemplateFile
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const long MaxPackageBytes = 32L * 1024 * 1024;
    private const long MaxXmlBytes = 4L * 1024 * 1024;

    /// <summary>Reads typography and page layout from a Word document template.</summary>
    public static MarkdownDocumentTemplate Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Template path is required.", nameof(path));
        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Word template file not found.", fullPath);
        if (!Path.GetExtension(fullPath).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Word template must use the .docx format.");
        if (info.Length <= 0 || info.Length > MaxPackageBytes)
            throw new InvalidDataException("Word template file is empty or too large.");

        using ZipArchive archive = ZipFile.OpenRead(fullPath);
        XDocument styles = ReadXml(archive, "word/styles.xml");
        XDocument document = ReadXml(archive, "word/document.xml");
        XDocument? theme = ReadOptionalXml(archive, "word/theme/theme1.xml");
        MarkdownDocumentTemplate basis = MarkdownTemplateCatalog.Minimal;

        XElement? normal = FindNormalStyle(styles);
        MarkdownTypography typography = ReadTypography(styles, normal, theme, basis.Typography);
        MarkdownLayout layout = ReadLayout(styles, document, normal, basis.Layout);

        string name = Path.GetFileNameWithoutExtension(fullPath);
        if (name.Length > 80) name = name[..80];
        return new MarkdownDocumentTemplate("docx:" + name, typography, layout, basis.Palette);
    }

    private static MarkdownTypography ReadTypography(XDocument styles, XElement? normal, XDocument? theme, MarkdownTypography fallback)
    {
        XElement? defaults = styles.Root?.Element(W + "docDefaults")?.Element(W + "rPrDefault")?.Element(W + "rPr");
        string? eastAsiaLanguage = ReadRunProperty(normal, styles, defaults, W + "lang", "eastAsia");
        string latin = CanonicalFont(ReadFont(normal, styles, defaults, theme, eastAsia: false, eastAsiaLanguage: null), fallback.LatinFont);
        string cjk = CanonicalFont(ReadFont(normal, styles, defaults, theme, eastAsia: true, eastAsiaLanguage), fallback.CjkFont);
        double bodySize = ReadHalfPoints(ReadRunProperty(normal, styles, defaults, W + "sz", "val"), fallback.BodySizePoints);
        double lineHeight = ReadLineHeight(normal, styles, bodySize, fallback.LineHeightPoints);

        double Heading(int level, double current)
        {
            XElement? style = FindHeadingStyle(styles, level);
            return ReadHalfPoints(ReadRunProperty(style, styles, defaults, W + "sz", "val"), current);
        }

        return fallback with
        {
            LatinFont = latin,
            CjkFont = cjk,
            BodySizePoints = bodySize,
            LineHeightPoints = Math.Max(bodySize, lineHeight),
            Headings = new MarkdownHeadingScale(
                Heading(1, fallback.Headings.H1), Heading(2, fallback.Headings.H2), Heading(3, fallback.Headings.H3),
                Heading(4, fallback.Headings.H4), Heading(5, fallback.Headings.H5), Heading(6, fallback.Headings.H6))
        };
    }

    private static MarkdownLayout ReadLayout(XDocument styles, XDocument document, XElement? normal, MarkdownLayout fallback)
    {
        XElement? sectPr = document.Descendants(W + "sectPr").LastOrDefault();
        XElement? margins = sectPr?.Element(W + "pgMar");
        double top = ReadTwips(margins?.Attribute(W + "top")?.Value, fallback.EffectiveMarginTopPoints);
        double right = ReadTwips(margins?.Attribute(W + "right")?.Value, fallback.EffectiveMarginRightPoints);
        double bottom = ReadTwips(margins?.Attribute(W + "bottom")?.Value, fallback.EffectiveMarginBottomPoints);
        double left = ReadTwips(margins?.Attribute(W + "left")?.Value, fallback.EffectiveMarginLeftPoints);

        XElement? pPr = EffectiveParagraphProperties(normal, styles);
        XElement? spacing = pPr?.Element(W + "spacing");
        XElement? indent = pPr?.Element(W + "ind");
        string? jc = pPr?.Element(W + "jc")?.Attribute(W + "val")?.Value;
        double after = ReadTwips(spacing?.Attribute(W + "after")?.Value, fallback.BlockGapPoints);
        double firstLine = ReadTwips(indent?.Attribute(W + "firstLine")?.Value, fallback.FirstLineIndentPoints);
        XElement? h1 = FindHeadingStyle(styles, 1);
        string? h1Jc = EffectiveParagraphProperties(h1, styles)?.Element(W + "jc")?.Attribute(W + "val")?.Value;

        return fallback with
        {
            MarginTopPoints = Clamp(top, 18, 144),
            MarginRightPoints = Clamp(right, 18, 144),
            MarginBottomPoints = Clamp(bottom, 18, 144),
            MarginLeftPoints = Clamp(left, 18, 144),
            BlockGapPoints = Clamp(after, 0, 40),
            FirstLineIndentPoints = Clamp(firstLine, 0, 72),
            CenterH1 = string.Equals(h1Jc, "center", StringComparison.OrdinalIgnoreCase),
            JustifyBody = jc is "both" or "distribute" or "thaiDistribute"
        };
    }

    private static double ReadLineHeight(XElement? style, XDocument styles, double bodySize, double fallback)
    {
        XElement? spacing = EffectiveParagraphProperties(style, styles)?.Element(W + "spacing");
        if (!double.TryParse(spacing?.Attribute(W + "line")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double line))
            return fallback;
        string rule = spacing?.Attribute(W + "lineRule")?.Value ?? "auto";
        double points = rule.Equals("auto", StringComparison.OrdinalIgnoreCase) ? bodySize * line / 240d : line / 20d;
        return points is >= 8 and <= 72 ? points : fallback;
    }

    private static XElement? FindNormalStyle(XDocument styles) => styles.Descendants(W + "style")
        .FirstOrDefault(style => IsParagraphStyle(style) &&
            (style.Attribute(W + "default")?.Value == "1" ||
             string.Equals(style.Attribute(W + "styleId")?.Value, "Normal", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(style.Element(W + "name")?.Attribute(W + "val")?.Value, "Normal", StringComparison.OrdinalIgnoreCase)));

    private static XElement? FindHeadingStyle(XDocument styles, int level) => styles.Descendants(W + "style")
        .FirstOrDefault(style => IsParagraphStyle(style) && IsHeadingStyle(style, level));

    private static bool IsParagraphStyle(XElement style) =>
        string.Equals(style.Attribute(W + "type")?.Value, "paragraph", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeadingStyle(XElement style, int level)
    {
        string id = style.Attribute(W + "styleId")?.Value ?? string.Empty;
        string name = style.Element(W + "name")?.Attribute(W + "val")?.Value ?? string.Empty;
        string? outline = style.Element(W + "pPr")?.Element(W + "outlineLvl")?.Attribute(W + "val")?.Value;
        return id.Equals("Heading" + level, StringComparison.OrdinalIgnoreCase)
            || name.Replace(" ", string.Empty, StringComparison.Ordinal).Equals("Heading" + level, StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(outline, out int outlineLevel) && outlineLevel == level - 1);
    }

    private static XElement? EffectiveParagraphProperties(XElement? style, XDocument styles)
    {
        var chain = StyleChain(style, styles).Reverse().ToList();
        XElement? defaults = styles.Root?.Element(W + "docDefaults")?.Element(W + "pPrDefault")?.Element(W + "pPr");
        var result = defaults is null ? new XElement(W + "pPr") : new XElement(defaults);
        foreach (XElement item in chain)
            MergeProperties(result, item.Element(W + "pPr"));
        return result;
    }

    private static string? ReadRunProperty(XElement? style, XDocument styles, XElement? defaults, XName elementName, string attribute)
    {
        string? value = defaults?.Element(elementName)?.Attribute(W + attribute)?.Value;
        foreach (XElement item in StyleChain(style, styles).Reverse())
            value = item.Element(W + "rPr")?.Element(elementName)?.Attribute(W + attribute)?.Value ?? value;
        return value;
    }

    private static string? ReadFont(
        XElement? style,
        XDocument styles,
        XElement? defaults,
        XDocument? theme,
        bool eastAsia,
        string? eastAsiaLanguage)
    {
        foreach (XElement item in StyleChain(style, styles))
        {
            string? resolved = ReadFontFromRunProperties(item.Element(W + "rPr"), theme, eastAsia, eastAsiaLanguage);
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        }
        return ReadFontFromRunProperties(defaults, theme, eastAsia, eastAsiaLanguage);
    }

    private static string? ReadFontFromRunProperties(
        XElement? rPr,
        XDocument? theme,
        bool eastAsia,
        string? eastAsiaLanguage)
    {
        XElement? fonts = rPr?.Element(W + "rFonts");
        if (fonts is null) return null;
        string? themeKey = eastAsia
            ? fonts.Attribute(W + "eastAsiaTheme")?.Value
            : fonts.Attribute(W + "asciiTheme")?.Value ?? fonts.Attribute(W + "hAnsiTheme")?.Value;
        if (!string.IsNullOrWhiteSpace(themeKey))
            return ResolveThemeFont(theme, themeKey, eastAsia, eastAsiaLanguage);

        return eastAsia
            ? fonts.Attribute(W + "eastAsia")?.Value
            : fonts.Attribute(W + "ascii")?.Value ?? fonts.Attribute(W + "hAnsi")?.Value;
    }

    private static string ResolveThemeFont(XDocument? theme, string themeKey, bool eastAsia, string? eastAsiaLanguage)
    {
        if (theme?.Root is null)
            throw new InvalidDataException($"Word template theme font '{themeKey}' cannot be resolved because theme1.xml is missing.");

        string key = themeKey.ToLowerInvariant();
        XElement? scheme = theme.Descendants(A + "fontScheme").FirstOrDefault();
        XElement? family = ResolveThemeFamily(scheme, key);
        if (family is null)
            throw new InvalidDataException($"Word template theme font '{themeKey}' cannot be resolved.");

        if (!eastAsia)
        {
            string? latin = family.Element(A + "latin")?.Attribute("typeface")?.Value;
            if (!string.IsNullOrWhiteSpace(latin)) return latin;
            throw new InvalidDataException($"Word template theme font '{themeKey}' does not define a Latin typeface.");
        }

        string? typeface = family.Element(A + "ea")?.Attribute("typeface")?.Value;
        if (!string.IsNullOrWhiteSpace(typeface)) return typeface;

        string? script = EastAsiaScript(eastAsiaLanguage);
        if (script is not null)
        {
            string? supplemental = family.Elements(A + "font")
                .FirstOrDefault(font => string.Equals(font.Attribute("script")?.Value, script, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("typeface")?.Value;
            if (!string.IsNullOrWhiteSpace(supplemental)) return supplemental;
        }

        string[] knownScripts = { "Hant", "Hans", "Jpan", "Hang" };
        string[] candidates = family.Elements(A + "font")
            .Where(font => knownScripts.Contains(font.Attribute("script")?.Value, StringComparer.OrdinalIgnoreCase))
            .Select(font => font.Attribute("typeface")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (script is null && candidates.Length == 1) return candidates[0];

        string language = string.IsNullOrWhiteSpace(eastAsiaLanguage) ? "unspecified East Asian language" : eastAsiaLanguage;
        throw new InvalidDataException($"Word template theme font '{themeKey}' cannot resolve {language} to one East Asian typeface.");
    }

    private static XElement? ResolveThemeFamily(XElement? scheme, string key)
    {
        if (key.StartsWith("major", StringComparison.Ordinal)) return scheme?.Element(A + "majorFont");
        if (key.StartsWith("minor", StringComparison.Ordinal)) return scheme?.Element(A + "minorFont");
        return null;
    }

    private static string? EastAsiaScript(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        string normalized = language.Replace('_', '-').ToLowerInvariant();
        if (normalized.StartsWith("ja", StringComparison.Ordinal)) return "Jpan";
        if (normalized.StartsWith("ko", StringComparison.Ordinal)) return "Hang";
        if (normalized.StartsWith("zh-hant", StringComparison.Ordinal)
            || normalized.StartsWith("zh-tw", StringComparison.Ordinal)
            || normalized.StartsWith("zh-hk", StringComparison.Ordinal)
            || normalized.StartsWith("zh-mo", StringComparison.Ordinal)) return "Hant";
        if (normalized.StartsWith("zh-hans", StringComparison.Ordinal)
            || normalized.StartsWith("zh-cn", StringComparison.Ordinal)
            || normalized.StartsWith("zh-sg", StringComparison.Ordinal)) return "Hans";
        return null;
    }

    private static IEnumerable<XElement> StyleChain(XElement? style, XDocument styles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        XElement? current = style;
        while (current is not null)
        {
            string id = current.Attribute(W + "styleId")?.Value ?? string.Empty;
            if (!seen.Add(id)) yield break;
            yield return current;
            string? basedOn = current.Element(W + "basedOn")?.Attribute(W + "val")?.Value;
            current = string.IsNullOrWhiteSpace(basedOn) ? null : styles.Descendants(W + "style")
                .FirstOrDefault(candidate => string.Equals(candidate.Attribute(W + "styleId")?.Value, basedOn, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void MergeProperties(XElement target, XElement? source)
    {
        if (source is null) return;
        foreach (XElement child in source.Elements())
        {
            target.Element(child.Name)?.Remove();
            target.Add(new XElement(child));
        }
    }

    private static string CanonicalFont(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string candidate = value.Trim();
        if (candidate.Equals("DFKai-SB", StringComparison.OrdinalIgnoreCase) || candidate.Equals("標楷體", StringComparison.Ordinal)) candidate = "KaiU";
        else if (candidate.Equals("新細明體", StringComparison.Ordinal)) candidate = "PMingLiU";
        else if (candidate.Equals("微軟正黑體", StringComparison.Ordinal)) candidate = "Microsoft JhengHei";
        if (ClickraFontResolver.TryGetCanonicalTemplateFamily(candidate, out string canonical)) return canonical;
        throw new InvalidDataException($"Word template font '{value}' is not supported by both PDF and DOCX output.");
    }

    private static double ReadHalfPoints(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out double halfPoints) && halfPoints is >= 12 and <= 144
            ? halfPoints / 2d
            : fallback;

    private static double ReadTwips(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out double twips)
            ? twips / 20d
            : fallback;

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"Word template is missing {entryName}.");
        if (entry.Length <= 0 || entry.Length > MaxXmlBytes) throw new InvalidDataException($"Word template part {entryName} is empty or too large.");
        using Stream stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaxXmlBytes,
            XmlResolver = null
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static XDocument? ReadOptionalXml(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null) return null;
        if (entry.Length <= 0 || entry.Length > MaxXmlBytes) throw new InvalidDataException($"Word template part {entryName} is empty or too large.");
        using Stream stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaxXmlBytes,
            XmlResolver = null
        });
        return XDocument.Load(reader, LoadOptions.None);
    }
}
