using System.Collections.Generic;

namespace Clickra.Core.Modules.Pdf.Translation.Pipeline;

public sealed class PdfLayoutRow
{
    public List<PdfLayoutElement> Elements { get; set; } = new List<PdfLayoutElement>();
}
