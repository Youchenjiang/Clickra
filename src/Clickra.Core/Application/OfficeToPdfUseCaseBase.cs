namespace Clickra.Core.Application;

/// <summary>Shared application lifecycle for legacy Office batch-to-PDF conversions.</summary>
public abstract class OfficeToPdfUseCaseBase : ConversionUseCaseBase
{
    protected abstract string OutputCountError { get; }

    protected abstract Task ExecuteBatchAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken);

    public override ConversionPlan Plan(ConversionRequest request)
    {
        ConversionValidationResult validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var inputs = request.InputFiles.ToList();
        List<string> outputs = inputs
            .Select(input => Path.Combine(
                ClickraStorage.GetOutputDir(input),
                Path.GetFileNameWithoutExtension(input) + ".pdf"))
            .ToList();

        return new ConversionPlan(
            Command,
            inputs,
            outputs,
            new HashSet<ConversionCapability>(),
            new Dictionary<string, object>(StringComparer.Ordinal),
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
            plan.Inputs.Count == plan.Outputs.Count,
            OutputCountError,
            observer);

        int completedFiles = 0;
        try
        {
            await ExecuteBatchAsync(plan, progress, cancellationToken);
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
