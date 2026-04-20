# ⚡ Windows ContextTools (Zero Latency)

A highly optimized, zero-latency context menu utility for Windows, compiled natively in C# to eliminate the slow startup times typical of Python scripting environments.

[閱讀中文版 (Read in Traditional Chinese)](README.zh-TW.md)

## 📌 Description

Traditional Python scripts (like those relying on `win32com`, `PyPDF2`, or `pandas`) suffer from a 1-to-2 second startup latency on Windows due to interpreter initialization and environment loading. While this is acceptable for long-running GUIs, it feels sluggish for quick "Right-Click & Execute" context menu actions.

**ContextTools** acts as a unified C# backend that provides instant execution (< 0.05s) for standard right-click file operations.

## ✨ Features

- **⚡ Instant PPTX to PDF**: Invokes native PowerPoint COM objects to convert presentations instantly in the background.
- **⚡ PDF Merge**: Merges multiple selected PDFs into one.
- **⚡ Image to PDF**: Packs selected Images (`.jpg`, `.png`) into a single PDF document.
- **⚡ Image Stitch**: Stitches selected images vertically into one seamless long image.
- **🛡️ Auto-Sorting**: Automatically sorts all selected files alphabetically (A-Z) before processing, preventing the infamous Windows bug where multi-selection order depends on which file the user right-clicked last.

## 🚀 Installation & Usage

1. **Pre-compiled Executable**: If you possess the `ContextTools.exe` file, place it in this folder.
2. **One-Click Setup**: Run `setup_context_menu.ps1` via PowerShell. It will automatically:
   - Register the `PPTX to PDF` prompt natively into your Windows Registry (`HKEY_CLASSES_ROOT`).
   - Create smart shortcuts for the Merge utilities in your Windows `SendTo` folder natively supporting multi-file selection.

### Usage Example
- **For a single PPTX**: Right-click the `.pptx` file ➔ `⚡ 轉為 PDF (極速)`.
- **For multi-file operations (PDFs/Images)**: Select all target files ➔ Right-click ➔ `Send to` ➔ Select the corresponding `⚡` command.

## 🛠️ Build from Source (For Developers)

If you wish to recompile the standalone executable:

**Requirements:**
- .NET SDK (8.0 or newer recommended)
- `PdfSharp` & `System.Drawing.Common` (Handled automatically by NuGet)

**Command:**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
This produces a standalone executable that works on any 64-bit Windows machine, even without the .NET runtime installed.
