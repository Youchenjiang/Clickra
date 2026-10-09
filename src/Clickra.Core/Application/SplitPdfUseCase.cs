using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for splitting PDF files.</summary>
public sealed class SplitPdfUseCase : IConversionUseCase
{
    public const string CommandName = "split-pdf";
    public const string PagesOptionKey = "pages";
    public const string PromptPages = "prompt";

    public string Command => CommandName;

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"SplitPdfUseCase cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(CommandName))
            return ConversionValidationResult.Failure("At least one PDF file is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(CommandName);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ConversionValidationResult.Success()
            : ConversionValidationResult.Failure($"Unsupported split input '{invalid}'.");
    }

    public ConversionPlan Plan(ConversionRequest request)
    {
        ConversionValidationResult validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var inputs = request.InputFiles.ToList();
        string? outputOverride = string.IsNullOrWhiteSpace(request.OutputOverride)
            ? null
            : Path.GetFullPath(request.OutputOverride);
        List<string> outputs = inputs
            .Select(input => Path.Combine(
                outputOverride ?? ClickraStorage.GetOutputDir(input),
                Path.GetFileNameWithoutExtension(input) + "_split.pdf"))
            .ToList();

        string pages = PromptPages;
        if (request.Options is not null &&
            request.Options.TryGetValue(PagesOptionKey, out object? pagesValue) &&
            pagesValue is string suppliedPages &&
            !string.IsNullOrWhiteSpace(suppliedPages))
        {
            pages = suppliedPages.Trim();
        }

        bool requiresPrompt = pages.Equals(PromptPages, StringComparison.OrdinalIgnoreCase);
        var normalizedOptions = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [PagesOptionKey] = pages
        };
        IReadOnlySet<ConversionCapability> capabilities = requiresPrompt
            ? new HashSet<ConversionCapability> { ConversionCapability.SplitPages }
            : new HashSet<ConversionCapability>();

        return new ConversionPlan(
            CommandName,
            inputs,
            outputs,
            capabilities,
            normalizedOptions,
            ExistingTaskId: request.ExistingTaskId,
            BestEffortTaskPersistence: request.BestEffortTaskPersistence,
            TrackTaskLifecycle: request.TrackTaskLifecycle);
    }

    public async Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!plan.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SplitPdfUseCase cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException("Split conversion requires one output per input.");

        string pages = plan.NormalizedOptions.TryGetValue(PagesOptionKey, out object? pagesValue) &&
                       pagesValue is string suppliedPages
            ? suppliedPages
            : PromptPages;
        bool requiresPrompt = pages.Equals(PromptPages, StringComparison.OrdinalIgnoreCase);

        var stopwatch = Stopwatch.StartNew();
        ConversionTaskLifecycle? lifecycle = plan.TrackTaskLifecycle
            ? ConversionTaskLifecycle.Start(
                CommandName,
                plan.Inputs,
                plan.ExistingTaskId,
                plan.BestEffortTaskPersistence)
            : null;
        if (lifecycle is not null)
            observer?.OnTaskStarted(lifecycle.TaskId);

        int completedFiles = Math.Clamp(plan.ResumeStartIndex, 0, plan.Inputs.Count);
        try
        {
            for (int i = completedFiles; i < plan.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lifecycle?.RecordFileStarting(i);
                observer?.OnFileStarting(i);

                string? targetPages = requiresPrompt
                    ? await interaction.RequestSplitPagesAsync(i, plan.Inputs[i], cancellationToken)
                    : pages;
                if (string.IsNullOrWhiteSpace(targetPages))
                    throw new OperationCanceledException(cancellationToken);

                int index = i;
                await Task.Run(
                    () => FileProcessor.SplitPdf(
                        plan.Inputs[index],
                        plan.Outputs[index],
                        targetPages,
                        (current, total, message) => progress?.Report(
                            new ConversionProgress(
                                (index * 100) + current,
                                plan.Inputs.Count * 100,
                                message)),
                        cancellationToken),
                    cancellationToken);
                completedFiles = i + 1;
            }

            lifecycle?.CompleteSuccess(string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return Result(ConversionResultStatus.Succeeded, null);
        }
        catch (ConversionParkedException ex)
        {
            lifecycle?.Park(ex.Message, ex.NextFileIndex);
            stopwatch.Stop();
            return Result(ConversionResultStatus.Parked, ex.Message);
        }
        catch (OperationCanceledException)
        {
            lifecycle?.CompleteFailure("Canceled", string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return Result(ConversionResultStatus.Canceled, null);
        }
        catch (Exception ex)
        {
            lifecycle?.CompleteFailure(ex.Message, string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return Result(ConversionResultStatus.Failed, ex.Message);
        }

        ConversionResult Result(ConversionResultStatus status, string? error) =>
            new(
                status,
                plan.Outputs,
                error,
                stopwatch.Elapsed,
                completedFiles,
                lifecycle?.TaskId ?? "");
    }
}
