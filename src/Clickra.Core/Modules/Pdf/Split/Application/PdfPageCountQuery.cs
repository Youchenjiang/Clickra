using Clickra.Core.Modules.Pdf.Split.Processing;

namespace Clickra.Core.Modules.Pdf.Split.Application;

/// <summary>Application query boundary for reading a PDF page count and encryption status without exposing processors to presentation code.</summary>
public static class PdfPageCountQuery
{
    public static int Get(string filePath, string? password = null) => PdfSplitProcessor.GetPageCount(filePath, password);

    public static bool IsPasswordProtected(string filePath) => PdfSplitProcessor.IsPasswordProtected(filePath);
}
