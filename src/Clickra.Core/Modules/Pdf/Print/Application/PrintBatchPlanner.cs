namespace Clickra.Core.Modules.Pdf.Print.Application;

/// <summary>One file of a print batch: which pages it prints, its sheet sides and its paper.</summary>
public sealed record PrintBatchFile(
    string Name,
    int TotalPages,
    IReadOnlyList<PrintPageSize> SelectedPages,
    IReadOnlyList<PrintSheet> Sheets,
    int PaperCount)
{
    /// <summary>None of the requested pages exist in this file, so it is not printed.</summary>
    public bool Skipped => SelectedPages.Count == 0;
}

/// <summary>A whole print batch: every file laid out on its own, plus the totals shown under the preview.</summary>
public sealed record PrintBatchPlan(
    IReadOnlyList<PrintBatchFile> Files,
    int TotalSelectedPages,
    int TotalPaper,
    IReadOnlyList<string> SkippedNames);

/// <summary>Where one sheet side sits in a file: its paper number, the file's paper count and whether it is a back side.</summary>
public readonly record struct PrintSidePosition(int PaperNumber, int PaperCount, bool IsBack);

/// <summary>
/// Lays out a batch of files for printing (docs/specs/print_preview_plan.md, sections 1 and 5).
/// Each file is planned on its own and starts on a fresh sheet of paper, so two-sided paper
/// is counted per file: three one-side files printed two-sided use three sheets, not two.
/// Pure calculation, shared by the desktop preview and its tests.
/// </summary>
public static class PrintBatchPlanner
{
    /// <summary>Applies a page range to a file's page sizes (in page order, 1-based numbers).</summary>
    public static IReadOnlyList<PrintPageSize> SelectPages(IReadOnlyList<PrintPageSize> allPages, string? pageRange)
    {
        if (allPages is null || allPages.Count == 0) return Array.Empty<PrintPageSize>();
        var selected = new List<PrintPageSize>();
        foreach (int number in PrintLayoutPlanner.ResolvePageNumbers(pageRange, allPages.Count))
        {
            selected.Add(allPages[number - 1]);
        }
        return selected;
    }

    public static PrintBatchPlan Plan(
        IReadOnlyList<(string Name, IReadOnlyList<PrintPageSize> AllPages)> files,
        float paperWidthPt,
        float paperHeightPt,
        PrintPdfOptions options)
    {
        var planned = new List<PrintBatchFile>(files.Count);
        var skipped = new List<string>();
        int totalPages = 0, totalPaper = 0;
        int copies = Math.Max(1, options.Copies);

        foreach (var (name, allPages) in files)
        {
            IReadOnlyList<PrintPageSize> selected = SelectPages(allPages, options.PageRange);
            IReadOnlyList<PrintSheet> sheets = PrintLayoutPlanner.Plan(selected, paperWidthPt, paperHeightPt, options);
            int paper = PrintLayoutPlanner.PaperCount(sheets, options.Duplex);

            var file = new PrintBatchFile(name, allPages?.Count ?? 0, selected, sheets, paper);
            planned.Add(file);
            if (file.Skipped) skipped.Add(name);
            totalPages += selected.Count;
            totalPaper += paper * copies;
        }

        return new PrintBatchPlan(planned, totalPages, totalPaper, skipped);
    }

    /// <summary>The paper number of sheet side <paramref name="sideIndex"/> (0-based) in a file.</summary>
    public static PrintSidePosition DescribeSide(IReadOnlyList<PrintSheet> sheets, int sideIndex, PrintDuplexMode duplex)
    {
        int count = PrintLayoutPlanner.PaperCount(sheets, duplex);
        if (duplex == PrintDuplexMode.Simplex) return new PrintSidePosition(sideIndex + 1, count, false);
        return new PrintSidePosition(sideIndex / 2 + 1, count, sideIndex % 2 == 1);
    }
}
