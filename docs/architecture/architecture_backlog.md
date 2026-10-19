# Clickra 待辦問題清單

整理日期：2026-10-14（L3、L4 項目於同日補充）　基準：分支 `refactor/storage-settings-boundaries`，HEAD `870967a8`（未 push）
每一項都在程式碼確認過；行號以該 HEAD 為準。優先順序：🔴 先處理　🟡 排得進來就做　⚪ 記錄即可

---

## A. 分支與 PR（GPT 目前在處理）

| # | 優先 | 問題 | 建議 |
|---|---|---|---|
| A1 | 🔴 | 分支有 40 個 commit 只存在本機，從未 push | 先 push 一個備份分支（如 `backup/architecture-refactor-1014`），不開 PR |
| A2 | 🔴 | 40 個 commit 一次合併太大（259 檔，+7.5k／−4.3k） | 拆成疊加 PR：A `140cc5c6` Office 修復（直接接 main）→ B storage／settings／presentation → C module-first 遷移 → D Native 執行統一 → E 命名＋版面表 |
| A3 | 🟡 | 幾個 docs commit 寫「最終驗證」「收尾完成」，後面又改了二十幾個 commit，內容已過時（`82c8a6f6`、`5199ed37`、`a0979834`、`d0c35daf`） | 在 PR E 統一更新一次 `docs/development/clickra_architecture.md` |
| A4 | 🔴 | 版面表第三項（點擊範圍修正）尚未提交，還差：① 預覽框畫圖改讀 `VisualSplitterLayout.PreviewArea`（`ProgressWindow.VisualSplitter.cs` 約 593–600 行仍自己算）② `Inflate` 移到 `LayoutRect` ③ 刪除 `TestSuite.VisualSplitterLayout.cs` 重複的 `Assert.Equal(136, PagesButton(true).X)` ④ Windows 實機點「＋」「－」「上一頁」「下一頁」「切開」與預覽框 | 補完並實機驗證後以 `fix(cli):` 提交，放進 PR E |
| A5 | 🟡 | GPT 未提交的架構圖原型仍在工作區：`.github/workflows/ci.yml`、`scripts/hooks/pre-push`、`docs/development/clickra_architecture.md`、`scripts/architecture/`、`clickra_architecture_map.svg`、`clickra_architecture_files.html`；已知問題是寫死的數量與一律放行的規則 | 決定保留或丟棄；無論如何不要混進上面的 PR |

## B. L0 入口

| # | 優先 | 問題 | 位置 | 建議 |
|---|---|---|---|---|
| B1 | 🟡 | Launcher 轉交參數用 `string.Join(" ", args)`，含空白的檔案路徑會被拆開 | `src/ClickraLauncher/Program.cs:74` | 逐一加引號（或改用 `ConvertCommandRegistry.SplitCommandLine` 的反向）。目前只影響啟動 Fluent 的路徑，Fluent 未發佈所以還沒爆 |
| B2 | 🟡 | 「要開哪個 UI」的判斷寫了三份 | `ClickraShell/ComMethods.cs:305`、`ClickraLauncher/Program.cs`（TryActivateFluent）、`Clickra.CLI/Cli/ClickraStartup.cs:226`（TryLaunchFluent） | 收斂成一個規則；至少三處行為要一致 |
| B3 | ⚪ | Fluent 沒有隨套件發佈，但每次啟動仍先嘗試：Launcher 必定 COM 啟動失敗、Clickra.exe 無參數時去找 `Clickra.Fluent.exe` | 同上 | 保留 Fluent 的決定不變；可考慮用建置旗標在未附 Fluent 時跳過嘗試 |

## C. L1 畫面

| # | 優先 | 問題 | 位置 | 建議 |
|---|---|---|---|---|
| C1 | 🔴 | L1 視覺分割狀態直接呼叫 L3 處理器，跳過 L2 | `Core/Modules/PdfSplit/Presentation/VisualSplitPresentationController.cs:187` → `PdfSplitProcessor.BuildSegmentSpec` | 在 `Modules/PdfSplit/Application/` 加一個對外入口（類似已有的 `PdfPageCountQuery`），L1 改呼叫它；加一條 guard 禁止 Presentation 引用 `*.Processing` |
| C2 | ⚪ | 三個 renderer 的 XML 註解還寫著由 `PdfSplitProcessor` 產生分割字串（實際程式沒有呼叫），C1 修完後會變成錯的說明 | `ProgressWindow.VisualSplitter.cs:438`、`VisualSplitterControl.xaml.cs:27, 206`、`SplitPagesOverlay.xaml.cs:15` | 跟 C1 一起改註解 |
| C3 | ⚪ | `ProgressLayout.ScrollbarHit` 仍是獨立的含邊界判斷（寬度是 float，GPT 刻意不併入 `ContainsInclusive`） | `Clickra.CLI/UI/Layout/ProgressLayout.cs` | 可接受，記錄即可 |

## D. L2 應用

| # | 優先 | 問題 | 位置 | 建議 |
|---|---|---|---|---|
| D1 | 🔴 | CLI 無畫面模式自己又寫了一份「取用例 → Plan → ExecuteAsync」流程，與 L1 轉換執行重複；翻譯指令還有第三份 | `Clickra.CLI/Cli/ClickraCli.cs:84–104`（翻譯）、`:402–418`（一般）對照 `Core/Presentation/Convert/ConversionExecutionPresentationController.cs:50–88` | 抽成 L2 的單一執行服務，CLI 與 L1 都呼叫它 |
| D2 | 🟡 | 頁數查詢由兩個 renderer 各自直接呼叫，而不是經 L1 視覺分割狀態 | `ProgressWindow.VisualSplitter.cs`、`Fluent/Controls/VisualSplitterControl.xaml.cs` → `PdfPageCountQuery` | 讓 `VisualSplitPresentationController` 負責取頁數，renderer 只讀狀態 |
| D3 | 🟡 | 入口層直接操作任務紀錄，並用 `Thread.Sleep(1500)` 讓 Dashboard 有時間顯示失敗 | `Clickra.CLI/Cli/ClickraStartup.cs:97–103`（ReportDispatchError） | 改由 L2 提供「記錄啟動失敗」的服務；去掉睡眠時序假設 |
| D4 | ⚪ | `ConversionTaskProgressService` 仍同時寫舊的單一紀錄（active.tmp） | `Core/Application/ConversionTaskProgressService.cs` | 依專案限制保留，記錄即可 |

## E. L3 與其他

| # | 優先 | 問題 | 位置 | 建議 |
|---|---|---|---|---|
| E1 | 🟡 | 在地化直接讀儲存層設定 | `Core/Localization/Localization.cs:81, 203` → `ClickraStorage.GetSetting` | 改經 `SettingsService` 或由呼叫端傳入語系 |
| E2 | 🟡 | `FileProcessor` 正式程式已無呼叫者，只剩測試在用 | `Core/FileProcessor.cs`；測試：ImageCompression、ImageConvert、MarkdownToPdf、MarkdownToWord、PdfDecrypt、PdfSplit、ArchitectureBoundaries | 測試改走用例後刪除 |
| E3 | 🟡 | PDF 縮圖由畫面直接呼叫 L3，跳過 L1／L2；Native 還用它另外取頁數（與 `PdfPageCountQuery` 重複） | `ProgressWindow.VisualSplitter.cs:133, 138, 176, 251`、`VisualSplitterControl.xaml.cs:357–358`、`UI/LocalizedUiFontSelector.cs:7` → `Core/Rendering/PdfPageThumbnailRenderer` | 在 PdfSplit 的 L2 提供縮圖／頁數入口，畫面只經 L1 視覺分割狀態取用（與 C1、D2 一起處理） |
| E4 | 🟡 | 上層直接引用 L3 的型別：Markdown 選項、圖片壓縮等級 | `ClickraCli.cs:180`、`ProgressWindow.MarkdownOptions.cs:177–198`、`Fluent/Controls/FluentDialogs.cs` → `MarkdownPdfOptions`、`MarkdownTemplateSource`；`SettingsService`、`SettingsContract`、`SettingDescriptor`、`SettingsPresentationController` → `ImageCompressionLevel`（`Modules/Image/Processing/`） | 把這些選項／列舉移到各模組的 Application（或 Contract）層，處理器反過來引用它們 |
| E5 | 🟡 | Office 處理器與 Office 引擎互相引用（循環） | `Office/Processing/{Word,Ppt,Excel}ToPdfProcessor.cs` → `PowerShellHelper`；`Office/Infrastructure/{LibreOfficeHelper,PowerShellHelper}.cs` → `OfficeConversionRequest`／`OfficeConversionSettings`／`OfficeInteropRequest`（定義在 `Processing/OfficeConversionOptions.cs`） | 把請求型別移到 Infrastructure（或獨立的 Contract 檔），讓依賴只往一個方向 |
| E6 | 🟡 | 翻譯的 Model 反過來引用 Pipeline：檔案層級唯一跨資料夾的循環（另一組在 GrayPrompts/ 資料夾內，可接受） | `PdfTranslation/Model/PdfParagraph.cs` → `Pipeline/Paragraphs/` 的 `PdfParagraphAnalysis`、`PdfParagraphLayoutAnalyzer`、`PdfParagraphMathClassifier`、`PdfTextLineMerger`（4 條；其中 3 個分析器又引用回 `PdfParagraph`） | 要改程式：分析結果放進 Model（純資料），分析邏輯留在 Pipeline；E8 搬完後只剩這 4 條（見 L4 翻譯區） |
| E7 | 🟡 | 儲存層門面與各 Store 互相引用（11 個檔一個循環）：Store 用到 `ClickraStorage` partial 檔裡定義的紀錄型別，門面又呼叫 Store；`ParkedTaskCoordinator.cs` 一個檔引用 11 個儲存檔 | `Core/Storage/`（見 L4 儲存面板） | 把紀錄型別抽到獨立檔（例如 `TaskRecords.cs`），讓依賴只由門面 → Store → 紀錄型別往下走 |
| E8 | 🟡 | 翻譯 Pipeline 9 個子資料夾＋Model 在資料夾層級全部互相引用；但展開到檔案，253 條引用中只有 23 條是「回頭引用」，原因是共用的分類器／幾何型別放在高層資料夾 | 見 L4 翻譯區「現在的回頭引用」清單 | **只搬檔案、不改程式**：新資料夾 `Pipeline/Shared/` 放 `PdfParagraphRoleClassifier`、`PdfParagraphSemanticClassifier`、`PdfChartLabelClassifier`、`PdfGrayPromptClassifier`、`ReferenceSectionDetector`、`PdfMaskGeometry`、`TableMaskRegion`、`PdfLayoutElement`、`PdfLayoutRow`（23 → 5）；`PdfTranslationLayoutPlanner` 搬到 `Rendering/`（5 → 4）。共 10 個檔，每個都必要（已逐一驗證）；剩下 4 條是 E6。搬完可加一條架構測試禁止回頭引用 |

---

## 已處理（供對照）

- Controller 命名統一（`1e360d8d`）、History 查詢型別合併（`134a416e`）、Native 版面表搬到 CLI（`5a057a3a`、`ee242c3d`、`4179d217`、`cd7c3f24`）
- 架構測試不再略過缺少的資料夾（`561120b6`）、含邊界判斷統一為 `LayoutRect.ContainsInclusive`（`870967a8`）
- **E8（PR #194）**：翻譯共用分類器與幾何型別收斂至 `Pipeline/Shared/`，回頭引用由 23 條降至 4 條。
- **D1、E4、C1（PR #195）**：引入 `ConversionExecutionService` 統一規劃與執行；將 Options 解耦移至各模組；視覺分割解耦。
- **E2（PR #196）**：徹底移除 `FileProcessor` 舊門面，所有用例直通模組 Processing。
- **E7（PR #197）**：引入 `ITaskLifecycleRepository` 介面，`ConversionTaskLifecycle` 與底層 `ClickraStorage` 實體徹底解耦。
