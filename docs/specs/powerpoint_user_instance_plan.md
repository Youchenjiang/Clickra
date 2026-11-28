# 轉檔時不動使用者開著的 PowerPoint

> 狀態：計畫（2026-11-28 起草），排在 `feat/error-log` 之後（[v4_release_plan.md](v4_release_plan.md) 第 3b 個 PR）；問題由使用者提出，從程式碼推論，**尚未實機重現**（第 2 節先做）。
> 範圍：`Clickra.Core` 的 Office 轉檔（`PowerShellHelper.cs`、`MicrosoftOfficeConversionStrategy.cs`）。3.x 與 4.0 共用，所以舊介面、新介面、右鍵執行都受影響。
> 相關：[office_com_direct_plan.md](office_com_direct_plan.md)（之後改走 COM 時要帶著同一條規則）；`OfficeEngineLock`（PR #256，處理 Clickra 自己的兩件工作，不處理本問題）。

## 1. 問題

PowerPoint 整台電腦只有一個行程。Clickra 轉 PPT 的指令是：

```powershell
$ppt = New-Object -ComObject PowerPoint.Application   # 使用者開著時，拿到的是他那一個
$ppt.DisplayAlerts = 1                                # 1 = 不顯示任何提示
$pres = $ppt.Presentations.Open(檔案, $true, $false, $false)
$pres.SaveAs(輸出, 32)
$pres.Close()
$ppt.Quit()                                           # 關掉整個 PowerPoint
```

使用者正在編輯簡報時，對另一個 PPT 按右鍵轉 PDF（或在主視窗轉），推論會發生：

1. 他的 PowerPoint 被關掉，開著的簡報全部關閉。
2. 因為提示被關了，沒存檔的內容可能不會詢問就消失。
3. 就算沒有關掉，他那個 PowerPoint 的提示設定也被改成不顯示，之後關閉沒存檔的簡報不會再問。

另外，轉檔超過 2 分鐘時 Clickra 會強制結束 PowerShell，`finally` 不會執行：我們開的那份簡報會留在他的 PowerPoint 裡（沒有視窗，看不到），或留下一個看不到的 PowerPoint 行程。

Word 和 Excel 每次 `New-Object` 都是新的行程，和使用者開著的互不相干，沒有第 1～3 點的問題。LibreOffice 每次轉檔用獨立的設定資料夾（`-env:UserInstallation`），也不受影響。

`OfficeEngineLock` 只協調 Clickra 自己的工作，使用者自己開的 PowerPoint 不經過它。

## 2. 先重現（動手修之前）

在 Windows 實機、3.22.11，各做一次並記下結果：

| 步驟 | 記錄 |
| :--- | :--- |
| 開 PowerPoint，新增一份簡報打幾個字、**不存檔**；對另一個 .pptx 右鍵轉 PDF | PowerPoint 有沒有被關掉？有沒有問要不要儲存？沒存的內容還在嗎？ |
| 同上，但簡報已存檔、之後又改了幾個字 | 同上 |
| PowerPoint 開著但停在開始畫面（沒有開任何簡報） | 視窗有沒有被關掉？ |
| 轉完後（若 PowerPoint 還在）關閉一份沒存檔的簡報 | 還會不會問要不要儲存？ |
| PowerPoint 沒開時轉檔 | 轉完後工作管理員有沒有殘留 POWERPNT.EXE？ |

結果寫進 PR 說明。即使實測沒有丟資料（例如 PowerPoint 仍然詢問），關掉使用者的 PowerPoint 與改他的設定仍然要修。

## 3. 規則

**不是 Clickra 啟動的 PowerPoint，Clickra 不關、不改設定、不強制結束。**

1. 取得 PowerPoint 之前先看有沒有 `POWERPNT` 行程在執行。
2. 取得之後判斷「是不是我們啟動的」：之前沒有行程，而且取得時沒有開著的簡報、視窗不可見。三個條件都成立才算。
3. 不是我們啟動的：
   - 不設定 `DisplayAlerts`（也就不需要還原）。
   - 用不開視窗的方式開檔（現在已經是），轉完只關我們開的那一份。
   - 不呼叫 `Quit`。
4. 是我們啟動的：照現在的做法；但呼叫 `Quit` 之前再看一次——這段時間使用者若自己開了 PowerPoint（視窗變成可見，或有其他簡報），就不關，並把 `DisplayAlerts` 設回會提示（2）。
5. 逾時：
   - 我們啟動的：依啟動前後的行程差異找出那個 `POWERPNT` 行程編號並結束它（現在是留下殘留）。
   - 不是我們啟動的：不結束任何 PowerPoint；結果照樣回報逾時。用 `ClickraLog.Warn` 記一筆（「可能有一份看不到的簡報留在 PowerPoint 裡」）。
6. 使用者開著的就是要轉的那個檔案：以唯讀開啟另一份來轉（現在的參數已是唯讀）。轉出的是**磁碟上已存檔的內容**，不含他還沒存的修改；不提示、不擋下。

Word、Excel 順便確認兩點，不改做法：要轉的檔案正被使用者開著時能轉（Word 已是唯讀開啟；Excel 的 `Workbooks.Open` 補上唯讀參數）；逾時後不留下看不到的行程（同第 5 點，只結束我們啟動的那一個）。

## 4. 做法

- `PowerShellHelper.cs` 的 PowerPoint 指令改成依第 3 節；指令輸出一行 `OWNED:0/1` 與（我們啟動時）`PID:<編號>`，讓 C# 端在逾時時知道能不能、以及要結束哪一個行程。
- Word、Excel 的指令同樣輸出 `PID:`，逾時時只結束那一個行程。
- 行程編號的取得：啟動前後各取一次該程式的行程清單，新出現的那一個才算；新出現的不只一個時視為不確定，不結束任何行程。
- `office_com_direct_plan.md` 補上同一條規則，並把「`Quit` 失敗時以程序編號收尾」限定為「只對 Clickra 啟動的行程」。

## 5. 測試

Office 無法在 CI 執行，分兩層：

**自動（Core 測試）**

- 產生的 PowerPoint 指令：`Quit()` 只出現在「我們啟動的」分支內；不是我們啟動時不含 `DisplayAlerts` 的設定。
- 指令輸出解析：`OWNED:1` 加 `PID:` 才允許逾時結束；`OWNED:0`、沒有 `PID:`、`PID:` 不是數字時都不結束任何行程。
- 行程清單差異：新出現 0 個、1 個、2 個時的結果。
- 架構測試：`src/` 不得以程式名稱結束 Office 行程（`Get-Process POWERPNT | Stop-Process`、`Process.GetProcessesByName("POWERPNT")…Kill` 之類），只能結束指令回報的那個編號。

**實機（驗收，重做第 2 節並加上）**

- 開著一份沒存檔的簡報，右鍵轉另一個 PPT：轉成功；原本的簡報還在、內容沒變、PowerPoint 沒有閃一下或跳到前面；之後關閉它仍會問要不要儲存。
- 開著並修改了要轉的那個檔案：轉出的是已存檔的內容，開著的那份不受影響。
- PowerPoint 沒開時轉：轉完沒有殘留行程。
- 轉檔途中使用者自己開啟 PowerPoint：轉完後他的 PowerPoint 還在。
- 兩批 PPT 同時右鍵轉、同時使用者開著 PowerPoint：兩批都成功，使用者的簡報不受影響。
- Word、Excel：開著要轉的檔案時能轉；逾時（用一個會跳對話框的檔案或暫時把逾時調成 5 秒）後沒有殘留行程，使用者開著的 Word、Excel 還在。

## 6. 提交

分支：`fix/powerpoint-user-instance`。

1. `fix(core)`：PowerPoint 指令依第 3 節判斷是否由 Clickra 啟動，不是就不設定提示、不 `Quit`。
2. `fix(core)`：逾時只結束 Clickra 啟動的那個 Office 行程（PowerPoint、Word、Excel）；Excel 以唯讀開啟。
3. `test(core)`：第 5 節的自動測試與架構測試。
4. `docs(docs)`：本計畫狀態（附第 2 節重現結果）、`office_com_direct_plan.md` 的規則。
5. `chore(msix)`：升版號、CHANGELOG（寫明修正內容，讓 Store 更新說明看得到）。
