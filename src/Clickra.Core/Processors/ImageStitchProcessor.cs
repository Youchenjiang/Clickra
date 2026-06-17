using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility

namespace Clickra.Core.Processors
{
    public class ImageStitchProcessor : IFileProcessor
    {
        public void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for image stitching.");

            int total = files.Count;
            onProgress?.Invoke(10, total * 100, "正在分析圖片尺寸...");
            cancellationToken.ThrowIfCancellationRequested();
            List<Image> images = new List<Image>();
            try
            {
                foreach (var f in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                    var img = images[i];
                    onProgress?.Invoke((i * 100) + 50, total * 100, $"正在拼接圖片 ({i + 1}/{total})...");
                    int x = (totalWidth - img.Width) / 2;
                    gfx.DrawImage(img, x, currentY, img.Width, img.Height);
                    currentY += img.Height;
                }

                cancellationToken.ThrowIfCancellationRequested();
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
    }
}
