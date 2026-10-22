# Clickra Documentation

Welcome to the Clickra documentation index. All project documentation is organized into clear domain categories:

## 🗺️ Roadmap & High-Level Plans
- [ROADMAP.md](ROADMAP.md): High-level feature roadmap, architecture milestones, and technical debt ledger.

## 🏛️ Architecture (`docs/architecture/`)
Architecture documentation, boundary invariants, layered structure maps, and ADRs:
- [Architecture Overview](architecture/README.md): .NET 8 baseline, NativeAOT compilation, and subsystem boundaries.
- [Layered Architecture Maps](architecture/diagrams/overview.html): Interactive L0–L4 layered diagrams.
- [Decisions & Principles](architecture/decisions/): Architectural Decision Records (ADRs).

## 🛠️ Development & Operations (`docs/development/`)
Guides for building, testing, packaging, signing, and releasing Clickra:
- [Local Build Notes](development/local_build_notes.md): Building locally with NativeAOT and Windows SDK tools.
- [Release Guidelines](development/release_guideline.md): Step-by-step release process and version bumping.
- [CI/CD Release Guide](development/CI_CD_DUAL_RELEASE_GUIDE.md): Automated release workflows and pipelines.
- [Code Signing Policy](development/CODE_SIGNING_POLICY.md): Signing keys, maintainer roles, and release integrity.
- [Windows Compatibility & MSIX Sandbox](development/WINDOWS_COMPATIBILITY_AND_MSIX_SANDBOX.md): Packaged storage and shell extensions.
- [PR Tool Documentation](development/pr_tool.md): Local gates, preflight checks, and PR acceptance assistant.
- [Troubleshooting & Resolutions](development/TROUBLESHOOTING_AND_RESOLUTIONS.md): Historical troubleshooting notes.

## 📐 Specifications (`docs/specs/`)
Functional and technical design specifications for future or specialized capabilities:
- [Translation Rules Specification](specs/translation_rules.md): Layout analysis, bypass rules (§2), and translation repair.
- [LibreOffice Engine Plan](specs/libreoffice_engine_plan.md): Local document conversion engine specifications.
- [Markdown Document Templates](specs/markdown_document_templates.md): Template architecture and schema design.
- [PDF Compression Roadmap](specs/pdf_compression_future_roadmap.md): Strategy for PDF compression optimization.

## 🏪 Microsoft Store Assets (`docs/store/`)
Localized listing metadata for the Microsoft Store:
- [English (en-US)](store/StoreListing_EN.md)
- [Traditional Chinese (zh-TW)](store/StoreListing_ZH.md)
- [Simplified Chinese (zh-CN)](store/StoreListing_ZH-CN.md)
- [Japanese (ja-JP)](store/StoreListing_JA.md)
- [Korean (ko-KR)](store/StoreListing_KO.md)

## 📜 Historical Archives (`docs/history/` & `docs/development/archive/`)
Archived design proposals, support tickets, and deprecated dual-track artifacts retained for provenance.
