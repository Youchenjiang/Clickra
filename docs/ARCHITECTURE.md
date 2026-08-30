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

## Execution paths

| Entry | Current path |
|---|---|
| Start menu or zero arguments | `Clickra.Fluent` -> `MainPage` |
| Explorer command | `ClickraShell` -> packaged activation -> `TaskProgressPage` -> `Clickra.Core` |
| Command line / quiet mode | `Clickra.exe` -> `Clickra.Core` |
| Packaged activation unavailable | direct `Clickra.Fluent.exe`, then legacy `Clickra.exe` |

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

## Known gaps (2026-07-31)

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

## Intended end state

ClickraLauncher is the NativeAOT entry point that routes to Fluent or
AOT Dashboard via IApplicationActivationManager COM activation. The
optional Fluent MSIX (WinUI 3) is distributed through Store on demand.
The AOT Dashboard (Clickra.exe) in Clickra.CLI ships inside the Main
MSIX as the fallback when Fluent is not installed. See
[docs/development/dual_track_guide.md](docs/development/dual_track_guide.md).

`ClickraShell` remains a small NativeAOT command provider. `Clickra.Fluent`
remains the flagship interactive dashboard and progress UI, shipped as the
framework-dependent `Clickra.msix`. The legacy Win32 dashboard/progress code in
user-selectable theme).

## Packaging Notes

Two MSIX tracks share the `g1014308.Clickra` identity (only one is installed at
a time; switching replaces the other):

- `Clickra.msix` (Fluent, framework-dependent, Store + GitHub) — built by
  `scripts/build_msix.ps1`, app entry `Clickra.Fluent.exe`.
  app entry `Clickra.exe`.
  App Runtime and installs the matching track.

MSIX packaging must include:

- `Clickra.Fluent.exe` as the Start menu GUI entry.
- `Clickra.exe` for CLI and legacy fallback execution.
- `ClickraShell.dll` for context-menu registration.
- The Fluent publish output, including its SDK-generated `resources.pri`.
- App assets and manifest files whose Windows App Runtime dependency matches
  the Windows App SDK package used by `Clickra.Fluent`.

Use `scripts/build_msix.ps1` as the packaging entry point. It cleans stale
Fluent Release output and derives the copied layout manifest's Windows App
Runtime dependency from the SDK version in `Clickra.Fluent.csproj`.

Do not change package identity or version metadata as part of feature work. Keep release/version commits separate.
