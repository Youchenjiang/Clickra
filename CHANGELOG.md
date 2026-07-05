# Changelog

All notable changes to Clickra will be documented in this file.

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
