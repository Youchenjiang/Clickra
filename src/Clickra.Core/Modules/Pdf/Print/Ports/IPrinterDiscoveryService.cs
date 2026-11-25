using System.Drawing.Printing;

namespace Clickra.Core.Modules.Pdf.Print.Ports;

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
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static class CacheHolder
    {
        internal static volatile IReadOnlyList<string>? InstalledPrinters;
        internal static volatile string? DefaultPrinter;
        internal static DateTime LastRefreshUtc = DateTime.MinValue;
        internal static readonly object SyncLock = new();

        internal static bool HasCache => InstalledPrinters != null && (DateTime.UtcNow - LastRefreshUtc) < CacheTtl;
        internal static bool IsCacheValid => InstalledPrinters != null && (DateTime.UtcNow - LastRefreshUtc) < CacheTtl;
        internal static bool IsDefaultCacheValid => DefaultPrinter != null && (DateTime.UtcNow - LastRefreshUtc) < CacheTtl;

        internal static void SetInstalledPrinters(IReadOnlyList<string> list)
        {
            InstalledPrinters = list;
            LastRefreshUtc = DateTime.UtcNow;
        }

        internal static void SetDefaultPrinter(string? name)
        {
            DefaultPrinter = name;
        }
    }

    public static bool HasCache => CacheHolder.HasCache;

    public static void WarmUp()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var service = new SystemPrinterDiscoveryService();
                service.GetInstalledPrinters();
                var def = service.GetDefaultPrinter();
                if (!string.IsNullOrEmpty(def))
                {
                    service.CanDuplex(def);
                    service.GetSupportedPaperSizes(def);
                }
            }
            catch
            {
                // Best effort background warm-up
            }
        });
    }

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        if (CacheHolder.IsCacheValid)
        {
            return CacheHolder.InstalledPrinters!;
        }

        lock (CacheHolder.SyncLock)
        {
            if (CacheHolder.IsCacheValid)
            {
                return CacheHolder.InstalledPrinters!;
            }

            var list = new List<string>();
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    list.Add(printer);
                }
            }
            CacheHolder.SetInstalledPrinters(list);
            return list;
        }
    }

    public string? GetDefaultPrinter()
    {
        if (CacheHolder.IsDefaultCacheValid)
        {
            return CacheHolder.DefaultPrinter;
        }

        try
        {
            var settings = new PrinterSettings();
            string? name = settings.IsValid ? settings.PrinterName : null;
            CacheHolder.SetDefaultPrinter(name);
            return name;
        }
        catch
        {
            return null;
        }
    }

    public bool IsPrinterInstalled(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;
        return GetInstalledPrinters().Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase));
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
