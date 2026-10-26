using Clickra.Core.Modules.Settings.Application;
using Clickra.Core.Modules.Settings.Model;

namespace Clickra.Core.Modules.Settings.Presentation;

/// <summary>
/// Instance-level presentation contract shared by CLI and Fluent. Keeping this
/// contract non-static supports substituting a presentation adapter in tests.
/// </summary>
public interface ISettingsPresentationController
{
    event Action? Reloaded;

    string Language { get; set; }
    string TranslationTargetLanguage { get; set; }
    string TranslationEngine
    {
        get => DefaultTranslationEngine;
        set { }
    }
    bool QuietMode { get; set; }
    bool NotificationsEnabled { get; set; }
    string LibreOfficePath { get; set; }
    bool LibreOfficeRemovalPendingRestart { get; set; }
    int PdfCompressionImageLevel { get; set; }
    bool PdfCompressionStripFonts { get; set; }
    bool PdfCompressionMinifyContent { get; set; }
    int ImageCompressionLevel { get; set; }
    int ImageCompressionMaxDimension { get; set; }
    int ParkedTaskRetentionDays { get; set; }
    OfficeEnginePreference OfficeEngine { get; set; }
    string OfficeEngineValue { get; }
    OutputDirectoryPreference OutputDirectory { get; set; }

    IReadOnlyList<SettingDescriptor> Descriptors { get; }
    IReadOnlyList<SettingDescriptor> DynamicDescriptors { get; }
    bool IsPrimaryRendererSetting(SettingDescriptor descriptor);
    bool IsImageCompressionDescriptor(SettingDescriptor descriptor);
    SettingDescriptor PdfCompressionLevelDescriptor { get; }
    SettingDescriptor PdfCompressionStripFontsDescriptor { get; }
    SettingDescriptor PdfCompressionMinifyContentDescriptor { get; }
    SettingDescriptor GetRequiredDescriptor(string key);

    bool ReadBool(SettingDescriptor descriptor);
    int ReadInt(SettingDescriptor descriptor);
    string ReadChoice(SettingDescriptor descriptor);
    void WriteBool(SettingDescriptor descriptor, bool value);
    void WriteInt(SettingDescriptor descriptor, int value);
    void WriteChoice(SettingDescriptor descriptor, string value);
    void EnsureFresh();
    string GetPdfCompressionSliderLabelKey(int sliderValue);

    int MinParkedRetentionDays { get; }
    int MaxParkedRetentionDays { get; }
    int DefaultParkedRetentionDays { get; }
    int MinPdfCompressionLevel { get; }
    int MaxPdfCompressionLevel { get; }
    string DefaultTranslationTargetLanguage { get; }
    string DefaultTranslationEngine => SettingsContract.TranslationEngineAuto;
    string GetOfficeEngineLabelKey(string engine);
    string GetDownloadsDirectory();
    string GetDataDirectory();

    LibreOfficePresentationState GetLibreOfficeState();
    OfficeReadinessPresentationState GetOfficeReadiness();
    bool IsOfficeCommandReady(string command);
    string GetOfficeUnavailableErrorKey();
    LibreOfficePackagePresentation RecommendedLibreOfficePackage { get; }
    string DefaultLibreOfficeInstallRoot { get; }
    bool TryUseLibreOfficeExecutable(string path);
    bool TryUseCurrentRecommendedLibreOffice(out string installedVersion);
    Task<LibreOfficeInstallPresentationResult> InstallRecommendedLibreOfficeAsync(
        string downloadDirectory, IProgress<int>? progress, CancellationToken cancellationToken);
    Task<LibreOfficeUninstallPresentationResult> UninstallManagedLibreOfficeAsync(CancellationToken cancellationToken);
    bool AdoptExistingLibreOffice();
}
