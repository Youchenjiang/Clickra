# Clickra 版本號管理與發布規範 (Release Versioning Guide)

本文件定義了 Clickra 的版本號設計邏輯、微軟商店相容規範，以及發布新版本時必須更新的檔案清單。

---

## 0. 發布清單 (Release Checklist)

> [!WARNING]
> **每次發版前，務必逐項檢查以下清單。** 遺漏任何一項都會導致版本不一致或商店文案過時。

### Step 1：決定版本號
- [ ] 依據 §2 規則決定新版本號（Major / Minor / Patch）
- [ ] 確認 Revision 固定為 `0`

### Step 2：執行 bump_version.ps1（自動更新版本號）
- [ ] 執行 `./scripts/bump_version.ps1 -Type minor`（或 `major` / `patch`）
- [ ] 腳本會自動更新：`Directory.Build.props`、3 份 `AppxManifest.xml`、`CHANGELOG.md`（TODO placeholder）、`README.md` / `README.zh-TW.md`（標題 + 版本表格）、5 份 `StoreListing_*.md`（版本標題）

### Step 3：手動更新文件（腳本無法自動處理的內容）
- [ ] **CHANGELOG.md**：將 `**TODO**: Add changelog entry here` 替換為實際的變更描述（參考 §3.4）
- [ ] **README.md / README.zh-TW.md**：將版本表格中的 `**TODO**: Add milestone description here` 替換為實際描述
- [ ] **docs/ROADMAP.md**：更新里程碑完成狀態（`[ ]` → `[x]`）與進度說明
- [ ] **docs/development/refactor_backlog.md**：更新「發行狀態」行的版本號
- [ ] **LOCAL_BUILD_NOTES.md**：更新架構版本標記（如有）
- [ ] **docs/StoreListing_*.md**（5 語言）：更新 Description、What's new、Product Features、Short description（參考 §3.5）

### Step 4：驗證
- [ ] `dotnet build` 無錯誤
- [ ] 測試全部通過
- [ ] `grep -rn "旧版本號" --include="*.xml" --include="*.props" --include="*.md" . | grep -v CHANGELOG | grep -v README | grep -v ROADMAP` 確認無殘留舊版本號（歷史記錄除外）

### Step 5：提交與推送
- [ ] 原子化提交：版本號升級一個 commit，文件內容更新可分開提交
- [ ] 推送並建立 PR（或直接推送標籤觸發 CI）

---

## 1. 版本號結構與微軟商店限制

Clickra 遵循四位數版本號格式：`Major.Minor.Patch.Revision`（例如 `3.0.8.0`）。

> [!IMPORTANT]
> **微軟商店強制限制**：
> 提交至 Microsoft Store 的 MSIX/APPX 套件，其版本號第四位（Revision）**必須強制為 `0`**。
> 若第四位為非 `0`（例如 `3.0.9.1`），微軟商店合作夥伴中心將拒絕該套件的上傳。

因此，我們的版本號結構定義如下：

$$\text{Version} = \text{Major} . \text{Minor} . \text{Patch} . \mathbf{0}$$

*   **Major (主版本號)**：重大架構重構或定位變更（例如 AOT 轉型、重寫 UI 框架，目前固定為 `3`）。
*   **Minor (次版本號)**：引入全新的功能模組或大型子系統（例如：新增離線 LibreOffice 插件、整套批次命名與資料夾工具）。
*   **Patch (修補/優化版本號)**：現有功能的修正、GUI 佈局修復、細部優化，以及**緊急 Hotfix** 或**商店退件修復**。
*   **Revision (修訂版本號)**：**永遠鎖定為 `0`**。

---

## 2. 升級與修補規則

### 2.1 一般升級路徑
當進行日常開發或功能發布時，依據功能影響範圍遞增 `Minor` 或 `Patch`：
*   **功能優化與 Bug 修正**：`3.0.8.0` ➔ `3.0.9.0`
*   **全新功能模組導入**：`3.0.9.0` ➔ `3.1.0.0`

### 2.2 緊急修補與商店退件處理
若發布某版本（例如 `3.0.9.0`）送審商店被退件，或上線後發現嚴重閃退，**禁止遞增第四位**，必須遞增第三位（Patch）發布新版本：
*   **修補/重新送審**：`3.0.9.0` ➔ `3.0.10.0` ➔ `3.0.11.0`

---

## 3. 發布版本時必須更新的檔案清單 (Files to Update)

> [!CAUTION]
> **此清單是權威來源。** 每次發版前务必逐項核對。遺漏任何檔案都會導致版本不一致。

當要發布/編譯新版本時，必須更新以下所有檔案：

### 3.1 核心專案與編譯配置 (Core & Compilation) — `bump_version.ps1` 自動處理
*   **[Directory.Build.props](../../src/Directory.Build.props)**:
    更新 `<Version>X.Y.Z.0</Version>` 標籤。這會自動套用至所有編譯出來的 C# 二進位檔 (`Clickra.exe`, `ClickraShell.dll` 等)。
*   **[src/resources/AppxManifest.xml](../../src/resources/AppxManifest.xml)**:
    更新 `<Identity ... Version="X.Y.Z.0" />`。此處為本地開發與資源包專用的 Manifest。

### 3.2 MSIX 打包配置 (MSIX Packaging) — `bump_version.ps1` 自動處理
*   **[packaging/msix/AppxManifest.xml](../../packaging/msix/AppxManifest.xml)**:
    更新 `<Identity ... Version="X.Y.Z.0" />`。此為主套件（AOT）的商店 Manifest。
*   **[packaging/msix/AppxManifest.Fluent.xml](../../packaging/msix/AppxManifest.Fluent.xml)**:
    更新 `<Identity ... Version="X.Y.Z.0" />`。此為 Fluent 可選套件的商店 Manifest。

> [!NOTE]
> Clickra 的 `X.Y.Z.0` 應用程式版本與 Windows App SDK 套件版本是兩套不同資料。
> Windows App SDK 版本只在 `src/Clickra.Fluent/Clickra.Fluent.csproj` 更新；
> `scripts/build_msix.ps1` 會依該版本自動對齊打包副本中的
> `Microsoft.WindowsAppRuntime` family 與 `MinVersion`。不要用
> `bump_version.ps1` 管理 Windows App SDK 版本。

### 3.3 專案文件 (Documentation) — `bump_version.ps1` 自動處理標題與版本表格
*   **[README.md](../../README.md)**: 更新標題的 `Clickra vX.Y.Z.0` 以及版本歷史表格。
*   **[README.zh-TW.md](../../README.zh-TW.md)**: 同步更新繁中說明的標題與版本歷史。

### 3.4 專案文件 (Documentation) — 需手動更新
*   **[CHANGELOG.md](../../CHANGELOG.md)**: 腳本會自動插入 TODO placeholder，需手動替換為實際變更描述。
*   **[docs/ROADMAP.md](../ROADMAP.md)**: 更新里程碑完成狀態（`[ ]` → `[x]`）與進度說明。
*   **[docs/development/refactor_backlog.md](refactor_backlog.md)**: 更新「發行狀態」行的版本號。
*   **[LOCAL_BUILD_NOTES.md](../../LOCAL_BUILD_NOTES.md)**: 更新架構版本標記（如有）。

### 3.5 商店文案 (Store Listings) — `bump_version.ps1` 自動更新版本標題，內容需手動更新
*   **[docs/StoreListing_EN.md](../StoreListing_EN.md)**: 英文商店文案。
*   **[docs/StoreListing_ZH.md](../StoreListing_ZH.md)**: 繁體中文商店文案。
*   **[docs/StoreListing_JA.md](../StoreListing_JA.md)**: 日文商店文案。
*   **[docs/StoreListing_KO.md](../StoreListing_KO.md)**: 韓文商店文案。
*   **[docs/StoreListing_ZH-CN.md](../StoreListing_ZH-CN.md)**: 簡體中文商店文案。

> [!WARNING]
> 商店文案的 **Description**、**What's new**、**Product Features**、**Short description** 欄位不會被腳本自動更新。
> 每次發版時，必須手動更新這 5 個檔案中的功能描述，確保與 CHANGELOG 一致。

---

## 4. Git 標籤 (Tag) 與發布規範

*   **標籤命名格式**：必須與版本號完全一致，即 `vX.Y.Z.0`（例如 `v3.0.8.0`）。
*   **Git 標籤指令**：
    ```bash
    git tag v3.0.8.0
    git push origin v3.0.8.0
    ```
*   **注意**：Clickra 專案**禁止直接推送至主要分支或發布分支**（例如 `main` 與 release branches）；開發者可將功能分支推送至 remote 以建立 PR，但正式發布僅允許直接推送 Git 標籤以觸發發布與追蹤。

---

## 5. 重大版本升級案例紀錄 (Major Version Upgrades Case Log)

### 5.1 v3.1.0.0 升級說明 (次版本號變更)
*   **發布日期**：2026/05/30
*   **變更背景**：原定開發版本為 `3.0.10.0`，但在此週期中，我們對視窗基礎行為、進程生命週期以及 UI 互動機制進行了重大的子系統級重構與升級：
    1.  **儀表板單一實例（Mutex）防護子系統**：引入全域具名 Mutex 與 `FindWindow`/`ShowWindow` 視窗喚醒啟動邏輯。
    2.  **進度視窗後台執行系統**：引入進度視窗最小化收納至 Windows 系統匣（Tray Icon）支援，包含即時進度百分比氣泡提示。
    3.  **進程終止與防呆子系統**：實作關閉/取防呆確認對話框，並在取消時自動 tree-kill 背景運作的 Word/PowerPoint 關聯進程。
    4.  **轉換歷史一檔一行（One-Row-Per-File）記錄模組**：將原有的批次 log 行重構為按檔案獨立分列，並擴充細部展開面板。
    5.  **水平滾動與自訂滑動條繪製引擎**：於 Win32/GDI+ 原生繪製自訂水平滾動條與滑動軌跡捕獲，並為過長的路徑與進度狀態文字實作水平滾輪事件。
*   **決策理由**：上述變更並非單純的 GUI 佈局微調或 bug 修正，而是引入了多個全新設計的後台管理與核心 UI 互動子系統，標誌著 Clickra 第一階段「原生儀表板與基礎視窗行為穩定化」的工作圓滿收尾。因此，依據規範，將版本號由 `3.0.9.0` ➔ 晉升至次版本號 `3.1.0.0`。

### 5.2 v3.2.0.0 升級說明 (次版本號變更)
*   **發布日期**：2026/05/31
*   **變更背景**：在此版本中，我們優化了歷史紀錄的排版與寬度自適應邏輯，並對轉檔的目標語系選項進行了大幅度的精簡與優化：
    1.  **儀表板歷史排版與檔名寬度自適應**：實作歷史紀錄排版自適應與動態檔名寬度計算，優化設定介面文字排版以消除重疊。
    2.  **精準「錯誤/取消」狀態判定**：將轉換失敗與使用者主動取消（打叉/關閉進度視窗）之狀態精準區分，解決了使用者中途取消卻被記錄為成功的 bug。
    3.  **目標語系簡化**：移除多餘且暫不可用的 PDF 翻譯目標語言，僅保留下拉式選單及「繁體中文」選項，提升易用性。
*   **決策理由**：本版本精簡了翻譯語系與重構狀態判定邏輯，並對歷史紀錄排版進行了細緻調整，依據使用者需求將版本號遞增為次版本號 `3.2.0.0`。

### 5.3 v3.3.1.0 升級說明 (次版本號變更)
*   **發布日期**：2026/06/17
*   **變更背景**：此版本新增了 `decrypt-pdf` 全新功能模組，並對進度視窗的 UI 架構進行了子系統級重構（原定為 v3.3.0.0，因 CI 發布套件問題推進至 v3.3.1.0）：
    1.  **PDF 密碼解除功能 (decrypt-pdf)**：新增右鍵選單一鍵對 PDF 去除密碼保護，整合 PdfSharpCore 讀取並重寫為無密碼 PDF 的完整流程，並具備加密狀態預檢（未加密檔案直接提示錯誤而不進入密碼輸入流程）。
    2.  **進度視窗內嵌式密碼輸入子系統**：全新設計的跨執行緒 UI 機制，透過 `PostMessageW(WM_USER_SHOW_PASSWORD_INPUT)` 通知 UI 執行緒動態建立 `ES_PASSWORD` Edit 控制項與 OK/Cancel 按鈕，避免彈出式對話框干擾。
    3.  **閃爍修復（WS_CLIPCHILDREN）**：在父視窗加上 `WS_CLIPCHILDREN` 旗標，防止 GDI+ 的 `Paint()` 覆蓋繪製子控制項導致閃爍。
    4.  **輸入修復（TranslateMessage + IsDialogMessageW）**：修正主訊息迴圈中 `TranslateMessage` 與 `IsDialogMessageW` 的呼叫順序，確保 `WM_CHAR` 能正確產生使文字可輸入，同時支援 Enter 確認、Esc 取消的熱鍵行為。
    5.  **bump_version.ps1 UTF-8 無 BOM 修正**：所有腳本的檔案讀寫改用 `[System.IO.File]` API 搭配 `New-Object System.Text.UTF8Encoding($false)`，消除 PowerShell 5.1 預設 ANSI 與 `[System.Text.Encoding]::UTF8` 隱式 BOM 對 Markdown/XML 文件造成的字元污染問題。
*   **決策理由**：`decrypt-pdf` 是全新的文件處理功能模組，且進度視窗的密碼輸入架構屬於子系統級的新設計，已超出單純 UI 修補的範疇，依規範遞增為次版本號 `3.3.0.0`（因 CI 問題實際發布為 `3.3.1.0`）。
