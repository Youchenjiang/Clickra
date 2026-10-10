using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Clickra_Fluent;

public sealed partial class MainPage
{
    private bool _libreOfficeSetupInProgress;

    private void RefreshLibreOfficeStatus()
    {
        string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
        bool removalPending = ClickraStorage.GetSettingBool(ClickraSettings.LibreOfficeRemovalPendingRestart);
        bool ready = !string.IsNullOrWhiteSpace(resolvedPath);
        string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();
        // Only a LibreOffice with a freshly verified Clickra management identity may be removed here.
        // Anything else remains user-managed and has to be removed through Windows.
        bool installedByClickra = LibreOfficeEngineInstaller.WasInstalledByClickra();
        bool canAdopt = !installedByClickra &&
                        LibreOfficeEngineInstaller.CanAdoptExistingInstallation(resolvedPath);

        LibreOfficeStatusText.Text = GetLibreOfficeStatusText(
            installedVersion,
            removalPending,
            ready,
            installedByClickra);
        LibreOfficePathText.Text = ready ? resolvedPath : "";
        LibreOfficeSetupProgress.Visibility = _libreOfficeSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        LibreOfficeBrowseButton.IsEnabled = !_libreOfficeSetupInProgress;
        LibreOfficeDownloadButton.IsEnabled = !_libreOfficeSetupInProgress;
        LibreOfficeUninstallButton.IsEnabled = !_libreOfficeSetupInProgress && ready && installedByClickra;
        LibreOfficeUninstallButton.Visibility = ready && installedByClickra ? Visibility.Visible : Visibility.Collapsed;
        LibreOfficeAdoptButton.IsEnabled = !_libreOfficeSetupInProgress && canAdopt;
        LibreOfficeAdoptButton.Visibility = canAdopt ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetLibreOfficeStatusText(
        string installedVersion,
        bool removalPending,
        bool ready,
        bool installedByClickra)
    {
        if (_libreOfficeSetupInProgress)
            return LibreOfficeStatusText.Text;
        if (removalPending)
            return L("setting_libreoffice_removal_pending");
        if (!ready)
            return L("setting_libreoffice_missing");

        string statusText = string.IsNullOrWhiteSpace(installedVersion)
            ? L("setting_libreoffice_ready")
            : $"{L("setting_libreoffice_ready")} · {installedVersion}";
        if (!installedByClickra)
            statusText += $"\n{L("setting_libreoffice_external_note")}";
        return statusText;
    }

    private async Task BrowseLibreOfficeAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".com");
        if (App.MainWindow is not null)
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        if (!LibreOfficeHelper.LooksLikeLibreOfficeExecutable(file.Path))
        {
            await ShowErrorAsync(L("setting_libreoffice_invalid"));
            return;
        }

        ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, file.Path);
        ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);
        RefreshLibreOfficeStatus();
    }

    private async Task InstallLibreOfficeAsync()
    {
        if (_libreOfficeSetupInProgress)
        {
            await ShowErrorAsync(L("setting_libreoffice_download_in_progress"));
            return;
        }

        bool removalPending = ClickraStorage.GetSettingBool(ClickraSettings.LibreOfficeRemovalPendingRestart);
        var package = LibreOfficeEngineInstaller.RecommendedPackage;
        string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();
        if (await TryHandleCurrentLibreOfficeAsync(removalPending, installedVersion))
            return;

        string prompt = string.Format(
            L("setting_libreoffice_download_prompt"),
            package.Version,
            package.Edition,
            FormatBytes(package.DownloadBytes),
            LibreOfficeEngineInstaller.GetDefaultInstallRoot(),
            package.Sha256);
        if (!await ConfirmAsync(prompt)) return;

        _libreOfficeSetupInProgress = true;
        LibreOfficeSetupProgress.Value = 0;
        LibreOfficeStatusText.Text = L(removalPending ? "setting_libreoffice_reinstall_starting" : "setting_libreoffice_download_starting");
        RefreshLibreOfficeStatus();

        try
        {
            string downloadDir = Path.Combine(ClickraStorage.GetDataDir(), ClickraSettings.OutputDirDownloads);
            var progress = new Progress<int>(percent =>
            {
                int displayPercent = Math.Min(80, Math.Max(1, percent * 80 / 100));
                LibreOfficeSetupProgress.Value = displayPercent;
                LibreOfficeStatusText.Text = percent >= 100
                    ? L("setting_libreoffice_verifying")
                    : string.Format(L("setting_libreoffice_download_progress"), percent);
            });

            string installerPath = await LibreOfficeEngineInstaller.DownloadAndVerifyAsync(
                package,
                downloadDir,
                progress,
                CancellationToken.None);

            LibreOfficeSetupProgress.Value = 85;
            LibreOfficeStatusText.Text = L("setting_libreoffice_installing");
            LibreOfficeInstallResult result = await LibreOfficeEngineInstaller.InstallMsiPackageAsync(installerPath, CancellationToken.None);
            string sofficePath = result.SofficePath;
            if (!result.RestartRequired && !LibreOfficeHelper.LooksLikeLibreOfficeExecutable(sofficePath))
                throw new InvalidOperationException(L("setting_libreoffice_validation_failed"));

            if (!string.IsNullOrWhiteSpace(sofficePath))
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, sofficePath);
            bool managementRecorded = LibreOfficeEngineInstaller.TryRecordManagedSystemInstallation(sofficePath);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);
            LibreOfficeSetupProgress.Value = 100;

            await ShowErrorAsync(string.Format(
                L(result.RestartRequired ? "setting_libreoffice_install_restart_required" : "setting_libreoffice_download_ready"),
                string.IsNullOrWhiteSpace(sofficePath) ? LibreOfficeEngineInstaller.GetDefaultInstallRoot() : sofficePath));
            if (!managementRecorded)
                await ShowErrorAsync(L("setting_libreoffice_management_unverified"));
        }
        catch (Exception ex)
        {
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty);
            await ShowErrorAsync(string.Format(L("setting_libreoffice_download_failed"), ex.Message));
        }
        finally
        {
            _libreOfficeSetupInProgress = false;
            RefreshLibreOfficeStatus();
        }
    }

    private async Task<bool> TryHandleCurrentLibreOfficeAsync(bool removalPending, string installedVersion)
    {
        if (removalPending ||
            string.IsNullOrWhiteSpace(installedVersion) ||
            !LibreOfficeEngineInstaller.IsRecommendedVersionInstalled())
        {
            return false;
        }

        string resolvedPath = LibreOfficeEngineInstaller.ResolveSystemSofficePath();
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, resolvedPath);
        await ShowErrorAsync(string.Format(L("setting_libreoffice_already_current"), installedVersion));
        RefreshLibreOfficeStatus();
        return true;
    }

    private async Task UninstallLibreOfficeAsync()
    {
        if (_libreOfficeSetupInProgress)
        {
            await ShowErrorAsync(L("setting_libreoffice_download_in_progress"));
            return;
        }
        if (ClickraStorage.GetSettingBool(ClickraSettings.LibreOfficeRemovalPendingRestart))
        {
            await ShowErrorAsync(L("setting_libreoffice_removal_pending"));
            return;
        }
        // The button is disabled in this case, but the action has to refuse on its own too: removing a
        // LibreOffice the user installed themselves would be destructive and hard to undo.
        if (!LibreOfficeEngineInstaller.WasInstalledByClickra())
        {
            await ShowErrorAsync(L("setting_libreoffice_external_note"));
            return;
        }
        if (!await ConfirmAsync(L("setting_libreoffice_uninstall_confirm"))) return;

        _libreOfficeSetupInProgress = true;
        LibreOfficeSetupProgress.Value = 60;
        LibreOfficeStatusText.Text = L("setting_libreoffice_uninstalling");
        RefreshLibreOfficeStatus();
        try
        {
            LibreOfficeUninstallResult result = await LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync(CancellationToken.None);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty);
            LibreOfficeEngineInstaller.ReleaseManagement();
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, result.RestartRequired ? ClickraSettings.ValueTrue : ClickraSettings.ValueFalse);
            ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.DefaultOfficeEngineAuto);
            _loadingSettings = true;
            EngineCombo.SelectedIndex = 0;
            _loadingSettings = false;
            await ShowErrorAsync(L(result.RestartRequired ? "setting_libreoffice_uninstall_restart_required" : "setting_libreoffice_uninstall_ready"));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(string.Format(L("setting_libreoffice_uninstall_failed"), ex.Message));
        }
        finally
        {
            _libreOfficeSetupInProgress = false;
            RefreshLibreOfficeStatus();
        }
    }

    private async Task AdoptLibreOfficeAsync()
    {
        if (_libreOfficeSetupInProgress) return;
        if (LibreOfficeEngineInstaller.WasInstalledByClickra()) return;
        string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
        if (!LibreOfficeEngineInstaller.CanAdoptExistingInstallation(resolvedPath)) return;
        if (!await ConfirmAsync(L("setting_libreoffice_adopt_confirm"))) return;

        try
        {
            LibreOfficeEngineInstaller.AdoptExistingInstallation();
        }
        catch (InvalidOperationException)
        {
            await ShowErrorAsync(L("setting_libreoffice_external_note"));
            RefreshLibreOfficeStatus();
            return;
        }
        RefreshLibreOfficeStatus();
        await ShowErrorAsync(L("setting_libreoffice_adopt_success"));
    }
}
