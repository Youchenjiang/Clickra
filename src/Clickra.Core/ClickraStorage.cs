using System;
using System.IO;
using System.Collections.Generic;

namespace Clickra.Core
{
    public static class ClickraStorage
    {
        private static readonly string DataDir;
        private static readonly string SettingsFile;
        private static readonly string HistoryFile;
        private static readonly object FileLock = new object();
        private static readonly Dictionary<string, string> SettingsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        public static void RecordHistory(string command, int fileCount, bool isSuccess, string errorMsg)
        {
            lock (FileLock)
            {
                try
                {
                    string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string cleanErrMsg = errorMsg.Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                    string line = $"{time}|{command}|{fileCount}|{(isSuccess ? "Success" : "Failed")}|{cleanErrMsg}";
                    
                    // 附加紀錄
                    File.AppendAllLines(HistoryFile, new[] { line });
                }
                catch { }
            }
        }

        public struct HistoryEntry
        {
            public string Time { get; set; }
            public string Command { get; set; }
            public int FileCount { get; set; }
            public bool IsSuccess { get; set; }
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
                            list.Add(new HistoryEntry
                            {
                                Time = parts[0],
                                Command = parts[1],
                                FileCount = count,
                                IsSuccess = parts[3].Equals("Success", StringComparison.OrdinalIgnoreCase),
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
