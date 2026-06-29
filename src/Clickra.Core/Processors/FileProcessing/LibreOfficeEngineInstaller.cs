using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Clickra.Core.Processors
{
    public sealed record LibreOfficeEngineManifest(
        int Schema,
        LibreOfficeEnginePackage LibreOffice);

    public sealed record LibreOfficeEnginePackage(
        string Version,
        string Edition,
        string DownloadPageUrl,
        string DirectDownloadUrl,
        string Sha256,
        long DownloadBytes,
        string License);

    public sealed record LibreOfficeInstallResult(string SofficePath, bool RestartRequired);
    public sealed record LibreOfficeUninstallResult(bool RestartRequired);

    public static class LibreOfficeEngineInstaller
    {
        public static readonly LibreOfficeEngineManifest BuiltInManifest = new(
            Schema: 1,
            LibreOffice: new LibreOfficeEnginePackage(
                Version: "26.2.4",
                Edition: "Windows x86-64 MSI",
                DownloadPageUrl: "https://download.documentfoundation.org/libreoffice/stable/26.2.4/win/x86_64/LibreOffice_26.2.4_Win_x86-64.msi.mirrorlist",
                DirectDownloadUrl: "https://download.documentfoundation.org/libreoffice/stable/26.2.4/win/x86_64/LibreOffice_26.2.4_Win_x86-64.msi",
                Sha256: "202f26cda071c5aa4996a5a28412fddceb3891dceb0366982c62650456c0730f",
                DownloadBytes: 372539392L,
                License: "MPL-2.0"));

        public static LibreOfficeEnginePackage RecommendedPackage => BuiltInManifest.LibreOffice;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        public static string GetDefaultInstallRoot()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return !string.IsNullOrWhiteSpace(programFiles)
                ? Path.Combine(programFiles, "LibreOffice")
                : "C:\\Program Files\\LibreOffice";
        }

        public static bool IsAsciiPath(string path)
        {
            foreach (char c in path)
            {
                if (c > 0x7F)
                    return false;
            }
            return true;
        }

        public static string ResolvePortableSofficePath(string installRoot)
        {
            string[] candidates =
            {
                Path.Combine(installRoot, "LibreOfficePortable", "App", "libreoffice", "program", "soffice.exe"),
                Path.Combine(installRoot, "App", "libreoffice", "program", "soffice.exe"),
                Path.Combine(installRoot, "program", "soffice.exe"),
                Path.Combine(installRoot, "soffice.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        public static string ResolveSystemSofficePath()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates =
            {
                Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe"),
                Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        public static string GetInstalledSystemVersion()
        {
            string sofficePath = ResolveSystemSofficePath();
            if (string.IsNullOrWhiteSpace(sofficePath))
                return "";

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(sofficePath);
                return info.ProductVersion ?? info.FileVersion ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static bool IsRecommendedVersionInstalled()
        {
            string installedVersion = GetInstalledSystemVersion();
            return !string.IsNullOrWhiteSpace(installedVersion) &&
                   installedVersion.StartsWith(RecommendedPackage.Version, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<LibreOfficeInstallResult> InstallMsiPackageAsync(
            string installerPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                throw new FileNotFoundException("LibreOffice installer was not found.", installerPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{installerPath}\" /quiet /norestart",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the LibreOffice installer.");

            await process.WaitForExitAsync(cancellationToken);

            bool restartRequired = process.ExitCode is 3010 or 1641;
            if (restartRequired)
            {
                string restartSofficePath = await WaitForSystemInstallReadyAsync(TimeSpan.FromSeconds(20), cancellationToken);
                if (string.IsNullOrWhiteSpace(restartSofficePath))
                    restartSofficePath = ResolveSystemSofficePath();

                return new LibreOfficeInstallResult(restartSofficePath, RestartRequired: true);
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"LibreOffice installer exited with code {process.ExitCode}.");

            string sofficePath = await WaitForSystemInstallReadyAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (string.IsNullOrWhiteSpace(sofficePath))
                throw new InvalidOperationException("LibreOffice installed, but the program files were not ready.");

            return new LibreOfficeInstallResult(sofficePath, RestartRequired: false);
        }

        public static async Task<LibreOfficeUninstallResult> UninstallSystemLibreOfficeAsync(CancellationToken cancellationToken)
        {
            string productCode = FindInstalledLibreOfficeProductCode()
                ?? throw new InvalidOperationException("LibreOffice MSI installation was not found.");

            var startInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/x {productCode} /quiet /norestart",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the LibreOffice uninstaller.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("LibreOffice uninstaller did not finish within 10 minutes.");
            }

            if (process.ExitCode is 3010 or 1641)
                return new LibreOfficeUninstallResult(RestartRequired: true);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"LibreOffice uninstaller exited with code {process.ExitCode}.");

            await WaitForSystemUninstallAsync(cancellationToken);
            return new LibreOfficeUninstallResult(RestartRequired: false);
        }

        private static string? FindInstalledLibreOfficeProductCode()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey? uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallKey == null)
                        continue;

                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);
                        if (appKey == null)
                            continue;

                        string displayName = Convert.ToString(appKey.GetValue("DisplayName")) ?? "";
                        if (!displayName.StartsWith("LibreOffice", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (LooksLikeProductCode(subKeyName))
                            return subKeyName;

                        string uninstallString = Convert.ToString(appKey.GetValue("UninstallString")) ?? "";
                        Match match = Regex.Match(uninstallString, @"\{[0-9A-Fa-f\-]{36}\}");
                        if (match.Success)
                            return match.Value;
                    }
                }
                catch
                {
                    // Some registry views may be unavailable under reduced permissions.
                }
            }

            return null;
        }

        private static bool LooksLikeProductCode(string value)
        {
            return Regex.IsMatch(value, @"^\{[0-9A-Fa-f\-]{36}\}$");
        }

        private static async Task WaitForSystemUninstallAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(ResolveSystemSofficePath()))
                    return;
                await Task.Delay(1000, cancellationToken);
            }
        }

        private static async Task<string> WaitForSystemInstallReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string sofficePath = ResolveSystemSofficePath();
                if (!string.IsNullOrWhiteSpace(sofficePath) && HasRequiredSystemFiles(sofficePath))
                {
                    await Task.Delay(1500, cancellationToken);
                    if (HasRequiredSystemFiles(sofficePath))
                        return sofficePath;
                }

                await Task.Delay(1000, cancellationToken);
            }

            return "";
        }

        private static bool HasRequiredSystemFiles(string sofficePath)
        {
            string? programDir = Path.GetDirectoryName(sofficePath);
            if (string.IsNullOrWhiteSpace(programDir))
                return false;

            string[] requiredFiles =
            {
                "soffice.exe",
                "soffice.bin",
                "mergedlo.dll",
                "sal3.dll",
                "cppu3.dll",
                "cppuhelper3MSC.dll"
            };

            foreach (string fileName in requiredFiles)
            {
                string path = Path.Combine(programDir, fileName);
                if (!File.Exists(path))
                    return false;
                try
                {
                    if (new FileInfo(path).Length <= 0)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        public static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static bool VerifySha256(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
                return false;
            return string.Equals(ComputeSha256(filePath), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static string GetExtractedPortableRoot(string downloadDirectory)
        {
            return Path.Combine(downloadDirectory, "LibreOfficePortable");
        }

        public static async Task<string> AdoptExtractedPackageAsync(
            string extractedPortableRoot,
            string installRoot,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(extractedPortableRoot) || !Directory.Exists(extractedPortableRoot))
                throw new DirectoryNotFoundException("LibreOffice Portable extracted folder was not found.");

            string sourceSofficePath = ResolvePortableSofficePath(extractedPortableRoot);
            if (string.IsNullOrWhiteSpace(sourceSofficePath))
                throw new InvalidOperationException("LibreOffice Portable extracted folder is incomplete.");

            string targetRoot = Path.Combine(installRoot, "LibreOfficePortable");
            Directory.CreateDirectory(installRoot);
            await CopyDirectoryAsync(extractedPortableRoot, targetRoot, progress, cancellationToken);

            string sofficePath = ResolvePortableSofficePath(targetRoot);
            if (string.IsNullOrWhiteSpace(sofficePath))
                throw new InvalidOperationException("LibreOffice Portable was copied, but soffice.exe could not be found.");

            return sofficePath;
        }

        private static async Task CopyDirectoryAsync(
            string sourceDirectory,
            string targetDirectory,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            string[] sourceFiles = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToArray();
            long totalBytes = 0;
            foreach (string sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalBytes += new FileInfo(sourceFile).Length;
            }

            long copiedBytes = 0;
            Directory.CreateDirectory(targetDirectory);
            progress?.Report(0);

            foreach (string sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                string targetFile = Path.Combine(targetDirectory, relativePath);
                string? targetParent = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetParent))
                    Directory.CreateDirectory(targetParent);

                await using var source = File.OpenRead(sourceFile);
                await using var target = File.Create(targetFile);
                await source.CopyToAsync(target, cancellationToken);

                copiedBytes += source.Length;
                if (totalBytes > 0)
                {
                    int percent = (int)Math.Min(99, Math.Max(1, copiedBytes * 100 / totalBytes));
                    progress?.Report(percent);
                }
            }

            progress?.Report(100);
        }

        public static async Task<string> DownloadAndVerifyAsync(
            LibreOfficeEnginePackage package,
            string downloadDirectory,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(downloadDirectory);
            string fileName = Path.GetFileName(new Uri(package.DirectDownloadUrl).LocalPath);
            string targetPath = Path.Combine(downloadDirectory, fileName);
            string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            if (File.Exists(targetPath) && VerifySha256(targetPath, package.Sha256))
            {
                progress?.Report(100);
                return targetPath;
            }

            using var response = await HttpClient.GetAsync(package.DirectDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(tempPath))
            {
                byte[] buffer = new byte[1024 * 128];
                long totalRead = 0;
                long expectedBytes = response.Content.Headers.ContentLength ?? package.DownloadBytes;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                    if (expectedBytes > 0)
                    {
                        int percent = (int)Math.Min(99, Math.Max(1, totalRead * 100 / expectedBytes));
                        progress?.Report(percent);
                    }
                }
            }

            if (!VerifySha256(tempPath, package.Sha256))
            {
                try { File.Delete(tempPath); } catch { }
                throw new InvalidOperationException("Downloaded LibreOffice package failed SHA256 verification.");
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(tempPath, targetPath);
            progress?.Report(100);
            return targetPath;
        }
    }
}
