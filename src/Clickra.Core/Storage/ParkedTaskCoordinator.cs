using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Clickra.Core.Storage;

/// <summary>Owns parked-task transitions, resume claims, and per-task retention orchestration.</summary>
internal static class ParkedTaskCoordinator
{
    internal static void Park(string taskId, string reason, int nextIndex)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (entry.HasValue && entry.Value.Status == ConversionStatus.InProgress)
            {
                TaskFileData data = TaskRecordStore.FromEntry(entry.Value, ConversionStatus.Parked, errorMessage: reason) with
                {
                    CurrentIndex = nextIndex,
                    ParkedSince = DateTime.UtcNow.ToString(StorageRuntime.DateTimeFormat, CultureInfo.InvariantCulture)
                };
                TaskRecordStore.Write(data);
            }
        });
        TaskQueueStore.ClearThreadTask(taskId);
    }

    internal static List<HistoryEntry> GetAll() =>
        StorageRuntime.RunWithMutex(() =>
        {
            TaskPruningCoordinator.Prune();
            return TaskRecordStore.ListTaskIds()
                .Select(TaskRecordStore.Read)
                .Where(entry => entry.HasValue && entry.Value.Status == ConversionStatus.Parked)
                .Select(entry => entry.GetValueOrDefault())
                .ToList();
        });

    internal static void Cancel(string taskId)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (entry is null || entry.Value.Status != ConversionStatus.Parked) return;

            string path = TaskRecordStore.PathFor(taskId);
            if (File.Exists(path)) File.Delete(path);
            HistoryStore.AppendCanceled(entry.Value, StorageTaskReasons.Canceled, DateTime.UtcNow);
        });
    }

    internal static ResumedTaskInfo? ClaimForResume(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return null;
        return StorageRuntime.RunWithMutex(() =>
        {
            HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (entry is null || entry.Value.Status != ConversionStatus.Parked) return null;

            var files = (entry.Value.InputPaths ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            if (files.Count == 0) return null;

            int startIndex = Math.Clamp(entry.Value.CurrentIndex, 0, files.Count);
            TaskFileData claimed = TaskRecordStore.FromEntry(
                entry.Value,
                ConversionStatus.InProgress,
                pidOverride: Environment.ProcessId) with { ParkedSince = null };
            if (!TaskRecordStore.Write(claimed)) return null;
            return new ResumedTaskInfo(taskId, entry.Value.Command, files, startIndex);
        });
    }
}
