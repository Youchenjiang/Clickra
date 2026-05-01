# ⚡ Clickra 專案行為準則 (AI Agent 專用)

## 1. Git 完整性
- **禁止 Nuke-and-Pave**：嚴禁刪除舊檔案再新增同名檔案。改名必須使用 `git mv`。
- **原子化提交**：一個 Commit 只做一件事。更名與邏輯修改必須分開。
- **版本號同步**：每次修改功能或 UI 後，必須執行 `powershell -File scripts/bump_version.ps1 -Build` 增加 Revision、同步版本號（含 README 與 MSIX AppxManifest.xml）並自動重新編譯產物，以確保 Windows 11 選單快取刷新且內外版本一致。
- **Commit 審核**：在執行 Commit 之前，必須執行 `git status` 確認沒有暫存 test 垃圾。

## 2. 代碼穩定性
- **增量修改 (Incremental Only)**：優先保留原始代碼結構。若要「重構」，必須先在對話中向使用者說明重構理由與覆蓋範圍。
- **語系優先 (Localization)**：選單標題、描述等字串嚴禁硬編碼 (Hardcode) 於 C# 中。必須統一修改 `packaging/msix/Strings/` 下的 `.resw` 檔案。
- **動態路徑**：在 Shell Extension 中存取外部資源（如圖標、執行檔）時，必須使用 `ShellUtils` 獲取執行期路徑，嚴禁假設檔案位於 `%LocalAppData%`。
- **本地驗證**：修改 CLI 邏輯後，必須手動執行 `./Clickra.exe [command]` 並檢查輸出檔案。修改封裝邏輯後，須執行 `scripts/build_msix.ps1` 檢查 `Layout` 結構。

## 3. 溝通協議
- **禁止自主 Push**：除非使用者明確下達 `/auto_commit` 或 `git push` 指令，否則禁止推送。
- **Diff 摘要**：在結束 turn 之前，若有變動，必須簡述受影響的檔案與變動行數。
