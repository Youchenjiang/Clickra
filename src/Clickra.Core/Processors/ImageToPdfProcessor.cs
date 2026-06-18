using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace Clickra.Core.Processors
{
    public class ImageToPdfProcessor : MultiFileProcessorBase
    {
        private PdfDocument? _doc;
        private string? _outputPath;

        public new void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for image to PDF conversion.");
            _outputPath = outputPath;
            _doc = new PdfDocument();
            try
            {
                base.Process(files, outputPath, options, onProgress, cancellationToken);
            }
            finally
            {
                _doc?.Dispose();
            }
        }

        protected override void ProcessFile(string filePath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            onProgress?.Invoke((fileIndex * 100) + 50, totalFiles * 100, $"正在處理圖片: {Path.GetFileName(filePath)} ({fileIndex + 1}/{totalFiles})...");
            if (!File.Exists(filePath)) throw new FileNotFoundException("Image file not found", filePath);
            using var ximg = XImage.FromFile(filePath);
            var page = _doc!.AddPage();

            double resolutionX = ximg.HorizontalResolution > 0 ? ximg.HorizontalResolution : 72.0;
            double resolutionY = ximg.VerticalResolution > 0 ? ximg.VerticalResolution : 72.0;

            page.Width = XUnit.FromPoint(ximg.PixelWidth * 72.0 / resolutionX);
            page.Height = XUnit.FromPoint(ximg.PixelHeight * 72.0 / resolutionY);

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(ximg, 0, 0, page.Width.Point, page.Height.Point);
        }

        protected override void OnAllFilesProcessed(string? outputPath, int totalFiles, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(totalFiles * 100, totalFiles * 100, "轉換完成，正在儲存 PDF...");
            _doc!.Save(outputPath);
        }
    }
}
