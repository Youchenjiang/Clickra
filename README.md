# ⚡ Windows ContextTools (Zero Latency)

A high-performance, native C# utility suite for Windows context menus. Designed to replace slow Python scripts with instant-start execution.

[閱讀中文版 (Read in Traditional Chinese)](README.zh-TW.md)

---

## 📌 Why ContextTools?

Most productivity scripts are written in Python. While powerful, Python suffers from a **1-2 second "cold start" delay** every time you run a script from the context menu. For quick actions like "Convert this PPT to PDF", that delay feels like an eternity.

**ContextTools** is written in native C# (.NET). It starts in **less than 0.01 seconds**, providing a truly "instant" integration that feels like a native part of Windows.

---

## ✨ Features & Usage

All features are bundled into a single executable `ContextTools.exe` and integrated via the **"Send To"** menu for maximum stability.

### 1. 簡報轉 PDF (PPT/PPTX to PDF)
- **What it does**: Silently opens PowerPoint in the background and exports your slides to a high-quality PDF.
- **Why SendTo?**: Unlike a standard right-click command, using "SendTo" allows you to select 10+ presentations at once and convert them in a **single background process** instead of spawning 10 separate windows.

### 2. PDF 合併 (PDF Merge)
- **What it does**: Scans all selected PDF files and merges them into a single `Merged_PDF.pdf` in the same directory.
- **Anti-Confusion**: Automatically sorts files by filename (A-Z) before merging, ensuring the output order is logical regardless of selection order.

### 3. 圖片合併成 PDF (Images to PDF)
- **What it does**: Concatentates multiple images (`.jpg`, `.png`, `.webp`, etc.) into a single multi-page PDF document.
- **Scaling**: Preserves original image dimensions for pixel-perfect results.

### 4. 圖片垂直拼接 (Vertical Image Stitch)
- **What it does**: Stitches multiple images vertically into one single "long" image.
- **Smart Alignment**: Automatically centers images horizontally if they have different widths.

---

## 🚀 Installation Guide

### Step 1: Prepare the Executable
Ensure `ContextTools.exe` is in the same folder as `setup_context_menu.ps1`.

### Step 2: Run the Installer
Right-click `setup_context_menu.ps1` and select **"Run with PowerShell"**.

### Step 3: Interactive Choices
The installer will ask:
- **Installation Path**: By default, it installs to `%LOCALAPPDATA%\ContextTools`. You can press **Enter** to accept or type a custom path (e.g., `D:\Tools\ContextTools`).
- **Clean Up**: Once the script says "Success", you can safely **delete** the original download folder. Everything needed is now safely tucked away in your chosen installation directory.

---

## 🛠️ Developer & Modification Guide

### Project Structure
- `Program.cs`: Core logic. Contains the command router and file processing functions.
- `ContextTools.csproj`: Project configuration and NuGet dependencies (`PdfSharp`, `System.Drawing.Common`).
- `app.ico`: The lightning bolt icon embedded into the executable.

### How to Modify
1. Open the folder in VS Code or Visual Studio.
2. Edit `Program.cs` to add new commands or change existing logic.
3. If adding UI elements, use the `ShowWarning` helper which uses native Win32 `user32.dll` to avoid heavy WinForms dependencies.

### Compilation Modes
You must have the [.NET SDK](https://dotnet.microsoft.com/download) installed.

#### 📦 Option A: Ultra-Fast Mode (Recommended)
Produces a **~4MB** file. Fast startup, small footprint. Requires [.NET Runtime](https://dotnet.microsoft.com/download/dotnet/current/runtime) on the PC.
```powershell
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true
```

#### 📦 Option B: Portable Mode
Produces a **~75MB** file. Works on any Windows machine even if .NET is NOT installed.
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## 🗑️ Uninstallation

1. Go to your installation directory (default: `%LOCALAPPDATA%\ContextTools`).
2. Delete the folder.
3. Open the "Send To" folder by pressing `Win + R` and typing `shell:sendto`.
4. Delete the "⚡" or "ContextTools" shortcuts.

---

## 📄 License
This project uses **PDFsharp** (MIT License).
