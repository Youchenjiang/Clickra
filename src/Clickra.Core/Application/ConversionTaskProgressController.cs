namespace Clickra.Core.Application;

/// <summary>Application-facing best-effort progress persistence for Native conversion presentation.</summary>
public static class ConversionTaskProgressController
{
    /// <summary>
    /// Records the current file index in both the legacy singleton record and, when present, the
    /// per-task record. Persistence failures remain advisory and never abort conversion output.
    /// </summary>
    public static void RecordFileStarting(string? taskId, int index)
    {
        Try(() => ClickraStorage.SetActiveRecordIndex(index));
        if (!string.IsNullOrWhiteSpace(taskId))
            Try(() => ClickraStorage.SetTaskIndex(taskId, index));
    }

    private static void Try(System.Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Native progress persistence is advisory; conversion output remains authoritative.
        }
    }
}
