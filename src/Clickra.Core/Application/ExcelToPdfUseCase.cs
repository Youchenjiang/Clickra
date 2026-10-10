using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting Excel workbooks to PDF.</summary>
public sealed class ExcelToPdfUseCase : IConversionUseCase
{
    public const string CommandName = "excel2pdf";

    public string Command => CommandName;

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"ExcelToPdfUseCase cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(CommandName))
            return ConversionValidationResult.Failure("At least one Excel workbook is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(CommandName);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ConversionValidationResult.Success()
            : ConversionValidationResult.Failure($"Unsupported Excel input '{invalid}'.");
    }

    public ConversionPlan Plan(ConversionRequest request)
    {
        ConversionValidationResult validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var inputs = request.InputFiles.ToList();
        // Preserve legacy Office behavior: output overrides were accepted by Native plumbing
        // but ignored by both registry planning and the batch processor.
        List<string> outputs = inputs
            .Select(input => Path.Combine(
                ClickraStorage.GetOutputDir(input),
                Path.GetFileNameWithoutExtension(input) + ".pdf"))
            .ToList();

        return new ConversionPlan(
            CommandName,
            inputs,
            outputs,
            new HashSet<ConversionCapability>(),
            new Dictionary<string, object>(StringComparer.Ordinal),
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
            throw new InvalidOperationException($"ExcelToPdfUseCase cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException("Excel to PDF conversion requires one output per input.");

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

        int completedFiles = 0;
        try
        {
            // Preserve the legacy Office batch call exactly. The old runner ignored
            // ResumeStartIndex and OnFileStarting for Office commands, so this owner does too.
            await Task.Run(
                () => FileProcessor.ConvertExcelToPdf(
                    plan.Inputs.ToList(),
                    (current, total, message) => progress?.Report(
                        new ConversionProgress(current, total, message)),
                    cancellationToken),
                cancellationToken);
            completedFiles = plan.Inputs.Count;

            lifecycle?.CompleteSuccess(string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return Result(ConversionResultStatus.Succeeded, null);
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
