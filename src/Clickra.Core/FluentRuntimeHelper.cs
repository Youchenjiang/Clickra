using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace Clickra.Core;

/// <summary>
/// Detects whether the Windows App Runtime (needed for Fluent/WinUI 3) is available.
/// </summary>
public static class FluentRuntimeHelper
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    internal const string StorePublisher = "CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

    /// <summary>
    /// Returns true if Clickra.Fluent.exe is present in the same directory as the current executable,
    /// meaning the optional Fluent package has been installed.
    /// </summary>
    public static bool IsFluentPackageInstalled()
    {
        string exeDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(exeDir))
            exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? ".";
        return File.Exists(Path.Combine(exeDir, "Clickra.Fluent.exe"));
    }

    /// <summary>
    /// Returns true if the Windows App Runtime bootstrap DLL is present on the system,
    /// meaning Clickra.Fluent.exe can be launched successfully.
    /// </summary>
    public static bool IsWinAppRuntimeAvailable()
    {
        try
        {
            IntPtr h = LoadLibraryExW("Microsoft.WindowsAppRuntime.Bootstrap.dll",
                                      IntPtr.Zero, LoadLibrarySearchSystem32);
            return h != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Combined check: Fluent package installed AND WinAppRuntime available.
    /// </summary>
    public static bool IsAvailable() => IsFluentPackageInstalled() && IsWinAppRuntimeAvailable();

    /// <summary>
    /// Returns true only for the Microsoft Store package identity that can use
    /// the Store-owned Fluent optional package. Direct-download builds use a
    /// different Publisher so they must not offer this Store add-on.
    /// </summary>
    public static bool SupportsStoreFluentAddon()
    {
        try
        {
            return IsStorePublisher(Package.Current.Id.Publisher);
        }
        catch
        {
            // Unpackaged or otherwise unverifiable identity: fail closed.
            return false;
        }
    }

    internal static bool IsStorePublisher(string? publisher) =>
        string.Equals(publisher, StorePublisher, StringComparison.Ordinal);

    /// <summary>
    /// Store page for the Clickra Fluent optional package.
    /// Current value is the placeholder Product ID used during development.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S1075", Justification = "Store URI is a fixed product identifier, not user-supplied input.")]
    public const string StoreUri = "ms-windows-store://pdp/?productid=Clickra.Fluent";
}
