using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Shared option and validation behavior for Markdown document conversions.</summary>
public abstract class MarkdownConversionUseCaseBase : PerFileConversionUseCaseBase
{
    protected override string InputRequirementError => "At least one Markdown file is required.";

    protected override string UnsupportedInputError(string path) =>
        $"Unsupported Markdown input '{path}'.";

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request) =>
        MarkdownPdfOptions.Create(
            MarkdownPdfOptions.GetTheme(request.Options),
            MarkdownPdfOptions.GetPaper(request.Options),
            MarkdownPdfOptions.GetTextSize(request.Options),
            MarkdownPdfOptions.GetCodeTheme(request.Options),
            MarkdownPdfOptions.GetTemplatePath(request.Options));

    protected override object CreateExecutionState(ConversionPlan plan) =>
        new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal);

    protected override int GetInitialCompletedFiles(ConversionPlan plan) =>
        plan.ResumeStartIndex;
}
