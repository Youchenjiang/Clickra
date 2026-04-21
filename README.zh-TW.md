# ⚡ Windows ContextTools v3.0

這是一個專為 Windows 11 設計的高性能原生右鍵選單工具套件。採用 C# NativeAOT 技術開發，徹底取代啟動緩慢的 Python 腳本，提供毫秒級的即時響應。

[English Version (英文版)](README.md)

---

## 📌 為什麼選擇 ContextTools？

大多數生產力腳本（如 PDF 合併、圖片轉檔）通常使用 Python 編寫。雖然開發快速，但 Python 每次執行都有 **1-2 秒的「冷啟動」延遲**。對於右鍵選單這種頻繁操作來說，這幾秒鐘的等待非常破壞節奏。

**ContextTools** 採用 **NativeAOT (原生預編譯)** 技術：
*   **啟動速度 < 0.01 秒**：體感上完全沒有延遲，感覺就像是 Windows 內建的功能。
*   **零依賴**：不需要安裝 .NET Runtime 或 Python 環境，點開即用。
*   **現代外觀**：完全整合進 Windows 11 的現代右鍵選單，支援優雅的「子選單」架構。

---

## 📜 版本演進

| 版本 | 日期 | 關鍵里程碑 |
| :--- | :--- | :--- |
| **v1.0.0** | 2025/12/07 | 初始版本 (Python 遺產)。 |
| **v2.0.0** | 2026/04/21 | 轉型 C# CLI 並導入互動式安裝程式。 |
| **v3.0.0** | **當前** | **NativeAOT Shell Extension**。全面支援 Win11 現代選單與資產隱寫打包。 |

---

## ✨ 核心功能

### 1. 📂 現代化子選單 (Windows 11 Only)
所有功能皆優雅地收納在 `ContextTools (⚡)` 子選單中，避免佔用一級選單空間，保持桌面簡潔。

### 2. 📄 簡報轉 PDF (PPT/PPTX to PDF)
*   **功能**：在背景靜默呼叫 PowerPoint 引擎進行高品質轉檔。
*   **特色**：支援多選檔案，一次處理多個簡報而不會彈出多個視窗。

### 3. 🔗 PDF 合併 (PDF Merge)
*   **功能**：將選取的多份 PDF 依照檔名順序合併為單一檔案。
*   **特色**：極速處理，並自動清理臨時資源。

### 4. 🖼️ 圖片轉 PDF (Images to PDF)
*   **功能**：將多張圖片（JPG, PNG, WebP 等）直接封裝成一份多頁 PDF。
*   **特色**：不損畫質，保留原始解析度。

### 5. 🎞️ 圖片垂直拼接 (Image Stitch)
*   **功能**：將多張圖片垂直「黏合」成一張超長圖。
*   **特色**：自動對齊，適合製作長圖或網頁截圖拼接。

---

## 🚀 專業安裝流程 (兩檔流)

為了達到極致的簡潔，我們採用了 **「資產隱寫 (Asset Embedding)」** 技術。您的分發包中只需要有：
1.  `ContextTools.exe` (主程式，內置所有選單資源)
2.  `setup_context_menu.ps1` (智慧安裝腳本)

### 安裝步驟：
1.  右鍵點擊 `setup_context_menu.ps1`，選擇 **「使用 PowerShell 執行」**。
2.  **選擇路徑**：您可以選擇預設路徑或輸入自訂的安裝位置。
3.  **自動配置**：腳本會自動提取內置資源、註冊身分、安裝數位憑證並完成選單掛載。

### 移除步驟：
執行腳本並選擇 **「2. 移除工具」**，系統將自動清理註冊資訊並移除安裝資料夾。

---

## 🛠️ 開發者與技術架構

### 專案結構
- `src/ContextTools.CLI`: 負責檔案處理的核心邏輯。
- `src/ContextToolsShell`: 採用 NativeAOT 實作的 COM 選單擴展組件。
- `src/resources`: 存放身分宣告 (Manifest) 與圖標資產。

### 核心黑科技：資產隱寫部署
我們在 `ContextTools.exe` 中嵌入了 `AppxManifest.xml`、`app.png` 以及 `ContextToolsShell.dll`。當執行 `--deploy` 指令時，主程式會自動釋放這些組件，實現真正的「零外部依賴」分發。

---

## 🧠 技術深度分享 (開發血淚史)

在 **NativeAOT** 下開發 Shell Extension 是一場與 Windows 底層的博弈：

### 1. 手寫 COM VTables
NativeAOT 不支援標準的 `.NET COM Interop`。我們必須手動構建 `IExplorerCommand` 的虛擬函數表 (VTable)。
*   **解法**：使用 `UniversalObject` 記憶體結構，將多個介面（Primary, Selection）整合進一個對齊的區塊，確保二進位級別的相容性。

### 2. Windows 11 「影子介面」
標準文件建議實作 `IExplorerCommand` 時使用官方 GUID。但 Windows 11 經常會詢問一些未公開的 **「影子 GUID」**。如果不支援這些 GUID，子選單的箭頭將無法顯示。

### 3. VTable 槽位與堆疊平衡
由於擴充功能運行在 `explorer.exe` 進程內，任何參數不匹配（例如 2 參數方法誤寫為 1 參數）都會導致堆疊失衡，進而引發整台電腦的檔案總管瞬間崩潰。

---

## 📄 許可協議
本專案使用 **PDFsharp** (MIT License)。
