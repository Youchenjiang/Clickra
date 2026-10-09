using System;
using System.IO;
using Clickra.Core;
using Clickra.Core.Processors;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
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
                if (LibreOfficeHelper.LooksLikeLibreOfficeExecutable(candidate))
                {
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, candidate);
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);
                    MessageBox(hwnd, string.Format(GetText("setting_libreoffice_validated"), Path.GetDirectoryName(candidate)), AppTitle, 0x40);
                }
                else
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_validation_failed"), AppTitle, 0x30);
                }
            }
            else
            {
                MessageBox(hwnd, GetText("setting_libreoffice_invalid"), AppTitle, 0x30);
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        static void HandleLibreOfficeDownload(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), AppTitle, 0x40);
                    return;
                }
            }

            bool removalPendingRestart = ClickraStorage.GetSetting(ClickraSettings.LibreOfficeRemovalPendingRestart).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase);
            var package = LibreOfficeEngineInstaller.RecommendedPackage;
            string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();
            if (!removalPendingRestart &&
                !string.IsNullOrWhiteSpace(installedVersion) &&
                LibreOfficeEngineInstaller.IsRecommendedVersionInstalled())
            {
                string resolvedPath = LibreOfficeEngineInstaller.ResolveSystemSofficePath();
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, resolvedPath);
                }

                MessageBox(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_already_current"), installedVersion),
                    AppTitle,
                    0x40);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            string prompt = string.Format(
                GetText("setting_libreoffice_download_prompt"),
                package.Version,
                package.Edition,
                FormatBytes(package.DownloadBytes),
                LibreOfficeEngineInstaller.GetDefaultInstallRoot(),
                package.Sha256);

            if (MessageBox(hwnd, prompt, AppTitle, 0x41) != 1) return;

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
            var package = LibreOfficeEngineInstaller.RecommendedPackage;
            try
            {
                string downloadDir = Path.Combine(ClickraStorage.GetDataDir(), "downloads");
                var progress = new Progress<int>(percent => ReportDownloadProgress(hwnd, percent));
                string installerPath = LibreOfficeEngineInstaller.DownloadAndVerifyAsync(
                        package,
                        downloadDir,
                        progress,
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(85, GetText("setting_libreoffice_installing")));

                LibreOfficeInstallResult installResult = LibreOfficeEngineInstaller.InstallMsiPackageAsync(
                        installerPath,
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                string sofficePath = installResult.SofficePath;
                if (!installResult.RestartRequired && !LibreOfficeHelper.LooksLikeLibreOfficeExecutable(sofficePath))
                    throw new InvalidOperationException(GetText("setting_libreoffice_validation_failed"));

                PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(95, GetText("setting_libreoffice_installing")));

                if (!string.IsNullOrWhiteSpace(sofficePath))
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, sofficePath);
                bool managementRecorded = LibreOfficeEngineInstaller.TryRecordManagedSystemInstallation(sofficePath);
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);

                PostDashboardAction(hwnd, () => ShowInstallResultMessage(
                    hwnd,
                    installResult.RestartRequired,
                    sofficePath,
                    managementRecorded));
            }
            catch (Exception ex)
            {
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty);
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
            MessageBox(
                hwnd,
                string.Format(
                    GetText(restartRequired
                        ? "setting_libreoffice_install_restart_required"
                        : "setting_libreoffice_download_ready"),
                    string.IsNullOrWhiteSpace(sofficePath) ? LibreOfficeEngineInstaller.GetDefaultInstallRoot() : sofficePath),
                AppTitle,
                0x40);
            if (!managementRecorded)
                MessageBox(hwnd, GetText("setting_libreoffice_management_unverified"), AppTitle, 0x30);
        }

        private static void ShowDownloadFailureMessage(IntPtr hwnd, string errorMessage)
        {
            MessageBox(hwnd, string.Format(GetText("setting_libreoffice_download_failed"), errorMessage), AppTitle, 0x10);
        }

        static void HandleLibreOfficeUninstall(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), AppTitle, 0x40);
                    return;
                }
            }

            if (ClickraStorage.GetSetting(ClickraSettings.LibreOfficeRemovalPendingRestart).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox(hwnd, GetText("setting_libreoffice_removal_pending"), AppTitle, 0x40);
                return;
            }

            // Only a LibreOffice with a freshly verified Clickra management identity may be removed here.
            // The dashboard hides the button otherwise, but the action still has to refuse on its own.
            if (!LibreOfficeEngineInstaller.WasInstalledByClickra())
            {
                MessageBox(hwnd, GetText("setting_libreoffice_external_note"), AppTitle, 0x40);
                return;
            }

            if (MessageBox(hwnd, GetText("setting_libreoffice_uninstall_confirm"), AppTitle, 0x31) != 1) return;

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
                LibreOfficeUninstallResult uninstallResult = LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync(
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty);
                LibreOfficeEngineInstaller.ReleaseManagement();
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, uninstallResult.RestartRequired ? ClickraSettings.ValueTrue : ClickraSettings.ValueFalse);
                ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.DefaultOfficeEngineAuto);

                PostDashboardAction(hwnd, () => MessageBox(
                    hwnd,
                    GetText(uninstallResult.RestartRequired
                        ? "setting_libreoffice_uninstall_restart_required"
                        : "setting_libreoffice_uninstall_ready"),
                    AppTitle,
                    0x40));
            }
            catch (Exception ex)
            {
                PostDashboardAction(hwnd, () => MessageBox(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_uninstall_failed"), ex.Message),
                    AppTitle,
                    0x10));
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
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), AppTitle, 0x40);
                    return;
                }
            }

            if (LibreOfficeEngineInstaller.WasInstalledByClickra())
            {
                return;
            }

            string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
            if (!LibreOfficeEngineInstaller.CanAdoptExistingInstallation(resolvedPath))
            {
                return;
            }

            if (MessageBox(hwnd, GetText("setting_libreoffice_adopt_confirm"), AppTitle, 0x31) != 1) return;

            try
            {
                LibreOfficeEngineInstaller.AdoptExistingInstallation();
            }
            catch (InvalidOperationException)
            {
                MessageBox(hwnd, GetText("setting_libreoffice_external_note"), AppTitle, 0x30);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
            MessageBox(hwnd, GetText("setting_libreoffice_adopt_success"), AppTitle, 0x40);
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
}
