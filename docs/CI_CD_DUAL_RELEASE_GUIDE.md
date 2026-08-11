# Clickra CI/CD 雙軌發布流程指南 (Dual-Release Strategy)

本文檔說明 CI/CD (`.github/workflows/release.yml`) 雙軌產出的最佳發布配置。

---

## 1. 雙軌產出架構

| 產物名稱 | 發布目標 | 模式 | 檔案大小 | 特點與適用對象 |
| :--- | :--- | :--- | :--- | :--- |
| **`Clickra-Portable.zip`** | GitHub Releases / 官網 | Framework-Dependent | **~15 MB** | **極輕量**。適合習慣點開即可運行的 GitHub 技術使用者。 |
| **`Clickra.msix`** | Microsoft Store | Framework-Dependent | **~25 MB** | **商店標準格式**。微軟商店會處理相依性與增量更新。 |

---

## 2. GitHub Actions (`release.yml`) 步驟規劃

1. **版本標籤觸發**：當 Push 標籤 `v*` 時啟動工作流程。
2. **打包 Portable Zip**：
   - 執行 `dotnet publish src/Clickra.Fluent/Clickra.Fluent.csproj -c Release --self-contained false`。
   - 將 CLI (NativeAOT)、Shell DLL (NativeAOT) 與 Fluent UI 壓縮為 `Clickra-vX.Y.Z-Portable.zip`。
   - 上傳作為 GitHub Release 的第一附件。
3. **打包 MSIX 套件**：
   - 執行 `powershell -File scripts/build_msix.ps1` 產出 `Clickra.msix`。
   - 將 `Clickra.msix` 上傳作為 GitHub Release 第二附件。
4. **自動提交微軟商店**：
   - 呼叫 `python scripts/publish_store.py` 將 `Clickra.msix` 自動上傳至 Microsoft Store Partner Center。

---

## 3. 微軟商店下載與增量更新機制
- **初次下載**：微軟商店伺服器二次壓縮後，使用者端下載僅約 25 MB 左右。
- **後續更新 (Delta Updates)**：微軟商店使用 Block Map 差分更新，更新新版本時使用者僅需下載改動的幾 MB 程式碼，無需重複下載整體框架。
