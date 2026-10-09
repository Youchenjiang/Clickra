using System.Diagnostics;
using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for compressing PDF files.</summary>
public sealed class CompressPdfUseCase : IConversionUseCase
{
    public const string CommandName = "compress-pdf";
    public const string LevelOptionKey = "level";

    public string Command => CommandName;

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"CompressPdfUseCase cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(CommandName))
            return ConversionValidationResult.Failure("At least one PDF file is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(CommandName);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        if (invalid is not null)
            return ConversionValidationResult.Failure($"Unsupported compression input '{invalid}'.");

        if (request.Options is not null &&
            request.Options.TryGetValue(LevelOptionKey, out object? levelValue) &&
            !PdfCompressionOptions.TryParseLevel(levelValue?.ToString(), out _))
        {
            return ConversionValidationResult.Failure($"Unsupported PDF compression level: {levelValue}");
        }

        return ConversionValidationResult.Success();
    }

    public ConversionPlan Plan(ConversionRequest request)
    {
        ConversionValidationResult validation = Validate(request);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Error);

        var inputs = request.InputFiles.ToList();
        string? outputOverride = string.IsNullOrWhiteSpace(request.OutputOverride)
            ? null
            : Path.GetFullPath(request.OutputOverride);
        List<string> outputs = inputs
            .Select(input => Path.Combine(
                outputOverride ?? ClickraStorage.GetOutputDir(input),
                Path.GetFileNameWithoutExtension(input) + "_compressed.pdf"))
            .ToList();

        Dictionary<string, object> normalizedOptions = request.Options is null
            ? ConvertCommandRegistry.CompressionOptions()
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);

        return new ConversionPlan(
            CommandName,
            inputs,
            outputs,
            new HashSet<ConversionCapability>(),
            normalizedOptions,
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
            throw new InvalidOperationException($"CompressPdfUseCase cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException("PDF compression requires one output per input.");

        var options = new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal);
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
                    () => FileProcessor.CompressPdf(
                        plan.Inputs[index],
                        plan.Outputs[index],
                        options,
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
