# 錯誤日誌：出問題時留下查得到原因的紀錄

> 狀態：計畫（2026-11-27 起草）
> 對應：[v4_cross_platform_master_plan.md](v4_cross_platform_master_plan.md)〈4.0 上線前清單〉；[v4_desktop_ui_layout.md](v4_desktop_ui_layout.md) 設定頁「關於」的「查看日誌」與「寄送問題回報」。
> 範圍：Core 新增一個寫日誌的元件，4.0 桌面端、右鍵執行（CLI）共用。舊介面（Fluent、經典儀表板）不改畫面，只跟著 Core 寫進同一個日誌。

## 1. 現況

- 設定頁的「查看日誌」打開 `%LOCALAPPDATA%\Clickra\logs`，但沒有任何程式寫進這個資料夾，所以一定是空的（2026-11-26 發現）。
- `history.log` 只記哪個任務成功或失敗，以及一句錯誤訊息，沒有錯誤的細節。
- 細節散落或直接遺失：
  - Office、LibreOffice 的結束碼與輸出只放在內層例外裡，畫面上的訊息已經改成「Word 無法處理這個檔案」，結束碼沒有地方留下來。
  - 翻譯失敗另外寫在 `translate_errors.log`。
  - 任務紀錄寫入失敗只印到 `Console.Error`，桌面端和右鍵執行都看不到。
  - 桌面端沒有攔截未處理的例外，程式當掉時什麼都沒留下。
- 「寄送問題回報」請使用者附上 `history.log`，但它幫不上忙。

## 2. 目標

1. 任何處理失敗、程式當掉，都能在日誌裡找到：哪個動作、第幾個檔案、例外的種類與訊息、內層例外、堆疊，以及外部程式的結束碼與輸出。
2. 日誌不含個人資料：不記完整路徑、不記密碼與 API 金鑰、不記文件內容或翻譯文字。
3. 寫日誌永遠不會讓處理失敗或變慢到使用者感覺得到。
4. 日誌自己會清理，不會越長越大。
5. 「查看日誌」與「寄送問題回報」指向這個日誌。

不在範圍內：上傳日誌到伺服器（Clickra 不連網回報）；使用者可調的日誌等級設定。

## 3. 位置與格式

- 資料夾：`%LOCALAPPDATA%\Clickra\logs\`（`StorageRuntime.LogsDirectory`），測試時跟著 `CLICKRA_DATA_DIR` 走。
- 檔名：`clickra-2026-11-27.log`，一天一個；單一檔案超過 5 MB 時，當天改寫 `clickra-2026-11-27.1.log`，以此類推。
- 保留 14 天：程式啟動時刪除 14 天前的檔案（和解鎖暫存檔的殘留清理放在一起）。
- 編碼 UTF-8（無 BOM），換行 CRLF，方便使用者用記事本打開。
- 每筆一行開頭，固定欄位，例外細節縮排接在下面：

```
2026-11-27 01:13:05.123 +08:00 ERROR [desktop 1234] compress task=7f3a file=2/5 "報告.pdf"
  Word 無法處理這個檔案
  System.Runtime.InteropServices.COMException (0x800706BA): The RPC server is unavailable.
     at Clickra.Core.Modules.Doc.Office.OfficeConverter.ConvertAsync(...)
  --- inner: Clickra.Core.Modules.Doc.Office.OfficeExitException: exit code 1 (0x1)
  stderr (前 2 KB): ...
```

- 第一行欄位：時間（含時區）、等級、來源與行程編號（`desktop`、`cli`、`fluent`、`dashboard`）、動作、任務編號前 4 碼、檔案序號與檔名。
- 等級只有三種：
  - `INFO`：程式啟動（版本、作業系統、介面語言）、任務開始（動作、檔案數、選項摘要）與結束（成功、失敗、略過的數量與耗時）。
  - `WARN`：自動改用備援（Office 失敗改用 LibreOffice、翻譯服務限流後重試、Google 翻譯備援）、任務紀錄寫入失敗、清理失敗。
  - `ERROR`：檔案處理失敗、任務失敗、未處理的例外。

## 4. 不記錄的內容

| 內容 | 怎麼處理 |
| :--- | :--- |
| 完整路徑 | 只記檔名與副檔名；資料夾一律不記（路徑常含使用者名稱與公司資料夾名稱） |
| 例外訊息裡的路徑 | 寫入前把訊息中出現的這次任務的輸入、輸出路徑換成檔名；其他看起來像 `X:\…` 或 `\\server\…` 的路徑換成 `<path>\檔名` |
| 密碼 | 寫入前把這次任務 `ConversionPasswordCache` 裡的每組密碼換成 `***` |
| API 金鑰、自訂 AI 服務網址的查詢字串 | 寫入前把設定裡的金鑰換成 `***`；網址只記主機名稱 |
| 文件內容、翻譯前後的文字 | 不記；翻譯錯誤只記頁碼、段落數與服務回的錯誤代碼 |
| 選項 | 只記選項名稱與值；名稱含 `password`、`key`、`token` 的一律記成 `***` |

遮蔽由日誌元件統一做（第 5 節的 `Redact`），呼叫的地方不用各自處理。

## 5. 做法

### 5.1 Core：`ClickraLog`

- 位置：`Clickra.Core/Diagnostics/ClickraLog.cs`（新命名空間 `Clickra.Core.Diagnostics`）。
- 介面：
  - `ClickraLog.Info(string area, string message)`
  - `ClickraLog.Warn(string area, string message, Exception? ex = null)`
  - `ClickraLog.Error(string area, string message, Exception? ex = null)`
  - `ClickraLog.BeginTask(...)` 回傳一個 `IDisposable` 的範圍，帶動作、任務編號、遮蔽清單（這次的路徑與密碼）；範圍內的每一筆自動帶上這些欄位與遮蔽。用 `AsyncLocal` 傳遞，逐檔處理時更新目前的檔案序號與檔名。
  - `ClickraLog.Source`：啟動時設一次（`desktop`、`cli` 等）。
- 例外展開：依序寫出例外與每一層內層例外的型別、`HResult`、訊息、堆疊；`AggregateException` 展開每一個；外部程式的例外帶結束碼與輸出（截到 2 KB）。
- 寫入：
  - 用具名 mutex（`Local\ClickraLogMutex_v1`）保護附加寫入，讓桌面端與同時執行的多個右鍵行程不會交錯；等不到 mutex 超過 500 毫秒就放棄這一筆。
  - 呼叫端不等寫檔：先放進行程內的佇列，由背景執行緒寫；程式結束時（`AppDomain.ProcessExit`）把佇列寫完，最多等 1 秒。
  - 任何寫入錯誤都吞掉，不丟例外、不重試、不寫 `Console.Error`。
- 遮蔽：`ClickraLog.Redact(string text, RedactionContext ctx)` 為純函式，方便測試。

### 5.2 要寫日誌的地方

| 地方 | 等級與內容 |
| :--- | :--- |
| `ConversionTaskLifecycle` 的 `catch (Exception ex)` | `ERROR`，完整例外；取消不記錯誤，只在任務結束那筆寫「已停止」 |
| `PerFileConversionUseCaseBase` 每個檔案失敗與略過 | 失敗 `ERROR`，略過 `INFO`（原因：需要密碼、沒有密碼） |
| `ConversionTaskLifecycle.RunStorage` | `WARN`，取代 `Console.Error.WriteLine` |
| Office、LibreOffice 轉檔 | 失敗 `ERROR` 帶結束碼與輸出；改用 LibreOffice 時 `WARN` |
| 翻譯 | 取代 `translate_errors.log`：頁面翻譯失敗 `ERROR`，限流重試與備援 `WARN`；舊檔不再寫入，也不刪除 |
| 列印 | 印表機無法使用、送印失敗 `ERROR` |
| 桌面端 `App` | `AppDomain.CurrentDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`、Avalonia `Dispatcher.UIThread.UnhandledException` 都寫 `ERROR`；啟動寫 `INFO` |
| CLI 右鍵執行 | `ClickraStartup` 現有的 `Debug.WriteLine` 改寫日誌；啟動與結束寫 `INFO` |
| 清理（任務紀錄、解鎖暫存、日誌本身） | 失敗 `WARN` |

各動作的 use case 不用自己寫日誌，由上面的共用處理統一寫，避免重複。

### 5.3 介面

- 「查看日誌」：打開 `logs` 資料夾並選取今天的檔案；今天還沒有檔案時選取最新的一個；資料夾是空的就只打開資料夾。不再建立空檔。
- 「寄送問題回報」：郵件說明改成請使用者附上「已幫你選好的日誌檔」，同時打開 `logs` 資料夾並選取最新的檔案。
- 介面文字（定稿前經使用者確認）：
  - 郵件說明：「請把已經幫你選好的日誌檔拖到這封信裡。日誌只記錄檔名和錯誤原因，不含檔案內容與密碼。」
- 舊介面的「查看日誌」維持打開 `history.log`，4.0 退場時一起移除。

## 6. 分支與提交

分支：`feat/error-log`，從最新 `main` 開。

1. `feat(core)`：`ClickraLog`、`RedactionContext`、例外展開、佇列寫入、mutex、輪替與 14 天清理，附單元測試。
2. `feat(core)`：轉檔流程接上日誌（任務開始與結束、逐檔失敗與略過、任務紀錄寫入失敗）。
3. `refactor(core)`：Office、LibreOffice、翻譯、列印改寫日誌；拿掉 `translate_errors.log` 的寫入。
4. `feat(shell)`：桌面端攔截未處理的例外、啟動紀錄、「查看日誌」與「寄送問題回報」指向新日誌。
5. `feat(cli)`：右鍵執行設定來源、啟動與結束紀錄，`Debug.WriteLine` 改寫日誌。
6. `test(core)`：架構測試（見第 7 節）。
7. `docs(docs)`：更新本計畫狀態、`v4_desktop_ui_layout.md` 決策紀錄、`specs/README.md`。
8. `chore(msix)`：升版號、CHANGELOG。

## 7. 測試

**單元測試**

- 遮蔽：輸入、輸出路徑換成檔名；其他磁碟路徑與網路路徑換成 `<path>\檔名`；密碼與 API 金鑰換成 `***`；選項名稱含 `password`、`key`、`token` 的值換成 `***`。
- 例外展開：兩層內層例外、`AggregateException` 都完整寫出；外部程式輸出超過 2 KB 會截斷。
- 輪替：超過 5 MB 改寫 `.1.log`；日期改變換新檔。
- 清理：14 天前的檔案刪除、14 天內的保留、非日誌檔不動。
- 並行：兩個執行緒各寫 500 筆，行數正確且沒有交錯的行。
- 寫入失敗：資料夾唯讀或被刪除時不丟例外，處理照常完成。
- 一次失敗的壓縮任務（假的處理程式丟出帶內層例外的錯誤）：日誌有一筆 `ERROR`，含動作、檔案序號、檔名、兩層例外；不含完整路徑。
- 一次使用密碼的任務：日誌、歷史、任務紀錄中都搜尋不到測試密碼字串（補上密碼解鎖計畫第 6 節少做的執行期檢查）。

**架構測試**

- `src/` 中除了 `ClickraLog` 以外，不得有 `File.AppendAllText` 寫入 `.log` 檔，也不得有 `Console.Error.Write`（CLI 的命令列輸出除外，以白名單列出檔案）。
- Core 的 use case 不得直接呼叫 `ClickraLog.Error`（統一由共用處理寫）。

## 8. 驗收（Windows 實機）

- 解除安裝 Word 或指定一個壞掉的 LibreOffice 後轉檔：畫面寫「Word 無法處理這個檔案」，日誌有結束碼與輸出。
- 翻譯時拔掉網路：日誌有頁碼與錯誤，不含原文與譯文。
- 用有密碼的 PDF 壓縮並輸入密碼：日誌與歷史找不到密碼。
- 從右鍵同時對 3 批檔案壓縮：同一個日誌檔裡三個行程的紀錄都在，沒有交錯的行。
- 桌面端在背景工作裡故意丟出例外（測試用的隱藏命令列參數）：日誌留下 `ERROR` 與堆疊。
- 「查看日誌」選取今天的檔案；「寄送問題回報」打開郵件並選取最新的檔案。
- 把系統時間調到 15 天後重開：舊日誌被清掉。

## 9. 待決

1. 14 天與 5 MB 是否合適：先用這組，看實際檔案大小再調整。
2. 是否把 `history.log` 併進新日誌：不併。`history.log` 是歷史頁的資料來源，格式要穩定；新日誌是給人看的，格式可以改。
3. 是否提供「複製最近的錯誤」按鈕，讓使用者直接貼到郵件：先不做，看問題回報的情況。
