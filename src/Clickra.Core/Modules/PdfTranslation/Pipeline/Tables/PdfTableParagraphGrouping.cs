using System;
using System.Collections.Generic;
using Clickra.Core.Modules.PdfTranslation.Model;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline;

internal static class PdfTableParagraphGrouping
{
    public static List<List<PdfParagraph>> GroupNearby(
        List<PdfParagraph> candidates, Func<PdfParagraph, bool> belongsToLeftColumn)
    {
        var groups = new List<List<PdfParagraph>>();
        foreach (var candidate in candidates)
        {
            bool added = false;
            foreach (var group in groups)
            {
                if (!IsNearGroup(candidate, group, belongsToLeftColumn)) continue;
                group.Add(candidate);
                added = true;
                break;
            }
            if (!added) groups.Add(new List<PdfParagraph> { candidate });
        }
        return groups;
    }

    private static bool IsNearGroup(PdfParagraph candidate, List<PdfParagraph> group,
        Func<PdfParagraph, bool> belongsToLeftColumn)
    {
        foreach (var member in group)
        {
            if (belongsToLeftColumn(candidate) != belongsToLeftColumn(member)) continue;
            double verticalDistance = 0;
            if (candidate.Y1 < member.Y0)
                verticalDistance = member.Y0 - candidate.Y1;
            else if (member.Y1 < candidate.Y0)
                verticalDistance = candidate.Y0 - member.Y1;
            if (verticalDistance < 45) return true;
        }
        return false;
    }
}
