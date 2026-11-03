# Native Application Manifests

This directory contains native assembly and deployment manifests for Clickra components:

- `AppxManifest.xml`: Sparse package identity manifest used by `--deploy` registration.
- `Clickra.exe.manifest`: Native application manifest for the CLI process.
- `ClickraShell.dll.manifest`: Native COM server registration manifest for the Windows 11 shell extension.

> **Note**: All icon and visual assets are located in `assets/` and compiled by `IconGen` into `obj/icons/`. No image binaries are stored in this directory.
