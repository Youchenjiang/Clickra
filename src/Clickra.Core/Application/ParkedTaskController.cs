using System.Collections.Generic;

namespace Clickra.Core.Application;

/// <summary>
/// Application-facing operations for parked conversions. Persistence and atomic task transitions
/// remain owned by the storage layer; UI surfaces use this boundary instead of raw storage calls.
/// </summary>
public static class ParkedTaskController
{
    /// <summary>Data required to resume one parked conversion.</summary>
    public sealed record ResumeInfo(string TaskId, string Command, List<string> Files, int StartIndex);

    /// <summary>Returns the effective global parked-task retention in days.</summary>
    public static int GetRetentionDays() => ClickraStorage.GetParkedRetentionDays();

    /// <summary>Adjusts one parked task's retention override by the requested number of days.</summary>
    public static int? AdjustRetention(string taskId, int deltaDays) =>
        ClickraStorage.AdjustParkedRetention(taskId, deltaDays);

    /// <summary>Clears one parked task's retention override so it follows the global policy again.</summary>
    public static void ResetRetention(string taskId) =>
        ClickraStorage.SetParkedRetentionOverride(taskId, null);

    /// <summary>Cancels one parked task using the storage-owned atomic bookkeeping transition.</summary>
    public static void Cancel(string taskId) => ClickraStorage.CancelParkedTask(taskId);

    /// <summary>
    /// Atomically claims one parked task for resume and returns the execution data captured by the
    /// storage transition. Returns <see langword="null"/> when the task cannot be claimed.
    /// </summary>
    public static ResumeInfo? ClaimForResume(string taskId)
    {
        var resumed = ClickraStorage.ClaimParkedTaskForResume(taskId);
        return resumed is null
            ? null
            : new ResumeInfo(resumed.TaskId, resumed.Command, resumed.Files, resumed.StartIndex);
    }
}
