# Changelog

All notable changes to Clickra will be documented in this file.

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
