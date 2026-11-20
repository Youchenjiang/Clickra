using System;
using System.IO;

namespace Clickra.Core;

/// <summary>Absolute paths to well-known system executables, so launch sites do
/// not rely on PATH lookup (S4036).</summary>
public static class SystemPaths
{
    /// <summary>%WINDIR%\explorer.exe</summary>
    public static string Explorer =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>/usr/bin/open on macOS</summary>
    public static string MacOpen => "/usr/bin/open";

    /// <summary>/usr/bin/xdg-open on Linux</summary>
    public static string LinuxXdgOpen => "/usr/bin/xdg-open";

    /// <summary>%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe</summary>
    public static string PowerShell =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
}
