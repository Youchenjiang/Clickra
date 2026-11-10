using System;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Doc.Excel.Processing;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Modules.Doc.Shared.Infrastructure;

public class ExcelToPdfProcessor : SingleFileProcessorBase
{
    protected override string GetOutputSuffix() => ".pdf";

    protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
    {
        PowerShellHelper.ExportOfficeToPdf(
            new OfficeConversionRequest("Excel", fullPath, targetOutputPath, fileIndex, totalFiles),
            OfficeConversionOptions.Read(options),
            onProgress,
            cancellationToken);
    }
}
