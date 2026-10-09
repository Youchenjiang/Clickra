using System.Diagnostics;
using Clickra.Core.Processors;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for removing PDF password protection.</summary>
public sealed class DecryptPdfUseCase : IConversionUseCase
{
    public const string CommandName = "decrypt-pdf";

    public string Command => CommandName;

    public ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"DecryptPdfUseCase cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < ConvertCommandRegistry.GetMinFiles(CommandName))
            return ConversionValidationResult.Failure("At least one PDF file is required.");

        string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(CommandName);
        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? ConversionValidationResult.Success()
            : ConversionValidationResult.Failure($"Unsupported decrypt input '{invalid}'.");
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
                Path.GetFileNameWithoutExtension(input) + "_decrypted.pdf"))
            .ToList();
        return new ConversionPlan(
            CommandName,
            inputs,
            outputs,
            new HashSet<ConversionCapability> { ConversionCapability.Password },
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
        if (!plan.Command.Equals(CommandName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"DecryptPdfUseCase cannot execute '{plan.Command}'.");
        if (plan.Inputs.Count != plan.Outputs.Count)
            throw new InvalidOperationException("Decrypt conversion requires one output per input.");

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

                string password = "";
                bool isRetry = false;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        int index = i;
                        await Task.Run(
                            () => FileProcessor.DecryptPdf(
                                plan.Inputs[index],
                                plan.Outputs[index],
                                password,
                                (current, total, message) => progress?.Report(
                                    new ConversionProgress(
                                        (index * 100) + current,
                                        plan.Inputs.Count * 100,
                                        message)),
                                cancellationToken),
                            cancellationToken);
                        completedFiles = i + 1;
                        break;
                    }
                    catch (PdfReaderException ex) when (IsPasswordError(ex))
                    {
                        string? supplied = await interaction.RequestPasswordAsync(
                            i,
                            plan.Inputs[i],
                            isRetry,
                            cancellationToken);
                        if (supplied is null)
                            throw new OperationCanceledException(cancellationToken);
                        password = supplied;
                        isRetry = true;
                    }
                }
            }

            lifecycle?.CompleteSuccess(string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return new ConversionResult(
                ConversionResultStatus.Succeeded,
                plan.Outputs,
                null,
                stopwatch.Elapsed,
                completedFiles,
                lifecycle?.TaskId ?? "");
        }
        catch (ConversionParkedException ex)
        {
            lifecycle?.Park(ex.Message, ex.NextFileIndex);
            stopwatch.Stop();
            return new ConversionResult(
                ConversionResultStatus.Parked,
                plan.Outputs,
                ex.Message,
                stopwatch.Elapsed,
                completedFiles,
                lifecycle?.TaskId ?? "");
        }
        catch (OperationCanceledException)
        {
            lifecycle?.CompleteFailure("Canceled", string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return new ConversionResult(
                ConversionResultStatus.Canceled,
                plan.Outputs,
                null,
                stopwatch.Elapsed,
                completedFiles,
                lifecycle?.TaskId ?? "");
        }
        catch (Exception ex)
        {
            lifecycle?.CompleteFailure(ex.Message, string.Join(";", plan.Outputs));
            stopwatch.Stop();
            return new ConversionResult(
                ConversionResultStatus.Failed,
                plan.Outputs,
                ex.Message,
                stopwatch.Elapsed,
                completedFiles,
                lifecycle?.TaskId ?? "");
        }
    }

    private static bool IsPasswordError(Exception ex) =>
        ex is PdfReaderException &&
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
