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

        var targetFiles = ResolveTargetFiles(request.InputFiles);
        if (targetFiles.Count == 0)
            return ConversionValidationResult.Failure("No target files found for batch rename.");

        return ConversionValidationResult.Success();
    }

    public override ConversionPlan Plan(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var targetFiles = ResolveTargetFiles(request.InputFiles);
        if (targetFiles.Count == 0)
            throw new InvalidOperationException("No target files found for batch rename.");

        BatchRenamePlan? renamePlan = null;
        if (request.Options != null && request.Options.TryGetValue("plan", out object? planObj) && planObj is BatchRenamePlan p)
        {
            renamePlan = p;
        }

        var options = request.Options != null && request.Options.TryGetValue("options", out object? optObj) && optObj is BatchRenameOptions opt
            ? opt
            : new BatchRenameOptions();

        renamePlan ??= BatchRenameEngine.GeneratePlan(targetFiles, options);

        var normalizedOptions = new Dictionary<string, object>
        {
            ["options"] = options,
            ["plan"] = renamePlan
        };

        return new ConversionPlan(
            Command,
            targetFiles,
            renamePlan.Items.Select(i => i.NewPath).ToList(),
            Capabilities,
            normalizedOptions,
            ExistingTaskId: request.ExistingTaskId,
            BestEffortTaskPersistence: request.BestEffortTaskPersistence,
            TrackTaskLifecycle: request.TrackTaskLifecycle,
            TaskLifecycleRepository: request.TaskLifecycleRepository);
    }

    public override async Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(interaction);
        var (stopwatch, lifecycle) = StartExecution(plan, interaction, true, "", observer);

        try
        {
            BatchRenamePlan renamePlan;
            if (plan.NormalizedOptions.TryGetValue("plan", out object? planObj) && planObj is BatchRenamePlan p)
            {
                renamePlan = p;
            }
            else
            {
                var options = plan.NormalizedOptions.TryGetValue("options", out object? optObj) && optObj is BatchRenameOptions opt
                    ? opt
                    : new BatchRenameOptions();
                renamePlan = BatchRenameEngine.GeneratePlan(plan.Inputs, options);
            }

            if (renamePlan.Items.All(i => i.Status == RenameItemStatus.Unchanged))
            {
                stopwatch.Stop();
                lifecycle?.CompleteSuccess(outputPath: "");
                return new ConversionResult(
                    ConversionResultStatus.Succeeded,
                    [],
                    null,
                    stopwatch.Elapsed,
                    0,
                    lifecycle?.TaskId ?? plan.ExistingTaskId ?? Guid.NewGuid().ToString("N"));
            }

            var renameProgress = progress is null ? null : new Progress<BatchRenameProgress>(p =>
            {
                progress.Report(new ConversionProgress(p.CompletedCount * 100, p.TotalCount * 100, p.CurrentFile));
            });

            var result = await BatchRenameExecutor.ExecuteAsync(renamePlan, renameProgress, cancellationToken);
            stopwatch.Stop();

            var outputFiles = result.RenamedFiles.Select(r => r.NewPath).ToList();
            if (!result.Succeeded)
            {
                var error = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Batch rename execution failed.";
                lifecycle?.CompleteFailure(error, outputPath: outputFiles.FirstOrDefault() ?? "");
                return new ConversionResult(
                    ConversionResultStatus.Failed,
                    outputFiles,
                    error,
                    stopwatch.Elapsed,
                    outputFiles.Count,
                    lifecycle?.TaskId ?? plan.ExistingTaskId ?? Guid.NewGuid().ToString("N"));
            }

            lifecycle?.CompleteSuccess(outputPath: outputFiles.FirstOrDefault() ?? "");
            return new ConversionResult(
                ConversionResultStatus.Succeeded,
                outputFiles,
                null,
                stopwatch.Elapsed,
                outputFiles.Count,
                lifecycle?.TaskId ?? plan.ExistingTaskId ?? Guid.NewGuid().ToString("N"));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            lifecycle?.CompleteFailure("Canceled", outputPath: "");
            return new ConversionResult(
                ConversionResultStatus.Canceled,
                [],
                null,
                stopwatch.Elapsed,
                0,
                lifecycle?.TaskId ?? plan.ExistingTaskId ?? Guid.NewGuid().ToString("N"));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            lifecycle?.CompleteFailure(ex.Message, outputPath: "");
            return new ConversionResult(
                ConversionResultStatus.Failed,
                [],
                ex.Message,
                stopwatch.Elapsed,
                0,
                lifecycle?.TaskId ?? plan.ExistingTaskId ?? Guid.NewGuid().ToString("N"));
        }
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
