# ⚡ Windows ContextTools (極速右鍵工具箱)

基於 C# 原生編譯的高效能 Windows 右鍵工具集。專為替換啟動緩慢的 Python 腳本而生，提供近乎零延遲的操作體驗。

[Read English Version](README.md)

---

## 📌 為什麼選擇 ContextTools？

大多數自動化腳本使用 Python 編寫，雖然強大，但每次從右鍵選單執行時，Python 都需要 **1~2 秒的「冷啟動」延遲**（初始化直譯器與載入環境）。對於「將此 PPT 轉為 PDF」這種簡單需求，這段等待時間非常影響手感。

**ContextTools** 使用 C# 撰寫，啟動時間 **小於 0.01 秒**。它能像系統內建功能一樣瞬間反應，讓您的右鍵操作流暢如絲。

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

## 📄 授權說明
本專案使用 **PDFsharp** (MIT 授權)。
