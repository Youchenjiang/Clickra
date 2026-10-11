using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Clickra.Core.Storage;

/// <summary>Owns settings.conf caching, persistence, freshness checks, and cross-process change notifications.</summary>
internal static class SettingsStore
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastLoadedTimestampUtc = DateTime.MinValue;
    private static FileSystemWatcher? _watcher;
    private static int _reloadDebounceScheduled;

    static SettingsStore()
    {
        Load();
        InitializeWatcher();
    }

    internal static event Action? Reloaded;
    internal static event Action<string, string>? Changed;

    internal static string SettingsFilePath => StorageRuntime.SettingsFilePath;

    internal static void EnsureFresh()
    {
        lock (StorageRuntime.FileLock)
            EnsureFreshLocked();
    }

    internal static void Reload() => _ = ReloadCore();

    internal static string Get(string key)
    {
        lock (StorageRuntime.FileLock)
        {
            EnsureFreshLocked();
            return Cache.TryGetValue(key, out string? value) ? value : ClickraSettings.GetDefault(key);
        }
    }

    internal static bool GetBool(string key) =>
        Get(key).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase);

    internal static int GetInt(string key) =>
        int.TryParse(Get(key), out int value) ? value : ClickraSettings.GetDefaultInt(key);

    internal static void Set(string key, string value)
    {
        lock (StorageRuntime.FileLock)
        {
            bool changed = false;
            StorageRuntime.RunWithMutex(() =>
            {
                if (!LoadInternalLocked()) return;

                bool hadOldValue = Cache.TryGetValue(key, out string? oldValue);
                if (hadOldValue && oldValue == value) return;

                Cache[key] = value;
                try
                {
                    PersistLocked();
                    changed = true;
                }
                catch
                {
                    if (hadOldValue) Cache[key] = oldValue!;
                    else Cache.Remove(key);
                }
            });

            if (changed)
                Notify(new List<(string Key, string Value)> { (key, value) });
        }
    }

    private static void InitializeWatcher()
    {
        try
        {
            Directory.CreateDirectory(StorageRuntime.DataDirectory);
            _watcher = new FileSystemWatcher(StorageRuntime.DataDirectory, Path.GetFileName(SettingsFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler onChanged = (_, _) => OnFileChanged();
            _watcher.Changed += onChanged;
            _watcher.Created += onChanged;
            _watcher.Renamed += (_, _) => OnFileChanged();
        }
        catch
        {
            // Restricted environments may not support FileSystemWatcher; freshness checks remain available.
        }
    }

    private static void OnFileChanged()
    {
        if (Interlocked.Exchange(ref _reloadDebounceScheduled, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(25);
            Interlocked.Exchange(ref _reloadDebounceScheduled, 0);
            ReloadFromWatcher();
        });
    }

    private static void ReloadFromWatcher()
    {
        const int maxAttempts = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (ReloadCore()) return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
            catch
            {
                return;
            }

            if (attempt < maxAttempts) Thread.Sleep(50);
        }
    }

    private static void Load()
    {
        lock (StorageRuntime.FileLock)
            StorageRuntime.RunWithMutex(LoadInternalLocked);
    }

    private static bool ReloadCore()
    {
        lock (StorageRuntime.FileLock)
            return StorageRuntime.RunWithMutex(LoadInternalLocked);
    }

    private static bool LoadInternalLocked()
    {
        if (!TryReadFile(out Dictionary<string, string> newCache, out bool cleanedRetiredKeys, out DateTime writeTime))
            return false;

        var changedKeys = new List<(string Key, string Value)>();
        foreach ((string key, string value) in newCache)
        {
            if (!Cache.TryGetValue(key, out string? oldValue) || oldValue != value)
                changedKeys.Add((key, value));
        }
        changedKeys.AddRange(Cache
            .Where(pair => !newCache.ContainsKey(pair.Key))
            .Select(pair => (pair.Key, ClickraSettings.GetDefault(pair.Key))));

        Cache.Clear();
        foreach ((string key, string value) in newCache) Cache[key] = value;
        _lastLoadedTimestampUtc = writeTime;

        if (cleanedRetiredKeys) PersistLocked();
        if (changedKeys.Count > 0) Notify(changedKeys);
        return true;
    }

    private static bool TryReadFile(
        out Dictionary<string, string> newCache,
        out bool cleanedRetiredKeys,
        out DateTime writeTime)
    {
        newCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        cleanedRetiredKeys = false;
        writeTime = DateTime.MinValue;
        if (!File.Exists(SettingsFilePath)) return true;

        try
        {
            writeTime = File.GetLastWriteTimeUtc(SettingsFilePath);
            foreach (string line in File.ReadLines(SettingsFilePath))
            {
                if (!TryParseLine(line, out string key, out string value)) continue;
                if (ClickraSettings.IsRetired(key))
                {
                    cleanedRetiredKeys = true;
                    continue;
                }
                newCache[key] = value;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLine(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) return false;
        int index = line.IndexOf('=');
        if (index <= 0) return false;
        key = line[..index].Trim();
        value = line[(index + 1)..].Trim();
        return true;
    }

    private static void PersistLocked()
    {
        using var writer = new StreamWriter(SettingsFilePath, false, System.Text.Encoding.UTF8);
        foreach ((string key, string value) in Cache) writer.WriteLine($"{key}={value}");
        try { _lastLoadedTimestampUtc = File.GetLastWriteTimeUtc(SettingsFilePath); }
        catch { }
    }

    private static void EnsureFreshLocked()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                if (File.GetLastWriteTimeUtc(SettingsFilePath) != _lastLoadedTimestampUtc)
                    StorageRuntime.RunWithMutex(LoadInternalLocked);
            }
            else if (_lastLoadedTimestampUtc != DateTime.MinValue)
            {
                StorageRuntime.RunWithMutex(LoadInternalLocked);
            }
        }
        catch
        {
        }
    }

    private static void Notify(List<(string Key, string Value)> changes)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                foreach ((string key, string value) in changes) Changed?.Invoke(key, value);
                Reloaded?.Invoke();
            }
            catch
            {
            }
        });
    }
}
