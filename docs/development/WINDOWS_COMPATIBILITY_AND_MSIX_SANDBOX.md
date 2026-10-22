# Windows 10/11 相容性與 MSIX 沙盒重定向指南

## 1. Windows 10 vs Windows 11 右鍵選單相容性

### 1.1 問題根源
- Windows 11 使用新版 Modern Context Menu API（`desktop4` / `desktop5` 的 `windows.fileExplorerContextMenus`）。
- **Windows 10 檔案資源管理員會直接忽略 `desktop4` / `desktop5` 標籤**。

### 1.2 相容解決方案 (`packaging/msix/AppxManifest.xml`)
Manifest 中已配置雙軌宣告：
1. **Win11 Modern 選單**：保留 `desktop4:FileExplorerContextMenus`。
2. **Win10 20H2+ / Win11 傳統選單**：`desktop9:FileExplorerClassicContextMenuHandler` 包含 `*`、`Directory` 與 `Directory\Background`。
3. **Win10 傳統檔案關聯**：新增 `uap:FileTypeAssociation` 宣告，覆蓋常見副檔名（`.pdf`, `.docx`, `.doc`, `.pptx`, `.ppt`, `.xlsx`, `.xls`, `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`）。

---

## 2. Shared data path and packaged execution

### 2.1 Canonical Clickra data directory

`ClickraStorage` is the storage authority for shared Clickra state. Unless tests/portable
execution set `CLICKRA_DATA_DIR`, it resolves the data directory as:

`%LOCALAPPDATA%\Clickra`

`settings.conf`, `history.log`, and related shared records must be derived from that contract.
Do **not** hard-code a package-family `LocalCache` path or assume that MSIX execution implies a
specific redirection layout; package identity/path details are not a stable substitute for
`ClickraStorage.GetDataDir()`.

### 2.2 Fluent folder-opening behavior

`MainPage.OpenDataDirAsync` currently probes `ApplicationData.Current.LocalFolder` when running
with package identity and falls back to `ClickraStorage.GetDataDir()`. That helper is UI behavior,
not the canonical persistence contract. Changes to folder-opening UX must verify that the selected
file corresponds to the same data used by `ClickraStorage` instead of inventing a second storage
location.

When opening the data folder through `explorer.exe`, pass a real filesystem path and use `/select`
only after the target file/path has been resolved. The goal is to highlight the actual Clickra
record without encoding PFN-specific paths in application logic or documentation.
