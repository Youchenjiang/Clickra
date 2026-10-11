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
            if (!Directory.Exists(StorageRuntime.TasksDirectory)) return;
            DateTime now = DateTime.UtcNow;
            foreach (string file in Directory.GetFiles(StorageRuntime.TasksDirectory, $"{StorageRuntime.TaskFilePrefix}*.tmp"))
            {
                try { PruneSingleTaskFile(file, now); }
                catch { }
            }
        }
        catch
        {
        }
    }

    private static void PruneSingleTaskFile(string file, DateTime now)
    {
        string taskId = Path.GetFileNameWithoutExtension(file);
        if (taskId.StartsWith(StorageRuntime.TaskFilePrefix, StringComparison.OrdinalIgnoreCase))
            taskId = taskId[StorageRuntime.TaskFilePrefix.Length..];

        ClickraStorage.HistoryEntry? entry = TaskRecordStore.Read(taskId);
        if (!entry.HasValue) return;

        ClickraStorage.HistoryEntry task = entry.Value;
        bool finished = task.Status is ConversionStatus.Success or ConversionStatus.Failed;
        bool parked = task.Status == ConversionStatus.Parked;
        TimeSpan age = now - File.GetLastWriteTimeUtc(file);

        if (IsOrphaned(task, finished, parked, age))
        {
            string reason = IsDeadPid(task, finished, parked) ? ClickraStorage.AbandonedReason : ClickraStorage.CanceledReason;
            HistoryStore.AppendCanceled(task, reason, now);
            File.Delete(file);
            return;
        }

        TimeSpan expiryAge = age;
        int parkedDays = 0;
        if (parked)
            (parkedDays, expiryAge) = ParkedTaskCoordinator.GetWindow(task, file, now);

        if (TaskRetentionPolicy.IsExpired(finished, parked, expiryAge, parkedDays, CompletedTaskTtlMinutes))
            File.Delete(file);
    }

    private static bool IsDeadPid(ClickraStorage.HistoryEntry entry, bool finished, bool parked) =>
        !finished && !parked && entry.Pid > 0 && !IsProcessAlive(entry.Pid);

    private static bool IsOrphaned(ClickraStorage.HistoryEntry entry, bool finished, bool parked, TimeSpan age)
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
