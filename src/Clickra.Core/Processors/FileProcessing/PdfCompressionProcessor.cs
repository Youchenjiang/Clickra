using System;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core.Processors
{
    public class PdfCompressionProcessor : SingleFileProcessorBase
    {
        private readonly IPdfCompressionEngine _engine;

        public PdfCompressionProcessor()
            : this(new NativePdfCompressionEngine())
        {
        }

        public PdfCompressionProcessor(IPdfCompressionEngine engine)
        {
            _engine = engine;
        }

        protected override string GetOutputSuffix() => "_compressed.pdf";

        protected override void ProcessSingleFile(
            string fullPath,
            string targetOutputPath,
            int fileIndex,
            int totalFiles,
            Dictionary<string, object>? options,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            string levelValue = "";
            if (options != null &&
                options.TryGetValue("level", out var levelObj) &&
                levelObj is string levelStr)
            {
                levelValue = levelStr;
            }

            if (!PdfCompressionOptions.TryParseLevel(levelValue, out PdfCompressionLevel level))
                throw new ArgumentException($"Unsupported PDF compression level: {levelValue}", nameof(options));

            int progressBase = ProgressCalculator.GetProgressBase(fileIndex);
            int totalProgressMax = ProgressCalculator.GetProgressMax(totalFiles);

            _engine.Compress(
                fullPath,
                targetOutputPath,
                level,
                (curr, tot, msg) =>
                {
                    int progressPct = tot > 0 ? (int)(curr * 100.0 / tot) : curr;
                    onProgress?.Invoke(progressBase + progressPct, totalProgressMax, msg);
                },
                cancellationToken);
        }
    }
}
