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
                onProgress?.Invoke(i + 1, total, $"正在合併: {Path.GetFileName(f)}...");
                using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                for (int j = 0; j < inDoc.PageCount; j++)
                {
                    outDoc.AddPage(inDoc.Pages[j]);
                }
            }
            onProgress?.Invoke(total, total, "合併完成，正在儲存檔案...");
            outDoc.Save(outputPath);
        }

        public static void ImagesToPdf(List<string> files, string outputPath, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            using var doc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                onProgress?.Invoke(i + 1, total, $"正在處理圖片: {Path.GetFileName(f)}...");
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
            onProgress?.Invoke(total, total, "轉換完成，正在儲存 PDF...");
            doc.Save(outputPath);
        }

        public static void StitchImages(List<string> files, string outputPath, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            onProgress?.Invoke(0, total, "正在分析圖片尺寸...");
            List<Image> images = files.Select(Image.FromFile).ToList();
            
            int totalWidth = images.Max(img => img.Width);
            int totalHeight = images.Sum(img => img.Height);

            using var stitched = new Bitmap(totalWidth, totalHeight);
            using var gfx = Graphics.FromImage(stitched);
            gfx.Clear(Color.White);

            int currentY = 0;
            for (int i = 0; i < images.Count; i++)
            {
                var img = images[i];
                onProgress?.Invoke(i + 1, total, $"正在拼接圖片 ({i + 1}/{total})...");
                int x = (totalWidth - img.Width) / 2;
                gfx.DrawImage(img, x, currentY, img.Width, img.Height);
                currentY += img.Height;
                img.Dispose();
            }

            onProgress?.Invoke(total, total, "拼接完成，正在儲存圖片...");
            stitched.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        public static void ConvertPptToPdf(List<string> files, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            foreach (var filePath in files)
            {
                int currentIndex = files.IndexOf(filePath) + 1;
                string fullPath = Path.GetFullPath(filePath);
                string outputPdfPath = Path.ChangeExtension(fullPath, ".pdf");
                onProgress?.Invoke(currentIndex, total, $"正在轉換 PowerPoint: {Path.GetFileName(filePath)}...");

                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    $ppt = New-Object -ComObject PowerPoint.Application
    try {{
        $pres = $ppt.Presentations.Open('{fullPath.Replace("'", "''")}', -1, 0, 0)
        $pres.SaveAs('{outputPdfPath.Replace("'", "''")}', 32)
        $pres.Close()
        Write-Host 'Success'
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
                string output = process?.StandardOutput.ReadToEnd() ?? "";
                string error = process?.StandardError.ReadToEnd() ?? "";
                process?.WaitForExit();

                if (File.Exists(outputPdfPath))
                {
                    onProgress?.Invoke(currentIndex, total, $"已完成: {Path.GetFileName(filePath)}");
                }
                else
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

        public static void ConvertWordToPdf(List<string> files, Action<int, int, string>? onProgress = null)
        {
            int total = files.Count;
            foreach (var filePath in files)
            {
                int currentIndex = files.IndexOf(filePath) + 1;
                string fullPath = Path.GetFullPath(filePath);
                string outputPdfPath = Path.ChangeExtension(fullPath, ".pdf");
                onProgress?.Invoke(currentIndex, total, $"正在轉換 Word: {Path.GetFileName(filePath)}...");

                // Word COM: wdExportFormatPDF = 17
                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    $word = New-Object -ComObject Word.Application
    try {{
        $doc = $word.Documents.Open('{fullPath.Replace("'", "''")}', $false, $true)
        $doc.ExportAsFixedFormat('{outputPdfPath.Replace("'", "''")}', 17)
        $doc.Close($false)
        Write-Host 'Success'
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
                string output = process?.StandardOutput.ReadToEnd() ?? "";
                string error = process?.StandardError.ReadToEnd() ?? "";
                process?.WaitForExit();

                if (File.Exists(outputPdfPath))
                {
                    onProgress?.Invoke(currentIndex, total, $"已完成: {Path.GetFileName(filePath)}");
                }
                else
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
