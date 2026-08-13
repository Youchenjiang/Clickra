# Clickra 雙軌發行指南 (Dual-Track Distribution Guide)

> 決策狀態：**已採用（真雙軌）**。2026/08 決定維持兩條發行軌道：
> 使用者電腦上有 .NET 8+ 與 Windows App Runtime 2.x → 安裝 **Fluent** 軌道；
> 任一缺失 → 安裝 **NativeAOT**（零依賴）軌道。

---

## 1. 背景與決策

Clickra.Fluent（WinUI 3 儀表板）是 framework-dependent，需要兩樣 runtime：

1. **.NET 8+ Desktop Runtime**（Windows 10/11 不一定預裝）
2. **Windows App Runtime 2.x**（幾乎不會預裝）

若使用者直接安裝 Fluent MSIX 而機器缺任一 runtime，應用程式將無法啟動。
為避免「乾淨機器裝不起來」，決定維持兩條軌道：

| 軌道 | 套件 | 內容 | Runtime 需求 |
| :--- | :--- | :--- | :--- |
| **Fluent** | `Clickra.msix` | Clickra.Fluent.exe + Clickra.exe + ClickraShell.dll | .NET 8+ + Windows App Runtime 2.x |
| **NativeAOT** | `Clickra-Native.msix` | Clickra.exe + ClickraShell.dll（舊 Win32 Dashboard/Progress） | 無（零依賴） |

由 **`ClickraSetup.exe`**（NativeAOT bootstrapper）在安裝前偵測 runtime，
自動下載並安裝對應軌道。

### 為什麼不是單一自包含套件？

- 自包含 Fluent（bundle .NET + WinAppSDK）會讓套件從 ~25MB 成長到 80MB 以上，
  懲罰所有用戶的下載量。
- 商店軌道（framework-dependent）由 Microsoft Store 自動補上 framework 相依，
  不需要分支；雙軌主要服務 GitHub Release / 官網直裝用戶。

### 決策代價（必須接受）

- 舊 Win32 Dashboard/Progress 從「過渡 fallback」升級為**永久產品線**，
  不再是 roadmap 中可移除的對象。新功能若兩條軌道都要支援，需開發兩份 UI。
- 實務上 NativeAOT 軌道允許功能落後 Fluent（它是「沒有 runtime 時的最後手段」），
  但至少需維持既有功能可用。

---

## 2. 套件 Identity 策略

兩個 MSIX 使用**相同 Identity**（`g1014308.Clickra` / 相同 Publisher / 相同版本）：

- 同一時間只會安裝其中一個套件（安裝另一個 = 取代/升級）。
- 切換軌道：重跑 `ClickraSetup.exe --native` 或 `--fluent`，
  bootstrapper 偵測到套件已安裝時會以
  `Add-AppxPackage -ForceUpdateFromAnyVersion -ForceApplicationShutdown` 強制取代。
- 商店套件（Fluent）與 GitHub 的 NativeAOT 套件不會同時存在於系統，
  避免兩個右鍵選單 COM 註冊（相同 CLSID）互相衝突。

> [!WARNING]
> 相同版本號的同 Identity 取代，在部分 Windows 版本可能被視為「已安裝」而拒絕。
> bootstrapper 已加入 `-ForceUpdateFromAnyVersion`；若實測仍有問題，
> 需考慮讓 NativeAOT 套件使用獨立 Identity（`g1014308.ClickraNative`），
> 但要處理「兩套右鍵選單同時註冊」的衝突問題。

---

## 3. Bootstrapper（ClickraSetup.exe）

### 3.1 偵測邏輯（`src/Clickra.Setup/Program.cs`）

| 檢查 | 方法 |
| :--- | :--- |
| .NET 8+ Desktop Runtime | 登錄檔 `HKLM/HKCU\SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\sharedfx\Microsoft.WindowsDesktop.App`；找不到時 fallback 列舉 `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App`（部分 SDK/zip 安裝不寫登錄檔） |
| Windows App Runtime 2.x | `LoadLibraryExW("Microsoft.WindowsAppRuntime.Bootstrap.dll", ..., LOAD_LIBRARY_SEARCH_SYSTEM32)`——官方 redistributable 會把 Bootstrap DLL 放進 System32，能載入即代表 framework 已安裝 |

> [!NOTE]
> 安裝程式以管理員權限執行，其 `HKCU` 是「管理員」的 hive。
> 一般使用者的 per-user .NET 安裝不會被偵測到；這是已知限制，
> 若需支援請改為讀取原始使用者的 hive 或列舉磁碟資料夾（已含 fallback）。

### 3.2 軌道決定

```
兩者皆具備 → Fluent
任一缺失   → NativeAOT
--fluent / --native 可強制指定
--check   僅輸出偵測結果（exit 0 = 可裝 Fluent；1 = 建議 Native）
```

### 3.3 安裝流程

1. 決定軌道後，從
   `https://github.com/Youchenjiang/Clickra/releases/latest/download/{asset}`
   下載對應 MSIX（可用 `--local <msix>` 改用本機檔案、`--release-url` 自訂來源）。
2. `Add-AppxPackage` 安裝（套件含 `runFullTrust` + COM 註冊，需要管理員權限，
   因此 setup 的 manifest 宣告 `requireAdministrator`）。
3. 若 Fluent 安裝失敗（例如偵測錯誤導致相依不足），提示改用 `--native`。

### 3.4 用法

```text
ClickraSetup.exe                   自動偵測並安裝最適合的軌道
ClickraSetup.exe --check           僅輸出偵測結果（不安裝）
ClickraSetup.exe --fluent          強制安裝 Fluent 軌道
ClickraSetup.exe --native          強制安裝 NativeAOT 軌道
ClickraSetup.exe --local <msix>    使用本機 MSIX 安裝
ClickraSetup.exe --release-url <base>  自訂下載來源
ClickraSetup.exe --quiet           精簡輸出
```

---

## 4. 打包與 CI

### 4.1 本機打包

| 產物 | 腳本 |
| :--- | :--- |
| `Clickra.msix`（Fluent） | `scripts/build_msix.ps1`（既有，含 Fluent publish 輸出） |
| `Clickra-Native.msix` | `scripts/build_native_msix.ps1`（新） |

Native 套件的 manifest 為 `packaging/msix/AppxManifest.Native.xml`：
Application entry 指向 `Clickra.exe`（無參數啟動舊 Win32 Dashboard），
**不宣告** `Microsoft.WindowsAppRuntime` 相依。

### 4.2 CI（`.github/workflows/release.yml`）

`v*` tag 觸發時一次產出三個 Release assets：

1. `Clickra.msix`（Fluent）→ GitHub Release 附件 + **Microsoft Store** 上架
2. `Clickra-Native.msix`（NativeAOT，零依賴）
3. `ClickraSetup.exe`（自動偵測安裝程式）

> [!IMPORTANT]
> 本次調整同時修正了既有 CI 的問題：舊 CI 直接以
> `Clickra.exe + ClickraShell.dll` 組成的「NativeAOT-only」layout 上架商店，
> 但 manifest 的 Application entry 是 `Clickra.Fluent.exe`（套件內不存在），
> 商店版開始功能表入口會失效。現在商店統一送 `build_msix.ps1` 產出的完整 Fluent 套件。

---

## 5. 已知限制與後續

- **同版本切換**：`-ForceUpdateFromAnyVersion` 是否在所有 Windows 版本生效，
  需實機驗證（Native ↔ Fluent 互換）。
- **憑證信任**：GitHub Release 的 MSIX 目前以 CI 自簽憑證簽署，
  使用者需信任憑證或開啟開發者模式才能側載（與既有流程相同）。
  bootstrapper 目前**不**自動安裝憑證；若要自動化需明確實作信任決策。
- **WinAppRuntime 偵測**：以 Bootstrap DLL 是否存在為 proxy；
  若未來 runtime 安裝方式改變（例如內建於 Windows），需同步更新。
- **ARM64**：目前兩條軌道均只打包 x64。
- **Store 分支**：商店無法安裝時分支，固定為 Fluent 軌道；
  商店用戶不需要 ClickraSetup.exe。
- **舊 UI 維護**：NativeAOT 軌道沿用舊 Win32 UI，不再排定移除時程
  （見 `docs/ROADMAP.md` 更新）。
