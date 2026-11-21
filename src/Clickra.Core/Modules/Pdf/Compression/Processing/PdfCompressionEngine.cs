using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Compression.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Application;
using Clickra.Core.Processors;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.Pdf.Compression.Processing;

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
        if (!System.IO.File.Exists(inputPath))
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
            try
            {
                if (PdfSharp.Fonts.GlobalFontSettings.FontResolver == null)
                    PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver();
            }
            catch { /* Ignored: font resolver setup is best-effort; default fonts still render. */ }

            var settings = PdfCompressionSettings.Parse(options);
            string language = ExecutionLocalizationOptions.ReadLanguage(options);

            onProgress?.Invoke(15, 100, ExecutionLocalizationOptions.Text(language, "core_progress_reading_pdf"));
            using var source = OpenSourceDocument(inputPath, options);
            using var output = new PdfDocument();

            CopyDocumentInfo(source, output, settings.Level);

            int pageCount = source.PageCount;
            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.AddPage(source.Pages[i]);

                int progress = 20 + (int)((i + 1) * 65.0 / pageCount);
                onProgress?.Invoke(progress, 100, ExecutionLocalizationOptions.Text(language, "core_compress_progress_page", ("current", i + 1), ("total", pageCount)));
            }

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(88, 100, ExecutionLocalizationOptions.Text(language, "core_compress_progress_tidying"));
            PdfStructuralCompressionOptimizer.Optimize(output, settings, inputPath);

            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(93, 100, ExecutionLocalizationOptions.Text(language, "core_progress_saving"));
            output.Save(tempOutput);

            if (!System.IO.File.Exists(tempOutput) || new FileInfo(tempOutput).Length <= 0)
                throw new InvalidOperationException("PDF optimization failed: output PDF file was not created.");

            long outputBytes = new FileInfo(tempOutput).Length;
            ReplaceOutput(tempOutput, outputPath);
            onProgress?.Invoke(100, 100, FormatCompressionSummary(inputBytes, outputBytes, language));
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
        if (System.IO.File.Exists(outputPath))
            System.IO.File.Delete(outputPath);

        System.IO.File.Move(tempOutput, outputPath);
    }

    public static string FormatCompressionSummary(long inputBytes, long outputBytes) =>
        FormatCompressionSummary(inputBytes, outputBytes, "");

    public static string FormatCompressionSummary(long inputBytes, long outputBytes, string language)
    {
        string before = FormatFileSize(inputBytes);
        string after = FormatFileSize(outputBytes);

        if (inputBytes <= 0)
            return ExecutionLocalizationOptions.Text(language, "core_compress_done_size_only", ("after", after));

        long savedBytes = inputBytes - outputBytes;
        if (savedBytes <= 0)
            return ExecutionLocalizationOptions.Text(language, "core_compress_done_not_smaller", ("before", before), ("after", after));

        // Whole percent; anything that did shrink reads as at least 1%.
        int savedPercent = Math.Max(1, (int)Math.Round(savedBytes * 100.0 / inputBytes, MidpointRounding.AwayFromZero));
        return ExecutionLocalizationOptions.Text(language, "core_compress_done", ("before", before), ("after", after), ("percent", savedPercent));
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

    private static PdfDocument OpenSourceDocument(string inputPath, Dictionary<string, object>? options)
    {
        try
        {
            return PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(options);
            throw new PdfEncryptedException("pdf_err_encrypted_compress", inputPath, Localization.T("pdf_err_encrypted_compress", lang), ex);
        }
    }
}
