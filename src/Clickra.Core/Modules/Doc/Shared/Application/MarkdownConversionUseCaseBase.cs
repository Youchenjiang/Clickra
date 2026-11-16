using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Application;
using Clickra.Core.Storage;
using Clickra.Core.Modules.Doc.Shared.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Doc.Shared.Application;

/// <summary>Shared option and validation behavior for Markdown document conversions.</summary>
public abstract class MarkdownConversionUseCaseBase : PerFileConversionUseCaseBase
{
    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.MarkdownOptions };

    protected override IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request)
    {
        if (request.Options is not null &&
            (request.Options.ContainsKey(MarkdownPdfOptions.ThemeKey) ||
             request.Options.ContainsKey(MarkdownPdfOptions.PaperKey) ||
             request.Options.ContainsKey(MarkdownPdfOptions.TextSizeKey) ||
             request.Options.ContainsKey(MarkdownPdfOptions.CodeThemeKey) ||
             request.Options.ContainsKey(MarkdownPdfOptions.TemplatePathKey)))
        {
            return new HashSet<ConversionCapability>();
        }

        return Capabilities;
    }

    protected override string InputRequirementError => "At least one Markdown file is required.";

    protected override string UnsupportedInputError(string path) =>
        $"Unsupported Markdown input '{path}'.";

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        // Whatever the caller leaves out comes from 設定 › 功能預設值 (right-click runs pass nothing).
        var source = request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);
        if (!source.ContainsKey(MarkdownPdfOptions.ThemeKey) && !source.ContainsKey(MarkdownPdfOptions.TemplatePathKey))
        {
            string savedTheme = SettingsController.MarkdownTheme;
            if (savedTheme == ClickraSettings.MarkdownThemeWordTemplate)
            {
                string path = SettingsController.MarkdownTemplatePath;
                if (!string.IsNullOrWhiteSpace(path)) source[MarkdownPdfOptions.TemplatePathKey] = path;
            }
            else
            {
                source[MarkdownPdfOptions.ThemeKey] = savedTheme;
            }
        }
        source.TryAdd(MarkdownPdfOptions.PaperKey, SettingsController.MarkdownPaper);
        source.TryAdd(MarkdownPdfOptions.TextSizeKey, SettingsController.MarkdownTextSize);
        source.TryAdd(MarkdownPdfOptions.CodeThemeKey, SettingsController.MarkdownCodeTheme);

        Dictionary<string, object> options = MarkdownPdfOptions.Create(
            MarkdownPdfOptions.GetTheme(source),
            MarkdownPdfOptions.GetPaper(source),
            MarkdownPdfOptions.GetTextSize(source),
            MarkdownPdfOptions.GetCodeTheme(source),
            MarkdownPdfOptions.GetTemplatePath(source));
        string language = request.Options?.TryGetValue(ExecutionLocalizationOptions.LanguageKey, out object? lang) == true && lang is not null
            ? lang.ToString()!
            : SettingsController.Language;
        options[ExecutionLocalizationOptions.LanguageKey] = language;
        return options;
    }

    protected override object CreateExecutionState(ConversionPlan plan) =>
        new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal);

    protected override int GetInitialCompletedFiles(ConversionPlan plan) =>
        plan.ResumeStartIndex;
}
