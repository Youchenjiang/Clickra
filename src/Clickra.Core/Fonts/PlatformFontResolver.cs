using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Clickra.Core.Fonts;

/// <summary>
/// Cross-platform font file and directory discovery service.
/// Resolves font paths across Windows, Linux, and macOS without hardcoding Windows paths.
/// </summary>
public static class PlatformFontResolver
{
    private const string FontsDirectoryName = "Fonts";
    private const string EnvironmentFontsDirectoryKey = "CLICKRA_FONTS_DIR";

    /// <summary>
    /// Enumerates candidate directories containing system or custom fonts for the current platform.
    /// </summary>
    public static IEnumerable<string> GetFontDirectories()
    {
        string? customDir = Environment.GetEnvironmentVariable(EnvironmentFontsDirectoryKey);
        if (!string.IsNullOrWhiteSpace(customDir) && Directory.Exists(customDir))
        {
            yield return customDir;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(systemDir))
            {
                yield return Path.Combine(systemDir, "..", FontsDirectoryName);
            }

            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir))
            {
                yield return Path.Combine(windowsDir, FontsDirectoryName);
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return Path.Combine(localAppData, "Microsoft", "Windows", FontsDirectoryName);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "share", "fonts");
                yield return Path.Combine(home, ".fonts");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, "Library", "Fonts");
            }
        }
    }

    /// <summary>
    /// Searches for a font file by name in the candidate font directories.
    /// </summary>
    public static string? ResolveFontFilePath(string fontFileName)
    {
        if (string.IsNullOrWhiteSpace(fontFileName))
            return null;

        foreach (string dir in GetFontDirectories())
        {
            if (!Directory.Exists(dir))
                continue;

            string directPath = Path.Combine(dir, fontFileName);
            if (File.Exists(directPath))
                return directPath;

            // On Unix systems, fonts may be placed in subdirectories (e.g. /usr/share/fonts/truetype/...)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir, fontFileName, SearchOption.AllDirectories))
                    {
                        if (File.Exists(file))
                            return file;
                    }
                }
                catch
                {
                    // Ignore inaccessible subdirectories during enumeration
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the fallback font file path (e.g. Arial, LiberationSans, DejaVuSans) across platforms.
    /// </summary>
    public static string? ResolveFallbackFontPath()
    {
        string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "arial.ttf", "segoeui.ttf", "calibri.ttf" }
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new[] { "Arial.ttf", "Helvetica.ttc", "Times New Roman.ttf" }
                : new[] { "LiberationSans-Regular.ttf", "DejaVuSans.ttf", "FreeSans.ttf", "arial.ttf" };

        foreach (string candidate in candidates)
        {
            string? path = ResolveFontFilePath(candidate);
            if (path is not null)
                return path;
        }

        return null;
    }
}
