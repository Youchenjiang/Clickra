using System;
using System.Collections.Generic;

namespace Clickra.Core.Layout;

/// <summary>History 頁由上而下的三個區塊；順序即畫面順序。</summary>
public enum HistoryBlockKind
{
    ActiveQueue,
    Parked,
    History
}

/// <summary>
/// 堆疊中的一個區塊：起點、高度，以及每一列實際的頂端與高度。
/// 呼叫端只問「第 i 列在哪」或「這個 Y 落在第幾列」，不自己累加，
/// 所以繪製、命中測試與捲動高度不可能各自算出不同的答案。
/// </summary>
public readonly struct HistoryBlock
{
    private readonly int[] _rowTops;
    private readonly int[] _rowHeights;

    internal HistoryBlock(HistoryBlockKind kind, int top, int height, int[] rowTops, int[] rowHeights)
    {
        Kind = kind;
        Top = top;
        Height = height;
        _rowTops = rowTops;
        _rowHeights = rowHeights;
    }

    public HistoryBlockKind Kind { get; }

    /// <summary>區塊起點 Y（含標題行）。</summary>
    public int Top { get; }

    /// <summary>區塊佔用的高度；沒有列時為 0，例如沒有待繼續任務。</summary>
    public int Height { get; }

    public int Bottom => Top + Height;
    public int RowCount => _rowTops.Length;
    public bool IsEmpty => _rowTops.Length == 0;

    /// <summary>第 index 列的頂端 Y。</summary>
    public int RowTop(int index) => Top + _rowTops[index];

    /// <summary>第 index 列的高度。</summary>
    public int RowHeight(int index) => _rowHeights[index];

    /// <summary>第 index 列內的相對 Y，用於明細區這類「列內座標」的判定。</summary>
    public int RowRelativeY(int index, int y) => y - RowTop(index);

    /// <summary>Y 落在第幾列；落在列與列之間的間隙或區塊外時回傳 -1。</summary>
    public int RowAt(int y)
    {
        if (y < Top || y >= Bottom) return -1;
        for (int i = 0; i < _rowTops.Length; i++)
        {
            int rowTop = Top + _rowTops[i];
            if (y >= rowTop && y < rowTop + _rowHeights[i]) return i;
        }
        return -1;
    }
}

/// <summary>
/// History 頁的縱向堆疊：進行中佇列 → 待繼續任務 → 持久化歷史紀錄。
/// 每個區塊的起點都由前一個區塊推導，因此在上方插入一個區塊或改動列高時，
/// 繪製、命中測試與捲動高度會一起移動。
/// </summary>
public static class HistoryLayout
{
    /// <summary>
    /// 建立整個堆疊。activeCount 為進行中任務數、parkedCount 為待繼續任務數、
    /// historyCount 為持久化歷史筆數，expandedHistoryIndex 為展開的歷史列（-1 表示沒有）。
    /// </summary>
    public static IReadOnlyList<HistoryBlock> Build(int activeCount, int parkedCount, int historyCount, int expandedHistoryIndex)
    {
        HistoryBlock activeQueue = BuildUniformBlock(HistoryBlockKind.ActiveQueue, DashboardLayout.ContentTop, activeCount);

        int parkedTop = NextTop(activeQueue);
        HistoryBlock parked = parkedCount == 0
            ? new HistoryBlock(HistoryBlockKind.Parked, parkedTop, 0, Array.Empty<int>(), Array.Empty<int>())
            : BuildUniformBlock(HistoryBlockKind.Parked, parkedTop, parkedCount, DashboardLayout.ParkedHeaderHeight);

        HistoryBlock history = BuildHistoryBlock(NextTop(parked), historyCount, expandedHistoryIndex);

        return new[] { activeQueue, parked, history };
    }

    /// <summary>區塊之後的下一個起點：最後一列的下緣再加上列距；空區塊不前進。</summary>
    public static int NextTop(HistoryBlock block) =>
        block.IsEmpty ? block.Top : block.Bottom + DashboardLayout.RowGap;

    /// <summary>取出指定種類的區塊。</summary>
    public static HistoryBlock Find(IReadOnlyList<HistoryBlock> stack, HistoryBlockKind kind)
    {
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Kind == kind) return stack[i];
        }
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "The stack does not contain that block.");
    }

    /// <summary>
    /// 捲動高度：最後一列之後留 SectionPadding（有列時再加上列距），
    /// 且不小於 MinContentHeight。
    /// </summary>
    public static int ContentHeight(IReadOnlyList<HistoryBlock> stack)
    {
        HistoryBlock history = Find(stack, HistoryBlockKind.History);
        int trailing = history.IsEmpty ? 0 : DashboardLayout.RowGap;
        return Math.Max(DashboardLayout.MinContentHeight, history.Bottom + trailing + DashboardLayout.SectionPadding);
    }

    /// <summary>等距的區塊；headerHeight 為列之前的標題行高度。</summary>
    private static HistoryBlock BuildUniformBlock(HistoryBlockKind kind, int top, int rowCount, int headerHeight = 0)
    {
        var tops = new int[rowCount];
        var heights = new int[rowCount];
        for (int i = 0; i < rowCount; i++)
        {
            tops[i] = headerHeight + i * DashboardLayout.RowStride;
            heights[i] = DashboardLayout.RowHeight;
        }

        int height = rowCount == 0
            ? 0
            : headerHeight + (rowCount - 1) * DashboardLayout.RowStride + DashboardLayout.RowHeight;

        return new HistoryBlock(kind, top, height, tops, heights);
    }

    /// <summary>持久化歷史：展開的那一列較高，列距因此不固定，逐列累加。</summary>
    private static HistoryBlock BuildHistoryBlock(int top, int rowCount, int expandedIndex)
    {
        var tops = new int[rowCount];
        var heights = new int[rowCount];
        int cursor = 0;
        for (int i = 0; i < rowCount; i++)
        {
            int height = i == expandedIndex ? DashboardLayout.ExpandedRowHeight : DashboardLayout.RowHeight;
            tops[i] = cursor;
            heights[i] = height;
            cursor += height + DashboardLayout.RowGap;
        }

        int total = rowCount == 0 ? 0 : cursor - DashboardLayout.RowGap;
        return new HistoryBlock(HistoryBlockKind.History, top, total, tops, heights);
    }
}
