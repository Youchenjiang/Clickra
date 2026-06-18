using System;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core.Processors
{
    public class WordToPdfProcessor : SingleFileProcessorBase
    {
        protected override string GetOutputSuffix() => ".pdf";

        protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            PowerShellHelper.ExportOfficeToPdf("Word", fullPath, targetOutputPath, fileIndex, totalFiles, onProgress, cancellationToken);
        }
    }
}
