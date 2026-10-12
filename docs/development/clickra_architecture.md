# Clickra Architecture

## 1. Purpose

This document describes Clickra's current as-built architecture after the application/storage boundary cleanup, storage decomposition, and Native/Fluent presentation consolidation.

It is the **canonical current-state architecture map** for the product. The major architecture goals are now implemented and guarded:

- application workflows have authoritative owners;
- processors are isolated from mutable presentation/storage state;
- persistence has focused storage owners;
- Convert, History, Settings, Progress, and Dashboard navigation have shared Presentation owners consumed by Native and Fluent;
- primary renderers no longer own application orchestration, storage policy, processor policy, settings schema, or duplicated screen state.

Related design records:

- `application_ui_architecture_consolidation_plan.md` — completed product/application/UI consolidation plan and rationale;
- `storage_processor_architecture_cleanup_plan.md` — completed storage, processor-state, namespace, and shared-Core cleanup track.

### Canonical as-built architecture

This is both the current implementation map and the dependency contract the repository must preserve. Smaller diagrams later in this document are zoomed views of this graph, not alternate architectures.

```mermaid
flowchart TB
    User["User / Windows"]

    subgraph Entry["Executable / OS entry points"]
        Launcher["ClickraLauncher\npackage activation + Native fallback"]
        Shell["ClickraShell\nWindows shell adapter"]
        Native["Clickra.CLI\nNative Win32 renderer + CLI adapter"]
        Fluent["Clickra.Fluent\nWinUI renderer + platform adapters"]
    end

    subgraph Presentation["Clickra.Core.Presentation — authoritative product state per screen"]
        DashboardState["DashboardPresentationController"]
        ConvertState["ConvertWorkspaceController\nConversionExecutionController"]
        HistoryState["HistoryPresentationController\nHistoryFeed / HistoryItem"]
        SettingsState["SettingsPresentationController\nSettingPageRegistry / descriptors"]
        ProgressState["TaskProgressController"]
    end

    subgraph Application["Clickra.Core.Application — product workflows"]
        Catalog["ConvertCommandRegistry / product metadata"]
        UseCases["Conversion use cases"]
        Lifecycle["ConversionTaskLifecycle"]
        HistoryApp["HistoryQueryController / HistoryController"]
        ParkedApp["ParkedTaskController"]
        SettingsApp["SettingsController"]
        LibreOfficeApp["LibreOfficeSettingsController"]
        PathApp["StoragePathController"]
        ProgressApp["ConversionTaskProgressController"]
    end

    subgraph Technical["Technical execution"]
        Processors["Processors\nPDF / Image / Markdown / Office"]
        PdfPipeline["PDF translation text-processing pipeline"]
        Translation["Translation engines / provider chain"]
        Infrastructure["Infrastructure\nLibreOffice / OS / external integration"]
        Fonts["Fonts"]
        Diagnostics["Diagnostics"]
    end

    subgraph Storage["Clickra.Core.Storage — persistence facts only"]
        Compat["ClickraStorage\ncompatibility facade"]
        SettingsStore["SettingsStore"]
        PathStore["StoragePathResolver"]
        HistoryStore["HistoryStore"]
        QueueStore["TaskQueueStore"]
        RecordStore["TaskRecordStore"]
        ParkedStore["ParkedTaskCoordinator"]
        Pruner["TaskPruningCoordinator / RetentionPolicy"]
        LegacyStore["LegacyActiveRecordStore"]
        Runtime["StorageRuntime"]
    end

    subgraph Persistence["Physical persistence"]
        SettingsFile["settings.conf"]
        HistoryFile["history.log"]
        TaskFiles["task-*.tmp"]
        LegacyFile["active.tmp"]
    end

    User --> Launcher
    User --> Shell
    Launcher --> Fluent
    Launcher --> Native
    Shell -->|"command/arguments only"| Native

    Native -->|"render + forward input"| DashboardState
    Native --> ConvertState
    Native --> HistoryState
    Native --> SettingsState
    Native --> ProgressState
    Fluent -->|"render + forward input"| DashboardState
    Fluent --> ConvertState
    Fluent --> HistoryState
    Fluent --> SettingsState
    Fluent --> ProgressState

    DashboardState --> HistoryState
    DashboardState --> SettingsState
    ConvertState --> Catalog
    ConvertState --> UseCases
    ConvertState --> Lifecycle
    HistoryState --> HistoryApp
    HistoryState --> ParkedApp
    SettingsState --> SettingsApp
    SettingsState --> LibreOfficeApp
    ProgressState --> ProgressApp
    ProgressState --> Lifecycle

    UseCases --> Processors
    UseCases --> PdfPipeline
    UseCases --> Infrastructure
    PdfPipeline --> Translation
    Processors --> Infrastructure
    Processors --> Fonts
    Processors --> Diagnostics

    Lifecycle --> Compat
    HistoryApp --> Compat
    ParkedApp --> Compat
    SettingsApp --> Compat
    LibreOfficeApp --> Compat
    LibreOfficeApp --> Infrastructure
    PathApp --> Compat
    ProgressApp --> Compat

    Compat --> SettingsStore
    Compat --> PathStore
    Compat --> HistoryStore
    Compat --> QueueStore
    Compat --> ParkedStore
    Compat --> LegacyStore
    QueueStore --> RecordStore
    QueueStore --> HistoryStore
    ParkedStore --> RecordStore
    ParkedStore --> HistoryStore
    QueueStore --> Pruner
    ParkedStore --> Pruner
    SettingsStore --> Runtime
    PathStore --> Runtime
    HistoryStore --> Runtime
    RecordStore --> Runtime
    LegacyStore --> Runtime

    SettingsStore --> SettingsFile
    HistoryStore --> HistoryFile
    RecordStore --> TaskFiles
    LegacyStore --> LegacyFile
```

The non-negotiable dependency direction is:

```text
Renderer -> Presentation -> Application -> Technical / Storage
```

Renderers may implement OS/UI capabilities required by the shared workflow (dialogs, pickers, visual page selection, notifications), but product state and product policy must not fork by UI technology. Storage returns persistence facts; it never produces localized presentation wording. Presentation consumes Application contracts rather than Storage, Processor, or Infrastructure implementation details.

---

## 2. Architecture in one sentence

> **Thin product surfaces call shared application workflows; application workflows own product behavior; processors perform technical conversion work; storage owns persistence; platform-specific code stays at the edges.**

The intended dependency direction is one-way:

```mermaid
flowchart TB
    Surface["Product surfaces / adapters"]
    Presentation["Shared presentation state / controllers"]
    Application["Application use cases and application gateways"]
    Technical["Processors / translation engines"]
    Storage["Storage / persistence"]
    Infrastructure["Infrastructure / platform integrations"]

    Surface --> Presentation
    Presentation --> Application
    Application --> Technical
    Application --> Storage
    Application --> Infrastructure

    Technical -. "explicit inputs only" .-> Infrastructure

    classDef target stroke-width:2px;
    class Presentation target;
```

The `Presentation` layer in this diagram is the architectural target for shared product state. Some shared presentation ownership already exists, but full per-screen consolidation remains tracked separately.

---

## 3. Product and executable boundaries

Clickra is not one executable. It has several product entry points with intentionally different responsibilities.

```mermaid
flowchart LR
    User["User / Windows"]

    Launcher["ClickraLauncher\nNativeAOT entry point"]
    Fluent["Clickra.Fluent\nWinUI / MSIX UI"]
    Native["Clickra.CLI\nNative Win32 dashboard + CLI\nNativeAOT"]
    Shell["ClickraShell\nWindows 11 shell extension\nNativeAOT COM"]
    Core["Clickra.Core\nshared product/application/technical core"]
    Shared["Shared command metadata"]

    User --> Launcher
    User --> Shell
    Launcher -->|"optional package activation"| Fluent
    Launcher -->|"fallback"| Native
    Fluent --> Core
    Native --> Core
    Shell --> Shared
    Core --> Shared

    Shell -. "no ProjectReference" .-> Core
```

### Project responsibilities

| Project | Responsibility | Important boundary |
| --- | --- | --- |
| `ClickraLauncher` | Zero/low-dependency packaged entry point that activates Fluent when available and falls back to the Native dashboard | Does not become a product workflow owner |
| `Clickra.Fluent` | WinUI/MSIX presentation and platform interaction | References `Clickra.Core`; must not own a second conversion workflow |
| `Clickra.CLI` | Native dashboard, quiet/CLI surface, Native progress and platform adapters | References `Clickra.Core`; must not bypass application owners |
| `ClickraShell` | Thin Windows 11 modern-menu COM shell extension | Intentionally does **not** reference full `Clickra.Core`; preserve NativeAOT boundary |
| `Clickra.Core` | Product/application workflows, processors, persistence boundaries, translation services, shared presentation infrastructure | Must remain UI-technology independent at the application-contract level |

---

## 4. `Clickra.Core` modules and bounded contexts

The Core assembly is shared for deployment and NativeAOT reasons, but repository ownership is capability-first. New product code belongs under `Modules/<Capability>/...`; technical roles are secondary inside the owning capability. The old top-level `Application`, `Presentation`, `Processors`, `Storage`, and `Models` directories are shrinking migration buckets rather than the target organization.

```mermaid
flowchart TB
    subgraph Core["Clickra.Core"]
        SharedApp["Legacy/shared Application\nconversion contracts, lifecycle, gateways"]
        Pdf["Modules.PdfTranslation\nApplication + Execution + Model + Pipeline + Translation"]
        SettingsModel["Modules.Settings.Model\nsettings value types"]
        Proc["Legacy/shared Processors\nnon-migrated technical conversion operations"]
        Store["Storage\nsingle persistence cluster + compatibility facade"]
        Infra["Infrastructure\nowned platform integrations"]
        Pres["Presentation\nshared / Native presentation helpers"]
        Diag["Diagnostics"]
        Fonts["Fonts"]
        Loc["Localization\ncentralized catalog ownership"]
    end

    Pres --> SharedApp
    Pres --> SettingsModel
    SharedApp --> Pdf
    SharedApp --> Proc
    SharedApp --> Store
    SharedApp --> Infra
    Pdf --> Store
    Pdf --> Loc
    Pdf --> Diag
    Pdf --> Fonts
    Proc --> Loc
    Proc --> Diag
    Proc --> Fonts
```

### Meaning of the major boundaries

#### Shared Application kernel and migrating use cases

`Clickra.Core.Application` remains the shared workflow kernel while capability-owned use cases move under their modules. Important shared owners include:

- conversion contracts and the use-case registry;
- non-migrated conversion use cases such as PDF tools, image, Markdown, Office, and merge workflows;
- `ConversionRequest`, `ConversionPlan`, and `ConversionResult`;
- `ConversionTaskLifecycle` for application-level task creation/completion/parking orchestration;
- `SettingsController` for typed application settings access; presentation code does not consume persisted setting keys or boolean string encodings directly;
- `ParkedTaskController` for parked-task actions and atomic resume claims;
- `HistoryFeed` / `HistoryController` for application-owned history views/actions;
- `StoragePathController` for application-facing storage path resolution;
- `ConversionTaskProgressController` for progress-index persistence, including compatibility writes.

The PDF translation use case has migrated to `Modules.PdfTranslation.Application.TranslatePdfUseCase` while continuing to consume the shared Application contracts. UI surfaces still ask these owners to perform product operations instead of coordinating persistence or processor behavior themselves.

#### PDF Translation module

`Modules/PdfTranslation` is the first migrated vertical capability:

- `Application/` — authoritative `TranslatePdfUseCase`;
- `Execution/` — `PdfTranslateProcessor`, adapting the shared processor execution contract;
- `Model/` — PDF paragraph, annotation, rendering, and diagnostics models that were previously misleadingly placed in the repository-wide `Models` bucket;
- `Pipeline/` — PDF-specific analysis, layout, table/diagram detection, rendering, and `TextProcessing`;
- `Translation/` — translation provider engines, fallback, timeout, and result-quality policy.

Provider infrastructure and PDF text processing remain distinct namespaces inside the same capability. This keeps their different responsibilities visible without pretending they belong to unrelated top-level layers.

Shared `Fonts`, `Rendering`, `Localization`, and `Diagnostics` remain outside the module because semantic dependency analysis shows real consumers outside PDF translation.

#### Legacy/shared Processors

The remaining `Clickra.Core.Processors` bucket owns technical mechanics that have not yet migrated to capability modules. Processors receive explicit, already-normalized execution inputs from application code. Existing guards continue to prevent hidden mutable settings/storage access.

#### Storage

`Clickra.Core.Storage` owns persistence details rather than product presentation behavior.

MSBuild/Roslyn dependency analysis shows these persistence files form one strongly connected cluster. They therefore remain one persistence boundary during the module-first migration rather than being split into artificial Settings/History/Task modules merely to mirror filenames.

Important ownership includes:

- `StorageRuntime` — data paths, the shared file lock, and the historical cross-process mutex contract;
- `SettingsStore` — `settings.conf` caching, persistence, freshness, watcher synchronization, and change events;
- `StoragePathResolver` — persisted output-directory policy to filesystem-path resolution;
- `HistoryStore` — `history.log` parsing and append/delete behavior;
- `TaskRecordStore` — the `task-*.tmp` file format and record I/O;
- `TaskQueueStore` — normal task lifecycle persistence over task records;
- `LegacyActiveRecordStore` — the preserved `active.tmp` compatibility format and lifecycle;
- `ParkedTaskCoordinator` — private/internal owner of parked-task atomic transitions;
- `TaskPruningCoordinator` / `TaskRetentionPolicy` — stale-task cleanup and retention policy;
- `ClickraStorage.*` — a compatibility facade that delegates to those focused owners rather than containing their implementations.

`ParkedTaskCoordinator` is intentionally not a presentation/application API. Storage returns retention facts through the Application query boundary; localized retention wording is owned by `Presentation.History/HistoryFeed` with an explicit language input.

#### Translation providers versus PDF text processing

The two translation-related contexts are deliberately separate:

```mermaid
flowchart LR
    PdfUseCase["Modules.PdfTranslation.Application\nTranslatePdfUseCase"]
    PdfPipeline["Modules.PdfTranslation.Pipeline.TextProcessing\nbatching, paragraph stages, reference bypass, post-processing"]
    Engines["Modules.PdfTranslation.Translation\nITranslationEngine + providers + quality guards"]

    PdfUseCase --> PdfPipeline
    PdfPipeline --> Engines
```

`Modules.PdfTranslation.Translation` is provider/engine infrastructure. `Modules.PdfTranslation.Pipeline.TextProcessing` is the PDF-specific text-processing pipeline. The ambiguous `Pipeline/Translation` context is explicitly guarded against returning.

#### Infrastructure

Infrastructure owns platform integrations only when ownership is explicit. For example, `LibreOfficeEngineInstaller` may persist the small allowlisted set of LibreOffice lifecycle keys it owns.

Infrastructure persistence is guarded by a per-file/key allowlist rather than being a generic escape hatch from application ownership.

#### Presentation helpers inside Core

Presentation code is intentionally shared in the Core assembly so Native and Fluent render one product state:

- `Presentation.Convert.ConvertWorkspaceController` — command catalog, selection, compatibility, validation, and run-state owner;
- `Presentation.Convert.ConversionExecutionController` — presentation-facing execution session over the Application use-case layer;
- `Presentation.History.HistoryPresentationController` / `HistoryFeed` — history/task state, derived wording, retention presentation, and available actions;
- `Presentation.Settings.SettingsPresentationController` / `SettingPageRegistry` — settings screen state, descriptors, bounds, and LibreOffice presentation actions;
- `Presentation.Progress.TaskProgressController` — semantic progress and terminal-state owner;
- `Presentation.Dashboard.DashboardPresentationController` — semantic dashboard navigation state;
- `Presentation.Native.DashboardLayout` / `HistoryLayout` — testable Native presentation geometry;
- `Presentation.Shared.VectorIcons` — single-source presentation geometry shared across UI technologies.

Their presence in the Core assembly does not make them domain/application logic; their namespaces make that ownership explicit.

---

## 5. Conversion workflow

Every migrated conversion command has one authoritative application use-case owner, reached by UI renderers through the shared Presentation execution owner.

```mermaid
sequenceDiagram
    actor U as User
    participant UI as Native / Fluent surface
    participant PS as Presentation controller/state
    participant UC as Application use case
    participant LC as ConversionTaskLifecycle
    participant P as Processor / engine
    participant S as Storage / TaskRecordStore

    U->>UI: choose command, files, options
    UI->>PS: forward intent + platform callbacks
    PS->>UC: ConversionRequest + interaction capabilities
    UC->>UC: validate + normalize + plan outputs
    UC->>LC: create / attach task lifecycle
    LC->>S: persist task state
    UC->>P: execute explicit ConversionPlan/options
    P-->>UC: technical result / progress
    UC->>LC: checkpoint / complete / park
    LC->>S: persist transition
    UC-->>PS: ConversionResult
    PS-->>UI: presentation result/state
    UI-->>U: render result
```

### Ownership rule

The UI may provide platform-specific capabilities (password prompt, visual page splitter, notifications, dialogs), but Presentation owns screen semantics and the shared Application use case owns the workflow in which those capabilities are used.

In other words:

> **Inject platform differences into one workflow; do not fork the workflow by UI technology.**

---

## 6. Task, history, resume, and persistence flow

Task persistence intentionally has more than one internal component, but each has a narrow owner.

```mermaid
flowchart TB
    NativeUI["Native / CLI"]
    FluentUI["Fluent"]

    HistoryPresentation["HistoryPresentationController\nHistoryFeed / HistoryItem"]
    ProgressPresentation["TaskProgressController"]
    HistoryQuery["HistoryQueryController / HistoryController"]
    Parked["ParkedTaskController"]
    Progress["ConversionTaskProgressController"]
    Lifecycle["ConversionTaskLifecycle"]

    Facade["ClickraStorage\ncompatibility facade"]
    Queue["TaskQueueStore"]
    Records["TaskRecordStore\ntask-*.tmp format"]
    HistoryStore["HistoryStore\nhistory.log"]
    Coordinator["ParkedTaskCoordinator\nprivate atomic transition owner"]
    Legacy["LegacyActiveRecordStore\nactive.tmp compatibility"]
    Pruner["TaskPruningCoordinator"]

    NativeUI --> HistoryPresentation
    FluentUI --> HistoryPresentation
    NativeUI --> ProgressPresentation
    FluentUI --> ProgressPresentation

    HistoryPresentation --> HistoryQuery
    HistoryPresentation --> Parked
    ProgressPresentation --> Progress

    HistoryQuery --> Facade
    Parked --> Facade
    Progress --> Facade
    Lifecycle --> Facade

    Facade --> Queue
    Facade --> Coordinator
    Facade --> Legacy
    Queue --> Records
    Queue --> HistoryStore
    Coordinator --> Records
    Coordinator --> HistoryStore
    Queue --> Pruner
    Coordinator --> Pruner
```

Preserved behavior includes:

- task-record formats;
- parked-task atomic claim/cancel transitions;
- resume identity/index semantics;
- retention semantics;
- legacy `active.tmp` compatibility;
- best-effort progress-index writes required by compatibility behavior.

---

## 7. Settings flow

Settings persistence has one typed Application gateway, while settings screen schema and UI-facing state have one Presentation owner. Persisted keys and string encodings stop below those boundaries.

```mermaid
flowchart LR
    Native["Native UI"]
    Fluent["Fluent UI"]
    UseCases["Application use cases"]
    Presentation["SettingsPresentationController\nSettingPageRegistry"]
    Gateway["SettingsController\ntyped settings contract"]
    LibreOffice["LibreOfficeSettingsController"]
    Facade["ClickraStorage\ncompatibility facade"]
    Store["SettingsStore\nsettings.conf owner"]
    Processors["Processors"]

    Native --> Presentation
    Fluent --> Presentation
    Presentation --> Gateway
    Presentation --> LibreOffice
    UseCases --> Gateway
    Gateway --> Facade
    Facade --> Store
    UseCases -->|"snapshotted normalized options"| Processors

    Processors -. "forbidden: mutable settings reads" .-> Facade
```

CLI and Fluent consume `SettingsPresentationController` plus Presentation-owned `SettingDescriptor` metadata. They do not call raw Application/Storage settings APIs, interpret persisted `"true"` / `"false"` strings, or know `ClickraSettings` keys/ranges. Application use cases snapshot normalized options before processor execution, preventing a conversion from silently changing behavior mid-run because a processor rereads mutable global settings.

---

## 8. Architecture guardrails

Architecture is enforced by tests, not only by documentation.

The current architecture-boundary test suite pins, among other rules:

- UI/application violation baseline: **0**;
- processor global-state baseline: **0**;
- processors do not depend on `ClickraStorage`;
- infrastructure settings access stays on an explicit allowlist;
- application contracts stay UI-independent;
- settings persistence has one application gateway;
- presentation settings use one shared Presentation owner and Presentation-owned descriptor catalog;
- parked-task actions have one application gateway;
- presentation code does not depend on raw `ClickraStorage`;
- primary Native/Fluent renderers depend on shared Presentation owners rather than directly orchestrating Application/Processor/Infrastructure policy;
- `Clickra.Core.Presentation` depends inward through Application contracts and presentation-neutral shared models, not Storage/Processors/Infrastructure implementation details;
- `ConvertCommandRegistry` belongs to Application rather than Processors;
- the `ClickraStorage` compatibility facade delegates persistence implementation to focused storage owners;
- storage code does not own localized presentation wording;
- Core bounded contexts declare their owned namespaces;
- presentation/platform helpers have explicit ownership;
- translation provider and PDF text-processing contexts stay distinct;
- each migrated conversion workflow has a single execution owner;
- duplicate use-case ownership is rejected.

Important prohibited regressions include:

- reintroducing `ConvertCommandRunner` as a parallel workflow owner;
- reintroducing `ConvertCommandRegistry.EstimateOutputs`;
- adding Native/Fluent legacy conversion fallback paths;
- adding a new Native `switch(command)` execution router;
- making UI surfaces own task lifecycle transitions or raw output planning;
- exposing `ParkedTaskCoordinator` as an application/UI service;
- adding full `Clickra.Core` as a dependency of `ClickraShell` merely for code sharing.

---

## 9. Architecture completion status

### Completed and strongly guarded

The following architecture areas are implemented and guarded in the current codebase:

1. **One application execution owner for migrated conversion commands.**
2. **Processor isolation from mutable storage/settings state.**
3. **Typed/narrow application gateways for settings, parked tasks, history, storage paths, task progress, and task lifecycle.**
4. **Focused storage owners for settings, paths, history, task records/lifecycle, retention/pruning, and legacy compatibility; `ClickraStorage` is a compatibility facade rather than the implementation owner.**
5. **Capability-first Core ownership:** PDF Translation now lives under `Modules/PdfTranslation`, Settings has begun migrating under `Modules/Settings`, and shared Storage/Infrastructure/Diagnostics/Fonts boundaries remain explicit.
6. **One authoritative Presentation owner for the major product screens:** Convert, History/Tasks, Settings, Progress, and Dashboard navigation.
7. **Presentation-owned derived state and UI schema:** command grouping/compatibility/validation, history wording/retention state, settings descriptors/bounds, and progress semantics are not duplicated by Native and Fluent.
8. **Thin primary renderers:** Native Dashboard and Fluent MainPage/TaskProgressPage forward intent and render shared state rather than directly orchestrating use cases, storage, Office/LibreOffice policy, or settings schema.
9. **NativeAOT boundaries for CLI, Launcher, and Shell remain intentional.**
10. **Zero architecture-violation and processor-global-state baselines for the guarded legacy patterns.**

The definition of done from `application_ui_architecture_consolidation_plan.md` is therefore satisfied:

> **One Product Model + One Application Workflow per Use Case + One Presentation State per Screen + Multiple Thin UI Renderers**

This does not mean the repository can never need future architecture work. New product areas can introduce new boundaries, and existing implementation details can still be improved. It means the architecture consolidation project described by the two development plans is complete: the intended ownership graph is now represented by production code and protected by automated guards.

Local completion evidence for this consolidation slice:

- 5 product warnings-as-errors builds plus the test project: 0 warnings / 0 errors;
- 79 architecture/localization guards: 79 passed, 0 failed, 0 skipped;
- 352 functional tests: 352 passed, 0 failed, 0 skipped;
- fixture-required full runner: 431 passed, 0 failed, 0 skipped;
- 34 Store publishing unit tests passed;
- product icon asset verification passed;
- NativeAOT publish succeeded for CLI, ClickraShell, and ClickraLauncher.

---

## 10. Contributor dependency checklist

Before adding a new feature, determine its owner in this order:

1. **Which product capability owns the change?** New Core product code is organized capability-first under Modules/<Capability>/...; technical role is the second axis. See module_first_architecture_decision.md.
2. **Is it product workflow policy?** Keep it in that capability's application workflow, not in Native or Fluent.
3. **Is it a technical conversion operation?** Keep it with the owning capability and pass explicit execution inputs.
4. **Is it persistence?** Keep it behind the owning capability's application/persistence boundary; do not expose raw persistence to presentation.
5. **Is it platform interaction?** Implement a platform adapter/capability and inject it into the shared workflow.
6. **Is it visual rendering/layout only?** It may remain surface-specific, or shared under explicit presentation ownership when single-source behavior is valuable.
7. **Is it Windows shell/packaging behavior?** Preserve the dedicated Launcher/Shell/Infrastructure boundary instead of pulling the full Core graph into it.

When in doubt, dependency direction should continue to point inward from Renderer to Presentation to Application and then to technical/storage owners, never outward from Core into a UI technology.

The existing top-level Application, Presentation, Processors, Storage, and Models directories are a shrinking migration baseline, not the preferred location for new feature-private files. Architecture guards prevent those generic buckets from expanding and prevent technical-role names from becoming the first axis under Modules.

---

## 11. Maintenance rule

Update this document when a change alters a real ownership or dependency boundary. Do not update it for cosmetic file moves that do not change architecture.

For detailed migration history and rationale, keep the architecture plans as the decision records; this file should remain the concise map of the architecture that a new contributor can understand first.
