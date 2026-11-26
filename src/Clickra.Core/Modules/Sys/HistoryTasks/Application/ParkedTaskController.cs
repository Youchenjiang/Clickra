using System.Collections.Generic;

namespace Clickra.Core.Modules.Sys.HistoryTasks.Application;

/// <summary>
/// Application-facing operations for parked conversions. Persistence and atomic task transitions
/// remain owned by the storage layer; UI surfaces use this boundary instead of raw storage calls.
/// </summary>
public static class ParkedTaskController
{
    /// <summary>Data required to resume one parked conversion.</summary>
    public sealed record ResumeInfo(string TaskId, string Command, List<string> Files, int StartIndex);

    /// <summary>
    /// Puts a task that was claimed for resume back into 待繼續 when the window cannot run it after all,
    /// so the claim never loses the task.
    /// </summary>
    public static void ReturnToParked(string taskId, string reason, int nextIndex) =>
        ClickraStorage.ParkTask(taskId, reason, nextIndex);

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
