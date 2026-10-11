using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Clickra.Core;

namespace Clickra.Core.Processors
{
    public static class PowerShellHelper
    {
        internal static void ExportOfficeToPdf(
            OfficeConversionRequest request,
            OfficeConversionSettings settings,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            string appType = request.AppType;
            string engine = settings.Engine;
            if (engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase))
            {
                LibreOfficeHelper.ExportToPdf(request, settings, onProgress, cancellationToken);
                return;
            }

            if (!engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
                LibreOfficeHelper.CanConvert(appType) &&
                !IsMicrosoftOfficeReady(appType))
            {
                if (string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath(
                        settings.LibreOfficeRemovalPendingRestart,
                        settings.LibreOfficePath)))
                    throw new InvalidOperationException(Localization.T("error_libreoffice_not_ready", settings.Language));

                LibreOfficeHelper.ExportToPdf(request, settings, onProgress, cancellationToken);
                return;
            }

            try
            {
                ExportMicrosoftOfficeToPdf(request, settings.Language, onProgress, cancellationToken);
                return;
            }
            catch (InvalidOperationException ex) when (ShouldFallBackToLibreOffice(engine, appType, cancellationToken))
            {
                ExportLibreOfficeFallback(ex, request, settings, onProgress, cancellationToken);
            }
            catch (NotSupportedException ex) when (ShouldFallBackToLibreOffice(engine, appType, cancellationToken))
            {
                ExportLibreOfficeFallback(ex, request, settings, onProgress, cancellationToken);
            }
            catch (TimeoutException ex) when (ShouldFallBackToLibreOffice(engine, appType, cancellationToken))
            {
                ExportLibreOfficeFallback(ex, request, settings, onProgress, cancellationToken);
            }
        }

        private static void ExportLibreOfficeFallback(
            Exception originalException,
            OfficeConversionRequest request,
            OfficeConversionSettings settings,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath(
                    settings.LibreOfficeRemovalPendingRestart,
                    settings.LibreOfficePath)))
            {
                ExceptionDispatchInfo.Capture(originalException).Throw();
            }

            onProgress?.Invoke(
                request.FileIndex * 100,
                request.TotalFiles * 100,
                string.Format(
                    Localization.T("status_office_fallback_to_libreoffice", settings.Language),
                    request.AppType,
                    Path.GetFileName(request.FullPath)));
            LibreOfficeHelper.ExportToPdf(request, settings, onProgress, cancellationToken);
        }

        /// <summary>
        /// Whether a failed Microsoft Office conversion may recover through LibreOffice.
        /// Cancellation is a terminal user decision and must never launch a second engine.
        /// </summary>
        internal static bool ShouldFallBackToLibreOffice(
            string engine,
            string appType,
            CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested &&
            !engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
            LibreOfficeHelper.CanConvert(appType);

        internal static string? GetMicrosoftOfficeProgId(string appType) =>
            OfficeEngineDetector.GetMicrosoftOfficeProgId(appType);

        internal static bool IsMicrosoftOfficeReady(string appType) =>
            OfficeEngineDetector.IsMicrosoftOfficeReady(appType);

        private static void ExportMicrosoftOfficeToPdf(
            OfficeConversionRequest request,
            string language,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            string appType = request.AppType;
            string fullPath = request.FullPath;
            string outputPdfPath = request.OutputPdfPath;
            string psScript = "";
            if (appType == "Word")
            {
                psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $word = New-Object -ComObject Word.Application
    try {{
        $word.Visible = $false
        $word.DisplayAlerts = 0
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
        $ppt.DisplayAlerts = 1
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
    try {{
        $excel.Visible = $false
        $excel.DisplayAlerts = $false
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
                throw new NotSupportedException(string.Format(Localization.T("error_office_unsupported", language), appType));
            }

            RunOfficeInteropScript(
                new OfficeInteropRequest(
                    psScript,
                    request.FileIndex,
                    request.TotalFiles,
                    fullPath,
                    appType,
                    language),
                onProgress,
                cancellationToken);
            
            if (!File.Exists(outputPdfPath))
            {
                throw new InvalidOperationException(string.Format(Localization.T("error_office_output_missing", language), appType));
            }
        }

        internal static void RunOfficeInteropScript(
            OfficeInteropRequest request,
            Action<int, int, string>? onProgress, 
            CancellationToken cancellationToken)
        {
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(request.Script));
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException(string.Format(Localization.T("error_office_powershell_start", request.Language), request.AppName));

            // skipcq: CS-W1100 — the registration is kept alive only to dispose it.
            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); }
                catch
                {
                    // Best-effort cancellation cleanup; the cancellation result must win.
                }
            });

            var error = new StringBuilder();
            process.OutputDataReceived += (_, e) => ReportOfficeProgress(e.Data, request, onProgress);
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
                throw new TimeoutException(string.Format(Localization.T("error_office_timeout", request.Language), request.AppName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            ThrowIfOfficeInteropFailed(process.ExitCode, error.ToString(), request);
        }

        private static void ReportOfficeProgress(
            string? output,
            OfficeInteropRequest request,
            Action<int, int, string>? onProgress)
        {
            if (string.IsNullOrEmpty(output) ||
                !output.StartsWith("PROGRESS:", StringComparison.Ordinal) ||
                !int.TryParse(output.AsSpan(9), out int subProgress))
                return;

            string fileName = Path.GetFileName(request.FilePath);
            string statusMessage = subProgress switch
            {
                20 => string.Format(Localization.T("status_office_starting", request.Language), request.AppName, request.FileIndex + 1, request.TotalFiles),
                50 => string.Format(Localization.T("status_office_reading", request.Language), fileName),
                80 => string.Format(Localization.T("status_office_exporting", request.Language), fileName),
                100 => string.Format(Localization.T("status_office_completed", request.Language), fileName),
                _ => string.Format(Localization.T("status_office_converting", request.Language), request.AppName, fileName)
            };
            onProgress?.Invoke(
                (request.FileIndex * 100) + subProgress,
                request.TotalFiles * 100,
                statusMessage);
        }

        private static void ThrowIfOfficeInteropFailed(
            int exitCode,
            string errorText,
            OfficeInteropRequest request)
        {
            if (exitCode == 0)
                return;

            if (string.IsNullOrWhiteSpace(errorText))
                throw new InvalidOperationException(string.Format(Localization.T("error_office_exit_code", request.Language), request.AppName, exitCode));

            if (errorText.Contains("0x80040154", StringComparison.Ordinal) ||
                errorText.Contains("New-Object", StringComparison.Ordinal))
                throw new InvalidOperationException(string.Format(Localization.T("error_office_not_installed", request.Language), request.AppName));

            throw new InvalidOperationException(
                string.Format(Localization.T("error_office_failed", request.Language), request.AppName, errorText.Trim()));
        }
    }
}
