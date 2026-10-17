using System.Collections.Generic;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline;

public sealed class PdfLayoutRow
{
    public List<PdfLayoutElement> Elements { get; set; } = new List<PdfLayoutElement>();
}
