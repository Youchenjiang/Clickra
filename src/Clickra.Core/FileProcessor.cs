using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility
using System.Drawing;

namespace Clickra.Core
{
    public static class FileProcessor
    {
        public static void MergePdfs(List<string> files, string outputPath, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            using var outDoc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                onProgress?.Invoke((i * 100) + 50, total * 100, $"正在合併: {Path.GetFileName(f)} ({i + 1}/{total})...");
                using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                for (int j = 0; j < inDoc.PageCount; j++)
                {
                    outDoc.AddPage(inDoc.Pages[j]);
                }
            }
            onProgress?.Invoke(total * 100, total * 100, "合併完成，正在儲存檔案...");
            outDoc.Save(outputPath);
        }

        public static void ImagesToPdf(List<string> files, string outputPath, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            using var doc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                onProgress?.Invoke((i * 100) + 50, total * 100, $"正在處理圖片: {Path.GetFileName(f)} ({i + 1}/{total})...");
                if (!File.Exists(f)) throw new FileNotFoundException("Image file not found", f);
                using var ximg = XImage.FromFile(f);
                var page = doc.AddPage();
                
                double resolutionX = ximg.HorizontalResolution > 0 ? ximg.HorizontalResolution : 72.0;
                double resolutionY = ximg.VerticalResolution > 0 ? ximg.VerticalResolution : 72.0;
                
                page.Width = ximg.PixelWidth * 72.0 / resolutionX;
                page.Height = ximg.PixelHeight * 72.0 / resolutionY;

                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(ximg, 0, 0, page.Width, page.Height);
            }
            onProgress?.Invoke(total * 100, total * 100, "轉換完成，正在儲存 PDF...");
            doc.Save(outputPath);
        }

        public static void StitchImages(List<string> files, string outputPath, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            onProgress?.Invoke(10, total * 100, "正在分析圖片尺寸...");
            List<Image> images = new List<Image>();
            try
            {
                foreach (var f in files)
                {
                    images.Add(Image.FromFile(f));
                }
                
                int totalWidth = images.Max(img => img.Width);
                int totalHeight = images.Sum(img => img.Height);

                using var stitched = new Bitmap(totalWidth, totalHeight);
                using var gfx = Graphics.FromImage(stitched);
                gfx.Clear(Color.White);

                int currentY = 0;
                for (int i = 0; i < images.Count; i++)
                {
                    var img = images[i];
                    onProgress?.Invoke((i * 100) + 50, total * 100, $"正在拼接圖片 ({i + 1}/{total})...");
                    int x = (totalWidth - img.Width) / 2;
                    gfx.DrawImage(img, x, currentY, img.Width, img.Height);
                    currentY += img.Height;
                }

                onProgress?.Invoke(total * 100, total * 100, "拼接完成，正在儲存圖片...");
                stitched.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            finally
            {
                foreach (var img in images)
                {
                    try { img?.Dispose(); } catch { }
                }
            }
        }

        public static void ConvertPptToPdf(List<string> files, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            for (int i = 0; i < files.Count; i++)
            {
                var filePath = files[i];
                int fileIndex = i;
                string fullPath = Path.GetFullPath(filePath);
                string outputPdfPath = Path.ChangeExtension(fullPath, ".pdf");
                onProgress?.Invoke((fileIndex * 100) + 10, total * 100, $"正在準備轉換 PowerPoint: {Path.GetFileName(filePath)}...");

                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    Write-Host 'PROGRESS:20'
    $ppt = New-Object -ComObject PowerPoint.Application
    try {{
        Write-Host 'PROGRESS:50'
        $pres = $ppt.Presentations.Open('{fullPath.Replace("'", "''")}', -1, 0, 0)
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
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("PROGRESS:"))
                        {
                            if (int.TryParse(e.Data.Substring(9), out int subProg))
                            {
                                int currentProgress = (fileIndex * 100) + subProg;
                                string statusMsg = subProg switch
                                {
                                    20 => $"正在啟動 PowerPoint 引擎 ({fileIndex + 1}/{files.Count})...",
                                    50 => $"正在讀取簡報: {Path.GetFileName(filePath)}...",
                                    80 => $"正在匯出 PDF: {Path.GetFileName(filePath)}...",
                                    100 => $"已完成轉換: {Path.GetFileName(filePath)}",
                                    _ => $"正在轉換 PowerPoint: {Path.GetFileName(filePath)}..."
                                };
                                onProgress?.Invoke(currentProgress, total * 100, statusMsg);
                            }
                        }
                    };
                    process.BeginOutputReadLine();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!File.Exists(outputPdfPath))
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            if (error.Contains("0x80040154") || error.Contains("New-Object"))
                                throw new Exception("Microsoft PowerPoint is not installed. This feature requires Microsoft PowerPoint to be installed on your system.");
                            else
                                throw new Exception($"PowerPoint conversion failed: {error.Trim()}");
                        }
                        throw new Exception("PowerPoint conversion failed with unknown error.");
                    }
                }
            }
        }

        public static void ConvertWordToPdf(List<string> files, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            for (int i = 0; i < files.Count; i++)
            {
                var filePath = files[i];
                int fileIndex = i;
                string fullPath = Path.GetFullPath(filePath);
                string outputPdfPath = Path.ChangeExtension(fullPath, ".pdf");
                onProgress?.Invoke((fileIndex * 100) + 10, total * 100, $"正在準備轉換 Word: {Path.GetFileName(filePath)}...");

                // Word COM: wdExportFormatPDF = 17
                string psScript = $@"
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
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("PROGRESS:"))
                        {
                            if (int.TryParse(e.Data.Substring(9), out int subProg))
                            {
                                int currentProgress = (fileIndex * 100) + subProg;
                                string statusMsg = subProg switch
                                {
                                    20 => $"正在啟動 Word 引擎 ({fileIndex + 1}/{files.Count})...",
                                    50 => $"正在讀取文件: {Path.GetFileName(filePath)}...",
                                    80 => $"正在匯出 PDF: {Path.GetFileName(filePath)}...",
                                    100 => $"已完成轉換: {Path.GetFileName(filePath)}",
                                    _ => $"正在轉換 Word: {Path.GetFileName(filePath)}..."
                                };
                                onProgress?.Invoke(currentProgress, total * 100, statusMsg);
                            }
                        }
                    };
                    process.BeginOutputReadLine();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!File.Exists(outputPdfPath))
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            if (error.Contains("0x80040154") || error.Contains("New-Object"))
                                throw new Exception("Microsoft Word is not installed. This feature requires Microsoft Word to be installed on your system.");
                            else
                                throw new Exception($"Word conversion failed: {error.Trim()}");
                        }
                        throw new Exception("Word conversion failed with unknown error.");
                    }
                }
            }
        }
    }
}
