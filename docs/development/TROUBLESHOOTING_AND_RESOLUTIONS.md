# Clickra 踩坑歷史與故障排除日誌 (Troubleshooting Log)

本文檔詳細紀錄 Clickra 在 Windows 10/11 相容性測試與 MSIX 發布過程中遭遇的關鍵問題與技術解決方案。

---

## 問題列表與修復方案

### 1. 「缺失 .NET 10 執行環境」報錯彈窗
- **現象**：Win10 電腦打開 `Clickra.Fluent.exe` 跳出 `You must install or update .NET to run this application. Required: Microsoft.NETCore.App 10.0.0 (x64)`。
- **原因**：專案原先指定 `net10.0-windows` 且發布模式未開放向下相容。
- **解決方案**：
  1. 將 Target Framework 統一降級至最高普及率的 **.NET 8 LTS** (`net8.0-windows`)。
  2. 開啟 `<RollForward>LatestMajor</RollForward>` 向上相容相容性。

---

### 2. Win10 開啟即閃退 (Access Violation 0xc0000005)
- **現象**：在 Win10 上啟動程式瞬間閃退，且 `C:\Users\user\AppData\Local\Clickra` 目錄完全未建立。
- **原因**：開啟了 `<PublishTrimmed>true</PublishTrimmed>`。WinUI 3 (Windows App SDK) 與 C#/WinRT 映射庫高度依賴未標註的反射與 COM 工廠，剪裁器誤將原生介面連線剔除，導致程式在進入 Managed C# 程式碼 (`App.xaml.cs`) 前即於原生 bootloader 崩潰。
- **解決方案**：在 `Clickra.Fluent.csproj` 中徹底移除 `<PublishTrimmed>` 與 `<TrimMode>` 設定。

---

### 3. PDF 壓縮功能失效 (No FontResolver)
- **現象**：其他轉換功能正常，但 PDF 壓縮功能執行失敗。
- **原因**：[`NativePdfCompressionEngine.cs`](file:///c:/Users/g1014308/Documents/GitHub/Youchen/Clickra/src/Clickra.Core/Processors/FileProcessing/PdfCompressionEngine.cs) 在調用 `PdfSharp` 進行頁面結構與字型最佳化時，未初始化全局字型解析器。
- **解決方案**：在 `NativePdfCompressionEngine.Compress` 開頭新增防禦性初始化：
  ```csharp
  try
  {
      if (PdfSharp.Fonts.GlobalFontSettings.FontResolver == null)
          PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver();
  }
  catch { }
  ```

---

### 4. Windows 10 右鍵選單消失
- **現象**：右鍵選單在 Win11 正常顯示，但在 Win10 檔案總管完全看不到 Clickra。
- **原因**：`AppxManifest.xml` 僅宣告了 Win11 專用的 `desktop4` / `desktop5` (`windows.fileExplorerContextMenus`)，Win10 系統解析時會自動忽略。
- **解決方案**：在 Manifest 中補齊 `desktop9:FileExplorerClassicContextMenuHandler`（`*`, `Directory`, `Directory\Background`）與 `uap:FileTypeAssociation` 傳統檔案關聯宣告。

---

### 5. 「檢視日誌資料夾」開啟後呈現空白 `AppData\Local`
- **現象**：點擊 UI 的「檢視日誌資料夾」時，跳出的檔案總管並未選中 `history.log`，反而退回開在 `AppData\Local`。
- **原因**：MSIX 沙盒重定向導致 C# 程式碼傳遞了容器內部的虛擬路徑給容器外的 `explorer.exe`，`explorer.exe` 找不到硬碟實體路徑而退回顯示上層資料夾。
- **解決方案**：在 `MainPage.xaml.cs` 使用 `Windows.Storage.ApplicationData.Current.LocalFolder.Path` 取得硬碟實體路徑，並傳遞 `/select,".../history.log"` 給 `explorer.exe` 自動呈藍色高亮選取狀態。
