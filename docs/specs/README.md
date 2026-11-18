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
| **[pdf_password_unlock_plan.md](pdf_password_unlock_plan.md)** | Password-protected PDFs (Core) | Planned | Every PDF action unlocks in place: restriction-only files without asking, open-password files ask once per batch; a real per-file skip; decrypt skips unprotected files; right-click runs skip and hand off to the main window. Scheduled after `feat/avalonia-views`. |

---

## 🏛️ Relationship with Architecture & Roadmap

- **[docs/ROADMAP.md](../ROADMAP.md)**: Product-level milestone tracking and priority snapshots.
- **[docs/architecture/spec/module_architecture_unification_manifesto.md](../architecture/spec/module_architecture_unification_manifesto.md)**: Engineering manifesto for capability module standardization and execution pipeline unification.
- **[docs/development/module_conventions.md](../development/module_conventions.md)**: Capability module folder hierarchy and layer boundary rules.
