using Clickra.Core.Application;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Application;
using Clickra.Core.Modules.Pdf.Print.Ports;
using Clickra.Core.Modules.Pdf.Print.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Pdf.Print.Application;

/// <summary>
/// Authoritative application use case for printing PDF files directly to Windows printers.
/// </summary>
public sealed class PrintPdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "print-pdf";

    private readonly IPrinterDiscoveryService _printerDiscovery;
    private readonly Action<string, PrintPdfOptions, Action<int, int, string>?, CancellationToken> _printAction;

    public PrintPdfUseCase(
        IPrinterDiscoveryService? printerDiscovery = null,
        Action<string, PrintPdfOptions, Action<int, int, string>?, CancellationToken>? printAction = null)
    {
        _printerDiscovery = printerDiscovery ?? new SystemPrinterDiscoveryService();
        _printAction = printAction ?? ((path, opts, prog, ct) => PdfPrintProcessor.Print(path, opts, prog, ct));
    }

    public override string Command => CommandName;
    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.PrintOptions, ConversionCapability.Password };
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

    protected override ConversionValidationResult ValidateOptions(ConversionRequest request)
    {
        var options = PrintPdfOptions.FromDictionary(request.Options);
        if (!string.IsNullOrWhiteSpace(options.PrinterName) && !_printerDiscovery.IsPrinterInstalled(options.PrinterName))
        {
            Diagnostics.ClickraLog.Warn("Print", $"Printer '{options.PrinterName}' is not installed.");
            return ConversionValidationResult.Failure($"Printer '{options.PrinterName}' is not installed.");
        }

        return ConversionValidationResult.Success();
    }

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        // Whatever the caller leaves out comes from 設定 › 功能預設值 (right-click runs pass nothing).
        var source = request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);
        if (!source.ContainsKey(PrintPdfOptions.PrinterNameKey) && !string.IsNullOrWhiteSpace(SettingsController.PrintPrinter))
        {
            source[PrintPdfOptions.PrinterNameKey] = SettingsController.PrintPrinter;
        }
        source.TryAdd(PrintPdfOptions.CopiesKey, SettingsController.PrintCopies);
        source.TryAdd(PrintPdfOptions.DuplexKey, SettingsController.PrintDuplex);
        source.TryAdd(PrintPdfOptions.OrientationKey, SettingsController.PrintOrientation);
        source.TryAdd(PrintPdfOptions.FitToPageKey, SettingsController.PrintFitToPage);
        source.TryAdd(PrintPdfOptions.DarkenTextKey, SettingsController.PrintDarkenText);
        source.TryAdd(PrintPdfOptions.PagesPerSheetKey, SettingsController.PrintPagesPerSheet);
        source.TryAdd(PrintPdfOptions.PageOrderKey, SettingsController.PrintPageOrder);
        source.TryAdd(PrintPdfOptions.PageBorderKey, SettingsController.PrintPageBorder);
        source.TryAdd(PrintPdfOptions.ScaleKey, SettingsController.PrintScale);
        source.TryAdd(PrintPdfOptions.ScalePercentKey, SettingsController.PrintScalePercent);
        source.TryAdd(PrintPdfOptions.GrayscaleKey, SettingsController.PrintGrayscale);
        source.TryAdd(PrintPdfOptions.CollateKey, SettingsController.PrintCollate);
        if (!source.ContainsKey(PrintPdfOptions.PaperSizeKey) && !string.IsNullOrWhiteSpace(SettingsController.PrintPaper))
        {
            source[PrintPdfOptions.PaperSizeKey] = SettingsController.PrintPaper;
        }

        var options = PrintPdfOptions.FromDictionary(source);
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

        using var unlock = await PdfUnlockStep.UnlockAsync(
            index,
            plan.Inputs[index],
            interaction,
            PasswordCache,
            cancellationToken).ConfigureAwait(false);

        if (unlock.IsSkipped)
        {
            throw new ConversionFileSkippedException(ConversionSkipReason.NeedsPassword);
        }

        await Task.Run(
            () => _printAction(
                unlock.EffectivePath,
                printOptions,
                (current, total, message) =>
                    ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }
}
