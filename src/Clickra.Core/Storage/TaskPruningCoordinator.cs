using System;
using System.Diagnostics;
using System.IO;

namespace Clickra.Core.Storage;

/// <summary>Owns stale, completed, and orphaned task-file cleanup decisions.</summary>
internal static class TaskPruningCoordinator
{
    private const int CompletedTaskTtlMinutes = 10;
    private const int AbandonedTaskTtlHours = 24;

    internal static void Prune()
    {
        try
        {
            TaskRecordStore.EnsureDirectory();
            PruneResidualUnlockFiles();
            Diagnostics.ClickraLog.CleanExpiredLogs();
            if (!Directory.Exists(StorageRuntime.TasksDirectory)) return;
            DateTime now = DateTime.UtcNow;
            foreach (string file in Directory.GetFiles(StorageRuntime.TasksDirectory, $"{StorageRuntime.TaskFilePrefix}*.tmp"))
            {
                try { PruneSingleTaskFile(file, now); }
                catch (Exception ex)
                {
                    Diagnostics.ClickraLog.Warn("Cleanup", $"Failed to prune task file {Path.GetFileName(file)}", ex);
                }
            }
        }
        catch
        {
            // Cleanup is opportunistic; directory access failures must not block task reads.
        }
    }

    private static void PruneSingleTaskFile(string file, DateTime now)
    {
        string taskId = Path.GetFileNameWithoutExtension(file);
        if (taskId.StartsWith(StorageRuntime.TaskFilePrefix, StringComparison.OrdinalIgnoreCase))
            taskId = taskId[StorageRuntime.TaskFilePrefix.Length..];

        HistoryEntry? entry = TaskRecordStore.Read(taskId);
        if (!entry.HasValue) return;

        HistoryEntry task = entry.Value;
        bool finished = task.Status is ConversionStatus.Success or ConversionStatus.Failed;
        bool parked = task.Status == ConversionStatus.Parked;
        TimeSpan age = now - File.GetLastWriteTimeUtc(file);

        if (IsOrphaned(task, finished, parked, age))
        {
            string reason = IsDeadPid(task, finished, parked) ? StorageTaskReasons.Abandoned : StorageTaskReasons.Canceled;
            HistoryStore.AppendCanceled(task, reason, now);
            File.Delete(file);
            return;
        }

        if (finished && age.TotalMinutes > CompletedTaskTtlMinutes)
            File.Delete(file);
    }

    private static void PruneResidualUnlockFiles()
    {
        try
        {
            string unlockDir = StorageRuntime.UnlockTempDirectory;
            if (!Directory.Exists(unlockDir)) return;
            DateTime cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (string file in Directory.GetFiles(unlockDir, "*.pdf"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // Best effort
                }
            }
        }
        catch
        {
            // Directory access failures must not block task pruning.
        }
    }

    private static bool IsDeadPid(HistoryEntry entry, bool finished, bool parked) =>
        !finished && !parked && entry.Pid > 0 && !IsProcessAlive(entry.Pid);

    private static bool IsOrphaned(HistoryEntry entry, bool finished, bool parked, TimeSpan age)
    {
        if (IsDeadPid(entry, finished, parked)) return true;
        return !finished && !parked && age.TotalHours > AbandonedTaskTtlHours;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
