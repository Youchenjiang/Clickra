using Clickra.Core.Modules.Settings.Application;
using Clickra.Core.Application;
using Clickra.Core.Modules.PdfCompression.Processing;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfCompression.Application;

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

    protected override ConversionValidationResult ValidateInputs(ConversionRequest request)
    {
        if (request.InputFiles.Any(f => File.Exists(f) && PdfEncryptionQuery.IsEncrypted(f)))
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(request.Options);
            return ConversionValidationResult.Failure(Localization.T("pdf_err_encrypted_compress", lang));
        }

        return ConversionValidationResult.Success();
    }

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

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        var options = request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);

        bool callerSuppliedLevel = options.TryGetValue(LevelOptionKey, out object? rawLevel) &&
            rawLevel is string levelStr &&
            !string.IsNullOrWhiteSpace(levelStr) &&
            PdfCompressionOptions.TryParseLevel(levelStr, out _);

        if (!callerSuppliedLevel)
        {
            int sliderLevel = ClickraSettings.NormalizePdfCompressLevel(
                SettingsController.PdfCompressionImageLevel);
            PdfCompressionLevel level = PdfCompressionOptions.FromSliderLevel(sliderLevel);
            options[LevelOptionKey] = PdfCompressionOptions.ToOptionName(level);

            if (!options.ContainsKey("strip_fonts"))
            {
                options["strip_fonts"] = SettingsController.PdfCompressionStripFonts;
            }

            if (!options.ContainsKey("minify_content"))
            {
                options["minify_content"] = SettingsController.PdfCompressionMinifyContent;
            }
        }

        return options;
    }

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
        var processor = new PdfCompressionProcessor();
        await Task.Run(
            () => processor.Process(
                new List<string> { plan.Inputs[index] },
                plan.Outputs[index],
                new Dictionary<string, object>(options, StringComparer.Ordinal),
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
