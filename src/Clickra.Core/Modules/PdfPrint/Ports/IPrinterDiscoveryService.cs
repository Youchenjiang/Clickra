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
}

/// <summary>
/// Default implementation of printer discovery backed by System.Drawing.Printing.PrinterSettings.
/// </summary>
public sealed class SystemPrinterDiscoveryService : IPrinterDiscoveryService
{
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
}
