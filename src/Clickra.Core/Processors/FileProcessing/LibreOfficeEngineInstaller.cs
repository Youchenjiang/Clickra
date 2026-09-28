using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
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
    internal sealed record LibreOfficeRegistryProduct(
        string ProductCode,
        string DisplayName,
        string InstallLocation);

    public static class LibreOfficeEngineInstaller
    {
        public static readonly LibreOfficeEngineManifest BuiltInManifest = new(
            Schema: 1,
            LibreOffice: new LibreOfficeEnginePackage(
                Version: "26.2.6",
                Edition: "Windows x86-64 MSI",
                DownloadPageUrl: "https://download.documentfoundation.org/libreoffice/stable/26.2.6/win/x86_64/LibreOffice_26.2.6_Win_x86-64.msi.mirrorlist",
                DirectDownloadUrl: "https://download.documentfoundation.org/libreoffice/stable/26.2.6/win/x86_64/LibreOffice_26.2.6_Win_x86-64.msi",
                Sha256: "f9877032fd908beb9c0ddf06df4af5c2e85f419c42e14876c4cce5aae5fb2660",
                DownloadBytes: 373_252_096, // skipcq: CS-R1005
                License: "MPL-2.0"));

        public static LibreOfficeEnginePackage RecommendedPackage => BuiltInManifest.LibreOffice;

        /// <summary>
        /// True only when the currently resolved LibreOffice is the same system installation whose
        /// MSI identity was explicitly recorded for Clickra management.
        /// </summary>
        public static bool WasInstalledByClickra()
        {
            return TryGetVerifiedManagedProductCode(out _);
        }

        /// <summary>
        /// Records or releases management of the current system LibreOffice. The positive path fails
        /// closed unless one unambiguous LibreOffice MSI identity can be bound to the system executable.
        /// </summary>
        public static void MarkInstalledByClickra(bool installed)
        {
            if (!installed)
            {
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeInstalledByClickra, ClickraSettings.ValueFalse);
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeManagedProductCode, ClickraSettings.DefaultEmpty);
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeManagedSofficePath, ClickraSettings.DefaultEmpty);
                return;
            }

            // Clear any older authorization before resolving a new identity. If discovery is ambiguous
            // or fails, the destructive path remains disabled instead of retaining stale consent.
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeInstalledByClickra, ClickraSettings.ValueFalse);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeManagedProductCode, ClickraSettings.DefaultEmpty);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeManagedSofficePath, ClickraSettings.DefaultEmpty);

            string systemPath = ResolveSystemSofficePath();
            string? productCode = FindUniqueInstalledLibreOfficeProductCode();
            if (string.IsNullOrWhiteSpace(systemPath) || string.IsNullOrWhiteSpace(productCode))
                throw new InvalidOperationException(
                    "Unable to bind Clickra management to one verified system LibreOffice MSI installation.");

            RecordManagedInstallation(productCode, systemPath);
        }

        /// <summary>
        /// Records management after a Clickra-driven MSI install/update only when the resulting executable
        /// and registry identity can be tied to one exact system installation. Failure leaves it unmanaged.
        /// </summary>
        public static bool TryRecordManagedSystemInstallation(string? sofficePath)
        {
            ReleaseManagement();

            string systemPath = ResolveSystemSofficePath();
            string candidatePath = string.IsNullOrWhiteSpace(sofficePath) ? systemPath : sofficePath;
            if (!PathsReferToSameInstallation(candidatePath, systemPath))
                return false;

            string? productCode = FindUniqueInstalledLibreOfficeProductCode();
            if (string.IsNullOrWhiteSpace(productCode))
                return false;

            RecordManagedInstallation(productCode, systemPath);
            return true;
        }

        /// <summary>
        /// Explicitly adopts an existing system LibreOffice installation into Clickra's management,
        /// allowing it to be updated or uninstalled by Clickra.
        /// </summary>
        public static void AdoptExistingInstallation()
        {
            string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
            if (!CanAdoptExistingInstallation(resolvedPath))
                throw new InvalidOperationException(
                    "Only one verified system LibreOffice MSI installation can be adopted by Clickra.");

            MarkInstalledByClickra(true);
        }

        /// <summary>
        /// Releases Clickra's management over the system LibreOffice installation without removing files.
        /// </summary>
        public static void ReleaseManagement() => MarkInstalledByClickra(false);

        /// <summary>
        /// Adoption is offered only for the system LibreOffice that the UI is currently showing, and
        /// only when its MSI identity is unambiguous. Portable/custom/PATH installations stay external.
        /// </summary>
        public static bool CanAdoptExistingInstallation(string? resolvedPath = null)
        {
            resolvedPath ??= LibreOfficeHelper.GetResolvedExecutablePath();
            string systemPath = ResolveSystemSofficePath();
            if (!PathsReferToSameInstallation(resolvedPath, systemPath))
                return false;

            return FindUniqueInstalledLibreOfficeProductCode() is not null;
        }

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
            return ResolveSystemSofficePaths().FirstOrDefault() ?? "";
        }

        private static IReadOnlyList<string> ResolveSystemSofficePaths()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates =
            {
                Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe"),
                Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe")
            };

            var resolved = new List<string>();
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate) &&
                    resolved.All(existing => !PathsReferToSameInstallation(existing, candidate)))
                {
                    resolved.Add(candidate);
                }
            }

            return resolved;
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

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                throw;
            }

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
            // Single choke point for the invariant: Clickra only ever removes a LibreOffice it installed
            // itself. The registry lookup below matches any LibreOffice MSI, including one the user
            // installed for their own work, so this check has to run before any uninstaller work.
            if (!TryGetVerifiedManagedProductCode(out string productCode))
                throw new InvalidOperationException(
                    "Refusing to uninstall: this LibreOffice was not installed by Clickra, so it is managed by the user.");

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                throw new TimeoutException("LibreOffice uninstaller did not finish within 10 minutes.");
            }

            if (process.ExitCode is 3010 or 1641)
                return new LibreOfficeUninstallResult(RestartRequired: true);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"LibreOffice uninstaller exited with code {process.ExitCode}.");

            await WaitForSystemUninstallAsync(cancellationToken);
            return new LibreOfficeUninstallResult(RestartRequired: false);
        }

        private static string? FindUniqueInstalledLibreOfficeProductCode()
        {
            return SelectUniqueProductCode(
                EnumerateInstalledLibreOfficeProducts(),
                ResolveSystemSofficePaths());
        }

        private static bool TryGetVerifiedManagedProductCode(out string productCode)
        {
            bool consent = ClickraStorage.GetSettingBool(ClickraSettings.LibreOfficeInstalledByClickra);
            productCode = ClickraStorage.GetSetting(ClickraSettings.LibreOfficeManagedProductCode);
            string managedPath = ClickraStorage.GetSetting(ClickraSettings.LibreOfficeManagedSofficePath);
            string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
            string? uniqueCurrentProductCode = FindUniqueInstalledLibreOfficeProductCode();

            return IsManagedIdentityCurrent(
                consent,
                productCode,
                managedPath,
                resolvedPath,
                uniqueCurrentProductCode);
        }

        private static IEnumerable<LibreOfficeRegistryProduct> EnumerateInstalledLibreOfficeProducts()
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (LibreOfficeRegistryProduct product in ReadLibreOfficeProducts(hive, view))
                {
                    yield return product;
                }
            }
        }

        private static IReadOnlyList<LibreOfficeRegistryProduct> ReadLibreOfficeProducts(
            RegistryHive hive,
            RegistryView view)
        {
            var entries = new List<LibreOfficeRegistryProduct>();
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey == null)
                    return entries;

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);
                    if (appKey == null)
                        continue;

                    string displayName = Convert.ToString(appKey.GetValue("DisplayName")) ?? "";
                    if (!displayName.StartsWith("LibreOffice", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? productCode = GetProductCode(subKeyName);
                    if (productCode is not null)
                    {
                        string installLocation = Convert.ToString(appKey.GetValue("InstallLocation")) ?? "";
                        entries.Add(new LibreOfficeRegistryProduct(productCode, displayName, installLocation));
                    }
                }
            }
            catch (SecurityException)
            {
                // Some registry views may be unavailable under reduced permissions.
            }
            catch (UnauthorizedAccessException)
            {
                // Some registry views may be unavailable under reduced permissions.
            }
            catch (IOException)
            {
                // Some registry views may be unavailable under reduced permissions.
            }

            return entries;
        }

        private static string? GetProductCode(string subKeyName)
        {
            if (LooksLikeProductCode(subKeyName))
                return subKeyName.ToUpperInvariant();

            return null;
        }

        internal static bool LooksLikeProductCode(string value)
        {
            return Regex.IsMatch(value, @"^\{[0-9A-Fa-f\-]{36}\}$");
        }

        internal static bool PathsReferToSameInstallation(string? resolvedPath, string? systemPath)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath) || string.IsNullOrWhiteSpace(systemPath))
                return false;

            try
            {
                string resolvedFullPath = Path.GetFullPath(resolvedPath);
                string systemFullPath = Path.GetFullPath(systemPath);
                return resolvedFullPath.Equals(systemFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
            catch (SecurityException)
            {
                return false;
            }
        }

        internal static bool IsManagedIdentityCurrent(
            bool consent,
            string? storedProductCode,
            string? storedSofficePath,
            string? currentResolvedPath,
            string? uniqueCurrentProductCode)
        {
            return consent &&
                   !string.IsNullOrWhiteSpace(storedProductCode) &&
                   LooksLikeProductCode(storedProductCode) &&
                   !string.IsNullOrWhiteSpace(storedSofficePath) &&
                   PathsReferToSameInstallation(currentResolvedPath, storedSofficePath) &&
                   !string.IsNullOrWhiteSpace(uniqueCurrentProductCode) &&
                   storedProductCode.Equals(uniqueCurrentProductCode, StringComparison.OrdinalIgnoreCase);
        }

        internal static string? SelectUniqueProductCode(
            IEnumerable<LibreOfficeRegistryProduct> entries,
            IEnumerable<string> systemExecutablePaths)
        {
            string? systemExecutablePath = SelectUniqueSystemExecutablePath(systemExecutablePaths);
            if (systemExecutablePath is null)
                return null;

            string? selected = null;
            foreach (LibreOfficeRegistryProduct entry in entries)
            {
                if (!entry.DisplayName.StartsWith("LibreOffice", StringComparison.OrdinalIgnoreCase) ||
                    !LooksLikeProductCode(entry.ProductCode) ||
                    !RegistryProductMatchesSystemExecutable(entry, systemExecutablePath))
                {
                    continue;
                }

                string normalized = entry.ProductCode.ToUpperInvariant();
                if (selected is null)
                {
                    selected = normalized;
                }
                else if (!selected.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return selected;
        }

        internal static string? SelectUniqueSystemExecutablePath(IEnumerable<string> systemExecutablePaths)
        {
            string? selected = null;
            foreach (string candidatePath in systemExecutablePaths)
            {
                if (string.IsNullOrWhiteSpace(candidatePath))
                    continue;

                if (selected is null)
                {
                    selected = candidatePath;
                }
                else if (!PathsReferToSameInstallation(selected, candidatePath))
                {
                    return null;
                }
            }

            return selected;
        }

        internal static bool RegistryProductMatchesSystemExecutable(
            LibreOfficeRegistryProduct product,
            string? systemExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(product.InstallLocation) ||
                string.IsNullOrWhiteSpace(systemExecutablePath))
            {
                return false;
            }

            string installRoot = product.InstallLocation.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return PathsReferToSameInstallation(
                       Path.Combine(installRoot, "program", "soffice.exe"),
                       systemExecutablePath) ||
                   PathsReferToSameInstallation(
                       Path.Combine(installRoot, "soffice.exe"),
                       systemExecutablePath);
        }

        private static void RecordManagedInstallation(string productCode, string sofficePath)
        {
            string canonicalPath = Path.GetFullPath(sofficePath);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeManagedProductCode, productCode.ToUpperInvariant());
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeManagedSofficePath, canonicalPath);
            ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeInstalledByClickra, ClickraSettings.ValueTrue);
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

            try
            {
                HttpResponseMessage response;
                try
                {
                    response = await HttpClient.GetAsync(package.DirectDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException ex) when (IsNetworkNameResolutionFailure(ex))
                {
                    throw new InvalidOperationException(
                        "Unable to resolve the LibreOffice download server. Check the network, DNS, proxy, or virtual machine internet settings, then try again.",
                        ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException(
                        $"Unable to connect to the LibreOffice download server. Check the network, proxy, or firewall settings, then try again. Details: {ex.Message}",
                        ex);
                }

                using (response)
                {
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
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static bool IsNetworkNameResolutionFailure(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException &&
                    socketException.SocketErrorCode == SocketError.HostNotFound)
                    return true;
            }

            return false;
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }
}
