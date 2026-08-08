# ⚡ Clickra 專案行為準則 (AI Agent 專用)

## 0. 授權閘門（每次對話必須先套用）

- 只讀檢查預設允許；本地檔案修改只有在使用者要求修改時才允許。
- 建立/切換/刪除分支、commit、push/force-push、建立/修改/合併/關閉 PR、resolve review thread、建立/修改/刪除 release、dispatch/rerun/cancel workflow，以及 Microsoft Store/Partner Center 寫入，都必須在**當次對話**取得對應的明確授權。
- 授權是逐項的，不可推論擴張。例如「force-push `v3.6.3.0` tag」只授權該 tag 操作，不授權開分支、開 PR、改 workflow、合併 PR 或提交商店。
- 每次外部寫入前，先列出「使用者授權涵蓋的精確操作」與「即將執行的單一操作」；未涵蓋的下一步必須停下來詢問。
- 授權操作失敗而需要新增分支、PR、合併或改 workflow 才能繼續時，必須停下回報證據，不得自行升級處理範圍。
- 使用者表示反對或撤回授權後，立即停止；不得自行 revert、刪除、取消 workflow 或 force-push 進行清理。

## 1. Git 完整性
- **禁止 Nuke-and-Pave**：嚴禁刪除舊檔案再新增同名檔案。改名必須使用 `git mv`。
- **原子化提交**：一個 Commit 只做一件事。嚴禁將多個不相干的邏輯修改（如版號同步、工作流修改、規則更新）合併到同一個 Commit 中。必須分批暫存（例如 `git add <特定檔案>`）並分開提交，確保每個 Commit 異動內容最小化且語意單一。
- **Commit 訊息格式規範**：每個 Commit 訊息必須符合本地 Commit Hook 與 CI policy 的格式限制：
  1. Header 必須遵循 `type(scope): subject` 或 `type: subject`，長度必須小於等於 72 字元，不可用句號結尾；若使用 scope，必須使用 allowlist 中有意義的範圍。
  2. 允許的 `type` 包括：`feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `style`, `perf`, `security`。
  3. 允許的 `scope` 包括：`cli`, `core`, `shell`, `msix`, `docs`, `ci`, `deps`, `store`, `agent`。
  4. Body 必須與 Header 留空一行，且必須是以英文寫成的編號列表並以 `1. ` 開頭（例如：`1. Add helper method.`）。
- **版本號同步**：每次修改功能或 UI 後，必須執行 `powershell -File scripts/bump_version.ps1 -Build` 增加 Patch（第 3 碼，且第四碼 Revision 保持為 0）、同步版本號（含 README、README.zh-TW.md、CHANGELOG.md 與 MSIX AppxManifest.xml）並自動重新編譯產物，以確保 Windows 11 選單快取刷新且內外版本一致，同時符合微軟商店的版號規範。**但升版動作本身必須先取得使用者明確同意（見下方「禁止自行升版」），同意後才可執行。**
- **禁止自行升版**：嚴禁在未經使用者明確同意前自行升版。任何版號變更——包括執行 `scripts/bump_version.ps1`、修改 `AppxManifest.xml` / `Directory.Build.props` / `CHANGELOG.md` / README 中的版本號、建立版本 Tag（`vX.Y.Z.0`）或提交商店發布——都必須先在對話中向使用者說明升版理由與影響範圍，取得明確同意後方可執行。若使用者未表態，一律視為不同意，不得擅自升版。
- **Commit 審核**：在執行 Commit 之前，必須執行 `git status` 確認沒有暫存 test 垃圾。
- **PR 描述格式**：PR body 與 commit body 是兩套不同規則；必須使用 `.github/pull_request_template.md`，依變更檔案數量選擇 Summary/Key Changes/Verification 結構，不得只貼 commit 的編號列表。
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

## 6. 問題解決策略
遇到錯誤、阻礙或反覆失敗時：
- **禁止暴力嘗試**：不要在不理解系統的情況下反覆換方法嘗試。每一次失敗的隨機嘗試都不會讓你更接近答案。
- **先觀察機制**：停下來，讀錯誤訊息、讀相關原始碼、讀文件。理解它**為什麼**失敗，再動手修。
- **歸一化問題**：把問題縮到最小的可驗證單元。例如：先用最簡單的輸入測試 API 有沒有回應，確認基本機制能運作後，再處理完整需求。
- **自我覺察**：問自己「我是在解決問題，還是在嘗試各種隨機解法？」前者有觀察，後者只有試驗。

## 7. 問題解決方法論：別暴力嘗試

> 一段可以貼進 Clickra agent 規則的行為規則。

### 可直接套用的規則

遇到問題時：
1. 不要立即嘗試修復。失敗的嘗試不會讓你更接近答案，只是消耗 token。
2. 先停下來，讀錯誤訊息、讀原始碼、讀文件，理解系統為什麼這樣運作。
3. 把問題縮到最簡單的形式（像 base case），先讓最小單元跑通，再往上堆疊。
4. 問自己：「我是在解決問題，還是在嘗試各種隨機解法？」

### 核心心法

**難題求一，得一求全。**

遇到問題時，不要急著嘗試解法。先把問題縮到最簡單的形式，看懂它的運作機制，再回來解決原本的問題。

### 為什麼你會卡住

想像你被關在一個黑箱裡，外面的人叫你「把開關打開」。你不知道箱子裡有什麼，所以你開始亂摸——碰到一個按鈕就按，碰到一條線就拉。每次失敗，你就換一個地方試。

這就是暴力嘗試的本質：**你在跟一個你不理解的系統搏鬥**。你假裝它有某種結構，但你從來沒有驗證過。

每一次失敗的嘗試都不是「接近答案一步」，而是「又一次在黑暗中摸索」。你沒有獲得任何關於系統運作方式的資訊，只是消耗了時間和 token。

真正該做的是：**先把箱子打開，看看裡面的齒輪怎麼轉。**

### 正確的思維模式

#### 類比一：物理學家的單位脈衝

工程師面對複雜的訊號系統，不會直接把完整訊號丟進去測試。他們會先丟一個「單位脈衝」——一個最簡單、瞬間的輸入。因為單位脈衝的反應能告訴你系統的全部特性。

你在解問題時也是一樣：不要一開始就丟完整需求進去試，先找到那個「單位脈衝」——問題的最簡單形式。

#### 類比二：程式設計師的 Base Case

寫遞迴的人一定知道 base case。如果沒有 base case，遞迴就會無限展開直到爆掉。你不會說「我先隨便跑幾次看看會不會停」；你會先定義最簡單的情況下答案是什麼。

遇到問題時也是一樣：**先把參數設為最小值，看看會發生什麼。**

#### 類比三：最小可驗證單元

寫測試的人知道單元測試的價值。不要先寫整合測試再看著它失敗；先把最小功能單位驗證通過，確認它真的能運作，再往上堆疊。

遇到問題時：**找到那個最小的、可以獨立驗證的單元，先讓它跑通。**

### 你該問自己的問題

不是指令，是幫助你看見自己在做什麼：

- **「我有真正理解過這個系統嗎？」** 還是我只是在嘗試各種隨機解法？
- **「如果我把問題縮到最小，我能看到什麼？」** 那個最簡單的形式長什麼樣？
- **「我是在嘗試解決問題，還是在嘗試各種隨機解法？」** 兩者的差別在於：前者有觀察，後者只有試驗。

### 對比案例

**問題**：你的 agent 嘗試呼叫某個 API，每次都失敗。

**暴力嘗試**：
1. 嘗試不同的 endpoint → 失敗。
2. 嘗試不同的參數格式 → 失敗。
3. 嘗試加上 header → 失敗。
4. 嘗試換一個 API → 失敗。
5. 結論：「這個框架真難用」。

**歸一縮放構造法**：
1. 停下來，不要嘗試新的 API 呼叫。
2. 問自己：這個框架的 API 呼叫機制是什麼？去讀文件或原始碼。
3. 發現它需要先初始化 session，所有呼叫都要帶上 session token。
4. 先用最簡單的請求驗證 session 機制能運作。
5. 確認理解後，把這個邏輯套用到原本的需求。
6. 結論：「原來如此，我之前根本不知道它需要 session。」

差別不在於最後有沒有解決問題，而在於第二種方式讓你獲得了關於系統的知識。下次遇到類似問題，不需要再從頭摸索。
