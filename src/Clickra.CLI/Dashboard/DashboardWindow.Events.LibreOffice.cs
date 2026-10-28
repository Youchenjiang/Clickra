using System;
using System.IO;
using Clickra.Core;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

    public static partial class DashboardWindow
    {
        static void HandleLibreOfficeClick(IntPtr hwnd, int element)
        {
            if (element == 35)
            {
                HandleLibreOfficeBrowse(hwnd);
            }
            else if (element == 36)
            {
                HandleLibreOfficeDownload(hwnd);
            }
            else if (element == 38)
            {
                HandleLibreOfficeUninstall(hwnd);
            }
            else if (element == 39)
            {
                HandleLibreOfficeAdopt(hwnd);
            }
        }

        static void HandleLibreOfficeBrowse(IntPtr hwnd)
        {
            const string sofficeFilter = "LibreOffice soffice.exe\0soffice.exe\0Executable Files (*.exe)\0*.exe\0All Files (*.*)\0*.*\0\0";
            var chosen = OpenFiles(hwnd, sofficeFilter, GetText("setting_libreoffice_browse_title"));
            if (chosen.Count == 0) return;

            string candidate = chosen[0];
            if (Path.GetFileName(candidate).Equals("soffice.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (SettingsPresentationController.Shared.TryUseLibreOfficeExecutable(candidate))
                {
                    ShowAlert(hwnd, string.Format(GetText("setting_libreoffice_validated"), Path.GetDirectoryName(candidate)));
                }
                else
                {
                    ShowAlert(hwnd, GetText("setting_libreoffice_validation_failed"));
                }
            }
            else
            {
                ShowAlert(hwnd, GetText("setting_libreoffice_invalid"));
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        static void HandleLibreOfficeDownload(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    ShowAlert(hwnd, GetText("setting_libreoffice_download_in_progress"));
                    return;
                }
            }

            bool removalPendingRestart = SettingsPresentationController.Shared.LibreOfficeRemovalPendingRestart;
            var package = SettingsPresentationController.Shared.RecommendedLibreOfficePackage;
            if (SettingsPresentationController.Shared.TryUseCurrentRecommendedLibreOffice(out string installedVersion))
            {
                ShowAlert(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_already_current"), installedVersion));
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            string prompt = string.Format(
                GetText("setting_libreoffice_download_prompt"),
                package.Version,
                package.Edition,
                FormatBytes(package.DownloadBytes),
                SettingsPresentationController.Shared.DefaultLibreOfficeInstallRoot,
                package.Sha256);

            if (!ShowConfirm(hwnd, prompt)) return;

            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadInProgress = true;
                _libreOfficeDownloadProgress = 0;
                _libreOfficeDownloadStatus = removalPendingRestart
                    ? GetText("setting_libreoffice_reinstall_starting")
                    : GetText("setting_libreoffice_download_starting");
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);

            var thread = new System.Threading.Thread(() => DownloadLibreOfficeInBackground(hwnd));
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        static void DownloadLibreOfficeInBackground(IntPtr hwnd)
        {
            try
            {
                string downloadDir = SettingsPresentationController.Shared.GetDownloadsDirectory();
                var progress = new Progress<int>(percent => ReportDownloadProgress(hwnd, percent));
                LibreOfficeInstallPresentationResult installResult = SettingsPresentationController.Shared.InstallRecommendedLibreOfficeAsync(
                        downloadDir,
                        progress,
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                string sofficePath = installResult.SofficePath;
                PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(95, GetText("setting_libreoffice_installing")));

                PostDashboardAction(hwnd, () => ShowInstallResultMessage(
                    hwnd,
                    installResult.RestartRequired,
                    sofficePath,
                    installResult.ManagementRecorded));
            }
            catch (Exception ex)
            {
                SettingsPresentationController.Shared.LibreOfficePath = string.Empty;
                PostDashboardAction(hwnd, () => ShowDownloadFailureMessage(hwnd, ex.Message));
            }
            finally
            {
                PostDashboardAction(hwnd, FinishLibreOfficeSetupStatus);
            }
        }

        private static void ReportDownloadProgress(IntPtr hwnd, int percent)
        {
            int displayPercent = Math.Min(80, Math.Max(1, percent * 80 / 100));
            PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(
                displayPercent,
                percent >= 100
                    ? GetText("setting_libreoffice_verifying")
                    : string.Format(GetText("setting_libreoffice_download_progress"), percent)));
        }

        private static void ShowInstallResultMessage(
            IntPtr hwnd,
            bool restartRequired,
            string sofficePath,
            bool managementRecorded)
        {
            ShowAlert(
                hwnd,
                string.Format(
                    GetText(restartRequired
                        ? "setting_libreoffice_install_restart_required"
                        : "setting_libreoffice_download_ready"),
                    string.IsNullOrWhiteSpace(sofficePath) ? SettingsPresentationController.Shared.DefaultLibreOfficeInstallRoot : sofficePath));
            if (!managementRecorded)
                ShowAlert(hwnd, GetText("setting_libreoffice_management_unverified"));
        }

        private static void ShowDownloadFailureMessage(IntPtr hwnd, string errorMessage)
        {
            ShowAlert(hwnd, string.Format(GetText("setting_libreoffice_download_failed"), errorMessage));
        }

        static void HandleLibreOfficeUninstall(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    ShowAlert(hwnd, GetText("setting_libreoffice_download_in_progress"));
                    return;
                }
            }

            LibreOfficePresentationState state = SettingsPresentationController.Shared.GetLibreOfficeState();
            if (state.RemovalPendingRestart)
            {
                ShowAlert(hwnd, GetText("setting_libreoffice_removal_pending"));
                return;
            }

            // Only a LibreOffice with a freshly verified Clickra management identity may be removed here.
            // The dashboard hides the button otherwise, but the action still has to refuse on its own.
            if (!state.IsManagedByClickra)
            {
                ShowAlert(hwnd, GetText("setting_libreoffice_external_note"));
                return;
            }

            if (!ShowConfirm(hwnd, GetText("setting_libreoffice_uninstall_confirm"))) return;

            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadInProgress = true;
                _libreOfficeDownloadProgress = 60;
                _libreOfficeDownloadStatus = GetText("setting_libreoffice_uninstalling");
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);

            var thread = new System.Threading.Thread(() => UninstallLibreOfficeInBackground(hwnd));
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        static void UninstallLibreOfficeInBackground(IntPtr hwnd)
        {
            try
            {
                LibreOfficeUninstallPresentationResult uninstallResult = SettingsPresentationController.Shared.UninstallManagedLibreOfficeAsync(
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                PostDashboardAction(hwnd, () => ShowAlert(
                    hwnd,
                    GetText(uninstallResult.RestartRequired
                        ? "setting_libreoffice_uninstall_restart_required"
                        : "setting_libreoffice_uninstall_ready")));
            }
            catch (Exception ex)
            {
                PostDashboardAction(hwnd, () => ShowAlert(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_uninstall_failed"), ex.Message)));
            }
            finally
            {
                PostDashboardAction(hwnd, FinishLibreOfficeSetupStatus);
            }
        }

        static void HandleLibreOfficeAdopt(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    ShowAlert(hwnd, GetText("setting_libreoffice_download_in_progress"));
                    return;
                }
            }

            if (!SettingsPresentationController.Shared.GetLibreOfficeState().CanAdopt) return;

            if (!ShowConfirm(hwnd, GetText("setting_libreoffice_adopt_confirm"))) return;

            try
            {
                if (!SettingsPresentationController.Shared.AdoptExistingLibreOffice()) return;
            }
            catch (InvalidOperationException)
            {
                ShowAlert(hwnd, GetText("setting_libreoffice_external_note"));
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
            ShowAlert(hwnd, GetText("setting_libreoffice_adopt_success"));
        }

        static string FormatBytes(long bytes)
        {
            const double mb = 1024d * 1024d;
            return $"{bytes / mb:F0} MB";
        }

        static void SetLibreOfficeSetupStatus(int progress, string status)
        {
            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadProgress = progress;
                _libreOfficeDownloadStatus = status;
            }
        }


        static void FinishLibreOfficeSetupStatus()
        {
            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadInProgress = false;
                _libreOfficeDownloadProgress = 0;
                _libreOfficeDownloadStatus = "";
            }
        }
    }
