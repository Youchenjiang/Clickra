using PdfSharp.Pdf.Annotations;

namespace Clickra.Core.Models
{
    public class ParagraphAnnotationInfo
    {
        public PdfAnnotation PdfAnnotation { get; set; } = null!;
        public string Text { get; set; } = "";
        public int OccurrenceIndex { get; set; }
        public int FirstLetterIndex { get; set; }
        public int LastLetterIndex { get; set; }
        public int TotalLetterCount { get; set; }
        public double RelCenterX { get; set; }
        public double RelCenterY { get; set; }
        public double RelWidth { get; set; }
    }
}
