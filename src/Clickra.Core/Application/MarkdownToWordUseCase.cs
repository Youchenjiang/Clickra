using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting Markdown documents to Word.</summary>
public sealed class MarkdownToWordUseCase : MarkdownConversionUseCaseBase
{
    public const string CommandName = "md2word";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(MarkdownToWordUseCase);

    protected override string OutputCountError =>
        "Markdown to Word conversion requires one output per input.";

    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + ".docx";

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
            () => FileProcessor.ConvertMarkdownToWord(
                plan.Inputs[index],
                plan.Outputs[index],
                options,
                (current, _, message) => ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
