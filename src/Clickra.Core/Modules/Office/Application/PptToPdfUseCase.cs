using Clickra.Core.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Office.Application;

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
                BuildExecutionOptions(plan),
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);

    private static Dictionary<string, object> BuildExecutionOptions(ConversionPlan plan)
    {
        var options = new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal)
        {
            [ProcessorExecutionOptions.OutputPathsKey] = plan.Outputs.ToArray()
        };
        return options;
    }
}
