using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core
{
    // 轉換作業的生命週期狀態
    public enum ConversionStatus
    {
        Pending,    // 已建立，尚未開始
        InProgress, // 正在轉換中
        Success,    // 成功完成
        Failed      // 發生錯誤
    }

    public static class ClickraStorage
    {
        private static readonly string DataDir;
        private static readonly string SettingsFile;
        private static readonly string HistoryFile;
        private static readonly object FileLock = new object();
        private static readonly Dictionary<string, string> SettingsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 記憶體中的進行中作業（非持久化，僅供 Dashboard 即時顯示）
        private static readonly object ActiveLock = new object();
        private static ActiveRecord? _activeRecord;

        static ClickraStorage()
        {
            // 標準 LocalAppData 目錄，MSIX 商店隔離與非商店版均適用
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataDir = Path.Combine(localApp, "Clickra");
            SettingsFile = Path.Combine(DataDir, "settings.conf");
            HistoryFile = Path.Combine(DataDir, "history.log");

            try
            {
                if (!Directory.Exists(DataDir))
                {
                    Directory.CreateDirectory(DataDir);
                }
            }
            catch { }

            LoadSettings();
        }

        // ─── Settings ──────────────────────────────────────────────────────────

        private static void LoadSettings()
        {
            lock (FileLock)
            {
                SettingsCache.Clear();
                // 預設值
                SettingsCache["QuietMode"] = "false";
                SettingsCache["Notification"] = "true";
                SettingsCache["OutputDir"] = "source"; // source, desktop, downloads

                if (File.Exists(SettingsFile))
                {
                    try
                    {
                        foreach (string line in File.ReadLines(SettingsFile))
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                            int idx = line.IndexOf('=');
                            if (idx > 0)
                            {
                                string key = line.Substring(0, idx).Trim();
                                string val = line.Substring(idx + 1).Trim();
                                SettingsCache[key] = val;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public static string GetSetting(string key)
        {
            lock (FileLock)
            {
                return SettingsCache.TryGetValue(key, out string? val) ? val : "";
            }
        }

        public static void SaveSetting(string key, string val)
        {
            lock (FileLock)
            {
                SettingsCache[key] = val;
                try
                {
                    using var sw = new StreamWriter(SettingsFile, false, System.Text.Encoding.UTF8);
                    foreach (var kvp in SettingsCache)
                    {
                        sw.WriteLine($"{kvp.Key}={kvp.Value}");
                    }
                }
                catch { }
            }
        }

        public static string GetOutputDir(string sourceFilePath)
        {
            string mode = GetSetting("OutputDir");
            if (mode.Equals("desktop", StringComparison.OrdinalIgnoreCase))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            if (mode.Equals("downloads", StringComparison.OrdinalIgnoreCase))
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads)) return downloads;
                return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
            return Path.GetDirectoryName(sourceFilePath) ?? "";
        }

        // ─── Active Job Tracking (In-Memory) ───────────────────────────────────

        /// <summary>
        /// 記憶體中的進行中作業，用於 Dashboard 即時顯示，不持久化。
        /// </summary>
        private class ActiveRecord
        {
            public string Command { get; set; } = "";
            public int FileCount { get; set; }
            public ConversionStatus Status { get; set; } = ConversionStatus.Pending;
            public string StartTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            public string ErrorMessage { get; set; } = "";
        }

        /// <summary>
        /// 開始追蹤一個新的作業（Pending 狀態）。應在 UI 執行緒或背景執行緒轉換開始前呼叫。
        /// </summary>
        public static void StartActiveRecord(string command, int fileCount)
        {
            lock (ActiveLock)
            {
                _activeRecord = new ActiveRecord
                {
                    Command = command,
                    FileCount = fileCount,
                    Status = ConversionStatus.Pending,
                    StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ErrorMessage = ""
                };
            }
        }

        /// <summary>
        /// 將進行中作業的狀態更新為 InProgress。應在實際開始處理前呼叫。
        /// </summary>
        public static void SetActiveRecordInProgress()
        {
            lock (ActiveLock)
            {
                if (_activeRecord != null)
                    _activeRecord.Status = ConversionStatus.InProgress;
            }
        }

        /// <summary>
        /// 完成進行中作業：寫入持久化日誌，並清除記憶體中的進行中紀錄。
        /// </summary>
        public static void CompleteActiveRecord(bool isSuccess, string errorMsg)
        {
            string? command = null;
            int fileCount = 0;
            string? startTime = null;

            lock (ActiveLock)
            {
                if (_activeRecord != null)
                {
                    command = _activeRecord.Command;
                    fileCount = _activeRecord.FileCount;
                    startTime = _activeRecord.StartTime;
                    _activeRecord.Status = isSuccess ? ConversionStatus.Success : ConversionStatus.Failed;
                    _activeRecord.ErrorMessage = errorMsg;
                }
            }

            // 寫入持久化日誌
            if (command != null)
            {
                lock (FileLock)
                {
                    try
                    {
                        string time = startTime ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string cleanErr = errorMsg.Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string line = $"{time}|{command}|{fileCount}|{(isSuccess ? "Success" : "Failed")}|{cleanErr}";
                        File.AppendAllLines(HistoryFile, new[] { line });
                    }
                    catch { }
                }
            }

            // 短暫保留最終狀態（讓 Dashboard Timer 能讀到最後一次結果），500ms 後清除
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(1500);
                lock (ActiveLock)
                {
                    // 只有在仍是這次完成的作業時才清除（避免覆蓋下一次作業）
                    if (_activeRecord?.Status == ConversionStatus.Success ||
                        _activeRecord?.Status == ConversionStatus.Failed)
                    {
                        _activeRecord = null;
                    }
                }
            });
        }

        /// <summary>
        /// 取得目前進行中作業的快照（供 Dashboard 即時顯示）。若無進行中作業則回傳 null。
        /// </summary>
        public static HistoryEntry? GetActiveEntry()
        {
            lock (ActiveLock)
            {
                if (_activeRecord == null) return null;
                return new HistoryEntry
                {
                    Time = _activeRecord.StartTime,
                    Command = _activeRecord.Command,
                    FileCount = _activeRecord.FileCount,
                    Status = _activeRecord.Status,
                    ErrorMessage = _activeRecord.ErrorMessage
                };
            }
        }

        // ─── History (Persistent) ──────────────────────────────────────────────

        public struct HistoryEntry
        {
            public string Time { get; set; }
            public string Command { get; set; }
            public int FileCount { get; set; }
            /// <summary>持久化紀錄中固定為 Success 或 Failed；進行中作業可為 Pending / InProgress。</summary>
            public ConversionStatus Status { get; set; }
            /// <summary>向下相容：True 代表 Success。</summary>
            public bool IsSuccess => Status == ConversionStatus.Success;
            public string ErrorMessage { get; set; }
        }

        public static List<HistoryEntry> GetHistory(int limit = 50)
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
                                ErrorMessage = parts.Length > 4 ? parts[4] : ""
                            });
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        public static void ClearHistory()
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
        }
    }
}
