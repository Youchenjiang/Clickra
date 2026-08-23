using System;
using System.IO;
using System.Collections.Generic;

namespace Clickra.Core
{
    public static partial class ClickraStorage
    {
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
