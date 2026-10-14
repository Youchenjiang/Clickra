using System;

namespace Clickra.Core.Storage;

public enum ConversionStatus
{
    Pending,
    InProgress,
    Parked,
    Success,
    Failed
}

/// <summary>
/// Compatibility facade for Clickra's historical public storage surface.
/// Persistence ownership lives in focused storage components; new product code should prefer
/// application owners instead of depending on this facade directly.
/// </summary>
public static partial class ClickraStorage
{
    public const string HistoryFileName = "history.log";

    public static event Action? SettingsReloaded
    {
        add => SettingsStore.Reloaded += value;
        remove => SettingsStore.Reloaded -= value;
    }

    public static event Action<string, string>? SettingChanged
    {
        add => SettingsStore.Changed += value;
        remove => SettingsStore.Changed -= value;
    }

    public static string GetDataDir() => StorageRuntime.DataDirectory;

    public static void EnsureFreshSettings() => SettingsStore.EnsureFresh();

    public static void ReloadSettings() => SettingsStore.Reload();

    internal static string GetSettingsFilePath() => SettingsStore.SettingsFilePath;

    public static string GetSetting(string key) => SettingsStore.Get(key);

    public static bool GetSettingBool(string key) => SettingsStore.GetBool(key);

    public static int GetSettingInt(string key) => SettingsStore.GetInt(key);

    public static void SaveSetting(string key, string val) => SettingsStore.Set(key, val);

    public static string GetOutputDir(string sourceFilePath) => StoragePathResolver.ResolveOutputDirectory(sourceFilePath);
}
