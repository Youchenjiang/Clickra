<div align="center">
<img src="src/resources/app.png" alt="Clickra Logo" width="80" height="80" />
<h1>Clickra</h1>
<p>高效能的 Windows 10 / 11 右鍵選單工具套件</p>
<p>
<a href="https://apps.microsoft.com/detail/9NGLBF6P1KLD"><img src="https://img.shields.io/badge/Microsoft%20Store-Clickra-blue?style=flat-square&logo=microsoft-store" alt="Microsoft Store" /></a>
<a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache%202.0-blue?style=flat-square" alt="License: Apache 2.0" /></a>
</p>
<p>
<a href="README.md">English</a> | <b>繁體中文</b>
</p>
</div>

**Clickra** 是一款專為 Windows 10 與 Windows 11 設計的高性能右鍵選單工具套件，將文書轉檔與 PDF 工具直接帶入檔案總管的右鍵選單。

多數生產力腳本以 Python 撰寫，每次執行都需付出 **1–2 秒的「冷啟動」延遲**。Clickra 改用混合式 Windows 架構：**NativeAOT** 殼層邊界確保選單即時回應、**WinUI 3 Fluent** 儀表板與進度介面，以及 100% 本機處理、不依賴 Python 執行環境。

## 主要功能

### 現代化子選單 (Windows 10/11)
在 Windows 11 中，所有指令皆收納於 `Clickra` 子選單並附在地化圖示；Windows 10 則使用相容的傳統右鍵選單整合。

### Fluent 儀表板
響應式 WinUI 3 儀表板，整合轉換、設定、歷史與診斷，並即時偵測 PDF 與 Office 轉檔引擎狀態。

### 文書轉 PDF
在背景靜默將 Word、Excel 與 PowerPoint 文件轉為高品質 PDF。支援自動、Microsoft Office 與 LibreOffice 三種模式 — 未安裝 Office 時，可由 Clickra 下載並管理 LibreOffice 作為免費本機備援引擎。

### PDF 工具
- **合併** — 依檔名順序將多份 PDF 合併為單一檔案。
- **分割** — 視覺化 PDF 分割器，具頁面預覽（自訂分段、全拆單頁、固定頁數模式）。
- **壓縮** — 高效能原生壓縮，搭配 4 段式品質滑桿。
- **去除密碼** — 直接從右鍵選單解密受保護的 PDF。

### 圖片
- **圖片轉 PDF** — 將 JPG、PNG、WebP 檔案封裝為多頁 PDF，畫質零損失。
- **垂直拼接** — 將多張圖片垂直「黏合」為單一長圖。

### PDF 翻譯
翻譯 PDF 內容並保留版面 — 標題階層、字型樣式、合併表格、固定圖形、圖說與連結 — 同時調整 CJK 字型縮放並重組跨行技術術語。

### 轉換歷史
以 Fluent 主從式介面於本機追蹤每次轉換，顯示檔案路徑、操作類型、時間、耗時與狀態。

## 安裝說明

### 推薦：Microsoft Store（自動更新）
[![Microsoft Store Badge](https://developer.microsoft.com/en-us/store/badges/images/English_get-it-from-MS.png)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)

### 手動安裝：GitHub Release
1. 從 [Releases](../../releases) 下載 `Clickra.msix`。
2. 雙擊即可安裝 — 在 Windows 10+ 上無需任何額外 runtime 相依性。
   - Fluent 介面附加元件可從 AOT Dashboard 設定頁面安裝。

## 本機開發

### 環境需求
- Windows 10 或 11
- .NET SDK 10
- Visual Studio（含 C++ 工作負載，供 NativeAOT 連結使用）

### 建置
```bash
git clone https://github.com/Youchenjiang/Clickra.git
cd Clickra
powershell -File scripts/build_msix.ps1
```

### 測試
```bash
dotnet run --project tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj -c Release
```

完整的環境設定、打包與 Git 流程說明：[LOCAL_BUILD_NOTES.md](LOCAL_BUILD_NOTES.md)

## 貢獻
發起 Pull Request 前，請參閱：
- [LOCAL_BUILD_NOTES.md](LOCAL_BUILD_NOTES.md) — 建置、打包與 Git 流程
- [docs/development/release_guideline.md](docs/development/release_guideline.md) — 版本管理與發布檢查清單
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — NativeAOT／Fluent 架構
- [docs/ROADMAP.md](docs/ROADMAP.md) — 產品路線圖與里程碑
- [CHANGELOG.md](CHANGELOG.md) — 完整版本歷史

## 授權條款
本專案採用 **Apache License 2.0**。核心組件使用 **PDFsharp** (MIT License)。