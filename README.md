# ⚡ Windows ContextTools v3.0

A high-performance, native context menu utility suite for Windows 11. Powered by C# NativeAOT, it provides sub-millisecond responsiveness, replacing slow Python scripts with a truly native user experience.

[閱讀中文版 (Read in Traditional Chinese)](README.zh-TW.md)

---

## 📌 Why ContextTools?

Most productivity scripts (PDF merging, image conversion, etc.) are typically written in Python. While powerful, Python suffers from a **1-2 second "cold start" delay** every time you run a script. For context menu actions, this delay feels like an eternity.

**ContextTools** is built using **NativeAOT (Native Ahead-of-Time)** technology:
*   **Zero Latency**: Starts in **less than 0.01 seconds**. It feels instantaneous, just like a built-in Windows feature.
*   **Zero Dependencies**: No need to install .NET Runtime or Python. It's a truly self-contained binary.
*   **Modern Aesthetics**: Fully integrated into the Windows 11 modern context menu with a sleek sub-menu architecture.

---

## 📜 Version History

| Version | Release Date | Key Milestone |
| :--- | :--- | :--- |
| **v1.0.0** | Dec 07, 2025 | Initial release (Python-based legacy). |
| **v2.0.0** | Apr 21, 2026 | Shift to C# CLI with interactive installer. |
| **v3.0.0** | **Current** | **NativeAOT Shell Extension**. Full Win11 modern menu support with Asset Embedding. |

---

## ✨ Core Features

### 1. 📂 Modern Sub-menu (Windows 11 Only)
Commands are elegantly tucked away in the `ContextTools (⚡)` sub-menu, keeping your primary context menu clean and uncluttered.

### 2. 📄 PPT/PPTX to PDF
*   **Feature**: Silently exports PowerPoint presentations to high-quality PDFs in the background.
*   **Native Power**: Handles multiple files in a single pass without spawning dozens of windows.

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

## 🚀 Professional Installation (Two-File Bundle)

To achieve maximum minimalism, we use **Asset Embedding** technology. Your distribution package only needs two files:
1.  `ContextTools.exe` (The engine, containing all menu assets)
2.  `setup_context_menu.ps1` (The smart installer)

### Installation:
1.  Right-click `setup_context_menu.ps1` and select **"Run with PowerShell"**.
2.  **Custom Path**: Choose the default path or enter your own installation directory.
3.  **Auto-Deploy**: The script calls the executable's deployment engine to extract manifests, icons, and DLLs automatically.

### Uninstallation:
Run the script and choose **"2. Remove Tool"**. It will automatically unregister the shell extension and clean up the installation folder.

---

## 🛠️ Developer & Architecture

### Project Structure
- `src/ContextTools.CLI`: Core logic for file processing.
- `src/ContextToolsShell`: NativeAOT implementation of the COM Shell Extension.
- `src/resources`: Identity manifests and visual assets.

### Technology: Stealth Asset Deployment
`ContextTools.exe` embeds `AppxManifest.xml`, `app.png`, and `ContextToolsShell.dll` as resources. The `--deploy` flag extracts these components on-the-fly, enabling a "Zero Side-car" distribution model.

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
This project uses **PDFsharp** (MIT License).
