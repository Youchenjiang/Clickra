# ⚡ Clickra 專案行為準則 (AI Agent 專用)

## 1. Git 完整性
- **禁止 Nuke-and-Pave**：嚴禁刪除舊檔案再新增同名檔案。改名必須使用 `git mv`。
- **原子化提交**：一個 Commit 只做一件事。更名與邏輯修改必須分開。
- **Commit 審核**：在執行 Commit 之前，必須執行 `git status` 確認沒有暫存 test 垃圾。

## 2. 代碼穩定性
- **增量修改 (Incremental Only)**：優先保留原始代碼結構。若要「重構」，必須先在對話中向使用者說明重構理由與覆蓋範圍。
- **本地驗證**：修改 CLI 邏輯後，必須手動執行 `./Clickra.exe [command]` 並檢查輸出檔案。
- **PowerShell 規範**：嚴禁將使用者的安裝腳本替換為通用範本。

## 3. 溝通協議
- **禁止自主 Push**：除非使用者明確下達 `/auto_commit` 或 `git push` 指令，否則禁止推送。
- **Diff 摘要**：在結束 turn 之前，若有變動，必須簡述受影響的檔案與變動行數。
