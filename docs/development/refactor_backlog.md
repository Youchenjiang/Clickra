# Clickra 重構待辦與未解決問題盤點 (Refactor Backlog & Open Issues)

> **建立**：2026-08-12（於 `feature/winui3-fluent-dashboard` 分支，基底 `21f2e6d`）
> **目的**：把程式碼審計發現的重複/契約繞過、功能落差 bug、未驗證項與已知限制一次盤點清楚，
> 作為後續 `refactor/command-metadata-to-core` 分支與發布驗證的執行依據。
> **追蹤慣例**：項目完成後在標題旁標記日期（與 `docs/ROADMAP.md` 的 R1-x 慣例一致）。

---

## 1. 待重構項（已確認，依優先序）

### 1.1 命令詮釋資料登錄上移 Clickra.Core（ConvertCommandDef 單一來源）⭐

**現況**：CLI 有完整登錄（`DashboardWindow.ConvertRegistry.cs`，11 個命令：word2pdf / excel2pdf / ppt2pdf / merge-pdf / compress-pdf / translate-pdf / decrypt-pdf / split-pdf / img2pdf / img-merge / img-stitch，含副檔名、filter、RequiresOffice、Group、TagColor）；Fluent 把按鈕寫死在 XAML（`Tag="word2pdf"`）並在頁面內散落重複對應。

**重複份數**：

| 資料 | 位置 | 份數 |
| :--- | :--- | :--- |
| 命令 → 副檔名 | CLI ConvertRegistry + Fluent `MainPage.xaml.cs:582` + Fluent `TaskProgressPage.xaml.cs:276` | **3**（Fluent 內部自己重複一次） |
| 命令 → 標籤 (TextKey) | Fluent `MainPage.xaml.cs:1455` + `TaskProgressPage.xaml.cs:286` | 2 |
| 命令清單本身 | CLI 登錄 vs Fluent XAML 按鈕 | 無單一來源 |

**修法**：把 `ConvertCommandDef`（command / TextKey / Extensions / MinFiles / RequiresOffice / Group）上移 `Clickra.Core` 作為單一來源，兩邊 UI 消費同一份清單。純重構、不改變行為。新功能成本從「Core 邏輯 + 兩邊各寫按鈕 + 兩邊各寫對應表」降為「Core 邏輯 + Core 加命令定義 + 兩邊各掛按鈕」。

### 1.2 Office 引擎偵測收斂成 Core 的 OfficeEngineDetector ⭐

**現況**：Microsoft Office 安裝偵測與引擎可用性判定散在兩套 UI，多種實作：

| 邏輯 | 位置 | 份數 / 實作 |
| :--- | :--- | :--- |
| `IsOfficeInstalled` | CLI `DashboardWindow.Paint.About.cs:182`（登錄檔查 `SOFTWARE\Classes\{ProgID}`）+ Fluent `OfficeEnginePreflight.cs:38`（`Type.GetTypeFromProgID`） | **2 種實作** |
| 引擎可用性判定（libreoffice→LO；microsoft→MS；auto→任一） | CLI `ConvertRegistry.cs:131` `HasAvailableEngine()` + CLI `Paint.Overview.cs:33`（inline 再算一次）+ Fluent `OfficeEnginePreflight.cs:21` | **3 份** |
| 命令 → app（word2pdf→Word 等） | CLI `ConvertRegistry.cs:139` + Fluent `OfficeEnginePreflight.cs:13` | 2 |

**修法**：Core 新增單一 `OfficeEngineDetector`（含 `IsAppInstalled`、`GetEngineReadiness`、命令→app 對應），兩邊 UI 呼叫。與 ROADMAP **F3-6**（Office 引擎抽象化）方向一致，可一併規劃。

### 1.3 history.log 契約繞過

**現況**：`history.log` 檔名在 UI 硬編碼，繞過 `ClickraStorage.HistoryFile`（Core 已有屬性）：

- CLI `DashboardWindow.Events.Click.cs:867`：`Path.Combine(dataDir, "history.log")`
- Fluent `MainPage.xaml.cs:1311-1315`：同上，且 `1323` 直接 `File.WriteAllText(logPath, "")`

檔名一改，兩邊都壞。修法：改走 `ClickraStorage.HistoryFile`（小改動，可獨立 commit）。

### 1.4 命令 → FileProcessor dispatch 重複（與 1.1 連動）

CLI（`ProgressWindow.Process.cs` / `ClickraCli.cs`）與 Fluent（`MainPage.xaml.cs:501` + `TaskProgressPage.xaml.cs:130`）各有一份命令 dispatch switch（10 個 case 幾乎相同）。與 1.1 一起解決（Core 至少提供命令描述與參數估算，或直接提供 dispatch 幫手）。

### 1.5 渲染管線已收斂（參考範式）✅（2026/08/12）

`PdfPageThumbnailRenderer.RenderPageFromFile` 已是共用元件：**Windows.Data.Pdf 主渲染 + PdfPig overlay fallback（加密檔）**，CLI 縮圖/燈箱與 Fluent 加密 fallback 消費同一份（commit `42221ca`；前置 `a35519b` 移除 200 字上限、`abf7cd3` 字框對齊）。這是「抽共用」的成功案例，1.1~1.4 的收斂可參考其模式。

**設計影響**：Core TFM 已升 `net8.0-windows10.0.19041.0`（WinRT projections）——Core 不再純平台輕量；NativeAOT 下的 CsWinRT 已實測可用（見 2.3）。後續重構若把更多 UI 邏輯上移 Core，須留意 Core 的平台依賴會隨之增加。

---

## 2. 未解決問題

### 2.1 [已實作，待實機驗證] Fluent 軌道「分割 PDF」✅（2026/08/12 更新）

**原狀（已確認，現已解決）**：`ClickraShell/ComMethods.cs` 的 `SubArgs` 提供 `split-pdf`，且 `Invoke` 優先走 packaged activation → **Fluent**；當時 Fluent 的 `TaskProgressPage` dispatch 沒有 split-pdf case 也沒有 default，`IsKnownCommand` 對 split-pdf 回 false → 顯示「無效命令」錯誤。v3.6.5.0 的視覺分割器原本只實作在 CLI（`ProgressWindow.VisualSplitter.cs`）。

**現況（2026/08/12 核對）**：Fluent 兩處 dispatch 都已有 `case "split-pdf"`：
- `MainPage.xaml.cs:535`（右鍵路徑）與 `TaskProgressPage.xaml.cs:169`（進度頁），皆含 `PromptSplitPagesAsync` 視覺分割流程與預覽/放大
- 共用渲染器 `PdfPageThumbnailRenderer.RenderPageFromFile` 改走 Windows.Data.Pdf 後，Fluent 分割預覽與 CLI 同一引擎（見 1.5）

**剩餘**：packaged activation 端到端（右鍵 → Fluent 分割 UI）尚未實機跑過——shell 延伸需重開機載入。選項 1（Fluent 實作分割 UI）已實作；選項 2/3 不再需要。

### 2.2 [未驗證] 同版本切換軌道（Native ↔ Fluent）

`-ForceUpdateFromAnyVersion` 是否在所有 Windows 版本生效未實機驗證。風險：部分版本視為「已安裝」而拒絕；後備方案：NativeAOT 套件改用獨立 Identity（`g1014308.ClickraNative`），但需處理兩套右鍵選單 COM 註冊（相同 CLSID）衝突。屬 F1-12 驗證範圍。

### 2.3 [部分已驗證] NativeAOT publish 產線 ✅（2026/08/12）

本機 `build_msix.ps1` 已成功產出含 Windows.Data.Pdf 的 NativeAOT 包，並安裝、執行成功（CsWinRT marshalling 正常）。之前 `dotnet publish` 在 shell 直接跑失敗是**環境問題**（vswhere 找不到 / 誤抓 VS2019 toolchain）；build 腳本會把 VS Installer 目錄加入 PATH，走腳本即可。

**仍待**：`Clickra.msix (Main)`（零依賴軌道）與 `ClickraLauncher.exe` 在本機的完整驗證（先前只驗了含 Fluent 的完整包）；CI（windows-latest）跑一次完整 release 流程。

### 2.4 [部分已解] MSIX 側載憑證信任

GitHub Release 的 MSIX 以 CI 自簽憑證簽署，使用者需信任憑證或開開發者模式才能側載。bootstrapper 目前**不**自動安裝憑證；若要自動化需明確實作信任決策（安全敏感）。

**本機層（已解，2026/08/12）**：`ClickraDev.pfx` 曾被 07/26 重生（BBF263）但信任沒跟上，導致 build 產的包安裝失敗（0x800B0109）。已把 PFX 對齊回機器信任的 DCA07995（含私鑰、密碼 1234，git-ignored）——本機 build → 安裝直接成功。注意：PFX 是各機器本機產物，若在別台機器/CI 簽名需各自對齊信任。

### 2.5 [已知限制]

- **WinAppRuntime 偵測 proxy**：以 System32 的 Bootstrap.dll 是否存在當 proxy；若 runtime 安裝方式改變（例如內建於 Windows）需同步更新。
- **ARM64**：兩條軌道目前都只打包 x64。
- **Bootstrapper 的 HKCU 是管理員 hive**：per-user .NET 安裝偵測不到（已有磁碟資料夾 fallback 緩解，見 `dual_track_guide.md` §3.1）。

### 2.6 [決策待定] NativeAOT 軌道功能落差策略

雙軌決策把舊 Win32 UI 升級為永久產品線（ROADMAP F1-11），但「新功能是否兩軌都要」未定案。候選策略：a) 凍結 Native 軌道（新功能只做 Fluent）；b) Native 軌道以 CLI 為主、GUI 只維持既有功能。決策影響所有未來功能的開發成本。

### 2.7 [已修] PDF 翻譯回歸測試套件不穩定（2026/08/12 記錄，2026/08/14 根治）

同一份 Core 程式碼（測試專案只引用 `Clickra.Core`，與 Fluent 改動無關）重複跑測試，FAIL 數在 **3~11 之間浮動**（PASS 恆為 95）：

- Pentest p4 / p7 / p14 gray prompt 系列（佈局分類）
- 2407 p7 / p10、Final project 系列（表格/圖表分類）
- Table captions 合併段分類、PDF batch runner hung provider 逾時測試（逾時測試易受環境影響）

疑似順序依賴（套件內共享靜態狀態）或環境依賴（字型渲染／逾時計時）。CI 若依賴「全 PASS」當門檻會誤報；需要先穩定測試套件（隔離狀態／固定順序／允許逾時容差）再以全 PASS 為合併條件。

**2026/08/12 補充**：測試專案 TFM 已對齊 Core 的 `net10.0-windows10.0.19041.0`（`f621572`），套件可正常執行；95 PASS / 11 FAIL 的 flaky 基線不變，仍未根治。

**2026/08/14 更新（PR-B 推進）**：套件已穩定，不再是 flaky 基線——CI（無 `test_pdfs/`）固定 **87 passed / 0 failed / 19 skipped**（fixture 測試 SKIP），本機（有 fixture）**100 passed / 0 failed / 6 skipped**。已修項目：fixture 缺失改 SKIP（不再誤報 FAIL）、Table captions caption-delimited 掃描 bug、Pentest p4/p14 diagram 誤標（`FinalizeShortFigureLabels` 未排除 gray prompt 內容）、Pentest p7 gray 標記被 `FinalizeGrayPromptContentFlags` 以冒號標題規則清掉、PDF batch runner culture 依賴（改斷言 culture-neutral 頁碼）。剩餘的 6 個 SKIP 是 fixture 存在但來源未覆蓋的測試，非失敗。

### 2.8 [已修] CLI 匯入覆寫已選指令（2026/08/12）

「選分割 PDF → 匯入/拖放 PDF」會被洗回 compress-pdf：`HandleDroppedFiles` 對單一 PDF 強制切 `compress-pdf`、element 18 重設後選第一個可用指令。已修（`2c49b15`）：新增 `CurrentSelectionAcceptsFiles()` 守門員，目前指令能接受檔案就保留。

**已確認無此問題（2026/08/12）**：Fluent 的 `DropZone_Tapped`/`DropZone_Drop` → `AddFiles` 不碰 `_selectedCommand`；`UpdateCommandAvailability` 只在目前指令與檔案不相容時才清空（`MainPage.xaml.cs:405`），相容則保留。設計上比 CLI 更保守（CLI 舊版是強制切換，Fluent 從不自動換指令）。無需修正。

---

## 3. 分支與發行狀態

| 項目 | 狀態 |
| :--- | :--- |
| `feature/winui3-fluent-dashboard` | 領先 origin/main **54 commit**（2026/08/12 核對；backlog 建立時為 29），**從未 push**；建置 0 錯誤、測試 95/11（既有 flaky 基線）、歷史已審計 |
| `refactor/command-metadata-to-core` | 已開（基底 = feature tip `21f2e6d`）；若 feature 先合回 main，此分支基底自動等於新 main tip，無需重開 |
| 合併順序建議 | 先合 feature → main（雙軌正式化），再合 refactor（diff 乾淨） |
| 合併前待決 | push 授權、合併方式（PR vs fast-forward）、2.2/2.4/2.3 殘項是否先補；**local main 停在 PR #37（b3dcedc）已過時，合併前先 fetch origin/main（已到 PR #45）** |

**發行狀態**：Main manifest (`AppxManifest.xml`) + Fluent manifest (`AppxManifest.Fluent.xml`) 版本已同步 3.6.5.0。

---

## 4. 與既有 ROADMAP 追蹤的關聯

| ROADMAP 項目 | 狀態與關聯 |
| :--- | :--- |
| **R1-3 Command Pattern**（HandleLButtonDown 複雜度 130） | ✅ **已完成**：本分支 `32c8837`（model convert features as Command objects）、`ad41a2e`（route clicks to per-tab handlers）、`b908d2a`、`628ab42` 已把 `HandleLButtonDown` 改成薄路由器（現為 delegate 鏈）；ROADMAP 已記 `(2026/08/11 完成)`。 |
| **R1-3 WndProc Router**（複雜度 137） | 未完成，仍待重構（與本盤點 1.x 無關，屬 CLI 既有技術債）。 |
| **R1-5 Localization 字典結構性重複** | 未完成（2026/08/09 記錄）。i18n 字典資料結構的必然模式，消除需「基底字典 + 語言覆寫」架構級改動，留待 Localization 專項。 |
| **R1-4 測試框架升級**（TestRunner → xUnit/NUnit） | 未完成。 |
| **F1-12 Fluent Release Stabilization** | 涵蓋本盤點 2.2（軌道切換）、2.3（NativeAOT publish）、2.4（憑證）的實機驗證。 |
| **F3-6 Office 引擎抽象化** | 與本盤點 **1.2**（OfficeEngineDetector）同源，可合併規劃。 |
| **R1-2 檔案命名整理** | 未完成。 |

---

## 5. 執行建議

1. **2.1 已解決（2026/08/12）**：Fluent 已實作 split-pdf 分割流程，剩 packaged activation 端到端實機驗證（重開機載入 shell 延伸後測右鍵）。
2. **refactor 分支依序**：1.1 命令登錄上移 → 1.2 OfficeEngineDetector → 1.3 history.log → 1.4 dispatch（與 1.1 連動），每個獨立 commit、各自可驗證。
3. **合併順序**：feature → main 先行，refactor 隨後（見 §3）。
4. **發布前**：以 CI 跑一次完整 release 流程補 2.2~2.4 驗證（F1-12）。
