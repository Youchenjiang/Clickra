using System;
using System.IO;

namespace Clickra.Core.Storage;

/// <summary>Resolves persisted output-directory policy into concrete filesystem paths.</summary>
internal static class StoragePathResolver
{
    internal static string ResolveOutputDirectory(string sourceFilePath)
    {
        string mode = SettingsStore.Get(ClickraSettings.OutputDir);
        if (mode.Equals(ClickraSettings.OutputDirDesktop, StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        if (mode.Equals(ClickraSettings.OutputDirDownloads, StringComparison.OrdinalIgnoreCase))
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(downloads)
                ? downloads
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        if (!mode.Equals(ClickraSettings.DefaultOutputDirSource, StringComparison.OrdinalIgnoreCase) && Directory.Exists(mode))
            return mode;

        return Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
    }
}
