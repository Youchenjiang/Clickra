using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors;

public interface IPdfCompressionEngine
{
    void Compress(
        string inputPath,
        string outputPath,
        Dictionary<string, object>? options,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default);
}

public sealed class NativePdfCompressionEngine : IPdfCompressionEngine
{
    public void Compress(
        string inputPath,
        string outputPath,
        Dictionary<string, object>? options,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input PDF file was not found.", inputPath);

        long inputBytes = new FileInfo(inputPath).Length;
        string? outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        string tempOutput = Path.Combine(
            Path.GetTempPath(),
            "ClickraPdfCompression",
            Guid.NewGuid().ToString("N"),
            Path.GetFileName(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(tempOutput)!);

        try
        {
            var settings = PdfCompressionSettings.Parse(options);

            onProgress?.Invoke(15, 100, "正在讀取 PDF...");
            using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();

            CopyDocumentInfo(source, output, settings.Level);

            int pageCount = source.PageCount;
            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.AddPage(source.Pages[i]);

                int progress = 20 + (int)((i + 1) * 65.0 / pageCount);
                onProgress?.Invoke(progress, 100, $"正在最佳化第 {i + 1}/{pageCount} 頁...");
            }

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(88, 100, "正在最佳化文字與字型資料...");
            PdfStructuralCompressionOptimizer.Optimize(output, settings, inputPath);

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(93, 100, "正在重新封裝 PDF...");
            output.Save(tempOutput);

            if (!File.Exists(tempOutput) || new FileInfo(tempOutput).Length <= 0)
                throw new InvalidOperationException("PDF optimization failed: output PDF file was not created.");

            long outputBytes = new FileInfo(tempOutput).Length;
            ReplaceOutput(tempOutput, outputPath);
            onProgress?.Invoke(100, 100, FormatCompressionSummary(inputBytes, outputBytes));
        }
        finally
        {
            try
            {
                string? tempDir = Path.GetDirectoryName(tempOutput);
                if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; ignore errors deleting temp directory
            }
        }
    }

    private static void CopyDocumentInfo(PdfDocument source, PdfDocument output, PdfCompressionLevel level)
    {
        if (level == PdfCompressionLevel.Small)
            return;

        output.Info.Title = source.Info.Title;
        output.Info.Subject = source.Info.Subject;
        output.Info.Author = source.Info.Author;
        output.Info.Keywords = source.Info.Keywords;
    }

    private static void ReplaceOutput(string tempOutput, string outputPath)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        File.Move(tempOutput, outputPath);
    }

    public static string FormatCompressionSummary(long inputBytes, long outputBytes)
    {
        string before = FormatFileSize(inputBytes);
        string after = FormatFileSize(outputBytes);

        if (inputBytes <= 0)
            return $"PDF 最佳化完成：{after}。";

        long savedBytes = inputBytes - outputBytes;
        if (savedBytes <= 0)
            return $"PDF 已接近最佳化：{before} -> {after}，檔案大小未明顯下降。";

        double savedPercent = savedBytes * 100.0 / inputBytes;
        string percentStr = savedPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"PDF 最佳化完成：{before} -> {after}，減少 {percentStr}%。";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = Math.Max(0, bytes);
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        string valueStr = unitIndex == 0
            ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return $"{valueStr} {units[unitIndex]}";
    }
}
