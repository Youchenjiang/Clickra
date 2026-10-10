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
        public const string HistoryFileName = "history.log";

        private static readonly string DataDir;
        private static readonly string SettingsFile;
        private static readonly string HistoryFile;
        private static readonly object FileLock = new object();
        private static readonly Dictionary<string, string> SettingsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastLoadedTimestampUtc = DateTime.MinValue;
        private static FileSystemWatcher? _settingsWatcher;
        private static int _reloadDebounceScheduled = 0;

        /// <summary>當設定自磁碟或外部程序重新載入時引發此事件。</summary>
        public static event Action? SettingsReloaded;

        /// <summary>當個別設定值變更時引發此事件（鍵，新值）。</summary>
        public static event Action<string, string>? SettingChanged;

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
            HistoryFile = Path.Combine(DataDir, HistoryFileName);

            try
            {
                if (!Directory.Exists(DataDir))
                {
                    Directory.CreateDirectory(DataDir);
                }
            }
            catch { }

            LoadSettings();
            InitializeSettingsWatcher();
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

        private static void InitializeSettingsWatcher()
        {
            try
            {
                if (!Directory.Exists(DataDir))
                {
                    Directory.CreateDirectory(DataDir);
                }

                _settingsWatcher = new FileSystemWatcher(DataDir, Path.GetFileName(SettingsFile))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                FileSystemEventHandler onFileChanged = (_, _) => OnSettingsFileChangedOnDisk();
                _settingsWatcher.Changed += onFileChanged;
                _settingsWatcher.Created += onFileChanged;
                _settingsWatcher.Renamed += (_, _) => OnSettingsFileChangedOnDisk();
            }
            catch
            {
                // In restricted sandbox or test environments where FileSystemWatcher is unsupported, fallback gracefully.
            }
        }

        private static void OnSettingsFileChangedOnDisk()
        {
            if (Interlocked.Exchange(ref _reloadDebounceScheduled, 1) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(25);
                    Interlocked.Exchange(ref _reloadDebounceScheduled, 0);
                    ReloadSettingsFromWatcher();
                });
            }
        }

        private static void ReloadSettingsFromWatcher()
        {
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (ReloadSettingsCore())
                    {
                        return;
                    }
                }
                catch (TimeoutException) when (attempt < maxAttempts)
                {
                    // Retry below after a short delay; watcher callbacks must not leak mutex timeouts.
                }
                catch
                {
                    return;
                }

                if (attempt < maxAttempts)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private static void LoadSettings()
        {
            lock (FileLock)
            {
                RunWithMutex(LoadSettingsInternalLocked);
            }
        }

        private static bool LoadSettingsInternalLocked()
        {
            if (!TryReadSettingsFile(out var newCache, out bool cleanedRetiredKeys, out DateTime currentWriteTime))
            {
                return false;
            }

            var changedKeys = new List<(string Key, string Value)>();
            foreach (var kvp in newCache)
            {
                if (!SettingsCache.TryGetValue(kvp.Key, out var oldVal) || oldVal != kvp.Value)
                {
                    changedKeys.Add((kvp.Key, kvp.Value));
                }
            }
            changedKeys.AddRange(SettingsCache
                .Where(kvp => !newCache.ContainsKey(kvp.Key))
                .Select(kvp => (kvp.Key, ClickraSettings.GetDefault(kvp.Key))));

            SettingsCache.Clear();
            foreach (var kvp in newCache)
            {
                SettingsCache[kvp.Key] = kvp.Value;
            }
            _lastLoadedTimestampUtc = currentWriteTime;

            if (cleanedRetiredKeys)
            {
                PersistSettingsFileLocked();
            }

            if (changedKeys.Count > 0)
            {
                NotifySettingsChanged(changedKeys);
            }

            return true;
        }

        private static bool TryReadSettingsFile(
            out Dictionary<string, string> newCache,
            out bool cleanedRetiredKeys,
            out DateTime currentWriteTime)
        {
            newCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            cleanedRetiredKeys = false;
            currentWriteTime = DateTime.MinValue;

            if (!File.Exists(SettingsFile))
            {
                return true;
            }

            try
            {
                currentWriteTime = File.GetLastWriteTimeUtc(SettingsFile);
                foreach (string line in File.ReadLines(SettingsFile))
                {
                    if (!TryParseSettingLine(line, out string key, out string val))
                    {
                        continue;
                    }

                    if (ClickraSettings.IsRetired(key))
                    {
                        cleanedRetiredKeys = true;
                        continue;
                    }

                    newCache[key] = val;
                }

                return true;
            }
            catch
            {
                // A transient read/sharing failure is not an empty settings file.
                // Preserve the last known-good cache/timestamp so the next freshness
                // check can retry without publishing a false reset to defaults.
                return false;
            }
        }

        private static bool TryParseSettingLine(string line, out string key, out string val)
        {
            key = string.Empty;
            val = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                return false;
            }

            int idx = line.IndexOf('=');
            if (idx <= 0)
            {
                return false;
            }

            key = line.Substring(0, idx).Trim();
            val = line.Substring(idx + 1).Trim();
            return true;
        }

        private static void PersistSettingsFileLocked()
        {
            using (var sw = new StreamWriter(SettingsFile, false, System.Text.Encoding.UTF8))
            {
                foreach (var kvp in SettingsCache)
                {
                    sw.WriteLine($"{kvp.Key}={kvp.Value}");
                }
            }
            try
            {
                _lastLoadedTimestampUtc = File.GetLastWriteTimeUtc(SettingsFile);
            }
            catch
            {
                // Persistence already succeeded; a timestamp refresh failure is safe to ignore.
            }
        }

        private static void EnsureFreshSettingsLocked()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    DateTime diskTime = File.GetLastWriteTimeUtc(SettingsFile);
                    if (diskTime != _lastLoadedTimestampUtc)
                    {
                        RunWithMutex(LoadSettingsInternalLocked);
                    }
                }
                else if (_lastLoadedTimestampUtc != DateTime.MinValue)
                {
                    RunWithMutex(LoadSettingsInternalLocked);
                }
            }
            catch { }
        }

        /// <summary>主動檢查設定檔在磁碟上的更新時間；若已被外部程序修改則立即同步快取。</summary>
        public static void EnsureFreshSettings()
        {
            lock (FileLock)
            {
                EnsureFreshSettingsLocked();
            }
        }

        public static void ReloadSettings()
        {
            _ = ReloadSettingsCore();
        }

        private static bool ReloadSettingsCore()
        {
            lock (FileLock)
            {
                return RunWithMutex(LoadSettingsInternalLocked);
            }
        }

        internal static string GetSettingsFilePath() => SettingsFile;

        /// <summary>讀取設定；未設定（或設定檔中沒有該行）時回傳登錄表的預設值。</summary>
        public static string GetSetting(string key)
        {
            lock (FileLock)
            {
                EnsureFreshSettingsLocked();
                return SettingsCache.TryGetValue(key, out string? val) ? val : ClickraSettings.GetDefault(key);
            }
        }

        /// <summary>讀取布林設定：登錄表的預設值決定未設定時的行為（只有 "true" 為真）。</summary>
        public static bool GetSettingBool(string key) =>
            GetSetting(key).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase);

        /// <summary>讀取整數設定；無法解析時回傳登錄表的預設值。</summary>
        public static int GetSettingInt(string key) =>
            int.TryParse(GetSetting(key), out int value) ? value : ClickraSettings.GetDefaultInt(key);

        public static void SaveSetting(string key, string val)
        {
            lock (FileLock)
            {
                bool changed = false;
                RunWithMutex(() =>
                {
                    if (!LoadSettingsInternalLocked())
                    {
                        return;
                    }

                    bool hadOldValue = SettingsCache.TryGetValue(key, out string? oldVal);
                    if (hadOldValue && oldVal == val)
                    {
                        return;
                    }

                    SettingsCache[key] = val;
                    try
                    {
                        PersistSettingsFileLocked();
                        changed = true;
                    }
                    catch
                    {
                        if (hadOldValue)
                        {
                            SettingsCache[key] = oldVal!;
                        }
                        else
                        {
                            SettingsCache.Remove(key);
                        }
                    }
                });

                if (changed)
                {
                    NotifySettingsChanged(new List<(string Key, string Value)> { (key, val) });
                }
            }
        }

        private static void NotifySettingsChanged(List<(string Key, string Value)> changes)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    foreach (var (key, value) in changes)
                    {
                        SettingChanged?.Invoke(key, value);
                    }
                    SettingsReloaded?.Invoke();
                }
                catch { }
            });
        }

        public static string GetOutputDir(string sourceFilePath)
        {
            string mode = GetSetting(ClickraSettings.OutputDir);
            if (mode.Equals(ClickraSettings.OutputDirDesktop, StringComparison.OrdinalIgnoreCase))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
            if (mode.Equals(ClickraSettings.OutputDirDownloads, StringComparison.OrdinalIgnoreCase))
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads)) return downloads;
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
            if (!mode.Equals(ClickraSettings.DefaultOutputDirSource, StringComparison.OrdinalIgnoreCase) && Directory.Exists(mode))
            {
                return mode;
            }
            return Path.GetDirectoryName(sourceFilePath) ?? "";
        }

    }
}
