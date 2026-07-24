using PdfSharp.Pdf.Annotations;

namespace Clickra.Core.Models
{
    public class ParagraphAnnotationInfo
    {
        public required PdfAnnotation PdfAnnotation { get; set; }
        public string Text { get; set; } = "";
        public int OccurrenceIndex { get; set; }
        /// <summary>Ordinal among figure references in the source paragraph.</summary>
        public int FigureOccurrenceIndex { get; set; } = -1;
        public int FirstLetterIndex { get; set; }
        public int LastLetterIndex { get; set; }
        public int TotalLetterCount { get; set; }
        public double RelCenterX { get; set; }
        public double RelCenterY { get; set; }
        public double RelWidth { get; set; }
    }
}
