namespace Clickra.Core.Models
{
    public class TranslationRegionDiagnostics
    {
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double Width => X1 - X0;
        public double Height => Y1 - Y0;
    }
}
