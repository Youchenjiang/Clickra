using Clickra.Core.Modules.Settings.Application;
using Clickra.Core.Application;
using Clickra.Core.Modules.PdfDecrypt.Processing;
using Clickra.Core.Processors;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.PdfDecrypt.Application;

/// <summary>Authoritative application workflow for removing PDF password protection.</summary>
public sealed class DecryptPdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "decrypt-pdf";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(DecryptPdfUseCase);
    protected override string InputRequirementError => "At least one PDF file is required.";
    protected override string OutputCountError => "Decrypt conversion requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported decrypt input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + "_decrypted.pdf";

    protected override ConversionValidationResult ValidateInputs(ConversionRequest request)
    {
        if (request.InputFiles.Any(f => File.Exists(f) && !PdfDecryptProcessor.IsEncrypted(f)))
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(request.Options);
            return ConversionValidationResult.Failure(Localization.T("pdf_not_encrypted", lang));
        }

        return ConversionValidationResult.Success();
    }
    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.Password };

    protected override IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request) =>
        Capabilities;

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        var options = request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);
        if (!options.ContainsKey(ExecutionLocalizationOptions.LanguageKey) || options[ExecutionLocalizationOptions.LanguageKey] is null)
        {
            options[ExecutionLocalizationOptions.LanguageKey] = SettingsController.Language;
        }
        return options;
    }

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        await ExecuteWithPasswordRetryAsync(
            index,
            plan,
            interaction,
            null,
            async password =>
            {
                var processor = new PdfDecryptProcessor();
                var options = new Dictionary<string, object>
                {
                    [ExecutionLocalizationOptions.LanguageKey] = plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey].ToString() ?? ClickraSettings.DefaultEmpty
                };
                if (!string.IsNullOrEmpty(password)) options["password"] = password;

                await Task.Run(
                    () => processor.Process(
                        new List<string> { plan.Inputs[index] },
                        plan.Outputs[index],
                        options,
                        (current, total, message) =>
                            ReportFileProgress(plan, index, progress, current, message),
                        cancellationToken),
                    cancellationToken);
            },
            IsPasswordError,
            cancellationToken);
    }

    private static bool IsPasswordError(Exception ex) =>
        ex is PdfReaderException &&
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
