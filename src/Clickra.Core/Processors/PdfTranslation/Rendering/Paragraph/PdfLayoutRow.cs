using System.Collections.Generic;

namespace Clickra.Core.Processors
{
    public sealed class PdfLayoutRow
    {
        public List<PdfLayoutElement> Elements { get; set; } = new List<PdfLayoutElement>();
    }
}
