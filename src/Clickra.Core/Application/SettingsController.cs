namespace Clickra.Core.Application;

/// <summary>
/// Application-facing access to persisted user settings. UI surfaces depend on this
/// boundary instead of reaching into the storage facade directly.
/// </summary>
public static class SettingsController
{
    /// <summary>Raised when persisted settings are reloaded or changed.</summary>
    public static event Action? Reloaded
    {
        add => ClickraStorage.SettingsReloaded += value;
        remove => ClickraStorage.SettingsReloaded -= value;
    }

    /// <summary>Reads a registered setting as text, including its registered default.</summary>
    public static string Get(string key) => ClickraStorage.GetSetting(key);

    /// <summary>Reads a registered setting as a boolean.</summary>
    public static bool GetBool(string key) => ClickraStorage.GetSettingBool(key);

    /// <summary>Reads a registered setting as an integer.</summary>
    public static int GetInt(string key) => ClickraStorage.GetSettingInt(key);

    /// <summary>Persists a registered setting value.</summary>
    public static void Set(string key, string value) => ClickraStorage.SaveSetting(key, value);

    /// <summary>Synchronizes the settings cache with any newer on-disk state.</summary>
    public static void EnsureFresh() => ClickraStorage.EnsureFreshSettings();
}
