using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfPrint.Application;

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
    double DarkenGamma = 2.2
)
{
    public const string PrinterNameKey = "printer";
    public const string PageRangeKey = "pages";
    public const string CopiesKey = "copies";
    public const string DpiKey = "dpi";
    public const string FitToPageKey = "fit_to_page";
    public const string DarkenTextKey = "darken_text";
    public const string DarkenGammaKey = "darken_gamma";

    public static PrintPdfOptions FromDictionary(IReadOnlyDictionary<string, object>? dict)
    {
        if (dict is null) return new PrintPdfOptions();

        string? printer = dict.TryGetValue(PrinterNameKey, out var p) ? p?.ToString() : null;
        string? pages = dict.TryGetValue(PageRangeKey, out var pg) ? pg?.ToString() : null;
        int copies = dict.TryGetValue(CopiesKey, out var c) && int.TryParse(c?.ToString(), out int cVal) ? Math.Max(1, cVal) : 1;
        int dpi = dict.TryGetValue(DpiKey, out var d) && int.TryParse(d?.ToString(), out int dVal) ? Math.Clamp(dVal, 72, 600) : 200;
        bool fit = !dict.TryGetValue(FitToPageKey, out var f) || !bool.TryParse(f?.ToString(), out bool fVal) || fVal;
        string? lang = dict.TryGetValue(ExecutionLocalizationOptions.LanguageKey, out var l) ? l?.ToString() : null;
        bool darken = dict.TryGetValue(DarkenTextKey, out var dk) && bool.TryParse(dk?.ToString(), out bool dkVal) && dkVal;
        double gamma = dict.TryGetValue(DarkenGammaKey, out var gm) && double.TryParse(gm?.ToString(), out double gmVal) && gmVal > 0.1 ? gmVal : 2.2;

        return new PrintPdfOptions(printer, pages, copies, dpi, fit, lang, darken, gamma);
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
        if (!string.IsNullOrWhiteSpace(Language)) dict[ExecutionLocalizationOptions.LanguageKey] = Language;
        return dict;
    }
}

