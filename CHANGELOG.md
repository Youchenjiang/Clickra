# Changelog

All notable changes to Clickra will be documented in this file.

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
