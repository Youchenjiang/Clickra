using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clickra.Core.Modules.Markdown.Processing;
using Clickra.Core.Modules.PdfCompression.Processing;
using Clickra.Core.Modules.PdfTranslation.Execution;
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
            new PdfMergeProcessor().Process(files, outputPath, PrepareOptions(null, new[] { outputPath }), onProgress, cancellationToken);

        /// <summary>Removes the password protection from a PDF.</summary>
        /// <param name="inputPath">Path of the password-protected input PDF.</param>
        /// <param name="outputPath">Path of the decrypted output PDF.</param>
        /// <param name="password">The document open password, when known.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
        public static void DecryptPdf(string inputPath, string outputPath, string password, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            DecryptPdf(
                inputPath,
                outputPath,
                password,
                ClickraStorage.GetSetting(ClickraSettings.Language),
                onProgress,
                cancellationToken);

        public static void DecryptPdf(string inputPath, string outputPath, string password, string language, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfDecryptProcessor();
            var options = new Dictionary<string, object>
            {
                [ExecutionLocalizationOptions.LanguageKey] = language
            };
            if (!string.IsNullOrEmpty(password)) options["password"] = password;
            processor.Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);
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
            processor.Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);
        }

        /// <summary>Converts a list of image files into a single PDF document.</summary>
        /// <param name="files">Input image paths.</param>
        /// <param name="outputPath">Path of the output PDF.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertImagesToPdf(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new ImageToPdfProcessor().Process(files, outputPath, PrepareOptions(null, new[] { outputPath }), onProgress, cancellationToken);

        /// <summary>Converts one Markdown document using explicit one-shot presentation options.</summary>
        public static void ConvertMarkdownToPdf(string inputPath, string outputPath, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new MarkdownToPdfProcessor().Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);

        /// <summary>Converts one Markdown document to an editable Word DOCX using the shared Markdown template.</summary>
        public static void ConvertMarkdownToWord(string inputPath, string outputPath, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new MarkdownToWordProcessor().Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);

        /// <summary>Stitches multiple images vertically into a single image file.</summary>
        /// <param name="files">Input image paths, in stitch order.</param>
        /// <param name="outputPath">Path of the stitched output image.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
        public static void StitchImages(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new ImageStitchProcessor().Process(files, outputPath, PrepareOptions(null, new[] { outputPath }), onProgress, cancellationToken);

        /// <summary>Converts an image file to a target format (png / jpg / webp / gif / heic).</summary>
        /// <param name="inputPath">Path of the source image.</param>
        /// <param name="outputPath">Path of the converted output image.</param>
        /// <param name="format">Target format name ("png", "jpg", "webp", "gif" or "heic").</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertImageFormat(string inputPath, string outputPath, string format, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            ConvertImageFormat(
                inputPath,
                outputPath,
                format,
                ClickraStorage.GetSetting(ClickraSettings.Language),
                onProgress,
                cancellationToken);

        public static void ConvertImageFormat(string inputPath, string outputPath, string format, string language, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new ImageFormatConvertProcessor();
            var options = new Dictionary<string, object>
            {
                ["format"] = format,
                [ExecutionLocalizationOptions.LanguageKey] = language
            };
            processor.Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);
        }

        /// <summary>Compresses an image using the shared image-compression option dictionary.</summary>
        public static void CompressImage(string inputPath, string outputPath, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new ImageCompressionProcessor();
            processor.Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);
        }

        /// <summary>Converts PowerPoint files to PDF using explicit Office execution options.</summary>
        /// <param name="files">Input .ppt/.pptx paths.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertPptToPdf(List<string> files, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new PptToPdfProcessor().Process(files, null, PrepareOptions(options, ResolveOfficeOutputs(files, options)), onProgress, cancellationToken);

        /// <summary>Converts Word documents to PDF using explicit Office execution options.</summary>
        /// <param name="files">Input .doc/.docx paths.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertWordToPdf(List<string> files, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            new WordToPdfProcessor().Process(files, null, PrepareOptions(options, ResolveOfficeOutputs(files, options)), onProgress, cancellationToken);

        /// <summary>Converts Excel workbooks to PDF using explicit Office execution options.</summary>
        /// <param name="files">Input .xlsx/.xls paths.</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the conversion.</param>
        public static void ConvertExcelToPdf(List<string> files, Dictionary<string, object> options, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new ExcelToPdfProcessor();
            processor.Process(files, null, PrepareOptions(options, ResolveOfficeOutputs(files, options)), onProgress, cancellationToken);
        }

        /// <summary>Translates a PDF into <paramref name="targetLang"/> while preserving layout.</summary>
        /// <param name="inputPath">Path of the source PDF.</param>
        /// <param name="outputPath">Path of the translated output PDF.</param>
        /// <param name="targetLang">Target language key (e.g. "en", "ja").</param>
        /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
        /// <param name="cancellationToken">Cancellation token to abort the translation.</param>
        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default) =>
            TranslatePdf(
                inputPath,
                outputPath,
                targetLang,
                ClickraStorage.GetSetting(ClickraSettings.Language),
                ClickraStorage.GetDataDir(),
                onProgress,
                cancellationToken);

        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, string language, string dataDirectory, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfTranslateProcessor();
            var options = new Dictionary<string, object>
            {
                ["targetLang"] = targetLang,
                [ExecutionLocalizationOptions.LanguageKey] = language,
                [ProcessorExecutionOptions.DataDirectoryKey] = dataDirectory
            };
            processor.Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);
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
        public static void SplitPdf(string inputPath, string outputPath, string pages, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfSplitProcessor();
            var options = new Dictionary<string, object> { { "pages", pages } };
            processor.Process(new List<string> { inputPath }, outputPath, PrepareOptions(options, new[] { outputPath }), onProgress, cancellationToken);
        }

        private static Dictionary<string, object> PrepareOptions(
            Dictionary<string, object>? options,
            IReadOnlyList<string>? outputPaths = null)
        {
            var prepared = options is null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(options, StringComparer.Ordinal);
            prepared[ProcessorExecutionOptions.FileStartingCallbackKey] =
                (Action<int>)(index => ClickraStorage.SetActiveRecordIndex(index));
            // Direct facade callers that did not snapshot a language keep the previous behavior of
            // localizing with the configured UI language; application use cases always provide one.
            if (!prepared.ContainsKey(ExecutionLocalizationOptions.LanguageKey))
                prepared[ExecutionLocalizationOptions.LanguageKey] = ClickraStorage.GetSetting(ClickraSettings.Language);
            if (outputPaths is not null)
                prepared[ProcessorExecutionOptions.OutputPathsKey] = outputPaths;
            return prepared;
        }

        private static IReadOnlyList<string> ResolveOfficeOutputs(
            IReadOnlyList<string> files,
            IReadOnlyDictionary<string, object> options)
        {
            if (options.TryGetValue(ProcessorExecutionOptions.OutputPathsKey, out object? explicitOutputs)
                && explicitOutputs is IReadOnlyList<string> outputPaths)
            {
                return outputPaths;
            }

            return files
                .Select(input => Path.Combine(
                    ClickraStorage.GetOutputDir(input),
                    Path.GetFileNameWithoutExtension(input) + ".pdf"))
                .ToArray();
        }

    }
}
