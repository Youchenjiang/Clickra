using System.Drawing.Printing;

namespace Clickra.Core.Modules.PdfPrint.Ports;

/// <summary>
/// Provides printer discovery and paper size query services.
/// </summary>
public interface IPrinterDiscoveryService
{
    /// <summary>
    /// Gets all installed printer names.
    /// </summary>
    IReadOnlyList<string> GetInstalledPrinters();

    /// <summary>
    /// Gets the name of the system default printer, or null if none is installed.
    /// </summary>
    string? GetDefaultPrinter();

    /// <summary>
    /// Checks whether the specified printer exists.
    /// </summary>
    bool IsPrinterInstalled(string printerName);

    /// <summary>
    /// Gets the supported paper size names for the specified printer (or default printer if null).
    /// </summary>
    IReadOnlyList<string> GetSupportedPaperSizes(string? printerName = null);

    /// <summary>
    /// Checks whether the specified printer (or default printer if null) supports duplex printing.
    /// </summary>
    bool CanDuplex(string? printerName = null);
}

/// <summary>
/// Default implementation of printer discovery backed by System.Drawing.Printing.PrinterSettings.
/// </summary>
public sealed class SystemPrinterDiscoveryService : IPrinterDiscoveryService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> PaperSizesCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> DuplexCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        var list = new List<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            if (!string.IsNullOrWhiteSpace(printer))
            {
                list.Add(printer);
            }
        }
        return list;
    }

    public string? GetDefaultPrinter()
    {
        try
        {
            var settings = new PrinterSettings();
            return settings.IsValid ? settings.PrinterName : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsPrinterInstalled(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;

        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(printer, printerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public IReadOnlyList<string> GetSupportedPaperSizes(string? printerName = null)
    {
        string key = printerName?.Trim() ?? string.Empty;
        return PaperSizesCache.GetOrAdd(key, QuerySupportedPaperSizes);
    }

    public bool CanDuplex(string? printerName = null)
    {
        string key = printerName?.Trim() ?? string.Empty;
        return DuplexCache.GetOrAdd(key, QueryCanDuplex);
    }

    private static IReadOnlyList<string> QuerySupportedPaperSizes(string key)
    {
        try
        {
            var settings = new PrinterSettings();
            if (!string.IsNullOrWhiteSpace(key))
            {
                settings.PrinterName = key;
            }

            if (!settings.IsValid) return Array.Empty<string>();

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (PaperSize paper in settings.PaperSizes)
            {
                if (!string.IsNullOrWhiteSpace(paper.PaperName) && set.Add(paper.PaperName))
                {
                    list.Add(paper.PaperName);
                }
            }
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool QueryCanDuplex(string key)
    {
        try
        {
            var settings = new PrinterSettings();
            if (!string.IsNullOrWhiteSpace(key))
            {
                settings.PrinterName = key;
            }
            return settings.IsValid && settings.CanDuplex;
        }
        catch
        {
            return false;
        }
    }
}
