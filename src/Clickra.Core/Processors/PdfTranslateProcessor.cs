using System;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core.Processors
{
    public class PdfTranslateProcessor : SingleFileProcessorBase
    {
        protected override string GetOutputSuffix() => "_translated.pdf";

        protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            string targetLang = "zh-TW";
            if (options != null && options.TryGetValue("targetLang", out var langObj) && langObj is string langStr)
            {
                targetLang = langStr;
            }

            FileProcessor.TranslatePdf(fullPath, targetOutputPath, targetLang, onProgress, cancellationToken);
        }
    }
}
