using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Common validation and task-start behavior for application conversion use cases.</summary>
public abstract class ConversionUseCaseBase : IConversionUseCase
{
    public abstract string Command { get; }

    protected abstract string UseCaseName { get; }

    protected abstract string InputRequirementError { get; }

    protected abstract string UnsupportedInputError(string path);

    protected virtual ConversionValidationResult ValidateOptions(ConversionRequest request) =>
        ConversionValidationResult.Success();

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
            ? ValidateOptions(request)
            : ConversionValidationResult.Failure(UnsupportedInputError(invalid));
    }

    protected (Stopwatch Stopwatch, ConversionTaskLifecycle? Lifecycle) StartExecution(
        ConversionPlan plan,
        IConversionInteraction interaction,
        bool outputShapeIsValid,
        string outputCountError,
        IConversionExecutionObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(interaction);
        if (!plan.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{UseCaseName} cannot execute '{plan.Command}'.");
        if (!outputShapeIsValid)
            throw new InvalidOperationException(outputCountError);

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
        return (stopwatch, lifecycle);
    }

    public abstract ConversionPlan Plan(ConversionRequest request);

    public abstract Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default);
}
