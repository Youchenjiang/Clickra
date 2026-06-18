using System;
using System.IO;
using System.Threading;

namespace Clickra.Core.Processors
{
    public static class PowerShellHelper
    {
        public static void ExportOfficeToPdf(
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
            else
            {
                throw new NotSupportedException($"Office application {appType} is not supported.");
            }

            RunOfficeInteropScript(psScript, fileIndex, totalFiles, fullPath, appType, onProgress, cancellationToken);
            
            if (!File.Exists(outputPdfPath))
            {
                throw new Exception($"{appType} conversion failed: Output PDF file was not created.");
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

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                using var registration = cancellationToken.Register(() =>
                {
                    try { process.Kill(true); } catch { }
                });

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("PROGRESS:"))
                    {
                        if (int.TryParse(e.Data.Substring(9), out int subProg))
                        {
                            int currentProgress = (fileIndex * 100) + subProg;
                            string statusMsg = subProg switch
                            {
                                20 => $"正在啟動 {appName} 引擎 ({fileIndex + 1}/{totalFiles})...",
                                50 => $"正在讀取文件: {Path.GetFileName(filePath)}...",
                                80 => $"正在匯出 PDF: {Path.GetFileName(filePath)}...",
                                100 => $"已完成轉換: {Path.GetFileName(filePath)}",
                                _ => $"正在轉換 {appName}: {Path.GetFileName(filePath)}..."
                            };
                            onProgress?.Invoke(currentProgress, totalFiles * 100, statusMsg);
                        }
                    }
                };
                process.BeginOutputReadLine();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(error) && process.ExitCode != 0)
                {
                    if (error.Contains("0x80040154") || error.Contains("New-Object"))
                        throw new Exception($"Microsoft {appName} is not installed. This feature requires Microsoft {appName} to be installed on your system.");
                    else
                        throw new Exception($"{appName} conversion failed: {error.Trim()}");
                }
                
                if (process.ExitCode != 0)
                {
                    throw new Exception($"{appName} conversion failed with exit code {process.ExitCode}.");
                }
            }
        }
    }
}
