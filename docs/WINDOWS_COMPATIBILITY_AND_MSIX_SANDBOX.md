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

## 2. MSIX 沙盒虛擬化與日誌開啟 (UX 體驗優化)

### 2.1 檔案系統虛擬化 (MSIX Container Redirection)
- **現象**：MSIX 包執行時，寫入 `AppData\Local\Clickra` 的檔案會被 Windows 透明重定向至硬碟的實體位置：
  `%LocalAppData%\Packages\Clickra_CBF59877-21AD-4BC4-8F91-FE8DA520A138\LocalCache\Local\Clickra\history.log`
- **問題**：若傳遞虛擬路徑給容器外的 `explorer.exe`，檔案總管找不到虛擬路徑，會退回顯示空無一物的 `%LocalAppData%`。

### 2.2 解決方案 (`MainPage.xaml.cs` 中的 `OpenDataDirAsync`)
透過 WinRT 官方 API 獲取原生實體硬碟路徑，並傳遞 `/select` 參數：

```csharp
private async Task OpenDataDirAsync()
{
    string logPath;
    try
    {
        // 1. 使用 WinRT API 取得硬碟真實實體路徑
        string localPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        logPath = Path.Combine(localPath, "history.log");
    }
    catch
    {
        logPath = Path.Combine(ClickraStorage.GetDataDir(), "history.log");
    }

    if (!File.Exists(logPath))
        File.WriteAllText(logPath, "");

    // 2. 喚醒檔案總管，自動將 history.log 呈藍色高亮選取狀態
    Process.Start(new ProcessStartInfo
    {
        FileName = "explorer.exe",
        Arguments = $"/select,\"{logPath}\"",
        UseShellExecute = true
    })?.Dispose();
}
```
- **使用者體驗**：開啟資料夾時，`history.log` 保持藍色高亮選取，使用者無需在資料夾中尋找或猜測日誌名稱。
