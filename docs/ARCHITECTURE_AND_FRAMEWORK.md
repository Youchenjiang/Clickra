# Clickra 架構與 Target Framework 決策文件

## 1. 核心組件架構

| 組件名稱 | 專案路徑 | 技術棧 | 發布模式 / 特性 |
| :--- | :--- | :--- | :--- |
| **Clickra.CLI** | `src/Clickra.CLI/Clickra.csproj` | C# (.NET 8 LTS) | **NativeAOT** (`PublishAot=true`)，零依賴原生可執行檔 |
| **ClickraShell** | `src/ClickraShell/ClickraShell.csproj` | C# (.NET 8 LTS) | **NativeAOT** (`PublishAot=true`)，Win32 COM Surrogate Server 右鍵擴充檔 |
| **Clickra.Core** | `src/Clickra.Core/Clickra.Core.csproj` | C# (.NET 8 LTS) | 共享轉換邏輯庫 (PDF, Office, 圖片處理) |
| **Clickra.Fluent** | `src/Clickra.Fluent/Clickra.Fluent.csproj` | C# (.NET 8 LTS) + WinUI 3 | **Framework-Dependent**，主儀表板與工作進度 UI |

---

## 2. Target Framework 策略與 RollForward 設定

### 2.1 基準版本：.NET 8 LTS (`net8.0-windows`)
- **選擇原因**：
  1. **高普及率**：目前絕大多數 Windows 10 / 11 終端電腦經由系統更新或常見軟體已有預裝 .NET 8，可將缺失 .NET 提示視窗的機率降低至最低。
  2. **WinUI 3 (Windows App SDK 2.x) 的最低需求**：微軟官方 C#/WinRT 投影套件最低支援版本為 .NET 8。
  3. **NativeAOT 生產級穩定性**：Windows COM/Shell 擴充套件在 .NET 8 達成熟狀態。

### 2.2 RollForward 機制 (`<RollForward>LatestMajor</RollForward>`)
各專案 `.csproj` 均配置：
```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <RollForward>LatestMajor</RollForward>
</PropertyGroup>
```
- **效果**：
  - 若電腦有 **.NET 8** ➔ 正常執行。
  - 若電腦無 .NET 8，但有 **.NET 9 / .NET 10 / 未來 .NET 11+** ➔ 自動向前向上相容執行，絕不因版本號鎖死。

---

## 3. 代碼剪裁 (Trimming) 禁忌說明

- **禁忌**：`Clickra.Fluent.csproj` **禁止開啟 `<PublishTrimmed>true</PublishTrimmed>`**。
- **原因**：WinUI 3 (Windows App SDK) 與 C#/WinRT 原生映射機制高度依賴未標註的反射與 COM 工廠。開啟 Trimming 會導致 `WinRT.Runtime.dll` 與 `Microsoft.WindowsAppSDK` 內部介面被 Linker 剔除，造成應用程式在 Win10 上進入 Managed Code (`App.xaml.cs`) 前直接觸發 `AccessViolation (0xc0000005)` 啟動閃退。
