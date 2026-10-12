namespace Clickra.Core.Application;

/// <summary>
/// Presentation-neutral names and scalar metadata for user settings. Persisted storage details
/// remain owned by ClickraSettings; presentation code consumes this Application contract instead.
/// </summary>
public static class SettingsContract
{
    public const string OutputDirectory = ClickraSettings.OutputDir;
    public const string OfficeEngine = ClickraSettings.OfficeEngine;
    public const string Language = ClickraSettings.Language;
    public const string TranslationTargetLanguage = ClickraSettings.TranslateTargetLang;
    public const string QuietMode = ClickraSettings.QuietMode;
    public const string Notifications = ClickraSettings.Notification;
    public const string PdfCompressionImageLevel = ClickraSettings.PdfCompressImageLevel;
    public const string PdfCompressionStripFonts = ClickraSettings.PdfCompressStripFonts;
    public const string PdfCompressionMinifyContent = ClickraSettings.PdfCompressMinifyContent;
    public const string ImageCompressionLevel = ClickraSettings.ImageCompressLevel;
    public const string ImageCompressionMaxDimension = ClickraSettings.ImageCompressMaxDimension;
    public const string ParkedTaskRetention = ClickraSettings.ParkedTaskRetention;

    public const string OutputDirectorySource = ClickraSettings.DefaultOutputDirSource;
    public const string OutputDirectoryDesktop = ClickraSettings.OutputDirDesktop;
    public const string OutputDirectoryDownloads = ClickraSettings.OutputDirDownloads;
    public const string OfficeEngineAuto = ClickraSettings.DefaultOfficeEngineAuto;
    public const string OfficeEngineMicrosoft = ClickraSettings.OfficeEngineMicrosoft;
    public const string OfficeEngineLibreOffice = ClickraSettings.OfficeEngineLibreOffice;

    public static string GetDefault(string key) => ClickraSettings.GetDefault(key);

    public static bool TryGetNumericRange(string key, out NumericSettingRange range) =>
        ClickraSettings.TryGetNumericRange(key, out range);

    public static NumericSettingRange GetNumericRange(string key) => ClickraSettings.GetNumericRange(key);

    public static int ClampNumeric(string key, int value) => ClickraSettings.ClampNumericSetting(key, value);

    public static int MinParkedRetentionDays => ClickraSettings.MinParkedRetentionDays;
    public static int MaxParkedRetentionDays => ClickraSettings.MaxParkedRetentionDays;
    public static int DefaultParkedRetentionDays => ClickraSettings.GetDefaultInt(ClickraSettings.ParkedTaskRetention);
    public static int MinPdfCompressionLevel => ClickraSettings.MinPdfCompressLevel;
    public static int MaxPdfCompressionLevel => ClickraSettings.MaxPdfCompressLevel;
    public static string DefaultTranslationTargetLanguage => ClickraSettings.DefaultTranslateTargetLang;
}
