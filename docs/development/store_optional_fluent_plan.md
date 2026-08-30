# AOT 主套件與 Fluent Optional Package 可行性及導入計畫

> **狀態**：最高優先級候選架構（Feasibility Gate，尚未核准正式遷移）  
> **記錄日期**：2026-08-29  
> **關聯里程碑**：`F1-13 Store-Resilient Optional Fluent Delivery`  
> **現行有效架構**：`docs/development/dual_track_guide.md` 所述的 Fluent / NativeAOT 真雙軌；本文件的全部閘門通過前不得移除現行產線。

## 1. 問題與決策目標

目前 Fluent MSIX 是 framework-dependent WinUI 3 應用程式。當套件在 manifest
宣告 Windows App Runtime framework dependency 時，Store 或側載安裝必須先解析並
取得 framework；實際使用中曾發生安裝等待過久、缺少 framework、安裝完成但 Fluent
仍無法啟動等情況。這會讓一個純 UI runtime 問題擴大成「Clickra 整體不可安裝或不可用」。

本計畫的產品目標不是消除 Fluent framework 的下載時間，而是建立以下保證：

1. 使用者先取得小型、零依賴、可立即工作的 NativeAOT Clickra。
2. Fluent UI 由使用者按需安裝；下載、framework 解析或啟動失敗不得破壞 AOT 功能。
3. Fluent 可用時由同一個 launcher 優先啟動，否則確定性回退 AOT。
4. Store 主套件維持小於 50 MB；不得用 Windows App SDK self-contained 規避問題。
5. Shell Extension 永遠留在 AOT 主套件，WinUI 失敗不得影響 Explorer。

## 2. 研究結論

### 2.1 已由 Microsoft 文件確認的能力

- MSIX optional package 可用來拆分大型應用或按需提供額外功能。
- Optional package 可包含 executable code；含程式碼的 optional package 必須屬於
  related set，否則其程式碼不能執行。
- Optional package 與 related set 會在主應用程式的 MSIX container 中執行。
- Microsoft 的 `OptionalPackageSample` 包含具有 app-list entry、可由 Start Menu
  啟動的 activatable optional app；optional package 不只限於資料檔或 DLL。
- `StoreContext.RequestDownloadAndInstallStorePackagesAsync` 可在使用者確認後按需下載
  Store DLC / optional package，並回報下載與安裝進度。
- `PackageCatalog.AddOptionalPackageAsync` 可將 related-set optional package 加入目前
  package catalog；`RemoveOptionalPackagesAsync` 可移除它。
- Windows 的 package graph 解析會包含 main package、optional package，以及各自宣告的
  framework dependencies。因而可讓 Windows App Runtime dependency 只由 Fluent optional
  package承擔，而不放進 AOT 主套件。

### 2.2 尚未確認、不可用推論取代的條件

- 公開範例主要是舊 UWP / C++ / .NET Native；尚無 Microsoft 範例直接證明
  「NativeAOT packagedClassicApp 主程式 + .NET 8 WinUI 3 full-trust optional EXE +
  Windows App SDK 2.x dependency」的完整組合。
- Microsoft Store 對 optional packages / related sets 需要額外權限；DLC package
  並非所有開發者帳號預設可用。未取得 Clickra 帳號的書面核准前，不得把此方案標記為
  Store-ready。
- Related set 的版本同步、optional app 的隱藏 app-list entry、AUMID activation、
  `ApplicationData` 共用位置與 Shell 參數轉交都必須以 Clickra PoC 實測。

因此目前結論是：**平台架構成立，但產品導入仍是有明確退出條件的可行性驗證，不是已核准實作。**

## 3. 目標套件架構

```text
Clickra Main MSIX / MSIX bundle（必要、零依賴）
├── ClickraLauncher.exe          NativeAOT，唯一 UI 路由入口
├── Clickra.exe                  NativeAOT CLI / Dashboard / Progress
├── ClickraShell.dll             NativeAOT Explorer command provider
├── Assets / Strings / resources.pri
├── Shell COM 與 context-menu registrations
└── 不宣告 Microsoft.WindowsAppRuntime dependency

Clickra Fluent Optional MSIX（按需）
├── Clickra.Fluent.exe           framework-dependent WinUI 3
├── Clickra.Core.dll 與 managed dependencies
├── WinUI XAML resources / SDK-generated resources.pri
├── activatable app entry（不得額外污染 Start Menu，待 PoC 決定隱藏方式）
├── uap3/uap4 MainPackageDependency → Clickra main identity
└── Microsoft.WindowsAppRuntime.2 dependency
```

主套件與 optional package 必須由同一 Store publisher 發行。Store 發行時不可任意改動
Clickra 已保留的 package identity、Publisher、PFN、App ID 或 Shell CLSID。

## 4. 執行與回退模型

### 4.1 啟動流程

```text
Start Menu / Shell command
        |
        v
ClickraLauncher.exe (NativeAOT)
        |
        +-- Fluent optional package 已安裝、版本可用、啟動成功
        |       -> 以 packaged activation / AUMID 啟動 Clickra.Fluent
        |
        +-- optional 未安裝、版本不相容、啟動非零失敗
                -> 啟動 Clickra.exe NativeAOT UI
```

Launcher 不得只靠 `Microsoft.WindowsAppRuntime.Bootstrap.dll` 是否存在判斷 Fluent。
是否可用至少必須同時驗證 optional package 已註冊、相容版本已進入 package graph、目標
application entry 可解析，以及啟動結果。Launcher 必須原樣、安全地轉交右鍵命令與檔案參數。

### 4.2 Fluent 安裝流程

1. AOT Dashboard 顯示「啟用 Fluent 介面」，說明額外下載及 framework 需求。
2. 由使用者操作呼叫 `RequestDownloadAndInstallStorePackagesAsync`；預設採有系統確認 UI
   的 API，不申請靜默安裝 restricted capability。
3. 顯示 Store 回報的下載、安裝與錯誤狀態；禁止自己推估「已完成」。
4. 安裝成功後要求關閉並重新啟動 Clickra，讓 related-set/package graph 狀態確定生效。
5. 安裝取消、失敗、逾時或 framework 解析失敗時保留 AOT，並提供重試與診斷資訊。

### 4.3 移除與損壞回退

- 使用者可從設定頁停用/移除 Fluent optional package。
- Related set 移除可能要求重新啟動主程式；UI 必須先明確通知。
- Optional package 被移除、狀態為 bad/not available，或 Fluent 啟動失敗時，launcher
  下次啟動直接回退 AOT，不得形成啟動循環。
- AOT 功能不可讀取 optional package 內的必要檔案；主套件必須能完全獨立工作。

## 5. Package graph、資料與啟動設計

### 5.1 Package graph

Windows 會把 optional package 放在 main package 之後，再解析 main / optional 的 framework
dependencies。Fluent optional package 可宣告 Windows App Runtime，而 AOT main 不宣告。
這個隔離是本方案成立的核心：optional 安裝失敗只能使 Fluent 不可用，不能阻止 main
package 先完成安裝。

### 5.2 App identity 與資料

Microsoft 文件表示 optional package 在 main app 的 MSIX container 中執行，但 Clickra 仍須
實測以下契約，不得假設：

- `ClickraStorage.GetDataDir()`、`ApplicationData.Current.LocalFolder` 是否落在預期的同一資料位置。
- `settings.conf`、`history.log`、`tasks/task-*.tmp` 是否可由兩個 UI 無損共用。
- Optional app activation 中 `Package.Current`、PFN、AUMID 與 AppLifecycle 的實際值。
- Fluent 單一實例 redirect 是否能接收由 AOT launcher 和 Shell 傳入的第二次 activation。

若資料位置不同，只能透過明確的共享資料契約或一次性遷移處理；禁止複製兩份互相漂移的設定。

### 5.3 Shell 邊界

`ClickraShell.dll`、COM CLSID、Explorer context-menu manifest registrations 必須只存在於
main package。Optional package 不可再次註冊 Shell Extension，避免雙選單、CLSID 衝突與
Explorer 載入 WinUI runtime。

## 6. 更新、版本與發布影響

Related set 會嚴格同步 main 與已安裝 optional package 的相容版本：

- 未安裝 Fluent 的使用者應只接收並啟用 main 更新。
- 已安裝 Fluent 的使用者必須取得可形成完整 related set 的 main + optional 版本；若其中
  一包尚未更新完成，Windows 可能繼續使用舊的完整組合。
- Optional 更新失敗不得損壞現有 AOT，但可能延遲新版 main 對該使用者生效。
- 每次 release 必須從相同版本來源產出 main、optional 與 bundle metadata；版本變更仍受
  專案「禁止自主升版」規則約束。

現行 `makeappx pack` 單包流程不足以產生 related-set metadata。PoC 必須評估使用正式
Windows Application Packaging Project / `Bundle.Mapping.txt`，或可重現的手工 bundle
manifest 產線；不得依賴只能在開發機 GUI 中操作的步驟。

## 7. Store 與權限閘門

在實作正式產品遷移前，必須向 Windows Developer Support 取得 Clickra Store 帳號與產品可用
以下能力的確認：

1. Optional packages。
2. Related sets。
3. Optional package with executable code。
4. 以使用者確認 UI 按需安裝 associated DLC package。
5. WinUI 3 full-trust `packagedClassicApp` 作為 activatable optional app 是否可通過認證。

未取得核准時，不申請靜默下載的 `storeOptionalPackageInstallManagement` restricted
capability；首版設計一律使用顯示系統確認的 Store API。權限遭拒或 Partner Center 無法建立
對應 DLC/optional product 時，本方案立即停止，不得用兩個未互斥的 Store listing 繞過。

## 8. 最高優先級可行性里程碑

### Phase 0 — 帳號與 Store 可用性（硬閘門）

- [ ] 向 Microsoft 送出 optional package / related set 權限申請。
- [ ] 取得書面回覆並保存工單編號、允許範圍與限制。
- [ ] 在 Partner Center 測試產品中確認能建立 associated optional/DLC package。

**退出條件**：權限遭拒、帳號不支援，或 Microsoft 明確不接受此 full-trust 組合時，停止
方案，不進入正式改造。

### Phase 1 — 最小本機 PoC（不得改造完整 Clickra）

- [ ] 建立最小 NativeAOT main package、最小 WinUI 3 optional package與 related-set bundle。
- [ ] 主套件不含 Windows App Runtime dependency，能在乾淨 x64 Windows VM 安裝並啟動。
- [ ] Optional package 單獨承擔 Windows App Runtime dependency。
- [ ] Optional app 可由 main launcher 以 AUMID 啟動並接收一組帶空格、Unicode 路徑的參數。
- [ ] Optional 未安裝、安裝失敗與移除後，main 仍可啟動。

### Phase 2 — Clickra 整合 PoC

- [ ] Shell → launcher → Fluent/AOT 的完整右鍵參數路徑通過。
- [ ] Dashboard、conversion progress、PDF 密碼、取消與 Office preflight 至少各跑一條真實流程。
- [ ] AOT / Fluent 共用 settings、history 與 per-task records，沒有資料分叉。
- [ ] Optional app 不產生不必要的第二個 Start Menu Clickra 入口。
- [ ] Main package 壓縮後小於 50 MB，且不包含 Fluent managed payload。

### Phase 3 — 更新、故障與移除矩陣

- [ ] `main v1`（無 optional）→ `main v2`。
- [ ] `main v1 + optional v1` → `main v2 + optional v2`。
- [ ] Optional 更新下載中斷／framework 缺失時，既有 AOT 仍可用。
- [ ] 移除 optional、重裝 optional、損壞 optional 狀態皆能回退。
- [ ] Explorer 重啟、套件更新與卸載不被殘留 Fluent / dllhost 程序阻擋。

### Phase 4 — Store private flight（正式遷移硬閘門）

- [ ] Partner Center ingestion 接受 main、optional 與 related-set metadata。
- [ ] Certification 接受 optional full-trust WinUI 3 executable。
- [ ] 兩台乾淨 VM 驗證：只裝 main 可立即使用；按需安裝 Fluent 可完成或安全失敗。
- [ ] Store 更新及 optional 移除矩陣通過。

只有 Phase 0–4 全數通過，才能：

1. 將方案狀態改為「已採用」。
2. 取代 Store 的 Fluent-only 主套件。
3. 評估現行 GitHub `ClickraSetup.exe` 真雙軌是否保留、簡化或退役。

## 9. 失敗時的回退決策

依優先順序採用：

1. **Store AOT-only；GitHub/官網維持 Fluent + NativeAOT 真雙軌**：可靠性最高。
2. **Store Fluent framework-dependent；GitHub/官網維持真雙軌**：只有 Store flight 證明
   framework 安裝可靠時才接受。
3. **兩個獨立 Store listing**：不採用。不同 identity 會造成兩個商店頁面、可能同時安裝、
   Shell CLSID/右鍵選單與資料位置衝突。
4. **單一 self-contained Fluent MSIX**：不採用。預估超過 50 MB 的產品限制。
5. **從 Store app 私自下載並執行外部 Fluent binaries**：不採用。會繞過 Store package
   servicing、簽章與更新模型。

## 10. 預期修改範圍（通過閘門後）

- `src/ClickraLauncher/`：optional package discovery、AUMID activation、啟動結果與 AOT fallback。
- `src/Clickra.CLI/`：AOT 設定頁的 Fluent acquisition / remove / status UX；移除重複 runtime 判斷。
- `src/ClickraShell/`：維持只呼叫 launcher，補強參數及 fallback 測試。
- `src/Clickra.Fluent/`：optional identity / activation / shared-data 驗證，不得再註冊 Shell。
- `packaging/`：拆分 Main / Fluent Optional manifests、related-set bundle metadata。
- `scripts/` 與 `.github/workflows/`：可重現的 main、optional、bundle、簽章與 Store artifacts。
- `docs/`：架構、發行、側載、Store 安裝與故障排除文件。
- 測試：launcher decision、package detection、argument forwarding、shared state、update matrix。

## 11. 原子化提交建議

正式實作時至少按下列關注點拆分，不把 PoC、產品邏輯、CI 與文件塞進同一 commit：

1. `docs`: 記錄可行性與 Store 權限結果。
2. `chore(msix)`: 建立最小 main / optional / related-set packaging scaffold。
3. `test(msix)`: 加入 package graph 與 activation PoC 驗證。
4. `feat(shell)` 或 `feat(cli)`: 實作 launcher 的 optional activation 與 AOT fallback。
5. `feat(cli)`: 實作使用者確認式 Fluent 安裝/移除 UX。
6. `chore(ci)`: 建立 main / optional / bundle artifacts 與驗證。
7. `docs`: 更新採用後的正式發行與維護指南。

若同一檔案同時包含行為改動與說明清理，使用 hunk staging 分開提交。任何版本號變更、Store
submission、Partner Center mutation、branch、commit、push 或 PR 操作仍需各自取得當次明確授權。

## 12. 參考資料

- [Optional packages and related set authoring](https://learn.microsoft.com/windows/msix/package/optional-packages)
- [Optional packages with executable code](https://learn.microsoft.com/windows/msix/package/optional-packages-with-executable-code)
- [Microsoft OptionalPackageSample](https://github.com/AppInstaller/OptionalPackageSample)
- [Download and install package updates from the Store](https://learn.microsoft.com/windows/apps/package-and-deploy/package-updates-from-store)
- [StoreContext.RequestDownloadAndInstallStorePackagesAsync](https://learn.microsoft.com/uwp/api/windows.services.store.storecontext.requestdownloadandinstallstorepackagesasync)
- [PackageCatalog.AddOptionalPackageAsync](https://learn.microsoft.com/uwp/api/windows.applicationmodel.packagecatalog.addoptionalpackageasync)
- [Windows App SDK dynamic dependency and package graph specification](https://github.com/microsoft/WindowsAppSDK/blob/main/specs/dynamicdependencies/DynamicDependencies.md)

