namespace Clickra.Core.Modules.Pdf.Print.Application;

public sealed record PrintPageSize(int PageNumber, float WidthPt, float HeightPt);

public readonly record struct PrintRect(float X, float Y, float Width, float Height);

public sealed record PrintCell(int PageNumber, PrintRect Cell, PrintRect Page, bool Overflows);

public sealed record PrintSheet(bool Landscape, bool IsBack, IReadOnlyList<PrintCell> Cells);

public static class PrintLayoutPlanner
{
    private const float Epsilon = 1e-4f;
    private const float MarginRatio = 0.03f;
    private const float GapRatio = 0.02f;

    public static IReadOnlyList<PrintSheet> Plan(
        IReadOnlyList<PrintPageSize> pages,
        float paperWidthPt,
        float paperHeightPt,
        PrintPdfOptions options)
    {
        if (pages is null || pages.Count == 0)
        {
            return Array.Empty<PrintSheet>();
        }

        float pW = Math.Abs(paperWidthPt);
        float pH = Math.Abs(paperHeightPt);
        float portW = Math.Min(pW, pH);
        float portH = Math.Max(pW, pH);
        if (portW <= Epsilon || portH <= Epsilon)
        {
            portW = 595.28f; // Default A4 portrait
            portH = 841.89f;
        }

        int pps = options.PagesPerSheet is 1 or 2 or 4 or 6 or 9 or 16 ? options.PagesPerSheet : 1;
        bool orderN = string.Equals(options.PageOrder, "N", StringComparison.OrdinalIgnoreCase);

        var sheets = new List<PrintSheet>();
        int totalSheets = (pages.Count + pps - 1) / pps;

        for (int s = 0; s < totalSheets; s++)
        {
            int startIdx = s * pps;
            int count = Math.Min(pps, pages.Count - startIdx);
            var sheetPages = new List<PrintPageSize>(count);
            for (int i = 0; i < count; i++)
            {
                sheetPages.Add(pages[startIdx + i]);
            }

            var firstPage = sheetPages[0];
            bool isLandscape = DetermineSheetOrientation(options.Orientation, firstPage, portW, portH, pps);

            float sheetWidth = isLandscape ? portH : portW;
            float sheetHeight = isLandscape ? portW : portH;
            float shortSide = Math.Min(sheetWidth, sheetHeight);
            float margin = shortSide * MarginRatio;
            float availW = Math.Max(0f, sheetWidth - 2f * margin);
            float availH = Math.Max(0f, sheetHeight - 2f * margin);

            (int cols, int rows) = GetGrid(pps, isLandscape);
            float gap = pps >= 2 ? shortSide * GapRatio : 0f;
            float cellW = Math.Max(0f, (availW - (cols - 1) * gap) / cols);
            float cellH = Math.Max(0f, (availH - (rows - 1) * gap) / rows);

            var cells = new List<PrintCell>(sheetPages.Count);
            for (int i = 0; i < sheetPages.Count; i++)
            {
                var page = sheetPages[i];
                int col = orderN ? i / rows : i % cols;
                int row = orderN ? i % rows : i / cols;

                float cellX = margin + col * (cellW + gap);
                float cellY = margin + row * (cellH + gap);
                var cellRect = new PrintRect(cellX, cellY, cellW, cellH);

                PrintRect pageRect;
                bool overflows = false;

                if (pps > 1)
                {
                    pageRect = CalculateFitRect(page, cellX, cellY, cellW, cellH);
                }
                else
                {
                    string scaleMode = options.Scale?.ToLowerInvariant() ?? "fit";
                    if (scaleMode == "actual")
                    {
                        pageRect = CalculateActualRect(page, cellX, cellY, cellW, cellH);
                        overflows = pageRect.Width > cellW + 1e-3f || pageRect.Height > cellH + 1e-3f;
                    }
                    else if (scaleMode == "custom")
                    {
                        float factor = (options.ScalePercent <= 0 ? 100 : options.ScalePercent) / 100f;
                        pageRect = CalculateCustomRect(page, cellX, cellY, cellW, cellH, factor);
                        overflows = pageRect.Width > cellW + 1e-3f || pageRect.Height > cellH + 1e-3f;
                    }
                    else
                    {
                        pageRect = CalculateFitRect(page, cellX, cellY, cellW, cellH);
                    }
                }

                cells.Add(new PrintCell(page.PageNumber, cellRect, pageRect, overflows));
            }

            bool isBack = (s % 2 == 1);
            sheets.Add(new PrintSheet(isLandscape, isBack, cells));
        }

        return sheets;
    }

    public static int PaperCount(IReadOnlyList<PrintSheet> sheets, PrintDuplexMode duplex)
    {
        if (sheets is null || sheets.Count == 0) return 0;
        return duplex == PrintDuplexMode.Simplex ? sheets.Count : (sheets.Count + 1) / 2;
    }

    public static List<int> ResolvePageNumbers(string? pageRange, int totalPages)
    {
        if (totalPages <= 0) return new List<int>();

        if (string.IsNullOrWhiteSpace(pageRange) || string.Equals(pageRange.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            var all = new List<int>(totalPages);
            for (int i = 1; i <= totalPages; i++) all.Add(i);
            return all;
        }

        var result = new HashSet<int>();
        string[] parts = pageRange.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            ParsePageToken(part, totalPages, result);
        }

        var list = result.ToList();
        list.Sort();
        return list;
    }

    private static void ParsePageToken(string token, int totalPages, HashSet<int> result)
    {
        if (int.TryParse(token, out int singlePage))
        {
            if (singlePage >= 1 && singlePage <= totalPages)
            {
                result.Add(singlePage);
            }
            return;
        }

        int dashIdx = token.IndexOf('-');
        if (dashIdx < 0) return;

        string[] rangeParts = token.Split('-');
        if (rangeParts.Length == 2 &&
            int.TryParse(rangeParts[0].Trim(), out int start) &&
            int.TryParse(rangeParts[1].Trim(), out int end))
        {
            int rStart = Math.Max(1, Math.Min(start, end));
            int rEnd = Math.Min(totalPages, Math.Max(start, end));
            for (int p = rStart; p <= rEnd; p++)
            {
                result.Add(p);
            }
        }
    }

    private static bool DetermineSheetOrientation(
        PrintOrientation orientation,
        PrintPageSize firstPage,
        float portW,
        float portH,
        int pps)
    {
        if (orientation == PrintOrientation.Landscape) return true;
        if (orientation == PrintOrientation.Portrait) return false;

        float scalePort = CalculateScale(firstPage, portW, portH, pps, false);
        float scaleLand = CalculateScale(firstPage, portH, portW, pps, true);

        if (scaleLand > scalePort + Epsilon)
        {
            return true;
        }
        if (scalePort > scaleLand + Epsilon)
        {
            return false;
        }

        return firstPage.WidthPt > firstPage.HeightPt;
    }

    private static float CalculateScale(PrintPageSize page, float sheetW, float sheetH, int pps, bool isLandscape)
    {
        if (page.WidthPt <= Epsilon || page.HeightPt <= Epsilon) return 1.0f;

        float shortSide = Math.Min(sheetW, sheetH);
        float margin = shortSide * MarginRatio;
        float availW = Math.Max(0f, sheetW - 2f * margin);
        float availH = Math.Max(0f, sheetH - 2f * margin);

        (int cols, int rows) = GetGrid(pps, isLandscape);
        float gap = pps >= 2 ? shortSide * GapRatio : 0f;
        float cellW = Math.Max(0f, (availW - (cols - 1) * gap) / cols);
        float cellH = Math.Max(0f, (availH - (rows - 1) * gap) / rows);

        return Math.Min(cellW / page.WidthPt, cellH / page.HeightPt);
    }

    private static (int cols, int rows) GetGrid(int pps, bool isLandscape)
    {
        if (isLandscape)
        {
            return pps switch
            {
                2 => (2, 1),
                4 => (2, 2),
                6 => (3, 2),
                9 => (3, 3),
                16 => (4, 4),
                _ => (1, 1)
            };
        }

        return pps switch
        {
            2 => (1, 2),
            4 => (2, 2),
            6 => (2, 3),
            9 => (3, 3),
            16 => (4, 4),
            _ => (1, 1)
        };
    }

    private static PrintRect CalculateFitRect(PrintPageSize page, float cellX, float cellY, float cellW, float cellH)
    {
        if (page.WidthPt <= Epsilon || page.HeightPt <= Epsilon)
        {
            return new PrintRect(cellX, cellY, cellW, cellH);
        }

        float scale = Math.Min(cellW / page.WidthPt, cellH / page.HeightPt);
        float drawnW = page.WidthPt * scale;
        float drawnH = page.HeightPt * scale;
        float drawnX = cellX + (cellW - drawnW) / 2f;
        float drawnY = cellY + (cellH - drawnH) / 2f;
        return new PrintRect(drawnX, drawnY, drawnW, drawnH);
    }

    private static PrintRect CalculateActualRect(PrintPageSize page, float cellX, float cellY, float cellW, float cellH)
    {
        float drawnW = page.WidthPt;
        float drawnH = page.HeightPt;
        float drawnX = cellX + (cellW - drawnW) / 2f;
        float drawnY = cellY + (cellH - drawnH) / 2f;
        return new PrintRect(drawnX, drawnY, drawnW, drawnH);
    }

    private static PrintRect CalculateCustomRect(PrintPageSize page, float cellX, float cellY, float cellW, float cellH, float scaleFactor)
    {
        float drawnW = page.WidthPt * scaleFactor;
        float drawnH = page.HeightPt * scaleFactor;
        float drawnX = cellX + (cellW - drawnW) / 2f;
        float drawnY = cellY + (cellH - drawnH) / 2f;
        return new PrintRect(drawnX, drawnY, drawnW, drawnH);
    }
}
