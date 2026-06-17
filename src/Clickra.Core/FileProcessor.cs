using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Processors;

namespace Clickra.Core
{
    public static class FileProcessor
    {
        public static void MergePdfs(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfMergeProcessor();
            processor.Process(files, outputPath, null, onProgress, cancellationToken);
        }

        public static void DecryptPdf(string inputPath, string outputPath, string password = "", Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfDecryptProcessor();
            var options = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(password)) options["password"] = password;
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }

        public static void ImagesToPdf(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new ImageToPdfProcessor();
            processor.Process(files, outputPath, null, onProgress, cancellationToken);
        }

        public static void StitchImages(List<string> files, string outputPath, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new ImageStitchProcessor();
            processor.Process(files, outputPath, null, onProgress, cancellationToken);
        }

        public static void ConvertPptToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PptToPdfProcessor();
            processor.Process(files, null, null, onProgress, cancellationToken);
        }

        public static void ConvertWordToPdf(List<string> files, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new WordToPdfProcessor();
            processor.Process(files, null, null, onProgress, cancellationToken);
        }

        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var processor = new PdfTranslateProcessor();
            var options = new Dictionary<string, object> { { "targetLang", targetLang } };
            processor.Process(new List<string> { inputPath }, outputPath, options, onProgress, cancellationToken);
        }
    }
}
