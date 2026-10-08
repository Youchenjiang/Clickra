namespace Clickra.Core.Layout;

/// <summary>
/// Clickra dashboard 的版面表：列高、行距與區塊高度只在此宣告。
/// Win32 dashboard 的繪製、命中測試與捲動高度全部由這張表推導，所以改一個列高
/// 或新增一個區塊時，三者會一起改變，而不是各自在繪製檔與命中檔裡留一份算式。
/// </summary>
public static class DashboardLayout
{
    // ── 內容區原點 ──────────────────────────────────────────────
    /// <summary>標題（30）與分隔線（75）之下，內容第一列的 Y。</summary>
    public const int ContentTop = 90;

    // ── 列高與行距 ──────────────────────────────────────────────
    /// <summary>一般列高：進行中佇列、待繼續任務與收合的歷史列。</summary>
    public const int RowHeight = 44;

    /// <summary>展開後含明細區的歷史列高。</summary>
    public const int ExpandedRowHeight = 160;

    /// <summary>列與列之間的間隙。</summary>
    public const int RowGap = 8;

    /// <summary>等距列的列距。</summary>
    public const int RowStride = RowHeight + RowGap;

    // ── 區塊高度 ────────────────────────────────────────────────
    /// <summary>待繼續任務區塊的標題行高度（標題與聚合警示都在這一行）。</summary>
    public const int ParkedHeaderHeight = 30;

    /// <summary>捲動高度在最後一列之後留下的空白。</summary>
    public const int SectionPadding = 20;

    /// <summary>內容區的最小高度。</summary>
    public const int MinContentHeight = 460;

    /// <summary>Dashboard 可縮放的最小 client 寬度；保留側邊欄與可操作的主要內容區。</summary>
    public const int MinClientWidth = 420;

    // ── 展開列的明細欄位 ────────────────────────────────────────
    /// <summary>明細區的分隔線，畫在收合列高的位置。</summary>
    public const int DetailDividerY = RowHeight;

    /// <summary>第一個明細欄位標籤的 Y（相對展開列的頂端）。</summary>
    public const int DetailFirstFieldY = 54;

    /// <summary>明細欄位的行距：輸入、輸出、時間、耗時（或錯誤）依序排列。</summary>
    public const int DetailFieldStride = 26;

    /// <summary>標籤上方仍屬於該欄位的距離。</summary>
    public const int DetailFieldTapTop = 4;

    /// <summary>可捲動欄位的命中帶高度。</summary>
    public const int DetailFieldTapHeight = 22;

    /// <summary>欄位捲軸相對於標籤 Y 的位移。</summary>
    public const int DetailScrollbarOffset = 17;

    /// <summary>可捲動的欄位各自落在明細的第幾列：輸入、輸出與耗時／錯誤（時間不可捲動）。</summary>
    private static readonly int[] DetailScrollableFieldRows = { 0, 1, 3 };

    /// <summary>第 index 個明細欄位標籤的 Y（相對展開列的頂端）。</summary>
    public static int DetailFieldY(int index) => DetailFirstFieldY + index * DetailFieldStride;

    /// <summary>可捲動欄位捲軸的 Y（相對展開列的頂端）。</summary>
    public static int DetailScrollbarY(int field) =>
        DetailFieldY(DetailScrollableFieldRows[field]) + DetailScrollbarOffset;

    /// <summary>可捲動欄位的數量。</summary>
    public static int DetailScrollableFieldCount => DetailScrollableFieldRows.Length;

    /// <summary>
    /// 把展開列內的相對 Y 對應到可捲動欄位編號（0 輸入、1 輸出、2 耗時／錯誤），
    /// 不在任何欄位上時回傳 -1。點擊與滾輪都走這一個函式：兩邊原本各有一份命中帶，
    /// 高度還不一樣，所以同一個欄位在點擊與滾輪下是兩個不同的範圍。
    /// </summary>
    public static int DetailScrollFieldAt(int relativeY)
    {
        for (int field = 0; field < DetailScrollableFieldRows.Length; field++)
        {
            int top = DetailFieldY(DetailScrollableFieldRows[field]) - DetailFieldTapTop;
            if (relativeY >= top && relativeY < top + DetailFieldTapHeight) return field;
        }
        return -1;
    }

    // ── 側邊欄籤 ────────────────────────────────────────────────
    /// <summary>籤的數量；繪製與命中都以此為準。</summary>
    public const int SidebarTabCount = 5;

    public const int SidebarTabTop = 120;
    public const int SidebarTabHeight = 40;
    public const int SidebarTabStride = 48;

    /// <summary>選取指示條在籤內的內縮，以及它的高度。</summary>
    public const int SidebarTabAccentInset = 4;
    public const int SidebarTabAccentHeight = SidebarTabHeight - 2 * SidebarTabAccentInset;

    /// <summary>第 index 個籤的 Y。</summary>
    public static int SidebarTabY(int index) => SidebarTabTop + index * SidebarTabStride;

    /// <summary>Y 落在第幾個籤；落在行距上或籤列之外時回傳 -1（該處沒有籤可點）。</summary>
    public static int SidebarTabAt(int y)
    {
        if (y < SidebarTabTop) return -1;
        int offset = y - SidebarTabTop;
        int index = offset / SidebarTabStride;
        if (index >= SidebarTabCount) return -1;
        return offset % SidebarTabStride < SidebarTabHeight ? index : -1;
    }

    // ── 轉換頁：拖放區、指令格線、開始鈕、清除鈕 ──────────────────
    /// <summary>Top edge of the conversion drop zone in logical pixels.</summary>
    public const int ConvertZoneTop = 95;
    /// <summary>Height of the conversion drop zone in logical pixels.</summary>
    public const int ConvertZoneHeight = 72;

    /// <summary>拖放區右側留白。</summary>
    public const int ConvertZoneRightMargin = 50;

    /// <summary>Width of the drop-zone clear button.</summary>
    public const int ConvertClearButtonWidth = 48;
    /// <summary>Height of the drop-zone clear button.</summary>
    public const int ConvertClearButtonHeight = 22;

    /// <summary>清除鈕相對拖放區上緣的位移。</summary>
    public const int ConvertClearButtonInset = 7;
    /// <summary>Right margin used to position the drop-zone clear button.</summary>
    public const int ConvertClearButtonRightMargin = 110;

    /// <summary>拖放區空白狀態內各列相對上緣的位置。</summary>
    public const int ConvertZoneIconOffset = 6;
    /// <summary>Vertical offset of the primary drop-zone hint.</summary>
    public const int ConvertZoneHintOffset = 26;
    /// <summary>Vertical offset of the secondary drop-zone hint.</summary>
    public const int ConvertZoneSubHintOffset = 48;

    /// <summary>已有檔案時，摘要、檔名與輸出位置相對拖放區上緣的位置。</summary>
    public const int ConvertZoneSummaryOffset = 8;
    /// <summary>Vertical offset of the selected-file list in the drop zone.</summary>
    public const int ConvertZoneFilesOffset = 29;
    /// <summary>Vertical offset of the output-location line in the drop zone.</summary>
    public const int ConvertZoneOutputOffset = 51;

    /// <summary>Number of conversion command groups rendered by the Dashboard.</summary>
    public const int ConvertGroupCount = 3;
    /// <summary>Horizontal gap between conversion cards in a group.</summary>
    public const int ConvertGroupGap = 14;
    /// <summary>Vertical gap between successive conversion command groups.</summary>
    public const int ConvertGroupSectionGap = 8;

    /// <summary>指令欄標題的 Y（格線頂端）。</summary>
    public const int ConvertGridTop = 176;

    /// <summary>Height reserved for each conversion group heading.</summary>
    public const int ConvertGroupHeaderHeight = 20;
    /// <summary>Height of a conversion command card.</summary>
    public const int ConvertCardHeight = 34;
    /// <summary>Vertical gap between conversion command cards.</summary>
    public const int ConvertCardGap = 6;
    /// <summary>Vertical stride from one conversion card row to the next.</summary>
    public const int ConvertCardStride = ConvertCardHeight + ConvertCardGap;

    /// <summary>最後一排卡片與開始鈕之間的距離。</summary>
    public const int ConvertStartButtonGap = 8;
    /// <summary>Height of the primary conversion action button.</summary>
    public const int ConvertStartButtonHeight = 32;

    /// <summary>拖放區的矩形。</summary>
    public static LayoutRect ConvertZoneRect(int contentX, int logW) =>
        new LayoutRect(contentX, ConvertZoneTop, logW - contentX - ConvertZoneRightMargin, ConvertZoneHeight);

    /// <summary>拖放區右上角的「清除」鈕。</summary>
    public static LayoutRect ConvertClearButtonRect(int logW) =>
        new LayoutRect(
            logW - ConvertClearButtonRightMargin,
            ConvertZoneTop + ConvertClearButtonInset,
            ConvertClearButtonWidth,
            ConvertClearButtonHeight);

    /// <summary>各分類在寬版工作區使用的欄數：Office 3、PDF 5、圖片 4。</summary>
    public static int ConvertGroupColumns(int groupIndex) => groupIndex switch
    {
        0 => 3,
        1 => 5,
        2 => 4,
        _ => 1
    };

    /// <summary>指定分類在目前項目數下需要幾列。</summary>
    public static int ConvertGroupRows(int groupIndex, int itemCount)
    {
        int columns = ConvertGroupColumns(groupIndex);
        return Math.Max(1, (itemCount + columns - 1) / columns);
    }

    /// <summary>指定分類標題的 Y；前面的分類依實際列數往下堆疊。</summary>
    public static int ConvertGroupTop(int groupIndex, IReadOnlyList<int> groupSizes)
    {
        int y = ConvertGridTop;
        for (int group = 0; group < groupIndex; group++)
        {
            y += ConvertGroupHeaderHeight
                + ConvertGroupRows(group, groupSizes[group]) * ConvertCardStride
                + ConvertGroupSectionGap;
        }
        return y;
    }

    /// <summary>第 groupIndex 分類第 itemIndex 張指令卡的位置。</summary>
    public static LayoutRect ConvertCardRect(int groupIndex, int itemIndex, int contentX, int zoneWidth, IReadOnlyList<int> groupSizes)
    {
        int columns = ConvertGroupColumns(groupIndex);
        int cardWidth = (zoneWidth - (columns - 1) * ConvertGroupGap) / columns;
        int row = itemIndex / columns;
        int column = itemIndex % columns;
        return new LayoutRect(
            contentX + column * (cardWidth + ConvertGroupGap),
            ConvertGroupTop(groupIndex, groupSizes) + ConvertGroupHeaderHeight + row * ConvertCardStride,
            cardWidth,
            ConvertCardHeight);
    }

    /// <summary>分類標題與該分類第一張卡片左緣對齊。</summary>
    public static int ConvertGroupX(int groupIndex, int contentX, int zoneWidth, IReadOnlyList<int> groupSizes) =>
        ConvertCardRect(groupIndex, 0, contentX, zoneWidth, groupSizes).X;

    /// <summary>內容流中最後一列之後原本會放開始鈕的位置；用來推導可捲動內容高度。</summary>
    public static int ConvertStartButtonY(IReadOnlyList<int> groupSizes)
    {
        int lastGroup = Math.Min(ConvertGroupCount, groupSizes.Count) - 1;
        return ConvertGroupTop(lastGroup, groupSizes)
            + ConvertGroupHeaderHeight
            + ConvertGroupRows(lastGroup, groupSizes[lastGroup]) * ConvertCardStride
            + ConvertStartButtonGap;
    }

    /// <summary>Returns the original scrollable start-button position for conversion content sizing.</summary>
    public static LayoutRect ConvertStartButtonRect(int contentX, int zoneWidth, IReadOnlyList<int> groupSizes) =>
        new LayoutRect(contentX, ConvertStartButtonY(groupSizes), zoneWidth, ConvertStartButtonHeight);

    /// <summary>固定在轉檔 viewport 底部的開始鈕，不跟內容捲動。</summary>
    public static LayoutRect ConvertStickyStartButtonRect(int contentX, int zoneWidth, int viewportHeight) =>
        new LayoutRect(
            contentX,
            viewportHeight - ConvertStartButtonHeight - ConvertStartButtonGap,
            zoneWidth,
            ConvertStartButtonHeight);

    /// <summary>固定 action footer 的完整覆蓋區；背景也必須攔截命中，避免點到被遮住的卡片。</summary>
    public static LayoutRect ConvertStickyFooterRect(int contentX, int zoneWidth, int viewportHeight)
    {
        LayoutRect button = ConvertStickyStartButtonRect(contentX, zoneWidth, viewportHeight);
        return new LayoutRect(
            contentX,
            button.Y - ConvertStartButtonGap,
            zoneWidth,
            ConvertStartButtonHeight + ConvertStartButtonGap * 2);
    }

    /// <summary>
    /// 轉檔頁可捲動內容高度。底部保留固定 action footer 的空間；當指令增加導致卡片
    /// 多出一列時，只增加內容捲動範圍，不增加 Dashboard 視窗高度。
    /// </summary>
    public static int ConvertContentHeight(IReadOnlyList<int> groupSizes)
    {
        LayoutRect start = ConvertStartButtonRect(0, 1, groupSizes);
        return Math.Max(MinContentHeight, start.Bottom + ConvertStartButtonGap);
    }

    // ── 下拉選單與它彈出的清單 ──────────────────────────────────
    /// <summary>下拉控制項的寬度與高度；彈出框與它同寬。</summary>
    public const int DropdownWidth = 240;
    public const int DropdownHeight = 30;

    /// <summary>清單中每一列的列距。</summary>
    public const int DropdownItemStride = 26;

    /// <summary>清單項目本身的高度（比列距小，列與列之間留白）。</summary>
    public const int DropdownItemHeight = 24;

    /// <summary>搜尋框與彈出框邊緣的距離；清單第一列就接在搜尋框之下。參照同樣的邊距。</summary>
    public const int DropdownSearchInset = 6;
    public const int DropdownSearchHeight = 26;

    /// <summary>語言清單的固定高度，包含上方的搜尋框。</summary>
    public const int LanguagePopupHeight = 180;

    /// <summary>語言清單最多同時顯示的列數。</summary>
    public const int LanguagePopupVisibleRows = 5;

    /// <summary>清單第一列距離彈出框頂端的距離：語言清單上方是搜尋框。</summary>
    public const int LanguagePopupListTop = DropdownSearchInset + DropdownSearchHeight + DropdownSearchInset;

    /// <summary>PDF 語言清單第一列距離彈出框頂端的距離（它沒有搜尋框）。</summary>
    public const int PdfPopupListTop = 4;

    /// <summary>彈出框上下各留的邊距；PDF 清單的高度是每列一個列距再加上它。</summary>
    public const int DropdownPopupPadding = 4;

    /// <summary>PDF 語言清單的高度。</summary>
    public static int PdfPopupHeight(int itemCount) =>
        itemCount * DropdownItemStride + 2 * DropdownPopupPadding;

    /// <summary>下拉控制項的矩形。</summary>
    public static LayoutRect DropdownButtonRect(int contentX, int y) =>
        new LayoutRect(contentX, y, DropdownWidth, DropdownHeight);

    public static LayoutRect DropdownButtonRect(int contentX, int y, int width) =>
        new LayoutRect(contentX, y, width, DropdownHeight);

    /// <summary>彈出框的矩形：畫在控制項正上方，寬度與控制項相同。</summary>
    public static LayoutRect DropdownPopupRect(int contentX, int controlY, int popupHeight) =>
        new LayoutRect(contentX, controlY - popupHeight, DropdownWidth, popupHeight);

    public static LayoutRect DropdownPopupRect(int contentX, int controlY, int popupHeight, int width) =>
        new LayoutRect(contentX, controlY - popupHeight, width, popupHeight);

    /// <summary>語言清單的搜尋框。</summary>
    public static LayoutRect DropdownSearchRect(int popupLeft, int popupTop) =>
        new LayoutRect(
            popupLeft + DropdownSearchInset,
            popupTop + DropdownSearchInset,
            DropdownWidth - 2 * DropdownSearchInset,
            DropdownSearchHeight);

    public static LayoutRect DropdownSearchRect(int popupLeft, int popupTop, int popupWidth) =>
        new LayoutRect(
            popupLeft + DropdownSearchInset,
            popupTop + DropdownSearchInset,
            popupWidth - 2 * DropdownSearchInset,
            DropdownSearchHeight);

    /// <summary>清單第 index 列的 Y（彈出框頂端 + 清單起點 + 列距）。</summary>
    public static int DropdownItemY(int popupTop, int listTop, int index) =>
        popupTop + listTop + index * DropdownItemStride;

    /// <summary>
    /// 把彈出框內的 Y 對應到清單列號；落在清單上方的搜尋區、彈出框外，或超出最後一列時回傳 -1。
    /// 點擊、hover 與滾輪原本各自再算一次列號，所以三者可以對同一條清單給出不同答案。
    /// </summary>
    public static int DropdownItemAt(int popupTop, int listTop, int popupHeight, int y)
    {
        int relative = y - popupTop - listTop;
        if (relative < 0) return -1;
        int index = relative / DropdownItemStride;
        if (listTop + index * DropdownItemStride + DropdownItemHeight > popupHeight) return -1;
        return index;
    }

    // ── 歷史頁的「清除紀錄」鈕 ──────────────────────────────────
    public const int HistoryClearButtonWidth = 90;
    public const int HistoryClearButtonHeight = 28;
    public const int HistoryClearButtonTop = 38;
    public const int HistoryClearButtonRightMargin = 130;

    public static LayoutRect HistoryClearButtonRect(int logW) =>
        new LayoutRect(
            logW - HistoryClearButtonRightMargin,
            HistoryClearButtonTop,
            HistoryClearButtonWidth,
            HistoryClearButtonHeight);

    // ── 待繼續任務列的動作按鈕 ────────────────────────────────
    // 每一列右端包含四個動作按鈕（縮短、延長、繼續、取消）。
    // 靠右的期限文字與命中判定共用相同的寬度與座標推導，量測與命中永遠對齊。
    public const int ParkedActionCount = 4;
    public const int ParkedActionShorten = 0;
    public const int ParkedActionExtend = 1;
    public const int ParkedActionResume = 2;
    public const int ParkedActionCancel = 3;

    public const int ParkedActionButtonWidth = 36;
    public const int ParkedCommandButtonWidth = 52;
    public const int ParkedActionButtonHeight = 22;
    public const int ParkedActionButtonGap = 6;
    public const int ParkedActionRightMargin = 16;

    /// <summary>期限文字與動作按鈕組之間的間隔。</summary>
    public const int ParkedActionTextGap = 10;

    /// <summary>取得指定動作按鈕之寬度。</summary>
    public static int ParkedActionWidth(int actionIndex) =>
        actionIndex is ParkedActionResume or ParkedActionCancel
            ? ParkedCommandButtonWidth
            : ParkedActionButtonWidth;

    /// <summary>所有動作按鈕與間距加總後的總寬度（畫期限文字時要讓開這一段）。</summary>
    public static int ParkedActionStripWidth =>
        ParkedActionButtonWidth * 2 + ParkedCommandButtonWidth * 2 + ParkedActionButtonGap * (ParkedActionCount - 1);

    /// <summary>待繼續任務列右端的動作按鈕矩形；actionIndex 0 = 縮短、1 = 延長、2 = 繼續、3 = 取消，由左而右。</summary>
    public static LayoutRect ParkedRowActionRect(int contentX, int rowW, int rowTop, int rowHeight, int actionIndex)
    {
        int startX = contentX + rowW - ParkedActionRightMargin - ParkedActionStripWidth;
        int x = startX;
        for (int i = 0; i < actionIndex; i++)
        {
            x += ParkedActionWidth(i) + ParkedActionButtonGap;
        }
        int top = rowTop + (rowHeight - ParkedActionButtonHeight) / 2;
        return new LayoutRect(x, top, ParkedActionWidth(actionIndex), ParkedActionButtonHeight);
    }

    /// <summary>這一列靠右的期限文字可以畫到哪裡（動作按鈕組的左緣再讓開一個間隔）。</summary>
    public static int ParkedRowTtlRight(int contentX, int rowW) =>
        contentX + rowW - ParkedActionRightMargin - ParkedActionStripWidth - ParkedActionTextGap;
}
