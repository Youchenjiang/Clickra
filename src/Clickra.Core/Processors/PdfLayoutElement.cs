namespace Clickra.Core.Processors
{
    public sealed class PdfLayoutElement
    {
        public string Text { get; set; } = "";
        public bool IsFormula { get; set; }
        public int FormulaId { get; set; }
        public double Width { get; set; }
    }
}
