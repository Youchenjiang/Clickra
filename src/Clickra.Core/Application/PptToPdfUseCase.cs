using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting PowerPoint presentations to PDF.</summary>
public sealed class PptToPdfUseCase : OfficeToPdfUseCaseBase
{
    public const string CommandName = "ppt2pdf";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(PptToPdfUseCase);

    protected override string InputRequirementError => "At least one PowerPoint presentation is required.";

    protected override string UnsupportedInputError(string path) =>
        $"Unsupported PowerPoint input '{path}'.";

    protected override string OutputCountError =>
        "PowerPoint to PDF conversion requires one output per input.";

    protected override Task ExecuteBatchAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => FileProcessor.ConvertPptToPdf(
                plan.Inputs.ToList(),
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
}
