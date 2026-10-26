using Clickra.Core.Modules.Settings.Application;

using Clickra.Core.Modules.Settings.Model;

namespace Clickra.Core.Modules.Settings.Presentation;

public sealed record LibreOfficePresentationState(
    bool RemovalPendingRestart,
    string ResolvedPath,
    bool IsReady,
    string InstalledVersion,
    bool IsManagedByClickra,
    bool CanAdopt,
    bool IsRecommendedVersionInstalled);

public sealed record LibreOfficeInstallPresentationResult(
    bool RestartRequired,
    string SofficePath,
    bool ManagementRecorded);

public sealed record LibreOfficeUninstallPresentationResult(bool RestartRequired);

public sealed record LibreOfficePackagePresentation(
    string Version,
    string Edition,
    long DownloadBytes,
    string Sha256);

public sealed record OfficeReadinessPresentationState(
    bool MicrosoftReady,
    bool LibreOfficeReady,
    bool SelectedEngineReady,
    string PreferredReadyEngine);

/// <summary>
/// Authoritative presentation boundary for Settings. UI surfaces render these typed values and
/// forward intent here; persisted representation and LibreOffice management orchestration remain
/// behind Application/Infrastructure boundaries.
/// </summary>
public sealed class SettingsPresentationController : ISettingsPresentationController
{
    private static readonly IReadOnlyList<SettingDescriptor> CachedDynamicDescriptors =
        SettingPageRegistry.AllDescriptors.Where(descriptor => !SettingPageRegistry.IsPrimaryRendererSetting(descriptor.Key)).ToArray();

    public static SettingsPresentationController Shared { get; } = new();

    private SettingsPresentationController()
    {
    }

    public event Action? Reloaded
    {
        add => SettingsController.Reloaded += value;
        remove => SettingsController.Reloaded -= value;
    }

    public string Language
    {
        get => SettingsController.Language;
        set => SettingsController.Language = value;
    }

    public string TranslationTargetLanguage
    {
        get => SettingsController.TranslationTargetLanguage;
        set => SettingsController.TranslationTargetLanguage = value;
    }

    public string TranslationEngine
    {
        get => SettingsController.TranslationEngine;
        set => SettingsController.TranslationEngine = value;
    }

    public bool QuietMode
    {
        get => SettingsController.QuietMode;
        set => SettingsController.QuietMode = value;
    }

    public bool NotificationsEnabled
    {
        get => SettingsController.NotificationsEnabled;
        set => SettingsController.NotificationsEnabled = value;
    }

    public string LibreOfficePath
    {
        get => SettingsController.LibreOfficePath;
        set => SettingsController.LibreOfficePath = value;
    }

    public bool LibreOfficeRemovalPendingRestart
    {
        get => SettingsController.LibreOfficeRemovalPendingRestart;
        set => SettingsController.LibreOfficeRemovalPendingRestart = value;
    }

    public int PdfCompressionImageLevel
    {
        get => SettingsController.PdfCompressionImageLevel;
        set => SettingsController.PdfCompressionImageLevel = value;
    }

    public bool PdfCompressionStripFonts
    {
        get => SettingsController.PdfCompressionStripFonts;
        set => SettingsController.PdfCompressionStripFonts = value;
    }

    public bool PdfCompressionMinifyContent
    {
        get => SettingsController.PdfCompressionMinifyContent;
        set => SettingsController.PdfCompressionMinifyContent = value;
    }

    public int ImageCompressionLevel
    {
        get => SettingsController.ImageCompressionLevel;
        set => SettingsController.ImageCompressionLevel = value;
    }

    public int ImageCompressionMaxDimension
    {
        get => SettingsController.ImageCompressionMaxDimension;
        set => SettingsController.ImageCompressionMaxDimension = value;
    }

    public int ParkedTaskRetentionDays
    {
        get => SettingsController.ParkedTaskRetentionDays;
        set => SettingsController.ParkedTaskRetentionDays = value;
    }

    public OfficeEnginePreference OfficeEngine
    {
        get => SettingsController.OfficeEngine;
        set => SettingsController.OfficeEngine = value;
    }

    public string OfficeEngineValue => SettingsController.OfficeEngineValue;

    public OutputDirectoryPreference OutputDirectory
    {
        get => SettingsController.OutputDirectory;
        set => SettingsController.OutputDirectory = value;
    }

    public IReadOnlyList<SettingDescriptor> Descriptors => SettingPageRegistry.AllDescriptors;

    public IReadOnlyList<SettingDescriptor> DynamicDescriptors => CachedDynamicDescriptors;

    public bool IsPrimaryRendererSetting(SettingDescriptor descriptor) =>
        SettingPageRegistry.IsPrimaryRendererSetting(descriptor.Key);

    public SettingDescriptor PdfCompressionLevelDescriptor => GetRequiredDescriptor(SettingsContract.PdfCompressionImageLevel);
    public SettingDescriptor PdfCompressionStripFontsDescriptor => GetRequiredDescriptor(SettingsContract.PdfCompressionStripFonts);
    public SettingDescriptor PdfCompressionMinifyContentDescriptor => GetRequiredDescriptor(SettingsContract.PdfCompressionMinifyContent);

    public bool IsImageCompressionDescriptor(SettingDescriptor descriptor) =>
        descriptor.Key.Equals(SettingsContract.ImageCompressionLevel, StringComparison.OrdinalIgnoreCase) ||
        descriptor.Key.Equals(SettingsContract.ImageCompressionMaxDimension, StringComparison.OrdinalIgnoreCase);

    public SettingDescriptor GetRequiredDescriptor(string key) =>
        SettingPageRegistry.GetDescriptor(key)
        ?? throw new InvalidOperationException($"Setting descriptor is not registered: {key}");

    public bool ReadBool(SettingDescriptor descriptor)
    {
        RequireEditor(descriptor, SettingEditorKind.Toggle);
        return SettingsController.ReadBool(descriptor.Key);
    }

    public int ReadInt(SettingDescriptor descriptor)
    {
        RequireNumericEditor(descriptor);
        NumericSettingRange? range = descriptor.GetEffectiveNumericRange();
        int value = SettingsController.ReadInt(descriptor.Key);
        return range.HasValue ? range.Value.Clamp(value) : value;
    }

    public string ReadChoice(SettingDescriptor descriptor)
    {
        RequireEditor(descriptor, SettingEditorKind.Choice);
        return SettingsController.ReadText(descriptor.Key);
    }

    public void WriteBool(SettingDescriptor descriptor, bool value)
    {
        RequireEditor(descriptor, SettingEditorKind.Toggle);
        SettingsController.WriteBool(descriptor.Key, value);
    }

    public void WriteInt(SettingDescriptor descriptor, int value)
    {
        RequireNumericEditor(descriptor);
        NumericSettingRange? range = descriptor.GetEffectiveNumericRange();
        SettingsController.WriteInt(descriptor.Key, range.HasValue ? range.Value.Clamp(value) : value);
    }

    public void WriteChoice(SettingDescriptor descriptor, string value)
    {
        RequireEditor(descriptor, SettingEditorKind.Choice);
        if (descriptor.Options is { Count: > 0 } &&
            descriptor.Options.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)) &&
            !string.Equals(descriptor.Key, SettingsContract.OutputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value is not registered for setting '{descriptor.Key}'.");
        }
        SettingsController.WriteText(descriptor.Key, value);
    }
    public void EnsureFresh() => SettingsController.EnsureFresh();

    public string GetPdfCompressionSliderLabelKey(int sliderValue)
    {
        SettingDescriptor descriptor = GetRequiredDescriptor(SettingsContract.PdfCompressionImageLevel);
        IReadOnlyList<string> labels = descriptor.SliderLabels ?? Array.Empty<string>();
        int normalized = SettingsContract.ClampNumeric(SettingsContract.PdfCompressionImageLevel, sliderValue);
        return normalized >= 0 && normalized < labels.Count
            ? labels[normalized]
            : "setting_pdf_compress_level_std";
    }

    public int MinParkedRetentionDays => SettingsContract.MinParkedRetentionDays;
    public int MaxParkedRetentionDays => SettingsContract.MaxParkedRetentionDays;
    public int DefaultParkedRetentionDays => SettingsContract.DefaultParkedRetentionDays;
    public int MinPdfCompressionLevel => SettingsContract.MinPdfCompressionLevel;
    public int MaxPdfCompressionLevel => SettingsContract.MaxPdfCompressionLevel;
    public string DefaultTranslationTargetLanguage => SettingsContract.DefaultTranslationTargetLanguage;
    public string DefaultTranslationEngine => SettingsContract.DefaultTranslationEngine;

    public string GetOfficeEngineLabelKey(string engine) => engine switch
    {
        SettingsContract.OfficeEngineMicrosoft => "setting_engine_microsoft",
        SettingsContract.OfficeEngineLibreOffice => "setting_engine_libreoffice",
        _ => "setting_engine_auto"
    };

    public string GetDownloadsDirectory() => StoragePathController.GetDownloadsDirectory();

    public string GetDataDirectory() => StoragePathController.GetDataDirectory();

    public LibreOfficePresentationState GetLibreOfficeState()
    {
        LibreOfficeSettingsState state = LibreOfficeSettingsController.GetState();
        return new LibreOfficePresentationState(
            state.RemovalPendingRestart,
            state.ResolvedPath,
            state.IsReady,
            state.InstalledVersion,
            state.IsManagedByClickra,
            state.CanAdopt,
            state.IsRecommendedVersionInstalled);
    }

    public OfficeReadinessPresentationState GetOfficeReadiness()
    {
        OfficeReadinessState state = LibreOfficeSettingsController.GetOfficeReadiness();
        return new OfficeReadinessPresentationState(
            state.MicrosoftReady,
            state.LibreOfficeReady,
            state.SelectedEngineReady,
            state.PreferredReadyEngine);
    }

    public bool IsOfficeCommandReady(string command) => LibreOfficeSettingsController.IsOfficeCommandReady(command);

    public string GetOfficeUnavailableErrorKey() => LibreOfficeSettingsController.GetOfficeUnavailableErrorKey();

    public LibreOfficePackagePresentation RecommendedLibreOfficePackage
    {
        get
        {
            var package = LibreOfficeSettingsController.RecommendedPackage;
            return new LibreOfficePackagePresentation(package.Version, package.Edition, package.DownloadBytes, package.Sha256);
        }
    }

    public string DefaultLibreOfficeInstallRoot => LibreOfficeSettingsController.DefaultInstallRoot;

    public bool TryUseLibreOfficeExecutable(string path) => LibreOfficeSettingsController.TryUseExecutable(path);

    public bool TryUseCurrentRecommendedLibreOffice(out string installedVersion)
    {
        return LibreOfficeSettingsController.TryUseCurrentRecommended(out installedVersion);
    }

    public async Task<LibreOfficeInstallPresentationResult> InstallRecommendedLibreOfficeAsync(
        string downloadDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        LibreOfficeSettingsInstallResult result = await LibreOfficeSettingsController.InstallRecommendedAsync(
            downloadDirectory,
            progress,
            cancellationToken).ConfigureAwait(false);
        return new LibreOfficeInstallPresentationResult(result.RestartRequired, result.SofficePath, result.ManagementRecorded);
    }

    public async Task<LibreOfficeUninstallPresentationResult> UninstallManagedLibreOfficeAsync(CancellationToken cancellationToken)
    {
        LibreOfficeSettingsUninstallResult result = await LibreOfficeSettingsController.UninstallManagedAsync(cancellationToken).ConfigureAwait(false);
        return new LibreOfficeUninstallPresentationResult(result.RestartRequired);
    }

    public bool AdoptExistingLibreOffice()
    {
        return LibreOfficeSettingsController.AdoptExisting();
    }

    private static void RequireEditor(SettingDescriptor descriptor, SettingEditorKind expected)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.EditorKind != expected)
            throw new ArgumentException($"Setting '{descriptor.Key}' is {descriptor.EditorKind}, not {expected}.", nameof(descriptor));
    }

    private static void RequireNumericEditor(SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.EditorKind is not (SettingEditorKind.Slider or SettingEditorKind.Number))
            throw new ArgumentException($"Setting '{descriptor.Key}' is not numeric.", nameof(descriptor));
    }
}
