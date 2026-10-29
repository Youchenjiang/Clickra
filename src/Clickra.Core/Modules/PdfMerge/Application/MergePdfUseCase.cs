using Clickra.Core.Application;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Modules.PdfMerge.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfMerge.Application;

/// <summary>Authoritative application workflow for merging PDF files into one document.</summary>
public sealed class MergePdfUseCase : SingleOutputConversionUseCaseBase
{
    public const string CommandName = "merge-pdf";
    public const string OutputFileName = "Merged_PDF.pdf";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(MergePdfUseCase);
    protected override string InputRequirementError => "At least two PDF files are required.";
    protected override string PlannedOutputFileName => OutputFileName;
    protected override string OutputCountError => "PDF merge requires exactly one output.";
    protected override string UnsupportedInputError(string path) => $"Unsupported merge input '{path}'.";

    protected override ConversionValidationResult ValidateInputs(ConversionRequest request)
    {
        if (request.InputFiles.Any(f => File.Exists(f) && PdfEncryptionQuery.IsEncrypted(f)))
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(request.Options);
            return ConversionValidationResult.Failure(Localization.T("pdf_err_encrypted_merge", lang));
        }

        return ConversionValidationResult.Success();
    }

    protected override async Task ExecuteSingleAsync(
        ConversionPlan plan,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var processor = new PdfMergeProcessor();
        await Task.Run(
            () => processor.Process(
                plan.Inputs.ToList(),
                plan.Outputs[0],
                null,
                (current, total, message) => progress?.Report(
                    new ConversionProgress(current, total, message)),
                cancellationToken),
            cancellationToken);
    }
}
