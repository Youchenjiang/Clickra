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

---

## 2. 未解決問題

### 2.1 [BUG] Fluent 軌道下「分割 PDF」無效（split-pdf）🚨

**根因（已確認）**：`ClickraShell/ComMethods.cs` 的 `SubArgs` 提供 `split-pdf`，且 `Invoke` 優先走 packaged activation → **Fluent**；但 Fluent 的 `TaskProgressPage` dispatch **沒有 split-pdf case 也沒有 default**，`IsKnownCommand`（= 副檔名表有無對應）對 split-pdf 回 false → 顯示「無效命令」錯誤。v3.6.5.0 的視覺分割器只實作在 CLI（`ProgressWindow.VisualSplitter.cs`）。

**影響**：Fluent 軌道用戶右鍵「分割 PDF」直接報錯；NativeAOT 軌道正常。

**選項**：
1. Fluent 實作分割 UI（雙份 UI 成本，但功能齊全）
2. 右鍵 split-pdf 改路由到軌道內的 `Clickra.exe`（shell 可依命令選擇目標 exe）
3. 先從 shell `SubArgs` 移除 split-pdf（最快止血，功能只剩 CLI/儀表板內）

### 2.2 [未驗證] 同版本切換軌道（Native ↔ Fluent）

`-ForceUpdateFromAnyVersion` 是否在所有 Windows 版本生效未實機驗證。風險：部分版本視為「已安裝」而拒絕；後備方案：NativeAOT 套件改用獨立 Identity（`g1014308.ClickraNative`），但需處理兩套右鍵選單 COM 註冊（相同 CLSID）衝突。屬 F1-12 驗證範圍。

### 2.3 [未驗證] NativeAOT publish 產線

本機只有 VS2019（MSVC 14.29），.NET 8 NativeAOT 需要 VS2022 的 MSVC 14.3x——`ClickraSetup.exe` 與 `Clickra-Native.msix` 的 AOT publish 本機無法驗證。CI（windows-latest 有 VS2022）可驗證，需跑一次完整 release 流程確認。

### 2.4 [未驗證] MSIX 側載憑證信任

GitHub Release 的 MSIX 以 CI 自簽憑證簽署，使用者需信任憑證或開開發者模式才能側載。bootstrapper 目前**不**自動安裝憑證；若要自動化需明確實作信任決策（安全敏感）。

### 2.5 [已知限制]

- **WinAppRuntime 偵測 proxy**：以 System32 的 Bootstrap.dll 是否存在當 proxy；若 runtime 安裝方式改變（例如內建於 Windows）需同步更新。
- **ARM64**：兩條軌道目前都只打包 x64。
- **Bootstrapper 的 HKCU 是管理員 hive**：per-user .NET 安裝偵測不到（已有磁碟資料夾 fallback 緩解，見 `dual_track_guide.md` §3.1）。

### 2.6 [決策待定] NativeAOT 軌道功能落差策略

雙軌決策把舊 Win32 UI 升級為永久產品線（ROADMAP F1-11），但「新功能是否兩軌都要」未定案。候選策略：a) 凍結 Native 軌道（新功能只做 Fluent）；b) Native 軌道以 CLI 為主、GUI 只維持既有功能。決策影響所有未來功能的開發成本。

### 2.7 [缺陷] PDF 翻譯回歸測試套件不穩定（2026/08/12 記錄）

同一份 Core 程式碼（測試專案只引用 `Clickra.Core`，與 Fluent 改動無關）重複跑測試，FAIL 數在 **3~11 之間浮動**（PASS 恆為 95）：

- Pentest p4 / p7 / p14 gray prompt 系列（佈局分類）
- 2407 p7 / p10、Final project 系列（表格/圖表分類）
- Table captions 合併段分類、PDF batch runner hung provider 逾時測試（逾時測試易受環境影響）

疑似順序依賴（套件內共享靜態狀態）或環境依賴（字型渲染／逾時計時）。CI 若依賴「全 PASS」當門檻會誤報；需要先穩定測試套件（隔離狀態／固定順序／允許逾時容差）再以全 PASS 為合併條件。

---

## 3. 分支與發行狀態

| 項目 | 狀態 |
| :--- | :--- |
| `feature/winui3-fluent-dashboard` | 領先 origin/main **29 commit**（0 落後），**從未 push**；建置 0 錯誤、測試全 PASS、歷史已審計 |
| `refactor/command-metadata-to-core` | 已開（基底 = feature tip `21f2e6d`）；若 feature 先 fast-forward 合回 main，此分支基底自動等於新 main tip，無需重開 |
| 合併順序建議 | 先合 feature → main（雙軌正式化），再合 refactor（diff 乾淨） |
| 合併前待決 | push 授權、合併方式（PR vs fast-forward）、2.2~2.4 未驗證項是否先補 |

**發行狀態**：`AppxManifest.Native.xml` 版本已同步 3.6.5.0（與 Fluent manifest / Directory.Build.props 一致）。

---

## 4. 與既有 ROADMAP 追蹤的關聯

| ROADMAP 項目 | 狀態與關聯 |
| :--- | :--- |
| **R1-3 Command Pattern**（HandleLButtonDown 複雜度 130） | ✅ **已完成但 ROADMAP 未更新**：本分支 `32c8837`（model convert features as Command objects）、`ad41a2e`（route clicks to per-tab handlers）、`b908d2a`、`628ab42` 已把 `HandleLButtonDown` 改成薄路由器（現為 delegate 鏈）。需在 ROADMAP 補記完成日期。 |
| **R1-3 WndProc Router**（複雜度 137） | 未完成，仍待重構（與本盤點 1.x 無關，屬 CLI 既有技術債）。 |
| **R1-5 Localization 字典結構性重複** | 未完成（2026/08/09 記錄）。i18n 字典資料結構的必然模式，消除需「基底字典 + 語言覆寫」架構級改動，留待 Localization 專項。 |
| **R1-4 測試框架升級**（TestRunner → xUnit/NUnit） | 未完成。 |
| **F1-12 Fluent Release Stabilization** | 涵蓋本盤點 2.2（軌道切換）、2.3（NativeAOT publish）、2.4（憑證）的實機驗證。 |
| **F3-6 Office 引擎抽象化** | 與本盤點 **1.2**（OfficeEngineDetector）同源，可合併規劃。 |
| **R1-2 檔案命名整理** | 未完成。 |

---

## 5. 執行建議

1. **先做 2.1 的止血決策**（split-pdf 在 Fluent 軌道壞掉是最急的用戶可見 bug）。
2. **refactor 分支依序**：1.1 命令登錄上移 → 1.2 OfficeEngineDetector → 1.3 history.log → 1.4 dispatch（與 1.1 連動），每個獨立 commit、各自可驗證。
3. **合併順序**：feature → main 先行，refactor 隨後（見 §3）。
4. **發布前**：以 CI 跑一次完整 release 流程補 2.2~2.4 驗證（F1-12）。
