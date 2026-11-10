using System;
using System.IO;
using System.Linq;

namespace Clickra.Core.Modules.File.BatchRename.Processing;

/// <summary>Enforces Windows filesystem naming rules and detects hazardous filename patterns.</summary>
public static class RenameSafetyGuard
{
    private static readonly char[] InvalidFilenameChars = Path.GetInvalidFileNameChars();

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// Validates whether a proposed filename conforms to Windows naming specifications.
    /// Returns null if valid, or a descriptive error message if invalid.
    /// </summary>
    public static string? ValidateFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return "Filename cannot be empty or whitespace.";
        }

        if (filename.IndexOfAny(InvalidFilenameChars) >= 0)
        {
            return "Filename contains invalid characters (\\, /, :, *, ?, \", <, >, |).";
        }

        if (filename.EndsWith(' ') || filename.EndsWith('.'))
        {
            return "Filename cannot end with a space or period.";
        }

        string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        if (ReservedDeviceNames.Any(r => string.Equals(r, nameWithoutExt, StringComparison.OrdinalIgnoreCase)))
        {
            return $"'{nameWithoutExt}' is a reserved Windows system device name.";
        }

        return null;
    }
}
