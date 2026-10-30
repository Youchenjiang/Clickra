using Clickra.Core.Application;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Modules.PdfPrint.Ports;
using Clickra.Core.Modules.PdfPrint.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfPrint.Application;

/// <summary>
/// Authoritative application use case for printing PDF files directly to Windows printers.
/// </summary>
public sealed class PrintPdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "print-pdf";

    private readonly IPrinterDiscoveryService _printerDiscovery;

    public PrintPdfUseCase(IPrinterDiscoveryService? printerDiscovery = null)
    {
        _printerDiscovery = printerDiscovery ?? new SystemPrinterDiscoveryService();
    }

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(PrintPdfUseCase);
    protected override string InputRequirementError => "At least one PDF file is required.";
    protected override string OutputCountError => "PDF print requires one job per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported print input '{path}'.";

    public override ConversionValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Command.Equals(Command, StringComparison.OrdinalIgnoreCase))
            return ConversionValidationResult.Failure($"{UseCaseName} cannot handle '{request.Command}'.");
        if (request.InputFiles.Count < 1)
            return ConversionValidationResult.Failure(InputRequirementError);

        string? invalid = request.InputFiles.FirstOrDefault(path =>
            !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));
        if (invalid is not null)
            return ConversionValidationResult.Failure(UnsupportedInputError(invalid));

        var inputValidation = ValidateInputs(request);
        if (!inputValidation.IsValid)
            return inputValidation;

        return ValidateOptions(request);
    }

    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + ".printed";

    protected override ConversionValidationResult ValidateInputs(ConversionRequest request)
    {
        if (request.InputFiles.Any(f => File.Exists(f) && PdfEncryptionQuery.IsEncrypted(f)))
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(request.Options);
            return ConversionValidationResult.Failure(Localization.T("pdf_err_encrypted_print", lang));
        }

        return ConversionValidationResult.Success();
    }

    protected override ConversionValidationResult ValidateOptions(ConversionRequest request)
    {
        var options = PrintPdfOptions.FromDictionary(request.Options);
        if (!string.IsNullOrWhiteSpace(options.PrinterName) && !_printerDiscovery.IsPrinterInstalled(options.PrinterName))
        {
            return ConversionValidationResult.Failure($"Printer '{options.PrinterName}' is not installed.");
        }

        return ConversionValidationResult.Success();
    }

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        var options = PrintPdfOptions.FromDictionary(request.Options);
        if (string.IsNullOrWhiteSpace(options.PrinterName))
        {
            string? defaultPrinter = _printerDiscovery.GetDefaultPrinter();
            if (!string.IsNullOrWhiteSpace(defaultPrinter))
            {
                options = options with { PrinterName = defaultPrinter };
            }
        }

        return options.ToDictionary();
    }

    protected override object CreateExecutionState(ConversionPlan plan) =>
        PrintPdfOptions.FromDictionary(plan.NormalizedOptions);

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        var printOptions = (PrintPdfOptions?)executionState
            ?? PrintPdfOptions.FromDictionary(plan.NormalizedOptions);

        await Task.Run(
            () => PdfPrintProcessor.Print(
                plan.Inputs[index],
                printOptions,
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
