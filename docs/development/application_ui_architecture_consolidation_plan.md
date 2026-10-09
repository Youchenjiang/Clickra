# Clickra Backend and UI Architecture Consolidation Plan

## 1. Purpose

Clickra has already completed several repository-cleanup passes that removed duplicated metadata, storage responsibilities, task lifecycle code, output planning, renderer measurements, and parts of Fluent/Native presentation policy.

Those changes reduced duplication, but they did not fully solve the deeper architecture problem:

- one user-facing use case can still span UI, registry, runner, processor, lifecycle, and storage code;
- Fluent and Native can still own parallel presentation decisions;
- a future feature can still reintroduce command switches, validation rules, task policy, or wording into individual UI surfaces.

The goal of this plan is therefore not another file-size cleanup. The target architecture is:

> **One Product Model, One Application Workflow, One Presentation State, Multiple Thin Renderers.**

The work is complete only when architecture and automated guards make it hard to scatter product logic again.

---

## 2. Backend Target

Every user-visible use case must have exactly one application-level owner.

Examples include:

- `compress-pdf`
- `split-pdf`
- `decrypt-pdf`
- `md2pdf`
- `md2word`
- Office-to-PDF conversions
- image compression and image format conversion

The authoritative owner of a use case must contain or coordinate:

- validation;
- prerequisite checks;
- normalized options;
- output planning;
- execution sequencing;
- progress semantics;
- task lifecycle;
- resume checkpoints;
- cancellation semantics;
- success/failure result construction.

The UI, registry, processor, and persistence layers must not each own separate pieces of the same workflow policy.

### Backend success criterion

Starting from any command name, a developer must be able to identify one application use-case owner that explains the complete product workflow without having to reconstruct business policy from Fluent and Native code.

---

## 3. UI Target

Fluent and Native are two rendering technologies for one product, not two independent product implementations.

The following presentation policy must have a shared source of truth:

- available commands;
- command grouping;
- command labels/descriptions/icon identity;
- selected command;
- selected files;
- file compatibility;
- enabled/disabled state;
- validation state;
- option requirements;
- progress state;
- history/task state;
- settings descriptors and bounds;
- available actions;
- empty/error state semantics.

The two UI surfaces may differ only where the platform genuinely differs:

- layout;
- WinUI controls versus Win32/GDI drawing;
- window lifecycle;
- focus and keyboard handling;
- platform dialogs;
- tray integration;
- notification rendering.

### UI success criterion

For each major screen, there must be one presentation controller/model that owns product state. Fluent and Native must only bind/draw that state and forward user actions.

---

## 4. Architecture Invariants

These rules are non-negotiable after migration.

### 4.1 One use case, one owner

There must not be separate Fluent and Native execution workflows for the same conversion.

### 4.2 UI must not own business workflow

UI code must not:

- switch on commands to decide execution behavior;
- calculate conversion output paths;
- decide task lifecycle transitions;
- complete or park conversion tasks directly;
- duplicate conversion validation;
- interpret history/task business status independently.

UI code may:

- collect user input;
- render application/presentation state;
- display progress;
- provide platform-specific interaction capabilities;
- handle platform-specific window behavior.

### 4.3 Inject differences; do not fork workflows

Platform differences must be represented by capabilities such as:

```text
IPasswordInteraction
ISplitPageInteraction
IMarkdownOptionsInteraction
INotificationSink
```

A shared use case consumes those capabilities. Fluent and Native provide different implementations.

The rule is:

> **Differences are injected into the workflow; the workflow does not fork by UI technology.**

### 4.4 Processors perform technical operations only

Processors such as PDF, image, Markdown, and Office engines must not know about:

- Fluent;
- Native;
- HWND;
- dashboard state;
- history rows;
- task IDs;
- resume UI;
- toasts;
- command routing.

### 4.5 Presentation policy has one owner

Renderers must not independently derive product state such as `CanStart`, history status, command grouping, settings defaults, or task action availability.

### 4.6 Dependency direction is one-way

The intended direction is:

```text
UI adapters/renderers
        |
        v
Presentation controllers/models
        |
        v
Application use cases
        |
        v
Domain/processors/storage
```

Lower layers must not depend on UI technologies.

---

## 5. Target Backend Model

The exact type names may evolve, but the responsibility boundaries must match the following model.

### 5.1 ConversionRequest

Contains user/application input:

```text
ConversionRequest
  Command
  InputFiles
  Options
  ExistingTaskId
  OutputOverride
```

### 5.2 ConversionPlan

Contains the validated, normalized execution plan:

```text
ConversionPlan
  Command
  Inputs
  Outputs
  RequiredCapabilities
  NormalizedOptions
  ResumeStartIndex
```

Execution must not independently recalculate outputs or reinterpret options.

### 5.3 ConversionResult

Returns application semantics to presentation code:

```text
ConversionResult
  Status
  Outputs
  Error
  Duration
  CompletedFiles
  TaskId
```

The UI must not reconstruct this state from exceptions and storage calls.

### 5.4 Use-case contract

Conceptually:

```text
IConversionUseCase
  Descriptor
  Validate(request)
  Plan(request)
  Execute(plan, interaction, progress, cancellation)
```

The implementation does not need to adopt these exact signatures immediately, but the ownership boundary must remain equivalent.

---

## 6. Target Presentation Model

### 6.1 ConvertWorkspaceController

The Convert screen must have one shared presentation owner responsible for:

- command catalog and grouping;
- selected command;
- selected files;
- compatibility state;
- validation;
- option requirements;
- `CanStart`;
- start/cancel actions;
- running state;
- progress state;
- error state.

Fluent and Native must consume the same state.

### 6.2 HistoryController

Build on the existing `HistoryFeed` work and centralize:

- list state;
- active/parked/completed grouping;
- filters;
- resume availability;
- cancel availability;
- retention display state;
- task actions and their results.

### 6.3 SettingsController

Build on `ClickraSettings`, `SettingDescriptor`, `SettingPageRegistry`, and `DynamicSettingsController` and centralize:

- current value;
- display metadata;
- enabled/visible state;
- bounds;
- validation;
- restart requirements;
- side-effect signals.

### 6.4 TaskProgressController

Provide shared progress semantics:

```text
TaskProgressState
  Command
  Current
  Total
  MessageKey
  Status
  CanCancel
  CanResume
  Error
```

Native may draw this through Win32/GDI and Fluent may render it through WinUI, but they must not assign different meanings to the state.

---

## 7. Phase 0 - Architecture Baseline and Guards

Before broad migration, establish automated architecture guards.

The initial baseline may contain explicit legacy exceptions, but the exception set is **monotonic shrinking**: it may decrease, never increase.

### UI code must not newly introduce

- `FileProcessor`-driven command execution;
- `ClickraStorage.StartTask`;
- `ClickraStorage.CompleteTask`;
- direct output planning;
- command-specific execution switches;
- duplicated command metadata;
- duplicated settings defaults/ranges;
- independent history/task status wording.

### Application code must not depend on

- WinUI;
- HWND;
- GDI;
- Native window classes.

### Processor code must not depend on

- UI projects;
- history presentation;
- task UI orchestration.

### Phase 0 exit criterion

CI has enforceable dependency/policy guards, with a documented shrinking legacy allowlist where immediate migration is not yet possible.

---

## 8. Phase 1 - Application Skeleton

Introduce the application abstractions needed for later migration:

```text
Application/
  ConversionRequest
  ConversionPlan
  ConversionResult
  IConversionInteraction
  ConversionUseCaseRegistry
  handlers/
```

This phase should avoid broad runtime changes. Its purpose is to establish the canonical dependency direction and enable vertical slices.

---

## 9. Phases 2-4 - Prove the Architecture with the Hardest Workflows

Do not validate the design using only simple conversions. Migrate the three commands that exercise Clickra's difficult platform behavior first.

### Phase 2 - `decrypt-pdf`

Must prove:

- password interaction;
- retry;
- cancellation;
- per-file checkpoint;
- resume;
- failure semantics.

Target:

```text
DecryptPdfUseCase
        |
        +-- IPasswordInteraction
                +-- Fluent implementation
                +-- Native implementation
```

Neither UI may own a second decrypt workflow.

### Phase 3 - `split-pdf`

Must prove:

- visual splitter interaction;
- CLI-supplied page ranges;
- prompt behavior;
- multi-file execution;
- cancellation;
- resume.

The Native visual splitter must become a platform implementation of an interaction capability, not an independent execution path.

### Phase 4 - `compress-pdf`

Must prove:

- command-specific options;
- specialized progress;
- multi-file execution;
- output planning;
- Native progress rendering.

Native must no longer require its own compression business workflow.

### Architecture checkpoint

Do not migrate all remaining commands until these three workflows share the same application architecture successfully.

If the design cannot support these three without UI workflow forks, the architecture is not ready.

---

## 10. Phase 5 - Migrate Remaining Conversion Use Cases

After the hard-workflow checkpoint passes, migrate remaining commands by family.

### Single/per-file conversions

- `img2pdf`
- `translate-pdf`
- `md2pdf`
- `md2word`

### Office family

- `ppt2pdf`
- `word2pdf`
- `excel2pdf`

Evaluate whether these share one `OfficeToPdfUseCase` plus command-specific descriptors.

### Image family

- `img-compress`
- `img-to-png`
- `img-to-jpg`
- `img-to-webp`
- `img-to-gif`
- `img-to-heic`

### Aggregate operations

- `merge-pdf`
- `img-merge`
- `img-stitch`

Each migration must remove the obsolete parallel policy after the shared owner is active.

---

## 11. Phase 6 - Convert Workspace Presentation Consolidation

Move the Convert screen's product state into the shared `ConvertWorkspaceController`.

Recommended migration order:

1. command catalog;
2. command grouping;
3. selected command;
4. selected files;
5. file compatibility;
6. validation;
7. option availability;
8. `CanStart`;
9. start/cancel actions;
10. progress state.

After each step, remove the corresponding Native/Fluent policy instead of leaving compatibility copies indefinitely.

---

## 12. Phase 7 - History and Task Presentation Consolidation

Expand the existing shared history/feed work into a presentation controller.

Native and Fluent must no longer independently decide:

- status interpretation;
- resume availability;
- cancel availability;
- retention wording policy;
- file-count wording;
- task action semantics.

---

## 13. Phase 8 - Settings Presentation Consolidation

Preserve the current registry/descriptor work and migrate the remaining shared settings presentation policy.

Explicitly cover:

- dynamic settings;
- static settings;
- output mode;
- language;
- parked retention;
- LibreOffice management;
- settings reload and cross-surface synchronization.

The key distinction is:

```text
shared setting state/policy
        versus
platform-specific control rendering
```

---

## 14. Phase 9 - Remove Transitional Layers

After migration, re-evaluate transitional abstractions instead of preserving them by inertia.

Review:

- whether `FileProcessor` is still a necessary facade;
- whether `ConvertCommandRunner` still has a distinct responsibility;
- whether `ConvertCommandRegistry` has become a registry only;
- whether any UI command switch remains;
- whether UI still directly performs persistence workflow;
- whether Native and Fluent still carry parallel application logic.

Do not keep a transitional layer merely because it existed before the architecture migration.

---

## 15. Phase 10 - Final Architecture Audit

The final audit must evaluate ownership and dependency direction, not file size.

### Backend questions

- Does every conversion command have one authoritative use-case owner?
- Can validation, planning, execution, lifecycle, and result semantics be understood from that owner?
- Are processors limited to technical/domain operations?

### UI questions

- Does every major screen have one shared presentation owner?
- Do Fluent and Native only render/bind shared product state?
- Are command/product policies absent from renderers?

### Cross-surface question

- Are Fluent/Native differences limited to rendering and platform interaction?

### Prevention question

- Would CI fail if a developer reintroduced business logic into a UI surface?

All answers must be yes before this project is considered complete.

---

## 16. Commit Strategy

All work under this plan must keep the repository's strict atomic-commit rules.

- different responsibilities or independently revertible changes must be separate commits;
- split same-file hunks when they represent different semantics;
- every staged state must build/test;
- never use `git add .` or `git add -A`;
- never skip hooks;
- no rebase;
- no push unless explicitly authorized;
- no PR/merge unless explicitly authorized.

Commit header:

```text
type(scope): subject
```

Constraints:

- at most 72 characters;
- no trailing period.

Commit body is always exactly two numbered English lines:

```text
1. ...
2. ...
```

Avoid giant "architecture rewrite" commits.

---

## 17. Validation Requirements

Every migration must include the relevant combination of:

### Behavior tests

Cover:

- output;
- errors;
- cancellation;
- resume;
- progress;
- options;
- interactive capabilities.

### Cross-surface parity tests

Given equivalent command/input/settings, Fluent and Native must resolve to the same application plan and result semantics.

### Architecture guards

Prove that the removed dependency/policy path cannot silently return.

### Release builds

At minimum:

- Core;
- CLI;
- Fluent;
- Shell;
- Launcher.

Use warnings-as-errors.

### Full test suite

Architecture-boundary changes are not considered validated by local tests alone.

### NativeAOT

Whenever CLI/Core dependency or Native execution boundaries change, re-run the relevant NativeAOT publish and smoke validation.

Do not restore or depend on the obsolete missing `scripts/smoke_test_aot.ps1`.

---

## 18. Explicit Non-Goals and Prohibitions

### Do not split files merely because they are large

File size is not an architecture boundary.

### Do not treat helper extraction as completion

If Fluent and Native still orchestrate separate workflows around a shared helper, the architecture problem remains.

### Do not create two product controllers

`FluentConvertController` and `NativeConvertController` are not acceptable if they independently own product state. Platform adapters may be separate; product policy may not.

### Do not introduce UI dependencies into Core/Application

No WinUI/HWND/GDI dependency may be added for the sake of code reuse.

### Preserve the Shell NativeAOT boundary

`ClickraShell` remains a thin NativeAOT COM shell extension. Do not solve sharing by adding a ProjectReference from Shell to the full Core project.

### Preserve intentional platform behavior

The migration must not accidentally remove:

- Native password interaction;
- visual splitter behavior;
- WM_CLOSE/window lifecycle semantics;
- tray behavior;
- resume semantics;
- task checkpoints;
- NativeAOT compatibility.

---

## 19. Execution Order

Execute strictly in this order:

```text
Phase 0   Architecture baseline + guards
Phase 1   Application contracts / skeleton
Phase 2   decrypt-pdf vertical slice
Phase 3   split-pdf vertical slice
Phase 4   compress-pdf vertical slice

          Architecture checkpoint

Phase 5   Remaining conversion use cases
Phase 6   ConvertWorkspace presentation consolidation
Phase 7   History / Task presentation consolidation
Phase 8   Settings presentation consolidation
Phase 9   Remove transitional facades / duplicate paths
Phase 10  Final architecture audit + regression + NativeAOT
```

Do not skip the three difficult vertical slices and immediately perform a repository-wide mechanical migration.

---

## 20. Definition of Done

This architecture project is complete only when all of the following are true.

### Backend

One use case equals one authoritative workflow owner.

### UI

One screen equals one authoritative presentation state/controller.

### Renderers

Fluent and Native contain rendering/platform concerns rather than conversion business policy.

### Cross-surface

The same product state and application workflows drive both UI surfaces.

### Prevention

Architecture tests make new policy scattering fail CI.

The final target is:

```text
One Product Model
        +
One Application Workflow per Use Case
        +
One Presentation State per Screen
        +
Multiple Thin UI Renderers
```

The purpose of this plan is not to make the repository look cleaner. It is to make ownership explicit enough that product logic cannot keep drifting back into unrelated layers and UI surfaces.