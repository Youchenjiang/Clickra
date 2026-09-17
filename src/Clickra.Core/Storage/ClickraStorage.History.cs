using System;
using System.IO;
using System.Collections.Generic;

namespace Clickra.Core
{
    public static partial class ClickraStorage
    {
        /// <summary>使用者取消（或任務清理判定為取消）時寫入歷史的原因標記。UI 用它把
        /// 取消顯示成「已取消」而非失敗，因此必須與寫入端共用同一個字串。</summary>
        public const string CanceledReason = "Canceled";

        /// <summary>建立任務的進程已死時，清理任務時寫入歷史的原因標記。</summary>
        public const string AbandonedReason = "Abandoned";

        /// <summary>CLI 進度視窗沿用至今的取消標記（見 ProgressWindow.Process.cs）。
        /// 尚未統一成 <see cref="CanceledReason"/>，但歷史顯示必須把兩者一視同仁。</summary>
        public const string LegacyUserAbortedReason = "User Aborted";

        /// <summary>歷史紀錄的錯誤訊息是否代表「使用者取消」而非失敗；跨 UI 共用，
        /// 讓歷史把取消顯示成已取消而不是錯誤。</summary>
        public static bool IsUserCanceledReason(string? errorMessage)
            => errorMessage is not null &&
               (errorMessage.Equals(CanceledReason, StringComparison.Ordinal) ||
                errorMessage.Equals(LegacyUserAbortedReason, StringComparison.OrdinalIgnoreCase));

        // ─── History (Persistent) ──────────────────────────────────────────────

        public struct HistoryEntry
        {
            /// <summary>任務佇列檔的唯一識別碼（history.log 紀錄中為空字串）。</summary>
            public string Id { get; set; }
            public string Time { get; set; }
            public string Command { get; set; }
            public int FileCount { get; set; }
            /// <summary>持久化紀錄中固定為 Success 或 Failed；進行中作業可為 Pending / InProgress。</summary>
            public ConversionStatus Status { get; set; }
            /// <summary>向下相容：True 代表 Success。</summary>
            public bool IsSuccess => Status == ConversionStatus.Success;
            public string ErrorMessage { get; set; }
            public string EndTime { get; set; }
            public long ElapsedMs { get; set; }
            public string InputPaths { get; set; }
            public string OutputPath { get; set; }
            public int CurrentIndex { get; set; }
            /// <summary>建立任務的進程 ID（history.log 紀錄中為 0）；用於跨進程定位任務。</summary>
            public int Pid { get; set; }

            /// <summary>這個暫存任務自己的保留天數（0 = 無限期）；null 代表沒有覆寫、沿用全域政策。
            /// 使用者可以只替某一件任務延長或縮短期限，而不必改動整個政策。</summary>
            public int? ParkedRetentionDays { get; set; }

            /// <summary>任務被暫存的時間（UTC，yyyy-MM-dd HH:mm:ss）；空字串代表尚未記錄，
            /// 此時的年資退回檔案 mtime。第一次調整期限時寫入 —— 寫入本身會把 mtime 更新成現在，
            /// 若不先記下來，調整一次就會把已過的時間清掉。</summary>
            public string ParkedSince { get; set; }
        }

        public static List<HistoryEntry> GetHistory(int limit = 50)
        {
            return RunWithMutex(() =>
            {
                var list = new List<HistoryEntry>();
                lock (FileLock)
                {
                    if (!File.Exists(HistoryFile)) return list;
                    try
                    {
                        var lines = File.ReadAllLines(HistoryFile);
                        int start = Math.Max(0, lines.Length - limit);
                        for (int i = lines.Length - 1; i >= start; i--)
                        {
                            string line = lines[i];
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            string[] parts = line.Split('|');
                            if (parts.Length >= 4)
                            {
                                int.TryParse(parts[2], out int count);
                                bool success = parts[3].Equals("Success", StringComparison.OrdinalIgnoreCase);
                                list.Add(new HistoryEntry
                                {
                                    Time = parts[0],
                                    Command = parts[1],
                                    FileCount = count,
                                    Status = success ? ConversionStatus.Success : ConversionStatus.Failed,
                                    ErrorMessage = parts.Length > 4 ? parts[4] : "",
                                    EndTime = parts.Length > 5 ? parts[5] : parts[0],
                                    ElapsedMs = parts.Length > 6 && long.TryParse(parts[6], out long ms) ? ms : -1,
                                    InputPaths = parts.Length > 7 ? parts[7] : "",
                                    OutputPath = parts.Length > 8 ? parts[8] : ""
                                });
                            }
                        }
                    }
                    catch { }
                }
                return list;
            });
        }

        public static void ClearHistory()
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        if (File.Exists(HistoryFile))
                        {
                            File.Delete(HistoryFile);
                        }
                    }
                    catch { }
                }
            });
        }
    }
}
