using Clickra.Core.Application;
using Clickra.Core.Modules.Office.Infrastructure;
using Clickra.Core.Modules.Office.Processing;
using Clickra.Core.Modules.Settings.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Office.Application;

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
                StoragePathController.GetOutputDirectory(input),
                Path.GetFileNameWithoutExtension(input) + ".pdf"))
            .ToList();

        return new ConversionPlan(
            Command,
            inputs,
            outputs,
            new HashSet<ConversionCapability>(),
            SnapshotSavedOptions(),
            ExistingTaskId: request.ExistingTaskId,
            BestEffortTaskPersistence: request.BestEffortTaskPersistence,
            TrackTaskLifecycle: request.TrackTaskLifecycle);
    }

    private static IReadOnlyDictionary<string, object> SnapshotSavedOptions() =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [OfficeConversionOptions.EngineKey] = SettingsController.OfficeEngineValue,
            [OfficeConversionOptions.LanguageKey] = SettingsController.Language,
            [OfficeConversionOptions.LibreOfficePathKey] = SettingsController.LibreOfficePath,
            [OfficeConversionOptions.LibreOfficeRemovalPendingRestartKey] =
                SettingsController.LibreOfficeRemovalPendingRestart
        };

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
        return await ConversionLifecycleResultRunner.RunAsync(
            new ConversionLifecycleRunContext(
                plan, stopwatch, lifecycle, string.Join(";", plan.Outputs), false),
            async _ =>
            {
                string? readinessError = GetReadinessError(plan);
                if (readinessError is not null)
                    throw new InvalidOperationException(readinessError);

                await ExecuteBatchAsync(plan, progress, cancellationToken);
                completedFiles = plan.Inputs.Count;
            },
            () => completedFiles);
    }

    private string? GetReadinessError(ConversionPlan plan)
    {
        OfficeConversionSettings settings = OfficeConversionOptions.Read(plan.NormalizedOptions);
        if (OfficeEngineDetector.IsCommandReady(
                Command,
                settings.Engine,
                settings.LibreOfficeRemovalPendingRestart,
                settings.LibreOfficePath))
        {
            return null;
        }

        string errorKey = OfficeEngineDetector.GetUnavailableErrorKey(settings.Engine);
        return Localization.T(errorKey, settings.Language);
    }
}
