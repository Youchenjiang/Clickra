using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Markdig;
using Markdig.Syntax;

namespace Clickra.Core.Processors;

/// <summary>Common validation and output state for one-input Markdown conversions.</summary>
public abstract class MarkdownSingleFileProcessorBase : MultiFileProcessorBase
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>The validated output path for the current conversion.</summary>
    protected string OutputPath { get; private set; } = "";

    /// <summary>The execution language snapshotted by the application owner for progress text.</summary>
    protected string Language { get; private set; } = ClickraSettings.DefaultEmpty;

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
        Language = ExecutionLocalizationOptions.ReadLanguage(options);
        base.Process(files, outputPath, options, onProgress, cancellationToken);
    }

    /// <summary>Validates and parses the single Markdown input using the shared Markdig pipeline.</summary>
    protected static MarkdownDocument ParseMarkdownFile(
        string filePath,
        string progressKey,
        string language,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath)) throw new FileNotFoundException("Markdown file not found", filePath);

        onProgress?.Invoke(15, 100, ExecutionLocalizationOptions.Text(language, progressKey, Path.GetFileName(filePath)));
        MarkdownDocument document = Markdown.Parse(File.ReadAllText(filePath), Pipeline);
        cancellationToken.ThrowIfCancellationRequested();
        return document;
    }
}
