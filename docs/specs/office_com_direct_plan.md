# Office → PDF：改由 Clickra 直接呼叫 COM（不再經過 PowerShell）

| 欄位 | 內容 |
| :--- | :--- |
| **文件狀態** | 計畫 (Planned)，排在 `feat/avalonia-views` 合併之後 |
| **影響範圍** | `Clickra.Core`（3.x 與 4.0 共用）：`Modules/Doc/Shared/Infrastructure` |
| **起草日期** | 2026-11-15 |
| **相關文件** | [libreoffice_engine_plan.md](libreoffice_engine_plan.md)（沒有 Microsoft Office 時的備援引擎） |

## 1. 為什麼要改

用 Microsoft Office 轉 PDF 時，`PowerShellHelper.RunOfficeInteropScript()` 會在背景啟動：

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand <Base64 腳本>
```

「看不見的 PowerShell ＋ 略過執行原則 ＋ 編碼過的腳本」正是惡意程式常見的手法。2026-11-15 實測：使用者電腦上的防護軟體跳出「發現程式試圖操作：利用 PowerShell 執行可疑腳本」，發起程式為 `Clickra.Desktop.exe`。

- 上架 Store 或加上程式碼簽章**無法解決**：簽章只影響 SmartScreen 的信任度，這類防護軟體是看行為。
- 3.x 用同一套 Core，轉 Office 文件時同樣會觸發。
- 每次轉檔還要多花 1–2 秒啟動 PowerShell。

## 2. 做法

由 Clickra 直接透過 COM 控制 Word／Excel／PowerPoint，步驟和現在的腳本一模一樣，只是拿掉 PowerShell 這一層。

### 2.1 各程式的呼叫步驟（沿用現有腳本）

| 程式 | 步驟 |
| :--- | :--- |
| Word | `Word.Application` → `DisplayAlerts = 0` → `Documents.Open(檔案, false, true)` → `ExportAsFixedFormat(輸出, 17)`（17 = PDF）→ `Close(false)` → `Quit()` |
| PowerPoint | `PowerPoint.Application` → `Presentations.Open(檔案, true, false, false)`（唯讀、不開視窗）→ `SaveAs(輸出, 32)`（32 = PDF）→ `Close()` → `Quit()` |
| Excel | `Excel.Application` → `DisplayAlerts = false` → `Workbooks.Open(檔案)` → `ExportAsFixedFormat(0, 輸出)`（0 = PDF）→ `Close(false)` → `Quit()` |

### 2.2 NativeAOT 相容的 COM 呼叫

`dynamic`、`Type.GetTypeFromProgID` ＋ `InvokeMember`、Office PIA（`Microsoft.Office.Interop.*`）都依賴執行時反射，**NativeAOT 不能用**。改用 .NET 8 的原始碼產生式 COM：

1. `CLSIDFromProgID("Word.Application")` 取得 CLSID，`CoCreateInstance`（`CLSCTX_LOCAL_SERVER`）啟動 Office。
2. 宣告 `IDispatch`（`GetIDsOfNames`、`Invoke`）並加上 `[GeneratedComInterface]`，以 `StrategyBasedComWrappers` 取得物件，編譯時產生 AOT 可用的封送程式碼。
3. 包一個共用的小工具 `ComObject`（約 150–200 行，`IDisposable`）：
   - `Get(name)`／`Set(name, value)`／`Call(name, params object[] args)`，內部用 `GetIDsOfNames` 換成 DISPID，再以 `Invoke` 執行。
   - 只需支援 `VARIANT` 的 `VT_BSTR`、`VT_I4`、`VT_BOOL`、`VT_DISPATCH`、`VT_EMPTY`，手動轉換即可。
   - `Dispose` 時釋放 COM 參考，確保 Office 程序能結束。

示意：

```csharp
using var word = ComObject.Create("Word.Application");
word.Set("DisplayAlerts", 0);
using var docs = word.Get("Documents");
using var doc  = docs.Call("Open", path, false, true);
doc.Call("ExportAsFixedFormat", outputPdf, 17);
doc.Call("Close", false);
word.Call("Quit");
```

### 2.3 原本靠 PowerShell 程序擋掉、現在要自己處理的事

| 事項 | 做法 |
| :--- | :--- |
| Office COM 必須在 STA 執行緒 | 每次轉換開一條專用 STA 執行緒，進度與結果回報給呼叫端 |
| 取消與逾時 | 啟動後由 `Application.Hwnd`（Excel／Word）或視窗查詢取得視窗，`GetWindowThreadProcessId` 記下 Office 程序編號；取消或逾時時結束該程序，取代現在的 `process.Kill(true)` |
| Office 殘留在背景 | 每個 COM 物件用完立即 `Dispose`；`Quit` 失敗時同樣以程序編號收尾 |
| 進度 | 取代腳本輸出的 `PROGRESS:20` 等文字，直接在各步驟呼叫 `onProgress` |
| 錯誤 | COM 錯誤（`HRESULT`、`EXCEPINFO`）轉成例外；維持現有 `IsRecoverableMicrosoftOfficeFailure` 的判斷，讓「Office 失敗 → 改用 LibreOffice」的備援照常運作 |
| 密碼保護、損毀檔案 | 對照現有腳本的錯誤處理逐一移植，避免 Office 跳出對話框卡住（`DisplayAlerts`、唯讀開啟） |

## 3. 分支與提交

分支：`refactor/office-com-direct`（`feat/avalonia-views` 合併之後開）。

1. `feat(core)`：新增 `ComObject`（`IDispatch` ＋ `VARIANT` 封送）與 STA 執行器，附單元測試（不需 Office 的部分）。
2. `refactor(core)`：Word 改走直接呼叫；**先在 NativeAOT 發佈後的 exe 實測**可行再繼續。
3. `refactor(core)`：Excel、PowerPoint 改走直接呼叫。
4. `refactor(core)`：移除 `RunOfficeInteropScript` 與三段 PowerShell 腳本、相關語系字串（如 `error_office_powershell_start`）。
5. `test(core)`：加一條架構測試，禁止 Core 再啟動 `powershell.exe`。

## 4. 驗收

- 轉 Word、Excel、PowerPoint 各一份（含中文路徑、含空白的路徑），PDF 內容正確。
- 轉換過程中防護軟體不再跳出警告。
- 轉換中按「取消」與逾時：Office 程序都被結束，工作管理員沒有殘留的 `WINWORD.EXE`／`EXCEL.EXE`／`POWERPNT.EXE`。
- 加密或損毀的檔案：顯示原有的錯誤訊息，不會卡住，且備援 LibreOffice 照常運作。
- NativeAOT 發佈（`-warnaserror`）沒有新增裁剪警告；3.x CLI 與 4.0 桌面端都通過。
- 比較改前改後單檔轉換時間（預期省 1–2 秒）。
