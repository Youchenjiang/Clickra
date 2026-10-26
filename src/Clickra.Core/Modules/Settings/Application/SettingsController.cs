namespace Clickra.Core.Modules.Settings.Application;

public enum OfficeEnginePreference
{
    Auto,
    Microsoft,
    LibreOffice
}

public enum OutputDirectoryKind
{
    Source,
    Desktop,
    Downloads,
    Custom
}

public readonly record struct OutputDirectoryPreference(OutputDirectoryKind Kind, string CustomPath = "");

/// <summary>
/// Typed application-facing user settings. Persisted string keys and boolean encodings stay behind
/// this boundary so presentation code consumes product values rather than storage representation.
/// </summary>
public static class SettingsController
{
    public static event Action? Reloaded
    {
        add => ClickraStorage.SettingsReloaded += value;
        remove => ClickraStorage.SettingsReloaded -= value;
    }

    public static string Language
    {
        get => ReadText(ClickraSettings.Language);
        set => WriteText(ClickraSettings.Language, value);
    }

    public static string TranslationTargetLanguage
    {
        get => ReadText(ClickraSettings.TranslateTargetLang);
        set => WriteText(ClickraSettings.TranslateTargetLang, value);
    }

    public static string TranslationEngine
    {
        get => ReadText(ClickraSettings.TranslateEngine);
        set => WriteText(ClickraSettings.TranslateEngine, value);
    }

    public static bool QuietMode
    {
        get => ReadBool(ClickraSettings.QuietMode);
        set => WriteBool(ClickraSettings.QuietMode, value);
    }

    public static bool NotificationsEnabled
    {
        get => ReadBool(ClickraSettings.Notification);
        set => WriteBool(ClickraSettings.Notification, value);
    }

    public static string LibreOfficePath
    {
        get => ReadText(ClickraSettings.LibreOfficePath);
        set => WriteText(ClickraSettings.LibreOfficePath, value ?? string.Empty);
    }

    public static bool LibreOfficeRemovalPendingRestart
    {
        get => ReadBool(ClickraSettings.LibreOfficeRemovalPendingRestart);
        set => WriteBool(ClickraSettings.LibreOfficeRemovalPendingRestart, value);
    }

    public static int PdfCompressionImageLevel
    {
        get => ClickraSettings.NormalizePdfCompressLevel(ReadInt(ClickraSettings.PdfCompressImageLevel));
        set => WriteInt(ClickraSettings.PdfCompressImageLevel, ClickraSettings.NormalizePdfCompressLevel(value));
    }

    public static bool PdfCompressionStripFonts
    {
        get => ReadBool(ClickraSettings.PdfCompressStripFonts);
        set => WriteBool(ClickraSettings.PdfCompressStripFonts, value);
    }

    public static bool PdfCompressionMinifyContent
    {
        get => ReadBool(ClickraSettings.PdfCompressMinifyContent);
        set => WriteBool(ClickraSettings.PdfCompressMinifyContent, value);
    }

    public static int ImageCompressionLevel
    {
        get => ReadClampedInt(ClickraSettings.ImageCompressLevel);
        set => WriteInt(ClickraSettings.ImageCompressLevel, ClickraSettings.ClampNumericSetting(ClickraSettings.ImageCompressLevel, value));
    }

    public static int ImageCompressionMaxDimension
    {
        get => ReadClampedInt(ClickraSettings.ImageCompressMaxDimension);
        set => WriteInt(ClickraSettings.ImageCompressMaxDimension, ClickraSettings.ClampNumericSetting(ClickraSettings.ImageCompressMaxDimension, value));
    }

    public static int ParkedTaskRetentionDays
    {
        get => ReadClampedInt(ClickraSettings.ParkedTaskRetention);
        set => WriteInt(ClickraSettings.ParkedTaskRetention, ClickraSettings.ClampNumericSetting(ClickraSettings.ParkedTaskRetention, value));
    }

    public static OfficeEnginePreference OfficeEngine
    {
        get => ReadText(ClickraSettings.OfficeEngine).ToLowerInvariant() switch
        {
            ClickraSettings.OfficeEngineMicrosoft => OfficeEnginePreference.Microsoft,
            ClickraSettings.OfficeEngineLibreOffice => OfficeEnginePreference.LibreOffice,
            _ => OfficeEnginePreference.Auto
        };
        set => WriteText(ClickraSettings.OfficeEngine, value switch
        {
            OfficeEnginePreference.Microsoft => ClickraSettings.OfficeEngineMicrosoft,
            OfficeEnginePreference.LibreOffice => ClickraSettings.OfficeEngineLibreOffice,
            _ => ClickraSettings.DefaultOfficeEngineAuto
        });
    }

    public static string OfficeEngineValue => OfficeEngine switch
    {
        OfficeEnginePreference.Microsoft => ClickraSettings.OfficeEngineMicrosoft,
        OfficeEnginePreference.LibreOffice => ClickraSettings.OfficeEngineLibreOffice,
        _ => ClickraSettings.DefaultOfficeEngineAuto
    };

    public static OutputDirectoryPreference OutputDirectory
    {
        get
        {
            string value = ReadText(ClickraSettings.OutputDir);
            if (value.Equals(ClickraSettings.OutputDirDesktop, StringComparison.OrdinalIgnoreCase))
                return new(OutputDirectoryKind.Desktop);
            if (value.Equals(ClickraSettings.OutputDirDownloads, StringComparison.OrdinalIgnoreCase))
                return new(OutputDirectoryKind.Downloads);
            if (value.Equals(ClickraSettings.DefaultOutputDirSource, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
                return new(OutputDirectoryKind.Source);
            return new(OutputDirectoryKind.Custom, value);
        }
        set => WriteText(ClickraSettings.OutputDir, value.Kind switch
        {
            OutputDirectoryKind.Desktop => ClickraSettings.OutputDirDesktop,
            OutputDirectoryKind.Downloads => ClickraSettings.OutputDirDownloads,
            OutputDirectoryKind.Custom when !string.IsNullOrWhiteSpace(value.CustomPath) => value.CustomPath,
            _ => ClickraSettings.DefaultOutputDirSource
        });
    }

    public static void EnsureFresh() => ClickraStorage.EnsureFreshSettings();

    internal static string ReadText(string key) => ClickraStorage.GetSetting(key);
    internal static bool ReadBool(string key) => ClickraStorage.GetSettingBool(key);
    internal static int ReadInt(string key) => ClickraStorage.GetSettingInt(key);
    internal static int ReadClampedInt(string key) => ClickraSettings.ClampNumericSetting(key, ReadInt(key));
    internal static void WriteText(string key, string value) => ClickraStorage.SaveSetting(key, value);
    internal static void WriteBool(string key, bool value) => WriteText(key, value ? ClickraSettings.ValueTrue : ClickraSettings.ValueFalse);
    internal static void WriteInt(string key, int value) => WriteText(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

}
