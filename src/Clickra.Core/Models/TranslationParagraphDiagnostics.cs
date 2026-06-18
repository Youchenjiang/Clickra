namespace Clickra.Core.Models
{
    public class TranslationParagraphDiagnostics
    {
        public int Index { get; set; }
        public string Column { get; set; } = "";
        public string Text { get; set; } = "";
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double AverageFontSize { get; set; }
        public bool IsBypassed { get; set; }
        public bool IsTable { get; set; }
        public bool IsCode { get; set; }
        public bool IsDiagram { get; set; }
        public bool IsGrayPromptContent { get; set; }
        public bool WouldSkipRender { get; set; }
        public bool IsBodyProse { get; set; }
        public bool IsCalloutProse { get; set; }
        public bool IsHeading { get; set; }
        public int WordCount { get; set; }
        public bool HasPeriod { get; set; }
        public double Width => X1 - X0;
        public double Height => Y1 - Y0;
    }
}
