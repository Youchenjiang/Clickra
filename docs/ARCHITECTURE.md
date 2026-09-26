# Clickra Architecture

## Current Shape

Clickra ships three Windows-facing pieces:

- `Clickra.Fluent`: WinUI 3 dashboard and right-click task-progress host. It references `Clickra.Core` directly and runs conversions in-process.
- `Clickra.CLI`: command-line and legacy Win32 dashboard/progress fallback.
- `ClickraShell`: NativeAOT shell extension for the Windows context menu.

`Clickra.Core` owns conversion logic, storage, localization, Office engine helpers, PDF processing, and history/active-job records.

## Why the architecture changed

NativeAOT came first because the shell extension is loaded inside
`explorer.exe`. That boundary benefits from a small native binary, deterministic
startup, and explicit unmanaged COM vtables. The CLI and original Win32
dashboard/progress window reused the same deployment model to avoid Python and
managed-runtime cold starts.

The Win32 UI proved expensive to evolve: responsive layout, DPI behavior,
localization, accessibility, dialogs, and standard Fluent interaction all had
to be implemented manually. The dashboard and progress window run out of
process, so they do not need to inherit Explorer's restrictions. WinUI 3 was
therefore added for user-facing windows while the NativeAOT shell boundary was
kept.

This is a deliberate split, not a failed NativeAOT migration:

- Keep `ClickraShell` NativeAOT and thin.
- Use `Clickra.Fluent` for the dashboard, right-click progress, dialogs, and
  settings.
- Keep conversion behavior in `Clickra.Core`.
- Keep `Clickra.CLI` for command-line use and temporary legacy UI fallback.

## Shared State

The Fluent dashboard and CLI both use `ClickraStorage`:

- `settings.conf` for preferences such as language, output directory, Office engine, and PDF options.
- `history.log` for completed jobs.
- `active.tmp` for currently running work.

These file formats are compatibility contracts. UI changes should read and write them through `ClickraStorage`.

## Fluent Dashboard

The Fluent dashboard is a full UI host, not a thin IPC wrapper:

- It calls `FileProcessor` methods directly.
- It uses `Localization.T(...)` for interface and processing messages.
- It reuses `LibreOfficeEngineInstaller` and `LibreOfficeHelper` for LibreOffice setup.
- It preserves the same settings keys as the legacy dashboard.

Explorer commands prefer packaged activation through
`IApplicationActivationManager`, passing the command and selected paths to
`Clickra.Fluent`. Direct launch of `Clickra.Fluent.exe`, followed by
`Clickra.exe`, is retained only as fallback.

## Network & External Dependencies Model

Clickra is designed as a **Local-First, privacy-respecting utility**. By principle:
- **Zero Telemetry**: Zero analytics, zero tracking, and zero background telemetry.
- **Local-First Execution**: File transformations and document conversions run 100% locally on your device without sending documents to cloud servers.
- **Lightweight Footprint**: Clickra avoids bundling multi-hundred-megabyte runtimes (no bundled Python, Chromium, or FFmpeg).

### Feature Dependency & Network Matrix

| Category | Command | Network Required? | External Software / Dependencies | Engine & Mechanism |
|---|---|:---:|---|---|
| **PDF Tools** | `merge-pdf` | ❌ 100% Offline | None | In-process PDFsharp |
| | `compress-pdf` | ❌ 100% Offline | None | Pure C# `PdfStructuralCompressionOptimizer` (stream compaction, font deduplication, image downsampling) + PDFsharp |
| | `split-pdf` | ❌ 100% Offline | None | In-process PdfPig + PDFsharp |
| | `decrypt-pdf` | ❌ 100% Offline | None | In-process PDFsharp |
| **Image Tools** | `img2pdf` | ❌ 100% Offline | None | In-process PDFsharp + GDI+/WIC |
| | `img-merge` ⚑ | ❌ 100% Offline | None | In-process PDFsharp |
| | `img-stitch` ⚑ | ❌ 100% Offline | None | In-memory canvas vertical stitching |
| | `img-to-png` / `jpg` / `gif` ⚑ | ❌ 100% Offline | None | Built-in Windows GDI+ / WIC encoders |
| | `img-to-webp` ⚑ | ⚠️ User-initiated Store preflight if extension is absent | Windows WebP Image Extension (if absent) | Windows WIC encoder with Store preflight fallback |
| | `img-to-heic` ⚑ | ⚠️ User-initiated Store preflight if extension is absent | Windows HEIF Image Extension (if absent) | Windows WIC / WinRT (`Microsoft HEIF Encoder`) with Store preflight fallback |
| **Office to PDF** | `word2pdf`<br>`excel2pdf`<br>`ppt2pdf` | ❌ 100% Offline | Microsoft Office or LibreOffice | 1. Local MS Office via COM Automation (preferred)<br>2. Local LibreOffice via headless CLI<br>3. Guided on-demand download of official LibreOffice MSI (~372 MB) if neither is installed |
| **PDF Translation** | `translate-pdf` | 🌐 **Requires Internet** | None | Text extracted locally and sent over HTTPS to Google Translate / MyMemory API; layout synthesis and PDF rendering are 100% local |

*(Note: Commands marked with ⚑ belong to the image-conversion roadmap. The image-compression preset/settings contract exists in Core, but `img-compress` is not currently exposed as a production command or processor and is therefore not listed as an active command here.)*

### Outbound Network Endpoints

Three explicit operations in Clickra can initiate outbound network connections:
1. **PDF Translation (`translate-pdf`)**: Requests translated text snippets from public translation APIs (`translate.googleapis.com` / `api.mymemory.translated.net`).
2. **On-Demand LibreOffice Download (Settings)**: If the user explicitly opts to install the fallback Office engine, Clickra downloads the verified MSI installer from The Document Foundation (`https://download.documentfoundation.org/`).
3. **Windows Encoder Extension Store Preflight (`img-to-webp`, `img-to-heic`)**: If the required Windows imaging extension is absent, Clickra can launch the official Microsoft Store URI (`ms-windows-store://pdp/?ProductId=...`) so the user may install the codec. The Store request is processed directly by the Windows Store app.

## Execution paths

| Entry | Current path |
|---|---|
| Packaged Start menu entry | `ClickraLauncher.exe` -> try Fluent optional-package activation -> fall back to `Clickra.exe` NativeAOT dashboard |
| Explorer command | `ClickraShell` -> `ClickraLauncher.exe` with the original verb/files -> Fluent activation when available, otherwise `Clickra.exe` |
| Command line / quiet mode | `Clickra.exe` -> `Clickra.Core` |
| Unpacked shell fallback | direct `Clickra.Fluent.exe` when present, otherwise `Clickra.exe` |

The Fluent right-click path currently includes localized status text, Office
engine preflight, cancellation, history recording, output-folder actions, and
PDF password input through a WinUI `ContentDialog`.

## Intentional constraints

| Behavior | Reason |
|---|---|
| AOT Dashboard ships as fallback | Main MSIX contains ClickraLauncher (router) + Clickra.exe (AOT). Fluent is an optional Store package installed on demand. |
| Fluent is framework-dependent | MSIX declares the Windows App Runtime dependency instead of bundling the complete runtime into every package. |
| Explorer must be restarted after reinstalling the same version | Explorer caches the loaded COM DLL and menu state. This is Windows shell behavior. |
| Packaging aligns only the copied layout manifest | Builds remain deterministic without silently rewriting tracked source files. |
| Automatic runtime mapping requires Windows App SDK 2.x or newer | 2.x uses aligned SemVer; the older 1.x date-based runtime minimum cannot be derived safely. |
| The package is larger than the old NativeAOT-only build | It now contains three binaries plus WinUI managed projections. A sudden unexplained increase is still a packaging warning. |
| WinUI is never hosted inside Explorer | A UI/runtime failure must not destabilize `explorer.exe`. |

## Historical gap snapshot (2026-07-31; non-normative)

The following bullets record the state observed on 2026-07-31. They are retained as
historical engineering evidence and must not be used as a live release/test checklist;
current CI and packaging state must be checked from the repository and GitHub runs.

- The Windows App SDK 2.3.1 MSIX builds and signs successfully, but still needs
  installed-package smoke testing for dashboard launch and real right-click
  conversion after the upgrade.
- Windows 10 requires explicit testing on a supported machine, including a
  clean machine without a preinstalled Windows App Runtime.
- Running, completed, failed, cancelled, PDF-password, and Office-engine paths
  need one final packaged test matrix under 2.3.1.
- Packaging is currently x64 only; ARM64 is not built or verified.
- Package creation is automated, but packaged-app startup and shell activation
  are not yet CI smoke tests.
- The package excludes large unused native Windows ML payloads, but remaining
  unused managed projections can be audited later if package size becomes a
  release problem.

## Packaging state and authority

The **current automated release artifact** is the NativeAOT Main MSIX produced by
`scripts/build_msix.ps1` and `.github/workflows/release.yml`. Its tracked manifest is
`packaging/msix/AppxManifest.xml`, whose Start menu entry is `ClickraLauncher.exe`.
The package layout contains:

- `ClickraLauncher.exe` — NativeAOT routing entry point.
- `Clickra.exe` — NativeAOT CLI and dashboard fallback.
- `ClickraShell.dll` — NativeAOT Explorer command provider.
- package resources, assets, and required native codec/runtime files.

The current `release.yml` does **not** build or upload `Clickra.Fluent.exe` as part of
`Clickra.msix`, and it does not publish a second Fluent MSIX. The tracked
`packaging/msix/AppxManifest.Fluent.xml` describes the proposed related-set optional
package shape, but that distribution path remains gated by
`development/store_optional_fluent_plan.md` until the required Store/Partner Center
conditions are explicitly cleared.

This separation matters when reading the source tree: `Clickra.Fluent` is a real maintained
application and `ClickraLauncher` already contains optional-package activation/fallback logic,
but source availability is not proof that the Fluent optional package is part of the current
automated Store release.

For release-pipeline behavior, use `docs/CI_CD_DUAL_RELEASE_GUIDE.md` together with the actual
workflow/scripts. Do not change package identity or version metadata as part of ordinary
feature work; release/version changes stay in dedicated release-preparation work.
