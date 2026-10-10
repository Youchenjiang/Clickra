using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for one img-to-* image conversion command.</summary>
public sealed class ImageFormatConvertUseCase : IConversionUseCase
{
    public const string PngCommand = "img-to-png";
    public const string JpgCommand = "img-to-jpg";
    public const string WebpCommand = "img-to-webp";
    public const string GifCommand = "img-to-gif";
    public const string HeicCommand = "img-to-heic";

    private readonly string _targetFormat;

    public ImageFormatConvertUseCase(string command)
    {
        Command = command switch
        {
            PngCommand => PngCommand,
            JpgCommand => JpgCommand,
            WebpCommand => WebpCommand,
            GifCommand => GifCommand,
            HeicCommand => HeicCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown image format conversion command.")
        };
        _targetFormat = Command["img-to-".Length..];
    }

    public string Command { get; }

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"ImageFormatConvertUseCase for '{Command}' cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(Command))
            return ConversionValidationResult.Failure("At least one image file is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(Command);
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
        string? outputOverride = string.IsNullOrWhiteSpace(request.OutputOverride)
            ? null
            : request.OutputOverride;
        string extension = ImageFormatConvertProcessor.ToOutputExtension(_targetFormat);
        List<string> outputs = inputs
            .Select(input => Path.Combine(
                outputOverride ?? ClickraStorage.GetOutputDir(input),
                Path.GetFileNameWithoutExtension(input) + extension))
            .ToList();
        ImageOutputSafety.EnsureUniqueOutputs(outputs);
        ImageOutputSafety.EnsureOutputsDoNotOverwriteInputs(inputs, outputs);

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
            throw new InvalidOperationException($"ImageFormatConvertUseCase for '{Command}' cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException("Image format conversion requires one output per input.");

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

        int completedFiles = Math.Clamp(plan.ResumeStartIndex, 0, plan.Inputs.Count);
        try
        {
            for (int i = completedFiles; i < plan.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lifecycle?.RecordFileStarting(i);
                observer?.OnFileStarting(i);

                int index = i;
                await Task.Run(
                    () => FileProcessor.ConvertImageFormat(
                        plan.Inputs[index],
                        plan.Outputs[index],
                        _targetFormat,
                        (current, total, message) => progress?.Report(
                            new ConversionProgress(
                                (index * 100) + current,
                                plan.Inputs.Count * 100,
                                message)),
                        cancellationToken),
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
