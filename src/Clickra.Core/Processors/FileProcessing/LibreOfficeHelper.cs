using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Clickra.Core;

namespace Clickra.Core.Processors;

public static class LibreOfficeHelper
{
    private const string LanguageSettingKey = "Language";
    private const string LibreOfficeExecutableName = "soffice.exe";
    private const string LibreOfficeProgramDirectoryName = "program";
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;
    private static readonly object ResolveCacheLock = new();
    private static DateTime _resolvedExecutableCachedAt = DateTime.MinValue;
    private static string _resolvedExecutableCacheKey = "";
    private static string _resolvedExecutableCacheValue = "";

    private readonly record struct ConversionPaths(string Input, string Output, string TemporaryDirectory, string ProfileDirectory);
    private readonly record struct ConversionProgress(int FileIndex, int TotalFiles, Action<int, int, string>? Callback);

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    public static bool CanConvert(string appType) =>
        appType.Equals("Word", StringComparison.OrdinalIgnoreCase) ||
        appType.Equals("PowerPoint", StringComparison.OrdinalIgnoreCase) ||
        appType.Equals("Excel", StringComparison.OrdinalIgnoreCase);

    public static bool TryResolveExecutable(string? configuredPath, out string executablePath)
    {
        executablePath = EnumerateExecutableCandidates(configuredPath).FirstOrDefault(File.Exists) ?? "";
        return executablePath.Length > 0;
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
                TryKillProcess(process);
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
        string executablePath = GetValidatedExecutablePath(appType);

        string tempDir = Path.Combine(Path.GetTempPath(), "ClickraLibreOffice", Guid.NewGuid().ToString("N"));
        string profileDir = Path.Combine(Path.GetTempPath(), "ClickraLibreOfficeProfile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(profileDir);
        var paths = new ConversionPaths(fullPath, outputPdfPath, tempDir, profileDir);
        var progress = new ConversionProgress(fileIndex, totalFiles, onProgress);

        try
        {
            RunConversion(executablePath, paths, progress, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
            TryDeleteDirectory(profileDir);
        }
    }

    private static string GetValidatedExecutablePath(string appType)
    {
        if (!CanConvert(appType))
            throw new NotSupportedException(string.Format(Localization.T("error_libreoffice_unsupported", ClickraStorage.GetSetting(LanguageSettingKey)), appType));

        if (ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase) ||
            !TryResolveExecutable(ClickraStorage.GetSetting("LibreOfficePath"), out string executablePath))
        {
            throw new InvalidOperationException(Localization.T("error_libreoffice_not_ready", ClickraStorage.GetSetting(LanguageSettingKey)));
        }

        if (!LooksLikeLibreOfficeExecutable(executablePath))
            throw new InvalidOperationException(Localization.T("error_libreoffice_unusable", ClickraStorage.GetSetting(LanguageSettingKey)));

        return executablePath;
    }

    private static void RunConversion(
        string executablePath,
        ConversionPaths paths,
        ConversionProgress progress,
        CancellationToken cancellationToken)
    {
        progress.Callback?.Invoke(
            progress.FileIndex * 100 + 20,
            progress.TotalFiles * 100,
            string.Format(
                Localization.T("status_libreoffice_starting", ClickraStorage.GetSetting(LanguageSettingKey)),
                progress.FileIndex + 1,
                progress.TotalFiles));

        ProcessStartInfo startInfo = CreateConversionStartInfo(executablePath, paths);
        var output = new StringBuilder();
        var error = new StringBuilder();
        uint previousErrorMode = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
        using var process = StartProcessAndRestoreErrorMode(startInfo, previousErrorMode);
        if (process == null)
            throw new InvalidOperationException(Localization.T("error_libreoffice_start", ClickraStorage.GetSetting(LanguageSettingKey)));

        // skipcq: CS-W1100 — the registration is kept alive only to dispose it.
        using var registration = cancellationToken.Register(() => TryKillProcess(process));
        CaptureProcessOutput(process, output, error);

        progress.Callback?.Invoke(
            progress.FileIndex * 100 + 60,
            progress.TotalFiles * 100,
            string.Format(
                Localization.T("status_libreoffice_exporting", ClickraStorage.GetSetting(LanguageSettingKey)),
                Path.GetFileName(paths.Input)));

        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            TryKillProcess(process);
            throw new TimeoutException(Localization.T("error_libreoffice_timeout", ClickraStorage.GetSetting(LanguageSettingKey)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        MoveConversionOutput(process.ExitCode, paths, output, error);

        progress.Callback?.Invoke(
            progress.FileIndex * 100 + 100,
            progress.TotalFiles * 100,
            string.Format(
                Localization.T("status_libreoffice_completed", ClickraStorage.GetSetting(LanguageSettingKey)),
                Path.GetFileName(paths.Input)));
    }

    private static ProcessStartInfo CreateConversionStartInfo(string executablePath, ConversionPaths paths)
    {
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
        startInfo.ArgumentList.Add("-env:UserInstallation=" + new Uri(paths.ProfileDirectory).AbsoluteUri);
        startInfo.ArgumentList.Add("--convert-to");
        startInfo.ArgumentList.Add("pdf");
        startInfo.ArgumentList.Add("--outdir");
        startInfo.ArgumentList.Add(paths.TemporaryDirectory);
        startInfo.ArgumentList.Add(paths.Input);
        return startInfo;
    }

    private static void CaptureProcessOutput(Process process, StringBuilder output, StringBuilder error)
    {
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
    }

    private static void MoveConversionOutput(
        int exitCode,
        ConversionPaths paths,
        StringBuilder output,
        StringBuilder error)
    {
        string details = error.Length > 0 ? error.ToString().Trim() : output.ToString().Trim();
        if (exitCode != 0)
            throw new InvalidOperationException(string.Format(Localization.T("error_libreoffice_exit_code", ClickraStorage.GetSetting(LanguageSettingKey)), exitCode, FormatLibreOfficeExitCode(exitCode), details));

        string convertedPath = Path.Combine(paths.TemporaryDirectory, Path.GetFileNameWithoutExtension(paths.Input) + ".pdf");
        if (!File.Exists(convertedPath))
            throw new InvalidOperationException(string.Format(Localization.T("error_libreoffice_output_missing", ClickraStorage.GetSetting(LanguageSettingKey)), details));

        string? outputDir = Path.GetDirectoryName(paths.Output);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);
        if (File.Exists(paths.Output))
            File.Delete(paths.Output);
        File.Move(convertedPath, paths.Output);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch
        {
            // Best effort only: the original timeout or cancellation outcome must be preserved.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort only: temporary cleanup must not hide the conversion outcome.
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
        if (!fileName.Equals(LibreOfficeExecutableName, StringComparison.OrdinalIgnoreCase) &&
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
                string windowedLauncher = Path.Combine(programDir, LibreOfficeExecutableName);
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
                yield return Path.Combine(configuredPath, LibreOfficeProgramDirectoryName, LibreOfficeExecutableName);
                yield return Path.Combine(configuredPath, LibreOfficeExecutableName);
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
                yield return Path.Combine(envPath, LibreOfficeProgramDirectoryName, LibreOfficeExecutableName);
                yield return Path.Combine(envPath, LibreOfficeExecutableName);
            }
            else
            {
                yield return envPath;
            }
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFiles))
            yield return Path.Combine(programFiles, "LibreOffice", LibreOfficeProgramDirectoryName, LibreOfficeExecutableName);
        if (!string.IsNullOrEmpty(programFilesX86))
            yield return Path.Combine(programFilesX86, "LibreOffice", LibreOfficeProgramDirectoryName, LibreOfficeExecutableName);

        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string pathDir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = pathDir.Trim('"');
            if (trimmed.Length > 0)
                yield return Path.Combine(trimmed, LibreOfficeExecutableName);
        }
    }
}
