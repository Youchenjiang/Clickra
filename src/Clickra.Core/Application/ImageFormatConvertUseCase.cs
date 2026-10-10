using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for one img-to-* image conversion command.</summary>
public sealed class ImageFormatConvertUseCase : PerFileConversionUseCaseBase
{
    public const string PngCommand = "img-to-png";
    public const string JpgCommand = "img-to-jpg";
    public const string WebpCommand = "img-to-webp";
    public const string GifCommand = "img-to-gif";
    public const string HeicCommand = "img-to-heic";

    private readonly string _targetFormat;

    public ImageFormatConvertUseCase(string command)
    {
        Command = command switch
        {
            PngCommand => PngCommand,
            JpgCommand => JpgCommand,
            WebpCommand => WebpCommand,
            GifCommand => GifCommand,
            HeicCommand => HeicCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown image format conversion command.")
        };
        _targetFormat = Command["img-to-".Length..];
    }

    public override string Command { get; }
    protected override string UseCaseName => $"ImageFormatConvertUseCase for '{Command}'";
    protected override string InputRequirementError => "At least one image file is required.";
    protected override string OutputCountError => "Image format conversion requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported image input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) +
        ImageFormatConvertProcessor.ToOutputExtension(_targetFormat);
    protected override string NormalizeOutputOverride(string outputOverride) => outputOverride;

    protected override void ValidateOutputs(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
        ImageOutputSafety.EnsureUniqueOutputs(outputs);
        ImageOutputSafety.EnsureOutputsDoNotOverwriteInputs(inputs, outputs);
    }

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () => FileProcessor.ConvertImageFormat(
                plan.Inputs[index],
                plan.Outputs[index],
                _targetFormat,
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
