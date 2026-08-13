using System;
using System.IO;

namespace Clickra.Core;

/// <summary>Absolute paths to well-known Windows executables, so launch sites do
/// not rely on PATH lookup (S4036).</summary>
public static class SystemPaths
{
        /// <summary>%WINDIR%\explorer.exe</summary>
        public static string Explorer =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        /// <summary>%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe</summary>
        public static string PowerShell =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
    }
