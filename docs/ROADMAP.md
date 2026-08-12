# Clickra 發展規劃 (Roadmap)

**規劃週期**：2026年5月 — 2026年8月

本文件定義了 Clickra 後續的功能擴張與視覺體驗優化標準，目標是從一個實用的腳本套件進化為精緻的 Windows 應用程式。

> **里程碑對應**：各子項標題前的編號（如 `[F2-1] Word to PDF`）即 GitHub 里程碑，命名與 GitHub 一致。

## 1. 視覺體驗與使用者介面 (Visuals & GUI)
為了提升 Clickra 的產品質感，已導入符合 Windows 11 Fluent Design 的視覺元素：

- [x] **[F1-1] Menu Visibility Optimization**：選單動態過濾（v3.0.6）。
    - **精確顯示**：已實作 `GetState` 邏輯，確保在不支援的檔案上完全隱藏選單。
- [x] **[F1-2] Native Dashboard Implementation**：Native 儀表板實作（v3.0.6）。
    - 基於 Native Win32 與 AOT 實作，整合 Office 狀態偵測。
- [x] **[F1-3] GUI with Options**：具備設定選項的 GUI（v3.0.8）。
    - **轉換設定與歷史**：提供 Dashboard 設定介面，顯示轉換歷史紀錄。
    - [x] **進度可視化**（v3.0.7）：處理過程中顯示動態進度條與完成通知。
- [x] **[F1-4] Startup Experience**：Windows 啟動體驗（v3.0.6）。
    - 已實作基本啟動顯示（暫不開發額外的現代化介面）。
- [x] **[F1-5] GUI & Layout Fixes**：Dashboard 視窗與佈局修復（v3.0.9）。
    - **最大化佈局修復**：修正視窗最大化後佈局未自適應縮放、背景出現大片黑色空區塊與元件重複重疊繪製的問題。
    - **最小化與系統匣整合**：實作最小化按鈕隱藏至 Windows 系統匣（System Tray / NotifyIcon）的功能，支援雙擊系統匣圖示恢復視窗。
    - **高 DPI 模糊修正 (High DPI Awareness)**：修正高解析度螢幕（2K/4K 等）下畫面模糊的問題，宣告 DPI 感知（DPI Aware）並在 Win32/GDI+ 繪製與佈局計算中動態套用 DPI 縮放因子。
- [x] **[F1-6] Dashboard Minor Enhancements**：Dashboard 細部功能優化（v3.0.9）。
    - **偏好設定自訂路徑**：於設定頁面新增可開啟 Windows 原生資料夾瀏覽器 (FolderBrowserDialog) 的按鈕，允許使用者設定任意自訂的預設輸出目錄。
    - **歷史紀錄卡片展開**：轉換歷史項目支援點擊展開顯示詳細資訊，包括輸入路徑、輸出路徑、原始檔名、轉換起訖時間與精確的轉換耗時。
    - **關於與說明頁面**：新增獨立的說明分頁，展示專案說明、一鍵診斷回報機制說明與協作開發指引。
    - **快速轉檔流程優化**：改為「檔案優先」互動流程，使用者先拖入/選取檔案，系統自動識別類型後，動態標註 (Highlight) 可用的轉檔動作，並停用 (Disable) 不支援的動作。
- [x] **[F1-7] Window Behavior & System Tray Optimization**：視窗行為與系統匣最佳化（v3.1.0）。
    - **系統匣支援調整**：新增進度視窗的最小化至系統匣支援（顯示轉換進度 % 與通知），並由進度視窗作為主要的系統匣承載介面。
    - **單一實例檢查**：主 Dashboard 啟動時強制進行單一實例（Single Instance）Mutex 檢查，重複啟動時直接還原並啟動已有視窗，避免累積多個失效圖示。
    - **進度視窗取消與關閉確認**：進度視窗支援關閉/取消防呆提示，關閉時主動寫入 Failed 歷史狀態（原因標註為 User Aborted）並清理進行中任務，解決歷史遺留 `Converting` 卡死的問題。
    - **歷史日誌拆分儲存**：批次轉換任務在歷史中全面拆分為個別獨立的檔案紀錄行（One Row Per File），在 UI 中移除「X files」欄位，改為直接清晰地顯示每個檔案的名稱與結果。
    - **右鍵選單優化**：當選取項目僅為「單個 PDF」時，在右鍵選單中隱藏 Clickra 主選單，避免出現空的子選單。
- [x] **[F1-8] Dashboard History Layout Optimization**：儀表板歷史排版優化（v3.2.0）。
    - **歷史紀錄與檔名寬度自適應**：實作歷史紀錄排版自適應與檔名寬度自適應，優化設定介面文字排版以消除重疊。
    - **「錯誤/取消」精準化判定**：歷史紀錄欄錯誤狀態「錯誤/取消」精準化判定，修復使用者中途取消卻被判定為成功的 bug。
    - **翻譯目標語言簡化**：移除多餘且暫不可用的 PDF 翻譯目標語言，僅保留下拉式選單與繁體中文選項。
- [x] **[F1-9] Progress Window Inline Input Optimization**：進度視窗內嵌輸入框優化（v3.3.0）。
    - **內嵌式密碼輸入**：實作於進度視窗內繪製與管理 Win32 Edit 控制項，實現無閃爍且支援 Enter 鍵送出、Esc 鍵取消的內嵌密碼輸入。
    - **加密狀態預檢**：自動判定 PDF 是否有加密，無加密檔案直接顯示提示不進行重複要求。
- [x] **[F1-10] Fluent Dashboard Migration (WinUI 3)**：Fluent 主介面與右鍵進度遷移。
    - Dashboard、Settings、History 與右鍵轉換進度已移至 `Clickra.Fluent`，直接重用 `Clickra.Core`、既有設定格式與歷史格式。
    - Explorer 透過 packaged activation 啟動 Fluent；NativeAOT Shell 維持輕量 COM 邊界，舊 Win32 UI 保留為 NativeAOT 軌道 fallback（見 F1-11）。
- [x] **[F1-11] Dual-Track Distribution (Fluent / NativeAOT)**：雙軌發行。
    - 2026/08 決定維持兩條軌道：本機有 .NET 8+ 與 Windows App Runtime → 安裝 Fluent；任一缺失 → 安裝 NativeAOT（零依賴）。
    - 新增 `ClickraSetup.exe`（NativeAOT bootstrapper）自動偵測 runtime 並安裝對應軌道；新增 `Clickra-Native.msix` 零依賴套件與 `scripts/build_native_msix.ps1`。
    - 舊 Win32 Dashboard/Progress 由「過渡 fallback」改為**永久 NativeAOT 軌道**，不再排定移除。
    - 詳細設計見 `docs/development/dual_track_guide.md`。
- [ ] **[F1-12] Fluent Release Stabilization (Dual-Track)**：Fluent 發布穩定化。
    - **2026/08/12 進度**：共用渲染器改走 Windows.Data.Pdf（真實文字/圖片/向量圖，`42221ca`）；NativeAOT 包已本機打包、安裝、執行驗證成功（含 CsWinRT marshalling）。剩 packaged shell activation 端到端、Native ↔ Fluent 同版本切換與乾淨機器安裝的實機驗證。
    - 在 Windows App SDK 2.3.1 下完成 Windows 10/11 的 dashboard 與右鍵實機測試，涵蓋執行中、成功、失敗、取消、PDF 密碼與 Office 雙引擎。
    - 補上 packaged-app 啟動與 shell activation smoke test，避免只有編譯／打包成功但啟動前崩潰。
    - 實機驗證 Native ↔ Fluent 同版本切換（`-ForceUpdateFromAnyVersion`）與乾淨機器（無 .NET / 無 WinAppRuntime）上的 NativeAOT 軌道安裝。

## 2. 核心功能擴張 (Advanced Features)
- [x] **[F2-1] Word to PDF**：Word 轉 PDF（v3.0.6）。
- [x] **[F2-2] Remove PDF Password**：PDF 去除密碼（v3.3.0）。
    - 支援右鍵選單一鍵去除 PDF 密碼保護。
- [x] **[F2-3] Excel to PDF**：Excel 轉 PDF（v3.4.0）。整合微軟 Excel COM 與 LibreOffice 雙引擎轉檔支援。
- [x] **[F2-4] PPT to PDF**：PPT 轉 PDF（v3.0.0）。
- [x] **[F2-5] LibreOffice Offline Office Engine**：LibreOffice 離線 Office 轉檔引擎（v3.5.0）。
    - 支援 Auto / Microsoft Office / LibreOffice 三種 Office 轉檔引擎模式，讓未安裝 Microsoft Office 的使用者可透過本機 LibreOffice 進行 Word、Excel 與 PowerPoint 轉 PDF。
    - 內建 LibreOffice 下載 manifest、官方 MSI 下載、SHA256 驗證、背景安裝/移除、版本比對與重啟需求狀態處理。
    - 轉檔頁改為依 Office、PDF、圖片三組呈現九個主要功能，降低使用者尋找功能時的掃描成本。
- [x] **[F2-6] PDF Shrinking & Compression**：PDF 壓縮與最佳化（v3.6.0）。
    - 實作以內建 PDFsharp 與 GDI+ 為基礎的優化引擎，支援多級壓縮設定（極小、小檔、標準、高品質），自動精簡文字流、字型去重、大字型剝離與圖片高品質雙立方降解析，並在設定頁面實作 4 停靠點的橫向拉條 UI 與 Toggles。
- [x] **[F2-7] PDF Split**：PDF 分割（v3.6.5）。
    - 支援依頁碼範圍（如 `1-5`, `8`）或全頁 (`all` / `each`) 將 PDF 拆分為獨立檔案，整合右鍵選單、原生 GDI+ 頁碼輸入框與 CLI 指令。
- [x] **[F2-8] PDF Merge**：PDF 合併（v3.0.0）。
- [ ] **[F2-9] Advanced PDF Deep Compression**：PDF 進階極限壓縮。
    - **階段一：結構可達性垃圾回收 (DFS GC)**：實作 Catalog 物件樹遍歷，徹底清理編輯殘留的孤立無用物件（Orphan Objects）；優化字型剝離機制，移除字型時保留度量屬性 (Font Metrics) 以防閱讀器渲染跑版。
    - **階段二：二進位物件壓縮流 (Object Streams) [PDF 1.5+]**：引入物件壓縮流 (`ObjStm`) 與交叉引用流 (Cross-Reference Streams)，將大量散落的明文 Dictionary 與 Array 物件打包進行整體 `/FlateDecode` 壓縮。
    - **階段三：影像進階編碼與 Zopfli 無損重壓縮**：針對 1-bit 黑白掃描文件引入 `/JBIG2Decode` 壓縮（可縮減至 1/10 體積且無 JPEG 雜訊）；針對無損 `/FlateDecode` 圖片使用 Zopfli 或 7-Zip Deflate 進行背景極限二次無損精簡。
    - **階段四：字型子集跨頁面合併 (Font Subset Merging)**：解析 OpenType/TrueType 子集二進位，合併同名但字元不全的字型子集，徹底解決多個 PDF 合併後字型資源重複累積、檔案體積異常膨脹的痛點。
- [ ] **[F2-10] PDF to Image**：PDF 轉圖片。一鍵將 PDF 頁面匯出為高品質 JPG/PNG/TIFF，支援自訂 DPI 渲染率、色彩模式與透明背景處理。
- [ ] **[F2-11] PDF to PPTX**：PDF 轉 PPTX。並存/整合三種不同定位之模式，供使用者自選或依 PDF 類型自動推薦：
    - **模式一：原樣保真 (Mode 1: Layout Preservation)**：將每頁 PDF 渲染為圖片並嵌入 PPTX。成功率高、相容性最高，且通常可避免版面跑位，但文字不可編輯；仍可能因 PDF 加密、檔案損毀、不支援字型或渲染失敗等情況而無法完成轉換（參考 `pdf2pptx`）。
    - **模式二：可編輯優先 (Mode 2: Text Reconstruction)**：解析 PDF 文字框、圖片與版面結構重建為 PPTX 各元素，支援 OCR 與字型自訂（參考 `pdf2slides`）。在此模式下，引入 `opendataloader` 的空間網格定位，精確記錄文字大小、色彩與位置以還原排版。
    - **模式三：AI 簡報修復 (Mode 3: AI Slide Repair)**：利用 Gemini AI 抹除頁面中的文字並修補背景，再藉由原始文字座標疊加可編輯文字層，使背景與文字完全分離（參考 `NBLM2PPTX`，即 *NotebookLM to PPTX* 的內部原型／概念驗證工具）。
- [ ] **[F2-12] Document De-identification & Redaction**：文件去識別化與隱私防護。
    - 借鑑 `jt-doc-tools` 的隱私防護與實體擦除設計。支援在本地透過正則表達式配合校驗碼算法（中華民國身分證、統一編號、居留證、信用卡 Luhn 碼等）與 PDF 坐標定位，自動搜尋 PDF 內的敏感資料。
    - 提供「遮蔽（Redact，以黑條塗黑並物理擦除底層字元串流）」與「遮罩（Mask，以同大小/字型大小/色彩的 * 號覆蓋重建）」雙重模式，確保敏感資料無法透過選取或複製還原。
    - 提供選用的本地/雲端 AI（Gemini / Ollama）語意檢核，補強 Regex 無法辨識的人名、職稱與特定合約代號。
    - **隱私安全設計**：敏感資料處理與主流程完全解耦，並在記憶體層級實作 Zeroing memory（安全清零），確保身分證字號等高度敏感資料在轉換完成後不留存於記憶體。
- [ ] **[F2-13] Intelligent Scanner Stitching**：智能多圖與掃描件拼合。
    - 借鑑 `jt-doc-tools` 的證件/掃描拼合算法，支援拉入多個掃描影像，自動偵測有內容的區塊（連通域 BFS 與 Y/X 軸對齊合併），進行自適應裁剪。
    - 實作「背景淨白」功能（僅針對亮度高且飽和度低之中性灰底色進行提亮與漂白，過濾折痕與掃描陰影；完整保留彩色印章、彩色照片及印刷色），並將裁剪出的正反面證件（身分證、健保卡）或數張單據發票一鍵拼合成單張 A4 頁面。
- [x] **[F2-14] PDF Math Translate**：PDF 學術論文翻譯（v3.2.0）。純 C# 原生實現（相容 Native AOT）。自動識別與保護 LaTeX 公式，目前已實作免金鑰 Google 翻譯引擎，具備併發控制與速率限制。
- [x] **[F2-15] PDF Layout Fixes & High-concurrency Translation Optimization**：PDF 佈局修復與高併發翻譯優化（v3.2.1）。解決中文字型與數學字型 TTC 閃退，實作 table 頁面動態橫向合併門檻維持表格列對齊，並重構為 Google Mobile 批次翻譯。
- [ ] **[F2-16] Bilingual/Dual-language Translation**：PDF 雙語對照翻譯模式。
    - 參考 `PDFMathTranslate` 的雙語對照實現，支援在輸出文件保留原文與譯文對照，提供更靈活的閱讀體驗；並擴充公式保護層（利用特殊字型如 Symbol / Cambria Math 自動辨識）。
- [ ] **[F2-17] PDF to Markdown/JSON with Hybrid Parser**：PDF 結構化數據與混合解析。
    - **雙欄/多欄排版與 XY-Cut++ 閱讀順序**：借鑑 `OpenDataLoader` 的雙欄與多欄解析算法，在本地 C# 解析中實現 XY-Cut 投影分割，確保學術論文與複雜文件在提取為 Markdown 時具備正確的閱讀順序。
    - **混合解析模式 (Hybrid Local+AI Mode)**：簡單或純文字頁面直接透過本地快速解析器（如 PDFium / PdfPig）處理，複雜表格、公式與圖表則路由至 Gemini API 等 Vision 端，實現 borderless 表格 HTML 重建與 AI 圖片描述。
    - **解耦設計**：於核心（`src/Clickra.Core`）設計 `IDocParserStrategy` 等解析策略介面（Strategy Pattern），使不同解析引擎（PDFium、AI Vision、PdfPig）能夠靈活切換與擴充。
- [x] **[F2-18] Image to PDF & Merge**：圖片轉 PDF 與合併（v3.0.0）。
- [ ] **[F2-19] PNG/JPG Batch Conversion**：PNG/JPG 批量轉換。支援多種常用格式間的快速互轉。
- [ ] **[F2-20] High-quality Thumbnails**：高品質縮圖。支援批量調整圖片尺寸並保留細節。
- [ ] **[F2-21] Folder & File Batch Renaming**：資料夾與檔案批次命名。支援自訂數字規則、提取建立日期、固定字串與自動編號。可以直接在資料夾右鍵選單對「整個資料夾及其內含檔案」進行操作。
- [ ] **[F2-22] Batch File Categorization**：批次檔案分類。支援依副檔名、日期區間或檔名關鍵字，將檔案自動分類並移入對應的資料夾。
- [ ] **[F2-23] Batch Create Empty Folders**：批量建立空資料夾。支援依指定命名規則與結構要求，一次建立多個指定結構的空資料夾。
- [ ] **[F2-24] Text Encoding & Traditional/Simplified Conversion**：文字編碼與簡繁轉換。支援 Big5/GBK/UTF-8 互轉與原生 `LCMapStringEx` 簡繁字元互轉。
- [ ] **[F2-25] Folder Right-click Direct Conversion**：資料夾右鍵直接轉換。支援直接在資料夾右鍵選單操作，一鍵將資料夾底下的所有支援檔案進行轉換（如 Word 批次轉 PDF、圖片轉 PDF 等），無須進入資料夾手動選取。

## 3. 專案架構與規範 (Refactoring)

> [!IMPORTANT] 品質問題追蹤流程
> 當品質/效能問題在 PR 中被發現（靜態分析、圈複雜度過高等）但本次**不重構**時，必須立即將問題記錄到本節技術債清單並隨 PR 提交，提醒下次開分支時優先修正；修正完成後在項目旁標記日期。

- [x] **[R1-1] Modularization**：模組化拆分（v3.0.6）。
    - 已完成 `src/Clickra.UI` 與 `src/Clickra.Core` 的解耦與 AOT 轉型。
- [ ] **[R1-2] File Naming Cleanup**：檔案命名整理。
    - 統一整理專案內的檔案命名規範，消除歷史遺留的不一致命名。
- [ ] **[R1-3] Complexity Reduction**：圈複雜度重構（技術債）。
    - **視窗訊息路由器 (WndProc Router)**：重構 `DashboardWindow.Events.cs` 的 `WndProc` (當前複雜度 137)，將龐大的 `switch` 拆分為單純的訊息路由，將特定 Win32 訊息指派至專屬的事件方法中處理。
    - [x] **命令模式拆分 (Command Pattern) (2026/08/11 完成)**：重構 `DashboardWindow.Events.Click.cs` 的 `HandleLButtonDown` (原複雜度 130)，將點擊區域偵測與具體功能執行解耦，使每個轉檔功能封裝為獨立的 Command 物件（`ConvertCommandDef` 登錄 + `ConvertCommand`），`HandleLButtonDown` 現為薄路由器（委派給各 tab 的 handler）。
    - **CLI 入口點精簡**：重構 `ClickraCli.cs` 的 `Main` (當前複雜度 89)，將參數解析與 Dashboard 啟動移至獨立的啟動類別。
    - **進度視窗複雜度 (2026/08/08 記錄)**：重構 `ProgressWindow` 系列四個高複雜度方法——`Controls.cs` 的 `InstanceWndProc` (156, critical)、`Process.cs` 的 `RunProcessing` (57, critical)、`Paint.cs` 的 `Paint` (47, very-high)、`VisualSplitter.cs` 的 `PaintVisualSplitter` (35, very-high)，將訊息路由與繪圖拆分為職責單一的方法。
    - [x] **SonarCloud 認知複雜度 (2026/08/09 記錄，同日完成重構)**：SonarCloud 標記 8 個超過認知複雜度門檻 15 的方法已全部重構——`PaintVisualSplitter` (60) 拆成 8 個職責單一的方法（mode bar / n-selector / body / cards / preview panel / preview page / buttons / zoom overlay）、`ProcessSingleFile` (28) 抽 `SplitEachPage`/`ExtractSegments`/`ExtractSingleRange`、`DrawPageImages` (17) 抽 `TryDecodeEmbeddedImage`、`DrawPageWords` (16) 抽 `ResolveWordColor`/`TryDrawWord`、`ApplyVisualSplitMode` (16) 每模式抽方法、`HandleVersionOrDeploy` (17) 抽 `TryHandleVisualSplitterArgs`、`DispatchPdfCommand` (19) 抽 `DispatchPdfCase`/`HandleSplitPdfQuiet`、`ShowInstance` (16) 抽 `RunMessageLoop`。全量重建 0 警告 0 錯誤，分割測試全 PASS。
    - [x] **已移除 (2026/08/09)**：`RenderSyntheticPageThumbnail`（無呼叫端的 dead code，複雜度 17、未用參數、非 static）已直接刪除，消除 SonarCloud S1172/S2325/S3776 三項。
- [ ] **[R1-4] Test Suite Architecture Standardization**：測試套件架構標準化（技術債）。
    - [x] **命名空間整合 (2026/08/08 完成)**：10 個 `TestSuite` partial 檔已移入 `Clickra.Core.Tests` 命名空間並移除 S3903 pragma，解決全域命名空間污染（CS-W1061）。
    - **升級測試框架**：後續規劃將自建的 `TestRunner` 升級為業界標準的單元測試框架（如 xUnit 或 NUnit），以利於在 CI 流程中整合覆蓋率分析。
- [ ] **[R1-5] Quality Gate Observations**：品質閘門觀察。
    - **CS-R1137 readonly 誤報 ×3**：`_isPromptingVisualSplitter`（volatile 欄位不得宣告 readonly）、`_visualSplitZoomDragLastX/Y`（拖曳期間持續變動）——DeepSource 誤判，需在儀表板標 ignore，不得為此改程式碼。
    - **SonarCloud S8970 null-forgiving ×10 已全數修正 (2026/08/09)**：`VisualSplitter.cs` 的 `_tipFont!`/`_msgFont ?? _tipFont!` 並非無法處理——它們是缺 null guard 的症狀。正確修法：n-selector 用 `Font? uiFont = _msgFont ?? _tipFont; if (uiFont == null) return;`、zoom overlay 用 `tipFont`/`uiFont` 兩個非 null local + 前置 guard。全量重建 0 警告 0 錯誤，`!` 在該檔歸零。教訓：遇到「直接刪除會報編譯警告」的 analyzer finding，正確做法是重構出可證明的非 null 路徑，而非保留運算子並標記誤判。
    - **Documentation Coverage 基線移動觀察**：DeepSource 的覆蓋率參考值會隨變更集同步移動（三次 run：0.4→9.7、3.1→12.4、10.7→20，差值恆為 9.3），單靠補文件追不上；新程式碼仍應持續補 XML 文件，閘門門檻需在儀表板設定合理值。
    - **Localization 字典結構性重複 (2026/08/09 記錄)**：SonarCloud 的 Duplications measures 將 `Localization.cs` 的 5 種語言字典鍵結構（鍵相同、值不同）判為重複（New Code 12 行、54.5%）。這是 i18n 字典資料結構的必然模式，且 repo 既有 4 個 143 行字典本就互相重複；消除需將 Localization 重構成「基底字典 + 語言覆寫」的架構級改動，留待 Localization 專項重構，不影響品質閘門（New Code 重複率 0.6% < 3%）。
- [ ] **[R1-6] Dev Scaffolding Cleanup**：開發期清理。
    - [x] **移除視覺分割測試後門**：移除 `ClickraCli.cs` 中硬編碼的測試 PDF 路徑與依執行檔名稱（`TestVisualSplitter` / `ClickraVisualSplitter`）自動進入視覺分割模式的開發測試邏輯，正式版本應僅由 CLI 旗標與參數驅動。
    - [x] **本機工具狀態隔離 (2026/08/11 完成)**：已將 `.freebuff/`（本機工具 SQLite 狀態）與 `Clickra.rar`（本機備份檔）加入 `.gitignore`，避免污染 git status 與誤提交。

## 4. 維護、診斷與離線轉檔插件 (Diagnostics & Offline Fallback)
- [x] **[F3-1] One-click Diagnostic Feedback**：一鍵診斷回報與郵件反饋（v3.0.9）。
    - 提供本地 Native AOT 診斷日誌記錄。於 Dashboard 實作一鍵郵件回報功能，自動打包診斷日誌與系統資訊，透過 Gmail 網頁開啟預設撰寫畫面（並支援標準 mailto: 協議連結作為系統預設郵件用戶端之備用方案）。
- [ ] **[F3-2] Multi-language Diagnostic Feedback Mail**：多語系一鍵診斷回報信件草稿。
    - 依據使用者目前的介面語言，自動撰寫對應語言的 Gmail/mailto 郵件草稿內容與主旨。
- [x] **[F3-3] Local LibreOffice Fallback Engine**：本地免 Office 離線轉檔引擎（v3.5.0）。
    - 已改採 LibreOffice 官方 MSI 作為免費備援引擎來源，並由 Clickra 以內建 manifest 管理下載 URL、版本與 SHA256，避免使用者自行尋找安裝檔或手動指定 `soffice.exe`。
    - 已加入 Auto / Microsoft Office / LibreOffice 引擎選擇、安裝狀態提示、版本相同時避免重複重裝、以及解除安裝後的 pending restart 狀態顯示。
- [ ] **[F3-4] Remote Manifest & Website Integration**：LibreOffice 遠端 manifest 與宣傳頁整合。
    - 在 Clickra 宣傳頁與 GitHub Pages 架構完成後，將 LibreOffice manifest 從純內建資料擴充為可更新的遠端 manifest，讓新版本 LibreOffice 發佈時可不必等待 Clickra 主程式更新。
    - 增加 manifest 簽章或 checksum metadata 防護，確保遠端資料來源可驗證且可回退到內建 manifest。
- [ ] **[F3-5] LibreOffice Setup Maintenance UX**：LibreOffice 安裝維護體驗強化。
    - 補強下載中斷續傳、代理/企業網路提示、安裝取消復原、以及 Windows Installer pending restart 場景的更細緻狀態說明。
- [ ] **[F3-6] Office Engine Abstraction & Integration Tests**：Office 轉檔引擎抽象化與整合測試。
    - 將 `PowerShellHelper.ExportOfficeToPdf` 目前直接讀取設定、呼叫 Microsoft Office COM 與 LibreOffice process 的流程抽出可測試的 engine resolver/strategy。
    - 補上 Auto / Microsoft Office / LibreOffice 模式、Microsoft Office 失敗後 LibreOffice fallback、無可用引擎、pending restart 與使用者指定引擎等整合測試，避免只能靠實機手測驗證。

## 5. PDF 無障礙化與自動標籤 (PDF Accessibility & Auto-Tagging)
- [ ] **[F4-1] PDF Accessibility Audit & WTPDF Rebuild**：PDF 無障礙稽核與 WTPDF 重建。
    - 借鑑 `OpenDataLoader` 與 PDF Association 倡導之規範，對輸入 PDF 進行無障礙 Tag 稽核，判定是否已標記結構樹以供讀屏軟體正常閱讀。
    - 對無標籤的 PDF，實現 100% 本地或 AI 輔助的自動標籤化 (Auto-Tagging)，重建 Headings、List、Table 結構樹，並輸出符合 WTPDF 標準的無障礙 Tagged PDF，協助解決全球無障礙法規合規需求（如 EAA、ADA 508）。
    - **WTPDF 合規與驗證**：遵循 PDF Association 的 WTPDF 規範，並於核心增加結構驗證模組，提供初步結構合規性稽核報告。
