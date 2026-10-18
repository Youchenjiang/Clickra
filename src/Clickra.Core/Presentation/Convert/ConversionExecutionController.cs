using Clickra.Core.Application;

namespace Clickra.Core.Presentation.Convert;

public enum ConversionExecutionPresentationStatus
{
    Succeeded,
    Canceled,
    Parked,
    Failed
}

public sealed record ConversionExecutionPlanPresentation(
    IReadOnlyList<string> Outputs,
    IReadOnlySet<ConversionCapability> Capabilities)
{
    public ConversionExecutionPlanPresentation(IReadOnlyList<string> outputs)
        : this(outputs, new HashSet<ConversionCapability>())
    {
    }

    public void Deconstruct(out IReadOnlyList<string> outputs)
    {
        outputs = Outputs;
    }
}

public sealed record ConversionExecutionPresentationResult(
    ConversionExecutionPresentationStatus Status,
    string? Error,
    string TaskId,
    IReadOnlyList<string> Outputs);

public sealed record ConversionExecutionCallbacks(
    Func<int, string, bool, CancellationToken, Task<string?>>? PasswordPrompt = null,
    Func<int, string, CancellationToken, Task<string?>>? SplitPrompt = null,
    Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, object>?>>? OptionsPrompt = null,
    Action<string>? TaskStarted = null,
    Action<int>? FileStarting = null);

/// <summary>
/// Presentation-facing execution session over the Application use-case layer. Renderers supply
/// platform interaction callbacks and consume presentation results without orchestrating use cases.
/// </summary>
public sealed class ConversionExecutionController
{
    private IConversionUseCase? _useCase;
    private ConversionPlan? _plan;

    public static Exception CreateParkSignal(string reason, int nextFileIndex) =>
        new ConversionParkedException(reason, nextFileIndex);

    public static bool HasCapability(string command, ConversionCapability capability) =>
        ConversionUseCases.TryGet(command, out IConversionUseCase? useCase) &&
        useCase?.Capabilities.Contains(capability) == true;

    public static bool RequiresOptions(string command) =>
        HasCapability(command, ConversionCapability.MarkdownOptions);

    public ConversionExecutionPlanPresentation Prepare(
        string command,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, object>? options = null,
        string? existingTaskId = null,
        int resumeStartIndex = 0)
    {
        IConversionUseCase useCase = ConversionUseCases.GetRequired(command);
        ConversionPlan plan = useCase.Plan(new ConversionRequest(
            command,
            files,
            options,
            ExistingTaskId: existingTaskId)) with
        {
            ResumeStartIndex = resumeStartIndex
        };

        _useCase = useCase;
        _plan = plan;
        return new ConversionExecutionPlanPresentation(plan.Outputs, useCase.Capabilities);
    }

    public async Task<ConversionExecutionPresentationResult> ExecutePreparedAsync(
        ConversionExecutionCallbacks? callbacks = null,
        IProgress<TaskProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IConversionUseCase useCase = _useCase ?? throw new InvalidOperationException("Prepare must be called before execution.");
        ConversionPlan plan = _plan ?? throw new InvalidOperationException("Prepare must be called before execution.");
        callbacks ??= new ConversionExecutionCallbacks();

        var interaction = new DelegateConversionInteraction(
            callbacks.PasswordPrompt ?? ((_, _, _, _) => Task.FromResult<string?>(null)),
            callbacks.SplitPrompt ?? ((_, _, _) => Task.FromResult<string?>(null)),
            callbacks.OptionsPrompt ?? ((_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null)));
        var applicationProgress = progress is null
            ? null
            : new Progress<ConversionProgress>(state => progress.Report(new TaskProgressReport(state.Current, state.Total, state.Message)));
        IConversionExecutionObserver? observer = callbacks.TaskStarted is null && callbacks.FileStarting is null
            ? null
            : new PresentationExecutionObserver(callbacks);

        ConversionResult result = await useCase.ExecuteAsync(
            plan,
            interaction,
            applicationProgress,
            observer,
            cancellationToken).ConfigureAwait(false);

        return new ConversionExecutionPresentationResult(
            result.Status switch
            {
                ConversionResultStatus.Succeeded => ConversionExecutionPresentationStatus.Succeeded,
                ConversionResultStatus.Canceled => ConversionExecutionPresentationStatus.Canceled,
                ConversionResultStatus.Parked => ConversionExecutionPresentationStatus.Parked,
                _ => ConversionExecutionPresentationStatus.Failed
            },
            result.Error,
            result.TaskId,
            result.Outputs);
    }

    private sealed class PresentationExecutionObserver(ConversionExecutionCallbacks callbacks) : IConversionExecutionObserver
    {
        public void OnTaskStarted(string taskId) => callbacks.TaskStarted?.Invoke(taskId);
        public void OnFileStarting(int fileIndex) => callbacks.FileStarting?.Invoke(fileIndex);
    }
}

public readonly record struct TaskProgressReport(int Current, int Total, string Message)
{
    public int Percent => Total > 0 ? Math.Clamp((int)(Current * 100.0 / Total), 0, 100) : 0;
}
