# Clickra Architecture Maps & Diagnostics

This directory contains Clickra's layered interactive architecture diagrams (L0 to L4) and the architecture backlog.

## Interactive HTML Architecture Maps

| Layer | Map | Description |
|---|---|---|
| **Overview** | [`overview.html`](overview.html) | All layers (L0 through L4) on one page |
| **L0 Entry Points** | [`l0_entry_points.html`](l0_entry_points.html) | Context menu, CLI, Launcher, Fluent entry points and process handoffs |
| **L1 Presentation** | [`l1_presentation.html`](l1_presentation.html) | Screen renderers and shared Presentation controllers/models |
| **L2 Application** | [`l2_application.html`](l2_application.html) | Application services, conversion use cases, task lifecycle |
| **L3 Infrastructure** | [`l3_infrastructure.html`](l3_infrastructure.html) | Processors, storage, Office engines, cross-cutting support, external dependencies |
| **L4 Modules Internal** | [`l4_modules_internal.html`](l4_modules_internal.html) | Every file under `src/` with its references, reference loops (red), and translation folder analysis |

Standalone SVG exports are in [`svg/`](svg/).

Line colour = colour of the calling layer. Red dashed = skips a layer or forms a loop. Grey dashed = Fluent (not shipped) or external.

## Regenerating

Run from the repo root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\architecture\update_architecture_diagrams.ps1
```

It does three things:

1. `scripts/architecture/SemanticGraph` (Roslyn) compiles every project and writes `scripts/architecture/semantic-msbuild.json`. It uses `..\.clickra-tools\dotnet\dotnet.exe` when present, otherwise `dotnet`. The generator stops if any project has compile errors.
2. `file_lines.py` writes `file_lines.json` (line count of each `.cs`).
3. `generate_layers.py` redraws every HTML page and SVG here.

### What is automatic and what is not

| Part | Automatic | Manual |
|---|---|---|
| L4 | Everything: files, panels' contents, arrows, loops, layout | Panel list and placement (`PANEL_INFO` in `l4_block.py`) when a new top-level folder appears |
| L1 / L2 | Which files sit on each card, whether each line is drawn, "also called by" notes | New cards: add the box to `cards.py` and place it in `generate_layers.py` / `l2_block.py` |
| L3 | File lists per processor, Office and storage | Box placement |
| L0 | — | Hand drawn; the name check below still covers it |
| Known-problem lines (e.g. layer skips) | Disappear by themselves once the code no longer has them (`problem_link`) | — |

Checks printed by `generate_layers.py`:

- `names ok`, or a list of file/type names shown on L0–L3 that no longer exist in `src/`.
- An error if any presentation or application file is not on any L1/L2 card (add it to `cards.py`).
- Lines that are planned but no longer exist in code.

## Architecture Specifications & Backlog

- **Canonical Architecture Guide**: [`clickra_architecture.md`](clickra_architecture.md) — As-built system topology, module ownership invariants, and design records.
- **Architecture Backlog**: [`architecture_backlog.md`](architecture_backlog.md) — Current technical debt and layer leaks ("10/20 現況" status).
- **Baseline Evaluation Report**: [`evaluation_report_20261012.md`](evaluation_report_20261012.md) — Initial baseline evaluation report.
- **Drawing Conventions**: `.agent/skills/layered-architecture-diagram/SKILL.md`.
