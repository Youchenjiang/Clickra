using Clickra.Core.Modules.Office.Infrastructure;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Settings.Application;

public sealed record LibreOfficeSettingsState(
    bool RemovalPendingRestart,
    string ResolvedPath,
    bool IsReady,
    string InstalledVersion,
    bool IsManagedByClickra,
    bool CanAdopt,
    bool IsRecommendedVersionInstalled);

public sealed record LibreOfficeSettingsInstallResult(
    bool RestartRequired,
    string SofficePath,
    bool ManagementRecorded);

public sealed record LibreOfficeSettingsUninstallResult(bool RestartRequired);

public sealed record OfficeReadinessState(
    bool MicrosoftReady,
    bool LibreOfficeReady,
    bool SelectedEngineReady,
    string PreferredReadyEngine);

/// <summary>
/// Application owner for LibreOffice lifecycle/readiness policy. Presentation surfaces consume
/// typed state/actions without reaching into processor or infrastructure implementations.
/// </summary>
public static class LibreOfficeSettingsController
{
    public static LibreOfficeSettingsState GetState()
    {
        bool removalPending = SettingsController.LibreOfficeRemovalPendingRestart;
        string configuredPath = SettingsController.LibreOfficePath;
        string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath(removalPending, configuredPath);
        bool managed = LibreOfficeEngineInstaller.WasInstalledByClickra();
        return new LibreOfficeSettingsState(
            removalPending,
            resolvedPath,
            !string.IsNullOrWhiteSpace(resolvedPath),
            LibreOfficeEngineInstaller.GetInstalledSystemVersion(),
            managed,
            !managed && LibreOfficeEngineInstaller.CanAdoptExistingInstallation(resolvedPath),
            LibreOfficeEngineInstaller.IsRecommendedVersionInstalled());
    }

    public static LibreOfficeEnginePackage RecommendedPackage => LibreOfficeEngineInstaller.RecommendedPackage;

    public static string DefaultInstallRoot => LibreOfficeEngineInstaller.GetDefaultInstallRoot();

    public static bool TryUseExecutable(string path)
    {
        if (!LibreOfficeHelper.LooksLikeLibreOfficeExecutable(path)) return false;
        SettingsController.LibreOfficePath = path;
        SettingsController.LibreOfficeRemovalPendingRestart = false;
        return true;
    }

    public static bool TryUseCurrentRecommended(out string installedVersion)
    {
        LibreOfficeSettingsState state = GetState();
        installedVersion = state.InstalledVersion;
        if (state.RemovalPendingRestart || string.IsNullOrWhiteSpace(installedVersion) || !state.IsRecommendedVersionInstalled)
            return false;

        string resolvedPath = LibreOfficeEngineInstaller.ResolveSystemSofficePath();
        if (!string.IsNullOrWhiteSpace(resolvedPath)) SettingsController.LibreOfficePath = resolvedPath;
        return true;
    }

    public static async Task<LibreOfficeSettingsInstallResult> InstallRecommendedAsync(
        string downloadDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        LibreOfficeEnginePackage package = RecommendedPackage;
        string installerPath = await LibreOfficeEngineInstaller.DownloadAndVerifyAsync(
            package,
            downloadDirectory,
            progress,
            cancellationToken).ConfigureAwait(false);
        LibreOfficeInstallResult result = await LibreOfficeEngineInstaller.InstallMsiPackageAsync(
            installerPath,
            cancellationToken).ConfigureAwait(false);
        string sofficePath = result.SofficePath;
        if (!result.RestartRequired && !LibreOfficeHelper.LooksLikeLibreOfficeExecutable(sofficePath))
            throw new InvalidOperationException("Installed LibreOffice executable could not be validated.");

        if (!string.IsNullOrWhiteSpace(sofficePath)) SettingsController.LibreOfficePath = sofficePath;
        bool managementRecorded = LibreOfficeEngineInstaller.TryRecordManagedSystemInstallation(sofficePath);
        SettingsController.LibreOfficeRemovalPendingRestart = false;
        return new LibreOfficeSettingsInstallResult(result.RestartRequired, sofficePath, managementRecorded);
    }

    public static async Task<LibreOfficeSettingsUninstallResult> UninstallManagedAsync(CancellationToken cancellationToken)
    {
        LibreOfficeSettingsState state = GetState();
        if (state.RemovalPendingRestart)
            throw new InvalidOperationException("LibreOffice removal is pending restart.");
        if (!state.IsManagedByClickra)
            throw new InvalidOperationException("LibreOffice is not managed by Clickra.");

        LibreOfficeUninstallResult result = await LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync(cancellationToken).ConfigureAwait(false);
        SettingsController.LibreOfficePath = string.Empty;
        LibreOfficeEngineInstaller.ReleaseManagement();
        SettingsController.LibreOfficeRemovalPendingRestart = result.RestartRequired;
        SettingsController.OfficeEngine = OfficeEnginePreference.Auto;
        return new LibreOfficeSettingsUninstallResult(result.RestartRequired);
    }

    public static bool AdoptExisting()
    {
        LibreOfficeSettingsState state = GetState();
        if (!state.CanAdopt) return false;
        LibreOfficeEngineInstaller.AdoptExistingInstallation();
        return true;
    }

    public static OfficeReadinessState GetOfficeReadiness()
    {
        bool microsoftReady = OfficeEngineDetector.IsMicrosoftSuiteReady();
        bool libreOfficeReady = OfficeEngineDetector.IsLibreOfficeReady(
            SettingsController.LibreOfficeRemovalPendingRestart,
            SettingsController.LibreOfficePath);
        string configured = SettingsController.OfficeEngineValue;
        return new OfficeReadinessState(
            microsoftReady,
            libreOfficeReady,
            OfficeEngineDetector.IsSelectedEngineReady(configured, microsoftReady, libreOfficeReady),
            OfficeEngineDetector.GetPreferredReadyEngine(configured, microsoftReady, libreOfficeReady));
    }

    public static bool IsOfficeCommandReady(string command) => OfficeEngineDetector.IsCommandReady(
        command,
        SettingsController.OfficeEngineValue,
        SettingsController.LibreOfficeRemovalPendingRestart,
        SettingsController.LibreOfficePath);

    public static string GetOfficeUnavailableErrorKey() =>
        OfficeEngineDetector.GetUnavailableErrorKey(SettingsController.OfficeEngineValue);
}
