# Clickra v3.6.3.0

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Clickra-blue?style=for-the-badge&logo=microsoft-store)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)

這是一個支援 Windows 10 與 Windows 11 的高性能原生右鍵選單工具套件。採用 C# NativeAOT 技術開發，徹底取代啟動緩慢的 Python 腳本，提供毫秒級的即時響應。

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
| **v3.6.3.0** | 2026/07/09 | **CI/CD 自動化發布與多國語言支援**。新增 GitHub Actions 提交規範檢查、將 Windows 商店上架流程整合至 CI/CD 自動化管線，並為應用程式安裝包與商店頁面完整補全繁中、英文、日文、韓文與簡中 5 國語系原生支援。 |
| **v3.6.2.0** | 2026/07/05 | **SSL/TLS 憑證驗證加強**。移除了 MyMemory 翻譯 API 中非安全的憑證校驗繞過，還原並啟用系統預設 TLS 1.2/1.3 憑證驗證。 |
| **v3.6.1.0** | 2026/07/05 | **PDF 翻譯崩潰修復**。解決 PDF 翻譯管線中，處理包含合字 (ligatures) 的數學公式字元時拋出 IndexOutOfRangeException 導致失敗的問題。 |

[檢視完整版本歷史](CHANGELOG.md)



---

## ✨ 核心功能

### 1. 📂 現代化子選單 (Windows 10/11)
在 Windows 11 中，所有功能皆優雅地收納在 `Clickra` 現代化子選單；Windows 10 則使用相容的傳統右鍵選單整合。

### 2. 📊 原生儀表板 (Native Dashboard)
*   **功能**：採用高效能 Win32 原生開發的深色模式儀表板。
*   **特色**：即時偵測 PDF 引擎與 Office 轉檔引擎狀態。

### 3. 📄 文書轉 PDF (Office to PDF)
*   **功能**：在背景靜默將 Word、Excel 與 PowerPoint 文件轉為高品質 PDF。
*   **引擎選擇**：支援自動、Microsoft Office 與 LibreOffice 三種模式。若未安裝 Microsoft Office，可由 Clickra 下載並管理 LibreOffice 作為免費本機備援引擎。

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

### 9. 📄 PDF 壓縮 (PDF Compression)
*   **功能**：在本地端使用高性能原生引擎壓縮 PDF 檔案，透過文字流簡化、字型去重與圖片降解析大幅縮減檔案體積。
*   **設定介面**：設定頁提供一個極簡、4 停靠點的橫向 Slider 拉條（極小、小檔、標準、高品質），方便即時控制 DPI 與品質參數。
*   **智慧過濾**：自動跳過低解析或小尺寸的圖片，避免流程圖或文字圖表變模糊。


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
├── README.md (或 README.zh-TW.md)  # 專案首頁入口 (產品介紹、安裝、功能)
├── CHANGELOG.md                    # 版本歷史 (所有版本紀錄)
├── PRIVACY.md                      # 隱私權政策 (符合 Windows 應用程式商店合規)
├── LOCAL_BUILD_NOTES.md            # 開發人員指南 (包含本地編譯、腳本、功能擴充與 Git 分支合併規則)
└── docs/
    ├── ROADMAP.md                  # 產品開發路線圖與里程碑
    ├── StoreListing_*.md           # 微軟商店文案與描述資訊
    └── development/
        ├── release_guideline.md    # 版本號管理規範與商店上線檢查清單
        ├── shell_extension_best_practices.md # COM、NativeAOT、記憶體與封裝不變量
        └── shell_diagnostic_guide.md         # Shell 擴充日誌與 Explorer 診斷
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
    *   [Shell 擴充開發最佳實踐](docs/development/shell_extension_best_practices.md) — COM 介面、NativeAOT 記憶體規則與 Sparse Package/MSIX 封裝不變量。
    *   [Shell 擴充故障診斷與偵錯](docs/development/shell_diagnostic_guide.md) — 安全日誌、HRESULT 分流與 Explorer 分階段診斷流程。

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
