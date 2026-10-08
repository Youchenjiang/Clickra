using System;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core.Processors;

/// <summary>Common validation and output state for one-input Markdown conversions.</summary>
public abstract class MarkdownSingleFileProcessorBase : MultiFileProcessorBase
{
    /// <summary>The validated output path for the current conversion.</summary>
    protected string OutputPath { get; private set; } = "";

    /// <summary>User-facing target format name used in validation errors.</summary>
    protected abstract string TargetFormatName { get; }

    /// <inheritdoc />
    public override void Process(
        List<string> files,
        string? outputPath,
        Dictionary<string, object>? options = null,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (files.Count != 1)
            throw new ArgumentException($"Markdown to {TargetFormatName} converts one input file per output.", nameof(files));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException($"Output path is required for Markdown to {TargetFormatName} conversion.", nameof(outputPath));

        OutputPath = outputPath;
        base.Process(files, outputPath, options, onProgress, cancellationToken);
    }
}
