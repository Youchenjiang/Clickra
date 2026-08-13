using System;
using System.IO;
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
                    throw new InvalidOperationException(Localization.T("error_libreoffice_not_ready", ClickraStorage.GetSetting(LanguageSettingKey)));

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
                throw new InvalidOperationException(string.Format(Localization.T("error_office_output_missing", ClickraStorage.GetSetting(LanguageSettingKey)), appType));
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

            // skipcq: CS-W1100 — the registration is kept alive only to dispose it.
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
}
