# Conditional Store Submission Runbook — Main + Optional Package Architecture

> **文件角色：conditional runbook，非現行 Store 發布流程。**
>
> 本文件只有在 `store_optional_fluent_plan.md` 的權限／related-set／private-flight gates
> 明確通過後才可執行。現行 `.github/workflows/release.yml` 只建立並提交 NativeAOT Main
> `Clickra.msix`，不會建立或上傳 Fluent optional package；不得因本文件存在就推定
> Partner Center 權限已核准或 optional distribution 已投入 production。

## 1. 概覽

若 optional-package migration gates 全部通過，目標 Store submission architecture 為：

| 套件 | 內容 | 大小目標 | 依賴 |
|------|------|---------|------|
| `Clickra_Main.msix` | AOT Launcher + Dashboard + Shell | < 50 MB | 無（零依賴） |
| `Clickra_Fluent.msix` | WinUI 3 Fluent UI | ~14 MB | Windows App Runtime 2.x |

兩包透過 **related set** 綁定，由同一 Store publisher 發行。

## 2. Conditional packaging path

> `scripts/build_store.ps1` 是 optional-package / related-set PoC 路徑，不是目前 tag-triggered
> production release 的打包入口。現行 production release 由 `scripts/build_msix.ps1` 建立 Main MSIX。

```powershell
# 產生 Store submission 用的兩個 MSIX
.\scripts\build_store.ps1
```

產物：
- `Clickra_Main.msix` — 主套件
- `Clickra_Fluent.msix` — 選擇性套件

## 3. Partner Center 設定（僅 gate 通過後）

### 3.1 主套件（已有）

如果 Partner Center 裡已經有 Clickra 產品：

1. 進入產品頁面 → **Product management** → **Package flights** 或 **New submission**
2. 上傳 `Clickra_Main.msix` 作為主要套件
3. 確認 Store listing、截圖、分類等資訊正確

### 3.2 建立 Optional Package

1. 在 Partner Center → **Product management** → **Associated products**
2. 點 **Create a new associated product**
3. 選擇類型為 **Optional package** 或 **DLC**
4. 設定：
   - Product name: `Clickra Fluent`
   - Package family name 必須與主套件相同 publisher
   - Main package dependency 指向 Clickra

### 3.3 上傳 Optional Package

1. 進入 optional package 產品頁面
2. 建立新 submission
3. 上傳 `Clickra_Fluent.msix`
4. 設定 Store listing（可共用主套件的 listing）

### 3.4 Related Set 配置

Related set 的綁定由 Partner Center 自動處理：
- 主套件和 optional package 共用同一個 Publisher
- 兩者的 Identity Name 透過 `MainPackageDependency` 關聯
- Store 會自動同步版本更新

## 4. 版本同步（目標架構）

在 optional-package architecture 正式採用後，每次 release 才需要：
1. 同時更新兩個 MSIX 的版本號（`AppxManifest.xml` 的 `Version`）
2. 從相同 source 同時 build 兩個套件
3. 同時提交兩個套件的 Store submission

```powershell
# 更新版本號（兩個 manifest 都要改）
# packaging/msix/AppxManifest.xml
# packaging/msix/AppxManifest.Fluent.xml
.\scripts\bump_version.ps1
```

## 5. 更新流程（目標架構）

| 情境 | 處理方式 |
|------|---------|
| 只更新 AOT | 只提交 Main MSIX |
| 更新 Fluent | 只提交 Optional MSIX |
| 同時更新兩者 | 同時提交兩個 MSIX |
| Optional 更新失敗 | Main 繼續使用舊版，AOT 不受影響 |

## 6. 使用者體驗（目標架構）

### 首次安裝（無 Fluent）
```
Store → 安裝 Clickra_Main.msix（~27MB）
  → Start Menu → ClickraLauncher.exe
    → 偵測 .NET → 沒有 → Clickra.exe（AOT Dashboard）
```

### 安裝 Fluent（按需）
```
AOT Dashboard → 點「啟用 Fluent 介面」
  → Store 下載 Clickra_Fluent.msix（~14MB）
    → 安裝完成 → 重啟 Clickra
      → ClickraLauncher.exe
        → 偵測 .NET → 有 → Clickra.Fluent.exe（WinUI 3）
```

### 移除 Fluent
```
設定 → 移除 Fluent 介面
  → ClickraLauncher.exe
    → 偵測 Fluent → 沒有 → Clickra.exe（AOT Dashboard）
```

## 7. 故障排除（目標架構）

| 問題 | 處理 |
|------|------|
| Fluent 安裝失敗 | AOT 繼續可用，提供重試 |
| Fluent 啟動 crash | Launcher 自動 fallback 到 AOT |
| Optional 更新中斷 | Main 繼續使用舊版 |
| 移除 Optional 後重啟 | Launcher 直接走 AOT |

## 8. 本地測試 / PoC

本地 sideloading 無法安裝 related-set optional package（`0x80073D17`）。測試方式：

1. **Main MSIX**：直接 sideloading 安裝，測試 AOT 功能
2. **Optional MSIX**：需要透過 Store flight 測試
3. **Launcher 邏輯**：可用 combined MSIX（`build_store.ps1`）測試偵測和 fallback
