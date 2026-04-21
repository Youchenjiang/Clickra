# ⚡ Windows ContextTools (極速右鍵工具箱)

基於 C# 原生編譯的高效能 Windows 右鍵工具集。專為替換啟動緩慢的 Python 腳本而生，提供近乎零延遲的操作體驗。

[Read English Version](README.md)

---

## 📌 為什麼選擇 ContextTools？

大多數自動化腳本使用 Python 編寫，雖然強大，但每次從右鍵選單執行時，Python 都需要 **1~2 秒的「冷啟動」延遲**（初始化直譯器與載入環境）。對於「將此 PPT 轉為 PDF」這種簡單需求，這段等待時間非常影響手感。

**ContextTools** 使用 C# 撰寫，啟動時間 **小於 0.01 秒**。它能像系統內建功能一樣瞬間反應，讓您的右鍵操作流暢如絲。

---

## 📜 版本演進史

| 版本 | 發布日期 | 關鍵里程碑 |
| :--- | :--- | :--- |
| **v1.0.0** | 2025/12/07 | 初始命令列版本 (Legacy Python 遺產)。 |
| **v2.0.0** | 2026/04/21 | 轉型 C# CLI 並導入「傳送到」選單互動安裝程式。 |
| **v3.0.0** | **當前** | **NativeAOT Shell Extension**。全面支援 Win11 現代右鍵選單與子選單架構。 |

---

## ✨ 功能介紹與操作指南

所有功能皆整合在單一執行檔 `ContextTools.exe` 中，並透過 Windows 的「**傳送到 (SendTo)**」選單提供服務，這能確保一次處理多個檔案時的系統穩定性。

### 1. 簡報轉 PDF (PPT/PPTX to PDF)
- **功能描述**：在背景靜默呼叫 PowerPoint 引擎，將簡報匯出為高品質 PDF。
- **為什麼用「傳送到」？**：傳統右鍵選單若選取 10 份文件，系統會瞬間彈出 10 個視窗導致當機。透過「傳送到」，系統會將這 10 份文件交給同一個 ContextTools 處理，依序且安靜地在同一個視窗完成轉檔。

### 2. PDF 合併 (PDF Merge)
- **功能描述**：將選取的多份 PDF 檔案合併為單一檔案 `Merged_PDF.pdf`。
- **智慧排序**：程式會自動依照「檔名 (A-Z)」重新排列傳入的檔案，確保合併後的順序符合邏輯，不會受選取時的順序影響。

### 3. 圖片合併成 PDF (Images to PDF)
- **功能描述**：將多張圖片（`.jpg`, `.png`, `.webp` 等）直接封裝成一份多頁的 PDF 文件。
- **不損畫質**：保留原始圖片比例與解析度，不進行任何強制壓縮。

### 4. 圖片垂直拼接 (Vertical Image Stitch)
- **功能描述**：將選取的多張圖片垂直「黏合」在一起，生成一張超長圖。
- **自動居中**：若圖片寬度不一，程式會自動以最寬的圖片為準進行水平置中，確保排版美觀。

---

## 🚀 安裝說明

### 第一步：準備執行檔
確保 `ContextTools.exe` 與安裝腳本 `setup_context_menu.ps1` 放在同一個資料夾。

### 第二步：啟動安裝
對著 `setup_context_menu.ps1` 點擊右鍵 ➔ 選擇 **「用 PowerShell 執行」**。

### 第三步：互動式設定
腳本啟動後會出現選單：
1. **安裝 / 更新工具**：設定捷徑並安裝執行檔。
2. **移除工具 (恢復原狀)**：移除所有捷徑與登錄檔項，並可選擇是否刪除安裝資料夾。
3. **退出**：關閉腳本。

在安裝過程中，腳本會詢問 **安裝路徑**。預設會裝在 `%LOCALAPPDATA%\ContextTools`。您可以直接按 **Enter** 接受，或是輸入自訂路徑。

---

## 🛠️ 開發與修改指南

### 專案結構
- `Program.cs`: 核心邏輯。包含指令分配與各項功能實作。
- `ContextTools.csproj`: 專案設定與第三方依賴管理 (`PdfSharp`, `System.Drawing.Common`)。
- `app.ico`: 嵌入在執行檔中的閃電圖示。

### 如何修改
1. 使用 VS Code 或 Visual Studio 開啟資料夾。
2. 修改 `Program.cs` 即可擴充功能。
3. 若需顯示彈出對話框，請使用專載的 `ShowWarning` 輔助函式，它直接呼叫 Win32 底層 API，不依賴龐大的 WinForms 框架。

### 編譯模式
您必須安裝 [.NET SDK](https://dotnet.microsoft.com/download)。

#### 📦 方案甲：極速閃電版（推薦）
產出約 **4MB** 的執行檔。啟動最快、體積最小。需本機安裝有 [.NET Runtime](https://dotnet.microsoft.com/download/dotnet/current/runtime)。
```powershell
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true
```

#### 📦 方案乙：高規相容版
產出約 **75MB** 的執行檔。檔案已內含所有環境，即使是沒裝過 .NET 的電腦也能直接執行。
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## 🗑️ 移除安裝

1. 前往您的安裝目錄（預設為 `%LOCALAPPDATA%\ContextTools`）。
2. 直接刪除該資料夾。
3. 按下 `Win + R` 並輸入 `shell:sendto` 開啟捷徑資料夾。
4. 刪除與 ContextTools 相關的捷徑即可。

---

## 🧠 技術心得 (開發實錄)

在 **NativeAOT** 環境下建構高性能的 Shell Extension 是極具挑戰性的。以下是我們在開發過程中克服的關鍵技術難點：

### 1. NativeAOT 下的手工 COM VTable
由於 NativeAOT 不支援標準的 `.NET COM Interop`，我們必須為 `IExplorerCommand` 和 `IEnumExplorerCommand` 手動構建 VTable。
- **解決方案**：我們實作了 `UniversalObject` 內存結構，將多個介面（Primary, Selection）整合到單一對齊的內存塊中，確保與 Windows Shell 期望的 C 風格對象實現二進位級別的相容。

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

## 📄 授權說明
本專案使用 **PDFsharp** (MIT 授權)。
