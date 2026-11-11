using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Clickra.Core;
using Clickra.Core.Modules.Doc.Shared.Application;

namespace Clickra.Core.Modules.Doc.Shared.Infrastructure;

public static class LibreOfficeHelper
{
    private const string LibreOfficeProgramDirectoryName = "program";
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S9422", Justification = "Project targets .NET 8.0 where System.Threading.Lock is not available.")]
    private static readonly object ResolveCacheLock = new();
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S9422", Justification = "Project targets .NET 8.0 where System.Threading.Lock is not available.")]
    private static readonly object ConversionLaunchLock = new();
    private static DateTime _resolvedExecutableCachedAt = DateTime.MinValue;
    private static string _resolvedExecutableCacheKey = "";
    private static string _resolvedExecutableCacheValue = "";

    private readonly record struct ConversionPaths(string Input, string Output, string TemporaryDirectory, string ProfileDirectory);
    private readonly record struct ConversionProgress(int FileIndex, int TotalFiles, Action<int, int, string>? Callback);

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    private static uint SafeSetErrorMode(uint uMode)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return SetErrorMode(uMode);
            }
            catch (EntryPointNotFoundException)
            {
                // Fall back when SetErrorMode entry point is not available in host environment
            }
            catch (DllNotFoundException)
            {
                // Fall back when kernel32 cannot be loaded
            }
        }
        return 0;
    }

    public static string DefaultLibreOfficeExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "soffice.exe" : "soffice";

    public static bool CanConvert(string appType) =>
        appType.Equals("Word", StringComparison.OrdinalIgnoreCase) ||
        appType.Equals("PowerPoint", StringComparison.OrdinalIgnoreCase) ||
        appType.Equals("Excel", StringComparison.OrdinalIgnoreCase);

    public static bool TryResolveExecutable(string? configuredPath, out string executablePath)
    {
        executablePath = EnumerateExecutableCandidates(configuredPath).FirstOrDefault(System.IO.File.Exists) ?? "";
        return executablePath.Length > 0;
    }

    public static string GetResolvedExecutablePath(bool removalPending, string configuredPath)
    {
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

    private static string ResolveExecutablePathUncached(bool removalPending, string configuredPath)
    {
        if (removalPending)
            return "";

        if (!TryResolveExecutable(configuredPath, out string executablePath))
            return "";

        return executablePath;
    }

    public static bool TryGetExecutableVersion(string executablePath, out string version)
    {
        version = "";
        if (string.IsNullOrWhiteSpace(executablePath) || !System.IO.File.Exists(executablePath))
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

            uint previousErrorMode = SafeSetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
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

    internal static void ExportToPdf(
        OfficeConversionRequest request,
        OfficeConversionSettings settings,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        string executablePath = GetValidatedExecutablePath(request.AppType, settings);

        string tempDir = Path.Combine(Path.GetTempPath(), "ClickraLibreOffice", Guid.NewGuid().ToString("N"));
        string profileDir = Path.Combine(Path.GetTempPath(), "ClickraLibreOfficeProfile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(profileDir);
        var paths = new ConversionPaths(request.FullPath, request.OutputPdfPath, tempDir, profileDir);
        var progress = new ConversionProgress(request.FileIndex, request.TotalFiles, onProgress);

        try
        {
            RunConversion(executablePath, paths, progress, settings.Language, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
            TryDeleteDirectory(profileDir);
        }
    }

    private static string GetValidatedExecutablePath(string appType, OfficeConversionSettings settings)
    {
        if (!CanConvert(appType))
            throw new NotSupportedException(string.Format(Localization.T("error_libreoffice_unsupported", settings.Language), appType));

        if (settings.LibreOfficeRemovalPendingRestart ||
            !TryResolveExecutable(settings.LibreOfficePath, out string executablePath))
        {
            throw new InvalidOperationException(Localization.T("error_libreoffice_not_ready", settings.Language));
        }

        if (!LooksLikeLibreOfficeExecutable(executablePath))
            throw new InvalidOperationException(Localization.T("error_libreoffice_unusable", settings.Language));

        return executablePath;
    }

    private static void RunConversion(
        string executablePath,
        ConversionPaths paths,
        ConversionProgress progress,
        string language,
        CancellationToken cancellationToken)
    {
        progress.Callback?.Invoke(
            progress.FileIndex * 100 + 20,
            progress.TotalFiles * 100,
            string.Format(
                Localization.T("status_libreoffice_starting", language),
                progress.FileIndex + 1,
                progress.TotalFiles));

        ProcessStartInfo startInfo = CreateConversionStartInfo(executablePath, paths);
        var output = new StringBuilder();
        var error = new StringBuilder();
        // Install the cancellation callback before launching the fallback engine.
        // Serializing the launch and callback also handles cancellation during Process.Start.
        Process? launched = null;
        using var registration = cancellationToken.Register(() =>
        {
            lock (ConversionLaunchLock)
                if (launched != null) TryKillProcess(launched);
        });
        // The registration remains alive until conversion finishes.
        _ = registration;
        Process process;
        lock (ConversionLaunchLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint previousErrorMode = SafeSetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
            launched = StartProcessAndRestoreErrorMode(startInfo, previousErrorMode);
            process = launched ?? throw new InvalidOperationException(Localization.T("error_libreoffice_start", language));
        }
        using (process)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureProcessOutput(process, output, error);

            progress.Callback?.Invoke(
                progress.FileIndex * 100 + 60,
                progress.TotalFiles * 100,
                string.Format(
                    Localization.T("status_libreoffice_exporting", language),
                    Path.GetFileName(paths.Input)));

            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                TryKillProcess(process);
                throw new TimeoutException(Localization.T("error_libreoffice_timeout", language));
            }

            cancellationToken.ThrowIfCancellationRequested();
            MoveConversionOutput(process.ExitCode, paths, output, error, language);

            progress.Callback?.Invoke(
                progress.FileIndex * 100 + 100,
                progress.TotalFiles * 100,
                string.Format(
                    Localization.T("status_libreoffice_completed", language),
                    Path.GetFileName(paths.Input)));
        }
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
        StringBuilder error,
        string language)
    {
        string details = error.Length > 0 ? error.ToString().Trim() : output.ToString().Trim();
        if (exitCode != 0)
            throw new InvalidOperationException(string.Format(Localization.T("error_libreoffice_exit_code", language), exitCode, FormatLibreOfficeExitCode(exitCode), details));

        string convertedPath = Path.Combine(paths.TemporaryDirectory, Path.GetFileNameWithoutExtension(paths.Input) + ".pdf");
        if (!System.IO.File.Exists(convertedPath))
            throw new InvalidOperationException(string.Format(Localization.T("error_libreoffice_output_missing", language), details));

        string? outputDir = Path.GetDirectoryName(paths.Output);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);
        if (System.IO.File.Exists(paths.Output))
            System.IO.File.Delete(paths.Output);
        System.IO.File.Move(convertedPath, paths.Output);
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
            SafeSetErrorMode(previousErrorMode);
        }
    }

    public static bool LooksLikeLibreOfficeExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !System.IO.File.Exists(executablePath))
            return false;

        string fileName = Path.GetFileName(executablePath);
        if (!IsSofficeFileName(fileName))
            return false;

        string? programDir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(programDir))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return HasRequiredWindowsDependencies(programDir);
        }

        return true;
    }

    private static bool IsSofficeFileName(string fileName) =>
        fileName.Equals(DefaultLibreOfficeExecutableName, StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("soffice", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("soffice.exe", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("soffice.com", StringComparison.OrdinalIgnoreCase);

    private static bool HasRequiredWindowsDependencies(string programDir)
    {
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
            if (!System.IO.File.Exists(path))
                return false;

            try
            {
                if (new FileInfo(path).Length <= 0)
                    return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
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
                string windowedLauncher = Path.Combine(programDir, DefaultLibreOfficeExecutableName);
                if (System.IO.File.Exists(windowedLauncher))
                    return windowedLauncher;
            }
        }

        return executablePath;
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(string? configuredPath)
    {
        string exeName = DefaultLibreOfficeExecutableName;

        foreach (string candidate in EnumeratePathCandidates(configuredPath, exeName))
            yield return candidate;

        foreach (string candidate in EnumeratePathCandidates(Environment.GetEnvironmentVariable("CLICKRA_LIBREOFFICE_PATH"), exeName))
            yield return candidate;

        foreach (string candidate in EnumerateDefaultPlatformCandidates(exeName))
            yield return candidate;

        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string pathDir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = pathDir.Trim('"');
            if (trimmed.Length > 0)
                yield return Path.Combine(trimmed, exeName);
        }
    }

    private static IEnumerable<string> EnumeratePathCandidates(string? basePath, string exeName)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            yield break;

        if (Directory.Exists(basePath))
        {
            yield return Path.Combine(basePath, LibreOfficeProgramDirectoryName, exeName);
            yield return Path.Combine(basePath, exeName);
        }
        else
        {
            yield return basePath;
        }
    }

    private static IEnumerable<string> EnumerateDefaultPlatformCandidates(string exeName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFiles))
                yield return Path.Combine(programFiles, "LibreOffice", LibreOfficeProgramDirectoryName, exeName);
            if (!string.IsNullOrEmpty(programFilesX86))
                yield return Path.Combine(programFilesX86, "LibreOffice", LibreOfficeProgramDirectoryName, exeName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return "/usr/bin/soffice";
            yield return "/usr/local/bin/soffice";
            yield return "/snap/bin/soffice";
            yield return "/opt/libreoffice/program/soffice";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
            yield return "/usr/local/bin/soffice";
        }
    }
}
