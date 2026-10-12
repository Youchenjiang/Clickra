using System.IO;

namespace Clickra.Core.Modules.HistoryTasks.Application;

/// <summary>Application-facing history persistence operations used by presentation surfaces.</summary>
public static class HistoryController
{
    /// <summary>Clears completed conversion history without affecting active or parked tasks.</summary>
    public static void Clear() => ClickraStorage.ClearHistory();

    /// <summary>Returns the persisted history log path, optionally rooted in a platform-provided data directory.</summary>
    public static string GetLogPath(string? preferredDataDirectory = null) =>
        Path.Combine(preferredDataDirectory ?? ClickraStorage.GetDataDir(), ClickraStorage.HistoryFileName);
}
