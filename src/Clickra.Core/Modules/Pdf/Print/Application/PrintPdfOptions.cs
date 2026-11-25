using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Pdf.Print.Application;

/// <summary>
/// Specifies duplex printing mode.
/// </summary>
public enum PrintDuplexMode
{
    Simplex = 0,
    DuplexLongEdge = 1,
    DuplexShortEdge = 2
}

/// <summary>
/// Specifies page orientation for printing.
/// </summary>
public enum PrintOrientation
{
    Auto = 0,
    Portrait = 1,
    Landscape = 2
}

/// <summary>
/// Execution parameters for printing a PDF file.
/// </summary>
public sealed record PrintPdfOptions
{
    public const string PrinterNameKey = "printer";
    public const string PageRangeKey = "pages";
    public const string CopiesKey = "copies";
    public const string DpiKey = "dpi";
    public const string FitToPageKey = "fit_to_page";
    public const string DarkenTextKey = "darken_text";
    public const string DarkenGammaKey = "darken_gamma";
    public const string DuplexKey = "duplex";
    public const string PaperSizeKey = "paper_size";
    public const string OrientationKey = "orientation";
    public const string PagesPerSheetKey = "pages_per_sheet";
    public const string PageOrderKey = "page_order";
    public const string PageBorderKey = "page_border";
    public const string ScaleKey = "scale";
    public const string ScalePercentKey = "scale_percent";
    public const string GrayscaleKey = "grayscale";
    public const string CollateKey = "collate";

    public string? PrinterName { get; init; }
    public string? PageRange { get; init; }
    public int Copies { get; init; }
    public int Dpi { get; init; }
    public bool FitToPage { get; init; }
    public string? Language { get; init; }
    public bool DarkenText { get; init; }
    public double DarkenGamma { get; init; }
    public PrintDuplexMode Duplex { get; init; }
    public string? PaperSize { get; init; }
    public PrintOrientation Orientation { get; init; }
    public int PagesPerSheet { get; init; }
    private const string ScaleFit = "fit";
    private const string ScaleActual = "actual";
    private const string ScaleCustom = "custom";

    public string PageOrder { get; init; }
    public bool PageBorder { get; init; }
    public string Scale { get; init; }
    public int ScalePercent { get; init; }
    public bool Grayscale { get; init; }
    public bool Collate { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S107", Justification = "Print options encapsulate all document printing settings with default parameters.")]
    public PrintPdfOptions(
        string? printerName = null,
        string? pageRange = null,
        int copies = 1,
        int dpi = 200,
        bool fitToPage = true,
        string? language = null,
        bool darkenText = false,
        double darkenGamma = 2.2,
        PrintDuplexMode duplex = PrintDuplexMode.Simplex,
        string? paperSize = null,
        PrintOrientation orientation = PrintOrientation.Auto,
        int pagesPerSheet = 1,
        string pageOrder = "Z",
        bool pageBorder = false,
        string? scale = null,
        int scalePercent = 100,
        bool grayscale = false,
        bool collate = true)
    {
        PrinterName = printerName;
        PageRange = pageRange;
        Copies = Math.Clamp(copies, 1, 999);
        Dpi = Math.Clamp(dpi, 72, 600);
        if (!string.IsNullOrWhiteSpace(scale))
        {
            Scale = NormalizeScale(scale);
            FitToPage = string.Equals(Scale, ScaleFit, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            FitToPage = fitToPage;
            Scale = fitToPage ? ScaleFit : ScaleActual;
        }
        Language = language;
        DarkenText = darkenText;
        DarkenGamma = darkenGamma >= 0.1 ? darkenGamma : 2.2;
        Duplex = duplex;
        PaperSize = paperSize;
        Orientation = orientation;
        PagesPerSheet = pagesPerSheet is 1 or 2 or 4 or 6 or 9 or 16 ? pagesPerSheet : 1;
        PageOrder = string.Equals(pageOrder, "N", StringComparison.OrdinalIgnoreCase) ? "N" : "Z";
        PageBorder = pageBorder;
        ScalePercent = Math.Clamp(scalePercent, 10, 400);
        Grayscale = grayscale;
        Collate = collate;
    }

    public static string NormalizeScale(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return ScaleFit;
        string s = str.Trim().ToLowerInvariant();
        return s switch
        {
            ScaleActual => ScaleActual,
            ScaleCustom => ScaleCustom,
            _ => ScaleFit
        };
    }

    public static PrintPdfOptions FromDictionary(IReadOnlyDictionary<string, object>? dict)
    {
        if (dict is null) return new PrintPdfOptions();

        string? printer = dict.TryGetValue(PrinterNameKey, out var p) ? p?.ToString() : null;
        string? pages = dict.TryGetValue(PageRangeKey, out var pg) ? pg?.ToString() : null;
        int copies = ParseInt(dict, CopiesKey, 1, 1, 999);
        int dpi = ParseInt(dict, DpiKey, 200, 72, 600);
        bool fit = ParseBool(dict, FitToPageKey, true);
        string? lang = dict.TryGetValue(ExecutionLocalizationOptions.LanguageKey, out var l) ? l?.ToString() : null;
        bool darken = ParseBool(dict, DarkenTextKey, false);
        double gamma = ParseDouble(dict, DarkenGammaKey, 2.2, 0.1);
        PrintDuplexMode duplex = ParseDuplex(dict);
        string? paper = dict.TryGetValue(PaperSizeKey, out var pSz) ? pSz?.ToString() : null;
        PrintOrientation orientation = ParseOrientation(dict);

        int pagesPerSheet = ParsePagesPerSheet(dict);
        string pageOrder = ParsePageOrder(dict);
        bool pageBorder = ParseBool(dict, PageBorderKey, false);
        string scale = ParseScale(dict, fit);
        int scalePercent = ParseInt(dict, ScalePercentKey, 100, 10, 400);
        bool grayscale = ParseBool(dict, GrayscaleKey, false);
        bool collate = ParseBool(dict, CollateKey, true);

        return new PrintPdfOptions(
            printer, pages, copies, dpi, fit, lang, darken, gamma, duplex, paper, orientation,
            pagesPerSheet, pageOrder, pageBorder, scale, scalePercent, grayscale, collate);
    }

    private static string ParseScale(IReadOnlyDictionary<string, object> dict, bool fallbackFitToPage)
    {
        if (dict.TryGetValue(ScaleKey, out var s) && s is not null && !string.IsNullOrWhiteSpace(s.ToString()))
        {
            return NormalizeScale(s.ToString());
        }
        if (dict.TryGetValue(FitToPageKey, out _))
        {
            return fallbackFitToPage ? "fit" : "actual";
        }
        return "fit";
    }

    private static int ParsePagesPerSheet(IReadOnlyDictionary<string, object> dict)
    {
        if (dict.TryGetValue(PagesPerSheetKey, out var val) && int.TryParse(val?.ToString(), out int parsed))
        {
            return parsed is 1 or 2 or 4 or 6 or 9 or 16 ? parsed : 1;
        }
        return 1;
    }

    private static string ParsePageOrder(IReadOnlyDictionary<string, object> dict)
    {
        if (dict.TryGetValue(PageOrderKey, out var val) && val is not null)
        {
            return string.Equals(val.ToString()?.Trim(), "N", StringComparison.OrdinalIgnoreCase) ? "N" : "Z";
        }
        return "Z";
    }

    private static int ParseInt(IReadOnlyDictionary<string, object> dict, string key, int defaultVal, int min, int max)
    {
        if (dict.TryGetValue(key, out var val) && int.TryParse(val?.ToString(), out int parsed))
        {
            return Math.Clamp(parsed, min, max);
        }
        return defaultVal;
    }

    private static bool ParseBool(IReadOnlyDictionary<string, object> dict, string key, bool defaultVal)
    {
        if (dict.TryGetValue(key, out var val) && bool.TryParse(val?.ToString(), out bool parsed))
        {
            return parsed;
        }
        return defaultVal;
    }

    private static double ParseDouble(IReadOnlyDictionary<string, object> dict, string key, double defaultVal, double min)
    {
        if (dict.TryGetValue(key, out var val) && double.TryParse(val?.ToString(), out double parsed) && parsed >= min)
        {
            return parsed;
        }
        return defaultVal;
    }

    private static PrintDuplexMode ParseDuplex(IReadOnlyDictionary<string, object> dict)
    {
        if (!dict.TryGetValue(DuplexKey, out var dx) || dx is null)
        {
            return PrintDuplexMode.Simplex;
        }
        string dxStr = dx.ToString()!;
        if (Enum.TryParse<PrintDuplexMode>(dxStr, true, out var parsedDx))
        {
            return parsedDx;
        }
        if (dxStr.Equals("long", StringComparison.OrdinalIgnoreCase) || dxStr.Equals("vertical", StringComparison.OrdinalIgnoreCase))
        {
            return PrintDuplexMode.DuplexLongEdge;
        }
        if (dxStr.Equals("short", StringComparison.OrdinalIgnoreCase) || dxStr.Equals("horizontal", StringComparison.OrdinalIgnoreCase))
        {
            return PrintDuplexMode.DuplexShortEdge;
        }
        return PrintDuplexMode.Simplex;
    }

    public static PrintOrientation ParseOrientation(string? str)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            return PrintOrientation.Auto;
        }
        if (Enum.TryParse<PrintOrientation>(str, true, out var parsedO))
        {
            return parsedO;
        }
        if (str.Equals("p", StringComparison.OrdinalIgnoreCase) || str.Equals("portrait", StringComparison.OrdinalIgnoreCase) || str.Equals("vertical", StringComparison.OrdinalIgnoreCase))
        {
            return PrintOrientation.Portrait;
        }
        if (str.Equals("l", StringComparison.OrdinalIgnoreCase) || str.Equals("landscape", StringComparison.OrdinalIgnoreCase) || str.Equals("horizontal", StringComparison.OrdinalIgnoreCase))
        {
            return PrintOrientation.Landscape;
        }
        return PrintOrientation.Auto;
    }

    private static PrintOrientation ParseOrientation(IReadOnlyDictionary<string, object> dict)
    {
        if (!dict.TryGetValue(OrientationKey, out var o) || o is null)
        {
            return PrintOrientation.Auto;
        }
        if (o is PrintOrientation po) return po;
        return ParseOrientation(o.ToString());
    }

    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(PrinterName)) dict[PrinterNameKey] = PrinterName;
        if (!string.IsNullOrWhiteSpace(PageRange)) dict[PageRangeKey] = PageRange;
        dict[CopiesKey] = Copies;
        dict[DpiKey] = Dpi;
        dict[FitToPageKey] = FitToPage;
        if (!string.Equals(Scale, "fit", StringComparison.OrdinalIgnoreCase)) dict[ScaleKey] = Scale;
        if (PagesPerSheet != 1) dict[PagesPerSheetKey] = PagesPerSheet;
        if (!string.Equals(PageOrder, "Z", StringComparison.OrdinalIgnoreCase)) dict[PageOrderKey] = PageOrder;
        if (PageBorder) dict[PageBorderKey] = true;
        if (ScalePercent != 100) dict[ScalePercentKey] = ScalePercent;
        if (Grayscale) dict[GrayscaleKey] = true;
        if (!Collate) dict[CollateKey] = false;
        if (DarkenText) dict[DarkenTextKey] = true;
        if (Math.Abs(DarkenGamma - 2.2) > 0.01) dict[DarkenGammaKey] = DarkenGamma;
        if (Duplex != PrintDuplexMode.Simplex) dict[DuplexKey] = Duplex.ToString();
        if (!string.IsNullOrWhiteSpace(PaperSize)) dict[PaperSizeKey] = PaperSize;
        if (Orientation != PrintOrientation.Auto) dict[OrientationKey] = Orientation.ToString();
        if (!string.IsNullOrWhiteSpace(Language)) dict[ExecutionLocalizationOptions.LanguageKey] = Language;
        return dict;
    }

    /// <summary>
    /// Like <see cref="ToDictionary"/>, but also writes duplex, orientation, darken-text, and new print options when they
    /// equal the type defaults. A dialog that shows every choice must pass them all: a key left out is
    /// filled from the saved defaults by <c>PrintPdfUseCase</c>, so "Auto" on screen could print as a
    /// saved "Portrait".
    /// </summary>
    public Dictionary<string, object> ToExplicitDictionary()
    {
        var dict = ToDictionary();
        dict[DuplexKey] = Duplex.ToString();
        dict[OrientationKey] = Orientation.ToString();
        dict[DarkenTextKey] = DarkenText;
        dict[PagesPerSheetKey] = PagesPerSheet;
        dict[PageOrderKey] = PageOrder;
        dict[PageBorderKey] = PageBorder;
        dict[ScaleKey] = Scale;
        dict[ScalePercentKey] = ScalePercent;
        dict[GrayscaleKey] = Grayscale;
        dict[CollateKey] = Collate;
        return dict;
    }
}
