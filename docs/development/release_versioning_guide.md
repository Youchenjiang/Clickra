# Clickra 版本號管理與發布規範 (Release Versioning Guide)

本文件定義了 Clickra 的版本號設計邏輯、微軟商店相容規範，以及發布新版本時必須更新的檔案清單。

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

當要發布/編譯新版本時，必須更新以下檔案中的版本號字串：

### 3.1 核心專案與編譯配置 (Core & Compilation)
*   **[Directory.Build.props](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/src/Directory.Build.props)**:
    更新 `<Version>X.Y.Z.0</Version>` 標籤。這會自動套用至所有編譯出來的 C# 二進位檔 (`Clickra.exe`, `ClickraShell.dll` 等)。
*   **[src/resources/AppxManifest.xml](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/src/resources/AppxManifest.xml)**:
    更新 `<Identity ... Version="X.Y.Z.0" />`。此處為本地開發與資源包專用的 Manifest。

### 3.2 MSIX 打包配置 (MSIX Packaging)
*   **[packaging/msix/AppxManifest.xml](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/packaging/msix/AppxManifest.xml)**:
    更新 `<Identity ... Version="X.Y.Z.0" />`。此為最終打包成 `.msix` 用於商店發布的核心 Manifest。

### 3.3 專案文件 (Documentation)
*   **[README.md](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/README.md)**: 更新標題的 `Clickra vX.Y.Z.0` 以及版本歷史表格。
*   **[README.zh-TW.md](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/README.zh-TW.md)**: 同步更新繁中說明的標題與版本歷史。
*   **[docs/ROADMAP.md](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/docs/ROADMAP.md)**: 更新里程碑狀態與對應的版本號。

---

## 4. Git 標籤 (Tag) 與發布規範

*   **標籤命名格式**：必須與版本號完全一致，即 `vX.Y.Z.0`（例如 `v3.0.8.0`）。
*   **Git 標籤指令**：
    ```bash
    git tag v3.0.8.0
    git push origin v3.0.8.0
    ```
*   **注意**：Clickra 專案禁止直接向 remote 推送分支（`git push`），僅允許直接推送 Git 標籤以觸發發布與追蹤。
