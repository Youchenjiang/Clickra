using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for compressing image files.</summary>
public sealed class ImgCompressUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "img-compress";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(ImgCompressUseCase);
    protected override string InputRequirementError => "At least one image file is required.";
    protected override string OutputCountError => "Image compression requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported image input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + "_compressed" + Path.GetExtension(input);
    protected override string NormalizeOutputOverride(string outputOverride) => outputOverride;

    protected override void ValidateOutputs(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
        ImageOutputSafety.EnsureUniqueOutputs(outputs);
        ImageOutputSafety.EnsureOutputsDoNotOverwriteInputs(inputs, outputs);
    }

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request) =>
        request.Options is null
            ? ConvertCommandRegistry.ImageCompressionOptions()
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
            () => FileProcessor.CompressImage(
                plan.Inputs[index],
                plan.Outputs[index],
                new Dictionary<string, object>(options, StringComparer.Ordinal),
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
