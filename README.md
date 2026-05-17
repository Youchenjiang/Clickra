# Clickra v3.0.7.0

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
| **v3.0.7.0** | **Current** | **Dynamic Progress Bar & Toast Notifications**. Implemented pure Win32/GDI+ animated progress window with WinUI 3 style shimmer effects, system accent color integration, and native Windows Toast notifications. |

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

### 3. 🔗 PDF Merge
*   **Feature**: Merges selected PDF files into a single document based on filename order.
*   **Speed**: Instant processing with automated cleanup of temporary resources.

### 4. 🖼️ Images to PDF
*   **Feature**: Packages multiple images (JPG, PNG, WebP) into a single multi-page PDF.
*   **Quality**: Pixel-perfect conversion preserving original resolution and aspect ratios.

### 5. 🎞️ Image Stitching
*   **Feature**: Joins multiple images vertically into a single long-form image.
*   **Alignment**: Automatic horizontal centering for images of varying widths.

---

## 🛠️ Installation

### Recommended: Microsoft Store (Auto-updates)
[![Microsoft Store Badge](https://developer.microsoft.com/en-us/store/badges/images/English_get-it-from-MS.png)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)

### Manual: GitHub Release
1.  Download the latest `Clickra.msix` from [Releases](../../releases).
2.  Double-click the file to install.

---

### 2. How to Build
Since we use asset embedding, the build is a two-stage process:

**Stage 1: Build the Shell Extension (DLL)**
```powershell
dotnet publish src\ClickraShell\ClickraShell.csproj -c Release -r win-x64 -p:PublishAot=true --output .
```

**Stage 2: Build the Main App (CLI)**
This embeds the DLL and assets from `src/resources` into the final executable using NativeAOT:
```powershell
dotnet publish src\Clickra.CLI\Clickra.csproj -c Release -r win-x64 --output .
```

### 3. How to add new features
1.  **Core Logic**: Add new command handling in `src/Clickra.CLI/Program.cs`.
2.  **UI Menu**: Modify the `SubTitles` and `SubArgs` arrays in `src/ClickraShell/ShellExtension.cs`.
3.  **Re-build**: Re-publish `Clickra.exe` following the build sequence above.

### 4. Development Workflow
To keep the `main` branch stable, direct pushes to `main` are prohibited. All changes must be made via **Pull Requests (PR)**.

*   **Feature Development (`feature/...`)**: For all new features or bug fixes, branch off from `main` to `feature/<branch-name>`.
*   **Hotfixes (`hotfix/...`)**: For urgent bugs in the released version, branch off to `hotfix/<branch-name>`.
*   **Merge Rules**: Once development is done, push your branch and open a PR. Merge into `main` after review.

### 5. Automated Versioning & MSIX Packaging
The project provides built-in PowerShell scripts for automated version bumping and MSIX packaging:

*   **Bump Version & Build**:
    ```powershell
    powershell -File scripts/bump_version.ps1 -Type <major|minor|patch|revision> -Build
    ```
    *   `-Type`: Specifies the version component to increment (e.g. `patch` increments `3.0.6.0` to `3.0.7.0`).
    *   `-Build`: Automatically triggers the Native AOT two-stage build, compiles PRI resources, signs the package with the developer certificate, and generates the final `Clickra.msix` in the root directory.

*   **Standalone MSIX Packaging**:
    If you only want to rebuild the MSIX package without bumping the version:
    ```powershell
    powershell -File scripts/build_msix.ps1
    ```

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
