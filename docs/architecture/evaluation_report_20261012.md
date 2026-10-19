# Clickra 架構審查與 Target Architecture 提案

> 基準：`HEAD 6a789c5`、`.tmp-architecture-graph/` 內的 brief、overview / full graph、`graph-data.json`（869 edges）以及 `semantic-edges.json`（Roslyn）。
> 判斷依據是**實際的 symbol 依賴與擁有權**，不是資料夾名稱。每一項主張都附上可以重現的證據（檔案 → 檔案 / symbol）。

---

## 0. 結論（TL;DR）

1. **四層圖會誤導人。** 它把「誰呼叫誰」畫成了上下層，但真正的耦合軸是**能力（capability）／bounded context**：PDF Translation、Task Journal、Settings、Office Engine。這幾個 cluster 內部都是強連通的，彼此之間反而相對獨立。
2. **目前這張圖低估了耦合，也有少量誤報。** `graph-data.json` 是以型別名稱比對的 analyzer，**看不到 static member 呼叫**（`ClickraStorage.X()`、`Localization.T()`、`ProgressWindow.Show()`）。改用 Roslyn semantic edges（去掉 namespace symbol）後，**多出 37 條 context edge**，其中幾條正好是最關鍵的違規。另外有約 5 條是名稱碰撞造成的誤報（Fluent 的 XAML `x:Name="HistoryLayout"` / `"SettingsLayout"` 被當成 Core 型別）。
3. **「One Workflow per Use Case」已經達成；「One Presentation State per Screen」還沒有。** Native Progress 透過 `NativeConversionExecutionAdapter` 用一串 per-command 的 `if` 直接驅動 use case；Fluent 走的則是 `ConversionExecutionController`。兩個 renderer 各有一套執行用的 presenter。
4. **`FileProcessor`（放在 Core root）是一個隱藏的第二 application gateway。** 它會偷偷寫入 `ClickraStorage.SetActiveRecordIndex`，也會讀取 Language／OutputDir 設定。這是 Application → Execution 唯一的接縫，介面卻是 `Dictionary<string, object>`，而且帶有持久化副作用。
5. **建議採用：Modular Monolith（以 bounded context 垂直切）＋ 只在有狀態／IO 的邊界用 Ports & Adapters ＋ 每個 screen 一個 Presentation model ＋ 薄 Host。** 水平分層只存在於 context 內部，不再是全域的四條 band。

---

## 1. 對目前架構模型的批判

### 1.1 四層模型的根本問題

| 問題 | 證據 |
|---|---|
| **「Technical / Storage」不是一層，而是六種東西**：執行引擎、外部整合、持久化、產品 schema、跨切面工具、只給 Native 用的 UI 元件。 | 154/264 個檔案都在這一層，但彼此之間幾乎沒有共同的依賴方向。 |
| **底層依賴上層概念**：`Localization`（跨切面）→ `ClickraStorage`（持久化），用來讀目前的 Language。 | `Localization.cs:81,203` 呼叫 `ClickraStorage.GetSetting(ClickraSettings.Language)` |
| **「Storage」裡的 `ClickraSettings` 其實是產品 schema。** 它包含 keys、defaults、`NormalizePdfCompressLevel`、`ClampNumericSetting`、Office engine 常數。processors、use cases、infra 都引用它，所以圖上的「Processors → Storage」大多是**誤判**：它們依賴的是產品常數，不是持久化。 | `OfficeEngineDetector.cs:66-91`、`ImgCompressUseCase.cs:38`、`CompressPdfUseCase.cs:38` |
| **「Shared Models」並不 shared。** 10 個檔案裡，9 個只被 PDF Translation（加上 `PdfTranslateProcessor`）使用。剩下的 `NumericSettingRange` 屬於 Settings。而且 `PdfParagraph` 反向呼叫 `PdfParagraphAnalysis`，形成 cycle。 | `Models/PdfParagraph.cs → Paragraphs/PdfParagraphAnalysis.cs` |
| **Context 是照檔名切的，不是照擁有權。** `ClickraStorage` 是一個 `static partial class`，分散在 4 個檔案，被分到 3 個「context」裡。partial class 會自動產生雙向邊，結果 5 個 Storage context 全部落在**同一個 SCC**。 | SCC 分析：Storage — Facade / History / Legacy / Settings / Task 互相可達 |
| **PDF Translation 的 9 個子 context 是同一個 SCC。** Paragraphs ↔ Diagrams ↔ GrayPrompts ↔ Tables ↔ Layout ↔ Rendering ↔ TextProcessing 互相引用，拆成 9 個框只是在畫內部檔案分類，不是邊界。 | 例：`PdfPageParagraphBuilder → PdfGrayPromptMarker`、`PdfGrayPromptGeometry → PdfMaskGeometry`、`PdfTranslationLayoutPlanner → PdfTranslatedParagraphRenderer` |
| **Contracts ↔ Use Cases cycle。** `ConversionUseCases`（catalog，屬於組裝職責）跟 contracts 放在同一個 context，所以 Contracts → 每個 UseCase → Contracts。 | `ConversionUseCases.cs` new 了 19 個 use case |

### 1.2 Presentation 的問題

- **一半以上是 Native renderer 的程式碼。** `Presentation/Native/DashboardLayout`、`HistoryLayout`、`Layout/LayoutRect`、`Layout/SettingsLayout`、`Presentation/Shared/VectorIcons` 的實際使用者**全都只有 Native**。Fluent 對 `HistoryLayout`／`SettingsLayout` 的「依賴」是 XAML `x:Name` 撞名造成的誤報（`MainPage.xaml.cs:45,229,267`）。
- **Presentation 洩漏下游型別。**
  - `SettingsPresentationController → LibreOfficeEnginePackage`（Infrastructure）
  - `SettingDescriptor/SettingsPresentationController → ImageCompressionLevel`（Image Processor enum）
- **Native Progress 繞過 Presentation**（見 §2.1）。
- `HistoryFeed`、`SettingDescriptor`、`TaskProgressController` 放在 Presentation **是正確的**（它們產生可直接顯示的狀態與在地化文字），問題只在命名與依賴。

### 1.3 Application 的問題

- 59 個 public type、只有 1 個 internal，**沒有任何 port**。所謂的「Controller」（`SettingsController`、`StoragePathController`、`HistoryController`、`HistoryQueryController`、`ParkedTaskController`、`ConversionTaskProgressController`、`ConversionTaskCleanup`）全都是**對 static `ClickraStorage` facade 的 static pass-through**。Application 實際上直接依賴持久化，只是多包了一層名字。
- Storage 型別洩漏到 Application 的 API：`ClickraStorage.HistoryEntry`、`ConversionStatus`、`ClickraStorage.CompleteTaskRequest`、`ParkedRetentionInfo`。
- **同一件事有兩個擁有者**：「目前處理到第幾個檔案」的 legacy active-record index，同時由
  - `FileProcessor.PrepareOptions` 注入的 callback（`ClickraStorage.SetActiveRecordIndex`），以及
  - `ConversionTaskProgressController.RecordFileStarting`（同樣寫 `SetActiveRecordIndex` + `SetTaskIndex`）

  兩邊寫入。

### 1.4 Product surfaces 的問題

- **「Headless CLI」並不 headless。** `ClickraCli`／`ClickraStartup` 是 `Clickra.exe` 的 **front controller**：`Main → ClickraStartup.Run`，依 quiet flag 決定走 quiet runner 還是 `ProgressWindow.Show(...)`（semantic edge：Headless CLI → Native Progress ×16、→ Native Dashboard ×18）。
- 它有自己的 `switch(command)`，而且 Office／PDF 的副檔名是**寫死的**（`ClickraCli.cs:182,187,192,240`），和 `ConvertCommandMetadata` 重複（目前值一致，但有兩個來源）。
- `GlobalUsings.Architecture.cs` 讓 CLI 和 Fluent 都 `global using Clickra.Core.Storage;`。等於整個 surface 都能直接用到持久化。
- 現有的 architecture guards 是 **regex 字串規則加上 baseline**（例如 `\bClickraStorage\.StartTask\(`）。它們抓不到型別層級的依賴，也抓不到換個呼叫形式的違規。

### 1.5 量測本身的問題（先修這個）

| 指標 | type-name analyzer（現用） | Roslyn semantic（去掉 namespace symbol） |
|---|---|---|
| file edges | 869 | 1,289 |
| context edges | 188 | 220 |
| 漏掉的 context edge | — | **37** |
| 誤報 | ≥5（XAML 撞名） | — |

漏掉的 37 條包含：Localization 被 15 個 context 使用、`Localization → ClickraStorage`、`FileProcessor → ClickraStorage.SetActiveRecordIndex`、`LibreOfficeEngineInstaller → ClickraStorage.SaveSetting`、`Settings Application → ClickraStorage.*`、CLI → Dashboard/Progress window、Settings Presentation → Infrastructure。

**目前的圖看起來比實際乾淨。**

---

## 2. 結構上最混亂或分錯群組的地方（依嚴重度排序）

### 2.1 Native Progress 擁有一套平行的執行用 presenter

`src/Clickra.CLI/Progress/NativeConversionExecutionAdapter.cs`（475 行）：

- 用一串 `if (command.Equals(DecryptPdfUseCase.CommandName)) … else if (SplitPdf…) …` 依 command 分派。這在本質上就是 brief 明列禁止的「Native `switch(command)` product execution router」，只是換了寫法。
- 直接呼叫 `ConversionUseCases.GetRequired(...)`、`useCase.Plan(...)`、`ExecuteAsync(...)`。
- 每個 command 的進度文案（`cli_progress_*`）、observer 行為都放在 Native 端，Fluent 端則另外實作。這正是**行為分歧的溫床**。

Fluent 使用的是 `Presentation/Convert/ConversionExecutionController`。Native Progress 從來沒有用它（semantic IN：只有 Fluent）。

### 2.2 `FileProcessor` 是放錯位置、帶副作用的 gateway

`src/Clickra.Core/FileProcessor.cs`（Core root）：

- 15 個 use case 都經過它呼叫 processor。
- `PrepareOptions` 會注入 `FileStartingCallbackKey => ClickraStorage.SetActiveRecordIndex(index)`。也就是說，**執行引擎的 facade 在寫 task 持久化**。
- 有好幾個 fallback overload 會讀 `ClickraStorage.GetSetting(Language)`、`GetDataDir()`、`GetOutputDir()`。
- 介面是 `Dictionary<string, object>`＋魔術字串 key（`"pages"`、`"targetLang"`、`"strip_fonts"`）。

它其實就是「Conversion → Engines」的 port，只是沒有被當成 port 來設計。

### 2.3 Storage 被當成全域 service locator

static `ClickraStorage` 被 Application、Infrastructure、Localization、FileProcessor 直接呼叫；UI 也透過 global using 看得到它。真正的持久化擁有者（`TaskQueueStore`、`ParkedTaskCoordinator`、`SettingsStore`…）已經是 internal，**這部分做得對**。問題在於 facade 本身是 public 的全域入口。

### 2.4 Office Engine 是一個被拆散的 bounded context

「Office 引擎可用性」這件事散在四個地方：

- `Infrastructure/LibreOfficeEngineInstaller.cs`（814 行，下載、安裝、移除，直接 `SaveSetting`）
- `Processors/FileProcessing/OfficeEngineDetector.cs`、`LibreOfficeHelper.cs`、`PowerShellHelper.cs`
- `Application/LibreOfficeSettingsController.cs`
- `ClickraSettings` 裡的 `LibreOffice*` keys

依賴鏈是：Settings Application → Office Processors（`LibreOfficeHelper`、`OfficeEngineDetector`）；Presentation → Infrastructure（`LibreOfficeEnginePackage`）。這些是同一個能力，應該是同一個 module。

### 2.5 PDF Translation 是一個獨立子系統，卻被當成「Processors 的子資料夾」

- 約 75 個檔案、全產品最大的單一能力，內部是一個 SCC。
- 它的 domain model（`PdfParagraph`、`PdfLetter`、`MathFormula`、`RenderedChar`、`Translation*Diagnostics`）放在 `Models/`。
- 它的 provider（`Translation/`）放在另一個頂層資料夾。
- `Clickra.Core.Processors` 這個 namespace 底下有 92 個檔案，涵蓋 Annotations／Diagrams／Tables… 等子資料夾，**namespace 和資料夾對不上**。
- `PdfProcessors → PdfParagraphBlockMerger / FormulaLiteralCleaner`：外部模組直接伸手進 translation 的內部。

---

## 3. Target Architecture

### 3.1 模型：兩個正交的軸

```
軸 A（垂直，主軸）：Bounded Context / Module —— 誰擁有什麼
軸 B（水平，次軸）：每個 context 內部的 ring —— Contracts → Application → Adapters
```

全域只保留**兩條**硬性方向規則：

1. **Host（組裝）→ 一切**；除了 Host 之外，**沒有人依賴 Host**。
2. **Kernel 不依賴任何東西**；所有人都可以依賴 Kernel。

其餘的方向都由「context 之間的允許清單」定義（§6），不再由「第幾層」定義。

### 3.2 依賴方向圖

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ HOSTS（executable / composition roots；連接 adapter 和 port）                 │
│  Clickra.exe (Native)          Clickra.Fluent.exe     ClickraShell.dll  Launcher│
│  ├ Entry / CommandLine front controller                 (只連結 Kernel 的     │
│  ├ Quiet runner (headless)                               metadata 檔，        │
│  └ Native UI renderers + Win32 + Native layout          不參照 Core)          │
└───────┬─────────────────────────────┬────────────────────────────────────────┘
        │ renderers                    │ quiet runner
        ▼                              │
┌───────────────────────────────┐      │
│ PRESENTATION（每個 screen 一個 │      │
│ model，Native/Fluent 共用）    │      │
│ Convert · Progress/Session ·   │      │
│ History · Settings · Dashboard │      │
└───────┬───────────────────────┘      │
        ▼                              ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ CONTEXT APPLICATION APIs（public 表面只有這一圈）                              │
│  Conversion        Tasks (Journal/History)   Settings        OfficeEngine     │
│  catalog, use      queries, parked-task      typed settings  readiness,       │
│  cases, lifecycle  commands, retention       output paths    install/uninstall│
│      │ ports ↓            ▲ implements             ▲              ▲           │
└──────┼────────────────────┼────────────────────────┼──────────────┼───────────┘
       │ ITaskJournal ──────┘   ISettingsReader ─────┘              │
       │ (ports 由 Conversion 擁有；adapter 在各 context 的            │
       │  Persistence ring 內，由 Host 接上)                          │
       ▼                                                            │
┌──────────────────────────────────────────────────────────────────────────────┐
│ ENGINES（純執行函式庫：明確的輸入/選項 → 輸出檔；不碰 settings/storage）      │
│  Pdf   Image   Markdown   Office(exec) ──────────────────────────┘           │
│  PdfTranslation (pipeline + model + analysis + rendering + Providers port)    │
│  Engines.Common (processor bases, progress, backup scope, execution options)  │
└──────────────────────────────────────────────────────────────────────────────┘

  橫向（可被任何人依賴，自己不往外依賴）：
  KERNEL      : ConvertCommandMetadata、command ids、SettingKeys/Schema、NumericSettingRange、
                Localization（純查表：T(key, lang)）、ChineseTextConverter
  DIAGNOSTICS : ClickraDebug（只寫 log，不依賴其他 module）
  PLATFORM    : 給 UI 用的 OS 服務：ClickraShellProcess、SystemPaths、PdfPagePreview(thumbnail)
                （只允許 Hosts / renderers 使用）
```

重點：

- **Engines 是 library，不是 adapter。** 它們不需要實作 Application 的 port，因為它們是純運算（明確輸入 → 檔案輸出），可以直接測試。Conversion 直接依賴各 engine 的 typed facade。強制 port 化只會增加儀式感，沒有換到可測性。
- **Port 只放在有狀態或有副作用的邊界**：task 持久化（`ITaskJournal`）、設定讀取（`ISettingsReader`／snapshot）、Office 引擎安裝（網路＋MSI）、翻譯 provider（網路，`ITranslationEngine` 已存在）。
- **Persistence 不是一層，而是 Tasks 和 Settings 兩個 context 內部的 ring。** 共用的 `StorageRuntime`（data dir、mutex）是它們共享的 internal 基礎。

### 3.3 推薦的資料夾與 namespace（namespace 等於資料夾，也等於 module）

```text
src/Clickra.Core/
  Kernel/
    Commands/        ConvertCommandMetadata.cs (linked from src/Shared), CommandIds
    Settings/        SettingKeys + SettingSchema (由 ClickraSettings 拆出), NumericSettingRange
    Localization/    Localization (pure), string tables, ChineseTextConverter
  Conversion/
    Contracts/       ConversionRequest/Plan/Result/Interaction/Observer/Capability/Progress
    Workflow/        ConversionUseCaseBase, PerFile…, SingleOutput…, ConversionTaskLifecycle, FailureClassifier
    UseCases/        Pdf/  Image/  Office/  Markdown/
    Catalog/         ConversionUseCases, ConversionUseCaseRegistry, ConvertCommandRegistry
    Ports/           ITaskJournal, ISettingsReader (或 ConversionSettingsSnapshot)
  Tasks/
    Application/     TaskHistoryQueries, ParkedTaskCommands, HistoryCommands, records/DTOs
    Persistence/     (internal) TaskQueueStore, TaskRecordStore, ParkedTaskCoordinator, TaskPruningCoordinator,
                     TaskRetentionPolicy, HistoryStore, LegacyActiveRecordStore, TaskJournal : ITaskJournal
  Settings/
    Application/     SettingsService (typed), OutputLocations
    Persistence/     (internal) SettingsStore, StoragePathResolver
  Persistence/Runtime/  (internal) StorageRuntime
  Compatibility/     ClickraStorage facade（凍結；只給 Host 和測試使用；之後可 internal 化）
  OfficeEngine/
    Application/     OfficeEngineService (原 LibreOfficeSettingsController)
    Detection/       OfficeEngineDetector
    Install/         LibreOfficeEngineInstaller (+ ports 給 settings 寫入)
    Interop/         LibreOfficeHelper, PowerShellHelper
  Engines/
    Common/          IFileProcessor, Single/MultiFileProcessorBase, ProcessorExecutionOptions,
                     ExecutionLocalizationOptions, FileBackupScope, ProgressCalculator, PostProcessor
    Pdf/             Compression*, Merge, Split, Decrypt, VisualSplitModel → 見 §5
    Image/           Image*Processor, WicImageHelper, ImageCompressionOptions
    Markdown/        Markdown*Processor, templates, MarkdownRenderProfile, MarkdownPdfOptions
    Office/          Word/Ppt/ExcelToPdfProcessor, OfficeConversionOptions
    DocumentFonts/   ClickraFontResolver
    PdfTranslation/
      PdfTranslationPipeline.cs (唯一 public 入口) + PdfTranslateProcessor
      Model/         PdfParagraph, PdfLetter, MathFormula, MathLetter, RenderedChar, ParagraphAnnotationInfo,
                     Translation*Diagnostics
      Analysis/      Paragraphs/ Tables/ Diagrams/ GrayPrompts/ Layout/
      Text/          原 TextProcessing
      Rendering/     Masking/ Paragraph/ PdfRewrite/ + Fonts/FontUtilities
      Annotations/
      Providers/     原 Translation/*（ITranslationEngine + Google/MyMemory/Fallback/…）
  Presentation/
    Convert/ Progress/ History/ Settings/ Dashboard/   （只放 Native 與 Fluent 共用的 screen model）
  Platform/          ClickraShellProcess, SystemPaths, PdfPagePreview (原 Rendering/PdfPageThumbnailRenderer)
  Diagnostics/       ClickraDebug

src/Clickra.CLI/  （概念上是 Clickra.Native host；不改 csproj／exe 名稱）
  Host/              ClickraStartup, CommandLine/ (ClickraCli.*), Quiet/ (quiet runner)
  Ui/Dashboard/ Ui/Progress/
  Ui/Layout/         DashboardLayout, HistoryLayout, LayoutRect, SettingsLayout, VectorIcons  ← 從 Core 移入
  Ui/Win32/          Win32, UIHelper, LocalizedUiFontSelector
```

> 關於「要不要每個 feature 都有 Presentation/Application/Persistence 子資料夾」（brief 第 7 題）：建議只對 **Tasks、Settings、OfficeEngine** 這三個有持久化的 context 用垂直切法。**Presentation 保持一個獨立 module**，因為它的消費者是兩個 renderer，跨 context 組合（例如 Dashboard 同時要 Convert、History、Settings）是它的本質。把它拆進各 context 反而會造成 Presentation ↔ Presentation 的橫向依賴。

---

## 4. 從目前 context 到 target 的對照

| 目前 context（檔案數） | Target | 備註 |
|---|---|---|
| Native Dashboard (18)、Native Progress (9) | Hosts/Native · Ui | Progress 的執行改用 Presentation session |
| Headless CLI (4) | Hosts/Native · Host/CommandLine + Quiet | 重新命名：它是 front controller |
| Native Bootstrap / Shared UI (2)、Native Platform Adapters (3) | Hosts/Native · Ui/Win32 | |
| Fluent UI / Platform (13)、Fluent Controls (3) | Hosts/Fluent | |
| Shell Extension (5)、Launcher (1) | Hosts（不變） | |
| Convert / Dashboard / History / Settings / Progress Presentation | Presentation/* | 名稱改成 `*Model` / `*Session`（§5） |
| Native Layout Primitives (2)、Layout Primitives (2)、Shared Presentation Primitives (1) | **移出 Core** → Hosts/Native · Ui/Layout | 只有 Native 使用 |
| Conversion Contracts / Registry (11) | Conversion/Contracts + Workflow + Catalog | catalog 拆開以消除 cycle |
| PDF / Image / Office-Markdown Use Cases | Conversion/UseCases/* | |
| Task Lifecycle / Progress (3) | Conversion/Workflow（Lifecycle）；`ConversionTaskProgressController`、`ConversionTaskCleanup` → 併入 Tasks API／Lifecycle | 消除 index 的雙寫 |
| Shared Product Model (1) | Kernel/Commands | 仍以 linked file 提供給 Shell |
| Settings / Path Application (4) | Settings/Application；`LibreOfficeSettingsController` → OfficeEngine/Application | |
| History / Parked Tasks (3) | Tasks/Application | |
| Storage — 5 個子 context (15) | Tasks/Persistence、Settings/Persistence、Persistence/Runtime、Compatibility | 5 → 2 個 context ＋ runtime ＋ 凍結的 facade |
| `ClickraSettings`（在 Storage） | Kernel/Settings（schema 與 policy） | key 字串不變 → 持久化格式不變 |
| PDF / Image / Office / Markdown Processors | Engines/Pdf · Image · Office · Markdown | |
| Processor Shared Primitives (8) | Engines/Common | |
| PDF Translation Orchestrator ＋ 9 個子 context ＋ Translation Engines (11) | **Engines/PdfTranslation（單一 module）** | 9 個子 context 只是內部資料夾 |
| Shared Models (10) | PdfTranslation/Model（9 個）、Kernel/Settings（NumericSettingRange） | |
| Fonts (1) | Engines/DocumentFonts | CLI 對它的引用要移除 |
| Rendering Primitives (1) | Platform/PdfPagePreview | 只有 UI 使用 |
| Diagnostics (2) | `ClickraDebug` → Diagnostics；`ClickraShellProcess` → Platform | |
| Infrastructure / External Integration (1) | OfficeEngine/Install | |
| Localization (7) | Kernel/Localization（純函式） | 拿掉對 Storage 的依賴 |
| Core Shared / Root (3) | `FileProcessor` → 拆成各 engine 的 typed facade；`SystemPaths` → Platform | |
| Core Assembly Metadata (1) | 不動 | |

結果：54 個 context → 約 **14 個 module**（Kernel、Conversion、Tasks、Settings、OfficeEngine、Engines.Common/Pdf/Image/Markdown/Office/PdfTranslation、Presentation、Platform、Diagnostics），外加 4 個 Host。

---

## 5. 具體的 move／rename 範例

### 5.1 會改變擁有權的（重要）

| 現在 | 改為 | 理由 |
|---|---|---|
| `Clickra.CLI/Progress/NativeConversionExecutionAdapter.cs` | **刪除**；Native Progress 改用 `Presentation/Progress/ConversionSession`（由 `ConversionExecutionController` 演進而來） | 每個 screen 只有一個 presentation 擁有者。per-command 的進度文案改成 Presentation 內一份「依 command 查表」的資料，Fluent 會一起受益 |
| `Core/FileProcessor.cs` | `Engines/Pdf/PdfEngine`、`Engines/Image/ImageEngine`、`Engines/Markdown/MarkdownEngine`、`Engines/Office/OfficeEngine`、`Engines/PdfTranslation/PdfTranslationEngine`，各自接收 typed request record | 移除 Storage 副作用。file-starting 改成由 use case 傳入的 `Action<int>` callback，並由 `ConversionTaskLifecycle`／`ITaskJournal` 統一寫入 active.tmp index |
| `Application/ConversionTaskProgressController.cs`、`ConversionTaskCleanup.cs` | 併入 `ConversionTaskLifecycle` 與 `Tasks.Application.TaskCommands.Delete(…, bestEffort)` | 每個寫入點只有一個擁有者 |
| `Storage/ClickraSettings.cs` | `Kernel/Settings/SettingKeys.cs` + `SettingSchema.cs`（`ClickraSetting` record、defaults、Normalize/Clamp） | 這是產品 schema，不是持久化 |
| `Models/NumericSettingRange.cs` | `Kernel/Settings/` | |
| `Models/{PdfParagraph,PdfLetter,MathFormula,MathLetter,RenderedChar,ParagraphAnnotationInfo,Translation*Diagnostics}.cs` | `Engines/PdfTranslation/Model/` | 唯一的使用者就是 translation |
| `Translation/*` | `Engines/PdfTranslation/Providers/` | `TranslationSourcePreservationClassifier`、`TranslationResultQualityGuard` 屬於翻譯 policy，可移到 `PdfTranslation/Text/` |
| `Infrastructure/LibreOfficeEngineInstaller.cs`、`Processors/FileProcessing/{OfficeEngineDetector,LibreOfficeHelper,PowerShellHelper}.cs`、`Application/LibreOfficeSettingsController.cs` | `OfficeEngine/{Install,Detection,Interop,Application}/` | 合成一個 bounded context |
| `Presentation/Native/{DashboardLayout,HistoryLayout}.cs`、`Layout/{LayoutRect,SettingsLayout}.cs`、`Presentation/Shared/VectorIcons.cs` | `Clickra.CLI/Ui/Layout/` | 只有 Native 使用（Fluent 的依賴是撞名誤報） |
| `Rendering/PdfPageThumbnailRenderer.cs` | `Platform/PdfPagePreview.cs` | 只有 UI 使用 |
| `Diagnostics/ClickraShellProcess.cs`、`SystemPaths.cs` | `Platform/` | 只有 UI 使用 |
| `Application/ConversionUseCases.cs` | `Conversion/Catalog/` | 消除 Contracts ↔ UseCases cycle |
| UI 對 `VisualSplitModel`、`MarkdownPdfOptions`、`MarkdownTemplateSource`、`ImageCompressionLevel`、`LibreOfficeEnginePackage` 的引用 | 在 Conversion.Contracts／Presentation 提供 DTO（例如 `SplitPagePlan`、`MarkdownOptionsModel`、`CompressionLevelOption`、`OfficeEnginePackageInfo`） | Presentation 和 renderer 不應該看到 engine 的型別 |

### 5.2 只改名（讓「Controller」不再一詞多義）

| 現在 | 改為 | 角色 |
|---|---|---|
| `SettingsController` | `SettingsService`（或 `AppSettings`） | Application 的 typed 讀寫 |
| `StoragePathController` | `OutputLocations` | Application |
| `HistoryController` | `HistoryCommands` | Application |
| `HistoryQueryController` | `TaskHistoryQueries` | Application（read side） |
| `ParkedTaskController` | `ParkedTaskCommands` | Application |
| `LibreOfficeSettingsController` | `OfficeEngineService` | Application |
| `ConvertWorkspaceController` | `ConvertWorkspaceModel` | Presentation |
| `ConversionExecutionController` | `ConversionSession` | Presentation |
| `TaskProgressController` | `TaskProgressModel` | Presentation |
| `HistoryPresentationController` + `HistoryFeed` | `HistoryScreenModel` + `HistoryFeedBuilder` | Presentation |
| `SettingsPresentationController` | `SettingsScreenModel` | Presentation |
| `DashboardPresentationController` | `DashboardNavigationModel` | Presentation |
| `ClickraCli` / `ClickraStartup` | `CommandLineFrontController` / `NativeHost`（或只調整資料夾與 namespace，保留類別名稱） | Host |

規則：**Application = `*Service` / `*Queries` / `*Commands` / `*UseCase`；Presentation = `*Model` / `*Session`；renderer = `*Window` / `*Page` / `*Control`。** 「Controller」這個字只留給 UI framework 自己的概念（或乾脆不用）。

---

## 6. 依賴規則與 guards

### 6.1 規則（以 module／namespace 表達）

| # | 規則 |
|---|---|
| R1 | `Kernel.*` 只能依賴 BCL。 |
| R2 | `Engines.*` 只能依賴 `Kernel`、`Engines.Common`、`Diagnostics`，以及自身 module。**禁止**依賴 `Conversion`、`Tasks`、`Settings`、`Compatibility`(`ClickraStorage`)、`Presentation`、`Platform`，也禁止 `Localization.T(key)` 這種讀取「目前語言」的 overload（必須明確傳入 lang）。 |
| R3 | `Engines.PdfTranslation` 除了 `PdfTranslationEngine`／`PdfTranslationPipeline`／`PdfTranslateProcessor` 之外全部是 `internal`。其他 engine 不得引用它的內部型別（目前 `PdfProcessors → PdfParagraphBlockMerger / FormulaLiteralCleaner` 需要處理）。 |
| R4 | `Engines.PdfTranslation.Providers` 只能被 `Engines.PdfTranslation` 使用。 |
| R5 | `Conversion.*` 可依賴 `Kernel`、`Engines.*`（只限 facade）、`Conversion.Ports`。**禁止**依賴 `*.Persistence`、`Compatibility`、`Presentation`、`Platform`。 |
| R6 | `Tasks.Persistence`、`Settings.Persistence`、`Persistence.Runtime` 一律 `internal`；只有同 context 的 `Application`、Host 組裝程式碼、測試可以引用。 |
| R7 | `Presentation.*` 可依賴 `Kernel`、`Conversion.Contracts`／`Catalog`、`Tasks.Application`、`Settings.Application`、`OfficeEngine.Application`。**禁止**依賴 `Engines.*`、`*.Persistence`、`OfficeEngine.Install`、`Platform`。 |
| R8 | Renderer（`Clickra.CLI/Ui/**`、`Clickra.Fluent/**`）可依賴 `Presentation`、`Kernel`、`Platform`、`Diagnostics`。**禁止**依賴 `IConversionUseCase.ExecuteAsync`、`ConversionUseCases`、任何 `*UseCase` 具體型別、`ConversionTaskLifecycle`、`Tasks.Application` 的寫入 API、`Engines.*`。 |
| R9 | Quiet runner（`Clickra.CLI/Host/Quiet/**`）可依賴 `Conversion`、`Settings.Application`、`Kernel`。**禁止**依賴 `Engines.*`（目前引用了 `MarkdownPdfOptions`、`ClickraFontResolver`）以及 `Ui/**`。 |
| R10 | `ClickraShell`、`ClickraLauncher`：不得有 `ProjectReference` 到 Core；只能 link `Kernel/Commands` 的檔案（維持現狀）。 |
| R11 | `GlobalUsings*.cs` 在 surface 專案中不得 global using `Storage`、`Engines`、`Processors`、`Infrastructure`、`Translation`。 |
| R12 | **module 之間不得有 cycle**（以 SCC 檢查）。module 內部可以有 cycle（PdfTranslation 內部目前大量存在，可另外安排治理）。 |
| R13 | `namespace` 必須等於資料夾路徑（目前 `Clickra.Core.Processors` 底下有 92 個檔案跨多個子資料夾）。 |
| R14 | 只有 Host 可以設定 static singleton（例如 `SettingsPresentationController.Shared`、`Localization` 的目前語言 provider）。 |
| R15 | 任何 `ClickraStorage.*` 的新呼叫點一律失敗（baseline 只能縮小），目標是讓 facade 變成 `internal`。 |

### 6.2 怎麼實作 guards

- **把現有的 `.tmp-architecture-graph/Program.cs`（Roslyn SemanticGraph）升級成測試。** 它已經能產生正確的 symbol 級 edges，只要加上兩件事：去掉 namespace symbol，以及「module 規則表＋SCC 檢查」。新規則先以 report-only 模式跑，等 baseline 清乾淨後改成 fail。
- 現有的 regex 規則（「migrated X has one execution owner」這類）**保留**。它們守的是特定呼叫點的擁有權，和型別級規則互補。
- 是否要引入第三方套件（例如 ArchUnitNET、NetArchTest）屬於 provider 決策，依 `AGENTS.md` 需要你明確選擇。以目前已有的 Roslyn analyzer 來說，沒有引入的必要。

---

## 7. 最低風險的遷移順序

每一步都可以單獨合併；每一步都先補測試、再搬程式碼。

| 步驟 | 內容 | 行為風險 | 清晰度收益 |
|---|---|---|---|
| **0** | **修量測**：圖表改用 semantic edges（去掉 namespace symbol、修正 XAML 撞名），加入 SCC 報表與 R1–R15 的 report-only 測試。 | 無 | 高（看見真實狀況） |
| **1** | **純搬移（namespace／資料夾，不改邏輯）**：Models → PdfTranslation/Model；Translation → PdfTranslation/Providers；Native-only layout／VectorIcons → `Clickra.CLI/Ui/Layout`；ClickraShellProcess／SystemPaths／Thumbnail → Platform；`ConversionUseCases` → Catalog；`ClickraSettings` → Kernel/Settings（**key 字串不變**）。圖上把 PDF Translation 收斂成 1 個 module、Storage 收斂成 2 個。 | 極低（只改 using） | 高 |
| **2** | 移除 surface 的 `global using Clickra.Core.Storage`（以及 Processors/Translation），讓編譯錯誤直接列出所有違規點。 | 極低 | 中 |
| **3** | **Localization 去除 Storage 依賴**：加入 `Localization.CurrentLanguageProvider`，由 Host 在啟動時設定，並在 `SettingsReloaded` 時更新。`T(key)` 改用 provider。 | 低（需測試設定重載後的語言切換） | 中（打斷最大的 SCC） |
| **4** | **合併 Office Engine context**（只搬移＋讓 Presentation 改用 DTO 取代 `LibreOfficeEnginePackage`）。Installer 的 `SaveSetting` 改走 `SettingsService`。 | 低 | 中 |
| **5** | **拔掉 `FileProcessor` 的副作用**：先寫回歸測試，鎖住「每個 command、quiet／non-quiet、bestEffort 下，active.tmp index 與 task index 的寫入序列」。再讓 use case 傳入 `onFileStarting`，由 `ConversionTaskLifecycle` 單一擁有者寫入，刪掉 `ConversionTaskProgressController`。最後把 `FileProcessor` 拆成 typed engine facades。 | **中**（active.tmp 語意，必須先有測試） | 高 |
| **6** | **Native Progress 改用 `ConversionSession`**：一次遷移一個 command，搭配現有的「one execution owner」測試；per-command 的進度文案搬進 Presentation 的查表。全部遷移完就刪掉 `NativeConversionExecutionAdapter`。 | **中**（Native/Fluent parity；需人工驗收進度文案、密碼與 split 對話框、park/resume） | **最高** |
| **7** | CLI front controller 的副檔名與最小檔案數全部改由 `ConvertCommandMetadata` 提供，移除寫死的清單。 | 低（目前值一致，先加上等價測試） | 中 |
| **8** | 引入 `ITaskJournal`／`ISettingsReader` port（instance interface＋Host 預設接線，不需要 DI container）。Application 的 `*Service` 改依賴 port；`ClickraStorage` facade 降為 `internal`（InternalsVisibleTo 給 tests）。 | 低～中 | 高（Application 第一次真正跟持久化解耦） |
| **9** | 改名（§5.2），保留 `[Obsolete]` type-forward 或一次性替換。 | 極低 | 中 |
| 10（選擇性） | 規則穩定後，再評估是否把 `Engines.PdfTranslation` 拆成獨立 assembly。**不建議提早做**：namespace 規則＋guards 就夠了，拆 assembly 會增加 NativeAOT／trim／packaging 的驗證成本。 | 中 | 低～中 |

**用最少風險換最多清晰度的組合：0 → 1 → 2 → 6。** 先看見真相，再做零風險搬移，最後解決最明顯的擁有權違規。

---

## 8. 明確**不**改的東西

- `ClickraShell` 維持 thin NativeAOT COM，只 link `ConvertCommandMetadata.cs`，**不加** Core `ProjectReference`，也不為此建立共用 assembly。
- `ClickraLauncher` 的 Fluent COM activation 與 Native fallback 流程。不做 Fluent 獨立發佈。
- `ConversionTaskLifecycle` 的對外語意；task record 格式；resume identity／index 語意；`active.tmp` 格式與寫入時機（步驟 5 只換擁有者，不換語意）。
- `ParkedTaskCoordinator` 維持 internal，也是唯一的 atomic transition 擁有者；`TaskPruningCoordinator`、`TaskRetentionPolicy` 的擁有權不變。
- 所有 settings 的 **key 字串與預設值**（只搬型別，不改值）。
- `IConversionUseCase` 的 `Validate / Plan / ExecuteAsync` contract，以及由 use case 擁有 output planning（PerFile／SingleOutput base）。
- 不重新引入 `ConvertCommandRunner`、`EstimateOutputs`，或任何 UI 端的 fallback 執行路徑。
- **不引入 DI container**（NativeAOT 加上 static composition 已經足夠）；**不強迫 engines 實作 Application ports**；**不建立 Clean Architecture 的 Entities 層**（Clickra 沒有豐富的 domain model，硬套只會產生空殼）。
- 不在這一輪拆 assembly。
- PDF Translation 內部的 cycle 先不處理（它屬於演算法品質議題，不是產品架構議題），只要求它對外收斂成單一入口。

---

## 9. 建議採用哪種架構

**Hybrid：Modular Monolith（主）＋ 局部 Ports & Adapters ＋ per-screen Presentation ＋ thin Hosts。**

| 選項 | 判斷 |
|---|---|
| 純水平分層（現在） | ✗ 實際依賴顯示耦合是沿著能力形成 cluster，不是沿著層。這個模型讓「Technical / Storage」變成垃圾桶，也把 static facade 呼叫偽裝成合法的「往下依賴」。 |
| 純垂直 feature slice | ✗ Presentation 天生跨 context（Dashboard 組合 Convert、History、Settings）；Engines 是共用函式庫，不屬於任何單一 feature。 |
| Clean Architecture（Entities／UseCases／Interface Adapters／Frameworks） | ✗ 沒有值得獨立成層的 Entities；會產生大量空的 mapping。 |
| 全面 Hexagonal | △ 對 engines 而言是過度設計。只在**有狀態或 IO 的邊界**（task journal、settings、office installer、translation provider）用 port，才有實際回報（可測試、可替換、擁有權清楚）。 |
| **Modular monolith ＋ 局部 hexagonal** | ✓ 符合實際 SCC 結構；一個 assembly 就能做到（NativeAOT 友善）；規則用 module 允許清單表達，可以機器檢查；和現在「One Product Model + One Workflow per Use Case + One Presentation State per Screen + Thin Renderers」的目標完全一致，而且讓目標裡的每一項都有明確的 module 對應：Kernel、Conversion、Presentation、Hosts。 |

一句話：**不要再問「它在第幾層」，改問「它屬於哪個 context、是不是那個 context 的 public API」。** 方向規則只剩三條：Host 在最外面、Kernel 在最裡面、其他 module 之間依允許清單。

---

## 附錄：本次分析使用的方法

- context 分類沿用 `generate_context_graph.py` 的 `context()`／`layer()`，避免分類口徑不同。
- semantic graph 取自 `semantic-edges.json`，去掉純 namespace 的 symbol（`Clickra`、`Core`、`Processors`、`Application`、`UI`…），得到 1,289 條 file edge、220 條 context edge，再用 Tarjan 演算法找出 SCC。
- 注意：Roslyn compilation 有 `diagnostics`（Core 約 2,049 個，主要是 NuGet 參照沒有載入），因此第三方型別相關的 edge 可能仍有缺漏；Clickra 內部的 symbol 解析不受影響。
