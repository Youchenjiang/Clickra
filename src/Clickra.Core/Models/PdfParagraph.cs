using Clickra.Core.Processors;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Models
{
    public enum PdfParagraphSemanticRole
    {
        Unknown,
        PageTitle,
        AbstractHeading,
        SectionHeading,
        SubsectionHeading,
        FigureCaption,
        Body,
        Protected
    }

    public class PdfParagraph
    {
        public enum TextAlignment
        {
            Left,
            Center,
            Right
        }

        public static readonly Regex MathFontRegex = new(
            @"^(?:CMMI|CMSY|CMEX|CMIB|CMBSY|cmmi|cmsy|cmex|cmib|cmbsy|lasy|rsfs|txsy|wasy|stmary|XY|bbld|line\d*|lcircle\d*|TeX-|MS[AB]|MT(?:MI|SY|EX|2)|EU[RSF])|" +
            @"(?:Mono|Code|Math|Sym|Wingdings|Webdings|Dingbats|Courier|Console|Inconsolata|Typewriter|NimbusMon|MonL|cmtt|ectt|sftt|\btt\d+|Teletype)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public string TextWithPlaceholders { get; set; } = "";
        /// <summary>Source text annotated with inline style markers for translation.</summary>
        public string TranslationTextWithStyles { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double OriginalX0 { get; private set; }
        public double OriginalY0 { get; private set; }
        public double OriginalX1 { get; private set; }
        public double OriginalY1 { get; private set; }
        public double AverageFontSize { get; set; }
        public bool IsOnlyMath { get; set; }
        public bool IsCode { get; set; }
        public bool IsBypassed { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsTable { get; set; }
        public bool IsDiagram { get; set; }
        /// <summary>Paragraph belongs to a gray System Message / Prompt box; keep English.</summary>
        public bool IsGrayPromptContent { get; set; }
        public bool IsPageTitle { get; set; }
        /// <summary>Semantic role captured from the source before translation.</summary>
        public PdfParagraphSemanticRole SemanticRole { get; set; }
        /// <summary>Largest source glyph size; unlike AverageFontSize this survives title groups.</summary>
        public double SourceVisualFontSize { get; set; }
        public double SourceLineHeight { get; set; }
        /// <summary>Planner-provided effective font size for a continuation line.</summary>
        public double LayoutFontSizeOverride { get; set; }
        /// <summary>Planner-provided leading for vertically balanced translated prose.</summary>
        public double LayoutLineSpacingMultiplierOverride { get; set; }
        public string TranslationGroupId { get; set; } = string.Empty;
        public bool brk { get; set; }
        public List<MathFormula> Formulas { get; set; } = new List<MathFormula>();
        public object TextDirection { get; set; } = "Rotate0";
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public List<PdfLetter> AllLetters { get; set; } = new List<PdfLetter>();
        public List<ParagraphAnnotationInfo> Annotations { get; set; } = new();

        public double Width => X1 - X0;
        public double Height => Y1 - Y0;

        public PdfParagraph(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                X0 = 0; Y0 = 0; X1 = 0; Y1 = 0;
                return;
            }

            ApplyAnalysis(PdfParagraphAnalyzer.Analyze(lines));
            ApplyLayout(PdfParagraphLayoutAnalyzer.Analyze(lines, TextWithPlaceholders));
        }

        private void ApplyLayout(PdfParagraphLayoutAnalysis layout)
        {
            X0 = layout.X0;
            Y0 = layout.Y0;
            X1 = layout.X1;
            Y1 = layout.Y1;
            OriginalX0 = X0;
            OriginalY0 = Y0;
            OriginalX1 = X1;
            OriginalY1 = Y1;
            TextDirection = layout.TextDirection;
            Alignment = layout.Alignment;
        }

        private void ApplyAnalysis(PdfParagraphAnalysis analysis)
        {
            TextWithPlaceholders = analysis.TextWithPlaceholders;
            TranslationTextWithStyles = analysis.TranslationTextWithStyles;
            AverageFontSize = analysis.AverageFontSize;
            IsBold = analysis.IsBold;
            IsItalic = analysis.IsItalic;
            IsOnlyMath = analysis.IsOnlyMath;
            IsCode = analysis.IsCode;
            brk = analysis.HasLineBreak;
            Formulas = analysis.Formulas;
            AllLetters = analysis.AllLetters;
            SourceVisualFontSize = AllLetters.Count == 0 ? AverageFontSize : AllLetters.Max(l => l.FontSize);
            SourceLineHeight = AllLetters.Count == 0 ? 0 : AllLetters.Max(l => l.Top - l.Bottom);
        }

        public static bool IsMathLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line)
        {
            return PdfParagraphMathClassifier.IsMathLine(line);
        }

        public static IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> MergeHorizontalLines(IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> initialLines)
        {
            return PdfTextLineMerger.MergeHorizontalLines(initialLines);
        }

        public void MergeWith(PdfParagraph other)
        {
            if (other == null) return;

            int formulaIdOffset = this.Formulas.Count;
            foreach (var formula in other.Formulas)
            {
                var newFormula = new MathFormula
                {
                    Id = formula.Id + formulaIdOffset,
                    Letters = formula.Letters,
                    Width = formula.Width
                };
                this.Formulas.Add(newFormula);
            }

            string otherText = AdjustOtherTextFormulaIds(other.TextWithPlaceholders, formulaIdOffset);

            this.TextWithPlaceholders = string.IsNullOrWhiteSpace(this.TextWithPlaceholders)
                ? otherText
                : this.TextWithPlaceholders + " " + otherText;

            string otherStyled = string.IsNullOrWhiteSpace(other.TranslationTextWithStyles)
                ? otherText
                : other.TranslationTextWithStyles;
            this.TranslationTextWithStyles = string.IsNullOrWhiteSpace(this.TranslationTextWithStyles)
                ? otherStyled
                : $"{this.TranslationTextWithStyles} {otherStyled}";

            this.AllLetters.AddRange(other.AllLetters);

            this.X0 = Math.Min(this.X0, other.X0);
            this.Y0 = Math.Min(this.Y0, other.Y0);
            this.X1 = Math.Max(this.X1, other.X1);
            this.Y1 = Math.Max(this.Y1, other.Y1);

            this.OriginalX0 = Math.Min(this.OriginalX0, other.OriginalX0);
            this.OriginalY0 = Math.Min(this.OriginalY0, other.OriginalY0);
            this.OriginalX1 = Math.Max(this.OriginalX1, other.OriginalX1);
            this.OriginalY1 = Math.Max(this.OriginalY1, other.OriginalY1);

            if (this.AllLetters.Count > 0)
            {
                this.AverageFontSize = this.AllLetters.Average(l => l.FontSize);
                this.SourceVisualFontSize = this.AllLetters.Max(l => l.FontSize);
                this.SourceLineHeight = this.AllLetters.Max(l => l.Top - l.Bottom);
            }

            this.brk = true;
            this.IsBold = this.IsBold || other.IsBold;
            this.IsOnlyMath = this.Formulas.Count == 1 && this.TextWithPlaceholders.Trim() == "{v0}";
            this.IsCode = this.IsCode || other.IsCode;
            this.IsDiagram = this.IsDiagram || other.IsDiagram;
            this.IsGrayPromptContent = this.IsGrayPromptContent || other.IsGrayPromptContent;
        }
        private static string AdjustOtherTextFormulaIds(string text, int offset)
        {
            if (string.IsNullOrEmpty(text) || offset <= 0) return text ?? string.Empty;
            return Regex.Replace(text, @"\{v(\d+)\}", m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int oldId))
                {
                    return $"{{v{oldId + offset}}}";
                }
                return m.Value;
            }, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
    }
}
