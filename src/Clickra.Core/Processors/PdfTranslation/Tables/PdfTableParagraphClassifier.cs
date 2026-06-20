using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;
using UglyToad.PdfPig.Content;

namespace Clickra.Core.Processors
{
    internal static class PdfTableParagraphClassifier
    {
        public static bool IsTableCaptionWord(Word w, List<Word> words)
        {
            if (w.Text.Equals("表", StringComparison.OrdinalIgnoreCase))
            {
                // Geometric check for "表"
                double centerY = w.BoundingBox.Centroid.Y;
                double lineTolerance = w.BoundingBox.Height * 0.5;
                foreach (var other in words)
                {
                    if (other == w) continue;
                    if (Math.Abs(other.BoundingBox.Centroid.Y - centerY) < lineTolerance && other.BoundingBox.Right < w.BoundingBox.Left)
                    {
                        return false;
                    }
                }
                return true;
            }

            if (w.Text.Equals("Table", StringComparison.OrdinalIgnoreCase))
            {
                double centerY = w.BoundingBox.Centroid.Y;
                double lineTolerance = w.BoundingBox.Height * 0.5;

                // 1. Check if there is any word to the left on the same line
                foreach (var other in words)
                {
                    if (other == w) continue;
                    if (Math.Abs(other.BoundingBox.Centroid.Y - centerY) < lineTolerance && other.BoundingBox.Right < w.BoundingBox.Left)
                    {
                        return false;
                    }
                }

                // 2. Check preceding words in reading order (if they are close textually or temporally)
                int idx = words.IndexOf(w);
                if (idx > 0)
                {
                    var prevWord = words[idx - 1];
                    string prevText = prevWord.Text.Trim().ToLowerInvariant();
                    if (Math.Abs(prevWord.BoundingBox.Centroid.Y - centerY) < lineTolerance * 3.0)
                    {
                        var preps = new HashSet<string> {
                            "in", "see", "shown", "of", "and", "or", "from", "on", "with", "below", "above", "shows", "depicts", "illustrates", "to", "for", "at", "using", "the"
                        };
                        if (preps.Contains(prevText))
                        {
                            return false;
                        }

                        if (idx > 1)
                        {
                            var prev2Word = words[idx - 2];
                            string prev2Text = prev2Word.Text.Trim().ToLowerInvariant();
                            if (preps.Contains(prev2Text))
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }

            return false;
        }

        public static bool IsTableParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return true;

            // Section number fragments (e.g. "2", "2.1") are not table data.
            if (Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2})?$"))
                return false;

            int letterCount = txt.Count(char.IsLetter);
            if (letterCount == 0) return true;

            return false;
        }
    }
}
