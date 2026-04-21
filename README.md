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
The script will present a menu:
1. **Install / Update**: Sets up the shortcuts and installs the executable.
2. **Uninstall (Restore)**: Removes all shortcuts and registry entries, and offers to delete the installation folder.
3. **Exit**: Closes the script.

During installation, the script will ask for an **Installation Path**. By default, it installs to `%LOCALAPPDATA%\ContextTools`. You can press **Enter** to accept or type a custom path.

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

## 🧠 Technical Insights (The "War Stories")

Building a high-performance shell extension in **NativeAOT** comes with unique challenges. Here are the key lessons learned during development:

### 1. Manual COM VTables in NativeAOT
NativeAOT does not support the standard `.NET COM Interop`. We had to manually construct the VTables for `IExplorerCommand` and `IEnumExplorerCommand`. 
- **Solution**: We implemented a `UniversalObject` memory structure that groups multiple interfaces (Primary, Selection) into a single aligned block, ensuring binary compatibility with the Windows Shell's expectation of C-style objects.

### 2. The Windows 11 "Shadow" Interface Mystery
Standard documentation suggests implementing `IExplorerCommand` using its official GUID. However, in modern Windows 11 builds, Explorer often queries for undocumented **"Shadow GUIDs"** (e.g., `ea5d0de4-770d-4da0-a9f8-d7f9a140ff79`).
- **Insight**: Without supporting these alternative GUIDs in `QueryInterface`, sub-menu arrows often fail to render, or commands become unresponsive.

### 3. VTable Slot Sensitivity (The Explorer Crash)
The most common cause of `explorer.exe` crashes during development was **VTable parameter mismatch**. 
- **The Pitfall**: Mapping a 2-parameter COM method (like `IInitializeCommand::Initialize`) to a 1-parameter C# method causes a stack imbalance. Since the shell extension runs inside the Explorer process, a single stack error results in an immediate desktop crash.
- **Safety Rule**: If you don't perfectly match the signature, it's safer to return `E_NOINTERFACE` than to provide a mismatched implementation.

### 4. Logging & Performance Bottlenecks
During the "QueryInterface Storm" (where Explorer asks for interfaces hundreds of times per second), synchronous file I/O (logging) is a death sentence for performance.
- **Problem**: Opening a file handle in a hot path causes deadlocks and UI freezes.
- **Standard**: All high-frequency COM methods in this project are "Log-Free" to ensure the context menu stays snappy.

---


### 2. Windows 11 「影子介面」之謎
標準文件建議實作 `IExplorerCommand` 時使用其官方 GUID。然而，在現代 Windows 11 版本中，檔案總管經常會詢問一些未公開的 **「影子 GUID」**（例如：`ea5d0de4-770d-4da0-a9f8-d7f9a140ff79`）。
- **關鍵發現**：如果在 `QueryInterface` 中不支援這些替代 GUID，子選單的箭頭往往會消失，或者指令會變得無法點擊。

### 3. VTable 槽位敏感度 (導致閃退的主因)
開發過程中 `explorer.exe` 閃退最常見的原因是 **VTable 參數不匹配**。
- **陷阱**：將一個 2 參數的 COM 方法（如 `IInitializeCommand::Initialize`）對應到一個 1 參數的 C# 方法會導致堆疊失衡（Stack Imbalance）。由於擴充功能運行在 Explorer 進程內，單個堆疊錯誤就會導致整個桌面環境直接崩潰。
- **安全準則**：如果無法完美匹配函數簽名，回傳 `E_NOINTERFACE` 比提供一個錯誤的實作要安全得多。

### 4. 日誌記錄與性能瓶頸
在「介面查詢風暴 (QueryInterface Storm)」期間（Explorer 每秒會詢問數百次介面），同步檔案 I/O（寫日誌）是性能的殺手。
- **問題**：在熱路徑（Hot Path）中開啟檔案句柄會導致死結（Deadlock）和介面凍結。
- **規範**：本專案中的所有高頻率 COM 方法均為「無日誌 (Log-Free)」實作，以確保右鍵選單保持極速反應。

---

## 📄 License
This project uses **PDFsharp** (MIT License).
