# Storage, Processor State, and Namespace Architecture Cleanup Plan

## 1. Purpose

This document records the architecture debt verified on `main` after PR #170 and defines the execution order for removing it without turning the work into a repository-wide rewrite.

It is a focused companion to `application_ui_architecture_consolidation_plan.md`:

- that document defines the overall product/application/UI target;
- this document tracks the remaining storage, processor-state, namespace, and shared-Core boundary problems that can otherwise be forgotten or mixed together during later refactors.

The goal is not to reduce line count. The goal is to make dependency direction, state ownership, and module boundaries explicit and enforceable.

> **Execution rule:** verify current `main` before every slice. Counts and call sites below are a 2026-10-11 baseline, not permanent truth.

---

## 2. Verified Baseline

The following findings were re-checked against `main` at merge commit `55a17d795b8e89028a2a2d1bdd91598e87719c37`.

### 2.1 Parked-task facade

`ClickraStorage.GetEffectiveParkedRetentionDays(HistoryEntry)` has **zero callers** in `src/` and `tests/`.

The remaining parked-task facade methods are active. Current approximate call counts are:

| Method | Calls |
| --- | ---: |
| `ParkTask` | 13 |
| `GetParkedTasks` | 8 |
| `CancelParkedTask` | 6 |
| `ClaimParkedTaskForResume` | 14 |
| `GetParkedRetentionDays` | 14 |
| `SetParkedRetentionOverride` | 4 |
| `AdjustParkedRetention` | 10 |
| `DescribeParkedRetention` | 2 |
| `CalculateRetentionInfo` | 6 |
| `GetParkedRetentionInfo` | 12 |

`ParkedTaskCoordinator` is currently a **private nested implementation owner inside `ClickraStorage`**. It is intentionally not a public application API.

### 2.2 `ClickraStorage` dependency surface

Current direct references to `ClickraStorage` include approximately:

- CLI: **76** references;
- Fluent: **62** references;
- Processor files directly referencing `ClickraStorage`: **16** files;
- Processor-side `ClickraStorage.GetSetting...` reads: **49** references.

These counts are diagnostics only. Success is measured by ownership and dependency direction, not by chasing a specific number.

### 2.3 Namespace baseline

Several physically separated Core areas still use the flat namespace `Clickra.Core`, including:

- `Storage/`;
- `Translation/`;
- `Localization/`;
- `Diagnostics/`;
- `Fonts/`.

All currently scanned processor source files under `Processors/` use the flat namespace:

```text
Clickra.Core.Processors
```

This remains true even for deep subtrees such as `Processors/PdfTranslation/...`.

### 2.4 Shared-Core presentation/platform content

The Core project currently contains types that are not domain/application logic, including:

- `Layout/DashboardLayout.cs` - Native dashboard geometry and hit-test layout;
- `Localization.Cli.cs` - CLI-facing strings;
- `Localization.Fluent.cs` - Fluent-facing strings;
- `Rendering/VectorIcons.cs` - shared Win32/XAML presentation geometry;
- `FluentRuntimeHelper.cs` - Windows App Runtime and Store package/runtime integration.

These are not all equivalent problems. Shared presentation infrastructure can legitimately be shared, but its ownership should not be confused with domain/application Core.

### 2.5 Translation naming overlap

Two directories currently use the word `Translation` for different responsibilities:

```text
Core/Translation/
Processors/PdfTranslation/Translation/
```

Their responsibilities are distinct:

- `Core/Translation/` owns provider/engine infrastructure such as `ITranslationEngine`, fallback, provider implementations, timeout, and quality guards;
- `Processors/PdfTranslation/Translation/` owns the PDF translation pipeline stage: batching, paragraph processing, reference bypass, post-processing, diagnostics, and health reporting.

The problem is discoverability and bounded-context naming, not duplicate translation engines.

---

## 3. Architecture Decisions

### 3.1 Do not expose `ParkedTaskCoordinator` to UI callers

Do **not** replace `ClickraStorage` parked-task calls with direct UI calls to `ParkedTaskCoordinator` merely to remove forwarding methods.

That would change:

```text
UI -> public storage facade -> private coordinator
```

into:

```text
UI -> storage implementation coordinator
```

and would make encapsulation worse.

The coordinator may remain hidden until a real application-level task/history API exists. Transitional forwarding is acceptable when it preserves a stronger boundary than the alternative.

### 3.2 Dead forwarding is different from active compatibility forwarding

A forwarding method with zero callers should be removed after verification.

An active forwarding method should not be removed unless callers move to an architecturally better owner. Mechanical replacement with an internal implementation type does not count as architecture improvement.

### 3.3 Processor behavior must become explicit

The highest-priority dependency leak is processor code reading mutable global settings through `ClickraStorage`.

Target direction:

```text
UI / settings
    |
    v
Application use case snapshots normalized options
    |
    v
Processor receives explicit immutable options
```

Processors should not decide their runtime behavior by reading hidden global configuration during execution.

### 3.4 Namespace changes must follow responsibility boundaries

Do not perform a repository-wide namespace rename for cosmetic consistency.

Introduce namespace boundaries only when a responsibility boundary is understood and can be guarded. A namespace migration must improve dependency enforcement, not just mirror folders.

### 3.5 Shared UI infrastructure is not automatically domain Core

Do not move presentation helpers blindly. First classify each type as one of:

- domain/application policy;
- shared presentation model/infrastructure;
- Native-only presentation;
- Fluent-only presentation;
- Windows/package/platform integration.

Move only when the target owner is clearer than the current one.

---

## 4. Execution Order

Execute the following slices in order unless new evidence requires the plan itself to be updated first.

### Slice 1 - Remove verified dead storage facade surface

Scope:

- remove `ClickraStorage.GetEffectiveParkedRetentionDays` if it is still unused;
- audit other public `ClickraStorage` members for zero production/test callers;
- remove only members proven dead;
- update architecture tests that explicitly pin a deleted dead facade.

Do not:

- expose `ParkedTaskCoordinator`;
- migrate active parked-task callers;
- combine this with processor option migration;
- move files/namespaces.

Exit criteria:

- every deleted API has zero callers before deletion;
- no product behavior changes;
- architecture guards and full tests pass.

### Slice 2 - Establish a shrinking Processor global-state guard

Before broad migration, make the current processor dependency leak measurable and monotonic.

Add an architecture guard for direct processor access to settings/storage, with an explicit baseline of existing exceptions.

At minimum track:

```text
ClickraStorage.GetSetting
ClickraStorage.GetSettingBool
ClickraStorage.GetSettingInt
ClickraStorage.SaveSetting
```

The baseline may shrink only. New processor-side global setting reads must fail CI.

Do not weaken or suppress the guard to land later slices.

Exit criteria:

- current debt is represented explicitly;
- a new processor global setting read fails a guard test;
- no existing behavior changes.

### Slice 3 - Migrate processor settings by conversion family

Remove global setting reads one vertical family at a time.

Recommended order:

1. PDF compression;
2. image compression;
3. image format conversion;
4. Office conversion policy;
5. Markdown conversion settings;
6. PDF translation settings;
7. remaining processor reads.

For each family:

1. identify every setting read inside the processor family;
2. move interpretation/defaulting/clamping to the application/use-case owner;
3. introduce or extend an immutable options/request object;
4. snapshot settings before execution starts;
5. pass normalized values into the processor explicitly;
6. remove processor-side `ClickraStorage` reads;
7. shrink the architecture baseline in the same PR;
8. verify Native/Fluent parity and resume/batch semantics where applicable.

Each family is a separate PR unless two changes are inseparable by behavior.

Exit criteria for this phase:

- processor-side global setting reads reach zero, or every remaining read has a documented non-setting storage responsibility scheduled separately;
- hidden mutable settings no longer determine processor behavior mid-run.

### Slice 4 - Separate storage/application access from raw `ClickraStorage`

After processor settings are explicit, re-audit the remaining CLI and Fluent `ClickraStorage` calls by responsibility:

- settings state;
- task lifecycle;
- parked-task actions;
- history feed;
- task record access;
- data/output directories;
- legacy compatibility.

Do not replace one god facade with another god interface.

Introduce narrow application-facing owners only where a coherent responsibility already exists, for example:

```text
HistoryController / task application service
SettingsController / settings application access
existing conversion lifecycle owners
```

Transitional `ClickraStorage` forwarding should be removed only after its callers have a better owner.

Exit criteria:

- UI code no longer orchestrates persistence/business transitions directly where an application owner exists;
- `ClickraStorage` trends toward persistence/infrastructure rather than being the product API.

### Slice 5 - Introduce meaningful namespace boundaries

Namespace work comes after ownership is stable enough to enforce useful rules.

Candidate boundaries to evaluate independently:

```text
Clickra.Core.Storage
Clickra.Core.Translation
Clickra.Core.Localization
Clickra.Core.Diagnostics
Clickra.Core.Fonts
Clickra.Core.Processors.PdfTranslation
```

Do not rename all processor subfolders mechanically in one PR.

Preferred strategy:

- start with a self-contained bounded context;
- update its consumers;
- add architecture tests that use the new namespace boundary;
- then continue to the next bounded context.

Exit criteria:

- namespace boundaries allow CI to prohibit at least one previously unenforceable dependency direction;
- namespace names reflect responsibility, not merely disk layout.

### Slice 6 - Re-home presentation/platform types from Core where justified

Review these independently:

#### `DashboardLayout`

Likely Native presentation ownership. Evaluate moving it toward the CLI dashboard presentation layer while preserving its testability and single-source geometry contract.

#### `Localization.Cli` / `Localization.Fluent`

Keep shared localization infrastructure centralized, but determine whether surface-specific catalogs should remain in the same Core namespace/module.

#### `VectorIcons`

Treat as shared presentation infrastructure, not domain logic. Do not duplicate path geometry between Fluent and Native merely to move it out of Core.

#### `FluentRuntimeHelper`

Treat as packaging/platform integration. Evaluate an infrastructure/platform owner rather than domain/application Core.

Each move must preserve NativeAOT and Store/package behavior.

### Slice 7 - Clarify PDF translation pipeline naming

After namespace and processor ownership work is stable, rename the nested PDF directory/context currently called `Translation` if a clearer name is justified.

Candidate concepts:

- `Pipeline`;
- `TextProcessing`;
- another name derived from the actual responsibility at migration time.

Do not rename `Core/Translation` merely to eliminate visual similarity. It already owns translation-engine infrastructure.

Exit criteria:

- a new contributor can distinguish translation provider infrastructure from the PDF translation pipeline without reconstructing the architecture from call sites.

---

## 5. PR and Commit Rules

This cleanup follows the repository's strict atomic-work rules.

- one PR may cover one coherent architecture slice;
- independently revertible responsibilities must be separate commits;
- do not combine dead-code cleanup, namespace migration, processor option migration, and UI relocation merely because they touch Core;
- use explicit path staging only;
- never use `git add .` or `git add -A`;
- never skip hooks;
- no force push;
- do not merge on behalf of the user;
- after merge, verify remote deletion, sync `main`, delete the local branch, and confirm a clean working tree before the next slice.

Commit format remains:

```text
type(scope): subject

1. ...
2. ...
```

The header is at most 72 characters and has no trailing period. The body is exactly two numbered English lines.

---

## 6. Guard Strategy

Every architecture migration should leave behind a guard that makes regression harder than before.

Preferred guard progression:

```text
measure current violation set
        |
        v
freeze explicit shrinking baseline
        |
        v
migrate one responsibility
        |
        v
shrink baseline in same PR
        |
        v
eventually replace baseline with absolute prohibition
```

Never solve a migration failure by broadening a baseline, weakening a rule, or adding a suppression for the new dependency.

---

## 7. Validation Requirements

For every slice, run the relevant subset plus the repository-required full checks.

Minimum expectations:

- `git diff --check`;
- warnings-as-errors builds for affected projects;
- architecture/localization guards;
- functional suite;
- NativeAOT publish when Core/CLI/native execution dependencies change;
- Fluent build when shared Core contracts consumed by Fluent change;
- exact-head GitHub checks after push;
- SonarCloud issue/duplication review and DeepSource checks;
- review-thread audit before declaring a PR clean.

For processor option migrations, add tests proving that explicit request/options values control behavior independently of later global setting changes.

---

## 8. Explicit Non-Goals

This plan does **not** authorize:

- a repository-wide namespace rewrite;
- a `ClickraStorage` replacement god service;
- exposing private coordinators just to remove forwarding methods;
- cosmetic file splitting;
- duplicate Native and Fluent application services;
- moving UI-specific code into Application for reuse;
- weakening current architecture guards;
- changing task lifecycle, legacy `active.tmp`, resume, or retention behavior as a side effect of cleanup;
- reintroducing retired command runners, output-estimation fallbacks, or Native command-routing switches.

---

## 9. Definition of Done

This cleanup track is complete when all of the following are true:

1. dead `ClickraStorage` facade APIs are gone;
2. processors do not read mutable global settings to determine conversion behavior;
3. application/use-case owners snapshot and normalize options before processor execution;
4. UI surfaces no longer use raw storage as a substitute for application orchestration where a coherent owner exists;
5. namespace boundaries expose meaningful bounded contexts and support enforceable dependency guards;
6. presentation/platform helpers in Core have explicit justified ownership rather than living there merely because they are shared;
7. translation provider infrastructure and the PDF translation pipeline are clearly distinguishable;
8. architecture baselines have only shrunk throughout the work;
9. NativeAOT, Fluent, task persistence, resume, and Store/package behavior remain intact.

The intended end state is not "fewer files" or "fewer forwarding methods". It is:

```text
explicit application state
        +
explicit processor inputs
        +
narrow infrastructure ownership
        +
enforceable dependency boundaries
```
