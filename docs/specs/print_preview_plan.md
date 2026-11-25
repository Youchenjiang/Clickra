# 列印預覽與多份檔案列印實作計畫

> 狀態：實作中（2026-11-23 起）
> 對應：[v4_desktop_ui_layout.md](v4_desktop_ui_layout.md) 4.9「列印預覽」、〈4.0 上線前清單〉13；畫布「Clickra 4.0 版面方案」討論中頁的「列印 A3」與「列印 A4：一次列印多個檔案」。
> 範圍：一次做完 A3 的全部選項（含要補 Core 的每張多頁、排列、頁框、實際大小／自訂 %、黑白、分份）與 A4 的多檔案預覽。經典列印視窗與 Fluent 不改畫面，只跟著 Core 的新選項與預設值走。

## 1. 原則

1. **預覽和列印用同一套排版。** Core 新增一個純計算的排版器，輸入頁面尺寸與選項，輸出每一張紙的方向與每一格的位置。預覽照它畫、列印照它印，兩邊不再各寫一份判斷（這次「自動」方向出錯，就是兩邊判斷不同造成的）。
2. **每個檔案各自排版。** 方向、張數、雙面都按檔案算；每個檔案從新的一張紙開始，雙面時不會接在上一份的背面。
3. **頁面尺寸一律用 PdfPig 的 `Page.Width`／`Page.Height`。** 它們已經套用 `/Rotate`，不要再對調（v3.22.2.0 的修正）。

## 2. Core：選項與預設值

### 2.1 `PrintPdfOptions` 新增欄位

| 欄位 | 鍵 | 值 | 預設 |
| :--- | :--- | :--- | :--- |
| `PagesPerSheet` | `pages_per_sheet` | 1、2、4、6、9、16（其他值退回 1） | 1 |
| `PageOrder` | `page_order` | `Z`（先橫排）、`N`（先直排） | `Z` |
| `PageBorder` | `page_border` | bool | false |
| `Scale` | `scale` | `fit`、`actual`、`custom` | `fit` |
| `ScalePercent` | `scale_percent` | 10–400 的整數 | 100 |
| `Grayscale` | `grayscale` | bool | false |
| `Collate` | `collate` | bool | true |

- 相容：呼叫端沒給 `scale` 但有 `fit_to_page` 時，`true` → `fit`、`false` → `actual`。`FitToPage` 屬性保留為 `Scale == fit` 的唯讀別名，舊程式不用改。
- `PagesPerSheet` 大於 1 時，`Scale` 一律當成 `fit`（每頁縮放到自己的格子），`ScalePercent` 不使用。
- `ToDictionary` 照舊省略等於預設的值；`ToExplicitDictionary` 一律寫出全部新欄位。

### 2.2 設定（功能預設值）

`ClickraSettings` 新增：`PrintPagesPerSheet`（1）、`PrintPageOrder`（Z）、`PrintPageBorder`（false）、`PrintScale`（空字串）、`PrintScalePercent`（100）、`PrintGrayscale`（false）、`PrintCollate`（true）。

- `SettingsController.PrintScale` 讀取時：有存值就用；空字串時依舊的 `PrintFitToPage` 換算（true → `fit`，false → `actual`）。寫入時同步更新 `PrintFitToPage`，讓舊介面看到一致的值。
- `PrintPdfUseCase.NormalizeOptions` 照現有寫法用 `TryAdd` 補上這些預設值。
- 新介面設定頁「功能預設值」的列印區塊補上這些項目（與列印卡片同樣的控制項）；「設為預設值」也一併存。

## 3. Core：排版器

新增 `Modules/Pdf/Print/Application/PrintLayoutPlanner.cs`（純計算，不碰 System.Drawing，可在任何平台跑）。

```csharp
public sealed record PrintPageSize(int PageNumber, float WidthPt, float HeightPt);
public sealed record PrintCell(int PageNumber, PrintRect Cell, PrintRect Page, bool Overflows);
public sealed record PrintSheet(bool Landscape, bool IsBack, IReadOnlyList<PrintCell> Cells);
public readonly record struct PrintRect(float X, float Y, float Width, float Height); // 單位 pt，原點在紙的左上角

public static class PrintLayoutPlanner
{
    public static IReadOnlyList<PrintSheet> Plan(
        IReadOnlyList<PrintPageSize> pages,       // 已套用頁面範圍、依頁碼排序
        float paperWidthPt, float paperHeightPt,  // 直向時的紙張尺寸
        PrintPdfOptions options);
    public static int PaperCount(IReadOnlyList<PrintSheet> sheets, PrintDuplexMode duplex); // 單面 = 張數；雙面 = 正反面湊成一張，奇數補一面
}
```

規則：

1. **分組**：頁面依序每 `PagesPerSheet` 頁一組，一組是一面。
2. **方向**：`Landscape`／`Portrait` 依選項；`Auto` 時對這一面的第一頁，各試一次直向與橫向紙，**選頁面能印得比較大的那個**，一樣大時跟第一頁同方向。（每張 1 頁時就是跟頁面同方向；每張 2 頁、直的頁面時紙轉成橫的；與畫布 A3 一致。）
3. **格子**：直向紙的欄×列為 1:1×1、2:1×2、4:2×2、6:2×3、9:3×3、16:4×4；橫向紙把欄列對調（2:2×1、6:3×2）。
4. **留白**：紙的四邊留短邊 3%；每張 2 頁以上時格子之間再留短邊 2%。
5. **順序**：`Z` 先排滿一列再換列；`N` 先排滿一欄再換欄。
6. **大小**：
   - `fit`：頁面等比縮放到格子內、置中。
   - `actual`：頁面原尺寸置中；超出可印範圍（紙減去四邊留白）時 `Overflows = true`。
   - `custom`：原尺寸乘以 `ScalePercent`，其餘同 `actual`。
7. **雙面**：面依序交替正面、背面（`IsBack`）。每個檔案各自從正面開始。

## 4. Core：列印處理器

`PdfPrintProcessor.Print` 改為：

1. 讀頁面尺寸（`ReadPageDimensions`，見原則 3），套用頁面範圍，交給 `PrintLayoutPlanner.Plan`。紙張尺寸取 `DefaultPageSettings.PaperSize`（1/100 吋換成 pt）。
2. `QueryPageSettings`：依目前這一面的 `Landscape` 設定；`Grayscale` 時 `PageSettings.Color = false`。
3. `PrintPage`：依 `e.PageBounds` 與這一面的紙張尺寸（pt）算出換算比例，對每一格：
   - 依 `Page` 矩形的大小與 `Dpi` 算像素寬，用 `PdfPageThumbnailRenderer.RenderPageFromFile` 畫出頁面。
   - `DarkenText` 時套用 `PdfContrastEnhancer`；`Grayscale` 時把點陣圖轉灰階（驅動程式不一定理會 `Color = false`）。
   - 把點陣圖畫到 `Page` 矩形，裁切在 `Cell` 矩形內。
   - `PageBorder` 且每張 2 頁以上時，在 `Page` 矩形外框畫一條細線（0.5 pt，灰色）。
4. `PrinterSettings.Copies` 與 `PrinterSettings.Collate` 交給驅動程式處理份數與分份。
5. 拿掉舊的逐頁方向判斷與 `CalculateDestinationBounds`（改由排版器負責）；`ReadPageOrientations` 若只剩測試在用，改成呼叫 `ReadPageDimensions`。

另外新增 `Modules/Pdf/Print/Application/PrintPreviewQuery.cs`（應用層，給介面用，不讓介面直接參考 Processing）：

- `ReadPageSizes(string file, string? pageRange)`：讀頁面尺寸並套用頁面範圍，回傳 `PrintPageSize` 清單與這份的總頁數。
- `RenderPage(string file, int pageNumber, int widthPx, bool darken, bool grayscale)`：畫出單頁點陣圖，套用加深與灰階，讓預覽看到的就是印出來的顏色。

## 5. 新介面：列印卡片

照畫布 A3＋A4 改寫目前的列印卡片（`ConvertPage.axaml` 的 `IsPrint` 區塊與 `ActionOptionsViewModel` 的列印部分；預覽邏輯放進新的 `PrintPreviewViewModel`，不要再擠進 `ActionOptionsViewModel`）。

### 5.1 版面

- 動作頁的內容區分成左右兩欄：左欄選項（寬約 420，選項多時只有左欄捲動），右欄預覽。這是唯一標籤放左邊（寬 76）的設定卡。
- 視窗太窄（右欄小於 260）時，預覽移到選項下方。
- **瞬時導覽與背景預熱**：點擊首頁「列印」卡片瞬間（t=0）立即切換至列印頁面，不在目錄同步等待印表機探索。App 啟動與拖入檔案時主動於背景預熱印表機快取（`SystemPrinterDiscoveryService.WarmUp`）。

### 5.2 左欄選項（由上到下）

| 列 | 內容 | 出現條件 |
| :--- | :--- | :--- |
| 印表機 | 下拉選單 | 一律 |
| 頁面 | 範圍輸入（提示「全部頁面」）＋份數 − ＋ | 一律 |
| 紙張 | 紙張下拉＋方向（自動／直向／橫向） | 一律 |
| 雙面 | 單面／長邊翻／短邊翻 | 印表機支援雙面時可選，否則停用並固定單面 |
| 每張紙 | 1 頁／2／4／6／9／16 | 一律 |
| 排列 | 先橫排 Z／先直排 N＋頁框勾選 | 每張 2 頁以上 |
| 大小 | 符合紙張／實際大小／自訂＋百分比輸入（自訂時出現） | 每張 1 頁；2 頁以上時停用並顯示「每張 2 頁以上時，頁面會縮放到格子大小」 |
| 色彩 | 彩色／黑白＋加深淺色文字勾選 | 一律 |
| 多份時 | 一份印完再印下一份（勾選） | 份數 2 以上 |

最下方照舊放「設為預設值」。原本的「縮放至紙張大小」勾選框由「大小」取代。
- **印表機搜尋中狀態**：若後台探索尚未完成，印表機下拉選單呈停用（`IsEnabled=False`）並顯示「正在搜尋印表機…」，紙張下拉選單同步停用並維持「依印表機設定」，嚴禁在探索中途閃爍「沒有可用的印表機」。探索完成後自動選取預設／已儲存印表機並解鎖。

### 5.3 右欄預覽

由上到下：

1. 標題列：「印出來的樣子」，右邊小字「點兩下放大」。
2. **檔案切換列**（選了 2 個以上檔案才出現）：檔案圖示、檔名、第二行小字（「共 12 頁」／「共 12 頁，印其中 3 頁」／橘色「只有 1 頁，選的頁碼都不在這份裡，不會印」）、「第 1 個，共 3 個」、上一個／下一個檔案按鈕。
3. **紙**：依排版器結果畫出目前這一面：白色紙張、每一格的頁面縮圖、每張 2 頁以上時每格右下角標頁碼、頁框、實際大小／自訂超出時畫橘色虛線表示可印範圍。
4. 翻頁列：上一張／「第 1 張，共 3 張」／下一張；右邊小字「A4 直向」「A4 橫向，每張 4 頁」，實際大小或自訂且有超出時改成橘色「橘色虛線外的部分不會印到」。份數大於 1 時張數乘以份數（與畫布一致）。
5. 點兩下紙開啟放大預覽：把分割工作區的放大預覽（`ConvertPage.axaml` 的 ZoomFull 區塊與 `SplitWorkspaceViewModel` 的縮放部分）抽成共用元件，分割與列印都用它；列印時左右鍵翻的是「張」。
   - **放大換頁取消與載入過渡（防止殘影閃爍）**：放大預覽中換頁時，立即遞增渲染版本號並取消（`CancellationTokenSource.Cancel()`）前一頁未完成的背景高解析度運算；同時立刻將 `Zoom.Image` 重設為 `null`，依新紙張比例描繪乾淨骨架，絕不保留前一頁的點陣圖，杜絕「標題與比例已切換、畫面卻殘留舊頁面縮放圖並突兀跳變」的視覺瑕疵；背景高解析度渲染完成後，嚴格比對版本號與目前紙張執行個體無誤才貼上畫面；關閉放大預覽時同步取消運算並釋放資源。
   - **未就緒時極簡載入指示**：
     - **草稿優先**：若目前張的頁面縮圖已在記憶體中，換頁當下立即以低解析草稿填入 `Zoom.Image`（0 毫秒感知延遲），背景再非同步繪製 1600px 高畫質圖覆蓋，使用者完全不會看到空白。
     - **極簡 Spinner 載入態**：若連縮圖都尚未就緒（冷啟動或極大檔案），**嚴禁在白紙正中央顯示重複的「第 N 張」標題文字**；改為在紙張正中央呈現**極簡微型旋轉載入圈（Minimal Spinner，20×20 px，淡色筆觸）**，並居中提示 `ui_print_loading_preview`（「正在載入預覽…」），風格精簡安靜，不喧賓奪主；高畫質完成後平滑覆蓋。

- **預覽載入中狀態**：版面計算尚未完成時（`CurrentSheet == null`），預覽畫布繪製淡灰色紙張骨架虛線框並居中提示「正在載入預覽…」，避免空白或撕裂；計算完成後平滑帶入頁面與非同步縮圖串流。
縮圖在背景執行緒產生，先顯示空白紙與格子，畫好再補上；改選項時只重排，已畫好的頁面縮圖依頁碼快取重用（灰階或加深改變時才重畫）。

### 5.4 底部列

左邊一行用紙統計，右邊「開始列印」：

- 細項用 `ui_list_separator` 串起：`{count} 個檔案`（2 個以上才列）、`{count} 頁`、`每張 {count} 頁`（2 以上才列）、`雙面`（雙面時）、`{count} 份`（2 以上才列），再套進「{details}，共用 {sheets} 張紙」。例：「3 個檔案、15 頁、雙面，共用 8 張紙」。
- 有檔案一頁都沒選到時，下一行橘色：「「收據.pdf」不會印：選的頁碼都不在這份裡」，多個時「{names} 不會印：選的頁碼都不在這些檔案裡」（`{names}` 用 `Strings.ShortList`）。
- 全部檔案都沒選到頁面時，「開始列印」停用，提示「選的頁碼都不在這些檔案裡」。
- **印表機搜尋中狀態**：若印表機仍在背景搜尋中，`IsReady` 判定為 false，「開始列印」按鈕呈停用狀態，按鈕文案顯示為「正在搜尋印表機…」。

## 6. 介面文字

新增的 `ui_*` 鍵（照《介面文字寫法》：完整句子、具名參數；英文 `_one` 依慣例）：

| 鍵 | 繁中 | 簡中 | 英文 | 日文 | 韓文 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| ui_print_pages | 頁面 | 页面 | Pages | ページ | 페이지 |
| ui_print_all_pages | 全部頁面 | 全部页面 | All pages | すべてのページ | 모든 페이지 |
| ui_print_paper | 紙張 | 纸张 | Paper | 用紙 | 용지 |
| ui_print_per_sheet | 每張紙 | 每张纸 | Per sheet | 1 枚あたり | 한 장에 |
| ui_print_one_page | 1 頁 | 1 页 | 1 page | 1 ページ | 1페이지 |
| ui_print_order | 排列 | 排列 | Order | 並び順 | 순서 |
| ui_print_order_rows | 先橫排 Z | 先横排 Z | Across first (Z) | 横が先（Z） | 가로 먼저(Z) |
| ui_print_order_columns | 先直排 N | 先竖排 N | Down first (N) | 縦が先（N） | 세로 먼저(N) |
| ui_print_page_border | 頁框 | 页框 | Page border | ページの枠 | 페이지 테두리 |
| ui_print_size | 大小 | 大小 | Size | サイズ | 크기 |
| ui_print_size_fit | 符合紙張 | 适合纸张 | Fit to paper | 用紙に合わせる | 용지에 맞춤 |
| ui_print_size_actual | 實際大小 | 实际大小 | Actual size | 実際のサイズ | 실제 크기 |
| ui_print_size_custom | 自訂 | 自定义 | Custom | カスタム | 사용자 지정 |
| ui_print_scale_percent | 縮放比例 | 缩放比例 | Scale | 拡大縮小率 | 배율 |
| ui_print_size_fixed_multi | 每張 2 頁以上時，頁面會縮放到格子大小 | 每张 2 页以上时，页面会缩放到格子大小 | With 2 or more pages per sheet, pages are scaled to fit their spaces | 1 枚に 2 ページ以上のときは、ページを枠に合わせて縮小します | 한 장에 2페이지 이상이면 페이지를 칸에 맞게 줄입니다 |
| ui_print_color | 色彩 | 色彩 | Color | カラー | 색상 |
| ui_print_color_color | 彩色 | 彩色 | Color | カラー | 컬러 |
| ui_print_color_gray | 黑白 | 黑白 | Black and white | 白黒 | 흑백 |
| ui_print_when_copies | 多份時 | 多份时 | Multiple copies | 複数部のとき | 여러 부일 때 |
| ui_print_collate | 一份印完再印下一份 | 一份打印完再打印下一份 | Finish each copy before the next | 1 部ずつまとめて印刷 | 한 부씩 인쇄 |
| ui_print_preview_title | 印出來的樣子 | 打印出来的样子 | Print preview | 印刷イメージ | 인쇄 미리보기 |
| ui_print_preview_double_click | 點兩下放大 | 双击放大 | Double-click to zoom | ダブルクリックで拡大 | 두 번 클릭하여 확대 |
| ui_print_sheet_of | 第 {no} 張，共 {count} 張 | 第 {no} 张，共 {count} 张 | Sheet {no} of {count} | {no} / {count} 枚目 | {count}장 중 {no}번째 |
| ui_print_file_of | 第 {no} 個，共 {count} 個 | 第 {no} 个，共 {count} 个 | File {no} of {count} | {no} / {count} 件目 | {count}개 중 {no}번째 |
| ui_print_previous_file | 上一個檔案 | 上一个文件 | Previous file | 前のファイル | 이전 파일 |
| ui_print_next_file | 下一個檔案 | 下一个文件 | Next file | 次のファイル | 다음 파일 |
| ui_print_previous_sheet | 上一張紙 | 上一张纸 | Previous sheet | 前の用紙 | 이전 장 |
| ui_print_next_sheet | 下一張紙 | 下一张纸 | Next sheet | 次の用紙 | 다음 장 |
| ui_print_file_pages | 共 {count} 頁 | 共 {count} 页 | {count} pages（_one：1 page） | 全 {count} ページ | 총 {count}페이지 |
| ui_print_file_pages_selected | 共 {count} 頁，印其中 {selected} 頁 | 共 {count} 页，打印其中 {selected} 页 | {count} pages, printing {selected} | 全 {count} ページ中 {selected} ページを印刷 | 총 {count}페이지 중 {selected}페이지 인쇄 |
| ui_print_file_skipped | 只有 {count} 頁，選的頁碼都不在這份裡，不會印 | 只有 {count} 页，选的页码都不在这份里，不会打印 | Only {count} pages; none of the selected pages are in this file, so it won't be printed（_one：Only 1 page; …） | {count} ページしかなく、選んだページが含まれないため印刷しません | {count}페이지뿐이라 선택한 페이지가 없어 인쇄하지 않습니다 |
| ui_print_sheet_portrait | {paper} 直向 | {paper} 纵向 | {paper} portrait | {paper} 縦 | {paper} 세로 |
| ui_print_sheet_landscape | {paper} 橫向 | {paper} 横向 | {paper} landscape | {paper} 横 | {paper} 가로 |
| ui_print_sheet_portrait_multi | {paper} 直向，每張 {count} 頁 | {paper} 纵向，每张 {count} 页 | {paper} portrait, {count} pages per sheet | {paper} 縦、1 枚に {count} ページ | {paper} 세로, 한 장에 {count}페이지 |
| ui_print_sheet_landscape_multi | {paper} 橫向，每張 {count} 頁 | {paper} 横向，每张 {count} 页 | {paper} landscape, {count} pages per sheet | {paper} 横、1 枚に {count} ページ | {paper} 가로, 한 장에 {count}페이지 |
| ui_print_outside_not_printed | 橘色虛線外的部分不會印到 | 橙色虚线外的部分不会打印 | Anything outside the orange dashed line won't print | オレンジの点線の外側は印刷されません | 주황색 점선 밖은 인쇄되지 않습니다 |
| ui_print_summary | {details}，共用 {sheets} 張紙 | {details}，共用 {sheets} 张纸 | {details}: {sheets} sheets of paper（_one：{details}: 1 sheet of paper） | {details}、用紙 {sheets} 枚 | {details}, 용지 {sheets}장 |
| ui_print_summary_files | {count} 個檔案 | {count} 个文件 | {count} files | {count} 件のファイル | 파일 {count}개 |
| ui_print_summary_pages | {count} 頁 | {count} 页 | {count} pages（_one：1 page） | {count} ページ | {count}페이지 |
| ui_print_summary_per_sheet | 每張 {count} 頁 | 每张 {count} 页 | {count} per sheet | 1 枚に {count} ページ | 한 장에 {count}페이지 |
| ui_print_summary_two_sided | 雙面 | 双面 | two-sided | 両面 | 양면 |
| ui_print_summary_copies | {count} 份 | {count} 份 | {count} copies | {count} 部 | {count}부 |
| ui_print_skipped_one | 「{name}」不會印：選的頁碼都不在這份裡 | “{name}”不会打印：选的页码都不在这份里 | “{name}” won't be printed: none of the selected pages are in it | 「{name}」は印刷しません：選んだページが含まれていません | ‘{name}’은(는) 인쇄하지 않습니다: 선택한 페이지가 없습니다 |
| ui_print_skipped_many | {names} 不會印：選的頁碼都不在這些檔案裡 | {names} 不会打印：选的页码都不在这些文件里 | {names} won't be printed: none of the selected pages are in them | {names} は印刷しません：選んだページが含まれていません | {names}은(는) 인쇄하지 않습니다: 선택한 페이지가 없습니다 |
| ui_print_no_selected_pages | 選的頁碼都不在這些檔案裡 | 选的页码都不在这些文件里 | None of the selected pages are in these files | 選んだページはどのファイルにもありません | 선택한 페이지가 어느 파일에도 없습니다 |

- 不再使用的 `ui_print_fit_to_paper` 刪除（孤兒測試會檢查）。
- 已有同意思的鍵（例如紙張、頁面範圍）先沿用既有鍵，不要重複新增；表中的鍵名只是建議。
- 英文 `_one` 變體照 `Strings.Count` 慣例新增，其他語言的 `_one` 值與基本值相同。

## 7. 測試

`TestSuite.PdfPrint`（Core，可在 Linux 跑的放前面）：

1. 排版器：A4 直向頁、每張 1 頁、自動 → 直向，一格填滿可印範圍。
2. 排版器：直向頁、每張 2 頁、自動 → 橫向紙、2×1；橫向頁、每張 2 頁、自動 → 直向紙、1×2。
3. 排版器：每張 4 頁、`Z` 與 `N` 的頁碼順序（1 2 / 3 4 與 1 3 / 2 4）。
4. 排版器：13 頁、每張 4 頁 → 4 面；雙面時 `PaperCount` = 2；單面 = 4。
5. 排版器：`actual` 放 A3 頁到 A4 紙 → `Overflows = true`；`custom` 50% → 不超出且大小是原尺寸一半。
6. 排版器：每張 2 頁以上時 `actual`／`custom` 一律當 `fit`。
7. 選項：`fit_to_page=false` 且沒有 `scale` → `actual`；`ToExplicitDictionary` 寫出全部新欄位；`NormalizeOptions` 不會把明確傳入的值換成預設值。
8. 頁面範圍：三個檔案（1、12、2 頁）、範圍「3-5」→ 第 1、3 個檔案沒有頁面、第 2 個 3 頁。
9. 處理器沿用既有的旋轉頁面測試（直式 A4＋`/Rotate 90` → 842×595）。

新介面：`DesktopLocalization` 既有守衛會檢查新鍵；另加一項：列印卡片的預覽張數與 `PrintLayoutPlanner` 的結果一致（用假的頁面尺寸跑 `PrintPreviewViewModel`）。

## 8. 交付

- 版本：在 v3.22.x 的下一個修正版號（以 Antigravity 開分支時的 main 為準），CHANGELOG 與五個商店說明同步。
- 規格：v4_desktop_ui_layout.md 4.9「列印預覽」補上多檔案規則與「自動」方向規則，〈4.0 上線前清單〉13 標為完成；決策紀錄加一列。
- 實機驗收（Antigravity 在 Windows 上）：三個檔案（含一份 `/Rotate 90` 的表單）各試每張 1／2／4 頁、雙面、黑白、實際大小，預覽與印出結果一致。
