# ⚡ Clickra v3.0.0

**Clickra** 是一個專為 Windows 11 設計的高性能、原生右鍵選單工具套件。

透過採用 **C# NativeAOT** 技術，Clickra 徹底解決了傳統腳本工具啟動緩慢的問題，提供毫秒級的即時響應體驗，讓檔案處理變得像系統原生功能一樣流暢。

---

## ✨ 核心特性

*   **極速響應**：啟動延遲 < 0.01 秒，體感零延遲。
*   **零依賴 (Zero-Dependency)**：無需安裝 .NET Runtime 或 Python，純原生二進位執行。
*   **情境感知 (Context-Aware)**：智慧偵測選取檔案，只顯示相關功能，保持選單清爽。
*   **工業級穩定性**：支援檔案鎖定自動繞過與無感更新。
*   **專業安裝引擎**：一鍵部署、自動提權、數位簽章與現代選單註冊。

---

## 🛠️ 功能概覽

*   📄 **簡報轉 PDF**：背景靜默呼叫 PowerPoint 引擎，支持多檔批量處理。
*   🔗 **PDF 合併**：極速將多份 PDF 依序合併，自動清理暫存資源。
*   🖼️ **圖片合併成 PDF**：無損封裝多張圖片為多頁 PDF。
*   🎞️ **圖片垂直拼接**：自動對齊並垂直拼接多張圖片，適合製作長圖。

---

## 🚀 快速開始

為了達到極致的簡捷，Clickra 採用了 **「資產隱寫 (Asset Embedding)」** 技術，分發包僅包含兩個核心檔案：

1.  `Clickra.exe` (主程式，內置所有選單組件)
2.  `setup_context_menu.ps1` (智慧安裝腳本)

### 安裝步驟：
1.  下載專案。
2.  右鍵點擊 `setup_context_menu.ps1`，選擇 **「使用 PowerShell 執行」**。
3.  選擇安裝路徑後，腳本將自動完成憑證安裝與選單掛載。

---

## 🧠 技術底層 (The Tech Behind Clickra)

Clickra 是 Windows 開發領域的一次深度探索：
*   **手寫 COM VTables**：在不使用標準 .NET COM Interop 的情況下，手動構建 `IExplorerCommand` 虛擬函數表。
*   **稀疏封裝 (Sparse Packages)**：利用 Windows 11 的現代應用身分識別系統實作右鍵選單擴充。
*   **資源隱寫 (Resource Steganography)**：在編譯期將 DLL 與 Manifest 封裝進執行檔資源，實作「兩檔流」安裝。

---

## 📄 許可協議
本專案使用 MIT License。
核心組件使用 **PDFsharp** (MIT License)。
