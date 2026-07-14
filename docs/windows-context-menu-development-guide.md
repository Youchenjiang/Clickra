# Windows 11 右鍵選單開發避雷指南

> 本文件總結了 Clickra 項目在開發 Windows 11 右鍵選單整合過程中累積的所有技術經驗、陷阱與解決方案。適用於任何使用 C# NativeAOT 開發 Shell Extension 的開發者。

---

## 目錄

1. [架構概述](#1-架構概述)
2. [NativeAOT COM 開發的致命陷阱](#2-nativeaot-com-開發的致命陷阱)
3. [稀疏封裝（Sparse Package）實作要點](#3-稀疏封裝sparse-package實作要點)
4. [IExplorerCommand 介面實作完整指南](#4-iexplorercommand-介面實作完整指南)
5. [記憶體對齊與指標操作的地雷](#5-記憶體對齊與指標操作的地雷)
6. [調試技巧與工具](#6-調試技巧與工具)
7. [Python vs C# 技術選型分析](#7-python-vs-c-技術選型分析)
8. [PPT 轉 PDF 技術方案比較](#8-ppt-轉-pdf-技術方案比較)
9. [Git 工作流最佳實踐](#9-git-工作流最佳實踐)
10. [常見錯誤碼與解決方案](#10-常見錯誤碼與解決方案)

---

## 1. 架構概述

Windows 11 的右鍵選單採用了全新的 `IExplorerCommand` 架構，取代了傳統的 `IContextMenu`。主要組件：

```
┌─────────────────────────────────────────────────────────────┐
│                    Windows Explorer                          │
├─────────────────────────────────────────────────────────────┤
│  1. 偵測到檔案選取                                           │
│  2. 查詢 Sparse Package 身分認證                              │
│  3. 加載 Shell Extension DLL                                 │
│  4. 呼叫 DllGetClassObject 獲取 ClassFactory                 │
│  5. 實例化 IExplorerCommand 物件                             │
│  6. 呼叫 GetTitle / GetFlags / EnumSubCommands              │
│  7. 顯示選單並等待使用者點擊                                   │
│  8. 呼叫 Invoke 執行對應功能                                  │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              ClickraShell.dll (NativeAOT)                    │
├─────────────────────────────────────────────────────────────┤
│  ClassFactory → IExplorerCommand → IEnumExplorerCommand     │
│       ↓              ↓                      ↓                │
│  QueryInterface   GetTitle/Flags        Next/GetTitle        │
│  CreateInstance   EnumSubCommands       Reset/Clone          │
│                   Invoke                                    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              ContextTools.exe (主程式)                        │
├─────────────────────────────────────────────────────────────┤
│  ppt2pdf / merge-pdf / img2pdf / img-stitch                 │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. NativeAOT COM 開發的致命陷阱

### 2.1 為什麼不能用傳統 .NET COM 方式？

**問題**：NativeAOT 為了極致性能，移除了 .NET 的自動 COM 代理（CCW - COM Callable Wrapper）。

**後果**：
- C# 的「類別 (Class)」不會自動包裝成 COM 物件
- Explorer 拿到指標後，發現它不是標準的 **COM VTable (虛擬函式表)**
- Explorer 直接放棄，不會呼叫後續的 `GetTitle`

**錯誤訊息範例**：
```
DllGetClassObject called.  ← 成功找到大門
（後續無任何呼叫）        ← 因為 VTable 格式不對，Explorer 放棄
```

### 2.2 解決方案：手動建立 VTable

**唯一正確的做法**：手寫 C++ 等級的虛擬函式表。

```csharp
// ❌ 錯誤做法：依賴 .NET 自動轉換
[ComVisible(true)]
[Guid("...")]
public class MyCommand : IExplorerCommand { ... }

// ✅ 正確做法：手動建立 VTable
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
public static unsafe int QueryInterface(IntPtr _this, Guid* riid, IntPtr* ppv)
{
    // 手動處理介面查詢
    if (*riid == IID_IExplorerCommand || *riid == IID_IUnknown)
    {
        *ppv = _this;
        AddRef(_this);
        return 0; // S_OK
    }
    *ppv = IntPtr.Zero;
    return unchecked((int)0x80004002); // E_NOINTERFACE
}
```

### 2.3 `[UnmanagedCallersOnly]` 的限制

**陷阱**：被標記為 `[UnmanagedCallersOnly]` 的函式**不能被 C# 代碼直接調用**。

**解決方案**：將核心邏輯抽離到內部的 Helper 函式。

```csharp
// ❌ 錯誤：直接呼叫 UnmanagedCallersOnly 方法
[UnmanagedCallersOnly]
public static unsafe int GetTitle(IntPtr _this, IntPtr* ppszName)
{
    // 這裡不能呼叫其他 UnmanagedCallersOnly 方法
}

// ✅ 正確：使用 Helper 函式
private static unsafe string GetTitleInternal(IntPtr _this)
{
    // 內部邏輯
    return "My Command";
}

[UnmanagedCallersOnly]
public static unsafe int GetTitle(IntPtr _this, IntPtr* ppszName)
{
    var title = GetTitleInternal(_this);
    *pszName = Marshal.StringToCoTaskMemUni(title);
    return 0;
}
```

---

## 3. 稀疏封裝（Sparse Package）實作要點

### 3.1 什麼是稀疏封裝？

Windows 11 的新式選單要求 Shell Extension 必須透過**稀疏封裝**機制註冊，讓系統能驗證二進制檔案的身分。

### 3.2 必要組件

#### 3.2.1 AppxManifest.xml

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
  <Identity Name="Clickra.ContextTools"
            Publisher="CN=YourCertificate"
            Version="1.0.0.0" />
  <Properties>
    <DisplayName>ContextTools</DisplayName>
    <PublisherDisplayName>Your Name</PublisherDisplayName>
  </Properties>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="ContextTools"
                 Executable="ContextTools.exe"
                 EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="ContextTools"
                          Description="Context Menu Tools"
                          Square150x150Logo="app.png" />
    </Application>
  </Applications>
  <Extensions>
    <rescap:Extension Category="windows.partialTrustCertificate">
      <rescap:PartialTrustCertificate>
        <rescap:Certificate PublicKey="..." />
      </rescap:PartialTrustCertificate>
    </rescap:Extension>
  </Extensions>
</Package>
```

#### 3.2.2 Side-by-side Manifest

**關鍵**：為 `ContextTools.exe` 和 `ClickraShell.dll` 分別建立 Manifest，聲明它們與稀疏封裝的身分連結。

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity version="1.0.0.0" name="MyApplication"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and Windows 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    </application>
  </compatibility>
</assembly>
```

### 3.3 安裝腳本關鍵步驟

```powershell
# 1. 安裝憑證到「本機電腦」受信任目錄（解決 SignatureKind: None）
certutil -addstore -f "Root" "$certPath"

# 2. 註冊稀疏封裝
Add-AppxPackage -Path "$msixPath" -AllowUnsigned

# 3. 重啟 Explorer 使生效
Stop-Process -Name explorer -Force
Start-Process explorer
```

### 3.4 常見錯誤

| 錯誤 | 原因 | 解決方案 |
|------|------|----------|
| `SignatureKind: None` | 憑證未安裝到正確位置 | 安裝到 LocalMachine\Root |
| `找不到指定的模組` | 舊版安裝衝突 | 先移除舊版本再安裝 |
| Manifest 找不到 | Side-by-side Manifest 未正確嵌入 | 使用 mt.exe 嵌入 |

---

## 4. IExplorerCommand 介面實作完整指南

### 4.1 必須實作的介面

| 介面 | GUID | 用途 |
|------|------|------|
| `IExplorerCommand` | `{a5e5dd8d-b2d9-47a1-a654-4ebd0140d30a}` | 主要命令介面 |
| `IEnumExplorerCommand` | `{a888a5ec-c2fa-4ec5-8b79-5ed9ea07956b}` | 子選單列舉器 |
| `IObjectWithSelection` | `{1ac7516e-e6bb-4a69-b63f-e841904dc5a6}` | 接收選取的檔案 |
| `IUnknown` | `{00000000-0000-0000-c000-000000000046}` | 基礎介面 |

### 4.2 為什麼必須實作 IObjectWithSelection？

**關鍵發現**：Windows 11 的新式選單在決定是否顯示子選單箭頭前，會詢問組件是否支援 `IObjectWithSelection` 介面。

**行為**：
```
Explorer: "你知不知道現在選了哪些檔案？"
組件: "不支援 (E_NOINTERFACE)"
Explorer: "那我不顯示子選單箭頭了"
```

**解決方案**：在 `QueryInterface` 中實作 `IObjectWithSelection` 的支援。

### 4.3 完整的 QueryInterface 實作

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
public static unsafe int QueryInterface(IntPtr _this, Guid* riid, IntPtr* ppv)
{
    var obj = (UniversalObject*)_this;
    
    // IUnknown
    if (*riid == IID_IUnknown)
    {
        *ppv = _this;
        AddRef(_this);
        return 0;
    }
    
    // IExplorerCommand
    if (*riid == IID_IExplorerCommand)
    {
        *ppv = _this;
        AddRef(_this);
        return 0;
    }
    
    // IObjectWithSelection (關鍵！)
    if (*riid == IID_IObjectWithSelection)
    {
        *ppv = _this;
        AddRef(_this);
        return 0;
    }
    
    // IEnumExplorerCommand (僅列舉器支援)
    if (*riid == IID_IEnumExplorerCommand && obj->ObjectType == ObjectType_Enumerator)
    {
        *ppv = _this;
        AddRef(_this);
        return 0;
    }
    
    *ppv = IntPtr.Zero;
    return unchecked((int)0x80004002); // E_NOINTERFACE
}
```

### 4.4 物件身分標記

**重要**：不同類型的物件（ClassFactory / Command / Enumerator）必須嚴格區分，避免介面混淆。

```csharp
enum ComObjectType : int
{
    ClassFactory = 0,
    Command = 1,
    Enumerator = 2
}

struct UniversalObject
{
    public IntPtr VTable;           // VTable 指標
    public int RefCount;            // 引用計數
    public ComObjectType ObjectType; // 物件類型標記
    public int Data;                // 自定義資料
    // ... 其他欄位
}
```

---

## 5. 記憶體對齊與指標操作的地雷

### 5.1 64 位元結構體填充（Padding）

**致命陷阱**：在 64 位元系統下，結構體中間會有自動填充位元。

```csharp
// ❌ 錯誤：手動計算偏移量
struct BadStruct
{
    public IntPtr VTable;    // 8 bytes (offset 0)
    public int RefCount;     // 4 bytes (offset 8)
    // 4 bytes padding      // (offset 12)
    public int Data;         // 4 bytes (offset 16)
}

// 手動計算：basePtr + 8 + 4 = basePtr + 12 ← 錯誤！
int* pData = (int*)(basePtr + 12); // 讀到的是 padding，不是 Data

// ✅ 正確：讓編譯器處理偏移量
var obj = (BadStruct*)basePtr;
int data = obj->Data; // 編譯器自動計算正確偏移量
```

### 5.2 結構體大小計算

```csharp
// ❌ 錯誤：假設結構體沒有填充
int size = IntPtr.Size + sizeof(int) + sizeof(int); // 16 bytes

// ✅ 正確：使用 Marshal.SizeOf
int size = Marshal.SizeOf<UniversalObject>(); // 包含填充的正確大小
```

### 5.3 記憶體分配的安全做法

```csharp
// ❌ 錯誤：分配過小的記憶體
IntPtr mem = Marshal.AllocCoTaskMem(4); // 只分配 4 bytes

// ✅ 正確：分配完整結構體大小
int objSize = Marshal.SizeOf<UniversalObject>();
IntPtr mem = Marshal.AllocCoTaskMem(objSize);
Marshal.StructureToPtr(myObject, mem, false);
```

### 5.4 物件隔離原則

**重要**：「命令物件」與「列舉器物件」的記憶體結構必須完全隔離。

```csharp
// ❌ 錯誤：共用同一個記憶體結構
IntPtr CreateCommand() => Marshal.AllocCoTaskMem(Marshal.SizeOf<UniversalObject>());
IntPtr CreateEnumerator() => Marshal.AllocCoTaskMem(Marshal.SizeOf<UniversalObject>()); // 同樣大小

// ✅ 正確：使用不同的結構體
struct CommandObject { ... }
struct EnumeratorObject { ... }

IntPtr CreateCommand() => Marshal.AllocCoTaskMem(Marshal.SizeOf<CommandObject>());
IntPtr CreateEnumerator() => Marshal.AllocCoTaskMem(Marshal.SizeOf<EnumeratorObject>());
```

---

## 6. 調試技巧與工具

### 6.1 日誌系統設計

```csharp
internal static class ComLogger
{
    private static readonly string LogPath = 
        Path.Combine(Path.GetTempPath(), "ClickraShell.log");
    
    public static void Log(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            File.AppendAllText(LogPath, $"[{timestamp}] {message}\n");
        }
        catch { /* 靜默失敗 */ }
    }
    
    public static void LogQI(string objectType, Guid iid)
    {
        Log($"QI: {objectType} -> {iid}");
    }
    
    public static void LogMethod(string method, int result)
    {
        Log($"{method} -> 0x{result:X8}");
    }
}
```

### 6.2 必須記錄的關鍵事件

| 事件 | 日誌格式 | 意義 |
|------|----------|------|
| DllGetClassObject | `DllGetClassObject called` | 系統找到 DLL |
| QueryInterface | `QI: [Type] -> [IID]` | 介面詢問 |
| GetTitle | `GetTitle -> [Title]` | 命令標題 |
| GetFlags | `GetFlags -> [Flags]` | 旗標設定 |
| EnumSubCommands | `EnumSubCommands called` | 子選單請求 |
| EnumNext | `EnumNext: [Index]` | 子項目列舉 |
| SetSelection | `SetSelection: [Count] files` | 檔案選取 |
| Invoke | `Invoke: [Index]` | 命令執行 |

### 6.3 命令列診斷工具

```powershell
# 查看日誌
Get-Content "$env:TEMP\ClickraShell.log" -Tail 50

# 搜尋特定事件
Select-String -Path "$env:TEMP\ClickraShell.log" -Pattern "QI|EnumSubCommands|Invoke"

# 清除舊日誌
Remove-Item "$env:TEMP\ClickraShell.log" -ErrorAction SilentlyContinue
```

### 6.4 安裝後驗證流程

```powershell
# 1. 等待 Explorer 完成加載
Start-Sleep -Seconds 3

# 2. 檢查日誌是否存在
if (Test-Path "$env:TEMP\ClickraShell.log") {
    Write-Host "✅ 日誌已建立"
} else {
    Write-Host "❌ 日誌不存在，DLL 可能未被加載"
}

# 3. 檢查 DllGetClassObject 是否被呼叫
$log = Get-Content "$env:TEMP\ClickraShell.log" -ErrorAction SilentlyContinue
if ($log -match "DllGetClassObject called") {
    Write-Host "✅ DLL 已被 Explorer 載入"
} else {
    Write-Host "❌ DLL 未被載入，請檢查身分認證"
}
```

---

## 7. Python vs C# 技術選型分析

### 7.1 啟動時間比較

| 方案 | 啟動時間 | 原因 |
|------|----------|------|
| Python 直譯執行 | 1-3 秒 | 需要初始化 Python 環境 |
| PyInstaller --onefile | 1-2 秒 | 每次執行都要解壓縮到暫存 |
| C# .NET Framework | 0.1-0.2 秒 | 需要載入 .NET Runtime |
| C# NativeAOT | 0.02-0.05 秒 | 原生編譯，無 Runtime 依賴 |

### 7.2 檔案體積比較

| 方案 | 體積 | 包含內容 |
|------|------|----------|
| Python 腳本 | 10-50 KB | 純腳本 |
| PyInstaller --onefile | 10-20 MB | Python 直譯器 + 腳本 + 依賴 |
| C# .NET Framework | 50-200 KB | 純代碼（依賴系統 .NET） |
| C# NativeAOT | 2-5 MB | 代碼 + .NET Runtime |

### 7.3 右鍵選單場景的關鍵差異

```
使用者體驗：
├─ Python (PyInstaller)
│  ├─ 右鍵 → 等待 1-2 秒 → 選單出現
│  ├─ 點擊 → 等待 1-2 秒 → 執行開始
│  └─ 總延遲：2-4 秒 ← 體驗差
│
└─ C# (NativeAOT)
   ├─ 右鍵 → 0.05 秒 → 選單出現
   ├─ 點擊 → 0.05 秒 → 執行開始
   └─ 總延遲：0.1 秒 ← 體驗佳
```

### 7.4 COM 操作相容性

| 操作 | Python (win32com) | C# (System.Runtime.InteropServices) |
|------|-------------------|-------------------------------------|
| PPT 轉 PDF | 可用，但不穩定 | 穩定，官方支援 |
| 錯誤處理 | 模糊 | 精確 |
| 型別安全 | 弱型別 | 強型別 |

---

## 8. PPT 轉 PDF 技術方案比較

### 8.1 方案總覽

| 方案 | 排版準確度 | 體積 | 依賴 | 成本 |
|------|------------|------|------|------|
| COM 物件（本機 PPT） | 100% | 0 | 需安裝 Office | 免費 |
| 商業庫 (Aspose.Slides) | 100% | 2-5 MB | 無 | 付費（有浮水印） |
| 開源庫 (python-pptx) | 60-80% | 10-50 MB | Python | 免費 |
| LibreOffice Headless | 85-95% | 200+ MB | LibreOffice | 免費 |
| 雲端 API | 95-100% | 0 | 網路 | 付費 |

### 8.2 COM 物件方案詳解

**原理**：
1. 程式在背景「隱藏啟動」PowerPoint 進程
2. 指示 PowerPoint 打開 .pptx 檔案
3. 指示 PowerPoint 另存為 PDF
4. 關閉 PowerPoint

**優點**：
- 排版 100% 完美（使用微軟自家引擎）
- 無需額外依賴
- 免費

**缺點**：
- 必須安裝 Microsoft Office
- 無法在無 Office 的環境使用

**C# 實作範例**：

```csharp
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

public static void ConvertPptxToPdf(string inputPath, string outputPath)
{
    var app = new PowerPoint.Application();
    app.Visible = Microsoft.Office.Core.MsoTriState.msoFalse;
    
    try
    {
        var presentation = app.Presentations.Open(inputPath);
        presentation.SaveAs(outputPath, PowerPoint.PpSaveAsType.ppSaveAsPDF);
        presentation.Close();
    }
    finally
    {
        app.Quit();
    }
}
```

### 8.3 為什麼開源庫排版會跑掉？

**原因**：微軟沒有完全公開 PPT 的排版私有算法。

**具體問題**：
- 字體替換不正確
- 複雜圖形位置偏移
- 動畫截圖失敗
- 表格欄寬不對

---

## 9. Git 工作流最佳實踐

### 9.1 提交前的準備

```powershell
# 1. 確保 .gitignore 正確（使用 UTF-8 編碼）
@"
bin/
obj/
*.exe
*.pdb
"@ | Out-File -FilePath .gitignore -Encoding utf8

# 2. 移除誤提交的檔案
git rm -r --cached bin/
git rm -r --cached obj/
```

### 9.2 PowerShell 編碼問題

**陷阱**：PowerShell 的 `echo` 指令會產生帶有 BOM 的 UTF-16 編碼。

**解決方案**：

```powershell
# ❌ 錯誤：產生 UTF-16 BOM
echo "bin/" > .gitignore

# ✅ 正確：使用 Out-File 指定 UTF-8
"bin/`nobj/" | Out-File -FilePath .gitignore -Encoding utf8
```

### 9.3 Conventional Commits 格式

```
<type>(<scope>): <subject>

<body>

<footer>
```

**常用 type**：
- `feat`: 新功能
- `fix`: Bug 修復
- `docs`: 文件更新
- `chore`: 雜項維護
- `refactor`: 重構

**範例**：
```
feat(context-menu): implement C# native context-tools unified CLI

- Created ContextTools C# project with zero Python startup latency.
- Implemented PPTX > PDF via COM.
- Implemented PDF and Image merging via PdfSharp.
- Added PowerShell installer script to bind to Registry.
```

### 9.4 提交批次原則

**規則**：每個功能必須分開提交，即使它們在同一個檔案中。

```powershell
# ❌ 錯誤：一次提交所有變更
git add .
git commit -m "update everything"

# ✅ 正確：分批次提交
git add .gitignore
git commit -m "chore: add .gitignore"

git add ContextTools.csproj
git commit -m "chore: add C# project file"

git add Program.cs
git commit -m "feat(context-menu): implement core functionality"
```

---

## 10. 常見錯誤碼與解決方案

| 錯誤碼 | 名稱 | 常見原因 | 解決方案 |
|--------|------|----------|----------|
| `0x00000000` | S_OK | 成功 | - |
| `0x80004002` | E_NOINTERFACE | 不支援請求的介面 | 實作該介面或回傳正確的 VTable |
| `0x80004005` | E_FAIL | 一般失敗 | 檢查記憶體對齊與指標 |
| `0x8007007E` | ERROR_MOD_NOT_FOUND | 模組找不到 | 檢查路徑或依賴 |
| `0x80070005` | E_ACCESSDENIED | 存取被拒絕 | 檢查權限設定 |
| `0x80040154` | CLASS_NOTREG | 類別未註冊 | 檢查 CLSID 註冊 |
| `0x80004001` | E_NOTIMPL | 未實作 | 回傳 E_NOTIMPL 而非 S_OK |

### 特殊情況處理

```csharp
// 對於未實作的功能，必須回傳 E_NOTIMPL 而非 S_OK
[UnmanagedCallersOnly]
public static int GetIcon(IntPtr _this, IntPtr* phIcon)
{
    // ❌ 錯誤：回傳 S_OK 但沒有設定 phIcon
    // return 0;
    
    // ✅ 正確：明確回傳 E_NOTIMPL
    *phIcon = IntPtr.Zero;
    return unchecked((int)0x80004001); // E_NOTIMPL
}
```

---

## 附錄：完整安裝腳本範例

```powershell
# setup_context_menu.ps1
param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$installDir = Join-Path $env:LOCALAPPDATA "Clickra"
$certPath = Join-Path $PSScriptRoot "clickra.cer"
$msixPath = Join-Path $PSScriptRoot "Clickra.msix"

if ($Uninstall) {
    # 移除選單
    Remove-Item -Path "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\Clickra_PPT2PDF" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\Clickra_MergePDF" -Recurse -Force -ErrorAction SilentlyContinue
    
    # 移除 SendTo 快捷方式
    $sendToPath = [Environment]::GetFolderPath("SendTo")
    Remove-Item "$sendToPath\Clickra 合併 PDF.lnk" -Force -ErrorAction SilentlyContinue
    Remove-Item "$sendToPath\Clickra 圖片轉 PDF.lnk" -Force -ErrorAction SilentlyContinue
    
    Write-Host "✅ 已移除 Clickra 右鍵選單"
    exit 0
}

# 安裝流程
Write-Host "=========================="
Write-Host "  Clickra 右鍵選單安裝"
Write-Host "=========================="

# 1. 建立安裝目錄
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# 2. 複製執行檔
Copy-Item (Join-Path $PSScriptRoot "Clickra.exe") $installDir -Force
Copy-Item (Join-Path $PSScriptRoot "ClickraShell.dll") $installDir -Force

# 3. 安裝憑證
Write-Host "正在安裝憑證..."
certutil -addstore -f "Root" $certPath

# 4. 註冊右鍵選單
Write-Host "正在註冊右鍵選單..."

# PPTX 轉 PDF
$pptKey = "HKCU:\Software\Classes\SystemFileAssociations\.pptx\shell\Clickra_PPT2PDF"
New-Item -Path $pptKey -Force | Out-Null
Set-ItemProperty -Path $pptKey -Name "(Default)" -Value "⚡ 轉為 PDF"
$pptCmd = "cmd /c `"$installDir\Clickra.exe`" ppt2pdf `"%1`""
New-Item -Path "$pptKey\command" -Force | Out-Null
Set-ItemProperty -Path "$pptKey\command" -Name "(Default)" -Value $pptCmd

# 5. 建立 SendTo 快捷方式（多檔案合併用）
Write-Host "正在建立 SendTo 快捷方式..."
$sendToPath = [Environment]::GetFolderPath("SendTo")
$wshell = New-Object -ComObject WScript.Shell

# 合併 PDF
$shortcut = $wshell.CreateShortcut("$sendToPath\⚡ 合併 PDF.lnk")
$shortcut.TargetPath = "$installDir\Clickra.exe"
$shortcut.Arguments = "merge-pdf"
$shortcut.WorkingDirectory = $installDir
$shortcut.Save()

# 圖片轉 PDF
$shortcut = $wshell.CreateShortcut("$sendToPath\⚡ 圖片轉 PDF.lnk")
$shortcut.TargetPath = "$installDir\Clickra.exe"
$shortcut.Arguments = "img2pdf"
$shortcut.WorkingDirectory = $installDir
$shortcut.Save()

Write-Host ""
Write-Host "✅ 安裝完成！"
Write-Host "  - 右鍵 .pptx 檔案 → ⚡ 轉為 PDF"
Write-Host "  - 選取多個 PDF → 右鍵 → 傳送到 → ⚡ 合併 PDF"
Write-Host "  - 選取多張圖片 → 右鍵 → 傳送到 → ⚡ 圖片轉 PDF"
```

---

## 參考資源

- [IExplorerCommand 接口 (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/shell/iexplorercommand)
- [IEnumExplorerCommand 接口 (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/shell/ienumexplorercommand)
- [IObjectWithSelection 接口 (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/shell/iobjectwithselection)
- [稀疏封裝 (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/msix/sparse-package)
- [NativeAOT COM 支援 (Microsoft Docs)](https://learn.microsoft.com/en-us/dotnet/core/ready-to-run-compatibility)

---

*本文件最後更新：2026-07-11*
*維護者：Clickra Team*
