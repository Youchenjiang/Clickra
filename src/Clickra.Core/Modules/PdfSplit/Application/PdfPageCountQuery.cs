using Clickra.Core.Modules.PdfSplit.Processing;

namespace Clickra.Core.Modules.PdfSplit.Application;

/// <summary>Application query boundary for reading a PDF page count without exposing processors to presentation code.</summary>
public static class PdfPageCountQuery
{
    public static int Get(string filePath) => PdfSplitProcessor.GetPageCount(filePath);
}
