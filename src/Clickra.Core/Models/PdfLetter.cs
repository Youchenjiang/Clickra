namespace Clickra.Core.Models
{
    public class PdfLetter
    {
        public string Value { get; set; } = "";
        public string FontName { get; set; } = "";
        public double FontSize { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Left { get; set; }
        public double Bottom { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
        /// <summary>Source glyph baseline rotation in degrees (0, 90, -90, or 180).</summary>
        public double Rotation { get; set; }
        /// <summary>Whether the source glyph was emitted by a bold/medium face.</summary>
        public bool IsBold { get; set; }
    }
}
