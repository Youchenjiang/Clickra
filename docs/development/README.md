# Clickra Development & Operations Guide

Welcome to the Clickra development guide index. This directory contains core operational playbooks, architecture conventions, packaging and release documentation, and developer tooling guides.

---

## 🚀 Daily Development Guides (Root)

These are the primary entry points for contributors and maintainers working on features, bug fixes, or architecture refactoring:

| Guide | Description |
| :--- | :--- |
| [Local Build Notes](local_build_notes.md) | Setting up Windows SDK, NativeAOT compiler, local self-signed certificates, and running tests. |
| [Module Conventions](module_conventions.md) | Capability-first architecture rules under `src/Clickra.Core/Modules/` and layer boundaries. |
| [New Command Checklist](new_command_checklist.md) | 7-stage end-to-end checklist for wiring a new conversion command from Core to Shell. |
| [Troubleshooting Guide](troubleshooting_guide.md) | Diagnosing common build errors, runtime crashes, and historical issue resolutions. |

---

## 📦 Release & Signing (`docs/development/release/`)

Guides for building, signing, packaging, and distributing Clickra across release channels:

| Document | Description |
| :--- | :--- |
| [Release Guidelines](release/release_guideline.md) | Version bumping procedures, release preflights, and tag triggers. |
| [CI/CD Release Guide](release/ci_cd_release_guide.md) | Automated GitHub Actions release pipelines and artifact verification. |
| [Code Signing Policy](release/code_signing_policy.md) | Release integrity, maintainer signing roles, and SignPath Foundation boundaries. |
| [Store Submission Guide](release/store_submission_guide.md) | Microsoft Store package generation, submission requirements, and flight gates. |

---

## 🪟 Windows Shell & Sandbox Integration (`docs/development/shell/`)

Technical specifications for Windows Explorer shell menus, COM servers, and package isolation:

| Document | Description |
| :--- | :--- |
| [Shell Extension Best Practices](shell/shell_extension_best_practices.md) | Windows 11 sparse package integration and IExplorerCommand COM servers. |
| [Shell Diagnostic Guide](shell/shell_diagnostic_guide.md) | Troubleshooting shell menu registration, registry entries, and execution hooks. |
| [Windows Sandbox Compatibility](shell/windows_sandbox_compatibility.md) | MSIX app container file system access, broadFileSystemAccess, and virtualization rules. |

---

## 🛠️ Developer Tooling (`docs/development/tools/`)

Automated assistants and local git / preflight workflow tools:

| Tool Guide | Description |
| :--- | :--- |
| [PR Tool Documentation](tools/pr_tool.md) | Local quality gates, preflight checks, CI verification, and review thread resolution. |
| [Git Tool Guide](tools/git_tool.md) | Worktree management, clean rebasing, and local branch maintenance helper. |

---

## 📜 Historical Archives (`docs/development/archive/`)

Archived technical design plans, deprecated dual-track configurations, and historical proposals retained for provenance:

- [Application UI Architecture Consolidation Plan](archive/application_ui_architecture_consolidation_plan.md)
- [Storage Processor Cleanup Plan](archive/storage_processor_architecture_cleanup_plan.md)
- [Module-First Architecture Decision](archive/module_first_architecture_decision.md)
- [Multi-Channel Signing Plan](archive/multi_channel_signing_distribution_plan.md)
- [SignPath Foundation Readiness](archive/signpath_foundation_application_readiness.md)
- [Store Optional Fluent Plan](archive/store_optional_fluent_plan.md)
- [Fluent Direct Distribution Plan](archive/fluent_direct_distribution_plan.md)
- [Dual-Track MSIX Guide](archive/dual_track_guide.md)
