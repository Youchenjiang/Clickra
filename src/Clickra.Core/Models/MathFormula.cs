namespace Clickra.Core.Models
{
    public class MathFormula
    {
        public int Id { get; set; }
        public List<MathLetter> Letters { get; set; } = new List<MathLetter>();
        public double Width { get; set; }

        public MathFormula() { }

        public MathFormula(int id, List<UglyToad.PdfPig.Content.Letter> letters)
        {
            Id = id;
            double minX = letters.Min(l => l.Location.X);
            foreach (var l in letters)
            {
                Letters.Add(new MathLetter
                {
                    Value = (l.Value ?? "").Replace('\u2217', '*'),
                    FontName = l.FontName ?? "Times New Roman",
                    FontSize = l.PointSize,
                    X = l.Location.X,
                    Y = l.Location.Y,
                    RelativeX = l.Location.X - minX,
                    RelativeY = l.Location.Y - letters[0].Location.Y
                });
            }
            Width = letters.Max(l => l.GlyphRectangle.Right) - minX;
        }
    }
}
