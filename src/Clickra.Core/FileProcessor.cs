using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Processors;

namespace Clickra.Core
{
    /// <summary>High-level entry points that dispatch conversion and PDF operations to the
    /// corresponding processors, reporting progress through a callback.</summary>
    public static class FileProcessor
    {
        /// <summary>Merges multiple PDF files into one document.</summary>
        /// <param name="files">Input PDF paths, in merge order.</param>
        /// <param name="outputPath">Path of the merged output PDF.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the merge.</param>
        public static void MergePdfs(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new PdfMergeProcessor().Process(files, outputPath, null, onProgress, cancellationToken);

        /// <summary>Removes the password protection from a PDF.</summary>
        /// <param name="inputPath">Path of the password-protected input PDF.</param>
        /// <param name="outputPath">Path of the decrypted output PDF.</param>
        /// <param name="password">The document open password, when known.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
        public static void DecryptPdf(string inputPath, string outputPath, string password = "", Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfDecryptProcessor();
            var options = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(password)) options["password"] = password;
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        /// <summary>Compresses a PDF with a named quality level ("small", "balanced" or "high").</summary>
        /// <param name="inputPath">Path of the input PDF.</param>
        /// <param name="outputPath">Path of the compressed output PDF.</param>
        /// <param name="level">Compression level name; defaults to "balanced".</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the compression.</param>
        public static void CompressPdf(string inputPath, string outputPath, string level = "balanced", Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfCompressionProcessor();
            var options = new Dictionary<string, object> { { "level", level } };
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        /// <summary>Compresses a PDF using an explicit options dictionary (target DPI, JPEG
        /// quality, font stripping and content minification).</summary>
        /// <param name="inputPath">Path of the input PDF.</param>
        /// <param name="outputPath">Path of the compressed output PDF.</param>
        /// <param name="options">Compression options keyed by processor option name.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the compression.</param>
        public static void CompressPdf(string inputPath, string outputPath, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfCompressionProcessor();
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        /// <summary>Converts a list of image files into a single PDF document.</summary>
        /// <param name="files">Input image paths.</param>
        /// <param name="outputPath">Path of the output PDF.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertImagesToPdf(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new ImageToPdfProcessor().Process(files, outputPath, null, onProgress, cancellationToken);

        /// <summary>Stitches multiple images vertically into a single image file.</summary>
        /// <param name="files">Input image paths, in stitch order.</param>
        /// <param name="outputPath">Path of the stitched output image.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
        public static void StitchImages(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new ImageStitchProcessor().Process(files, outputPath, null, onProgress, cancellationToken);

        /// <summary>Converts PowerPoint files to PDF via the LibreOffice engine.</summary>
        /// <param name="files">Input .ppt/.pptx paths.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertPptToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new PptToPdfProcessor().Process(files, null, null, onProgress, cancellationToken);

        /// <summary>Converts Word documents to PDF via the LibreOffice engine.</summary>
        /// <param name="files">Input .doc/.docx paths.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertWordToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new WordToPdfProcessor().Process(files, null, null, onProgress, cancellationToken);

        /// <summary>Converts Excel workbooks to PDF via the LibreOffice engine.</summary>
        /// <param name="files">Input .xlsx/.xls paths.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertExcelToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new ExcelToPdfProcessor();
            processor.Process(files, null, null, onProgress, cancellationToken);
        }

        /// <summary>Translates a PDF into <paramref name="targetLang"/> while preserving layout.</summary>
        /// <param name="inputPath">Path of the source PDF.</param>
        /// <param name="outputPath">Path of the translated output PDF.</param>
        /// <param name="targetLang">Target language key (e.g. "en", "ja").</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the translation.</param>
        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfTranslateProcessor();
            var options = new Dictionary<string, object> { { "targetLang", targetLang } };
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        /// <summary>
        /// Splits a PDF into segment documents. <paramref name="pages"/> accepts a
        /// range spec like "1-5, 8", multiple "1-3; 5; 7-9" segments, or "all" to
        /// write one output file per page.
        /// </summary>
        /// <param name="inputPath">Path of the input PDF.</param>
        /// <param name="outputPath">Path of the single-segment output; a base name for
        /// multi-segment and split-each outputs.</param>
        /// <param name="pages">Page-range specification ("all" splits every page).</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the split.</param>
        public static void SplitPdf(string inputPath, string outputPath, string pages = "all", Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfSplitProcessor();
            var options = new Dictionary<string, object> { { "pages", pages } };
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        /// <summary>Returns the page count of the PDF at <paramref name="inputPath"/>, or 0 when it cannot be opened.</summary>
        /// <param name="inputPath">Path of the PDF file.</param>
        public static int GetPdfPageCount(string inputPath) => PdfSplitProcessor.GetPageCount(inputPath);
    }
}
