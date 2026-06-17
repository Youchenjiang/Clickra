using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace Clickra.Core.Processors
{
    public class ImageToPdfProcessor : IFileProcessor
    {
        public void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for image to PDF conversion.");

            int total = files.Count;
            using var doc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                var f = files[i];
                onProgress?.Invoke((i * 100) + 50, total * 100, $"正在處理圖片: {Path.GetFileName(f)} ({i + 1}/{total})...");
                if (!File.Exists(f)) throw new FileNotFoundException("Image file not found", f);
                using var ximg = XImage.FromFile(f);
                var page = doc.AddPage();
                
                double resolutionX = ximg.HorizontalResolution > 0 ? ximg.HorizontalResolution : 72.0;
                double resolutionY = ximg.VerticalResolution > 0 ? ximg.VerticalResolution : 72.0;
                
                page.Width = XUnit.FromPoint(ximg.PixelWidth * 72.0 / resolutionX);
                page.Height = XUnit.FromPoint(ximg.PixelHeight * 72.0 / resolutionY);

                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(ximg, 0, 0, page.Width.Point, page.Height.Point);
            }
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(total * 100, total * 100, "轉換完成，正在儲存 PDF...");
            doc.Save(outputPath);
        }
    }
}
