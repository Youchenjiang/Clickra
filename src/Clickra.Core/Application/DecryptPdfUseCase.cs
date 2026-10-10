using Clickra.Core.Processors;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Application;

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
    protected override IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request) =>
        new HashSet<ConversionCapability> { ConversionCapability.Password };

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        string password = "";
        bool isRetry = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(
                    () => FileProcessor.DecryptPdf(
                        plan.Inputs[index],
                        plan.Outputs[index],
                        password,
                        (current, total, message) =>
                            ReportFileProgress(plan, index, progress, current, message),
                        cancellationToken),
                    cancellationToken);
                return;
            }
            catch (PdfReaderException ex) when (IsPasswordError(ex))
            {
                string? supplied = await interaction.RequestPasswordAsync(
                    index,
                    plan.Inputs[index],
                    isRetry,
                    cancellationToken);
                if (supplied is null)
                    throw new OperationCanceledException(cancellationToken);
                password = supplied;
                isRetry = true;
            }
        }
    }

    private static bool IsPasswordError(Exception ex) =>
        ex is PdfReaderException &&
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
