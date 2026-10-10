using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for stitching images into one PNG.</summary>
public sealed class ImgStitchUseCase : SingleOutputConversionUseCaseBase
{
    public const string CommandName = "img-stitch";
    public const string OutputFileName = "Stitched_Image.png";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(ImgStitchUseCase);
    protected override string InputRequirementError => "At least two image files are required.";
    protected override string PlannedOutputFileName => OutputFileName;
    protected override string OutputCountError => "Image stitching requires exactly one output.";
    protected override string UnsupportedInputError(string path) => $"Unsupported image input '{path}'.";

    protected override void ValidateOutput(
        IReadOnlyList<string> inputs,
        string output) =>
        ImageOutputSafety.EnsureOutputsDoNotOverwriteInputs(inputs, new[] { output });

    protected override async Task ExecuteSingleAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () => FileProcessor.StitchImages(
                plan.Inputs.ToList(),
                plan.Outputs[0],
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
    }
}
