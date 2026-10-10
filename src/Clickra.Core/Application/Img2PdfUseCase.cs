using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting images to individual PDF files.</summary>
public sealed class Img2PdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "img2pdf";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(Img2PdfUseCase);
    protected override string InputRequirementError => "At least one image file is required.";
    protected override string OutputCountError => "Image-to-PDF conversion requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported image input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + ".pdf";

    protected override void ValidateOutputs(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs) =>
        ImageOutputSafety.EnsureUniqueOutputs(outputs);

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () => FileProcessor.ConvertImagesToPdf(
                new List<string> { plan.Inputs[index] },
                plan.Outputs[index],
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
