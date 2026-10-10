using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for compressing PDF files.</summary>
public sealed class CompressPdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "compress-pdf";
    public const string LevelOptionKey = "level";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(CompressPdfUseCase);
    protected override string InputRequirementError => "At least one PDF file is required.";
    protected override string OutputCountError => "PDF compression requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported compression input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + "_compressed.pdf";

    protected override ConversionValidationResult ValidateOptions(ConversionRequest request)
    {
        if (request.Options is not null &&
            request.Options.TryGetValue(LevelOptionKey, out object? levelValue) &&
            !PdfCompressionOptions.TryParseLevel(levelValue?.ToString(), out _))
        {
            return ConversionValidationResult.Failure($"Unsupported PDF compression level: {levelValue}");
        }

        return ConversionValidationResult.Success();
    }

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request) =>
        request.Options is null
            ? ConvertCommandRegistry.CompressionOptions()
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);

    protected override object CreateExecutionState(ConversionPlan plan) =>
        new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal);

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        var options = (IReadOnlyDictionary<string, object>?)executionState
            ?? plan.NormalizedOptions;
        await Task.Run(
            () => FileProcessor.CompressPdf(
                plan.Inputs[index],
                plan.Outputs[index],
                new Dictionary<string, object>(options, StringComparer.Ordinal),
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
