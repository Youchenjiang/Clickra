using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Common validation and task-start behavior for application conversion use cases.</summary>
public abstract class ConversionUseCaseBase : IConversionUseCase
{
    public abstract string Command { get; }
    public virtual IReadOnlySet<ConversionCapability> Capabilities => new HashSet<ConversionCapability>();

    protected abstract string UseCaseName { get; }

    protected abstract string InputRequirementError { get; }

    protected abstract string UnsupportedInputError(string path);

    protected virtual ConversionValidationResult ValidateOptions(ConversionRequest request) =>
        ConversionValidationResult.Success();

    protected virtual ConversionValidationResult ValidateInputs(ConversionRequest request) =>
        ConversionValidationResult.Success();

    public virtual ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"{UseCaseName} cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(Command))
            return ConversionValidationResult.Failure(InputRequirementError);

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(Command);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        if (invalid is not null)
            return ConversionValidationResult.Failure(UnsupportedInputError(invalid));

        var inputValidation = ValidateInputs(request);
        if (!inputValidation.IsValid)
            return inputValidation;

        return ValidateOptions(request);
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
                plan.BestEffortTaskPersistence,
                repository: plan.TaskLifecycleRepository)
            : null;
        if (lifecycle is not null)
        {
            if (!string.IsNullOrWhiteSpace(lifecycle.TaskId))
            {
                observer?.OnTaskStarted(lifecycle.TaskId);
            }
            else
            {
                string reason = lifecycle.LastPersistenceError?.Message ?? "task identifier was not assigned";
                Console.Error.WriteLine($"[ConversionUseCaseBase] Best-effort task start failed for '{Command}': {reason}.");
            }
        }
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
