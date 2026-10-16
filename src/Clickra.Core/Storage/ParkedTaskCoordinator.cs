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

            string path = TaskRecordStore.PathFor(taskId);
            (int retentionDays, TimeSpan age) = GetWindow(entry.Value, path, DateTime.UtcNow);
            if (retentionDays > 0 && TaskRetentionPolicy.CalculateInfo(retentionDays, age).HasExpired)
            {
                if (File.Exists(path)) File.Delete(path);
                return null;
            }

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

    internal static int GetRetentionDays() =>
        ClickraSettings.ClampNumericSetting(
            ClickraSettings.ParkedTaskRetention,
            SettingsStore.GetInt(ClickraSettings.ParkedTaskRetention));

    internal static int GetEffectiveRetentionDays(HistoryEntry entry) =>
        entry.ParkedRetentionDays.HasValue
            ? ClickraSettings.ClampNumericSetting(ClickraSettings.ParkedTaskRetention, entry.ParkedRetentionDays.Value)
            : GetRetentionDays();

    internal static void SetRetentionOverride(string taskId, int? days)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (!entry.HasValue || entry.Value.Status != ConversionStatus.Parked) return;

            string path = TaskRecordStore.PathFor(taskId);
            DateTime now = DateTime.UtcNow;
            TimeSpan age = GetAge(entry.Value, path, now);
            string since = string.IsNullOrEmpty(entry.Value.ParkedSince)
                ? (now - age).ToString(StorageRuntime.DateTimeFormat, CultureInfo.InvariantCulture)
                : entry.Value.ParkedSince;
            int? clamped = days.HasValue
                ? ClickraSettings.ClampNumericSetting(ClickraSettings.ParkedTaskRetention, days.Value)
                : null;
            TaskFileData data = TaskRecordStore.FromEntry(entry.Value, ConversionStatus.Parked) with
            {
                ParkedRetentionDays = clamped,
                ParkedSince = since
            };
            TaskRecordStore.Write(data);
        });
    }

    internal static int? AdjustRetention(string taskId, int deltaDays)
    {
        if (deltaDays == 0) return null;

        return StorageRuntime.RunWithMutex(() =>
        {
            string path = TaskRecordStore.PathFor(taskId);
            HistoryEntry? maybe = TaskRecordStore.Read(taskId);
            if (!maybe.HasValue || maybe.Value.Status != ConversionStatus.Parked) return (int?)null;

            HistoryEntry entry = maybe.Value;
            DateTime now = DateTime.UtcNow;
            TimeSpan age = GetAge(entry, path, now);
            int current = GetEffectiveRetentionDays(entry);
            int? adjusted = TaskRetentionPolicy.CalculateAdjustedDays(current, age, deltaDays);
            if (!adjusted.HasValue) return null;

            string since = string.IsNullOrEmpty(entry.ParkedSince)
                ? (now - age).ToString(StorageRuntime.DateTimeFormat, CultureInfo.InvariantCulture)
                : entry.ParkedSince;
            TaskFileData data = TaskRecordStore.FromEntry(entry, ConversionStatus.Parked) with
            {
                ParkedRetentionDays = adjusted.Value,
                ParkedSince = since
            };
            return TaskRecordStore.Write(data) ? adjusted.Value : null;
        });
    }

    internal static (int Days, TimeSpan Age) GetWindow(HistoryEntry entry, string path, DateTime now) =>
        (GetEffectiveRetentionDays(entry), GetAge(entry, path, now));

    internal static ParkedRetentionInfo GetRetentionInfo(string taskId) =>
        StorageRuntime.RunWithMutex(() =>
        {
            string path = TaskRecordStore.PathFor(taskId);
            HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (!entry.HasValue)
            {
                return new ParkedRetentionInfo(
                    IsUnlimited: false,
                    RemainingDays: 0,
                    RemainingTime: TimeSpan.Zero,
                    IsExpiringSoon: false,
                    HasExpired: true);
            }

            (int days, TimeSpan age) = GetWindow(entry.Value, path, DateTime.UtcNow);
            return TaskRetentionPolicy.CalculateInfo(days, age, entry.Value.ParkedRetentionDays.HasValue);
        });

    private static TimeSpan GetAge(HistoryEntry entry, string path, DateTime now)
    {
        if (DateTime.TryParse(
                entry.ParkedSince,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime since))
        {
            return now - since;
        }

        return now - File.GetLastWriteTimeUtc(path);
    }
}
