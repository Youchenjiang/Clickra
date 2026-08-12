using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Clickra.Core;

namespace Clickra.Core.Processors
{
    public static class PowerShellHelper
    {
        private const string LanguageSettingKey = "Language";

        public static void ExportOfficeToPdf(
            string appType,
            string fullPath,
            string outputPdfPath,
            int fileIndex,
            int totalFiles,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            string engine = ClickraStorage.GetSetting("OfficeEngine");
            if (engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase))
            {
                LibreOfficeHelper.ExportToPdf(appType, fullPath, outputPdfPath, fileIndex, totalFiles, onProgress, cancellationToken);
                return;
            }

            if (!engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
                LibreOfficeHelper.CanConvert(appType) &&
                !IsMicrosoftOfficeReady(appType))
            {
                if (string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath()))
                    throw new Exception(Localization.T("error_libreoffice_not_ready", ClickraStorage.GetSetting(LanguageSettingKey)));

                LibreOfficeHelper.ExportToPdf(appType, fullPath, outputPdfPath, fileIndex, totalFiles, onProgress, cancellationToken);
                return;
            }

            try
            {
                ExportMicrosoftOfficeToPdf(appType, fullPath, outputPdfPath, fileIndex, totalFiles, onProgress, cancellationToken);
                return;
            }
            catch (Exception) when (!engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase) && LibreOfficeHelper.CanConvert(appType))
            {
                if (string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath()))
                    throw;

                string language = ClickraStorage.GetSetting(LanguageSettingKey);
                onProgress?.Invoke(
                    fileIndex * 100,
                    totalFiles * 100,
                    string.Format(
                        Localization.T("status_office_fallback_to_libreoffice", language),
                        appType,
                        Path.GetFileName(fullPath)));
                LibreOfficeHelper.ExportToPdf(appType, fullPath, outputPdfPath, fileIndex, totalFiles, onProgress, cancellationToken);
            }
        }

        private static bool IsMicrosoftOfficeReady(string appType)
        {
            try
            {
                Type? type = appType switch
                {
                    "Word" => Type.GetTypeFromProgID("Word.Application"),
                    "Excel" => Type.GetTypeFromProgID("Excel.Application"),
                    "PowerPoint" => Type.GetTypeFromProgID("PowerPoint.Application"),
                    _ => null
                };

                return type != null;
            }
            catch
            {
                return false;
            }
        }

        private static void ExportMicrosoftOfficeToPdf(
            string appType,
            string fullPath,
            string outputPdfPath,
            int fileIndex,
            int totalFiles,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            string psScript = "";
            if (appType == "Word")
            {
                psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $word = New-Object -ComObject Word.Application
    try {{
        Write-Host 'PROGRESS:50'
        $doc = $word.Documents.Open('{fullPath.Replace("'", "''")}', $false, $true)
        Write-Host 'PROGRESS:80'
        $doc.ExportAsFixedFormat('{outputPdfPath.Replace("'", "''")}', 17)
        $doc.Close($false)
        Write-Host 'PROGRESS:100'
    }} finally {{
        $word.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    }}
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}";
            }
            else if (appType == "PowerPoint")
            {
                psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $ppt = New-Object -ComObject PowerPoint.Application
    try {{
        Write-Host 'PROGRESS:50'
        $pres = $ppt.Presentations.Open('{fullPath.Replace("'", "''")}', $true, $false, $false)
        Write-Host 'PROGRESS:80'
        $pres.SaveAs('{outputPdfPath.Replace("'", "''")}', 32)
        $pres.Close()
        Write-Host 'PROGRESS:100'
    }} finally {{
        $ppt.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($ppt) | Out-Null
    }}
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}";
            }
            else if (appType == "Excel")
            {
                psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    try {{
        Write-Host 'PROGRESS:50'
        $wb = $excel.Workbooks.Open('{fullPath.Replace("'", "''")}')
        try {{
            Write-Host 'PROGRESS:80'
            # xlTypePDF = 0
            $wb.ExportAsFixedFormat(0, '{outputPdfPath.Replace("'", "''")}')
        }} finally {{
            $wb.Close($false)
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wb) | Out-Null
        }}
        Write-Host 'PROGRESS:100'
    }} finally {{
        $excel.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    }}
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}";
            }
            else
            {
                throw new NotSupportedException(string.Format(Localization.T("error_office_unsupported", ClickraStorage.GetSetting(LanguageSettingKey)), appType));
            }

            RunOfficeInteropScript(psScript, fileIndex, totalFiles, fullPath, appType, onProgress, cancellationToken);
            
            if (!File.Exists(outputPdfPath))
            {
                throw new Exception(string.Format(Localization.T("error_office_output_missing", ClickraStorage.GetSetting("Language")), appType));
            }
        }

        public static void RunOfficeInteropScript(
            string psScript, 
            int fileIndex, 
            int totalFiles, 
            string filePath, 
            string appName, 
            Action<int, int, string>? onProgress, 
            CancellationToken cancellationToken)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException(string.Format(Localization.T("error_office_powershell_start", ClickraStorage.GetSetting(LanguageSettingKey)), appName));

            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            var error = new StringBuilder();
            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data) || !e.Data.StartsWith("PROGRESS:") ||
                    !int.TryParse(e.Data.Substring(9), out int subProg))
                {
                    return;
                }

                string language = ClickraStorage.GetSetting(LanguageSettingKey);
                string fileName = Path.GetFileName(filePath);
                int currentProgress = (fileIndex * 100) + subProg;
                string statusMsg = subProg switch
                {
                    20 => string.Format(Localization.T("status_office_starting", language), appName, fileIndex + 1, totalFiles),
                    50 => string.Format(Localization.T("status_office_reading", language), fileName),
                    80 => string.Format(Localization.T("status_office_exporting", language), fileName),
                    100 => string.Format(Localization.T("status_office_completed", language), fileName),
                    _ => string.Format(Localization.T("status_office_converting", language), appName, fileName)
                };
                onProgress?.Invoke(currentProgress, totalFiles * 100, statusMsg);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    error.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                try { process.Kill(true); } catch { /* Ignored: a hung process must not mask the timeout error. */ }
                throw new TimeoutException(string.Format(Localization.T("error_office_timeout", ClickraStorage.GetSetting(LanguageSettingKey)), appName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            string errorText = error.ToString();
            if (!string.IsNullOrWhiteSpace(errorText) && process.ExitCode != 0)
            {
                if (errorText.Contains("0x80040154") || errorText.Contains("New-Object"))
                    throw new InvalidOperationException(string.Format(Localization.T("error_office_not_installed", ClickraStorage.GetSetting(LanguageSettingKey)), appName));
                else
                    throw new InvalidOperationException(string.Format(Localization.T("error_office_failed", ClickraStorage.GetSetting(LanguageSettingKey)), appName, errorText.Trim()));
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.Format(Localization.T("error_office_exit_code", ClickraStorage.GetSetting(LanguageSettingKey)), appName, process.ExitCode));
            }
        }
    }

    public static class LibreOfficeHelper
    {
        private const string LanguageSettingKey = "Language";
        private const uint SemFailCriticalErrors = 0x0001;
        private const uint SemNoGpFaultErrorBox = 0x0002;
        private const uint SemNoOpenFileErrorBox = 0x8000;
        private static readonly object ResolveCacheLock = new();
        private static DateTime _resolvedExecutableCachedAt = DateTime.MinValue;
        private static string _resolvedExecutableCacheKey = "";
        private static string _resolvedExecutableCacheValue = "";

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint uMode);

        public static bool CanConvert(string appType) =>
            appType.Equals("Word", StringComparison.OrdinalIgnoreCase) ||
            appType.Equals("PowerPoint", StringComparison.OrdinalIgnoreCase) ||
            appType.Equals("Excel", StringComparison.OrdinalIgnoreCase);

        public static bool TryResolveExecutable(string? configuredPath, out string executablePath)
        {
            foreach (string candidate in EnumerateExecutableCandidates(configuredPath))
            {
                if (File.Exists(candidate))
                {
                    executablePath = candidate;
                    return true;
                }
            }

            executablePath = "";
            return false;
        }

        public static string GetResolvedExecutablePath()
        {
            string removalPending = ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart");
            string configuredPath = ClickraStorage.GetSetting("LibreOfficePath");
            string envPath = Environment.GetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH") ?? "";
            string cacheKey = $"{removalPending}|{configuredPath}|{envPath}";

            lock (ResolveCacheLock)
            {
                if (cacheKey == _resolvedExecutableCacheKey &&
                    DateTime.UtcNow - _resolvedExecutableCachedAt < TimeSpan.FromSeconds(2))
                {
                    return _resolvedExecutableCacheValue;
                }
            }

            string resolved = ResolveExecutablePathUncached(removalPending, configuredPath);

            lock (ResolveCacheLock)
            {
                _resolvedExecutableCacheKey = cacheKey;
                _resolvedExecutableCacheValue = resolved;
                _resolvedExecutableCachedAt = DateTime.UtcNow;
            }

            return resolved;
        }

        private static string ResolveExecutablePathUncached(string removalPending, string configuredPath)
        {
            if (removalPending.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "";

            if (!TryResolveExecutable(configuredPath, out string executablePath))
                return "";

            return executablePath;
        }

        public static bool TryGetExecutableVersion(string executablePath, out string version)
        {
            version = "";
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return false;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--headless");
                startInfo.ArgumentList.Add("--version");

                uint previousErrorMode = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
                using var process = StartProcessAndRestoreErrorMode(startInfo, previousErrorMode);
                if (process == null)
                    return false;

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(true); } catch { }
                    return false;
                }

                if (process.ExitCode != 0)
                    return false;

                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                version = (output.Length > 0 ? output : error).Trim();
                return version.Contains("LibreOffice", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void ExportToPdf(
            string appType,
            string fullPath,
            string outputPdfPath,
            int fileIndex,
            int totalFiles,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            if (!CanConvert(appType))
                throw new NotSupportedException(string.Format(Localization.T("error_libreoffice_unsupported", ClickraStorage.GetSetting(LanguageSettingKey)), appType));

            if (ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(Localization.T("error_libreoffice_not_ready", ClickraStorage.GetSetting(LanguageSettingKey)));
            }

            if (!TryResolveExecutable(ClickraStorage.GetSetting("LibreOfficePath"), out string executablePath))
            {
                throw new Exception(Localization.T("error_libreoffice_not_ready", ClickraStorage.GetSetting(LanguageSettingKey)));
            }

            if (!LooksLikeLibreOfficeExecutable(executablePath))
            {
                throw new Exception(Localization.T("error_libreoffice_unusable", ClickraStorage.GetSetting(LanguageSettingKey)));
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "ClickraLibreOffice", Guid.NewGuid().ToString("N"));
            string profileDir = Path.Combine(Path.GetTempPath(), "ClickraLibreOfficeProfile", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(profileDir);

            try
            {
                onProgress?.Invoke(
                    fileIndex * 100 + 20,
                    totalFiles * 100,
                    string.Format(
                        Localization.T("status_libreoffice_starting", ClickraStorage.GetSetting(LanguageSettingKey)),
                        fileIndex + 1,
                        totalFiles));

                var startInfo = new ProcessStartInfo
                {
                    FileName = GetLaunchExecutablePath(executablePath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
                };
                startInfo.ArgumentList.Add("--headless");
                startInfo.ArgumentList.Add("--nologo");
                startInfo.ArgumentList.Add("--nolockcheck");
                startInfo.ArgumentList.Add("--nodefault");
                startInfo.ArgumentList.Add("--nofirststartwizard");
                startInfo.ArgumentList.Add("-env:UserInstallation=" + new Uri(profileDir).AbsoluteUri);
                startInfo.ArgumentList.Add("--convert-to");
                startInfo.ArgumentList.Add("pdf");
                startInfo.ArgumentList.Add("--outdir");
                startInfo.ArgumentList.Add(tempDir);
                startInfo.ArgumentList.Add(fullPath);

                var output = new StringBuilder();
                var error = new StringBuilder();
                uint previousErrorMode = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
                using var process = StartProcessAndRestoreErrorMode(startInfo, previousErrorMode);
                if (process == null)
                    throw new InvalidOperationException(Localization.T("error_libreoffice_start", ClickraStorage.GetSetting(LanguageSettingKey)));

                using var registration = cancellationToken.Register(() =>
                {
                    try { process.Kill(true); } catch { }
                });

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        error.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                onProgress?.Invoke(
                    fileIndex * 100 + 60,
                    totalFiles * 100,
                    string.Format(
                        Localization.T("status_libreoffice_exporting", ClickraStorage.GetSetting(LanguageSettingKey)),
                        Path.GetFileName(fullPath)));

                if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
                {
                    try { process.Kill(true); } catch { /* Ignored: a hung process must not mask the timeout error. */ }
                    throw new TimeoutException(Localization.T("error_libreoffice_timeout", ClickraStorage.GetSetting(LanguageSettingKey)));
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (process.ExitCode != 0)
                {
                    string details = error.Length > 0 ? error.ToString().Trim() : output.ToString().Trim();
                    throw new InvalidOperationException(string.Format(Localization.T("error_libreoffice_exit_code", ClickraStorage.GetSetting(LanguageSettingKey)), process.ExitCode, FormatLibreOfficeExitCode(process.ExitCode), details));
                }

                string convertedPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(fullPath) + ".pdf");
                if (!File.Exists(convertedPath))
                {
                    string details = error.Length > 0 ? error.ToString().Trim() : output.ToString().Trim();
                    throw new InvalidOperationException(string.Format(Localization.T("error_libreoffice_output_missing", ClickraStorage.GetSetting(LanguageSettingKey)), details));
                }

                string? outputDir = Path.GetDirectoryName(outputPdfPath);
                if (!string.IsNullOrEmpty(outputDir))
                    Directory.CreateDirectory(outputDir);
                if (File.Exists(outputPdfPath))
                    File.Delete(outputPdfPath);
                File.Move(convertedPath, outputPdfPath);

                onProgress?.Invoke(
                    fileIndex * 100 + 100,
                    totalFiles * 100,
                    string.Format(
                        Localization.T("status_libreoffice_completed", ClickraStorage.GetSetting(LanguageSettingKey)),
                        Path.GetFileName(fullPath)));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
                try
                {
                    if (Directory.Exists(profileDir))
                        Directory.Delete(profileDir, recursive: true);
                }
                catch { }
            }
        }

        private static string FormatLibreOfficeExitCode(int exitCode)
        {
            uint unsigned = unchecked((uint)exitCode);
            return unsigned switch
            {
                0xE06D7363 => "native C++ exception",
                0xC0000409 => "native crash",
                _ => $"0x{unsigned:X8}"
            };
        }

        private static Process? StartProcessAndRestoreErrorMode(ProcessStartInfo startInfo, uint previousErrorMode)
        {
            try
            {
                return Process.Start(startInfo);
            }
            finally
            {
                SetErrorMode(previousErrorMode);
            }
        }

        public static bool LooksLikeLibreOfficeExecutable(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return false;

            string fileName = Path.GetFileName(executablePath);
            if (!fileName.Equals("soffice.exe", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("soffice.com", StringComparison.OrdinalIgnoreCase))
                return false;

            string? programDir = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(programDir))
                return false;

            string[] requiredFiles =
            {
                "soffice.bin",
                "mergedlo.dll",
                "sal3.dll",
                "cppu3.dll",
                "cppuhelper3MSC.dll"
            };

            foreach (string requiredFile in requiredFiles)
            {
                string path = Path.Combine(programDir, requiredFile);
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

        private static string GetLaunchExecutablePath(string executablePath)
        {
            if (Path.GetFileName(executablePath).Equals("soffice.com", StringComparison.OrdinalIgnoreCase))
            {
                string? programDir = Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrWhiteSpace(programDir))
                {
                    string windowedLauncher = Path.Combine(programDir, "soffice.exe");
                    if (File.Exists(windowedLauncher))
                        return windowedLauncher;
                }
            }

            return executablePath;
        }

        private static IEnumerable<string> EnumerateExecutableCandidates(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (Directory.Exists(configuredPath))
                {
                    yield return Path.Combine(configuredPath, "program", "soffice.exe");
                    yield return Path.Combine(configuredPath, "soffice.exe");
                }
                else
                {
                    yield return configuredPath;
                }
            }

            string? envPath = Environment.GetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                if (Directory.Exists(envPath))
                {
                    yield return Path.Combine(envPath, "program", "soffice.exe");
                    yield return Path.Combine(envPath, "soffice.exe");
                }
                else
                {
                    yield return envPath;
                }
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFiles))
                yield return Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe");
            if (!string.IsNullOrEmpty(programFilesX86))
                yield return Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe");

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string pathDir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = pathDir.Trim('"');
                if (trimmed.Length > 0)
                    yield return Path.Combine(trimmed, "soffice.exe");
            }
        }
    }
}
