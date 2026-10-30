using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfPrint.Application;

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
/// Execution parameters for printing a PDF file.
/// </summary>
public sealed record PrintPdfOptions(
    string? PrinterName = null,
    string? PageRange = null,
    int Copies = 1,
    int Dpi = 200,
    bool FitToPage = true,
    string? Language = null,
    bool DarkenText = false,
    double DarkenGamma = 2.2,
    PrintDuplexMode Duplex = PrintDuplexMode.Simplex,
    string? PaperSize = null
)
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

        return new PrintPdfOptions(printer, pages, copies, dpi, fit, lang, darken, gamma, duplex, paper);
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

    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(PrinterName)) dict[PrinterNameKey] = PrinterName;
        if (!string.IsNullOrWhiteSpace(PageRange)) dict[PageRangeKey] = PageRange;
        dict[CopiesKey] = Copies;
        dict[DpiKey] = Dpi;
        dict[FitToPageKey] = FitToPage;
        if (DarkenText) dict[DarkenTextKey] = true;
        if (Math.Abs(DarkenGamma - 2.2) > 0.01) dict[DarkenGammaKey] = DarkenGamma;
        if (Duplex != PrintDuplexMode.Simplex) dict[DuplexKey] = Duplex.ToString();
        if (!string.IsNullOrWhiteSpace(PaperSize)) dict[PaperSizeKey] = PaperSize;
        if (!string.IsNullOrWhiteSpace(Language)) dict[ExecutionLocalizationOptions.LanguageKey] = Language;
        return dict;
    }
}
