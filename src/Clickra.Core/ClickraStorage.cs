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

        public static string GetDataDir() => DataDir;

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

        private static void RunWithMutex(Action action)
        {
            using var mutex = new Mutex(false, "Local\\ClickraStorageMutex_v1");
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(5000, false);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                action();
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }

        private static T RunWithMutex<T>(Func<T> func)
        {
            using var mutex = new Mutex(false, "Local\\ClickraStorageMutex_v1");
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(5000, false);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                return func();
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }

        private static void LoadSettings()
        {
            lock (FileLock)
            {
                RunWithMutex(() =>
                {
                    SettingsCache.Clear();
                    // 預設值
                    SettingsCache["QuietMode"] = "false";
                    SettingsCache["Notification"] = "true";
                    SettingsCache["OutputDir"] = "source"; // source, desktop, downloads
                    SettingsCache["Language"] = "";

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
                });
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
                RunWithMutex(() =>
                {
                    try
                    {
                        using var sw = new StreamWriter(SettingsFile, false, System.Text.Encoding.UTF8);
                        foreach (var kvp in SettingsCache)
                        {
                            sw.WriteLine($"{kvp.Key}={kvp.Value}");
                        }
                    }
                    catch { }
                });
            }
        }

        public static string GetOutputDir(string sourceFilePath)
        {
            string mode = GetSetting("OutputDir");
            if (mode.Equals("desktop", StringComparison.OrdinalIgnoreCase))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
            if (mode.Equals("downloads", StringComparison.OrdinalIgnoreCase))
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads)) return downloads;
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
            if (!mode.Equals("source", StringComparison.OrdinalIgnoreCase) && Directory.Exists(mode))
            {
                return mode;
            }
            return Path.GetDirectoryName(sourceFilePath) ?? "";
        }

        // ─── Active Job Tracking (File-based IPC) ─────────────────────────────
        // ProgressWindow 與 DashboardWindow 是不同進程，必須透過檔案溝通即時狀態。
        // 格式：每行 Key=Value，欄位有 Time / Command / FileCount / Status / ErrorMessage

        private static string ActiveFile => Path.Combine(DataDir, "active.tmp");

        /// <summary>
        /// 開始追蹤一個新的作業（Pending 狀態），寫入 active.tmp。
        /// </summary>
        public static void StartActiveRecord(string command, int fileCount, string? inputPaths = null)
        {
            RunWithMutex(() =>
            {
                WriteActiveFileInternal(command, fileCount, ConversionStatus.Pending, "", null, inputPaths);
            });
        }

        /// <summary>
        /// 將進行中作業的狀態更新為 InProgress。
        /// </summary>
        public static void SetActiveRecordInProgress()
        {
            RunWithMutex(() =>
            {
                var entry = ReadActiveFileInternal();
                if (entry.HasValue)
                {
                    WriteActiveFileInternal(entry.Value.Command, entry.Value.FileCount, ConversionStatus.InProgress, "", entry.Value.Time, entry.Value.InputPaths);
                }
            });
        }

        /// <summary>
        /// 更新進行中作業的當前處理檔案索引。
        /// </summary>
        public static void SetActiveRecordIndex(int index)
        {
            RunWithMutex(() =>
            {
                var entry = ReadActiveFileInternal();
                if (entry.HasValue)
                {
                    WriteActiveFileInternal(entry.Value.Command, entry.Value.FileCount, entry.Value.Status, entry.Value.ErrorMessage, entry.Value.Time, entry.Value.InputPaths, index);
                }
            });
        }

        public static void CompleteActiveRecord(string command, string startTime, bool isSuccess, string errorMsg, string? endTime = null, long elapsedMs = -1, string? inputPaths = null, string? outputPath = null)
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        string cleanErr = (errorMsg ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string et = endTime ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string inputs = (inputPaths ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string output = (outputPath ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");

                        var inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        var outputList = output.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                        if (inputList.Length > 0 && outputList.Length == inputList.Length)
                        {
                            long elapsedPerFile = elapsedMs >= 0 ? elapsedMs / inputList.Length : -1;
                            var lines = new List<string>();
                            for (int i = 0; i < inputList.Length; i++)
                            {
                                string singleInput = inputList[i].Trim();
                                string singleOutput = outputList[i].Trim();
                                
                                bool isSingleSuccess = isSuccess;
                                if (!isSuccess)
                                {
                                    try { isSingleSuccess = File.Exists(singleOutput); } catch { isSingleSuccess = false; }
                                }
                                
                                string singleErr = isSingleSuccess ? "" : cleanErr;
                                string line = $"{startTime}|{command}|1|{(isSingleSuccess ? "Success" : "Failed")}|{singleErr}|{et}|{elapsedPerFile}|{singleInput}|{singleOutput}";
                                lines.Add(line);
                            }
                            File.AppendAllLines(HistoryFile, lines);
                        }
                        else
                        {
                            string line = $"{startTime}|{command}|1|{(isSuccess ? "Success" : "Failed")}|{cleanErr}|{et}|{elapsedMs}|{inputs}|{output}";
                            File.AppendAllLines(HistoryFile, new[] { line });
                        }
                    }
                    catch { }
                }

                try
                {
                    var entry = ReadActiveFileInternal();
                    int fileCount = entry?.FileCount ?? 0;
                    if (fileCount == 0 && !string.IsNullOrEmpty(inputPaths))
                    {
                        fileCount = inputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    }
                    WriteActiveFileInternal(command, fileCount, isSuccess ? ConversionStatus.Success : ConversionStatus.Failed, errorMsg, startTime, inputPaths);
                }
                catch { }
            });
        }

        /// <summary>
        /// 徹底清除進行中作業的暫存檔。
        /// </summary>
        public static void ClearActiveRecord()
        {
            RunWithMutex(() =>
            {
                try
                {
                    if (File.Exists(ActiveFile))
                    {
                        File.Delete(ActiveFile);
                    }
                }
                catch { }
            });
        }

        /// <summary>
        /// 取得目前進行中作業的快照（供 Dashboard 即時顯示）。若無進行中作業則回傳 null。
        /// </summary>
        public static HistoryEntry? GetActiveEntry()
        {
            return RunWithMutex(() => ReadActiveFileInternal());
        }

        private static void WriteActiveFileInternal(string command, int fileCount, ConversionStatus status,
            string errorMsg, string? time = null, string? inputPaths = null, int currentIndex = 0)
        {
            try
            {
                string t = time ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string cleanErr = (errorMsg ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                string cleanInputs = (inputPaths ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                string content =
                    $"Time={t}\n" +
                    $"Command={command}\n" +
                    $"FileCount={fileCount}\n" +
                    $"Status={status}\n" +
                    $"ErrorMessage={cleanErr}\n" +
                    $"InputPaths={cleanInputs}\n" +
                    $"CurrentIndex={currentIndex}\n";
                File.WriteAllText(ActiveFile + ".tmp", content, System.Text.Encoding.UTF8);
                File.Move(ActiveFile + ".tmp", ActiveFile, overwrite: true);
            }
            catch { }
        }

        private static HistoryEntry? ReadActiveFileInternal()
        {
            try
            {
                if (!File.Exists(ActiveFile)) return null;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadLines(ActiveFile))
                {
                    int idx = line.IndexOf('=');
                    if (idx > 0)
                        dict[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }
                if (!dict.ContainsKey("Command")) return null;
                int.TryParse(dict.GetValueOrDefault("FileCount", "0"), out int fc);
                int.TryParse(dict.GetValueOrDefault("CurrentIndex", "0"), out int ci);
                Enum.TryParse(dict.GetValueOrDefault("Status", "Pending"), out ConversionStatus status);
                return new HistoryEntry
                {
                    Time = dict.GetValueOrDefault("Time", ""),
                    Command = dict.GetValueOrDefault("Command", ""),
                    FileCount = fc,
                    Status = status,
                    ErrorMessage = dict.GetValueOrDefault("ErrorMessage", ""),
                    InputPaths = dict.GetValueOrDefault("InputPaths", ""),
                    CurrentIndex = ci
                };
            }
            catch { return null; }
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
            public string EndTime { get; set; }
            public long ElapsedMs { get; set; }
            public string InputPaths { get; set; }
            public string OutputPath { get; set; }
            public int CurrentIndex { get; set; }
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
