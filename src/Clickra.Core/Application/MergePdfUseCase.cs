using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for merging PDF files into one document.</summary>
public sealed class MergePdfUseCase : SingleOutputConversionUseCaseBase
{
    public const string CommandName = "merge-pdf";
    public const string OutputFileName = "Merged_PDF.pdf";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(MergePdfUseCase);
    protected override string InputRequirementError => "At least two PDF files are required.";
    protected override string PlannedOutputFileName => OutputFileName;
    protected override string OutputCountError => "PDF merge requires exactly one output.";
    protected override string UnsupportedInputError(string path) => $"Unsupported merge input '{path}'.";

    protected override async Task ExecuteSingleAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () => FileProcessor.MergePdfs(
                plan.Inputs.ToList(),
                plan.Outputs[0],
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
    }
}
