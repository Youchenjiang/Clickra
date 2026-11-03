using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Clickra.Core.Modules.Image.Processing;
using Clickra.Core.Modules.PdfSplit.Processing;
using Clickra.Core.Modules.PdfToImage.Application;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

#pragma warning disable CA1416 // Validate platform compatibility

namespace Clickra.Core.Modules.PdfToImage.Processing;

/// <summary>
/// Processor that renders PDF pages into raster images (PNG, JPG, WEBP),
/// supporting per-page export and vertical long-image stitching.
/// </summary>
public class PdfToImageProcessor : SingleFileProcessorBase
{
    private const int MaxStitchedHeight = 32767;

    protected override string GetOutputSuffix() => ".png";

    protected override void ProcessSingleFile(
        string fullPath,
        string targetOutputPath,
        int fileIndex,
        int totalFiles,
        Dictionary<string, object>? options,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        string format = PdfToImageOptions.NormalizeFormat(
            options?.TryGetValue(PdfToImageOptions.FormatOptionKey, out var fVal) == true ? fVal as string : null);

        int rawDpi = PdfToImageOptions.DefaultDpi;
        if (options != null && options.TryGetValue(PdfToImageOptions.DpiOptionKey, out var dVal))
        {
            if (dVal is int dInt)
            {
                rawDpi = dInt;
            }
            else if (int.TryParse(dVal?.ToString(), out int parsedDpi))
            {
                rawDpi = parsedDpi;
            }
        }
        int dpi = PdfToImageOptions.NormalizeDpi(rawDpi);

        string pagesSpec = options?.TryGetValue(PdfToImageOptions.PagesOptionKey, out var pVal) == true && pVal is string pStr && !string.IsNullOrWhiteSpace(pStr)
            ? pStr.Trim()
            : PdfToImageOptions.DefaultPages;

        bool stitch = false;
        if (options != null && options.TryGetValue(PdfToImageOptions.StitchOptionKey, out var sVal))
        {
            if (sVal is bool sBool)
            {
                stitch = sBool;
            }
            else if (bool.TryParse(sVal?.ToString(), out bool pBool))
            {
                stitch = pBool;
            }
        }

        string? password = options?.TryGetValue(PdfToImageOptions.PasswordOptionKey, out var pwdVal) == true
            ? pwdVal as string
            : null;

        int progressBase = ProgressCalculator.GetProgressBase(fileIndex);
        int totalProgressMax = ProgressCalculator.GetProgressMax(totalFiles);

        onProgress?.Invoke(progressBase + 10, totalProgressMax, "正在讀取 PDF 文件...");
        cancellationToken.ThrowIfCancellationRequested();

        using var inDoc = OpenInputDocument(fullPath, password);
        int pageCount = inDoc.PageCount;
        if (pageCount == 0)
        {
            throw new InvalidOperationException("PDF 檔案不包含任何頁面。");
        }

        List<int> targetPages = pagesSpec.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                pagesSpec.Equals("each", StringComparison.OrdinalIgnoreCase)
            ? Enumerable.Range(1, pageCount).ToList()
            : PdfSplitProcessor.ParsePageRange(pagesSpec, pageCount);

        if (targetPages.Count == 0)
        {
            throw new ArgumentException($"指定的分頁範圍「{pagesSpec}」無效或超出頁數範圍 (1-{pageCount})。");
        }

        string baseDir = Path.GetDirectoryName(Path.GetFullPath(targetOutputPath)) ??
                         Path.GetDirectoryName(fullPath) ??
                         ".";
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        string baseFileName = Path.GetFileNameWithoutExtension(fullPath);
        string ext = "." + format;

        if (stitch)
        {
            ProcessStitchMode(fullPath, targetPages, dpi, format, password, baseDir, baseFileName, targetOutputPath, progressBase, totalProgressMax, onProgress, cancellationToken);
        }
        else
        {
            ProcessPerPageMode(fullPath, targetPages, dpi, format, password, baseDir, baseFileName, targetOutputPath, progressBase, totalProgressMax, onProgress, cancellationToken);
        }

        onProgress?.Invoke(progressBase + 100, totalProgressMax, "PDF 轉圖片完成！");
    }

    private static void ProcessStitchMode(
        string fullPath,
        List<int> pages,
        int dpi,
        string format,
        string? password,
        string baseDir,
        string baseFileName,
        string targetOutputPath,
        int progressBase,
        int totalProgressMax,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var bitmaps = new List<Bitmap>();
        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pageNum = pages[i];
                int prog = progressBase + 10 + (int)((i + 1) * 70.0 / pages.Count);
                onProgress?.Invoke(prog, totalProgressMax, $"正在渲染第 {pageNum} 頁 ({i + 1}/{pages.Count})...");

                Bitmap bmp = RenderPageToBitmap(fullPath, pageNum, dpi, password)
                    ?? throw new InvalidOperationException($"無法渲染 PDF 第 {pageNum} 頁。");
                bitmaps.Add(bmp);
            }

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(progressBase + 85, totalProgressMax, "正在拼接直式長圖...");

            using var stitched = StitchBitmaps(bitmaps);
            string finalOutputPath = ResolveStitchedOutputPath(targetOutputPath, baseDir, baseFileName, format);
            SaveImage(stitched, finalOutputPath, format);
        }
        finally
        {
            foreach (var b in bitmaps)
            {
                try { b.Dispose(); } catch { }
            }
        }
    }

    private static void ProcessPerPageMode(
        string fullPath,
        List<int> pages,
        int dpi,
        string format,
        string? password,
        string baseDir,
        string baseFileName,
        string targetOutputPath,
        int progressBase,
        int totalProgressMax,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        string ext = "." + format;
        bool singlePage = pages.Count == 1;

        for (int i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pageNum = pages[i];
            int prog = progressBase + 10 + (int)((i + 1) * 85.0 / pages.Count);
            onProgress?.Invoke(prog, totalProgressMax, $"正在匯出第 {pageNum} 頁圖片 ({i + 1}/{pages.Count})...");

            using Bitmap bmp = RenderPageToBitmap(fullPath, pageNum, dpi, password)
                ?? throw new InvalidOperationException($"無法渲染 PDF 第 {pageNum} 頁。");

            string outPath;
            if (singlePage)
            {
                outPath = targetOutputPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                    ? targetOutputPath
                    : Path.Combine(baseDir, $"{baseFileName}{ext}");
            }
            else
            {
                outPath = Path.Combine(baseDir, $"{baseFileName}_page_{pageNum:D3}{ext}");
            }

            SaveImage(bmp, outPath, format);
        }
    }

    private static string ResolveStitchedOutputPath(string targetOutputPath, string baseDir, string baseFileName, string format)
    {
        string ext = "." + format;
        if (targetOutputPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            return targetOutputPath;
        }
        return Path.Combine(baseDir, $"{baseFileName}_stitched{ext}");
    }

    /// <summary>
    /// Renders a single 1-based page of a PDF document to a Bitmap at the requested DPI.
    /// Uses Windows.Data.Pdf when available, and falls back to PdfPig vector/raster overlay.
    /// </summary>
    public static Bitmap? RenderPageToBitmap(string filePath, int pageNumber1Based, int dpi, string? password = null)
    {
        Bitmap? winBmp = TryRenderWithWindowsPdf(filePath, pageNumber1Based, dpi, password);
        if (winBmp != null) return winBmp;

        int targetWidth = (int)Math.Max(120, Math.Round(595.0 * dpi / 72.0));
        string fontName = PdfPageThumbnailRenderer.GetTextFontName(CultureInfo.CurrentUICulture.Name);
        return PdfPageThumbnailRenderer.RenderPageFromFile(filePath, pageNumber1Based, targetWidth, fontName, password);
    }

    private static Bitmap? TryRenderWithWindowsPdf(string filePath, int pageNumber1Based, int dpi, string? password = null)
    {
        try
        {
            var file = Windows.Storage.StorageFile.GetFileFromPathAsync(filePath).AsTask().GetAwaiter().GetResult();
            var doc = string.IsNullOrEmpty(password)
                ? Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult()
                : Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, password).AsTask().GetAwaiter().GetResult();

            if (pageNumber1Based < 1 || pageNumber1Based > (int)doc.PageCount) return null;

            using var page = doc.GetPage((uint)(pageNumber1Based - 1));
            uint destWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * (dpi / 72.0)));
            uint destHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * (dpi / 72.0)));

            var renderOptions = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = destWidth,
                DestinationHeight = destHeight,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };

            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            page.RenderToStreamAsync(stream, renderOptions).AsTask().GetAwaiter().GetResult();

            using var ms = stream.AsStreamForRead();
            using var temp = new Bitmap(ms);
            return new Bitmap(temp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Stitches multiple page bitmaps vertically into a single bitmap.</summary>
    public static Bitmap StitchBitmaps(IReadOnlyList<Bitmap> bitmaps)
    {
        if (bitmaps == null || bitmaps.Count == 0)
        {
            throw new ArgumentException("No images provided for stitching.", nameof(bitmaps));
        }
        if (bitmaps.Count == 1)
        {
            return new Bitmap(bitmaps[0]);
        }

        int maxWidth = bitmaps.Max(b => b.Width);
        long totalHeight = bitmaps.Sum(b => (long)b.Height);

        if (totalHeight > MaxStitchedHeight)
        {
            double scale = (double)MaxStitchedHeight / totalHeight;
            int scaledWidth = Math.Max(1, (int)Math.Round(maxWidth * scale));
            var stitched = new Bitmap(scaledWidth, MaxStitchedHeight);
            using var g = Graphics.FromImage(stitched);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int currentY = 0;
            foreach (var bmp in bitmaps)
            {
                int h = (int)Math.Round(bmp.Height * scale);
                int w = (int)Math.Round(bmp.Width * scale);
                int x = (scaledWidth - w) / 2;
                g.DrawImage(bmp, x, currentY, w, h);
                currentY += h;
            }
            return stitched;
        }
        else
        {
            var stitched = new Bitmap(maxWidth, (int)totalHeight);
            using var g = Graphics.FromImage(stitched);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int currentY = 0;
            foreach (var bmp in bitmaps)
            {
                int x = (maxWidth - bmp.Width) / 2;
                g.DrawImage(bmp, x, currentY, bmp.Width, bmp.Height);
                currentY += bmp.Height;
            }
            return stitched;
        }
    }

    /// <summary>Saves a Bitmap to the target path according to the resolved format.</summary>
    public static void SaveImage(Bitmap bmp, string outputPath, string format)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string normalized = PdfToImageOptions.NormalizeFormat(format);
        switch (normalized)
        {
            case "jpg":
                SaveJpegWithQuality(bmp, outputPath, 90L);
                break;
            case "webp":
                WicImageHelper.SaveAsWebp(bmp, outputPath, 90);
                break;
            default:
                bmp.Save(outputPath, ImageFormat.Png);
                break;
        }
    }

    private static void SaveJpegWithQuality(Bitmap bmp, string outputPath, long quality)
    {
        ImageCodecInfo? encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

        if (encoder == null)
        {
            bmp.Save(outputPath, ImageFormat.Jpeg);
            return;
        }

        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bmp.Save(outputPath, encoder, encoderParams);
    }

    private static PdfDocument OpenInputDocument(string fullPath, string? password) =>
        string.IsNullOrEmpty(password)
            ? PdfReader.Open(fullPath, PdfDocumentOpenMode.Import)
            : PdfReader.Open(fullPath, password, PdfDocumentOpenMode.Import);
}
