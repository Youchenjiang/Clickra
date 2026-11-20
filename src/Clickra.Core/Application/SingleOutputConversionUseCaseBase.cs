using Clickra.Core.Modules.Sys.Settings.Application;
namespace Clickra.Core.Application;

/// <summary>Shared application lifecycle for conversions that produce one output file.</summary>
public abstract class SingleOutputConversionUseCaseBase : ConversionUseCaseBase
{
    protected abstract string PlannedOutputFileName { get; }

    /// <summary>
    /// Optional option: the name (without extension) of the single output file, e.g. 「期末報告_合併」 typed
    /// in the desktop merge workspace. Invalid characters are replaced; the action's extension is kept.
    /// </summary>
    public const string OutputNameOptionKey = "output_name";

    /// <summary>The output file name: the caller's name when given, otherwise the action's default name.</summary>
    protected string ResolveOutputFileName(ConversionRequest request)
    {
        if (request.Options is null ||
            !request.Options.TryGetValue(OutputNameOptionKey, out object? raw) ||
            raw is not string name ||
            string.IsNullOrWhiteSpace(name))
        {
            return PlannedOutputFileName;
        }

        string extension = Path.GetExtension(PlannedOutputFileName);
        string stem = name.Trim();
        if (stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) stem = stem[..^extension.Length];
        foreach (char invalid in Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }))
            stem = stem.Replace(invalid, '_');
        stem = stem.Trim().TrimEnd('.');
        return stem.Length == 0 ? PlannedOutputFileName : stem + extension;
    }

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
            ? StoragePathController.GetOutputDirectory(inputs[0])
            : Path.GetFullPath(request.OutputOverride);
        string output = Path.Combine(outputDir, ResolveOutputFileName(request));
        ValidateOutput(inputs, output);

        return new ConversionPlan(
            Command,
            inputs,
            new[] { output },
            GetCapabilities(request),
            NormalizeOptions(request),
            ExistingTaskId: request.ExistingTaskId,
            BestEffortTaskPersistence: request.BestEffortTaskPersistence,
            TrackTaskLifecycle: request.TrackTaskLifecycle,
            TaskLifecycleRepository: request.TaskLifecycleRepository);
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
        return await ConversionLifecycleResultRunner.RunAsync(
            new ConversionLifecycleRunContext(
                plan, stopwatch, lifecycle, plan.Outputs[0], true),
            async _ =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteSingleAsync(plan, progress, cancellationToken);
                completedFiles = plan.Inputs.Count;
            },
            () => completedFiles);
    }
}
