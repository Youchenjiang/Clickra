using Clickra.Core.Application;
using Clickra.Core.Modules.Image.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Image.Application;

/// <summary>Authoritative application workflow for merging images into one PDF document.</summary>
public sealed class ImgMergeUseCase : SingleOutputConversionUseCaseBase
{
    public const string CommandName = "img-merge";
    public const string OutputFileName = "Merged_Images.pdf";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(ImgMergeUseCase);
    protected override string InputRequirementError => "At least two image files are required.";
    protected override string PlannedOutputFileName => OutputFileName;
    protected override string OutputCountError => "Image merge requires exactly one output.";
    protected override string UnsupportedInputError(string path) => $"Unsupported image input '{path}'.";

    protected override async Task ExecuteSingleAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var processor = new ImageToPdfProcessor();
        await Task.Run(
            () => processor.Process(
                plan.Inputs.ToList(),
                plan.Outputs[0],
                null,
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
    }
}
