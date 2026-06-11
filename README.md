# Clickra v3.2.1.0

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Clickra-blue?style=for-the-badge&logo=microsoft-store)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)

A high-performance, native context menu utility suite for Windows 11. Powered by C# NativeAOT, it provides sub-millisecond responsiveness, replacing slow Python scripts with a truly native user experience.

[閱讀中文版 (Read in Traditional Chinese)](README.zh-TW.md)

---

## 📌 Why Clickra?

Most productivity scripts (PDF merging, image conversion, etc.) are typically written in Python. While powerful, Python suffers from a **1-2 second "cold start" delay** every time you run a script. For context menu actions, this delay feels like an eternity.

**Clickra** is built using **NativeAOT (Native Ahead-of-Time)** technology:
*   **Zero Latency**: Starts in **less than 0.01 seconds**. It feels instantaneous, just like a built-in Windows feature.
*   **Zero Dependencies**: No need to install .NET Runtime or Python. It's a truly self-contained binary.
*   **Modern Aesthetics**: Fully integrated into the Windows 11 modern context menu with a sleek sub-menu architecture.

---

## 📜 Version History

| Version | Release Date | Key Milestone |
| :--- | :--- | :--- |
| **v1.0.0** | Dec 07, 2025 | Initial release (Python-based legacy). |
| **v2.0.0** | Apr 21, 2026 | Shift to C# CLI with interactive installer. |
| **v3.0.0** | Apr 24, 2026 | **NativeAOT Shell Extension**. Full Win11 modern menu support with Asset Embedding. |
| **v3.0.1** | Apr 25, 2026 | Logic Decoupling & Dev Guidelines. Split image processing and introduced AI-driven automation. |
| **v3.0.2**   | May 05, 2026 | **Cross-version Stability Release**. Fixed Win10/11 compatibility and installer errors. |
| **v3.0.3.0** | May 07, 2026 | **Store Compliance**. Fixed version revision number requirements. |
| **v3.0.4.0** | May 11, 2026 | **Critical Shell Fix**. Resolved Windows 11 context menu visibility issues (Store Policy 10.1.2.10) by supporting system-specific IIDs and synchronizing CLSID across manifests. |
| **v3.0.5.0** | May 13, 2026 | **Diagnostic & Compatibility Fix**. Improved PPT conversion error handling and Store compliance. |
| **v3.0.6.0** | May 15, 2026 | **Native Dashboard & Word-to-PDF**. Implemented high-performance Win32 dashboard and added Microsoft Word conversion engine. Achieved 100% NativeAOT project structure. |
| **v3.0.7.0** | May 21, 2026 | **Dynamic Progress Bar & Toast Notifications**. Implemented pure Win32/GDI+ animated progress window with WinUI 3 style shimmer effects, system accent color integration, and native Windows Toast notifications. |
| **v3.0.8.0** | May 21, 2026 | **Conversion History & Dashboard Enhancements**. Implemented local conversion history tracking and dynamic Win32 list view, integrated Quick Convert tab, added localized user language switching (zh-TW/en-US), and refactored `DashboardForm` into clean static partial files. |
| **v3.0.9.0** | May 26, 2026 | **About Tab, Full i18n & Dashboard Enhancements**. Added About tab (project description, collaboration links, one-click Gmail diagnostics). Expanded localization to ja-JP, ko-KR, zh-CN across dashboard and shell context menu. Added minimize-to-system-tray, custom output folder picker, expandable history cards with elapsed time and file paths. Implemented adaptive layout for window maximization, crisp scaling for high DPI displays, and smart "file-first" interaction flow for fast conversions. Dynamic sidebar width, language-aware font normalization on first launch, and NativeAOT compatibility fixes (`[STAThread]`, `GetModuleHandle`). |
| **v3.1.0.0** | May 30, 2026 | **Dashboard Stabilization, Progress Minimize-to-Tray & Horizontal Scrollbar**. Enforced single-instance check, supported progress window minimize to system tray with progress percentage updates, implemented cancellation warning dialog to terminate background conversion processes (PowerPoint/Word), split history logs into individual file rows with middle truncation and horizontal wheel scrolling, and supported horizontal text scrolling with a draggable scrollbar for overflow progress status. |
| **v3.2.0.0** | **Current** | **Dashboard History Layout Optimization, Target Translation Language Simplification, and Correct Failure Recording**. Implemented adaptive history layout and filename width calculation, added precise "Error/Cancel" status display in history, removed redundant target translation language options, and improved settings panel text layout to eliminate overlap. |


---

## ✨ Core Features

### 1. 📂 Modern Sub-menu (Windows 11 Only)
Commands are elegantly tucked away in the `Clickra` sub-menu, keeping your primary context menu clean and uncluttered.

### 2. 📊 Native Dashboard
*   **Feature**: A high-performance, dark-themed dashboard to monitor system compatibility.
*   **Status Detection**: Real-time detection of PDF Engine, Microsoft Word, and PowerPoint status.
*   **AOT Power**: Pure Win32 implementation ensures zero startup lag.

### 3. 📄 Word & PPT to PDF
*   **Feature**: Silently exports Office documents to high-quality PDFs in the background.
*   **Requirement**: Microsoft Office (Word/PowerPoint) must be installed locally.

### 4. 🔗 PDF Merge
*   **Feature**: Merges selected PDF files into a single document based on filename order.
*   **Speed**: Instant processing with automated cleanup of temporary resources.

### 5. 🖼️ Images to PDF
*   **Feature**: Packages multiple images (JPG, PNG, WebP) into a single multi-page PDF.
*   **Quality**: Pixel-perfect conversion preserving original resolution and aspect ratios.

### 6. 🎞️ Image Stitching
*   **Feature**: Joins multiple images vertically into a single long-form image.
*   **Alignment**: Automatic horizontal centering for images of varying widths.

### 7. 🕒 Conversion History
*   **Feature**: Locally tracks all conversion activities.
*   **Details**: A high-performance native Win32 list view rendering processed file paths, operation types, timestamps, and execution status.


---

## 🛠️ Installation

### Recommended: Microsoft Store (Auto-updates)
[![Microsoft Store Badge](https://developer.microsoft.com/en-us/store/badges/images/English_get-it-from-MS.png)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)

### Manual: GitHub Release
1.  Download the latest `Clickra.msix` from [Releases](../../releases).
2.  Double-click the file to install.

---

## 📂 Documentation Directory

To keep the main documentation clean and accessible, Clickra's documentation is divided into specialized guides. Please refer to the branch map below to navigate the repository documentation:

```text
Clickra/
├── README.md (or README.zh-TW.md)  # Root Entry (Product Intro, Install, Features, Version History)
├── PRIVACY.md                      # Privacy Policy (for Windows Store compliance)
├── LOCAL_BUILD_NOTES.md            # Developer Guidelines (Setup, compilation, packaging, and Git workflow)
└── docs/
    ├── ROADMAP.md                  # Product Roadmap & Milestones
    ├── StoreListing_*.md           # Store Metadata & Product Descriptions
    └── development/
        ├── release_guideline.md    # Versioning constraints & release checklists
        ├── shell_extension_best_practices.md # Native COM & Shell Extension development rules
        └── shell_diagnostic_guide.md         # Shell extension debugging & registry diagnostics
```

### Document Navigation Links
*   **Product & Public Documentation**:
    *   [Roadmap & Future Goals](docs/ROADMAP.md) — High-level milestone tracking and feature plans.
    *   [Privacy Policy](PRIVACY.md) — Privacy declarations for Windows App Store compliance.
    *   [Store Listing Metadata](docs/StoreListing_EN.md) — Store descriptions and screenshots index.
*   **Developer Guidelines (Start Here)**:
    *   [LOCAL_BUILD_NOTES.md](LOCAL_BUILD_NOTES.md) — Local development compilation, packaging scripts, adding new features, and development Git workflows.
*   **Specialized Development Guides**:
    *   [Release & Versioning Guidelines](docs/development/release_guideline.md) — 4-digit version constraints, version update files list, and MSIX store publishing checklist.
    *   [Shell Extension Best Practices](docs/development/shell_extension_best_practices.md) — COM interface guidelines, NativeAOT integration, and registration practices.
    *   [Shell Extension Diagnostics & Debugging](docs/development/shell_diagnostic_guide.md) — Debug logging implementation and troubleshooting missing registry/menu commands.

---

## 🧠 Technical Insights (The "War Stories")

Developing a Shell Extension in **NativeAOT** is a deep dive into Windows internals:

### 1. Manual COM VTables
Since NativeAOT doesn't support standard .NET COM Interop, we manually constructed VTables for `IExplorerCommand`. This ensures binary compatibility with the Windows Shell while maintaining high performance.

### 2. The Windows 11 "Shadow" Interface
Windows 11 often queries undocumented **"Shadow GUIDs"** instead of the official `IExplorerCommand` GUID. Supporting these alternative interfaces is critical for rendering sub-menu arrows and maintaining responsiveness.

### 3. VTable Parameter Sensitivity
Because the extension runs inside `explorer.exe`, any stack imbalance (like a mismatched function signature) results in an immediate desktop crash. Precision is mandatory.

---

## 📄 License
This project is licensed under the **Apache License 2.0**.
Core components use **PDFsharp** (MIT License).
