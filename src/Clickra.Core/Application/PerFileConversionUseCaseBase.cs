namespace Clickra.Core.Application;

/// <summary>Shared application lifecycle for one-input/one-output file conversions.</summary>
public abstract class PerFileConversionUseCaseBase : ConversionUseCaseBase
{
    protected abstract string OutputCountError { get; }

    protected abstract string GetOutputFileName(string input);

    protected virtual IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request) =>
        new HashSet<ConversionCapability>();

    protected virtual IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request) =>
        request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);

    protected virtual object? CreateExecutionState(ConversionPlan plan) => null;

    protected virtual int GetInitialCompletedFiles(ConversionPlan plan) =>
        Math.Clamp(plan.ResumeStartIndex, 0, plan.Inputs.Count);

    protected virtual ConversionFailureKind ClassifyFailure(Exception exception) =>
        ConversionFailureKind.None;

    protected virtual string NormalizeOutputOverride(string outputOverride) =>
        Path.GetFullPath(outputOverride);

    protected virtual void ValidateOutputs(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
    }

    protected abstract Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken);

    public override ConversionPlan Plan(ConversionRequest request)
    {
        ConversionValidationResult validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var inputs = request.InputFiles.ToList();
        string? outputOverride = string.IsNullOrWhiteSpace(request.OutputOverride)
            ? null
            : NormalizeOutputOverride(request.OutputOverride);
        List<string> outputs = inputs
            .Select(input => Path.Combine(
                outputOverride ?? ClickraStorage.GetOutputDir(input),
                GetOutputFileName(input)))
            .ToList();
        ValidateOutputs(inputs, outputs);

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

    public override async Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        object? executionState = CreateExecutionState(plan);
        var (stopwatch, lifecycle) = StartExecution(
            plan,
            interaction,
            plan.Inputs.Count == plan.Outputs.Count,
            OutputCountError,
            observer);

        int completedFiles = GetInitialCompletedFiles(plan);
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
            return Result(ConversionResultStatus.Failed, ex.Message, ClassifyFailure(ex));
        }

        ConversionResult Result(
            ConversionResultStatus status,
            string? error,
            ConversionFailureKind failureKind = ConversionFailureKind.None) =>
            new(
                status,
                plan.Outputs,
                error,
                stopwatch.Elapsed,
                completedFiles,
                lifecycle?.TaskId ?? "",
                failureKind);
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
