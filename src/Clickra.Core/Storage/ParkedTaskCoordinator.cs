using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Clickra.Core.Storage;

public static partial class ClickraStorage
{
    /// <summary>Owns parked-task transitions, resume claims, and per-task retention orchestration.</summary>
    private static class ParkedTaskCoordinator
    {
        internal static void Park(string taskId, string reason, int nextIndex)
        {
            RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (entry.HasValue && entry.Value.Status == ConversionStatus.InProgress)
                {
                    var data = ToTaskData(entry.Value, ConversionStatus.Parked, errorMsg: reason) with
                    {
                        CurrentIndex = nextIndex,
                        ParkedSince = DateTime.UtcNow.ToString(DateTimeFormat, CultureInfo.InvariantCulture)
                    };
                    WriteTaskFileInternal(data);
                }
            });
            if (_threadTaskId == taskId) _threadTaskId = null;
        }

        internal static List<HistoryEntry> GetAll()
        {
            return RunWithMutex(() =>
            {
                TaskPruningCoordinator.Prune();
                return ListTaskFiles()
                    .Select(ReadTaskFileInternal)
                    .Where(e => e.HasValue && e.Value.Status == ConversionStatus.Parked)
                    .Select(e => e.GetValueOrDefault())
                    .ToList();
            });
        }

        internal static void Cancel(string taskId)
        {
            RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (entry is null || entry.Value.Status != ConversionStatus.Parked) return;

                string path = TaskFilePath(taskId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                WriteCanceledHistory(entry.Value, CanceledReason, DateTime.UtcNow);
            });
        }

        internal static ResumedTaskInfo? ClaimForResume(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return null;
            return RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (entry is null || entry.Value.Status != ConversionStatus.Parked) return null;

                string path = TaskFilePath(taskId);
                var (retentionDays, age) = GetWindow(entry.Value, path, DateTime.UtcNow);
                if (retentionDays > 0 && CalculateInfo(retentionDays, age).HasExpired)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return null;
                }

                var files = (entry.Value.InputPaths ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                if (files.Count == 0) return null;

                int startIndex = Math.Clamp(entry.Value.CurrentIndex, 0, files.Count);
                var claimed = ToTaskData(entry.Value, ConversionStatus.InProgress, pidOverride: Environment.ProcessId) with
                {
                    ParkedSince = null
                };
                if (!WriteTaskFileInternal(claimed)) return null;
                return new ResumedTaskInfo(taskId, entry.Value.Command, files, startIndex);
            });
        }

        internal static int GetRetentionDays() =>
            ClickraSettings.ClampNumericSetting(
                ClickraSettings.ParkedTaskRetention,
                GetSettingInt(ClickraSettings.ParkedTaskRetention));

        internal static int GetEffectiveRetentionDays(HistoryEntry entry) =>
            entry.ParkedRetentionDays.HasValue
                ? ClickraSettings.ClampNumericSetting(ClickraSettings.ParkedTaskRetention, entry.ParkedRetentionDays.Value)
                : GetRetentionDays();

        internal static void SetRetentionOverride(string taskId, int? days)
        {
            RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (!entry.HasValue || entry.Value.Status != ConversionStatus.Parked) return;

                string path = TaskFilePath(taskId);
                var now = DateTime.UtcNow;
                var age = GetAge(entry.Value, path, now);
                string since = string.IsNullOrEmpty(entry.Value.ParkedSince)
                    ? (now - age).ToString(DateTimeFormat, CultureInfo.InvariantCulture)
                    : entry.Value.ParkedSince;
                int? clamped = days.HasValue
                    ? ClickraSettings.ClampNumericSetting(ClickraSettings.ParkedTaskRetention, days.Value)
                    : null;
                var data = ToTaskData(entry.Value, ConversionStatus.Parked) with
                {
                    ParkedRetentionDays = clamped,
                    ParkedSince = since
                };
                WriteTaskFileInternal(data);
            });
        }

        internal static int? AdjustRetention(string taskId, int deltaDays)
        {
            if (deltaDays == 0) return null;

            return RunWithMutex(() =>
            {
                string path = TaskFilePath(taskId);
                var maybe = ReadTaskFileInternal(taskId);
                if (!maybe.HasValue || maybe.Value.Status != ConversionStatus.Parked) return (int?)null;

                var entry = maybe.Value;
                var now = DateTime.UtcNow;
                var age = GetAge(entry, path, now);
                int current = GetEffectiveRetentionDays(entry);

                int? adjusted = TaskRetentionPolicy.CalculateAdjustedDays(current, age, deltaDays);
                if (!adjusted.HasValue) return (int?)null;
                int next = adjusted.Value;
                string since = string.IsNullOrEmpty(entry.ParkedSince)
                    ? (now - age).ToString(DateTimeFormat, CultureInfo.InvariantCulture)
                    : entry.ParkedSince;

                var data = ToTaskData(entry, ConversionStatus.Parked) with
                {
                    ParkedRetentionDays = next,
                    ParkedSince = since
                };
                if (!WriteTaskFileInternal(data)) return (int?)null;
                return next;
            });
        }

        internal static (int Days, TimeSpan Age) GetWindow(HistoryEntry entry, string path, DateTime now) =>
            (GetEffectiveRetentionDays(entry), GetAge(entry, path, now));

        internal static string DescribeRetention(ParkedRetentionInfo info)
        {
            string phrase;
            if (info.IsUnlimited)
                phrase = Localization.T("task_parked_ttl_unlimited");
            else if (info.HasExpired)
                phrase = Localization.T("task_parked_ttl_expired");
            else if (info.IsExpiringSoon)
                phrase = Localization.T("task_parked_ttl_expiring_soon");
            else if (info.RemainingDays == 1)
                phrase = Localization.T("task_parked_ttl_days_one", 1);
            else
                phrase = Localization.T("task_parked_ttl_days", info.RemainingDays);

            return info.IsTaskOverride ? phrase + Localization.T("task_parked_ttl_override") : phrase;
        }

        internal static ParkedRetentionInfo CalculateInfo(int retentionDays, TimeSpan age, bool isTaskOverride = false) =>
            TaskRetentionPolicy.CalculateInfo(retentionDays, age, isTaskOverride);

        internal static ParkedRetentionInfo GetRetentionInfo(string taskId)
        {
            return RunWithMutex(() =>
            {
                string path = TaskFilePath(taskId);
                var entry = ReadTaskFileInternal(taskId);
                if (!entry.HasValue)
                {
                    return new ParkedRetentionInfo(
                        IsUnlimited: false,
                        RemainingDays: 0,
                        RemainingTime: TimeSpan.Zero,
                        IsExpiringSoon: false,
                        HasExpired: true);
                }

                var (days, age) = GetWindow(entry.Value, path, DateTime.UtcNow);
                return CalculateInfo(days, age, entry.Value.ParkedRetentionDays.HasValue);
            });
        }

        private static TimeSpan GetAge(HistoryEntry entry, string path, DateTime now)
        {
            if (DateTime.TryParse(entry.ParkedSince, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var since))
            {
                return now - since;
            }

            return now - File.GetLastWriteTimeUtc(path);
        }
    }
}
