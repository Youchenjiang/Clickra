namespace Clickra.Core.Processors;

/// <summary>Renderer-neutral presentation contract shared by Markdown PDF and DOCX backends.</summary>
public sealed record MarkdownDocumentTemplate(
    string Id,
    MarkdownTypography Typography,
    MarkdownLayout Layout,
    MarkdownPalette Palette);

public sealed record MarkdownTypography(
    string LatinFont,
    string CjkFont,
    string MonospaceFont,
    double BodySizePoints,
    double LineHeightPoints,
    MarkdownHeadingScale Headings);

public sealed record MarkdownHeadingScale(
    double H1,
    double H2,
    double H3,
    double H4,
    double H5,
    double H6);

public sealed record MarkdownLayout(
    double MarginPoints,
    double BlockGapPoints,
    double FirstLineIndentPoints,
    bool CenterH1,
    bool JustifyBody,
    bool AccentH2,
    bool DrawH2Bar,
    bool FillTableHeader,
    double QuoteBarWidthPoints,
    double? MarginTopPoints = null,
    double? MarginRightPoints = null,
    double? MarginBottomPoints = null,
    double? MarginLeftPoints = null)
{
    public double EffectiveMarginTopPoints => MarginTopPoints ?? MarginPoints;
    public double EffectiveMarginRightPoints => MarginRightPoints ?? MarginPoints;
    public double EffectiveMarginBottomPoints => MarginBottomPoints ?? MarginPoints;
    public double EffectiveMarginLeftPoints => MarginLeftPoints ?? MarginPoints;
}

public sealed record MarkdownPalette(
    MarkdownThemeColor Body,
    MarkdownThemeColor Strong,
    MarkdownThemeColor Accent,
    MarkdownThemeColor SoftAccent,
    MarkdownThemeColor Border);

public readonly record struct MarkdownThemeColor(byte R, byte G, byte B)
{
    public string Hex => $"{R:X2}{G:X2}{B:X2}";
}

/// <summary>Derived neutral surfaces shared by the PDF and DOCX renderers.</summary>
public sealed record MarkdownResolvedPalette(
    MarkdownThemeColor TableHeader,
    MarkdownThemeColor Surface,
    MarkdownThemeColor QuoteBar)
{
    public static MarkdownResolvedPalette Create(MarkdownDocumentTemplate template) =>
        template.Id == MarkdownPdfOptions.ThemeDefault
            ? new(template.Palette.SoftAccent, new(248, 250, 252), new(148, 163, 184))
            : new(template.Palette.SoftAccent, new(255, 255, 255), template.Palette.Border);
}

/// <summary>
/// Concrete print-layout metrics resolved once from a template and text scale.
/// Both PDF and DOCX backends consume this contract so they do not invent
/// separate spacing, indentation, table, and code-block geometry.
/// </summary>
public sealed record MarkdownResolvedLayout(
    double Scale,
    double BodySizePoints,
    double BodyLineHeightPoints,
    double BlockGapPoints,
    double ListIndentPoints,
    double ListAfterPoints,
    double QuoteIndentPoints,
    double QuoteVerticalPaddingPoints,
    double CodeFontSizePoints,
    double CodeLineHeightPoints,
    double CodeHorizontalPaddingPoints,
    double CodeVerticalPaddingPoints,
    double TableFontSizePoints,
    double TableLineHeightPoints,
    double TableHorizontalPaddingPoints,
    double TableVerticalPaddingPoints)
{
    public static MarkdownResolvedLayout Create(MarkdownDocumentTemplate template, double scale) => new(
        scale,
        template.Typography.BodySizePoints * scale,
        template.Typography.LineHeightPoints * scale,
        template.Layout.BlockGapPoints * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 15 : 20) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 15 : 0) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 13.5 : 16) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 8 : 0) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 10.875 : 9.5) * scale,
        18 * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 11.25 : 10) * scale,
        5 * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 11.25 : 9.5) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 18 : 13) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 9 : 8) * scale,
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 6.375 : 4) * scale);

    public double HeadingSizePoints(MarkdownDocumentTemplate template, int level) => level switch
    {
        1 => template.Typography.Headings.H1 * Scale,
        2 => template.Typography.Headings.H2 * Scale,
        3 => template.Typography.Headings.H3 * Scale,
        4 => template.Typography.Headings.H4 * Scale,
        5 => template.Typography.Headings.H5 * Scale,
        _ => template.Typography.Headings.H6 * Scale
    };

    public double HeadingLineHeightPoints(MarkdownDocumentTemplate template, int level) =>
        HeadingSizePoints(template, level) * 1.35;

    public double HeadingBeforePoints(MarkdownDocumentTemplate template, int level) =>
        (template.Id == MarkdownPdfOptions.ThemeDefault && level == 1 ? 2 : 0) * Scale;

    public double HeadingAfterPoints(MarkdownDocumentTemplate template, int level) =>
        (template.Id == MarkdownPdfOptions.ThemeDefault && level == 1
            ? 14
            : template.Id == MarkdownPdfOptions.ThemeDefault && level == 2
                ? 7.5
                : level <= 2 ? 12 : 7) * Scale;

    public double CodeAfterPoints(MarkdownDocumentTemplate template) =>
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 13.5 : template.Layout.BlockGapPoints) * Scale;

    public double RuleBlockHeightPoints(MarkdownDocumentTemplate template) =>
        (template.Id == MarkdownPdfOptions.ThemeDefault ? 28 : 18) * Scale;
}

/// <summary>Built-in document templates. Values intentionally differ in layout, not only color.</summary>
public static class MarkdownTemplateCatalog
{
    public static MarkdownDocumentTemplate Resolve(string id, string? customTemplatePath = null) =>
        !string.IsNullOrWhiteSpace(customTemplatePath)
            ? MarkdownTemplateSource.Load(customTemplatePath)
            : ResolveBuiltIn(id);

    internal static MarkdownDocumentTemplate ResolveBuiltIn(string id) => id switch
    {
        MarkdownPdfOptions.ThemeMinimal => Minimal,
        MarkdownPdfOptions.ThemeAcademic => Academic,
        _ => Default
    };

    public static MarkdownDocumentTemplate Default { get; } = new(
        MarkdownPdfOptions.ThemeDefault,
        new MarkdownTypography(
            "Segoe UI", "Microsoft JhengHei", "Consolas", 12, 18,
            new MarkdownHeadingScale(21, 15, 12.375, 11.25, 11.25, 11.25)),
        new MarkdownLayout(51, 9, FirstLineIndentPoints: 0, CenterH1: false, JustifyBody: false, AccentH2: true, DrawH2Bar: true, FillTableHeader: true, QuoteBarWidthPoints: 3, MarginTopPoints: 51),
        new MarkdownPalette(
            new(51, 65, 85), new(30, 41, 59), new(2, 132, 199),
            new(241, 245, 249), new(203, 213, 225)));

    public static MarkdownDocumentTemplate Minimal { get; } = new(
        MarkdownPdfOptions.ThemeMinimal,
        new MarkdownTypography(
            "Segoe UI", "Microsoft JhengHei", "Courier New", 10.8, 16,
            new MarkdownHeadingScale(22, 16, 13, 11.8, 11, 10.5)),
        new MarkdownLayout(42, 6, FirstLineIndentPoints: 0, CenterH1: false, JustifyBody: false, AccentH2: false, DrawH2Bar: false, FillTableHeader: false, QuoteBarWidthPoints: 2),
        new MarkdownPalette(
            new(55, 65, 81), new(31, 41, 55), new(75, 85, 99),
            new(249, 250, 251), new(229, 231, 235)));

    public static MarkdownDocumentTemplate Academic { get; } = new(
        MarkdownPdfOptions.ThemeAcademic,
        new MarkdownTypography(
            "Times New Roman", "KaiU", "Courier New", 12, 18,
            new MarkdownHeadingScale(18, 16, 14, 12, 12, 12)),
        new MarkdownLayout(
            72, 0, FirstLineIndentPoints: 24, CenterH1: true, JustifyBody: true,
            AccentH2: false, DrawH2Bar: false, FillTableHeader: false, QuoteBarWidthPoints: 0.75,
            MarginTopPoints: 70.87, MarginRightPoints: 56.69, MarginBottomPoints: 70.87, MarginLeftPoints: 85.04),
        new MarkdownPalette(
            new(24, 24, 27), new(0, 0, 0), new(0, 0, 0),
            new(255, 255, 255), new(115, 115, 115)));
}
