# 有密碼的 PDF：當場解鎖並接著處理

| 欄位 | 內容 |
| :--- | :--- |
| **文件狀態** | 計畫 (Planned)，排在 `feat/avalonia-views` 合併之後 |
| **影響範圍** | `Clickra.Core`（3.x 與 4.0 共用）：`Application`（逐檔處理與密碼互動）、`Modules/Pdf`（壓縮、合併、翻譯、列印）；4.0 桌面端只需小幅調整 |
| **起草日期** | 2026-11-16 |
| **相關文件** | [v4_desktop_ui_layout.md](v4_desktop_ui_layout.md) 4.1「能當場補救的問題不算失敗」 |

## 1. 現況

介面規格要求：處理到一半遇到有密碼的 PDF，不判定失敗，而是在那個檔案的列上要求輸入密碼，輸入後程式在內部先解密，再接著做原本的動作（「解鎖並壓縮」），使用者不必先跑一次「解密 PDF」。

4.0 桌面端已經做好這一列（`feat/avalonia-views`）：鎖頭、密碼欄位、「解鎖並＋動作」按鈕、輸錯時「密碼不正確」，以及同一批先用同一組密碼試。但 Core 只有三個動作會走到這一步。

| 動作 | 現在遇到有密碼的檔案 |
| :--- | :--- |
| PDF → 圖片、分割、解密 | 會詢問密碼（`ExecuteWithPasswordRetryAsync`），可當場解鎖 |
| 壓縮、合併、翻譯、列印 | **規劃階段就擋下**：`PdfEncryptionQuery.ValidateNotEncrypted` 只要任一檔有密碼，整批不處理，只回「請先使用解除密碼」 |

### 1.1 問題

1. **一個檔案有密碼，整批都不做**：壓縮 10 個 PDF，其中 1 個有密碼，其他 9 個也不會壓縮。
2. **要使用者自己繞路**：先「解密 PDF」產生無密碼的副本，再對副本做原本的動作，最後還得自己刪掉副本。
3. **「略過」等於「全部停止」**：`ExecuteWithPasswordRetryAsync` 在使用者不給密碼時拋出 `OperationCanceledException`，整個任務被取消，剩下的檔案也不處理。桌面端目前只能把這個按鈕寫成「停止」，無法提供真正的「略過這個檔案」。
4. **錯誤訊息只說原因、不給辦法**：「此 PDF 檔案受密碼保護或已加密，請先使用「解除密碼」功能……」。

## 2. 目標

- 壓縮、合併、翻譯、列印遇到有密碼的檔案時，和 PDF → 圖片一樣當場詢問密碼，解鎖後接著做原本的動作。
- 一批裡只有輸入密碼的那個檔案要等使用者，其他檔案照常處理。
- 使用者可以「略過這個檔案」：該檔記為未處理，其他檔案繼續，任務最後顯示「好 9 個，1 個略過」。
- 密碼只用在這一次，不寫進設定、歷史或日誌；解密的暫存檔用完立即刪除。
- 3.x（Fluent、CLI 進度視窗）與 4.0 共用同一套 Core 行為。

## 3. 做法

### 3.1 「先解鎖再處理」共用一個步驟

在 `Application` 加一個共用的解鎖步驟，供所有處理 PDF 的動作使用：

1. 處理某個檔案前，以 `PdfEncryptionQuery.IsEncrypted` 檢查。沒有密碼就照常處理。
2. 有密碼時，先用這一批已輸入過的密碼試（`IConversionInteraction` 回傳的上一組），失敗再呼叫 `RequestPasswordAsync`。
3. 拿到密碼後，用 `PdfDecryptProcessor` 解密到暫存資料夾（`StorageRuntime` 的 temp 目錄，檔名用 GUID），接著把**暫存檔**交給原本的處理流程。
4. 輸出檔名仍依**原始檔名**產生，輸出位置不變。
5. 不論成功、失敗或取消，`finally` 刪除暫存檔。

這樣壓縮、翻譯、列印的處理程式碼不需要知道密碼；合併則在讀入每個來源檔時套用同一個步驟。

### 3.2 拿掉規劃階段的擋下

- 刪除 `CompressPdfUseCase`、`MergePdfUseCase`、`TranslatePdfUseCase`、`PrintPdfUseCase` 中的 `ValidateNotEncrypted`。
- 這四個動作的 `Capabilities` 加上 `ConversionCapability.Password`。
- 處理程式裡 `PdfEncryptedException` 的轉換保留，當作最後一道防線（例如解密後的檔案仍無法開啟）。

### 3.3 「略過這個檔案」

- `IConversionInteraction.RequestPasswordAsync` 的回傳值區分三種結果：密碼、略過、停止全部。做法：新增 `PasswordPromptResult`（`Password`、`Skip`、`Cancel`），保留舊的 `string?` 介面作為相容包裝（`null` 視為 `Cancel`），讓 3.x 不必同時改。
- 逐檔處理（`PerFileConversionUseCaseBase`）收到 `Skip` 時記錄該檔「已略過」並繼續下一個；`ConversionResult` 增加 `SkippedFiles`。
- 單一輸出的動作（合併）收到 `Skip` 時，從合併清單拿掉該檔，繼續合併其他檔案，並在結果中註明。
- 歷史紀錄：有略過的檔案時，該筆仍記為成功，但原因欄寫「1 個略過（需要密碼）」。

### 3.4 介面

- 桌面端：「停止」改回「略過這個檔案」（`FileItemViewModel.SkipPassword` 改為回傳 `Skip`）；結果頁列出略過的檔案，旁邊放「再轉一次」。
- 3.x：Fluent 的密碼對話框與 CLI 進度視窗加上「略過」按鈕；文案經使用者確認後再改。
- 語系：新增「解鎖並＋動作」「已略過（需要密碼）」等字串，五種語言都補齊。

## 4. 分支與提交

分支：`feat/pdf-password-unlock`（`feat/avalonia-views` 合併之後開）。

1. `feat(core)`：共用解鎖步驟（解密到暫存、用完刪除），附單元測試（以 qpdf 產生的 AES-256 加密 PDF）。
2. `feat(core)`：`PasswordPromptResult` 與「略過」，`PerFileConversionUseCaseBase` 與 `ConversionResult` 支援略過的檔案。
3. `refactor(core)`：壓縮、翻譯、列印改用共用解鎖步驟，移除 `ValidateNotEncrypted`。
4. `refactor(core)`：合併支援逐檔解鎖與略過。
5. `feat(shell)`：桌面端「略過這個檔案」與結果頁；3.x 介面的「略過」。
6. `test(core)`：架構測試──處理 PDF 的動作不得在規劃階段以 `ValidateNotEncrypted` 擋下整批。

## 5. 驗收

- 壓縮 3 個 PDF（中間一個有密碼）：前後兩個直接完成；有密碼的那列要求密碼，輸入後完成；輸出檔名與沒有密碼時相同。
- 同一批 2 個用同一組密碼的檔案：只需輸入一次。
- 密碼輸錯：同一列顯示「密碼不正確」，可重輸，不中斷其他檔案。
- 按「略過這個檔案」：其他檔案照常完成，結果寫「好 2 個，1 個略過」，歷史紀錄註明略過。
- 合併時略過一個檔案：其餘檔案照順序合併。
- 處理完後暫存資料夾沒有殘留的解密檔；設定、歷史、日誌中找不到密碼。
- 翻譯、列印各實測一次加密檔。
- 3.x 與 4.0 都通過；NativeAOT 發佈沒有新增裁剪警告。
