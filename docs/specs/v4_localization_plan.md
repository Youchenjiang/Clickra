# 4.0 桌面端多語系

| 欄位 | 內容 |
| :--- | :--- |
| **文件狀態** | 計畫 (Planned)，〈4.0 上線前清單〉第 1 項 |
| **影響範圍** | `Clickra.Desktop`（全部畫面）、`Clickra.Core/Resources/Locales/*.json`、`Clickra.Core` 處理程式的進度與錯誤訊息、`tests/Clickra.Core.Tests` |
| **起草日期** | 2026-11-20 |
| **相關文件** | [v4_cross_platform_master_plan.md](v4_cross_platform_master_plan.md)〈4.0 上線前清單〉、[v4_desktop_ui_layout.md](v4_desktop_ui_layout.md) 4.10 命名規則 |

## 1. 現況（2026-11-20 盤點）

**Core 語系檔**
- `src/Clickra.Core/Resources/Locales/` 有繁中、簡中、英、日、韓五個 JSON，各 550 個鍵，嵌入組件，`Localization.T(key)` 依 `SettingsController.Language` 查詢；找不到時退回繁中，再找不到就顯示鍵名。
- 鍵名是小寫加底線（`tab_history`、`setting_output_title`）。
- `TestSuite.UiResourceCoverage` 已檢查右鍵選單資源、命令標籤鍵與商店說明五種語言齊全。

**桌面端**
- 介面文字幾乎全部直接寫成繁體中文：20 個檔案、約 510 條含中文的字串（去掉註解後統計，同一檔案內重複的只算一次）。最多的是 `ConvertPage.axaml`（136）、`SettingsPage.axaml`（82）、`ConvertViewModel.cs`（64）、`ConvertActionCatalog.cs`（45）、`ActionOptionsViewModel.cs`（39）、`SettingsPage.axaml.cs`（30）。
- 只有三處走語系：上方三個分頁名稱、歷史列的功能名稱、診斷信件。
- 設定頁已有「介面語言」（跟隨系統＋五種語言），但改了之後桌面端幾乎沒有變化。
- 內嵌字型只有 Noto Sans TC 子集：日文假名、韓文、簡體字不在子集裡，會退回系統字型；日文漢字若先用到繁中字型，字形會是繁中寫法。

**Core 處理程式**
- 進度與錯誤訊息有約 190 條直接寫成中文（26 個檔案），例如「正在讀取 PDF 檔案...」。這些會出現在桌面端的進度列與結果列，3.x 也一樣。

## 2. 目標

- 桌面端每一段使用者看得到的文字都從語系檔取得，五種語言齊全。
- 換語言後整個介面跟著換，包括動作名稱、提示、錯誤、日期時間格式。
- 文字較長的語言（英文、日文）在 960 × 660 與最小視窗 760 × 600 下不截斷、不擠出版面。
- 每種語言都用對的字型。
- 之後新增畫面時，忘了翻譯會被測試擋下。

不在範圍內：文件、商店說明（已有各自的流程）、Markdown 範本內容。

## 3. 做法

### 3.1 怎麼在畫面上取字

- 新增 `Clickra.Desktop/L10n/Strings.cs`（命名空間 `Clickra.Desktop.L10n`，避開與 Core 的 `Localization` 類別同名）：`Strings.T(key)`、具名參數的 `Strings.F(key, ("count", 3))`、清單用的 `Strings.List`／`ShortList`。語言在啟動時讀一次，整個視窗不會混用兩種語言。
- XAML 用標記擴充 `{l:T key_name}`，載入畫面時取一次字串；ViewModel 用 `Strings.T("key")`。兩者都不用反射，符合 NativeAOT。
- 有參數的句子一律用具名參數（`{count}`、`{name}`），不在程式裡拼接，讓各語言能調整語序（例如「完成 {count} 個檔案」）；寫法規則見 [ui_writing_guide.md](ui_writing_guide.md)。
- 英文需要單複數時另加 `鍵_one`（例如 `ui_files_count_files_one` = "1 file"），由 `Strings.Count(key, n)` 選；中日韓的 `_one` 與原鍵內容相同，五種語言都有值。
- 句子中間夾著輸入框（「每 [ 3 ] 頁一份」）時仍存成一句（`每 {n} 頁一份`），由 `Strings.Around(key, "n")` 拆成框前與框後的文字，各語言自己決定框的位置。

### 3.2 換語言什麼時候生效

**建議：重新開啟後生效。** 設定頁換語言時，下方出現「重新開啟 Clickra 後套用」與「立即重新開啟」按鈕（處理中時按鈕停用並說明要等處理完）。

理由：所有字串在載入時取一次即可，不必每個綁定都監聽語言變更，程式簡單、NativeAOT 安全；換語言是很少做的事。

另一個選項是「立即生效」：每段文字都綁到可通知變更的來源，換語言時整個視窗重新整理。做得到，但每個畫面都要改成可通知的寫法，工作量與出錯機會都高很多。

### 3.3 鍵的命名與放置

- 桌面端新鍵一律以 `ui_` 開頭，接畫面與用途：`ui_convert_catalog_title`、`ui_split_preview_cut`、`ui_settings_language_restart`。
- **功能名稱**：原訂共用既有的 `cmd_*` 鍵；實作時改為新介面自己的 `ui_feature_*`（2026-11-20），因為 4.10 的名稱（例如「批次重新命名」）與 3.x 的 `cmd_*`（「批次命名」）不同，改 `cmd_*` 會連帶改到舊介面與右鍵選單登錄。舊介面退場時再把右鍵選單改用 `ui_feature_*`，兩處合一。
- 仍放在 Core 的五個 JSON，不另開檔案；依畫面分段排序，方便對照。
- 桌面端只用 `ui_*` 新鍵與功能名稱等共用鍵（`cmd_*` 等），**不引用只屬於舊介面（Fluent、Dashboard、右鍵進度視窗）的鍵**，即使文字剛好相同也另開 `ui_*` 鍵。舊鍵先保留，等舊介面退場（清單第 8 項）時，由「語系鍵必須被用到」的架構測試找出並一併移除，不會誤刪新介面用到的字。

### 3.4 字型

- 依介面語言決定字型順序，沿用 Core 的 `LocalizedUiFontSelector`：
  - 繁中：Noto Sans TC（內嵌）→ Microsoft JhengHei UI
  - 簡中：Microsoft YaHei UI → Noto Sans TC
  - 日文：Yu Gothic UI → Meiryo UI
  - 韓文：Malgun Gothic
  - 英文：Segoe UI Variable → Segoe UI，中文退回 Noto Sans TC
- macOS 與 Linux 的對應字型（PingFang、Hiragino、Apple SD Gothic Neo、Noto CJK）一併列入，系統沒有時退回內嵌字型。

### 3.5 日期、時間與數字

- 歷史的時間、檔案大小、頁數依介面語言的文化格式顯示（`CultureInfo` 取自介面語言，不是系統地區）。
- 「今天」「昨天」等相對日期也進語系檔。

### 3.6 Core 進度與錯誤訊息

- 約 190 條寫死的中文改為鍵，進度訊息由 `ExecutionLocalizationOptions` 傳入的語言取字。3.x 也一併受益。
- 這部分範圍大且動到處理程式，放在桌面端完成之後、另一個提交群組中處理（見第 5 節）。

## 4. 文字確認流程

依 4.10，介面用語改前改後都要經使用者確認。為了省時間，用一張表一次看完：

1. **抽取**：寫一個腳本掃 `Clickra.Desktop` 的 `.axaml` 與 `.cs`，列出每條中文字串、所在檔案與行號、建議的鍵名，輸出成對照表（一個 Sheets artifact）。
2. **第一輪：只看繁中**。使用者確認每條繁中文字是否就是要的說法；趁這次把不一致的用詞統一（例如「檔案」「資料夾」「設為預設值」）。
3. **翻譯**：
   - 簡中：用 Core 的 `ChineseTextConverter` 轉換後，人工修正兩岸用語差異（檔案→文件、資料夾→文件夹、預設→默认、列印→打印、影片→视频等），差異詞另列一欄方便確認。
   - 英文：依 4.10 的功能名稱寫，按鈕用動詞開頭、句子用一般大小寫（sentence case）。
   - 日文、韓文：先翻譯，再用既有的 3.x 譯文對照用詞；有疑問的條目在表上標出。
4. **第二輪：看英文**。使用者確認英文（主要是語氣與功能名稱），日韓抽查。
5. **回寫**：腳本把表格寫回五個 JSON 並替換程式中的字串。

## 5. 分支與提交

分支：`feat/desktop-localization`（從 3.21.0.0 之後的 `main` 開）。

1. `feat(shell)`：`Strings`、`{l:T}` 標記擴充、`Strings.Plural`；設定頁「重新開啟後套用」；依語言選字型。
2. `feat(shell)`：轉檔頁（放檔案、動作目錄、動作頁、選項卡片）改用語系鍵，附繁中鍵值。
3. `feat(shell)`：工作區（分割、合併、重新命名、放大預覽）改用語系鍵。
4. `feat(shell)`：處理中與結果、對話框、精簡視窗改用語系鍵。
5. `feat(shell)`：歷史、設定頁改用語系鍵；日期時間依介面語言格式化。
6. `feat(core)`：五個語系檔補齊 `ui_*` 的簡中、英、日、韓。
7. `test(core)`：架構測試──`Clickra.Desktop` 的 `.axaml` 與 `.cs`（註解除外）不得出現中日韓文字；所有 `ui_*` 鍵五種語言都有非空值；同一個鍵在各語言的具名參數相同；`ui_*` 的值不以標點開頭、不以「，」「、」結尾；繁中值不出現 [ui_writing_guide.md](ui_writing_guide.md) 第 2 節不用的詞；程式用到的鍵都存在、語系檔裡的 `ui_*` 鍵都有被用到。
8. `docs(docs)`：本計畫狀態、4.10 補英文用詞、清單第 1 項打勾。
9. `chore(msix)`：升版號（3.22.0.0）、CHANGELOG、商店說明。

Core 進度與錯誤訊息（3.6）另開 `feat/core-message-localization`，在本分支合併後進行。

## 6. 驗收

- 五種語言各截一輪主畫面每一步的畫面（沿用 `dseed.sh` 的種子截圖，960 × 660 與 760 × 600 兩種大小），檢查：
  - 沒有任何中文殘留（英日韓）、沒有鍵名外露。
  - 沒有截斷、沒有擠出版面、沒有被迫出現的整頁捲動。
  - 日文漢字用日文字形、簡中用簡體字形。
- 換語言後重新開啟，整個介面切換；處理中時不能立即重新開啟。
- 右鍵選單、歷史、動作卡片的功能名稱三處一致。
- 架構測試全數通過；NativeAOT 建置無新增裁剪警告。

## 7. 已決定（2026-11-20）

1. 換語言重新開啟後生效（3.2 的建議做法）。
2. 英文以 3.x 英文介面的用語為基礎，與 4.10 命名規則不一致的地方再修。
3. 日文、韓文以 3.x 譯文為用詞基準；CHANGELOG 註明歡迎回報用詞問題。
4. 對照表改成照畫面整理：一列是畫面上完整的一句，變數填上例子；程式碎片不給使用者審。
5. 介面文字的寫法規則記在 [ui_writing_guide.md](ui_writing_guide.md)，翻譯與之後新增文字都照它。

## 8. 進度

- 2026-11-20：抽取 `Clickra.Desktop` 的介面文字，共 427 條不重複文字（另有紀錄訊息不列入），做成對照表交給使用者確認繁中（第 4 節第 2 步）。
- 2026-11-20：對照表改成照畫面整理（410 列），繁中逐條審完，修改寫在「修改成」欄；整理出的寫法規則存成 [ui_writing_guide.md](ui_writing_guide.md)。下一步：翻譯（第 4 節第 3 步）。
- 2026-11-20：翻譯完成（對照表「翻譯」分頁），新介面約 420 條文字改為 `ui_*` 鍵並寫入五個語系檔；依語言選字型；設定頁換語言後提示重新開啟並可立即重開；新增 `TestSuite.DesktopLocalization` 四項架構測試，`LocalizationOrphans` 改為也讀 `.axaml` 的 `{l:T 鍵}`。與 3.21.1 的修正同一個分支，版本 3.22.0.0。Core 進度與錯誤訊息（3.6）仍留待下一步。
