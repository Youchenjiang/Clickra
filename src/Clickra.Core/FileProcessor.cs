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
        public static void MergePdfs(List<string> files, string outputPath)
        {
            using var outDoc = new PdfDocument();
            foreach (var f in files)
            {
                using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                for (int i = 0; i < inDoc.PageCount; i++)
                {
                    outDoc.AddPage(inDoc.Pages[i]);
                }
            }
            outDoc.Save(outputPath);
        }

        public static void ImagesToPdf(List<string> files, string outputPath)
        {
            using var doc = new PdfDocument();
            foreach (var f in files)
            {
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
            doc.Save(outputPath);
        }

        public static void StitchImages(List<string> files, string outputPath)
        {
            List<Image> images = files.Select(Image.FromFile).ToList();
            
            int totalWidth = images.Max(img => img.Width);
            int totalHeight = images.Sum(img => img.Height);

            using var stitched = new Bitmap(totalWidth, totalHeight);
            using var gfx = Graphics.FromImage(stitched);
            gfx.Clear(Color.White);

            int currentY = 0;
            foreach (var img in images)
            {
                int x = (totalWidth - img.Width) / 2;
                gfx.DrawImage(img, x, currentY, img.Width, img.Height);
                currentY += img.Height;
                img.Dispose();
            }

            stitched.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        public static void ConvertPptToPdf(List<string> files, Action<string>? onProgress = null)
        {
            foreach (var filePath in files)
            {
                string fullPath = Path.GetFullPath(filePath);
                string outputPdfPath = Path.ChangeExtension(fullPath, ".pdf");
                onProgress?.Invoke($"Converting: {Path.GetFileName(filePath)}...");

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
                    onProgress?.Invoke("Done.");
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
    }
}
