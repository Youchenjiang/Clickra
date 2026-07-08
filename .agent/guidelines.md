# ⚡ Clickra 專案行為準則 (AI Agent 專用)

## 1. Git 完整性
- **禁止 Nuke-and-Pave**：嚴禁刪除舊檔案再新增同名檔案。改名必須使用 `git mv`。
- **原子化提交**：一個 Commit 只做一件事。嚴禁將多個不相干的邏輯修改（如版號同步、工作流修改、規則更新）合併到同一個 Commit 中。必須分批暫存（例如 `git add <特定檔案>`）並分開提交，確保每個 Commit 異動內容最小化且語意單一。
- **Commit 訊息格式規範**：每個 Commit 訊息必須符合本地 Commit Hook 的嚴格格式限制：
  1. Header 必須遵循 `type(scope): subject` 或 `type: subject`，長度必須小於等於 72 字元，不可用句號結尾。
  2. 允許的 `type` 包括：`feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`。
  3. Body 必須與 Header 留空一行，且必須是以英文寫成的編號列表並以 `1. ` 開頭（例如：`1. Add helper method.`）。
- **版本號同步**：每次修改功能或 UI 後，必須執行 `powershell -File scripts/bump_version.ps1 -Build` 增加 Patch（第 3 碼，且第四碼 Revision 保持為 0）、同步版本號（含 README、README.zh-TW.md、CHANGELOG.md 與 MSIX AppxManifest.xml）並自動重新編譯產物，以確保 Windows 11 選單快取刷新且內外版本一致，同時符合微軟商店的版號規範。
- **Commit 審核**：在執行 Commit 之前，必須執行 `git status` 確認沒有暫存 test 垃圾。
- **Tag 規範與發布順序**：在正式對外發布或商店提交時，必須嚴格遵守以下 Git Flow 順序：
  1. 在 `feature/*` 或 `hotfix/*` 工作分支上完成開發並提交（Commit）。
  2. 將工作分支推送到遠端，在 GitHub 上建立 Pull Request，成功合併（Merge）入 `main`。
  3. 切換回本地 `main` 分支並拉取最新代碼（`git checkout main && git pull`）。
  4. 在合併後的最新 `main` 分支節點上，建立對應的 Git Tag（格式為 `vX.Y.Z.0`）。
  5. 嚴禁直接 push 到 `main`、`release` 等受保護分支；若需推送 Tag，使用 `git push origin vX.Y.Z.0`。
- **分支命名規範**：嚴禁在 Git 分支名稱中包含版本號（如 `vX.Y.Z` 或 `vX.Y.Z.0`），以避免與 Git Tag 混淆，且不符合一般專案的開發常規。分支名稱必須使用 `feature/*` 或 `hotfix/*` 前綴，並配上純描述性的功能名稱（例如 `hotfix/ci-store-release-automation`）。

## 2. 代碼穩定性
- **增量修改 (Incremental Only)**：優先保留原始代碼結構。若要「重構」，必須先在對話中向使用者說明重構理由與覆蓋範圍。
- **語系優先 (Localization)**：選單標題、描述等字串嚴禁硬編碼 (Hardcode) 於 C# 中。必須統一修改 `packaging/msix/Strings/` 下的 `.resw` 檔案。
- **動態路徑**：在 Shell Extension 中存取外部資源（如圖標、執行檔）時，必須使用 `ShellUtils` 獲取執行期路徑，嚴禁假設檔案位於 `%LocalAppData%`。
- **本地驗證**：修改 CLI 邏輯後，必須手動執行 `./Clickra.exe [command]` 並檢查輸出檔案。修改封裝邏輯後，須執行 `scripts/build_msix.ps1` 檢查 `Layout` 結構。

## 3. 命名規範
- **方法命名**：遵循 verb-noun 模式，如 `GetLogicalWidth`、`ProcessFile`、`ValidateExtensions`。
- **屬性命名**：使用 PascalCase，如 `_dpiScale`、`_activeTab`。
- **常數命名**：Win32 常數統一放在 `Native/Win32.cs`，使用 PascalCase，如 `IDC_HAND`、`WS_CLIPCHILDREN`。
- **色彩常數**：統一放在 `UIHelper.cs`，使用語意化命名，如 `BgCard`、`BorderDefault`、`TextPrimary`。
- **檔案命名**：Partial class 使用 `ClassName.Part.cs` 格式，如 `DashboardWindow.Paint.cs`。
- **避免單字元變數**：在長方法中使用描述性名稱，如 `inputLabelW` 而非 `w1`。

## 4. 架構規則
- **三層架構**：`Clickra.CLI`（UI 層）→ `Clickra.Core`（核心邏輯）→ `ClickraShell`（Windows 整合）。
- **Partial Class 拆分**：大檔案按功能拆分，每個 partial class 檔案職責單一。
- **Processor 繼承**：單一檔案處理繼承 `SingleFileProcessorBase`，多檔案處理繼承 `MultiFileProcessorBase`。
- **UI 工具方法**：共用的 UI 繪圖方法統一放在 `UIHelper.cs`。
- **Win32 互動**：所有 P/Invoke 聲明統一放在 `Native/Win32.cs`。

## 5. 溝通協議
- **禁止自主 Push**：除非使用者明確下達 `/auto_commit` 或 `git push` 指令，否則禁止推送。
- **Diff 摘要**：在結束 turn 之前，若有變動，必須簡述受影響的檔案與變動行數。
