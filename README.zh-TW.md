# Clickra v3.3.1.0

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
| **v3.0.8.0** | 2026/05/21 | **轉換歷史紀錄與儀表板優化**。實作本地轉檔歷史紀錄追蹤與原生 Win32 列表 UI，整合快速轉檔分頁，新增多國語系切換 (zh-TW/en-US)，並將 `DashboardForm` 重構拆分為靜態 Partial 類別檔案。 |
| **v3.0.9.0** | 2026/05/26 | **關於分頁、完整多語系與儀表板細項優化**。新增關於分頁（專案說明、協作連結、一鍵 Gmail 診斷回報）。儀表板與右鍵選單完整支援 ja-JP、ko-KR、zh-CN。新增最小化至系統匣、自訂輸出路徑資料夾選取器、可展開歷史紀錄卡片（含計時與輸入輸出路徑）。實作視窗最大化自適應佈局、高 DPI 顯示清晰度修正，以及快速轉檔的「檔案優先」智慧交互流程。動態側邊欄寬度、首次啟動語言字型正規化，以及 NativeAOT 相容性修正（`[STAThread]`、`GetModuleHandle`）。 |
| **v3.1.0.0** | 2026/05/30 | **儀表板穩定化、進度視窗收納系統匣與水平滑動條**。實作主 Dashboard 單一實例 (Mutex) 檢查、進度視窗最小化至系統匣、進度視窗取消/關閉確認防呆與關聯進程中止、轉換歷史細緻化拆分（一檔一行並顯示個別檔名）、單個 PDF 隱藏選單，並支援進度訊息水平滾動與滑鼠拖曳滑動條。 |
| **v3.2.0.0** | 2026/05/31 | **儀表板歷史排版優化、選單目標語言簡化與錯誤判定邏輯修正**。實作歷史紀錄排版自適應與檔名寬度自適應、歷史紀錄欄錯誤狀態「錯誤/取消」精準化判定、移除多餘 PDF 翻譯目標語言，並優化設定介面文字排版消除重疊。 |
| **v3.3.0.0** | **當前**   | **PDF 去除密碼與內嵌密碼輸入**。新增高效能 PDF 密碼解除功能，支援直接從右鍵選單進行解密。於進度視窗中實作無閃爍的內嵌式密碼輸入框與確認按鈕，免除傳統彈出式對話框的干擾；並具備加密狀態預檢機制，防呆避免對未加密檔案重複要求輸入。 |



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

### 8. 🔓 PDF 去除密碼 (Remove PDF Password)
*   **功能**：直接在右鍵選單對受密碼保護的 PDF 檔案進行解密並生成無密碼版本。
*   **特色**：於 GDI+ 進度視窗內建無閃爍的內嵌密碼輸入介面，輸入過程流暢且免去彈出視窗打斷體驗。
*   **安全**：自動偵測檔案加密狀態，若檔案本身未加密則會提示無須解密以維護系統安全性。


---

## 🛠️ 安裝說明

### 推薦方法：Microsoft Store (自動更新)
[![Microsoft Store Badge](https://developer.microsoft.com/en-us/store/badges/images/English_get-it-from-MS.png)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)

### 手動安裝 (GitHub Release)
1.  從 [Releases](../../releases) 下載最新的 `Clickra.msix`。
2.  雙擊檔案進行安裝。

---

## 📂 專案文件導覽 (Documentation Directory)

為了保持主說明的簡潔，Clickra 的開發與技術文件皆已分類至獨立導覽中。您可以參考下方的分支圖結構進行查閱：

```text
Clickra/
├── README.md (或 README.zh-TW.md)  # 專案首頁入口 (產品介紹、安裝、功能、歷史版本)
├── PRIVACY.md                      # 隱私權政策 (符合 Windows 應用程式商店合規)
├── LOCAL_BUILD_NOTES.md            # 開發人員指南 (包含本地編譯、腳本、功能擴充與 Git 分支合併規則)
└── docs/
    ├── ROADMAP.md                  # 產品開發路線圖與里程碑
    ├── StoreListing_*.md           # 微軟商店文案與描述資訊
    └── development/
        ├── release_guideline.md    # 版本號管理規範與商店上線檢查清單
        ├── shell_extension_best_practices.md # Native COM 與 Shell Extension 開發規範
        └── shell_diagnostic_guide.md         # Shell 擴充故障診斷與偵錯日誌
```

### 文件導覽連結
*   **產品與公開文件**：
    *   [產品路線圖](docs/ROADMAP.md) — 了解未來版本的功能規劃與里程碑進度。
    *   [隱私權政策](PRIVACY.md) — 符合 Windows 應用程式商店合規之隱私聲明。
    *   [商店描述資訊](docs/StoreListing_ZH.md) — 微軟商店中的詳細功能描述文案。
*   **開發人員核心指南（新進開發者起點）**：
    *   [LOCAL_BUILD_NOTES.md](LOCAL_BUILD_NOTES.md) — 包含詳細的本地端編譯步驟、自動化打包腳本、功能擴充方法，以及開發分支合併規則。
*   **進階開發與發布專題**：
    *   [版本管理與發布規範](docs/development/release_guideline.md) — 定義四位數版本限制、發布時需更新之檔案清單與 Git Tag 規定。
    *   [Shell 擴充開發最佳實踐](docs/development/shell_extension_best_practices.md) — COM 生命週期、NativeAOT 記憶體管理與 Sparse Package 測試注意事項。
    *   [Shell 擴充故障診斷與偵錯](docs/development/shell_diagnostic_guide.md) — 如何透過輕量日誌捕獲 `QueryInterface` 失敗的 IID 以及 COM 加載異常。

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
