# Clickra v3.6.5.0

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Clickra-blue?style=for-the-badge&logo=microsoft-store)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)

A high-performance, native context menu utility suite for Windows 10 and Windows 11. Powered by C# NativeAOT, it provides sub-millisecond responsiveness, replacing slow Python scripts with a truly native user experience.

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
| **v3.6.5.0** | 2026/08/08 | **Visual PDF Splitter & Split PDF Button**. Added a visual PDF splitter (custom segments, split-each-page, fixed-page modes) with page preview, a Split PDF button in the dashboard, and split-at-current-page. |
| **v3.6.4.0** | 2026/07/22 | **Reliable, Layout-Aware PDF Translation**. Added bounded .NET provider fallback, malformed-output guards, and atomic output gates; preserved heading hierarchy, typography, merged tables, fixed artwork, captions, and links; added rendered source/output occupancy and deterministic ASTER regression coverage. |
| **v3.6.3.0** | 2026/07/09 | **CI/CD Release Automation & Multi-language Support**. Added repository policy validation, integrated Microsoft Store publishing into CI/CD release workflow, and enabled full native packaging and listing synchronization for 5 languages. |
| **v3.6.2.0** | 2026/07/05 | **SSL/TLS Certificate Verification**. Removed insecure validation bypass in MyMemory API translator and enabled standard TLS 1.2/1.3 system validation. |

[View Full Changelog](CHANGELOG.md)


---

## ✨ Core Features

### 1. 📂 Modern Sub-menu (Windows 10/11)
On Windows 11, commands appear in the modern `Clickra` sub-menu. Windows 10 uses the compatible classic context-menu integration.

### 2. 📊 Native Dashboard
*   **Feature**: A high-performance, dark-themed dashboard to monitor system compatibility.
*   **Status Detection**: Real-time detection of the PDF engine and Office conversion engine readiness.
*   **AOT Power**: Pure Win32 implementation ensures zero startup lag.

### 3. 📄 Office to PDF
*   **Feature**: Silently exports Word, Excel, and PowerPoint documents to high-quality PDFs in the background.
*   **Engine Choice**: Supports Auto, Microsoft Office, and LibreOffice modes. LibreOffice can be downloaded and managed from Clickra as a free local fallback when Microsoft Office is not available.

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

### 8. 🔓 Remove PDF Password
*   **Feature**: Removes passwords from protected PDF documents directly from the right-click menu.
*   **Aesthetics**: Implemented a modern, non-flickering inline password input box directly within the GDI+ progress window, offering a native, seamless flow without modal dialog popups.
*   **Safety**: Validates PDF encryption status first to prevent decrypting unencrypted files.

### 9. 📄 PDF Compression
*   **Feature**: Compresses PDF files locally using a high-fidelity native engine, reducing file size through content stream minification, font deduplication, and image downsampling.
*   **UI Settings**: A compact, space-saving 4-stop horizontal slider (Min, Small, Std, High) to control DPI and JPEG quality parameters instantly.
*   **Clever Logic**: Automatically bypasses low-resolution or small images to keep drawings and charts sharp.


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
├── README.md (or README.zh-TW.md)  # Root Entry (Product Intro, Install, Features)
├── CHANGELOG.md                    # Version History (all releases)
├── PRIVACY.md                      # Privacy Policy (for Windows Store compliance)
├── LOCAL_BUILD_NOTES.md            # Developer Guidelines (Setup, compilation, packaging, and Git workflow)
└── docs/
    ├── ROADMAP.md                  # Product Roadmap & Milestones
    ├── StoreListing_*.md           # Store Metadata & Product Descriptions
    └── development/
        ├── release_guideline.md    # Versioning constraints & release checklists
        ├── shell_extension_best_practices.md # COM, NativeAOT, memory, and package invariants
        └── shell_diagnostic_guide.md         # Shell extension logging and Explorer diagnostics
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
    *   [Shell Extension Best Practices](docs/development/shell_extension_best_practices.md) — COM interfaces, NativeAOT memory rules, and Sparse Package/MSIX invariants.
    *   [Shell Extension Diagnostics & Debugging](docs/development/shell_diagnostic_guide.md) — Fail-safe logging, HRESULT triage, and staged Explorer troubleshooting.

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

## 🏗️ Architecture

Clickra follows a clean three-layer architecture:

```text
src/
├── Clickra.CLI/              # UI Layer (Win32/GDI+ Dashboard & Progress)
│   ├── DashboardWindow.*.cs      (6 partial classes: State, Events, Paint, etc.)
│   ├── ProgressWindow.*.cs       (5 partial classes: Controls, Paint, Process, Tray)
│   ├── ClickraCli.*.cs           (Entry point & deployment)
│   ├── Native/Win32.cs           (P/Invoke declarations)
│   └── UIHelper.cs               (Shared UI utilities)
├── Clickra.Core/             # Core Logic (Processors & Storage)
│   ├── Processors/               (Base classes + 8 Processor implementations)
│   ├── ClickraStorage.*.cs       (Settings, History, ActiveRecord)
│   ├── FileProcessor.cs          (Public API facade)
│   ├── Localization.cs           (i18n support)
│   └── TranslationEngine.cs      (PDF translation)
└── ClickraShell/             # Windows Shell Integration
    ├── ComMethods.cs             (COM interface implementations)
    ├── Exporter.cs               (VTable creation)
    ├── Guids.cs                  (COM interface GUIDs)
    ├── Kernel32.cs               (Win32 API declarations)
    └── ShellUtils.cs             (Module path & string resources)
```

### Key Design Patterns
- **Partial Classes**: Large files split by responsibility (e.g., `DashboardWindow.Paint.cs`, `DashboardWindow.Events.cs`)
- **Template Method**: `SingleFileProcessorBase` and `MultiFileProcessorBase` provide common loop/cancellation patterns
- **Facade Pattern**: `FileProcessor` exposes a simplified public API over internal processors
- **Shared Utilities**: `UIHelper` contains reusable drawing methods (scrollbars, dropdowns, rounded rects)

---

## 📄 License
This project is licensed under the **Apache License 2.0**.
Core components use **PDFsharp** (MIT License).
