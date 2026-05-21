# Clickra v3.0.8.0

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Clickra-blue?style=for-the-badge&logo=microsoft-store)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)

這是一個專為 Windows 11 設計的高性能原生右鍵選單工具套件。採用 C# NativeAOT 技術開發，徹底取代啟動緩慢的 Python 腳本，提供毫秒級的即時響應。

[English Version (英文版)](README.md)

---

## 📌 為什麼選擇 Clickra？

大多數生產力腳本（如 PDF 合併、圖片轉檔）通常使用 Python 編寫。雖然開發快速，但 Python 每次執行都有 **1-2 秒的「冷啟動」延遲**。對於右鍵選單這種頻繁操作來說，這幾秒鐘的等待非常破壞節奏。

**Clickra** 採用 **NativeAOT (原生預編譯)** 技術：
*   **啟動速度 < 0.01 秒**：體感上完全沒有延遲，感覺就像是 Windows 內建的功能。
*   **零依賴**：不需要安裝 .NET Runtime 或 Python 環境，點開即用。
*   **現代外觀**：完全整合進 Windows 11 的現代右鍵選單，支援優雅的「子選單」架構。

---

## 📜 版本演進

| 版本       | 日期       | 關鍵里程碑                                                             |
| :--------- | :--------- | :--------------------------------------------------------------------- |
| **v1.0.0** | 2025/12/07 | 初始版本 (Python 遺產)。                                               |
| **v2.0.0** | 2026/04/21 | 轉型 C# CLI 並導入互動式安裝程式。                                     |
| **v3.0.0** | 2026/04/24 | **NativeAOT Shell Extension**。全面支援 Win11 現代選單與資產隱寫打包。 |
| **v3.0.1** | 2026/04/25 | 功能解耦與開發規範。拆分圖片處理邏輯，並導入 AI 自動化。         |
| **v3.0.2**   | 2026/05/05 | **跨版本穩定性修復**。修復 Win10/11 安裝相容性，消除報錯紅字。 |
| **v3.0.3.0** | 2026/05/07 | **商店合規性更新**。修正版本號修訂編號規範。 |
| **v3.0.4.0** | 2026/05/11 | **關鍵 Shell 修復**。解決 Windows 11 右鍵選單顯示問題（商店政策 10.1.2.10），支援系統特定 IID 並同步 Manifest CLSID。 |
| **v3.0.5.0** | 2026/05/13 | **診斷與相容性修復**。強化 PPT 報錯訊息並符合商店披露要求。 |
| **v3.0.6.0** | 2026/05/15 | **原生儀表板與 Word 轉 PDF**。實作高性能 Win32 儀表板，整合 Word 轉換引擎，並達成 100% NativeAOT 架構。 |
| **v3.0.7.0** | 2026/05/21 | **動態進度條與完成通知**。實作純 Win32/GDI+ 動態進度視窗，支援 WinUI 3 級別流動光暈動畫、系統主色調連動與原生 Toast 通知。 |
| **v3.0.8.0** | **當前**   | **轉換歷史紀錄與儀表板優化**。實作本地轉檔歷史紀錄追蹤與原生 Win32 列表 UI，整合快速轉檔分頁，新增多國語系切換 (zh-TW/en-US)，並將 `DashboardForm` 重構拆分為靜態 Partial 類別檔案。 |



---

## ✨ 核心功能

### 1. 📂 現代化子選單 (Windows 11 Only)
所有功能皆優雅地收納在 `Clickra` 子選單中，避免佔用一級選單空間，保持桌面簡潔。

### 2. 📊 原生儀表板 (Native Dashboard)
*   **功能**：採用高效能 Win32 原生開發的深色模式儀表板。
*   **特色**：即時偵測 PDF 引擎、Microsoft Word 與 PowerPoint 的安裝狀態。

### 3. 📄 文書轉 PDF (Word & PPT to PDF)
*   **功能**：在背景靜默呼叫 Office 引擎進行高品質轉檔。
*   **需求**：此功能需本地安裝對應的 Microsoft Office 軟體。

### 4. 🔗 PDF 合併 (PDF Merge)
*   **功能**：將選取的多份 PDF 依照檔名順序合併為單一檔案。
*   **特色**：極速處理，並自動清理臨時資源。

### 5. 🖼️ 圖片轉 PDF (Images to PDF)
*   **功能**：將多張圖片（JPG, PNG, WebP 等）直接封裝成一份多頁 PDF。
*   **特色**：不損畫質，保留原始解析度。

### 6. 🎞️ 圖片垂直拼接 (Image Stitch)
*   **功能**：將多張圖片垂直「黏合」成一張超長圖。
*   **特色**：自動對齊，適合製作長圖或網頁截圖拼接。

### 7. 🕒 轉換歷史記錄 (Conversion History)
*   **功能**：本地追蹤並顯示所有的轉檔操作歷史。
*   **特色**：採用高性能原生繪製的歷史紀錄列表，呈現轉換的檔案路徑、操作類型、時間戳記與成功/失敗狀態。


---

## 🛠️ 安裝說明

### 推薦方法：Microsoft Store (自動更新)
[![Microsoft Store Badge](https://developer.microsoft.com/en-us/store/badges/images/English_get-it-from-MS.png)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)

### 手動安裝 (GitHub Release)
1.  從 [Releases](../../releases) 下載最新的 `Clickra.msix`。
2.  雙擊檔案進行安裝。

---

### 2. 編譯指令 (How to Build)
由於採用了資產隱寫技術，編譯必須分為兩個階段：

**第一階段：編譯選單組件 (DLL)**
```powershell
dotnet publish src\ClickraShell\ClickraShell.csproj -c Release -r win-x64 -p:PublishAot=true --output .
```

**第二階段：編譯主程式 (CLI)**
這會將產出的 DLL 以及 `src/resources` 中的資產封裝進執行檔，並採用 NativeAOT 達成零依賴：
```powershell
dotnet publish src\Clickra.CLI\Clickra.csproj -c Release -r win-x64 --output .
```

### 3. 如何增加新功能
1.  **核心邏輯**：在 `src/Clickra.CLI/Program.cs` 中增加新的命令處理分支。
2.  **選單介面**：修改 `src/ClickraShell/ShellExtension.cs` 中的 `SubTitles` 與 `SubArgs` 陣列。
3.  **重新編譯**：按照上述編譯順序重新產出 `Clickra.exe` 即可。

### 4. 開發與分支規範 (Development Workflow)
為確保主分支 (`main`) 永遠處於穩定可發布的狀態，本專案禁止直接推送程式碼至 `main`。所有變更皆須透過 **Pull Request (PR)** 合併。

*   **功能開發 (`feature/...`)**：所有新功能或一般修復，請從 `main` 切出 `feature/分支名稱`。
*   **緊急修復 (`hotfix/...`)**：針對已上線版本的重大 Bug，請切出 `hotfix/分支名稱`。
*   **合併規則**：開發完成後，推送到遠端並開啟 PR。審查無誤後即可合併進 `main`。
*   **Git 標籤與發布 (Git Tags)**：版本更新後，請於本地端建立 Git Tag（格式為 `vX.Y.Z.0`）。推送時僅能直接推送該 Tag（例如 `git push origin vX.Y.Z.0`），禁止直接推送分支至遠端主分支。

### 5. 自動化版本更新與 MSIX 封裝 (Automated Packaging)
專案內建 PowerShell 腳本，可自動執行版本升級與 MSIX 封裝：

*   **升級版本並重新打包**：
    ```powershell
    powershell -File scripts/bump_version.ps1 -Type <major|minor|patch|revision> -Build
    ```
    *   `-Type`：指定要遞增的版本號層級（例如 `patch` 會將 `3.0.6.0` 升級至 `3.0.7.0`）。
    *   `-Build`：升級版本後，自動執行 Native AOT 兩階段編譯、PRI 資源編譯與憑證簽章，直接在根目錄產生最新版 `Clickra.msix`。

*   **單獨封裝 MSIX**：
    若只想重新編譯打包而不變更版本號：
    ```powershell
    powershell -File scripts/build_msix.ps1
    ```

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
本專案使用 **Apache License 2.0**。
核心組件使用 **PDFsharp** (MIT License)。
