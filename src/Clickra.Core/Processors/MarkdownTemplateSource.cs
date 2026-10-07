using System;
using System.IO;

namespace Clickra.Core.Processors;

/// <summary>Loads a user-supplied Markdown document template from a supported source format.</summary>
public static class MarkdownTemplateSource
{
    public static MarkdownDocumentTemplate Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Template path is required.", nameof(path));
        string extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".docx" => MarkdownDocxTemplateFile.Load(path),
            ".json" => MarkdownTemplateFile.Load(path),
            _ => throw new InvalidDataException("Markdown template must be a DOCX file or Clickra JSON template.")
        };
    }
}
