using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for converting Word documents to PDF.</summary>
public sealed class WordToPdfUseCase : OfficeToPdfUseCaseBase
{
    public const string CommandName = "word2pdf";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(WordToPdfUseCase);

    protected override string InputRequirementError => "At least one Word document is required.";

    protected override string UnsupportedInputError(string path) =>
        $"Unsupported Word input '{path}'.";

    protected override string OutputCountError =>
        "Word to PDF conversion requires one output per input.";

    protected override Task ExecuteBatchAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => FileProcessor.ConvertWordToPdf(
                plan.Inputs.ToList(),
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
}
