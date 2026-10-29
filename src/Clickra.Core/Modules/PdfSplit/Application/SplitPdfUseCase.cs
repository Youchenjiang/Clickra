using Clickra.Core.Application;
using Clickra.Core.Modules.PdfSplit.Processing;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.PdfSplit.Application;

/// <summary>Authoritative application workflow for splitting PDF files.</summary>
public sealed class SplitPdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "split-pdf";
    public const string PagesOptionKey = "pages";
    public const string PromptPages = "prompt";

    public override string Command => CommandName;
    protected override string UseCaseName => nameof(SplitPdfUseCase);
    protected override string InputRequirementError => "At least one PDF file is required.";
    protected override string OutputCountError => "Split conversion requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported split input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + "_split.pdf";

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        string pages = PromptPages;
        if (request.Options is not null &&
            request.Options.TryGetValue(PagesOptionKey, out object? pagesValue) &&
            pagesValue is string suppliedPages &&
            !string.IsNullOrWhiteSpace(suppliedPages))
        {
            pages = suppliedPages.Trim();
        }
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [PagesOptionKey] = pages
        };
    }

    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.SplitPages };

    protected override IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request)
    {
        string pages = NormalizeOptions(request)[PagesOptionKey].ToString() ?? PromptPages;
        return pages.Equals(PromptPages, StringComparison.OrdinalIgnoreCase)
            ? new HashSet<ConversionCapability> { ConversionCapability.SplitPages }
            : new HashSet<ConversionCapability>();
    }

    protected override object CreateExecutionState(ConversionPlan plan) =>
        plan.NormalizedOptions.TryGetValue(PagesOptionKey, out object? pagesValue) &&
        pagesValue is string suppliedPages
            ? suppliedPages
            : PromptPages;

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        string pages = executionState as string ?? PromptPages;
        bool requiresPrompt = pages.Equals(PromptPages, StringComparison.OrdinalIgnoreCase);
        string? targetPages = requiresPrompt
            ? await interaction.RequestSplitPagesAsync(index, plan.Inputs[index], cancellationToken)
            : pages;
        if (string.IsNullOrWhiteSpace(targetPages))
            throw new OperationCanceledException(cancellationToken);

        string? initialPassword = plan.NormalizedOptions.TryGetValue("password", out var pwdObj) && pwdObj is string pwdStr
            ? pwdStr
            : null;

        await ExecuteWithPasswordRetryAsync(
            index,
            plan,
            interaction,
            initialPassword,
            async password =>
            {
                var processor = new PdfSplitProcessor();
                var options = new Dictionary<string, object> { { "pages", targetPages } };
                if (!string.IsNullOrEmpty(password))
                {
                    options["password"] = password;
                }

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
