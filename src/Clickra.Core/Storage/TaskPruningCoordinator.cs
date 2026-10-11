using System;
using System.Diagnostics;
using System.IO;

namespace Clickra.Core.Storage;

public static partial class ClickraStorage
{
    /// <summary>Owns stale, completed, and orphaned task-file cleanup decisions.</summary>
    private static class TaskPruningCoordinator
    {
        // 完成後的任務檔保留一小段時間供 UI 短暫顯示結果，之後自動清除。
        private const int CompletedTaskTtlMinutes = 10;
        // 逾時仍未完成的任務視為遺棄（進程崩潰/被強制結束），自動清除。
        private const int AbandonedTaskTtlHours = 24;

        /// <summary>清除過期的任務檔：已完成超過 10 分鐘、進行中超過 24 小時（遺棄）、
        /// 或建立進程已死的進行中任務（崩潰/被強制結束/系統重啟）——後者記錄為
        /// Canceled 歷史後刪除，避免 dashboard 永遠顯示「轉換中」。
        /// 注意：進行中任務的檔案 last-write-time 由 SetTaskIndex/SetTaskInProgress
        /// 更新，因此 24h 超時實際上偵測的是「24 小時無任何進度更新」。</summary>
        internal static void Prune()
        {
            try
            {
                EnsureTasksDir();
                if (!Directory.Exists(TasksDir)) return;
                var now = DateTime.UtcNow;
                foreach (string file in Directory.GetFiles(TasksDir, $"{TaskFilePrefix}*.tmp"))
                {
                    try { PruneSingleTaskFile(file, now); }
                    catch { /* skip corrupt task file */ }
                }
            }
            catch { /* tasks dir not ready */ }
        }

        private static void PruneSingleTaskFile(string file, DateTime now)
        {
            string taskId = Path.GetFileNameWithoutExtension(file);
            if (taskId.StartsWith(TaskFilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                taskId = taskId[TaskFilePrefix.Length..];
            }
            var entry = ReadTaskFileInternal(taskId);
            if (!entry.HasValue) return;

            var e = entry.Value;
            bool finished = e.Status == ConversionStatus.Success || e.Status == ConversionStatus.Failed;
            bool parked = e.Status == ConversionStatus.Parked;
            TimeSpan age = now - File.GetLastWriteTimeUtc(file);

            if (IsOrphaned(e, finished, parked, age))
            {
                string reason = IsDeadPid(e, finished, parked) ? AbandonedReason : CanceledReason;
                WriteCanceledHistory(e, reason, now);
                File.Delete(file);
                return;
            }

            // 期限與顯示用的數字必須是同一份：使用者替某件任務延長期限後，清理不能
            // 仍依全域政策把它刪掉。
            TimeSpan expiryAge = age;
            int parkedDays = 0;
            if (parked)
            {
                (parkedDays, expiryAge) = ParkedTaskCoordinator.GetWindow(e, file, now);
            }

            if (IsExpired(finished, parked, expiryAge, parkedDays))
            {
                File.Delete(file);
            }
        }

        private static bool IsDeadPid(HistoryEntry e, bool finished, bool parked)
            => !finished && !parked && e.Pid > 0 && !IsProcessAlive(e.Pid);

        private static bool IsOrphaned(HistoryEntry e, bool finished, bool parked, TimeSpan age)
        {
            if (IsDeadPid(e, finished, parked)) return true;
            return !finished && !parked && age.TotalHours > AbandonedTaskTtlHours;
        }

        private static bool IsExpired(bool finished, bool parked, TimeSpan age, int parkedRetentionDays) =>
            TaskRetentionPolicy.IsExpired(finished, parked, age, parkedRetentionDays, CompletedTaskTtlMinutes);

        /// <summary>指定的進程 ID 是否仍在執行（用於偵測遺棄任務）。</summary>
        private static bool IsProcessAlive(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}
