# Clickra Technical Specifications & RFCs

Welcome to the technical design specifications and RFC directory. This folder contains detailed functional and architectural specifications for new capabilities, format engines, and long-term evolutionary plans.

---

## 🗺️ Specifications Index

| Specification | Target Scope | Status | Description |
| :--- | :--- | :--- | :--- |
| **[v4_cross_platform_unification.md](v4_cross_platform_unification.md)** | `v3.19.0` → `v4.0.0` | Approved Proposal | Three-phase roadmap toward pure `net8.0` Core, cross-platform universal CLI, and unified modern GUI. |
| **[v4_cross_platform_master_plan.md](v4_cross_platform_master_plan.md)** | `v3.19.0` → `v4.0.0` | In Progress | Branch plan and atomic commit matrix for each phase; `feat/avalonia-shell` is the UI shell only, Core wiring lands in `feat/avalonia-views`. |
| **[v4_desktop_ui_layout.md](v4_desktop_ui_layout.md)** | `v4.0.0` Desktop GUI | Draft | Window size, page layouts, overflow/scroll rules, and visual tokens for the unified Avalonia desktop UI. |
| **[translation_rules.md](translation_rules.md)** | `[F2-16]` PDF Translation | Implemented & Maintained | Comprehensive layout analysis, math formula protection, bypass rules, and repair pipelines. |
| **[libreoffice_engine_plan.md](libreoffice_engine_plan.md)** | `[F2-5]` LibreOffice Engine | Implemented & Maintained | Local LibreOffice headless document conversion engine and installer automation. |
| **[markdown_document_templates.md](markdown_document_templates.md)** | `[F2-31]` Markdown to PDF/DOCX | Implemented & Maintained | Template architecture, typographic CSS models, and DOCX template import schema. |
| **[pdf_compression_future_roadmap.md](pdf_compression_future_roadmap.md)** | `[F2-9]` Extreme Compression | Planning / Phased | Four-phase compression strategy: DFS GC orphan cleanup, Object Streams (ObjStm), JBIG2, and font subset merging. |
| **[office_com_direct_plan.md](office_com_direct_plan.md)** | Office → PDF (Core) | Planned | Call Word/Excel/PowerPoint through NativeAOT-compatible COM instead of a hidden PowerShell `-EncodedCommand` script that security software flags. Scheduled after `feat/avalonia-views`. |
| **[feature_icon_recognition_plan.md](feature_icon_recognition_plan.md)** | Feature icons (context menu + Desktop) | Planned | Make the 22 feature icons distinguishable at 16 px and on dark backgrounds: one main symbol per action, one shared structure for format conversions, no text in icons. Scheduled after `feat/avalonia-views`. |
| **[v4_localization_plan.md](v4_localization_plan.md)** | Desktop localization (Desktop + Core locales) | Planned | Move all ~510 hard-coded Traditional Chinese strings in `Clickra.Desktop` to the Core locale files in five languages, per-language fonts, culture-aware dates, a guard against new hard-coded text; Core progress and error messages follow in a separate branch. First item of the 4.0 readiness checklist. |
| **[ui_writing_guide.md](ui_writing_guide.md)** | Interface writing rules (zh-TW, applies to all languages) | Active | What to write and leave out, the agreed vocabulary (處理, 停止, 完成／沒有完成, 使用限制 …), punctuation and spacing, how sentences are stored as locale keys (one key per full sentence, named parameters). Read before adding or changing any interface text. |
| **[print_preview_plan.md](print_preview_plan.md)** | Print preview and multi-file printing (A3 + A4 on the canvas) | Implemented | Shared Core layout planner for preview and printing, new print options (pages per sheet, order, border, actual size / custom %, grayscale, collate) and their defaults, the desktop print card and multi-file preview. |
| **[collage_layout_plan.md](collage_layout_plan.md)** | Collage onto a sized canvas (Core + Desktop) | Draft | Lay out images and PDF pages (mixed) on one canvas of a chosen size (paper or custom px/mm), grid or automatic arrangement, output PDF (pages kept as vectors) or images. After 4.0; layout to be discussed on the canvas first. |
| **[pdf_password_unlock_plan.md](pdf_password_unlock_plan.md)** | Password-protected PDFs (Core) | Implemented | Every PDF action unlocks in place: restriction-only files without asking, open-password files ask once per batch; a real per-file skip; decrypt skips unprotected files; right-click runs skip and hand off to the main window. Scheduled after `feat/avalonia-views`. |
| **[v4_release_plan.md](v4_release_plan.md)** | `v4.0.0` release scope and schedule | Approved | Narrowed 4.0 scope (confirmed 2026-11-28): the remaining PRs in order, the desktop UI taking over right-click runs, removal of Fluent / classic dashboard / `Clickra.exe` from the package, the 50 MB MSIX download cap, one acceptance round, and what moves to 4.1. Supersedes the sequence table in the master plan. |
| **[error_log_plan.md](error_log_plan.md)** | Error log (Core + Desktop) | Implemented | Daily log files under `logs/` with full exception detail and external tool exit codes, redaction of paths, passwords and API keys, background queued writes safe across processes, 14-day retention; "View log" and the problem report point to it. |

---

## 🏛️ Relationship with Architecture & Roadmap

- **[docs/ROADMAP.md](../ROADMAP.md)**: Product-level milestone tracking and priority snapshots.
- **[docs/architecture/spec/module_architecture_unification_manifesto.md](../architecture/spec/module_architecture_unification_manifesto.md)**: Engineering manifesto for capability module standardization and execution pipeline unification.
- **[docs/development/module_conventions.md](../development/module_conventions.md)**: Capability module folder hierarchy and layer boundary rules.
