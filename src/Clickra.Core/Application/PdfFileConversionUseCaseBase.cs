using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Shared application lifecycle for one-input/one-output PDF conversions.</summary>
public abstract class PdfFileConversionUseCaseBase : IConversionUseCase
{
    public abstract string Command { get; }

    protected abstract string UseCaseName { get; }

    protected abstract string OutputSuffix { get; }

    protected abstract string OutputCountError { get; }

    protected abstract string UnsupportedInputError(string path);

    protected virtual ConversionValidationResult ValidateOptions(ConversionRequest request) =>
        ConversionValidationResult.Success();

    protected virtual IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request) =>
        new HashSet<ConversionCapability>();

    protected virtual IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request) =>
        request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);

    protected virtual object? CreateExecutionState(ConversionPlan plan) => null;

    protected abstract Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken);

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"{UseCaseName} cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(Command))
            return ConversionValidationResult.Failure("At least one PDF file is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(Command);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ValidateOptions(request)
            : ConversionValidationResult.Failure(UnsupportedInputError(invalid));
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
                Path.GetFileNameWithoutExtension(input) + OutputSuffix))
            .ToList();

        return new ConversionPlan(
            Command,
            inputs,
            outputs,
            GetCapabilities(request),
            NormalizeOptions(request),
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
        if (!plan.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{UseCaseName} cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException(OutputCountError);

        object? executionState = CreateExecutionState(plan);
        var stopwatch = Stopwatch.StartNew();
        ConversionTaskLifecycle? lifecycle = plan.TrackTaskLifecycle
            ? ConversionTaskLifecycle.Start(
                Command,
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
                await ExecuteFileAsync(
                    i,
                    plan,
                    interaction,
                    progress,
                    executionState,
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

    protected static void ReportFileProgress(
        ConversionPlan plan,
        int index,
        IProgress<ConversionProgress>? progress,
        int current,
        string message) =>
        progress?.Report(new ConversionProgress(
            (index * 100) + current,
            plan.Inputs.Count * 100,
            message));
}
