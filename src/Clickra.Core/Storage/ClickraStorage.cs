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
        Parked,     // 已暫存（等待恢復或取消，不寫歷史）
        Success,    // 成功完成
        Failed      // 發生錯誤
    }

    public static partial class ClickraStorage
    {
        private static string DataDir;
        private static readonly string SettingsFile;
        private static readonly string HistoryFile;
        private static readonly object FileLock = new object();
        private static readonly Dictionary<string, string> SettingsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string GetDataDir() => DataDir;

        static ClickraStorage()
        {
            // 標準 LocalAppData 目錄，MSIX 商店隔離與非商店版均適用。
            // CLICKRA_DATA_DIR 可覆寫資料目錄（可攜式執行 / 測試隔離用）。
            string? overrideDir = Environment.GetEnvironmentVariable("CLICKRA_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                DataDir = Path.GetFullPath(overrideDir);
            }
            else
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                DataDir = Path.Combine(localApp, "Clickra");
            }
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
                if (!acquired)
                    throw new TimeoutException("Storage mutex not acquired within 5 s; aborting to prevent concurrent file corruption.");
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
                if (!acquired)
                    throw new TimeoutException("Storage mutex not acquired within 5 s; aborting to prevent concurrent file corruption.");
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
                    SettingsCache["TranslateTargetLang"] = "zh-TW";
                    SettingsCache["OfficeEngine"] = "auto"; // auto, microsoft, libreoffice
                    SettingsCache["LibreOfficePath"] = "";
                    SettingsCache["ParkedTaskRetention"] = "7"; // 已暫存任務保留天數；0 = 無限期

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

    }
}
