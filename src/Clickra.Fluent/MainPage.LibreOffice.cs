using Clickra.Core;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Clickra_Fluent;

public sealed partial class MainPage
{
    private bool _libreOfficeSetupInProgress;

    private void RefreshLibreOfficeStatus()
    {
        LibreOfficePresentationState state = SettingsPresentationController.Shared.GetLibreOfficeState();

        LibreOfficeStatusText.Text = GetLibreOfficeStatusText(
            state.InstalledVersion,
            state.RemovalPendingRestart,
            state.IsReady,
            state.IsManagedByClickra);
        LibreOfficePathText.Text = state.IsReady ? state.ResolvedPath : "";
        LibreOfficeSetupProgress.Visibility = _libreOfficeSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        LibreOfficeBrowseButton.IsEnabled = !_libreOfficeSetupInProgress;
        LibreOfficeDownloadButton.IsEnabled = !_libreOfficeSetupInProgress;
        LibreOfficeUninstallButton.IsEnabled = !_libreOfficeSetupInProgress && state.IsReady && state.IsManagedByClickra;
        LibreOfficeUninstallButton.Visibility = state.IsReady && state.IsManagedByClickra ? Visibility.Visible : Visibility.Collapsed;
        LibreOfficeAdoptButton.IsEnabled = !_libreOfficeSetupInProgress && state.CanAdopt;
        LibreOfficeAdoptButton.Visibility = state.CanAdopt ? Visibility.Visible : Visibility.Collapsed;
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
        if (!SettingsPresentationController.Shared.TryUseLibreOfficeExecutable(file.Path))
        {
            await ShowErrorAsync(L("setting_libreoffice_invalid"));
            return;
        }
        RefreshLibreOfficeStatus();
    }

    private async Task InstallLibreOfficeAsync()
    {
        if (_libreOfficeSetupInProgress)
        {
            await ShowErrorAsync(L("setting_libreoffice_download_in_progress"));
            return;
        }

        bool removalPending = SettingsPresentationController.Shared.LibreOfficeRemovalPendingRestart;
        var package = SettingsPresentationController.Shared.RecommendedLibreOfficePackage;
        if (await TryHandleCurrentLibreOfficeAsync())
            return;

        string prompt = string.Format(
            L("setting_libreoffice_download_prompt"),
            package.Version,
            package.Edition,
            FormatBytes(package.DownloadBytes),
            SettingsPresentationController.Shared.DefaultLibreOfficeInstallRoot,
            package.Sha256);
        if (!await ConfirmAsync(prompt)) return;

        _libreOfficeSetupInProgress = true;
        LibreOfficeSetupProgress.Value = 0;
        LibreOfficeStatusText.Text = L(removalPending ? "setting_libreoffice_reinstall_starting" : "setting_libreoffice_download_starting");
        RefreshLibreOfficeStatus();

        try
        {
            string downloadDir = SettingsPresentationController.Shared.GetDownloadsDirectory();
            var progress = new Progress<int>(percent =>
            {
                int displayPercent = Math.Min(80, Math.Max(1, percent * 80 / 100));
                LibreOfficeSetupProgress.Value = displayPercent;
                LibreOfficeStatusText.Text = percent >= 100
                    ? L("setting_libreoffice_verifying")
                    : string.Format(L("setting_libreoffice_download_progress"), percent);
            });

            LibreOfficeInstallPresentationResult result = await SettingsPresentationController.Shared.InstallRecommendedLibreOfficeAsync(
                downloadDir,
                progress,
                CancellationToken.None);
            string sofficePath = result.SofficePath;
            LibreOfficeSetupProgress.Value = 100;

            await ShowErrorAsync(string.Format(
                L(result.RestartRequired ? "setting_libreoffice_install_restart_required" : "setting_libreoffice_download_ready"),
                string.IsNullOrWhiteSpace(sofficePath) ? SettingsPresentationController.Shared.DefaultLibreOfficeInstallRoot : sofficePath));
            if (!result.ManagementRecorded)
                await ShowErrorAsync(L("setting_libreoffice_management_unverified"));
        }
        catch (Exception ex)
        {
            SettingsPresentationController.Shared.LibreOfficePath = string.Empty;
            await ShowErrorAsync(string.Format(L("setting_libreoffice_download_failed"), ex.Message));
        }
        finally
        {
            _libreOfficeSetupInProgress = false;
            RefreshLibreOfficeStatus();
        }
    }

    private async Task<bool> TryHandleCurrentLibreOfficeAsync()
    {
        if (!SettingsPresentationController.Shared.TryUseCurrentRecommendedLibreOffice(out string installedVersion))
        {
            return false;
        }
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
        LibreOfficePresentationState state = SettingsPresentationController.Shared.GetLibreOfficeState();
        if (state.RemovalPendingRestart)
        {
            await ShowErrorAsync(L("setting_libreoffice_removal_pending"));
            return;
        }
        // The button is disabled in this case, but the action has to refuse on its own too: removing a
        // LibreOffice the user installed themselves would be destructive and hard to undo.
        if (!state.IsManagedByClickra)
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
            LibreOfficeUninstallPresentationResult result = await SettingsPresentationController.Shared.UninstallManagedLibreOfficeAsync(CancellationToken.None);
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
        LibreOfficePresentationState state = SettingsPresentationController.Shared.GetLibreOfficeState();
        if (!state.CanAdopt) return;
        if (!await ConfirmAsync(L("setting_libreoffice_adopt_confirm"))) return;

        try
        {
            if (!SettingsPresentationController.Shared.AdoptExistingLibreOffice()) return;
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
