using System;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Modules.Img.ToPdf.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Img.Merge.Processing;

/// <summary>Processor for merging multiple images into a single PDF output.</summary>
public class ImageMergeProcessor : MultiFileProcessorBase
{
    private readonly ImageToPdfProcessor _innerProcessor = new();

    public override void Process(
        List<string> files,
        string? outputPath,
        Dictionary<string, object>? options = null,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        _innerProcessor.Process(files, outputPath, options, onProgress, cancellationToken);
    }

    protected override void ProcessFile(
        string filePath,
        int fileIndex,
        int totalFiles,
        Dictionary<string, object>? options,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        // Handled directly by Process delegation
    }
}
