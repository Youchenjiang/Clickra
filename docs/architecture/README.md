# Clickra Architecture Maps & Diagnostics

This directory contains Clickra's layered interactive architecture diagrams (L0 to L4), dependency graphs, and architectural backlog records.

## Interactive HTML Architecture Maps

| Layer | Map | Description |
|---|---|---|
| **Overview** | [`overview.html`](overview.html) | Full interactive layered system map (L0 through L4) |
| **L0 Entry Points** | [`l0_entry_points.html`](l0_entry_points.html) | Windows context menu, CLI, Launcher, Fluent entry points and process handoffs |
| **L1 Presentation** | [`l1_presentation.html`](l1_presentation.html) | Screen renderers and shared Presentation controllers/models |
| **L2 Application** | [`l2_application.html`](l2_application.html) | Application services, conversion use cases, and task lifecycle orchestration |
| **L3 Infrastructure** | [`l3_infrastructure.html`](l3_infrastructure.html) | Processors, storage, external engines (LibreOffice/Word), and cross-cutting dependencies |
| **L4 Modules Internal** | [`l4_modules_internal.html`](l4_modules_internal.html) | Complete 273-file dependency graph, inner module links, cycle detection (Tarjan SCC), and cross-panel references |

Vector standalone SVG exports are available in [`svg/`](svg/).

## Generating Diagrams

The diagrams are generated automatically from repository source code using scripts in `scripts/architecture/`:

1. **Scan dependencies**:
   ```bash
   python scripts/architecture/scan_dependencies.py
   ```
   Scans `src/` to extract type declarations and references, writing `scripts/architecture/all.json`.

2. **Generate layers**:
   ```bash
   python scripts/architecture/generate_layers.py
   ```
   Generates interactive HTML diagrams and SVGs into `docs/architecture/`.

## Architecture Records & Backlog

- [`architecture_backlog.md`](architecture_backlog.md): Tracked architectural backlog items across L0–L4.
- [`evaluation_report_20261012.md`](evaluation_report_20261012.md): Initial architecture baseline evaluation report.
- Canonical architecture specification: [`docs/development/clickra_architecture.md`](../development/clickra_architecture.md).
