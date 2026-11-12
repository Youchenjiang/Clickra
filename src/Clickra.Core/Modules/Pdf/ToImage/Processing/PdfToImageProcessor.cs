using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.WebP;
using Clickra.Core.Modules.Pdf.Split.Processing;
using Clickra.Core.Modules.Pdf.ToImage.Application;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

#pragma warning disable CA1416 // Validate platform compatibility

namespace Clickra.Core.Modules.Pdf.ToImage.Processing;

/// <summary>
/// Processor that renders PDF pages into raster images (PNG, JPG, WEBP),
/// supporting per-page export and vertical long-image stitching.
/// </summary>
public class PdfToImageProcessor : SingleFileProcessorBase
{
    private const int MaxStitchedHeight = 32767;

    private sealed record ParsedOptions(
        string Format,
        int Dpi,
        string PagesSpec,
        bool Stitch,
        string? Password);

    private sealed record RenderContext(
        string FullPath,
        List<int> Pages,
        int Dpi,
        string Format,
        string? Password,
        string BaseDir,
        string BaseFileName,
        string TargetOutputPath,
        int ProgressBase,
        int TotalProgressMax,
        Action<int, int, string>? OnProgress,
        CancellationToken CancellationToken);

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
        var parsed = ParseOptions(options);

        int progressBase = ProgressCalculator.GetProgressBase(fileIndex);
        int totalProgressMax = ProgressCalculator.GetProgressMax(totalFiles);

        onProgress?.Invoke(progressBase + 10, totalProgressMax, "正在讀取 PDF 文件...");
        cancellationToken.ThrowIfCancellationRequested();

        using var inDoc = OpenInputDocument(fullPath, parsed.Password);
        int pageCount = inDoc.PageCount;
        if (pageCount == 0)
        {
            throw new InvalidOperationException("PDF 檔案不包含任何頁面。");
        }

        List<int> targetPages = ResolveTargetPages(parsed.PagesSpec, pageCount);

        string baseDir = Path.GetDirectoryName(Path.GetFullPath(targetOutputPath)) ??
                         Path.GetDirectoryName(fullPath) ??
                         ".";
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        string baseFileName = Path.GetFileNameWithoutExtension(fullPath);
        var ctx = new RenderContext(
            fullPath,
            targetPages,
            parsed.Dpi,
            parsed.Format,
            parsed.Password,
            baseDir,
            baseFileName,
            targetOutputPath,
            progressBase,
            totalProgressMax,
            onProgress,
            cancellationToken);

        if (parsed.Stitch)
        {
            ProcessStitchMode(ctx);
        }
        else
        {
            ProcessPerPageMode(ctx);
        }

        onProgress?.Invoke(progressBase + 100, totalProgressMax, "PDF 轉圖片完成！");
    }

    private static ParsedOptions ParseOptions(Dictionary<string, object>? options)
    {
        string format = PdfToImageOptions.NormalizeFormat(
            options?.TryGetValue(PdfToImageOptions.FormatOptionKey, out var fVal) == true ? fVal as string : null);

        int rawDpi = PdfToImageOptions.DefaultDpi;
        if (options is not null && options.TryGetValue(PdfToImageOptions.DpiOptionKey, out var dVal))
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
        if (options is not null && options.TryGetValue(PdfToImageOptions.StitchOptionKey, out var sVal))
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

        return new ParsedOptions(format, dpi, pagesSpec, stitch, password);
    }

    private static List<int> ResolveTargetPages(string pagesSpec, int pageCount)
    {
        List<int> targetPages = pagesSpec.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                pagesSpec.Equals("each", StringComparison.OrdinalIgnoreCase)
            ? Enumerable.Range(1, pageCount).ToList()
            : PdfSplitProcessor.ParsePageRange(pagesSpec, pageCount);

        if (targetPages.Count == 0)
        {
            throw new ArgumentException($"指定的分頁範圍「{pagesSpec}」無效或超出頁數範圍 (1-{pageCount})。");
        }
        return targetPages;
    }

    private static void ProcessStitchMode(RenderContext ctx)
    {
        var bitmaps = new List<Bitmap>();
        try
        {
            for (int i = 0; i < ctx.Pages.Count; i++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                int pageNum = ctx.Pages[i];
                int prog = ctx.ProgressBase + 10 + (int)((i + 1) * 70.0 / ctx.Pages.Count);
                ctx.OnProgress?.Invoke(prog, ctx.TotalProgressMax, $"正在渲染第 {pageNum} 頁 ({i + 1}/{ctx.Pages.Count})...");

                Bitmap bmp = RenderPageToBitmap(ctx.FullPath, pageNum, ctx.Dpi, ctx.Password)
                    ?? throw new InvalidOperationException($"無法渲染 PDF 第 {pageNum} 頁。");
                bitmaps.Add(bmp);
            }

            ctx.CancellationToken.ThrowIfCancellationRequested();
            ctx.OnProgress?.Invoke(ctx.ProgressBase + 85, ctx.TotalProgressMax, "正在拼接直式長圖...");

            using var stitched = StitchBitmaps(bitmaps);
            string finalOutputPath = ResolveStitchedOutputPath(ctx.TargetOutputPath, ctx.BaseDir, ctx.BaseFileName, ctx.Format);
            SaveImage(stitched, finalOutputPath, ctx.Format);
        }
        finally
        {
            foreach (var b in bitmaps)
            {
                try
                {
                    b.Dispose();
                }
                catch (Exception) // skipcq: CS-R1008
                {
                    // Best-effort bitmap disposal during cleanup
                }
            }
        }
    }

    private static void ProcessPerPageMode(RenderContext ctx)
    {
        string ext = "." + ctx.Format;
        bool singlePage = ctx.Pages.Count == 1;

        for (int i = 0; i < ctx.Pages.Count; i++)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            int pageNum = ctx.Pages[i];
            int prog = ctx.ProgressBase + 10 + (int)((i + 1) * 85.0 / ctx.Pages.Count);
            ctx.OnProgress?.Invoke(prog, ctx.TotalProgressMax, $"正在匯出第 {pageNum} 頁圖片 ({i + 1}/{ctx.Pages.Count})...");

            using Bitmap bmp = RenderPageToBitmap(ctx.FullPath, pageNum, ctx.Dpi, ctx.Password)
                ?? throw new InvalidOperationException($"無法渲染 PDF 第 {pageNum} 頁。");

            string outPath = singlePage
                ? ResolveSinglePageOutputPath(ctx, ext)
                : Path.Combine(ctx.BaseDir, $"{ctx.BaseFileName}_page_{pageNum:D3}{ext}");

            SaveImage(bmp, outPath, ctx.Format);
        }
    }

    private static string ResolveSinglePageOutputPath(RenderContext ctx, string ext)
    {
        if (ctx.TargetOutputPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            return ctx.TargetOutputPath;
        }
        return Path.Combine(ctx.BaseDir, $"{ctx.BaseFileName}{ext}");
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
#if WINDOWS
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
        catch (Exception) // skipcq: CS-R1008
        {
            // Fall back to alternative renderer when Windows.Data.Pdf cannot process the file
            return null;
        }
#else
        _ = filePath;
        _ = pageNumber1Based;
        _ = dpi;
        _ = password;
        return null;
#endif
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
        double scale = totalHeight > MaxStitchedHeight ? (double)MaxStitchedHeight / totalHeight : 1.0;
        int targetWidth = scale < 1.0 ? Math.Max(1, (int)Math.Round(maxWidth * scale)) : maxWidth;
        int targetHeight = scale < 1.0 ? MaxStitchedHeight : (int)totalHeight;

        var stitched = new Bitmap(targetWidth, targetHeight);
        using var g = Graphics.FromImage(stitched);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int currentY = 0;
        foreach (var bmp in bitmaps)
        {
            int h = scale < 1.0 ? (int)Math.Round(bmp.Height * scale) : bmp.Height;
            int w = scale < 1.0 ? (int)Math.Round(bmp.Width * scale) : bmp.Width;
            int x = (targetWidth - w) / 2;
            g.DrawImage(bmp, x, currentY, w, h);
            currentY += h;
        }
        return stitched;
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
                SaveWebpWithQuality(bmp, outputPath, 90);
                break;
            default:
                bmp.Save(outputPath, ImageFormat.Png);
                break;
        }
    }

    private static void SaveWebpWithQuality(Bitmap image, string outputPath, int quality)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var output = System.IO.File.Create(outputPath);
        var encoder = new SimpleEncoder();
        encoder.Encode(image, output, Math.Clamp(quality, 1, 100));
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
