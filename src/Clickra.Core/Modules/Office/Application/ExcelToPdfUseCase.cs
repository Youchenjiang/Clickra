using Clickra.Core.Application;
using Clickra.Core.Modules.Office.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Office.Application;

/// <summary>Authoritative application workflow for converting Excel workbooks to PDF.</summary>
public sealed class ExcelToPdfUseCase : OfficeToPdfUseCaseBase
{
    public const string CommandName = "excel2pdf";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(ExcelToPdfUseCase);

    protected override string InputRequirementError => "At least one Excel workbook is required.";

    protected override string UnsupportedInputError(string path) =>
        $"Unsupported Excel input '{path}'.";

    protected override string OutputCountError =>
        "Excel to PDF conversion requires one output per input.";

    protected override Task ExecuteBatchAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => new ExcelToPdfProcessor().Process(
                plan.Inputs.ToList(),
                null,
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
