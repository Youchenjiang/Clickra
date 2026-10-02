# Changelog

## [v3.12.0.0] - 2026-10-03

- **設定平台現代化 (Settings Platform Modernization)**：Core 新增集中式 `SettingDescriptor` / `SettingPageRegistry`，統一定義設定項目的標題、說明、控制項種類、選項與數值範圍，降低 Native Dashboard 與 Fluent Settings 的規格漂移。
- **描述器驅動設定介面 (Descriptor-driven Settings UI)**：Native Dashboard 與 Fluent Settings 會從共用 registry 自動產生支援的設定控制項，新設定不再需要在兩套介面重複維護相同 metadata。
- **跨執行中程序即時同步 (Live Settings Synchronization)**：設定檔變更會安全刷新共用 cache，並透過 `SettingsReloaded` 讓其他正在執行的 Clickra UI 即時更新設定值與相關文字。
- **數值設定邊界一致性 (Numeric Settings Bounds)**：數值與滑桿設定共用 Core 宣告的範圍、預設值與 clamp 規則，並補強步進溢位保護與 UI 邊界 guard。

## [v3.11.1.0] - 2026-10-02

- **進度與分割器介面一致性 (Progress / Splitter UI Parity)**：Visual PDF Splitter 與 Progress Window 補齊五語系文字、邊界行為與 task-progress 對齊，降低 Native / Fluent / Shell 之間的顯示與操作差異。
- **系統匣還原與取消控制 (Tray Restore / Cancel Controls)**：進度視窗最小化到系統匣後，可從右鍵選單直接還原或取消目前轉換，並保留左鍵快速還原行為。
- **向量圖示與無障礙名稱 (Vector Icons / Accessibility)**：進度狀態、分割器導覽與縮放控制改用共用向量圖示，避免字型 glyph 差異，同時為 Fluent icon-only controls 保留在地化 UI Automation 名稱。
- **暫存任務到期可視性 (Parked Retention Visibility)**：Native Dashboard 會顯示暫存任務的剩餘保留時間、即將到期警示、檔案索引與停止原因，狹窄版面也會避免文字覆蓋 retention 標籤。
- **發佈與 Store 驗證可靠性 (Release / Store Reliability)**：發佈簽章流程改為 provider-neutral fail-closed，並新增已發布 Store 套件的公開解析、Microsoft delivery 驗證與 GitHub Release reconciliation 路徑。

## [v3.11.0.0] - 2026-09-29

- **LibreOffice 既有安裝納管 (LibreOffice Adoption)**：Native Dashboard 與 Fluent Settings 現在可將符合條件的既有系統 LibreOffice 明確交由 Clickra 管理，不必重新安裝才能使用 Clickra 的更新與解除安裝流程。
- **可驗證的管理身分 (Verified Management Identity)**：Clickra 會將管理權綁定到唯一可驗證的 MSI ProductCode 與 soffice.exe 系統路徑，只有目前身分仍完全一致時才允許 Clickra 執行解除安裝。
- **模糊安裝狀態 Fail-closed (Ambiguous Install Fail-closed)**：若同時存在 32/64 位元系統安裝、找不到唯一 MSI 身分、路徑不一致或安裝已被替換，Clickra 會保留 LibreOffice 並拒絕取得或使用解除安裝管理權。
- **管理狀態與同意提示 (Management Status & Consent)**：五語系介面新增納管確認、成功、外部安裝與管理身分無法驗證提示，讓使用者清楚知道哪些 LibreOffice 由 Clickra 管理、哪些仍由 Windows 管理。

## [v3.10.0.0] - 2026-09-28

- **警告零容忍建置 (Warning-free Builds)**：產品專案與測試統一啟用 warnings-as-errors，CI 與 pre-push gate 會在發佈前阻擋新的編譯警告。
- **測試產物隔離與清理 (Test Artifact Isolation)**：測試資料改存於使用者私有的 Clickra TestRuns 目錄，並加入可驗證的 stale-artifact 清理流程，避免共用暫存目錄與舊測試產物干擾。
- **暫存任務保留控制 (Parked Task Retention Controls)**：Native Dashboard 新增 0–365 天的暫存任務保留設定、stepper 與常用預設，並與 Fluent 共用設定登錄的預設值與最大值。
- **到期狀態與提醒 (Retention Expiration Alerts)**：Fluent History 會顯示暫存任務剩餘保留時間、即將到期與已到期狀態，並以五語系警示提示使用者及時繼續或取消任務。
- **保留期限一致性 (Retention Boundary Consistency)**：Core、Native Dashboard 與 Fluent 共用相同的最大保留天數與嚴格少於 24 小時的即將到期邊界，避免不同介面產生不一致判定。

## [v3.9.0.0] - 2026-09-28

- **CLI 多語系支援 (CLI Localization)**：命令列介面的說明、狀態與錯誤訊息擴充為繁體中文、簡體中文、英文、日文與韓文，並以共用 Core localization 資源維持一致。
- **診斷郵件在地化 (Diagnostics Email Localization)**：診斷郵件的主旨與內容會依目前語系產生對應文字，降低跨語系支援時的理解成本。
- **系統匣與視覺分割器在地化 (Tray / Visual Splitter Localization)**：系統匣提示、還原操作與 Visual PDF Splitter 的按鈕、區段、頁數與預覽標籤全面改用共用翻譯資源。
- **翻譯完整性檢查 (Translation Gap Diagnostics)**：新增缺漏翻譯偵測、分語系報告與完整字典 parity 測試，避免新增功能只補到部分語言。
- **發佈可靠性 (Release Reliability)**：GitHub Release 建立流程與 public PR governance 進一步收斂，降低 release target 與公開分支歷史被誤操作的風險。

## [v3.8.1.0] - 2026-09-27

- **LibreOffice 安裝所有權保護 (LibreOffice Ownership Safety)**：Clickra 只會移除自己安裝與管理的 LibreOffice；偵測到使用者自行安裝的 LibreOffice 時會保留原安裝並顯示對應提示。
- **設定登錄集中化 (Settings Registry Consolidation)**：Core、原生介面與 Fluent 介面統一使用共享設定鍵、預設值與 ownership helpers，降低不同介面之間的設定漂移。
- **PDF 壓縮三段預設 (PDF Compression Presets)**：PDF 壓縮滑桿統一為 Small、Balanced、High Quality 三個明確位置；舊版儲存的第 4 段 High 設定會自動保留為新的 High 預設，不會降級成 Balanced。
- **設定檔清理 (Settings Cleanup)**：載入設定時會移除已退役的 PDF 壓縮鍵，同時保留所有有效設定，避免舊設定長期殘留造成行為不一致。
- **發佈與維護可靠性 (Release / Maintenance Reliability)**：CI、安全掃描、Store submission verification 與文件權威邊界進一步收斂，降低發布與後續維護時的狀態誤判。

## [v3.8.0.0] - 2026-09-26

- **圖片格式轉換 (Image Format Conversion)**：新增 `img-to-png`、`img-to-jpg`、`img-to-webp`、`img-to-gif` 與 `img-to-heic`，並透過 shared Core registry/runner 同步提供給 legacy CLI、Native dashboard/progress 與 Fluent UI。
- **內建 WebP runtime (Bundled WebP Runtime)**：WebP 編碼與解碼改由隨應用程式封裝的 Imazen.WebP/libwebp 提供，不再依賴 Microsoft Store 的 WebP codec。
- **HEIC/HEIF fail-closed 檢查 (HEIC/HEIF Safety)**：系統缺少必要的 HEIF/HEIC decoder 或 encoder 時會顯示在地化錯誤並停止；`.heic`、`.heif`、`.hif` 輸入皆套用一致的 preflight。
- **輸出路徑安全 (Output Safety)**：轉換前拒絕同格式輸入、不支援的目標格式與重複輸出碰撞，並讓 CLI / Native flow 正確處理明確指定的輸出資料夾與失敗 exit code。

## [v3.7.3.0] - 2026-09-25

- **Headless CLI 錯誤碼 (Headless CLI Exit Codes)**：命令派送拋出例外時會將 process exit code 設為 `1`，讓 scripts、CI 與排程工作可靠辨識轉換失敗；成功命令（例如 `--version`）仍維持 exit code `0`。

## [v3.7.2.0] - 2026-09-25

- **Office 引擎可靠性 (Office Engine Reliability)**：Office readiness 改以 `CLSIDFromProgID` 檢查 COM 註冊，避免 NativeAOT 發布環境依賴 reflection-only API，並保留 Word / Excel / PowerPoint 的自動引擎選擇。
- **取消與 fallback 安全 (Cancellation / Fallback Safety)**：使用者取消 Microsoft Office 轉換後不再啟動 LibreOffice 第二次嘗試；明確指定 Microsoft engine 時也會直接回報原始失敗。

## [v3.7.1.0] - 2026-09-25

- **破壞性操作安全 (Destructive-operation Safety)**：覆寫既有輸出前若 recovery backup 建立失敗就立即停止；rollback 或 backup cleanup 失敗會明確回報並保留 recovery artifact，避免無保護覆寫或靜默遺失復原資料。
- **LibreOffice 卸載保護 (LibreOffice Uninstall Safety)**：只允許 Clickra 移除自己管理的 LibreOffice 安裝，不再誤刪使用者自行安裝的 Office engine。
- **發佈流程可靠性 (Release Pipeline Reliability)**：版本同步保留每個檔案原有的 UTF-8 BOM 狀態；Store publisher 遇到既存 pending submission 時改為 fail closed，不再自動刪除前一版 submission。

All notable changes to Clickra will be documented in this file.

## [Unreleased]

### Added

- HEIC 輸入與 HEIC 輸出支援：圖片轉換命令現已接受 HEIC 輸入，並新增 `img-to-heic` 命令將 PNG/JPG/WEBP/GIF/HEIC 轉出為 HEIC（輸出依賴系統 HEIF/HEIC 編碼器）。
- 編碼器預檢：`img-to-heic`／`img-to-webp`／`img-compress`（WebP/HEIC 輸入）在轉檔前先檢查系統編碼器；缺少免費的 Windows「HEIF 影像延伸」或「WebP 影像延伸」時，以本地化訊息提示並提供一鍵開啟 Microsoft Store 安裝，不再於轉檔中途直接失敗。
- 圖片壓縮（`img-compress`）：全新獨立的圖片壓縮功能，支援 0–3 品質等級（比照 PDF 壓縮滑桿）與最大長邊尺寸（原始／4K／FHD／HD），可與改尺寸一併使用；輸出至 `<名稱>_compressed.<原副檔名>`，壓縮後反而變大時自動略過以保護來源。PNG 若不超過 256 色會以無損的索引式調色盤重新編碼（截圖與圖表通常可省 40% 以上）。Fluent 與原生儀表板設定頁皆可調整。

### Changed

- 同格式轉換排除：選取的檔案已是目標格式時（如選 PNG 時的「轉成 PNG」），對應的 `img-to-*` 命令會自動停用或隱藏，jpg/jpeg 視為同格式；Fluent、原生儀表板、右鍵選單與 CLI 一體適用。
- 修正右鍵選單 img-to-webp／img-to-heic／img-to-gif 顯示錯誤標籤的問題，並新增 `img-compress` 右鍵項目與圖示。

<!-- Remainder of changelog is preserved unchanged below -->

## [v3.7.0.0] - 2026-09-02

- **PDF Translation Hyphenation**：技術術語跨行連字自動重組（如 Cop-peliaSim → CoppeliaSim），並調整 CJK 字體縮放比例以提升可讀性。
- **右鍵選單圖示 (Context Menu Icons)**：所有 Shell 轉檔指令現在在 Windows 11 及傳統右鍵選單中顯示在地化圖示。
- **每任務檔案佇列 (Per-task File Queue)**：取代單一 active.tmp，改為個別任務進度檔案；新增歷史記錄、任務暫停/恢復與過期任務清理，提升多工作業可靠性。
- **卸載安全機制 (Uninstall Safety)**：終止 COM 代理程序前先驗證模組路徑，避免誤殺無關程序。
- **WinUI 3 Fluent Dashboard**：全新 WinUI 3 介面，包含設定、歷史、轉檔與關於頁面；以可選 MSIX 套件形式提供（Store 可選套件權限待審核中）。
- 修復 PDF 解密失敗時出現重複彈窗的問題。
- 限定 Store 發佈步驟僅在版本標籤推送時觸發。

## [v3.6.5.0] - 2026-08-08

- **視覺化 PDF 分割標記 (Visual PDF Splitter)**：於進度視窗新增視覺化分割介面，支援自訂分段、全拆單頁與固定頁數三種模式，提供頁面縮圖預覽與放大檢視，並以分號分隔的多區段規格一次輸出多個檔案。
- **主視窗「分割 PDF」按鈕 (Split PDF Button)**：轉檔頁「PDF 工具」群組新增「分割 PDF」按鈕，可直接從主視窗呼叫分割功能，並一併修復圖片合併／圖片拼接按鈕無法點選的問題。
- **「切開」功能 (Split at Current Page)**：分割視窗的頁面導覽列新增「切開」按鈕，可在目前預覽的頁面將選中分段直接切成兩段，方便快速拆出單一頁面。
- **PDF 翻譯穩定性修復 (PDF Translation Stability)**：修正 PDF 翻譯流程中版面溢出計算與流程式正文旗標處理的問題，並重構翻譯測試註冊結構以提升可維護性。
- 修復視覺分割器首次開啟時版面殘留舊畫面、單頁分段顯示為 `P.28-28`、視窗未放大導致底部按鈕被裁切，以及密碼輸入控制項重疊於分割介面的問題。

## [v3.6.4.0] - 2026-07-22

- **PDF 翻譯可靠性 (PDF Translation Reliability)**：限制文件、provider 與 fallback 的 deadline 和重試範圍；以純 .NET MyMemory 請求、批次拆分及 provider fallback 復原可恢復的失敗。未翻譯原文、破損粗體標記、重複片語與異常英文殘留均會觸發 fallback，且只在翻譯與 health gate 全部通過後原子發布輸出。
- **來源排版與文字結構保存 (Source Layout and Text Structure Preservation)**：保存標題階層、來源字級、對齊錨點、續行、混合／整段粗體、旋轉文字及圖說標記；新增同欄 layout planning、CJK reflow 與固定區邊界平衡，避免段尾縮字、標題漂移、裁切、底部溢位及異常欄位空白。
- **固定內容與繪圖保護 (Protected Content and Drawing Preservation)**：保護圖表、合併／窄欄表格、程式碼、公式、灰色 prompt、作者資訊與參考文獻等 bypass 區域，並重建向量標記、邊框、遮罩及 overlay，避免翻譯覆蓋、Table III 列遺失或原始圖形受損。
- **PDF 連結保存 (PDF Link Preservation)**：依 annotation occurrence 重建內部引用與外部超連結，避免重複文字造成錯誤配對或遺失連結。
- **診斷與回歸門檻 (Diagnostics and Regression Gates)**：擴充 PDF layout health report，加入來源對譯文的逐頁逐欄渲染占用比較，並以 ASTER 標題、摘要、Table III、圖說、受保護區域、連結、provider fallback 及輸出品質檢查作為 deterministic regression gates。

## [v3.6.3.0] - 2026-07-09

- **CI/CD 自動化發布與多國語言支援 (CI/CD Release Automation & Multi-language Support)**：
  1. 補全並整合繁體中文、英文、日語、韓語、簡體中文 5 國語言的原生 MSIX 套件資源封裝。
  2. 新增 GitHub Actions 提交規範檢查（Conventional Commits 驗證）。
  3. 將 Microsoft Store 上架流程整合至 GitHub Actions CI/CD 自動化發布管線。

## [v3.6.2.0] - 2026-07-05

- **SSL/TLS 憑證校驗安全加強 (SSL/TLS Certificate Verification)**：修復了 `MyMemoryTranslator` 的 `HttpClient` 中繞過 SSL/TLS 憑證驗證的安全漏洞。移除了非安全的 `RemoteCertificateValidationCallback`，啟用預設的系統安全證書驗證以防範中間人 (MITM) 攻擊，並將支援的連線協議擴充為 `Tls12` 與 `Tls13`。

## [v3.6.1.0] - 2026-07-05

- **PDF 翻譯崩潰修復 (PDF Translation Crash Fix)**：修正了 `PdfBypassedParagraphRenderer` 中在處理具有多字元配對（如 PDF 字元合字 ligatures「fi」等）的數學公式字元序列時，因使用 Concatenated Needle 長度做為 `formula.Letters` 陣列索引而導致的 `IndexOutOfRangeException` 崩潰問題。現在改為依據 `formula.Letters` 物件列表的實際長度進行準確的逐一元素比對。

## [v3.6.0.0] - 2026-07-02

- **PDF 壓縮與最佳化 (PDF Compression & Shrinking)**：實作內建的 PDF 壓縮處理核心，不依賴任何外部工具。
- **結構化優化引擎 (Structural PDF Optimizer)**：支援重複嵌入字型去重、頁面 Stream 註解與空白簡化、大字型剝離（Unembedding）等結構化精簡。
- **GDI+ 圖片降樣式與編碼 (Native Image Downsampling)**：使用 GDI+ 進行圖片的高品質雙立方（Bicubic）降樣式與 JPEG 編碼重壓縮，並對低解析或小圖片自動跳過壓縮以維持圖表清晰度。
- **Dashboard 設定頁 Slider 拉條 UI**：實作一個緊湊、4 停靠點的橫向 Slider UI，一鍵連動 DPI 與品質設定，省下設定頁面 60% 垂直空間。
- **測試與重組**：補齊 PDF 壓縮自訂參數的單元測試，並重構 Git 提交歷史為乾淨、原子、無過渡期垃圾的原子提交。

## [v3.5.0.0] - 2026-06-29

- **LibreOffice Offline Office Engine**: Added Auto, Microsoft Office, and LibreOffice engine modes for Word, Excel, and PowerPoint to PDF conversion.
- **Managed LibreOffice Setup**: Added built-in manifest metadata, official MSI download, SHA256 verification, version matching, background installation, quiet removal, and restart-aware status handling.
- **No-Office Fallback**: Allows users without Microsoft Office to run Office-to-PDF conversion locally through LibreOffice while preserving local processing.
- **Dashboard Settings**: Added Office engine controls, LibreOffice status messaging, clearer download/network failures, and simplified overview engine status.
- **Convert Tool Groups**: Reorganized the Convert tab into Office, PDF, and Image groups so the nine main actions are easier to scan.

## [v3.4.0.0] - 2026-06-21

- **Excel to PDF Conversion**: Added new right-click context menu command to convert Excel spreadsheets (.xlsx/.xls) to PDF using Microsoft Excel COM automation
- **Shell Extension Integration**: Added Excel to PDF menu item with localized labels in 5 languages (en, zh-TW, zh-CN, ja, ko)
- **Dashboard UI**: Added Excel conversion card with drag-and-drop auto-detection and Excel engine status indicator in Overview tab
- **CLI Support**: Added `excel2pdf` command with directory expansion and progress display
- **Developer Documentation**: Added 18-step checklist for adding new conversion commands and Conventional Commits guide

## [v3.3.3.0] - 2026-06-21

- **PDF Translation Pipeline Modularization**: Decomposed monolithic `FileProcessor` (2000+ lines) into 80+ dedicated classes organized by domain (paragraphs, tables, diagrams, gray prompts, annotations, rendering, translation)
- **Layout Analysis Improvements**: Enhanced table detection, diagram region bypass, paragraph role/semantic classification, and page reading order extraction
- **Translation Rule Documentation**: Added comprehensive translation rules specification (`docs/translation_rules.md`) covering layout analysis, bypass logic, translation correction, and rendering rules
- **PDF Translation Diagnostics**: Added reusable diagnostic scripts for analyzing translation quality, mask coverage, and rendering correctness
- **CLI Batch Progress**: Added real-time PDF translation progress display and explicit output directory support
- **Simplified-Traditional Chinese Converter**: Integrated 7800+ character mapping pairs for simplified-to-traditional Chinese conversion
- **Test Infrastructure**: Added C# integration test suites and Python PDF regression testing framework
- **Font Resolver Enhancement**: Rewrote `ClickraFontResolver` with improved CJK and math symbol mapping

## [v3.3.2.0] - 2026-06-19

- **Dependency Updates**: Updated PDFsharp 6.1.1 → 6.2.4, PdfPig 0.1.8 → 0.1.14, System.Drawing.Common 10.0.8 → 10.0.9
- **Build Script Fixes**: Fixed CHANGELOG rotation regex in bump_version.ps1, removed invalid /q flag from build_msix.ps1
- **Naming Conventions**: Renamed LogicalWidth/Height → GetLogicalWidth/Height, PowerShellInteropHelper → PowerShellHelper, ProcessorHelper → ProgressCalculator
- **Code Quality**: Replaced magic numbers with named constants (IDC_HAND), fixed null reference warnings

## [v3.3.1.0] - 2026-06-18

- **Architecture Refactoring**: Comprehensive codebase refactoring for improved maintainability
- Extract shared UI helpers to `UIHelper.cs` (GetRoundedRectPath, Lighten, GetSystemColorizationColor, etc.)
- Create `MultiFileProcessorBase` for processor loop boilerplate
- Create `ProgressCalculator` for progress calculation utilities
- Split `ShellExtension.cs` into 5 separate class files
- Split `DashboardWindow.Paint.cs` by tab (Overview, History, Settings, About, Dropdowns)
- Extract `WM_LBUTTONDOWN` handler to separate file
- Rename methods for consistency (LogicalWidth → GetLogicalWidth, ImagesToPdf → ConvertImagesToPdf, etc.)
- Remove duplicate Win32 constants and magic numbers

## [v3.3.0.0] - 2026-06-10

- **PDF Decryption & Inline Password Input**: Added high-performance PDF password removal feature
- Implemented non-flickering, inline password input field in progress window
- Protected unencrypted files from redundant decryption prompts

## [v3.2.0.0] - 2026-05-31

- **Dashboard History Layout Optimization**: Adaptive history layout and filename width calculation
- Target Translation Language Simplification
- Correct Failure Recording with precise "Error/Cancel" status display

## [v3.1.0.0] - 2026-05-30

- **Dashboard Stabilization**: Enforced single-instance check
- Progress Minimize-to-Tray with progress percentage updates
- Cancellation warning dialog for background conversion processes
- Horizontal scrollbar for overflow progress status

## [v3.0.9.0] - 2026-05-26

- **About Tab, Full i18n & Dashboard Enhancements**: Added About tab with collaboration links
- Expanded localization to ja-JP, ko-KR, zh-CN
- Minimize-to-system-tray, custom output folder picker
- Expandable history cards with elapsed time and file paths
- Adaptive layout for window maximization, high DPI support

## [v3.0.8.0] - 2026-05-21

- **Conversion History & Dashboard Enhancements**: Local conversion history tracking
- Quick Convert tab, localized user language switching
- Refactored `DashboardForm` into clean static partial files

## [v3.0.7.0] - 2026-05-21

- **Dynamic Progress Bar & Toast Notifications**: Pure Win32/GDI+ animated progress window
- WinUI 3 style shimmer effects, system accent color integration
- Native Windows Toast notifications

## [v3.0.6.0] - 2026-05-15

- **Native Dashboard & Word-to-PDF**: High-performance Win32 dashboard
- Microsoft Word conversion engine
- Achieved 100% NativeAOT project structure

## [v3.0.5.0] - 2026-05-13

- **Diagnostic & Compatibility Fix**: Improved PPT conversion error handling
- Store compliance improvements

## [v3.0.4.0] - 2026-05-11

- **Critical Shell Fix**: Resolved Windows 11 context menu visibility issues
- Supporting system-specific IIDs and synchronizing CLSID across manifests

## [v3.0.3.0] - 2026-05-07

- **Store Compliance**: Fixed version revision number requirements

## [v3.0.2.0] - 2026-05-05

- **Cross-version Stability Release**: Fixed Win10/11 compatibility and installer errors

## [v3.0.1.0] - 2026-04-25

- Logic Decoupling & Dev Guidelines
- Split image processing and introduced AI-driven automation

## [v3.0.0.0] - 2026-04-24

- **NativeAOT Shell Extension**: Full Win11 modern menu support with Asset Embedding

## [v2.0.0] - 2026-04-21

- Shift to C# CLI with interactive installer

## [v1.0.0] - 2025-12-07

- Initial release (Python-based legacy)
