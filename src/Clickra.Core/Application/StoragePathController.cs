using System.IO;

namespace Clickra.Core.Application;

/// <summary>Application-facing access to Clickra-owned data and output locations.</summary>
public static class StoragePathController
{
    /// <summary>Returns Clickra's persisted data directory.</summary>
    public static string GetDataDirectory() => ClickraStorage.GetDataDir();

    /// <summary>Returns the output directory selected for the given source path.</summary>
    public static string GetOutputDirectory(string sourceFilePath) => ClickraStorage.GetOutputDir(sourceFilePath);

    /// <summary>Returns the directory used for downloaded application-managed packages.</summary>
    public static string GetDownloadsDirectory() => Path.Combine(GetDataDirectory(), ClickraSettings.OutputDirDownloads);
}
