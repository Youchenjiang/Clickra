using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting Markdown documents to PDF.</summary>
public sealed class MarkdownToPdfUseCase : IConversionUseCase
{
    public const string CommandName = "md2pdf";

    public string Command => CommandName;

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"MarkdownToPdfUseCase cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(CommandName))
            return ConversionValidationResult.Failure("At least one Markdown file is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(CommandName);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ConversionValidationResult.Success()
            : ConversionValidationResult.Failure($"Unsupported Markdown input '{invalid}'.");
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
                Path.GetFileNameWithoutExtension(input) + ".pdf"))
            .ToList();

        IReadOnlyDictionary<string, object>? requestedOptions = request.Options;
        Dictionary<string, object> normalizedOptions = MarkdownPdfOptions.Create(
            MarkdownPdfOptions.GetTheme(requestedOptions),
            MarkdownPdfOptions.GetPaper(requestedOptions),
            MarkdownPdfOptions.GetTextSize(requestedOptions),
            MarkdownPdfOptions.GetCodeTheme(requestedOptions),
            MarkdownPdfOptions.GetTemplatePath(requestedOptions));

        return new ConversionPlan(
            CommandName,
            inputs,
            outputs,
            new HashSet<ConversionCapability>(),
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
            throw new InvalidOperationException($"MarkdownToPdfUseCase cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException("Markdown to PDF conversion requires one output per input.");

        var options = new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal);
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

        int completedFiles = plan.ResumeStartIndex;
        try
        {
            for (int i = completedFiles; i < plan.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lifecycle?.RecordFileStarting(i);
                observer?.OnFileStarting(i);

                int index = i;
                await Task.Run(
                    () => FileProcessor.ConvertMarkdownToPdf(
                        plan.Inputs[index],
                        plan.Outputs[index],
                        options,
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
