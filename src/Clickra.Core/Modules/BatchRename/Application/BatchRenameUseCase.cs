using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Modules.BatchRename.Domain;
using Clickra.Core.Modules.BatchRename.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.BatchRename.Application;

/// <summary>Application service orchestrating batch file renaming.</summary>
public sealed class BatchRenameUseCase : ConversionUseCaseBase
{
    public const string CommandName = "batch-rename";

    public override string Command => CommandName;
    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.BatchRename };
    public override bool GeneratesHistoryTask => false;
    public override bool AcceptsAllFileExtensions => true;

    protected override string UseCaseName => nameof(BatchRenameUseCase);
    protected override string InputRequirementError => "At least one input file or directory is required for batch rename.";
    protected override string UnsupportedInputError(string path) => $"Unsupported input '{path}'.";

    public override ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"{UseCaseName} cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < 1)
            return ConversionValidationResult.Failure(InputRequirementError);

        return ConversionValidationResult.Success();
    }

    public override ConversionPlan Plan(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targetFiles = ResolveTargetFiles(request.InputFiles);
        var options = request.Options != null && request.Options.TryGetValue("options", out object? optObj) && optObj is BatchRenameOptions opt
            ? opt
            : new BatchRenameOptions();

        var normalizedOptions = new Dictionary<string, object>
        {
            ["options"] = options
        };

        return new ConversionPlan(
            Command,
            targetFiles,
            targetFiles,
            Capabilities,
            normalizedOptions,
            TrackTaskLifecycle: false);
    }

    public override async Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var options = plan.NormalizedOptions.TryGetValue("options", out object? optObj) && optObj is BatchRenameOptions opt
            ? opt
            : new BatchRenameOptions();

        var renamePlan = BatchRenameEngine.GeneratePlan(plan.Inputs, options);

        var renameProgress = progress is null ? null : new Progress<BatchRenameProgress>(p =>
        {
            progress.Report(new ConversionProgress(p.CompletedCount, p.TotalCount, p.CurrentFile));
        });

        var result = await BatchRenameExecutor.ExecuteAsync(renamePlan, renameProgress, cancellationToken);
        stopwatch.Stop();

        var outputFiles = result.RenamedFiles.Select(r => r.NewPath).ToList();
        return new ConversionResult(
            ConversionResultStatus.Succeeded,
            outputFiles,
            null,
            stopwatch.Elapsed,
            outputFiles.Count,
            plan.ExistingTaskId ?? Guid.NewGuid().ToString("N"));
    }

    /// <summary>Evaluates and produces a validated rename plan without modifying files.</summary>
    public static BatchRenamePlan Plan(IReadOnlyList<string> filePaths, BatchRenameOptions options)
    {
        return BatchRenameEngine.GeneratePlan(filePaths, options);
    }

    /// <summary>Executes the rename plan with cancellation and progress reporting.</summary>
    public static Task<BatchRenameResult> ExecuteAsync(
        BatchRenamePlan plan,
        IProgress<BatchRenameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return BatchRenameExecutor.ExecuteAsync(plan, progress, cancellationToken);
    }

    /// <summary>Expands a directory or file list into candidate file paths for renaming.</summary>
    public static List<string> ResolveTargetFiles(IEnumerable<string> inputs, bool recursive = false)
    {
        var result = new List<string>();

        foreach (string input in inputs)
        {
            if (File.Exists(input))
            {
                result.Add(Path.GetFullPath(input));
            }
            else if (Directory.Exists(input))
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                result.AddRange(Directory.EnumerateFiles(input, "*", searchOption).Select(Path.GetFullPath));
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
    }
}
