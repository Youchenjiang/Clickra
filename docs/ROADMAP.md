# Clickra 發展規劃 (Roadmap)

**規劃週期**：2026年5月 — 2026年6月

本文件定義了 Clickra 後續的功能擴張與視覺體驗優化標準，目標是從一個實用的腳本套件進化為精緻的 Windows 應用程式。

## 1. 視覺體驗與使用者介面 (Visuals & GUI)
為了提升 Clickra 的產品質感，後續將導入符合 Windows 11 Fluent Design 的視覺元素：

- [x] **選單動態過濾 (Menu Visibility Optimization) [最高優先]**:
    - **精確顯示**: 已實作 `GetState` 邏輯，確保在不支援的檔案上完全隱藏選單。
- [x] **Native 儀表板實作 [v3.0.6]**:
    - 基於 Native Win32 與 AOT 實作，整合 Office 狀態偵測。
- [x] **具備設定選項的 GUI (GUI with Options) [v3.0.8]**:
    - **轉換設定與歷史**: 提供 Dashboard 設定介面，顯示轉換歷史紀錄。
    - [x] **進度可視化 [v3.0.7]**: 處理過程中顯示動態進度條與完成通知。
- [x] **Windows 啟動體驗 (Startup Experience)**:
    - 已實作基本啟動顯示（暫不開發額外的現代化介面）。
- [x] **Dashboard 視窗與佈局修復 (GUI & Layout Fixes) [v3.0.9]**:
    - **最大化佈局修復**: 修正視窗最大化後佈局未自適應縮放、背景出現大片黑色空區塊與元件重複重疊繪製的問題。
    - **最小化與系統匣整合**: 實作最小化按鈕隱藏至 Windows 系統匣（System Tray / NotifyIcon）的功能，支援雙擊系統匣圖示恢復視窗。
    - **高 DPI 模糊修正 (High DPI Awareness)**：修正高解析度螢幕（2K/4K 等）下畫面模糊的問題，宣告 DPI 感知（DPI Aware）並在 Win32/GDI+ 繪製與佈局計算中動態套用 DPI 縮放因子。
- [x] **Dashboard 細部功能優化 (Dashboard Minor Enhancements) [v3.0.9]**:
    - **偏好設定自訂路徑**: 於設定頁面新增可開啟 Windows 原生資料夾瀏覽器 (FolderBrowserDialog) 的按鈕，允許使用者設定任意自訂的預設輸出目錄。
    - **歷史紀錄卡片展開**: 轉換歷史項目支援點擊展開顯示詳細資訊，包括輸入路徑、輸出路徑、原始檔名、轉換起訖時間與精確的轉換耗時。
    - **關於與說明頁面**: 新增獨立的說明分頁，展示專案說明、一鍵診斷回報機制說明與協作開發指引。
    - **快速轉檔流程優化**: 改為「檔案優先」互動流程，使用者先拖入/選取檔案，系統自動識別類型後，動態標註 (Highlight) 可用的轉檔動作，並停用 (Disable) 不支援的動作。
- [x] **視窗行為與系統匣最佳化、歷史紀錄細緻化、右鍵選單優化 [v3.1.0]**:
    - **系統匣支援調整**: 新增進度視窗的最小化至系統匣支援（顯示轉換進度 % 與通知），並由進度視窗作為主要的系統匣承載介面。 (已完成)
    - **單一實例檢查**: 主 Dashboard 啟動時強制進行單一實例（Single Instance）Mutex 檢查，重複啟動時直接還原並啟動已有視窗，避免累積多個失效圖示。 (已完成)
    - **進度視窗取消與關閉確認**: 進度視窗支援關閉/取消防呆提示，關閉時主動寫入 Failed 歷史狀態（原因標註為 User Aborted）並清理進行中任務，解決歷史遺留 `Converting` 卡死的問題。 (已完成)
    - **歷史日誌拆分儲存**: 批次轉換任務在歷史中全面拆分為個別獨立的檔案紀錄行（One Row Per File），在 UI 中移除「X files」欄位，改為直接清晰地顯示每個檔案的名稱與結果。 (已完成)
    - **右鍵選單優化**: 當選取項目僅為「單個 PDF」時，在右鍵選單中隱藏 Clickra 主選單，避免出現空的子選單。 (已完成)
- [x] **儀表板歷史排版優化、選單目標語言簡化與錯誤判定邏輯修正 [v3.2.0]**:
    - **歷史紀錄與檔名寬度自適應**: 實作歷史紀錄排版自適應與檔名寬度自適應，優化設定介面文字排版以消除重疊。 (已完成)
    - **「錯誤/取消」精準化判定**: 歷史紀錄欄錯誤狀態「錯誤/取消」精準化判定，修復使用者中途取消卻被判定為成功的 bug。 (已完成)
    - **翻譯目標語言簡化**: 移除多餘且暫不可用的 PDF 翻譯目標語言，僅保留下拉式選單與繁體中文選項。 (已完成)
- [x] **PDF 去除密碼與進度視窗內嵌輸入框優化 [v3.3.1]**:
    - **內嵌式密碼輸入**: 實作於進度視窗內繪製與管理 Win32 Edit 控制項，實現無閃爍且支援 Enter 鍵送出、Esc 鍵取消的內嵌密碼輸入。 (已完成)
    - **加密狀態預檢**: 自動判定 PDF 是否有加密，無加密檔案直接顯示提示不進行重複要求。 (已完成)



## 2. 核心功能擴張 (Advanced Features)
- [ ] **文件處理工具**:
    - [x] **Word 轉 PDF (Word to PDF)**: 已完成實作。
    - [x] **PDF 去除密碼 (Remove PDF Password) [v3.3.0]**:
        - 支援右鍵選單一鍵去除 PDF 密碼保護。 (已完成)
    - [x] **Excel 轉 PDF (Excel to PDF)**: 整合微軟 Excel COM 與 LibreOffice 雙引擎轉檔支援。 (已完成)
    - [x] **LibreOffice 離線 Office 轉檔引擎 (LibreOffice Offline Office Engine) [v3.5.0]**:
        - 支援 Auto / Microsoft Office / LibreOffice 三種 Office 轉檔引擎模式，讓未安裝 Microsoft Office 的使用者可透過本機 LibreOffice 進行 Word、Excel 與 PowerPoint 轉 PDF。
        - 內建 LibreOffice 下載 manifest、官方 MSI 下載、SHA256 驗證、背景安裝/移除、版本比對與重啟需求狀態處理。
        - 轉檔頁改為依 Office、PDF、圖片三組呈現九個主要功能，降低使用者尋找功能時的掃描成本。
    - [x] **PDF 壓縮與最佳化 (PDF Shrinking & Compression) [v3.6.0]**:
        - 實作以內建 PDFsharp 與 GDI+ 為基礎的優化引擎，支援多級壓縮設定（極小、小檔、標準、高品質），自動精簡文字流、字型去重、大字型剝離與圖片高品質雙立方降解析，並在設定頁面實作 4 停靠點的橫向拉條 UI 與 Toggles。
    - [x] **PDF 分割 (PDF Split)**:
        - 支援依頁碼範圍（如 `1-5`, `8`）或全頁 (`all` / `each`) 將 PDF 拆分為獨立檔案，整合右鍵選單、原生 GDI+ 頁碼輸入框與 CLI 指令。
    - [ ] **PDF 進階極限壓縮 (Advanced PDF Deep Compression)**:
        - **階段一：結構可達性垃圾回收 (DFS GC)**：實作 Catalog 物件樹遍歷，徹底清理編輯殘留的孤立無用物件（Orphan Objects）；優化字型剝離機制，移除字型時保留度量屬性 (Font Metrics) 以防閱讀器渲染跑版。
        - **階段二：二進位物件壓縮流 (Object Streams) [PDF 1.5+]**：引入物件壓縮流 (`ObjStm`) 與交叉引用流 (Cross-Reference Streams)，將大量散落的明文 Dictionary 與 Array 物件打包進行整體 `/FlateDecode` 壓縮。
        - **階段三：影像進階編碼與 Zopfli 無損重壓縮**：針對 1-bit 黑白掃描文件引入 `/JBIG2Decode` 壓縮（可縮減至 1/10 體積且無 JPEG 雜訊）；針對無損 `/FlateDecode` 圖片使用 Zopfli 或 7-Zip Deflate 進行背景極限二次無損精簡。
        - **階段四：字型子集跨頁面合併 (Font Subset Merging)**：解析 OpenType/TrueType 子集二進位，合併同名但字元不全的字型子集，徹底解決多個 PDF 合併後字型資源重複累積、檔案體積異常膨脹的痛點。
    - [ ] **PDF 轉圖片 (PDF to Image)**: 一鍵將 PDF 頁面匯出為高品質 JPG/PNG/TIFF，支援自訂 DPI 渲染率、色彩模式與透明背景處理。
    - [ ] **PDF 轉 PPTX (PDF to PPTX)**: 並存/整合三種不同定位之模式，供使用者自選或依 PDF 類型自動推薦：
        - **模式一：原樣保真 (Mode 1: Layout Preservation)**：將每頁 PDF 渲染為圖片並嵌入 PPTX。成功率高、相容性最高，且通常可避免版面跑位，但文字不可編輯；仍可能因 PDF 加密、檔案損毀、不支援字型或渲染失敗等情況而無法完成轉換（參考 `pdf2pptx`）。
        - **模式二：可編輯優先 (Mode 2: Text Reconstruction)**：解析 PDF 文字框、圖片與版面結構重建為 PPTX 各元素，支援 OCR 與字型自訂（參考 `pdf2slides`）。在此模式下，引入 `opendataloader` 的空間網格定位，精確記錄文字大小、色彩與位置以還原排版。
        - **模式三：AI 簡報修復 (Mode 3: AI Slide Repair)**：利用 Gemini AI 抹除頁面中的文字並修補背景，再藉由原始文字座標疊加可編輯文字層，使背景與文字完全分離（參考 `NBLM2PPTX`，即 *NotebookLM to PPTX* 的內部原型／概念驗證工具）。
    - [ ] **文件去識別化與隱私防護 (Document De-identification & Redaction)**:
        - 借鑑 `jt-doc-tools` 的隱私防護與實體擦除設計。支援在本地透過正則表達式配合校驗碼算法（中華民國身分證、統一編號、居留證、信用卡 Luhn 碼等）與 PDF 坐標定位，自動搜尋 PDF 內的敏感資料。
        - 提供「遮蔽（Redact，以黑條塗黑並物理擦除底層字元串流）」與「遮罩（Mask，以同大小/字型大小/色彩的 * 號覆蓋重建）」雙重模式，確保敏感資料無法透過選取或複製還原。
        - 提供選用的本地/雲端 AI（Gemini / Ollama）語意檢核，補強 Regex 無法辨識的人名、職稱與特定合約代號。
        - **隱私安全設計**：敏感資料處理與主流程完全解耦，並在記憶體層級實作 Zeroing memory（安全清零），確保身分證字號等高度敏感資料在轉換完成後不留存於記憶體。
    - [ ] **智能多圖與掃描件拼合 (Intelligent Scanner Stitching)**:
        - 借鑑 `jt-doc-tools` 的證件/掃描拼合算法，支援拉入多個掃描影像，自動偵測有內容的區塊（連通域 BFS 與 Y/X 軸對齊合併），進行自適應裁剪。
        - 實作「背景淨白」功能（僅針對亮度高且飽和度低之中性灰底色進行提亮與漂白，過濾折痕與掃描陰影；完整保留彩色印章、彩色照片及印刷色），並將裁剪出的正反面證件（身分證、健保卡）或數張單據發票一鍵拼合成單張 A4 頁面。
- [ ] **進階 PDF 學術與 AI 工具 (Advanced PDF Academic & AI Utilities)**:
    - [x] **PDF 學術論文翻譯 (PDF Math Translate) [v3.2.0]**: 純 C# 原生實現（相容 Native AOT）。自動識別與保護 LaTeX 公式，目前已實作免金鑰 Google 翻譯引擎，具備併發控制與速率限制。
    - [x] **PDF 佈局修復、字型解析 TTC 映射與高併發翻譯優化 [v3.2.1]**: 解決中文字型與數學字型 TTC 閃退，實作 table 頁面動態橫向合併門檻維持表格列對齊，並重構為 Google Mobile 批次翻譯。
    - [ ] **PDF 雙語對照翻譯模式 (Bilingual/Dual-language Translation)**:
        - 參考 `PDFMathTranslate` 的雙語對照實現，支援在輸出文件保留原文與譯文對照，提供更靈活的閱讀體驗；並擴充公式保護層（利用特殊字型如 Symbol / Cambria Math 自動辨識）。
    - [ ] **PDF 結構化數據與混合解析 (PDF to Markdown/JSON with Hybrid Parser)**:
        - **雙欄/多欄排版與 XY-Cut++ 閱讀順序**: 借鑑 `OpenDataLoader` 的雙欄與多欄解析算法，在本地 C# 解析中實現 XY-Cut 投影分割，確保學術論文與複雜文件在提取為 Markdown 時具備正確的閱讀順序。
        - **混合解析模式 (Hybrid Local+AI Mode)**: 簡單或純文字頁面直接透過本地快速解析器（如 PDFium / PdfPig）處理，複雜表格、公式與圖表則路由至 Gemini API 等 Vision 端，實現 borderless 表格 HTML 重建與 AI 圖片描述。
        - **解耦設計**：於核心（`src/Clickra.Core`）設計 `IDocParserStrategy` 等解析策略介面（Strategy Pattern），使不同解析引擎（PDFium、AI Vision、PdfPig）能夠靈活切換與擴充。
- [ ] **圖片處理強化**:
    - [ ] **PNG/JPG 批量轉換**: 支援多種常用格式間的快速互轉。
    - [ ] **高品質縮圖**: 支援批量調整圖片尺寸並保留細節。
- [ ] **批次檔名與資料夾工具**:
    - [ ] **資料夾與檔案批次命名**: 支援自訂數字規則、提取建立日期、固定字串與自動編號。可以直接在資料夾右鍵選單對「整個資料夾及其內含檔案」進行操作。
    - [ ] **批次檔案分類**: 支援依副檔名、日期區間或檔名關鍵字，將檔案自動分類並移入對應的資料夾。
    - [ ] **批量建立空資料夾**: 支援依指定命名規則與結構要求，一次建立多個指定結構的空資料夾。
- [ ] **文字與編碼工具**:
    - [ ] **文字編碼與簡繁轉換**: 支援 Big5/GBK/UTF-8 互轉與原生 `LCMapStringEx` 簡繁字元互轉。
- [ ] **右鍵選單功能擴充 (Context Menu Enhancements)**:
    - [ ] **資料夾右鍵直接轉換**: 支援直接在資料夾右鍵選單操作，一鍵將資料夾底下的所有支援檔案進行轉換（如 Word 批次轉 PDF、圖片轉 PDF 等），無須進入資料夾手動選取。

## 3. 專案架構與規範 (Refactoring)

> [!IMPORTANT] 品質問題追蹤流程
> 當品質/效能問題在 PR 中被發現（靜態分析、圈複雜度過高等）但本次**不重構**時，必須立即將問題記錄到本節技術債清單並隨 PR 提交，提醒下次開分支時優先修正；修正完成後在項目旁標記日期。

- [x] **模組化拆分 (Done)**:
    - 已完成 `src/Clickra.UI` 與 `src/Clickra.Core` 的解耦與 AOT 轉型。
- [ ] **檔案命名整理**:
    - 統一整理專案內的檔案命名規範，消除歷史遺留的不一致命名。
- [ ] **降低 CLI 與 GUI 視窗事件的圈複雜度 (Complexity Reduction) [技術債]**:
    - **視窗訊息路由器 (WndProc Router)**：重構 `DashboardWindow.Events.cs` 的 `WndProc` (當前複雜度 137)，將龐大的 `switch` 拆分為單純的訊息路由，將特定 Win32 訊息指派至專屬的事件方法中處理。
    - **命令模式拆分 (Command Pattern)**：重構 `DashboardWindow.Events.Click.cs` 的 `HandleLButtonDown` (當前複雜度 130)，將點擊區域偵測與具體功能執行解耦，使每個轉檔功能封裝為獨立的 Command 物件。
    - **CLI 入口點精簡**：重構 `ClickraCli.cs` 的 `Main` (當前複雜度 89)，將參數解析與 Dashboard 啟動移至獨立的啟動類別。
    - **進度視窗複雜度 (2026/08/08 記錄)**：重構 `ProgressWindow` 系列四個高複雜度方法——`Controls.cs` 的 `InstanceWndProc` (156, critical)、`Process.cs` 的 `RunProcessing` (57, critical)、`Paint.cs` 的 `Paint` (47, very-high)、`VisualSplitter.cs` 的 `PaintVisualSplitter` (35, very-high)，將訊息路由與繪圖拆分為職責單一的方法。
    - **SonarCloud 認知複雜度 (2026/08/09 記錄，同日完成重構)**：SonarCloud 標記 8 個超過認知複雜度門檻 15 的方法已全部重構——`PaintVisualSplitter` (60) 拆成 8 個職責單一的方法（mode bar / n-selector / body / cards / preview panel / preview page / buttons / zoom overlay）、`ProcessSingleFile` (28) 抽 `SplitEachPage`/`ExtractSegments`/`ExtractSingleRange`、`DrawPageImages` (17) 抽 `TryDecodeEmbeddedImage`、`DrawPageWords` (16) 抽 `ResolveWordColor`/`TryDrawWord`、`ApplyVisualSplitMode` (16) 每模式抽方法、`HandleVersionOrDeploy` (17) 抽 `TryHandleVisualSplitterArgs`、`DispatchPdfCommand` (19) 抽 `DispatchPdfCase`/`HandleSplitPdfQuiet`、`ShowInstance` (16) 抽 `RunMessageLoop`。全量重建 0 警告 0 錯誤，分割測試全 PASS。
    - **已移除 (2026/08/09)**：`RenderSyntheticPageThumbnail`（無呼叫端的 dead code，複雜度 17、未用參數、非 static）已直接刪除，消除 SonarCloud S1172/S2325/S3776 三項。
- [ ] **測試套件架構標準化與命名空間升級 [技術債]**:
    - [x] **命名空間整合 (2026/08/08 完成)**：10 個 `TestSuite` partial 檔已移入 `Clickra.Core.Tests` 命名空間並移除 S3903 pragma，解決全域命名空間污染（CS-W1061）。
    - **升級測試框架**：後續規劃將自建的 `TestRunner` 升級為業界標準的單元測試框架（如 xUnit 或 NUnit），以利於在 CI 流程中整合覆蓋率分析。
- [ ] **品質閘門誤報與基線觀察 (2026/08/08 記錄, Static Analysis Triage)**：
    - **CS-R1137 readonly 誤報 ×3**：`_isPromptingVisualSplitter`（volatile 欄位不得宣告 readonly）、`_visualSplitZoomDragLastX/Y`（拖曳期間持續變動）——DeepSource 誤判，需在儀表板標 ignore，不得為此改程式碼。
    - **SonarCloud S8970 null-forgiving ×10 已全數修正 (2026/08/09)**：`VisualSplitter.cs` 的 `_tipFont!`/`_msgFont ?? _tipFont!` 並非無法處理——它們是缺 null guard 的症狀。正確修法：n-selector 用 `Font? uiFont = _msgFont ?? _tipFont; if (uiFont == null) return;`、zoom overlay 用 `tipFont`/`uiFont` 兩個非 null local + 前置 guard。全量重建 0 警告 0 錯誤，`!` 在該檔歸零。教訓：遇到「直接刪除會報編譯警告」的 analyzer finding，正確做法是重構出可證明的非 null 路徑，而非保留運算子並標記誤判。
    - **Documentation Coverage 基線移動觀察**：DeepSource 的覆蓋率參考值會隨變更集同步移動（三次 run：0.4→9.7、3.1→12.4、10.7→20，差值恆為 9.3），單靠補文件追不上；新程式碼仍應持續補 XML 文件，閘門門檻需在儀表板設定合理值。
- [ ] **開發期測試後門與倉庫整潔清理 (Dev Scaffolding Cleanup)**：
    - [x] **移除視覺分割測試後門**：移除 `ClickraCli.cs` 中硬編碼的測試 PDF 路徑與依執行檔名稱（`TestVisualSplitter` / `ClickraVisualSplitter`）自動進入視覺分割模式的開發測試邏輯，正式版本應僅由 CLI 旗標與參數驅動。
    - [ ] **本機工具狀態隔離**：將 `.freebuff/`（本機工具 SQLite 狀態）加入 `.gitignore`，避免污染 git status 與誤提交。

## 4. 維護、診斷與離線轉檔插件 (Diagnostics & Offline Fallback)
- [x] **一鍵診斷回報與郵件反饋 (One-click Diagnostic Feedback) [v3.0.9]**:
    - 提供本地 Native AOT 診斷日誌記錄。於 Dashboard 實作一鍵郵件回報功能，自動打包診斷日誌與系統資訊，透過 Gmail 網頁開啟預設撰寫畫面（並支援標準 mailto: 協議連結作為系統預設郵件用戶端之備用方案）。
- [ ] **多語系一鍵診斷回報信件草稿 (Multi-language Support for Diagnostic Feedback Mail)**:
    - 依據使用者目前的介面語言，自動撰寫對應語言的 Gmail/mailto 郵件草稿內容與主旨。
- [x] **本地免 Office 離線轉檔引擎 (Local LibreOffice Fallback Engine) [v3.5.0]**:
    - 已改採 LibreOffice 官方 MSI 作為免費備援引擎來源，並由 Clickra 以內建 manifest 管理下載 URL、版本與 SHA256，避免使用者自行尋找安裝檔或手動指定 `soffice.exe`。
    - 已加入 Auto / Microsoft Office / LibreOffice 引擎選擇、安裝狀態提示、版本相同時避免重複重裝、以及解除安裝後的 pending restart 狀態顯示。
- [ ] **LibreOffice 遠端 manifest 與宣傳頁整合 (Remote Manifest & Website Integration)**:
    - 在 Clickra 宣傳頁與 GitHub Pages 架構完成後，將 LibreOffice manifest 從純內建資料擴充為可更新的遠端 manifest，讓新版本 LibreOffice 發佈時可不必等待 Clickra 主程式更新。
    - 增加 manifest 簽章或 checksum metadata 防護，確保遠端資料來源可驗證且可回退到內建 manifest。
- [ ] **LibreOffice 安裝維護體驗強化 (LibreOffice Setup Maintenance UX)**:
    - 補強下載中斷續傳、代理/企業網路提示、安裝取消復原、以及 Windows Installer pending restart 場景的更細緻狀態說明。
- [ ] **Office 轉檔引擎抽象化與整合測試 (Office Engine Abstraction & Integration Tests)**:
    - 將 `PowerShellHelper.ExportOfficeToPdf` 目前直接讀取設定、呼叫 Microsoft Office COM 與 LibreOffice process 的流程抽出可測試的 engine resolver/strategy。
    - 補上 Auto / Microsoft Office / LibreOffice 模式、Microsoft Office 失敗後 LibreOffice fallback、無可用引擎、pending restart 與使用者指定引擎等整合測試，避免只能靠實機手測驗證。

## 5. PDF 無障礙化與自動標籤 (PDF Accessibility & Auto-Tagging)
- [ ] **PDF 無障礙結構稽核與 Well-Tagged PDF (WTPDF) 重建**:
    - 借鑑 `OpenDataLoader` 與 PDF Association 倡導之規範，對輸入 PDF 進行無障礙 Tag 稽核，判定是否已標記結構樹以供讀屏軟體正常閱讀。
    - 對無標籤的 PDF，實現 100% 本地或 AI 輔助的自動標籤化 (Auto-Tagging)，重建 Headings、List、Table 結構樹，並輸出符合 WTPDF 標準的無障礙 Tagged PDF，協助解決全球無障礙法規合規需求（如 EAA、ADA 508）。
    - **WTPDF 合規與驗證**：遵循 PDF Association 的 WTPDF 規範，並於核心增加結構驗證模組，提供初步結構合規性稽核報告。

---

## 🏁 近期已達成項目 (Recently Accomplished)
- [x] **v3.6.4.0 PDF 翻譯可靠性與版面穩定化** (2026/07/22)：加入有界限的純 .NET provider fallback、異常輸出品質 guard、暫存檔原子發布及 health gate；保存標題階層、字級、對齊、粗體、合併／窄欄表格、圖說與內外部連結，並以 ASTER 的摘要、Table III 及來源對譯文逐欄渲染占用比較作為發布回歸門檻。
- [x] **v3.6.2.0 SSL/TLS 憑證驗證安全加強** (2026/07/05)：修復了 MyMemory 翻譯 API 連線中繞過憑證驗證的安全漏洞。移除了非安全的 `RemoteCertificateValidationCallback` 委派以重啟系統預設證書校驗防範 MITM，並使連線協議相容 TLS 1.2 與 TLS 1.3。
- [x] **v3.6.1.0 PDF 翻譯合字越界崩潰修正** (2026/07/05)：修正了學術論文 PDF 翻譯管線中，當公式出現合字（如 "fi" 等在 PdfPig 中為單個 letters 物件但有多字元 Value）時，因錯誤使用拼接後的字元長度作為 `formula.Letters` 陣列索引導致的 `IndexOutOfRangeException` 崩潰問題。改為直接基於 `formula.Letters.Count` 進行子序列元素比對，並以 `2602.08146v2.pdf` 驗證修復。
- [x] **v3.5.0.0 LibreOffice 離線 Office 轉檔引擎** (2026/06/29)：新增 Auto / Microsoft Office / LibreOffice 引擎選擇，內建 LibreOffice 官方 MSI manifest、SHA256 驗證、背景安裝/移除與版本比對；未安裝 Microsoft Office 時可透過 LibreOffice 在本機完成 Word、Excel、PowerPoint 轉 PDF；並將轉檔頁九個功能依 Office、PDF、圖片分組，提升尋找效率。
- [x] **v3.4.0.0 Excel 轉 PDF 功能** (2026/06/21)：新增右鍵選單 Excel 轉 PDF 功能，整合 Shell Extension 在地化選單、Dashboard 轉檔卡片與拖放自動偵測、CLI `excel2pdf` 指令及 Overview 頁 Excel 引擎狀態顯示。
- [x] **v3.3.3.0 PDF 翻譯管線模組化重構與佈局分析增強** (2026/06/21)：將 PDF 翻譯核心引擎拆解為 80+ 個獨立模組（段落、表格、圖表、灰階提示、標註、渲染、翻譯），增強表格偵測、圖表避讓與段落分類邏輯，新增簡繁中文轉換器、翻譯規則文件與 PDF 診斷工具集，完善批次翻譯進度顯示與輸出路徑自訂功能。
- [x] **v3.0.5.0 商店發布與合規性通過** (2026/05/13)：成功通過微軟商店認證與合規審核。
- [x] **v3.0.6.0 儀表板與 AOT 轉型** (2026/05/15)：完成 Native UI 儀表板，支援 Office 偵測。
- [x] **v3.0.6.0 專案目錄重整與模組化架構** (2026/05/15)：完成核心與 CLI 的解耦與 AOT 轉型。
- [x] **v3.0.7.0 動態進度條與完成通知** (2026/05/17)：完成純 Win32 進度視窗與原生 Toast 通知整合。
- [x] **v3.0.7.0 選單動態過濾優化** (2026/05/19)：解決多檔案選單動態顯示與空選單過濾。
- [x] **v3.0.8.0 轉換歷史紀錄、快速轉檔與語系** (2026/05/21)：於儀表板整合本地轉換歷史紀錄 (Conversion History)、快速轉檔分頁與多國語系切換 (zh-TW/en-US)，並完成 DashboardForm 的 partial 程式碼重構。
- [x] **v3.0.9.0 關於分頁、完整多語系擴充與儀表板細項優化** (2026/05/26)：新增關於分頁（含一鍵 Gmail 診斷）；儀表板與右鍵選單完整支援 ja-JP、ko-KR、zh-CN；新增自訂輸出路徑、歷史紀錄展開詳情（計時、輸入輸出路徑）；支援最大化自適應佈局與高 DPI 模糊修正；優化快速轉檔為「檔案優先」互動流程；動態側邊欄寬度、首次啟動語言字型正規化，以及 NativeAOT 相容性修正。
- [x] **v3.1.0.0 視窗行為、系統匣最佳化、歷史紀錄細緻化與進度滾動支援** (2026/05/30)：實作主 Dashboard 單一實例 (Mutex) 檢查與還原、進度視窗支援最小化至系統匣、進度視窗取消/關閉確認防呆與關聯行程中止、轉換歷史細緻化拆分（一檔一行並顯示個別檔名）、單個 PDF 時隱藏 Clickra 右鍵選單，並支援進度訊息水平滾輪與直接滑鼠拖曳滑動條。
- [x] **v3.2.1.0 PDF 佈局修復、字型解析 TTC 映射與高併發翻譯優化** (2026/06/12)：解決中文字型與數學字型 TTC 閃退，實作 table 頁面動態橫向合併門檻維持表格列對齊，修復雙欄排版橫向合併錯亂與第一頁作者資訊繞過翻譯，並重構為 Google Mobile API 支援 batch 翻譯、併發 semaphore 控制與隨機延遲等防 Ban 限流機制。
- [x] **v3.2.0.0 儀表板歷史排版優化、選單目標語言簡化與錯誤判定邏輯修正** (2026/05/31)：實作歷史紀錄排版自適應與檔名寬度自適應、歷史紀錄欄錯誤狀態「錯誤/取消」精準化判定、移除多餘 PDF 翻譯目標語言，並優化設定介面文字排版消除重疊。
- [x] **v3.3.0.0 PDF 去除密碼與進度視窗內嵌密碼輸入功能** (2026/06/17)：新增右鍵選單 PDF 去除密碼功能；於進度視窗中整合無閃爍的原生內嵌 Edit 密碼輸入框及 OK/Cancel 控制項，修復視窗繪製與輸入衝突，並支援 Enter 與 Esc 熱鍵操作；提供加密狀態預檢機制以防止誤導提示。

## 🚀 後續預計里程碑 (Upcoming Milestones)
- **第一階段**：收尾「LibreOffice 離線引擎 QA 與發佈準備」（Windows 10/11 無 Office、已有 Microsoft Office、已有 LibreOffice、下載網站連線失敗、解除安裝 pending restart 等情境測試；完成商店文案、版本文件與 release artifact 驗證；後續抽出 Office engine resolver/strategy 並補齊引擎選擇與 fallback 整合測試）。
- **第二階段**：開發「文字與編碼工具」（包含編碼轉換與原生 `LCMapStringEx` 簡繁字元互轉）。
- **第三階段**：開發「批次檔名、資料夾與圖片處理工具」（支援資料夾批次命名、分類、批量建立空資料夾，以及基於 Ghostscript/PDFium 技術的 PNG/JPG 批量轉換與高品質縮圖，並支援資料夾右鍵直接轉換功能）。
- **第四階段**：開發「PDF 壓縮與轉 PPTX 工具強化」：
    - PDF 壓縮：引進 Ghostscript (pdfwrite) 的多級降採樣與壓縮引擎。
    - PDF 轉 PPTX：支援原樣保真、基於空間網格重建可編輯文字之模式，以及基於 Gemini AI 抹除修補之模式。
- **第五階段**：開發「進階 PDF 學術與 AI 工具」（引進 XY-Cut++ 閱讀順序分析重建多欄排版 Markdown，並支援本地與 AI 混合解析模式：本地快速解析搭配 Gemini 輔助表格與圖表描述提取（於 `src/Clickra.Core` 建立明確的解析策略介面 Strategy Pattern 以便解耦和擴充解析引擎）；另擴充 PDF 雙語對照翻譯與公式特殊字型防護）。
- **第六階段**：開發「PDF 無障礙化與自動標籤」（支援對 PDF 結構進行無障礙稽核，並結合版面分析自動為未標記的 PDF 重建結構樹樹狀圖，生成符合 Well-Tagged PDF 標準之無障礙 Tagged PDF（遵循 PDF Association 的 WTPDF 規範，並於核心增加結構稽核與驗證模組））。
- **第七階段**：開發「隱私去識別化與智能掃描拼合工具」：
    - 去識別化：實作純本地正則加校驗碼的 PII 自動識別、PDF 實體物理抹除 (Redaction) 及同格式/顏色/位置遮罩重寫 (Mask) 引擎，配合本地/雲端 AI 做語意漏抓檢索（將敏感資料處理邏輯與核心處理流程解耦，並實作零記憶體殘留 Zeroing Memory 機制以確保高度敏感的身份證件等資訊安全）。
    - 掃描拼合：實作連通元件 BFS 卡片偵測與 Y/X 軸對齊合併算法，並支援保守型「背景淨白」（提亮紙張灰黃底色、保護印章與彩色內容），提供一鍵證件與收據 A4 合成。
