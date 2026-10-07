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
    bool AccentH2,
    bool DrawH2Bar,
    bool FillTableHeader,
    double QuoteBarWidthPoints);

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

/// <summary>Built-in document templates. Values intentionally differ in layout, not only color.</summary>
public static class MarkdownTemplateCatalog
{
    public static MarkdownDocumentTemplate Resolve(string id, string? customTemplatePath = null) =>
        !string.IsNullOrWhiteSpace(customTemplatePath)
            ? MarkdownTemplateFile.Load(customTemplatePath)
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
            "Segoe UI", "Microsoft JhengHei", "Courier New", 11.5, 18,
            new MarkdownHeadingScale(25, 18, 14.5, 12.5, 11.5, 11)),
        new MarkdownLayout(54, 9, FirstLineIndentPoints: 0, CenterH1: false, AccentH2: true, DrawH2Bar: true, FillTableHeader: true, QuoteBarWidthPoints: 3),
        new MarkdownPalette(
            new(51, 65, 85), new(30, 41, 59), new(2, 132, 199),
            new(240, 249, 255), new(203, 213, 225)));

    public static MarkdownDocumentTemplate Minimal { get; } = new(
        MarkdownPdfOptions.ThemeMinimal,
        new MarkdownTypography(
            "Segoe UI", "Microsoft JhengHei", "Courier New", 10.8, 16,
            new MarkdownHeadingScale(22, 16, 13, 11.8, 11, 10.5)),
        new MarkdownLayout(42, 6, FirstLineIndentPoints: 0, CenterH1: false, AccentH2: false, DrawH2Bar: false, FillTableHeader: false, QuoteBarWidthPoints: 2),
        new MarkdownPalette(
            new(55, 65, 81), new(31, 41, 55), new(75, 85, 99),
            new(249, 250, 251), new(229, 231, 235)));

    public static MarkdownDocumentTemplate Academic { get; } = new(
        MarkdownPdfOptions.ThemeAcademic,
        new MarkdownTypography(
            "Times New Roman", "PMingLiU", "Courier New", 12, 18,
            new MarkdownHeadingScale(16, 14, 12, 12, 11, 11)),
        new MarkdownLayout(72, 2, FirstLineIndentPoints: 24, CenterH1: true, AccentH2: false, DrawH2Bar: false, FillTableHeader: false, QuoteBarWidthPoints: 0.75),
        new MarkdownPalette(
            new(24, 24, 27), new(0, 0, 0), new(0, 0, 0),
            new(255, 255, 255), new(115, 115, 115)));
}
