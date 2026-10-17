# Module Architecture Conventions

This document specifies the architectural conventions for capability modules in `Clickra.Core/Modules/`.

## 1. Capability-First Organization

In accordance with [module_first_architecture_decision.md](file:///docs/development/module_first_architecture_decision.md), product capability is the primary directory and ownership axis. Technical roles are secondary and placed within each module.

```text
src/Clickra.Core/Modules/<Capability>/
  Application/       # (Mandatory for conversion modules) Use cases, options, request models
  Processing/        # (Mandatory for conversion modules) Processors, conversion algorithms
  Presentation/      # (Optional) Only if module owns capability-specific UI presentation state
  Infrastructure/    # (Optional) Only if module encapsulates external engines or external processes
  <Domain>/          # (Optional) Domain-specific pipelines, models, or persistence
```

## 2. Directory & Role Invariants

### 2.1 Application Layer (`Application/`)
- Every conversion capability module must have an `Application/` directory.
- All use case classes must inherit from the shared use case base hierarchy:
  - `PerFileConversionUseCaseBase`
  - `SingleOutputConversionUseCaseBase`
  - `OfficeToPdfUseCaseBase`
- All caller-facing option records (`*Options`), settings DTOs, and request records (`*Request`) must reside in `Application/`. They must not reside in `Processing/` or `Infrastructure/`.
- Use cases must accept caller-supplied options via `request.Options` and only fall back to `SettingsController` when no options are provided.

### 2.2 Processing Layer (`Processing/`)
- Every conversion capability module must provide a processing layer named `Processing/` (with `Execution/` permitted for `PdfTranslation`).
- All conversion processor classes must inherit from `SingleFileProcessorBase` or `MultiFileProcessorBase`.
- Subdirectories under `Processing/` are organized freely by the module's processing needs.
- Processors must not access `ClickraStorage` directly, nor access presentation or UI types.

### 2.3 Presentation Layer (`Presentation/`)
- A module creates a `Presentation/` subdirectory **only** when it owns typed UI presentation controllers or state models (e.g. `PdfSplit`, `HistoryTasks`, `Settings`).
- Presentation surfaces must consume `Application/` contracts and must never depend directly on `Processing/` or `Infrastructure/`.

### 2.4 Infrastructure Layer (`Infrastructure/`)
- A module creates an `Infrastructure/` subdirectory **only** when it encapsulates external binaries, external process runners, or third-party engine installers (e.g. `Office` LibreOffice installer and helpers).
- Infrastructure types must not be exposed directly to `Presentation/`. An `Application/` DTO or controller must mediate access.

## 3. Automated Guard Verification

The conventions above are guarded by automated architecture boundary tests in `TestSuite.ArchitectureBoundaries.cs`:
- Module-first capability axis test
- Use case base inheritance and application placement test
- Processor base inheritance and processing/execution placement test
- Module options placement in `Application/`
- Presentation prohibition of module `Processing/` and `Infrastructure/` references
