using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility

namespace Clickra.Core.Processors
{
    public class ImageStitchProcessor : MultiFileProcessorBase
    {
        private List<Image> _images = new List<Image>();
        private Bitmap? _stitched;
        private Graphics? _gfx;
        private int _currentY = 0;
        private int _totalWidth = 0;

        public new void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for image stitching.");

            onProgress?.Invoke(10, files.Count * 100, "正在分析圖片尺寸...");
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var f in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _images.Add(Image.FromFile(f));
                }

                _totalWidth = _images.Max(img => img.Width);
                int totalHeight = _images.Sum(img => img.Height);

                _stitched = new Bitmap(_totalWidth, totalHeight);
                _gfx = Graphics.FromImage(_stitched);
                _gfx.Clear(Color.White);

                base.Process(files, outputPath, options, onProgress, cancellationToken);
            }
            finally
            {
                _gfx?.Dispose();
                _stitched?.Dispose();
                foreach (var img in _images)
                {
                    try { img?.Dispose(); } catch { }
                }
            }
        }

        protected override void ProcessFile(string filePath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            var img = _images[fileIndex];
            onProgress?.Invoke((fileIndex * 100) + 50, totalFiles * 100, $"正在拼接圖片 ({fileIndex + 1}/{totalFiles})...");
            int x = (_totalWidth - img.Width) / 2;
            _gfx!.DrawImage(img, x, _currentY, img.Width, img.Height);
            _currentY += img.Height;
        }

        protected override void OnAllFilesProcessed(string? outputPath, int totalFiles, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(totalFiles * 100, totalFiles * 100, "拼接完成，正在儲存圖片...");
            _stitched!.Save(outputPath!, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
