using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for stitching images into one PNG.</summary>
public sealed class ImgStitchUseCase : IConversionUseCase
{
    public const string CommandName = "img-stitch";
    public const string OutputFileName = "Stitched_Image.png";

    public string Command => CommandName;

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"ImgStitchUseCase cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(CommandName))
            return ConversionValidationResult.Failure("At least two image files are required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(CommandName);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ConversionValidationResult.Success()
            : ConversionValidationResult.Failure($"Unsupported image input '{invalid}'.");
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
        string output = Path.Combine(outputDir, OutputFileName);
        ImageOutputSafety.EnsureOutputsDoNotOverwriteInputs(inputs, new[] { output });

        return new ConversionPlan(
            CommandName,
            inputs,
            new[] { output },
            new HashSet<ConversionCapability>(),
            new Dictionary<string, object>(),
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
        if (!plan.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ImgStitchUseCase cannot execute '{plan.Command}'.");
        if (plan.Outputs.Count != 1)
            throw new InvalidOperationException("Image stitching requires exactly one output.");

        var stopwatch = Stopwatch.StartNew();
        ConversionTaskLifecycle? lifecycle = plan.TrackTaskLifecycle
            ? ConversionTaskLifecycle.Start(
                CommandName,
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
            await Task.Run(
                () => FileProcessor.StitchImages(
                    plan.Inputs.ToList(),
                    plan.Outputs[0],
                    (current, total, message) => progress?.Report(
                        new ConversionProgress(current, total, message)),
                    cancellationToken),
                cancellationToken);
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
