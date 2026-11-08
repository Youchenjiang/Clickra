# Clickra Technical Specifications & RFCs

Welcome to the technical design specifications and RFC directory. This folder contains detailed functional and architectural specifications for new capabilities, format engines, and long-term evolutionary plans.

---

## 🗺️ Specifications Index

| Specification | Target Scope | Status | Description |
| :--- | :--- | :--- | :--- |
| **[v4_cross_platform_unification.md](v4_cross_platform_unification.md)** | `v3.19.0` → `v4.0.0` | Approved Proposal | Three-phase roadmap toward pure `net8.0` Core, cross-platform universal CLI, and unified modern GUI. |
| **[translation_rules.md](translation_rules.md)** | `[F2-16]` PDF Translation | Implemented & Maintained | Comprehensive layout analysis, math formula protection, bypass rules, and repair pipelines. |
| **[libreoffice_engine_plan.md](libreoffice_engine_plan.md)** | `[F2-5]` LibreOffice Engine | Implemented & Maintained | Local LibreOffice headless document conversion engine and installer automation. |
| **[markdown_document_templates.md](markdown_document_templates.md)** | `[F2-31]` Markdown to PDF/DOCX | Implemented & Maintained | Template architecture, typographic CSS models, and DOCX template import schema. |
| **[pdf_compression_future_roadmap.md](pdf_compression_future_roadmap.md)** | `[F2-9]` Extreme Compression | Planning / Phased | Four-phase compression strategy: DFS GC orphan cleanup, Object Streams (ObjStm), JBIG2, and font subset merging. |

---

## 🏛️ Relationship with Architecture & Roadmap

- **[docs/ROADMAP.md](../ROADMAP.md)**: Product-level milestone tracking and priority snapshots.
- **[docs/architecture/spec/module_architecture_unification_manifesto.md](../architecture/spec/module_architecture_unification_manifesto.md)**: Engineering manifesto for capability module standardization and execution pipeline unification.
- **[docs/development/module_conventions.md](../development/module_conventions.md)**: Capability module folder hierarchy and layer boundary rules.
