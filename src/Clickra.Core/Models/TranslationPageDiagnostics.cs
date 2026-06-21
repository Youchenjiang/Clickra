namespace Clickra.Core.Models
{
    public class TranslationPageDiagnostics
    {
        public string SourcePath { get; set; } = "";
        public int PageNumber { get; set; }
        public double PageWidth { get; set; }
        public double PageHeight { get; set; }
        public int TableCount { get; set; }
        public List<TranslationRegionDiagnostics> TableMaskRegions { get; set; } = new();
        public List<TranslationRegionDiagnostics> DiagramMaskRegions { get; set; } = new();
        public List<TranslationRegionDiagnostics> FigureClipRegions { get; set; } = new();
        public List<TranslationRegionDiagnostics> GrayPromptShadedRegions { get; set; } = new();
        public List<TranslationRegionDiagnostics> EffectiveGrayMaskRegions { get; set; } = new();
        public List<TranslationParagraphDiagnostics> Paragraphs { get; set; } = new();
    }
}
