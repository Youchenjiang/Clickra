using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clickra.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Clickra.Core.Processors
{
    internal sealed class ProtectedNoStripPredicates
    {
        public required Func<PdfParagraph, bool> IsGrayPromptCodeParagraph { get; init; }
        public required Func<PdfParagraph, IReadOnlyList<TableMaskRegion>, bool> ParagraphCenterInsideAnyRegion { get; init; }
        public required Func<PdfParagraph, IReadOnlyList<TableMaskRegion>, bool> IsParagraphInsideGrayShadedRegion { get; init; }
        public required Func<PdfParagraph, bool> IsLikelyChartLabel { get; init; }
    }

    internal static class PdfFontStripper
    {
        public static HashSet<string> CollectTranslatableFontBaseNames(IEnumerable<PdfParagraph> paragraphs)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var para in paragraphs)
            {
                if (para.IsBypassed) continue;
                foreach (var letter in para.AllLetters)
                {
                    string cleanFontName = CleanPdfBaseFontName(letter.FontName);
                    if (string.IsNullOrEmpty(cleanFontName)) continue;
                    if (PdfParagraph.MathFontRegex.IsMatch(cleanFontName)) continue;
                    names.Add(cleanFontName);
                }
            }

            return names;
        }

        public static HashSet<string> CollectFontsUsedOnlyInProtectedRegions(
            IEnumerable<PdfParagraph> paragraphs,
            IReadOnlyList<TableMaskRegion> grayRegions,
            int pageIndex,
            double pageHeight,
            ProtectedNoStripPredicates predicates)
        {
            var fontHasUnprotectedUse = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var pageList = paragraphs as List<PdfParagraph> ?? paragraphs.ToList();

            foreach (var para in pageList)
            {
                bool inProtected = IsParagraphInProtectedNoStripZone(
                    para, grayRegions, pageIndex, pageHeight, pageList, predicates);
                foreach (var letter in para.AllLetters)
                {
                    string cleanFontName = CleanPdfBaseFontName(letter.FontName);
                    if (string.IsNullOrEmpty(cleanFontName)) continue;
                    if (PdfParagraph.MathFontRegex.IsMatch(cleanFontName)) continue;
                    if (!inProtected)
                        fontHasUnprotectedUse[cleanFontName] = true;
                    else if (!fontHasUnprotectedUse.ContainsKey(cleanFontName))
                        fontHasUnprotectedUse[cleanFontName] = false;
                }
            }

            var onlyProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fontHasUnprotectedUse)
            {
                if (!kv.Value) onlyProtected.Add(kv.Key);
            }
            return onlyProtected;
        }

        public static HashSet<string> CollectFontsUsedByPageOneAuthorBlock(
            IEnumerable<PdfParagraph> paragraphs,
            double pageHeight)
        {
            var pageList = paragraphs as List<PdfParagraph> ?? paragraphs.ToList();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var para in pageList)
            {
                if (!PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, pageHeight))
                    continue;
                foreach (var letter in para.AllLetters)
                {
                    string cleanFontName = CleanPdfBaseFontName(letter.FontName);
                    if (string.IsNullOrEmpty(cleanFontName)) continue;
                    if (PdfParagraph.MathFontRegex.IsMatch(cleanFontName)) continue;
                    names.Add(cleanFontName);
                }
            }
            return names;
        }

        public static bool ParagraphUsesStrippedFont(PdfParagraph para, HashSet<string> strippedBaseFonts)
        {
            if (strippedBaseFonts.Count == 0) return false;
            foreach (var letter in para.AllLetters)
            {
                string cleanFontName = CleanPdfBaseFontName(letter.FontName);
                if (!string.IsNullOrEmpty(cleanFontName) && strippedBaseFonts.Contains(cleanFontName))
                {
                    return true;
                }
            }
            return false;
        }

        public static HashSet<string> StripTextFromPage(
            PdfPage page, IReadOnlyCollection<string> translatableBaseFontNames)
        {
            var strippedBaseFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null) return strippedBaseFonts;

            var fonts = resources.Elements.GetDictionary("/Font");
            if (fonts == null) return strippedBaseFonts;

            var fontsToStrip = new HashSet<string>();
            foreach (var key in fonts.Elements.KeyNames)
            {
                var fontItem = fonts.Elements[key];
                if (fontItem is PdfReference reference) fontItem = reference.Value;
                if (fontItem is PdfDictionary fontDict)
                {
                    var baseFont = fontDict.Elements.GetName("/BaseFont");
                    if (!string.IsNullOrEmpty(baseFont))
                    {
                        string cleanFontName = CleanPdfBaseFontName(baseFont);
                        bool isMathOrCode = PdfParagraph.MathFontRegex.IsMatch(cleanFontName);
                        if (!isMathOrCode && translatableBaseFontNames.Contains(cleanFontName))
                        {
                            fontsToStrip.Add(key.ToString().TrimStart('/'));
                            strippedBaseFonts.Add(cleanFontName);
                        }
                    }
                }
            }

            if (fontsToStrip.Count == 0) return strippedBaseFonts;

            // A PDF page may store one logical content stream in an array of
            // separate streams.  The array can split in the middle of a text
            // run (ASTER page 1 does this immediately after the abstract's
            // first line), so the active font must survive the stream
            // boundary.  Resetting it for every stream leaves the remainder
            // of that run visible after the translated text is drawn.
            bool stripActive = false;
            var tokens = new List<string>();
            var contents = page.Contents;
            for (int i = 0; i < contents.Elements.Count; i++)
            {
                var contentObj = contents.Elements[i];
                if (contentObj is PdfReference reference) contentObj = reference.Value;
                if (contentObj is PdfDictionary contentDict && contentDict.Stream != null)
                {
                    byte[] decompressedBytes = contentDict.Stream.UnfilteredValue;
                    byte[] cleanBytes = StripSelectedText(
                        decompressedBytes, fontsToStrip, tokens, ref stripActive);
                    contentDict.Stream.Value = cleanBytes;
                    contentDict.Elements.Remove("/Filter");
                }
            }

            return strippedBaseFonts;
        }

        private static string CleanPdfBaseFontName(string? baseFont)
        {
            if (string.IsNullOrEmpty(baseFont)) return "";
            string cleanFontName = baseFont.Replace("/", "").Trim();
            int plusIdx = cleanFontName.IndexOf('+');
            if (plusIdx >= 0 && plusIdx < cleanFontName.Length - 1)
            {
                cleanFontName = cleanFontName.Substring(plusIdx + 1);
            }
            return cleanFontName;
        }

        private static bool IsParagraphInProtectedNoStripZone(
            PdfParagraph para,
            IReadOnlyList<TableMaskRegion> grayRegions,
            int pageIndex,
            double pageHeight,
            List<PdfParagraph> pageList,
            ProtectedNoStripPredicates predicates)
        {
            if (pageIndex == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, pageHeight))
                return true;
            if (para.IsGrayPromptContent || predicates.IsGrayPromptCodeParagraph(para))
            {
                if (grayRegions.Count == 0) return true;
                return predicates.ParagraphCenterInsideAnyRegion(para, grayRegions) ||
                       predicates.IsParagraphInsideGrayShadedRegion(para, grayRegions);
            }
            if (para.IsBypassed && (para.IsDiagram || para.IsCode || predicates.IsLikelyChartLabel(para)))
            {
                if (grayRegions.Count == 0) return para.IsDiagram || para.IsCode;
                return predicates.ParagraphCenterInsideAnyRegion(para, grayRegions) ||
                       predicates.IsParagraphInsideGrayShadedRegion(para, grayRegions);
            }
            return false;
        }

        private static byte[] StripSelectedText(
            byte[] contentBytes,
            HashSet<string> fontsToStrip,
            List<string> tokens,
            ref bool stripActive)
        {
            using var ms = new MemoryStream();
            int i = 0;
            int len = contentBytes.Length;

            while (i < len)
            {
                byte b = contentBytes[i];

                if (b == '(')
                {
                    ProcessParenthesisString(contentBytes, len, stripActive, ms, ref i);
                    continue;
                }

                if (b == '<')
                {
                    if (i + 1 < len && contentBytes[i + 1] == '<')
                    {
                        ms.WriteByte((byte)'<');
                        ms.WriteByte((byte)'<');
                        i += 2;
                        continue;
                    }

                    ProcessAngleString(contentBytes, len, stripActive, ms, ref i);
                    continue;
                }

                if (IsDelimiter(contentBytes, i))
                {
                    ms.WriteByte(b);
                    i++;
                    continue;
                }

                ProcessOperatorToken(contentBytes, len, fontsToStrip, tokens, ms, ref i, ref stripActive);
            }

            return ms.ToArray();
        }

        private static void ProcessParenthesisString(byte[] contentBytes, int len, bool stripActive, MemoryStream ms, ref int i)
        {
            int start = i;
            i++;
            int escapeCount = 0;
            while (i < len)
            {
                byte sb = contentBytes[i];
                if (sb == '\\')
                    escapeCount = (escapeCount + 1) % 2;
                else if (sb == ')' && escapeCount == 0)
                {
                    i++;
                    break;
                }
                else
                    escapeCount = 0;
                i++;
            }
            int end = i;

            if (stripActive)
            {
                ms.WriteByte((byte)'(');
                ms.WriteByte((byte)')');
            }
            else
            {
                ms.Write(contentBytes, start, end - start);
            }
        }

        private static void ProcessAngleString(byte[] contentBytes, int len, bool stripActive, MemoryStream ms, ref int i)
        {
            int start = i;
            i++;
            while (i < len && contentBytes[i] != '>') i++;
            if (i < len) i++;
            int end = i;

            if (stripActive)
            {
                ms.WriteByte((byte)'<');
                ms.WriteByte((byte)'>');
            }
            else
            {
                ms.Write(contentBytes, start, end - start);
            }
        }

        private static void ProcessOperatorToken(
            byte[] contentBytes,
            int len,
            HashSet<string> fontsToStrip,
            List<string> tokens,
            MemoryStream ms,
            ref int i,
            ref bool stripActive)
        {
            int tokenStart = i;
            while (i < len && !IsDelimiter(contentBytes, i) && contentBytes[i] != '(' && contentBytes[i] != '<')
            {
                i++;
            }
            int tokenLen = i - tokenStart;
            string token = Encoding.ASCII.GetString(contentBytes, tokenStart, tokenLen);
            ms.Write(contentBytes, tokenStart, tokenLen);

            tokens.Add(token);
            if (tokens.Count > 3) tokens.RemoveAt(0);

            if (token == "Tf" && tokens.Count >= 3)
            {
                string fontName = tokens[tokens.Count - 3];
                stripActive = fontsToStrip.Contains(fontName.TrimStart('/'));
            }
        }

        private static bool IsDelimiter(byte[] bytes, int index)
        {
            byte b = bytes[index];
            return b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32 ||
                   b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' ||
                   b == '{' || b == '}' || b == '/' || b == '%';
        }
    }
}
