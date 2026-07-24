using System;
using System.Collections.Generic;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfReferenceSectionBypasser
    {
        /// <summary>
        /// Marks bibliography entries as bypassed from the REFERENCES heading through the next major section.
        /// Heading itself remains translatable (e.g. REFERENCES → 參考文獻).
        /// </summary>
        public static void Apply(
            List<List<PdfParagraph>> allPages,
            double[] pageWidths,
            Func<List<PdfParagraph>, double, List<PdfParagraph>> getPageReadingOrder)
        {
            bool inSection = false;

            for (int p = 0; p < allPages.Count; p++)
            {
                double pageWidth = p < pageWidths.Length ? pageWidths[p] : 595.0;
                foreach (var para in getPageReadingOrder(allPages[p], pageWidth))
                {
                    if (ReferenceSectionDetector.IsHeading(para))
                    {
                        inSection = true;
                        ClickraDebug.LogReferenceState(p + 1, "start", para.TextWithPlaceholders);
                        continue;
                    }

                    if (inSection && ReferenceSectionDetector.IsTerminator(para))
                    {
                        inSection = false;
                        ClickraDebug.LogReferenceState(p + 1, "stop", para.TextWithPlaceholders);
                        continue;
                    }

                    // Once the bibliography heading is reached, every
                    // paragraph belongs to the reference section until an
                    // explicit major-section terminator. Continuation lines
                    // may start with lowercase words or an author initial and
                    // must not be translated merely because they fail the
                    // numbered-entry heuristic (e.g. ASTER [25]).
                    if (inSection && !para.IsTable)
                    {
                        para.IsBypassed = true;
                    }
                }
            }
        }
    }
}
