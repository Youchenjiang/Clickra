# Clickra Documentation

Welcome to the Clickra documentation index. All project documentation is organized into clear domain categories:

## 🗺️ Roadmap & High-Level Plans
- [ROADMAP.md](ROADMAP.md): High-level feature roadmap, architecture milestones, and technical debt ledger.

## 🏛️ Architecture (docs/architecture/)
Architecture documentation, boundary invariants, layered structure maps, and ADRs:
- [Architecture Overview](architecture/README.md): .NET 8 baseline, NativeAOT compilation, and subsystem boundaries.
- [Layered Architecture Maps](architecture/diagrams/overview.html): Interactive L0–L4 layered diagrams.
- [Architecture Specifications](architecture/spec/): System architecture specifications and architectural audit reports.

## 🛠️ Development & Operations (docs/development/)
Guides for building, testing, packaging, signing, and releasing Clickra:
- [Local Build Notes](development/local_build_notes.md): Building locally with NativeAOT and Windows SDK tools.
- [Release Guidelines](development/release/release_guideline.md): Step-by-step release process and version bumping.
- [CI/CD Release Guide](development/release/ci_cd_release_guide.md): Automated release workflows and pipelines.
- [Code Signing Policy](development/release/code_signing_policy.md): Signing keys, maintainer roles, and release integrity.
- [Windows Compatibility & MSIX Sandbox](development/shell/windows_sandbox_compatibility.md): Packaged storage and shell extensions.
- [PR Tool Documentation](development/tools/pr_tool.md): Local gates, preflight checks, and PR acceptance assistant.
- [Module Conventions](development/module_conventions.md): Capability-first module structure and layer invariants.
- [New Command Checklist](development/new_command_checklist.md): Step-by-step checklist for introducing new CLI/UI commands.
- [Store Submission Guide](development/release/store_submission_guide.md): Microsoft Store package submission and flight rules.
- [Shell Diagnostic Guide](development/shell/shell_diagnostic_guide.md): Windows Explorer shell extension diagnostics.
- [Shell Extension Best Practices](development/shell/shell_extension_best_practices.md): Shell menu registration and COM server guidance.
- [Troubleshooting Guide](development/troubleshooting_guide.md): Historical troubleshooting notes and diagnostic fixes.
- [Git Tool Guide](development/tools/git_tool.md): Branching and worktree maintenance helper.

## 📐 Specifications (docs/specs/)
Functional and technical design specifications for future or specialized capabilities:
- [Translation Rules Specification](specs/translation_rules.md): Layout analysis, bypass rules (§2), and translation repair.
- [LibreOffice Engine Plan](specs/libreoffice_engine_plan.md): Local document conversion engine specifications.
- [Markdown Document Templates](specs/markdown_document_templates.md): Template architecture and schema design.
- [PDF Compression Roadmap](specs/pdf_compression_future_roadmap.md): Strategy for PDF compression optimization.

## 🏪 Microsoft Store Assets (docs/store/)
Localized listing metadata for the Microsoft Store:
- [English (en-US)](store/StoreListing_EN.md)
- [Traditional Chinese (zh-TW)](store/StoreListing_ZH.md)
- [Simplified Chinese (zh-CN)](store/StoreListing_ZH-CN.md)
- [Japanese (ja-JP)](store/StoreListing_JA.md)
- [Korean (ko-KR)](store/StoreListing_KO.md)

## 📜 Historical Archives (docs/history/ & docs/development/archive/)
Archived design proposals, support tickets, and completed phase implementation plans retained for provenance:
- [Dual-Track MSIX Guide](development/archive/dual_track_guide.md): Deprecated dual-track build configuration.
- [Application UI Architecture Consolidation Plan](development/archive/application_ui_architecture_consolidation_plan.md): Completed UI consolidation plan.
- [Storage Processor Cleanup Plan](development/archive/storage_processor_architecture_cleanup_plan.md): Completed storage/processor state refactoring.
- [Module-First Architecture Decision](development/archive/module_first_architecture_decision.md): Capability-first packaging rationale.
- [Multi-Channel Signing Plan](development/archive/multi_channel_signing_distribution_plan.md): Channel isolation and signing boundaries.
- [SignPath Foundation Readiness](development/archive/signpath_foundation_application_readiness.md): SignPath open-source signing application.
- [Store Optional Fluent Plan](development/archive/store_optional_fluent_plan.md): Store optional package research and gates.
- [Fluent Direct Distribution Plan](development/archive/fluent_direct_distribution_plan.md): Standalone Fluent MSIX direct distribution design.
- [Certification Fix v3.0.4](history/certification-fix-v3.0.4.md): Historical Windows App Certification Kit remediation.
