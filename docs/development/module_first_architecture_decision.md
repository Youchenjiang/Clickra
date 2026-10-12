# Module-first architecture decision

Status: **Accepted for migration**

Baseline evaluated: 6a789c507c7f61bd28d4e609ab6bd7be20589ec5

## Decision

For new Clickra.Core product code, **capability/module is the primary directory and ownership axis**.
Technical responsibility is secondary and belongs inside the owning capability when needed.

Prefer:

    Modules/
      PdfTranslation/
        Model/
        Analysis/
        Translation/
        Rendering/

      Settings/
        Application/
        Presentation/
        Persistence/

Do not organize new feature-private code as:

    Models/
      PdfTranslation/

    Analysis/
      PdfTranslation/

    Presentation/
      Settings/

    Storage/
      Settings/

The rule is:

> **First choose the product capability that owns the code. Then choose the technical role inside that capability.**

This is a repository-organization rule, not a requirement that every capability contain every technical role.

## Why

The current repository was historically organized around large horizontal buckets such as
Application, Presentation, Processors, Storage, and Models. That structure is useful for describing
dependency roles, but it is not the strongest representation of actual ownership.

Two measurements were used to compare a layer-first partition with a capability/module-first
partition over current Clickra.Core + linked Shared production C# code.

### Dependency locality

The first migration slice was re-measured with an MSBuildWorkspace-backed Roslyn semantic graph.
It loaded all five production projects with **0 semantic errors** and **0 workspace failures**,
covering 264 production C# files and 1,302 semantic file-to-file edges. Of those, 789 edges are
inside Clickra.Core + linked Shared code and are used for this partition comparison.

| Partition | Same-group edges | Same-group rate | Size-adjusted lift | Graph modularity |
| --- | ---: | ---: | ---: | ---: |
| Layer-first | 481 | 61.0% | 2.01x | 0.3381 |
| Module-first | 611 | 77.4% | 2.44x | 0.4762 |

The dependency graph therefore clusters more strongly by capability than by technical layer.

### Git co-change locality

Large historical refactors bias the full-history graph toward horizontal layers, because those
changes deliberately touched many files in one technical bucket. A sensitivity analysis of normal
small/medium C# commits removes most of that migration bias.

| Maximum files in commit | Layer-first modularity | Module-first modularity | Layer single-group commits | Module single-group commits |
| --- | ---: | ---: | ---: | ---: |
| 5 | 0.3173 | **0.3910** | 153 | **167** |
| 10 | 0.3939 | **0.4236** | 166 | **181** |
| 20 | 0.3063 | **0.3828** | 171 | **185** |

For ordinary changes, capability-first organization also better matches files that evolve together.

These measurements are architectural evidence produced from compiler-resolved symbols rather than
type-name matching. The analyzer is still a temporary engineering tool; it should be productized
before dependency metrics themselves become a CI truth source.

## Target shape

The intended repository direction is a **feature-oriented modular monolith**:

    Clickra.Core/
      Modules/
        Conversion/
        HistoryTasks/
        Settings/
        PdfTranslation/
        Office/

      Kernel/
      Infrastructure/
      Shared/

Inside a module, use technical subdirectories only when they improve clarity:

    Modules/PdfTranslation/
      Model/
      Analysis/
      Translation/
      Rendering/

or:

    Modules/HistoryTasks/
      Application/
      Presentation/
      Persistence/

Do not create empty symmetry merely to make modules look alike.

## Dependency model

Module-first organization does **not** remove dependency rules.

Global rules remain:

1. Product hosts are outermost.
2. The small shared Kernel is innermost.
3. Module-to-module dependencies must be intentional and explicitly allowed.
4. UI technology must not leak into application/domain contracts.
5. Persistence and external systems stay behind narrow owners; use ports only where a mutable or
   external boundary benefits from one.
6. Multiple renderers for the same product screen share one Presentation state/model.
7. Each conversion use case keeps one authoritative Application workflow.

Within a module, technical roles may still have an inward dependency direction such as:

    Presentation
        |
        v
    Application
        |
        v
       Port
      /    \
     v      v
    Persistence / External adapters

This is intentionally local. The repository must not re-create global top-level
Application/Presentation/Models/Processors/Storage buckets as the primary organization axis.

## Migration rule

Migration is incremental.

The existing generic top-level buckets are a **shrinking legacy baseline**:

- src/Clickra.Core/Application
- src/Clickra.Core/Presentation
- src/Clickra.Core/Processors
- src/Clickra.Core/Storage
- src/Clickra.Core/Models

Existing files may be moved out of those directories over time.

**New production C# files must not expand those legacy buckets.**

New capability-owned code should use:

    src/Clickra.Core/Modules/<Capability>/...

The architecture guard reads
tests/Clickra.Core.Tests/Architecture.ModuleFirstLegacyBuckets.txt as the migration baseline.
Deleting/moving entries is allowed. Adding a new path to that baseline requires an explicit
architecture decision and guard review rather than routine feature work.

The Modules directory is also guarded against technical-role first-axis directories such as:

- Modules/Application
- Modules/Presentation
- Modules/Models
- Modules/Processors
- Modules/Storage
- Modules/Analysis
- Modules/Rendering
- Modules/Infrastructure

Those roles belong below a capability, not above it.

## Shared code rule

Do not move code into Kernel, Shared, or another global bucket merely because more than one file
uses it.

Shared code must satisfy a stronger ownership test:

- it is genuinely capability-neutral;
- it has stable semantics independent of one feature;
- moving it to the shared kernel reduces dependency coupling rather than hiding feature ownership.

Feature-private models remain with the feature even if their type category is generic.

For example, PDF paragraph/layout/translation models belong with PdfTranslation; they should not
be placed in a repository-wide Models bucket solely because they are model types.

## Ports rule

Ports are local tools, not a mandatory layer.

Prefer explicit ports at stateful or external boundaries such as:

- task journal / persistence;
- settings persistence;
- Office / LibreOffice installation integration;
- translation providers.

Do not introduce a port/interface merely to mirror every concrete class, and do not add a DI
container solely for architectural symmetry.

## Preserved product constraints

This decision does not authorize behavioral changes to:

- task-record formats;
- resume identity/index semantics;
- parked-task atomic transitions;
- retention semantics;
- legacy active.tmp;
- ConversionTaskLifecycle;
- output planning;
- quiet mode;
- Native / Fluent parity;
- NativeAOT boundaries;
- Shell/Launcher package identity;
- Store/signing/distribution behavior.

ParkedTaskCoordinator remains internal/private storage implementation.
ClickraShell remains a thin NativeAOT shell extension and must not gain a full Clickra.Core
project reference for convenience.

## Follow-up

The MSBuild-backed Roslyn semantic graph is now the measurement method used for migration review.
Its SCC report confirms, among other things, that the Storage implementation is one persistence
cluster while PDF translation has cohesive model/pipeline SCCs inside its capability.

Future slices should rerun the semantic graph after ownership moves. This does not change the
primary rule accepted here: **capability first, technical role second**, unless later evidence is
strong enough to justify an explicit architecture-decision change.
