namespace Clickra.Core.Application;

/// <summary>Shared application lifecycle for conversions that produce one output file.</summary>
public abstract class SingleOutputConversionUseCaseBase : ConversionUseCaseBase
{
    protected abstract string PlannedOutputFileName { get; }

    protected abstract string OutputCountError { get; }

    protected virtual IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request) =>
        new HashSet<ConversionCapability>();

    protected virtual IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request) =>
        request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);

    protected virtual void ValidateOutput(
        IReadOnlyList<string> inputs,
        string output)
    {
    }

    protected abstract Task ExecuteSingleAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken);

    public override ConversionPlan Plan(ConversionRequest request)
    {
        ConversionValidationResult validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var inputs = request.InputFiles.ToList();
        string outputDir = string.IsNullOrWhiteSpace(request.OutputOverride)
            ? ClickraStorage.GetOutputDir(inputs[0])
            : Path.GetFullPath(request.OutputOverride);
        string output = Path.Combine(outputDir, PlannedOutputFileName);
        ValidateOutput(inputs, output);

        return new ConversionPlan(
            Command,
            inputs,
            new[] { output },
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
        var (stopwatch, lifecycle) = StartExecution(
            plan,
            interaction,
            plan.Outputs.Count == 1,
            OutputCountError,
            observer);

        int completedFiles = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteSingleAsync(plan, progress, cancellationToken);
            completedFiles = plan.Inputs.Count;

            lifecycle?.CompleteSuccess(plan.Outputs[0]);
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
            lifecycle?.CompleteFailure("Canceled", plan.Outputs[0]);
            stopwatch.Stop();
            return Result(ConversionResultStatus.Canceled, null);
        }
        catch (Exception ex)
        {
            lifecycle?.CompleteFailure(ex.Message, plan.Outputs[0]);
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
