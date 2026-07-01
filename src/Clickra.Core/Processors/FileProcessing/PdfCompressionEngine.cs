using System;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors
{
    public enum PdfCompressionLevel
    {
        Small,
        Balanced,
        HighQuality
    }

    public interface IPdfCompressionEngine
    {
        void Compress(
            string inputPath,
            string outputPath,
            PdfCompressionLevel level,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default);
    }

    public static class PdfCompressionOptions
    {
        public static bool TryParseLevel(string? value, out PdfCompressionLevel level)
        {
            level = PdfCompressionLevel.Balanced;
            if (string.IsNullOrWhiteSpace(value))
                return true;

            switch (value.Trim().ToLowerInvariant())
            {
                case "small":
                case "screen":
                case "compact":
                case "小檔":
                case "小文件":
                    level = PdfCompressionLevel.Small;
                    return true;
                case "balanced":
                case "ebook":
                case "平衡":
                    level = PdfCompressionLevel.Balanced;
                    return true;
                case "high":
                case "highquality":
                case "high-quality":
                case "printer":
                case "quality":
                case "高品質":
                    level = PdfCompressionLevel.HighQuality;
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class NativePdfCompressionEngine : IPdfCompressionEngine
    {
        public void Compress(
            string inputPath,
            string outputPath,
            PdfCompressionLevel level,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input PDF file was not found.", inputPath);

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
                onProgress?.Invoke(15, 100, "正在讀取 PDF...");
                using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();

                CopyDocumentInfo(source, output, level);

                int pageCount = source.PageCount;
                for (int i = 0; i < pageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.AddPage(source.Pages[i]);

                    int progress = pageCount > 0 ? 20 + (int)((i + 1) * 65.0 / pageCount) : 85;
                    onProgress?.Invoke(progress, 100, $"正在最佳化第 {i + 1}/{pageCount} 頁...");
                }

                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(90, 100, "正在重新封裝 PDF...");
                output.Save(tempOutput);

                if (!File.Exists(tempOutput) || new FileInfo(tempOutput).Length <= 0)
                    throw new InvalidOperationException("PDF optimization failed: output PDF file was not created.");

                ReplaceOutput(tempOutput, outputPath);
                onProgress?.Invoke(100, 100, "PDF 最佳化完成。");
            }
            finally
            {
                try
                {
                    string? tempDir = Path.GetDirectoryName(tempOutput);
                    if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
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
    }
}
