using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting Markdown documents to PDF.</summary>
public sealed class MarkdownToPdfUseCase : MarkdownConversionUseCaseBase
{
    public const string CommandName = "md2pdf";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(MarkdownToPdfUseCase);

    protected override string OutputCountError =>
        "Markdown to PDF conversion requires one output per input.";

    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + ".pdf";

    protected override Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        var options = (Dictionary<string, object>)executionState!;
        return Task.Run(
            () => FileProcessor.ConvertMarkdownToPdf(
                plan.Inputs[index],
                plan.Outputs[index],
                options,
                (current, _, message) => ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
