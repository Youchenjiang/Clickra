using System.Collections.Generic;
using Clickra.Core.Modules.Markdown.Application;

namespace Clickra.Core.Modules.Markdown.Processing;

/// <summary>
/// Renderer-neutral Markdown presentation state resolved once from conversion options.
/// PDF and DOCX backends consume this profile instead of interpreting option keys independently.
/// </summary>
internal sealed record MarkdownRenderProfile(
    MarkdownDocumentTemplate Template,
    MarkdownResolvedLayout Layout,
    MarkdownResolvedPalette Palette,
    string Paper,
    double Scale,
    bool LightCode)
{
    /// <summary>Resolves normalized Markdown conversion options into backend-ready presentation state.</summary>
    public static MarkdownRenderProfile Resolve(IReadOnlyDictionary<string, object>? options)
    {
        MarkdownDocumentTemplate template = MarkdownTemplateCatalog.Resolve(
            MarkdownPdfOptions.GetTheme(options),
            MarkdownPdfOptions.GetTemplatePath(options));
        double scale = MarkdownPdfOptions.GetTextSize(options) switch
        {
            MarkdownPdfOptions.TextSmall => 0.9,
            MarkdownPdfOptions.TextLarge => 1.12,
            _ => 1.0
        };

        return new MarkdownRenderProfile(
            template,
            MarkdownResolvedLayout.Create(template, scale),
            MarkdownResolvedPalette.Create(template),
            MarkdownPdfOptions.GetPaper(options),
            scale,
            MarkdownPdfOptions.GetCodeTheme(options) == MarkdownPdfOptions.CodeLight);
    }
}
