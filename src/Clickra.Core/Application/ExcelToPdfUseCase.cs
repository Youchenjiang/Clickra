using Clickra.Core.Processors;

namespace Clickra.Core.Application;

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
            () => FileProcessor.ConvertExcelToPdf(
                plan.Inputs.ToList(),
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
}
