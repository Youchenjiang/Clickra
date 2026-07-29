# Clickra Architecture

## Current Shape

Clickra ships three Windows-facing pieces:

- `Clickra.Fluent`: WinUI 3 dashboard. It references `Clickra.Core` directly and runs conversions in-process.
- `Clickra.CLI`: command-line and legacy dashboard entry. Explorer commands continue to route through this executable.
- `ClickraShell`: NativeAOT shell extension for the Windows context menu.

`Clickra.Core` owns conversion logic, storage, localization, Office engine helpers, PDF processing, and history/active-job records.

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

## Packaging Notes

MSIX packaging must include:

- `Clickra.Fluent.exe` as the Start menu GUI entry.
- `Clickra.exe` for CLI and Explorer command execution.
- `ClickraShell.dll` for context-menu registration.
- App assets and manifest files.

Do not change package identity or version metadata as part of feature work. Keep release/version commits separate.
