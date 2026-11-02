# Runtime & Native Resources

This directory contains runtime binaries, manifest files, and icons compiled or linked into Clickra projects.

## Contents

- `app.ico` / `app.png`: Main application icons linked virtually into `Clickra.Fluent` and `Clickra.CLI`.
- `menu-*.ico`: 20 context menu and dashboard icons (6-frame ladders: 16, 20, 24, 32, 48, 64px) loaded by `ClickraShell.dll` and `Clickra.CLI`.
- `AppxManifest.xml`, `*.manifest`: Native assembly and deployment manifests.

> **Warning**: Do NOT manually edit `menu-*.ico` files here. They are generated from vector sources in `assets/features/*.svg` via `scripts/generate_feature_icons.py`.
