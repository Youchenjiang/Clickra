using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Application;
using Clickra.Core.Modules.Doc.Shared.Processing;
using Clickra.Core.Modules.Doc.MarkdownWord.Processing;

namespace Clickra.Core.Modules.Doc.MarkdownWord.Application;

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
        var processor = new MarkdownToWordProcessor();
        return Task.Run(
            () => processor.Process(
                new List<string> { plan.Inputs[index] },
                plan.Outputs[index],
                options,
                (current, _, message) => ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
