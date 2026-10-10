using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Shared application lifecycle for conversions that produce one output file.</summary>
public abstract class SingleOutputConversionUseCaseBase : IConversionUseCase
{
    public abstract string Command { get; }

    protected abstract string UseCaseName { get; }

    protected abstract string InputRequirementError { get; }

    protected abstract string PlannedOutputFileName { get; }

    protected abstract string OutputCountError { get; }

    protected abstract string UnsupportedInputError(string path);

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

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"{UseCaseName} cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(Command))
            return ConversionValidationResult.Failure(InputRequirementError);

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(Command);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ConversionValidationResult.Success()
            : ConversionValidationResult.Failure(UnsupportedInputError(invalid));
    }

    public ConversionPlan Plan(ConversionRequest request)
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
        if (plan.Outputs.Count != 1)
            throw new InvalidOperationException(OutputCountError);

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
