# Clickra 待辦問題清單

整理日期：2026-10-14（L3、L4 項目於同日補充；C4、D5 與 E4 更正於 10/16；10/20 補上現況表）　基準：分支 `refactor/storage-settings-boundaries`，HEAD `870967a8`（未 push）
每一項都在程式碼確認過；行號以該 HEAD 為準。優先順序：🔴 先處理　🟡 排得進來就做　⚪ 記錄即可

---

## 10/20 現況（依 main `ea1543d9` 重新檢查）

下面 A–E 各表保留原始描述（行號以 10/14 的 HEAD 為準），實際狀態以這張表為準。✅ 已處理　⚠ 部分處理或仍存在　❓ 未重新檢查

| # | 狀態 | 依據 |
|---|---|---|
| A1–A5 | ✅ | 分支已拆成 PR 依序合併進 main（至 #197）；本區不再追蹤 |
| B1 | ✅ | `ClickraLauncher/Program.cs` 改為逐一 `QuoteArgument` 後再 Join |
| B2 | ⚠ | 仍有兩處各自判斷：`ClickraLauncher/Program.cs`（TryActivateFluent）、`Clickra.CLI/Cli/ClickraStartup.cs`（TryLaunchFluent） |
| B3 | ⚪ | 記錄即可 |
| C1 | ✅ | `PdfSplit/Presentation` 已不引用 `PdfSplitProcessor` |
| C2 | ❓ | 隨 C1 處理，註解未逐一確認 |
| C3 | ⚪ | 記錄即可 |
| C4 | ⚠ | 畫面端仍有指令判斷，例如 `ProgressWindow.cs:259` 的 `command.Equals("md2pdf", …)` |
| C5 | ✅ | `LibreOfficeEnginePackage` 只剩 Infrastructure 內部使用 |
| D1 | ✅ | CLI 改呼叫 L2 的 `ConversionExecutionService.Plan/Execute` |
| D2 | ✅ | renderer 已不直接呼叫 `PdfPageCountQuery` |
| D3 | ✅ | 啟動失敗改由 `ConversionTaskLifecycle.RecordDispatchFailure` 記錄，啟動流程不再睡 1500ms（`ProgressWindow.Process.cs:119` 的 1500ms 是成功後關窗前顯示完成訊息，另一件事） |
| D4 | ⚪ | 依專案限制保留 |
| D5 | ✅ | 模組 Application 已不直接呼叫 `SettingsService` |
| E1 | ✅ | `Localization` 已不讀 `ClickraStorage` |
| E2 | ✅ | `Core/FileProcessor.cs` 已刪除 |
| E3 | ⚠ | 畫面仍直接用 `PdfPageThumbnailRenderer`：`ProgressWindow.VisualSplitter.cs`、`UI/LocalizedUiFontSelector.cs`、`Fluent/Controls/VisualSplitterControl.xaml.cs` |
| E4 | ✅ | `MarkdownPdfOptions`、`ImageCompressionLevel`、`OfficeConversionRequest` 都已移到各模組 `Application/`（E5 一併解掉） |
| E6 | ✅ | `PdfTranslation/Model` 已不引用 Pipeline 的分析器 |
| E7 | ⚠ | `ClickraStorage.*.cs` partial 檔仍在 `Core/Storage/`；拆紀錄型別的 commit `9f810c3b` 在未合併分支 `refactor/module-architecture-convergence` |
| E8 | ✅ | 已建 `Pipeline/Shared/`（PR #194） |
| E9 | ⚠ | 已有 `TestSuite.ArchitectureBoundaries.cs`，但模組慣例文件尚未寫 |

---

## A. 分支與 PR（GPT 目前在處理）

| # | 優先 | 問題 | 建議 |
|---|---|---|---|
| A1 | 🔴 | 分支有 40 個 commit 只存在本機，從未 push | 先 push 一個備份分支（如 `backup/architecture-refactor-1014`），不開 PR |
| A2 | 🔴 | 40 個 commit 一次合併太大（259 檔，+7.5k／−4.3k） | 拆成疊加 PR：A `140cc5c6` Office 修復（直接接 main）→ B storage／settings／presentation → C module-first 遷移 → D Native 執行統一 → E 命名＋版面表 |
| A3 | 🟡 | 幾個 docs commit 寫「最終驗證」「收尾完成」，後面又改了二十幾個 commit，內容已過時（`82c8a6f6`、`5199ed37`、`a0979834`、`d0c35daf`） | 在 PR E 統一更新一次 `docs/development/clickra_architecture.md` |
| A4 | 🔴 | 版面表第三項（點擊範圍修正）尚未提交，還差：① 預覽框畫圖改讀 `VisualSplitterLayout.PreviewArea`（`ProgressWindow.VisualSplitter.cs` 約 593–600 行仍自己算）② `Inflate` 移到 `LayoutRect` ③ 刪除 `TestSuite.VisualSplitterLayout.cs` 重複的 `Assert.Equal(136, PagesButton(true).X)` ④ Windows 實機點「＋」「－」「上一頁」「下一頁」「切開」與預覽框 | 補完並實機驗證後以 `fix(cli):` 提交，放進 PR E |
| A5 | 🟡 | GPT 未提交的架構圖原型仍在工作區：`.github/workflows/ci.yml`、`scripts/hooks/pre-push`、`docs/development/clickra_architecture.md`、`scripts/architecture/`、`clickra_architecture_map.svg`、`clickra_architecture_files.html`；已知問題是寫死的數量與一律放行的規則 | **保留並提交 `scripts/architecture/SemanticGraph/`**（Roslyn 引用圖工具，Claude 的 L4 架構圖已改用它的輸出）；其餘（`generate_architecture.py`、svg／html、CI／pre-push 改動）可丟棄或另議；無論如何不要混進上面的 PR |

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
| C4 | 🟡 | 畫面端逐一辨認模組：每個畫面都用「指令是不是 X」決定要問使用者什麼（密碼、頁面、壓縮等級、Markdown 選項…），新增模組或改模組需要的輸入就得同時改 Native、CLI、Fluent。功能模組彼此之間 0 引用、共用同一套基底，這部分是健康的；問題只在畫面端 | `Clickra.CLI/Progress/NativeConversionPresentationAdapter.cs:53–119`（14 個指令、23 處 `XxxUseCase.CommandName`）；`Clickra.CLI/Cli/ClickraCli.cs:59–385`（10 處）；`Clickra.Fluent/TaskProgressPage.xaml.cs:208–211`、`MainPage.xaml.cs:535–536` 寫死 `"md2pdf"`／`"md2word"`；`Core/Application/ConversionInteraction.cs` 的 `IConversionInteraction` 只有固定三種詢問（密碼、分割頁、Markdown 選項），新增一種也要改介面與每個畫面 | 讓每個用例宣告自己需要的輸入（例如 `RequiredInputs`：密碼／頁面範圍／壓縮等級／Markdown 選項），畫面依宣告顯示對應的輸入元件，不再認得具體模組；與 D1（執行流程三份）一起處理 |
| C5 | ⚪ | L1 Office 引擎設定狀態用到 L3 引擎的型別：L2 服務直接回傳 Infrastructure 的 `LibreOfficeEnginePackage`，L1 讀它的欄位，所以 L1 間接依賴引擎層（與 E4 同類：型別放在下層） | `Office/Presentation/OfficeSettingsPresentationController.cs:89` → `Office/Application/LibreOfficeSettingsService.cs:56`（`RecommendedPackage`）→ `Office/Infrastructure/LibreOfficeEngineInstaller.cs` | 讓 L2 服務回傳自己的資料型別（只含版本、版別、大小、雜湊），與 E4 一起處理 |

## D. L2 應用

| # | 優先 | 問題 | 位置 | 建議 |
|---|---|---|---|---|
| D1 | 🔴 | CLI 無畫面模式自己又寫了一份「取用例 → Plan → ExecuteAsync」流程，與 L1 轉換執行重複；翻譯指令還有第三份 | `Clickra.CLI/Cli/ClickraCli.cs:84–104`（翻譯）、`:402–418`（一般）對照 `Core/Presentation/Convert/ConversionExecutionPresentationController.cs:50–88` | 抽成 L2 的單一執行服務，CLI 與 L1 都呼叫它 |
| D2 | 🟡 | 頁數查詢由兩個 renderer 各自直接呼叫，而不是經 L1 視覺分割狀態 | `ProgressWindow.VisualSplitter.cs`、`Fluent/Controls/VisualSplitterControl.xaml.cs` → `PdfPageCountQuery` | 讓 `VisualSplitPresentationController` 負責取頁數，renderer 只讀狀態 |
| D3 | 🟡 | 入口層直接操作任務紀錄，並用 `Thread.Sleep(1500)` 讓 Dashboard 有時間顯示失敗 | `Clickra.CLI/Cli/ClickraStartup.cs:97–103`（ReportDispatchError） | 改由 L2 提供「記錄啟動失敗」的服務；去掉睡眠時序假設 |
| D4 | ⚪ | `ConversionTaskProgressService` 仍同時寫舊的單一紀錄（active.tmp） | `Core/Application/ConversionTaskProgressService.cs` | 依專案限制保留，記錄即可 |
| D5 | ⚪ | 6 個模組的用例直接呼叫靜態 `SettingsService` 讀全域設定，而不是由呼叫端把選項傳進來；能用，但模組依賴全域狀態、較難單獨測試 | `Office/Application/LibreOfficeSettingsService.cs`、`OfficeToPdfUseCaseBase.cs`、`Office/Presentation/OfficeSettingsPresentationController.cs`；`Image/Application/ImageFormatConvertUseCase.cs`、`ImageOutputSafety.cs`、`ImgCompressUseCase.cs`；`PdfTranslation/Application/TranslatePdfUseCase.cs`；`Markdown/Application/MarkdownConversionUseCaseBase.cs`；`PdfCompression/Application/CompressPdfUseCase.cs`；`PdfDecrypt/Application/DecryptPdfUseCase.cs` | 記錄即可；若做 C4，可順便讓設定值隨請求傳入 |

## E. L3 與其他

| # | 優先 | 問題 | 位置 | 建議 |
|---|---|---|---|---|
| E1 | 🟡 | 在地化直接讀儲存層設定 | `Core/Localization/Localization.cs:81, 203` → `ClickraStorage.GetSetting` | 改經 `SettingsService` 或由呼叫端傳入語系 |
| E2 | 🟡 | `FileProcessor` 正式程式已無呼叫者，只剩測試在用 | `Core/FileProcessor.cs`；測試：ImageCompression、ImageConvert、MarkdownToPdf、MarkdownToWord、PdfDecrypt、PdfSplit、ArchitectureBoundaries | 測試改走用例後刪除 |
| E3 | 🟡 | PDF 縮圖由畫面直接呼叫 L3，跳過 L1／L2；Native 還用它另外取頁數（與 `PdfPageCountQuery` 重複） | `ProgressWindow.VisualSplitter.cs:133, 138, 176, 251`、`VisualSplitterControl.xaml.cs:357–358`、`UI/LocalizedUiFontSelector.cs:7` → `Core/Rendering/PdfPageThumbnailRenderer` | 在 PdfSplit 的 L2 提供縮圖／頁數入口，畫面只經 L1 視覺分割狀態取用（與 C1、D2 一起處理） |
| E4 | 🟡 | **選項／請求型別一律放在 `Processing/` 這條慣例本身有問題**（10/16 合併原 E4、E5）：各模組都照做，但畫面端要設定選項就得直接引用處理器層（Markdown），引擎要讀請求就得反過來引用處理器、形成循環（Office）。另：原本列的「設定 → `ImageCompressionLevel`」是誤判，那些檔用的是同名屬性／常數 | 畫面直接引用：`ClickraCli.cs:180`、`ProgressWindow.MarkdownOptions.cs:177–198`、`Fluent/Controls/FluentDialogs.cs` → `Markdown/Processing/MarkdownPdfOptions.cs`（含 `MarkdownTemplateSource`）；循環：`Office/Infrastructure/{LibreOfficeHelper,PowerShellHelper}.cs` → `Office/Processing/OfficeConversionOptions.cs`（`OfficeConversionRequest`／`OfficeConversionSettings`／`OfficeInteropRequest`），而 `Office/Processing/*ToPdfProcessor.cs` → `PowerShellHelper`；同一慣例的其他檔：`Image/Processing/ImageCompressionLevel.cs`、`ImageCompressionOptions.cs`，`PdfCompression/Processing/PdfCompressionLevel.cs`、`PdfCompressionOptions.cs`、`PdfCompressionSettings.cs` | 規則改成「選項、請求型別放 `Application/`，處理器與引擎反過來引用它」，所有模組一起搬；Markdown 與 Office 先做（解掉畫面直引與循環），其餘照規則搬。搭配 E9 的架構測試守住 |
| E5 | — | （10/16 已併入 E4：Office 處理器與引擎互相引用，是「選項放 Processing」慣例造成的） | 見 E4 | 見 E4 |
| E6 | 🟡 | 翻譯的 Model 反過來引用 Pipeline：檔案層級唯一跨資料夾的循環（另一組在 GrayPrompts/ 資料夾內，可接受） | `PdfTranslation/Model/PdfParagraph.cs` → `Pipeline/Paragraphs/` 的 `PdfParagraphAnalysis`、`PdfParagraphLayoutAnalyzer`、`PdfParagraphMathClassifier`、`PdfTextLineMerger`（4 條；其中 3 個分析器又引用回 `PdfParagraph`） | 要改程式：分析結果放進 Model（純資料），分析邏輯留在 Pipeline；E8 搬完後只剩這 4 條（見 L4 翻譯區） |
| E7 | 🟡 | 儲存層門面與各 Store 互相引用（11 個檔一個循環）：Store 用到 `ClickraStorage` partial 檔裡定義的紀錄型別，門面又呼叫 Store；`ParkedTaskCoordinator.cs` 一個檔引用 11 個儲存檔 | `Core/Storage/`（見 L4 儲存面板） | 把紀錄型別抽到獨立檔（例如 `TaskRecords.cs`），讓依賴只由門面 → Store → 紀錄型別往下走 |
| E8 | 🟡 | 翻譯 Pipeline 9 個子資料夾＋Model 在資料夾層級全部互相引用；但展開到檔案，253 條引用中只有 23 條是「回頭引用」，原因是共用的分類器／幾何型別放在高層資料夾 | 見 L4 翻譯區「現在的回頭引用」清單 | **只搬檔案、不改程式**：新資料夾 `Pipeline/Shared/` 放 `PdfParagraphRoleClassifier`、`PdfParagraphSemanticClassifier`、`PdfChartLabelClassifier`、`PdfGrayPromptClassifier`、`ReferenceSectionDetector`、`PdfMaskGeometry`、`TableMaskRegion`、`PdfLayoutElement`、`PdfLayoutRow`（23 → 5）；`PdfTranslationLayoutPlanner` 搬到 `Rendering/`（5 → 4）。共 10 個檔，每個都必要（已逐一驗證）；剩下 4 條是 E6。搬完可加一條架構測試禁止回頭引用 |
| E9 | 🟡 | 模組沒有寫下來的共同慣例，也沒有測試守住。現況多數已一致（17 個用例都繼承共用基底、13 個處理器都繼承 `SingleFileProcessorBase`／`MultiFileProcessorBase`、都有 Application → Processing 主線），內部依大小不同是合理的；不一致的只有 E4 的選項位置，以及翻譯處理器放在 `Execution/` 而非 `Processing/` | `Modules/*/`；`Modules/PdfTranslation/Execution/PdfTranslateProcessor.cs` | 不要「統一模組內部架構」；寫一份短的模組慣例（放 `docs/development/`）並加架構測試：① 必有 `Application/`，用例繼承共用基底 ② 必有 `Processing/`，處理器繼承共用基底 ③ 選項與請求型別放 `Application/`（E4）④ 只有自己的畫面狀態才建 `Presentation/`、只有包外部引擎才建 `Infrastructure/` ⑤ `Processing/` 以下的子資料夾各模組自由。翻譯的 `Execution/` 改名 `Processing/` 為低優先 |

---

## 已處理（供對照）

- Controller 命名統一（`1e360d8d`）、History 查詢型別合併（`134a416e`）、Native 版面表搬到 CLI（`5a057a3a`、`ee242c3d`、`4179d217`、`cd7c3f24`）
- 架構測試不再略過缺少的資料夾（`561120b6`）、含邊界判斷統一為 `LayoutRect.ContainsInclusive`（`870967a8`）
