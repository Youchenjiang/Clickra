<div align="center">
<img src="src/resources/app.png" alt="Clickra Logo" width="80" height="80" />
<h1>Clickra</h1>
<p>High-performance context-menu utility suite for Windows 10 &amp; 11</p>
<p>
<a href="https://apps.microsoft.com/detail/9NGLBF6P1KLD"><img src="https://img.shields.io/badge/Microsoft%20Store-Clickra-blue?style=flat-square&logo=microsoft-store" alt="Microsoft Store" /></a>
<a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache%202.0-blue?style=flat-square" alt="License: Apache 2.0" /></a>
</p>
<p>
<b>English</b> | <a href="README.zh-TW.md">繁體中文</a>
</p>
</div>

**Clickra** is a high-performance context-menu utility suite for Windows 10 and Windows 11. It brings fast document conversion and PDF tools straight to File Explorer's right-click menu.

Most productivity scripts are written in Python and pay a 1–2 second "cold start" penalty on every invocation. Clickra uses a hybrid Windows architecture instead: a **NativeAOT** shell boundary for instant menu responses, a **WinUI 3 Fluent** dashboard and progress UI, and 100% local processing with no Python runtime.

## Core Features

### Modern Context Menu (Windows 10/11)
On Windows 11, all commands live in the `Clickra` sub-menu with localized icons. Windows 10 uses the compatible classic context-menu integration.

### Fluent Dashboard
A responsive WinUI 3 dashboard for conversion, settings, history, and diagnostics, with real-time detection of PDF and Office engine readiness.

### Office to PDF
Silently exports Word, Excel, and PowerPoint documents to high-quality PDFs in the background. Supports Auto, Microsoft Office, and LibreOffice engine modes — LibreOffice can be downloaded and managed from Clickra as a free local fallback.

### PDF Tools
- **Merge** — combine selected PDFs into one document in filename order.
- **Split** — visual PDF splitter with page previews (custom segments, split-each-page, fixed-page modes).
- **Compress** — high-fidelity native compression with a 4-stop quality slider.
- **Remove Password** — decrypt protected PDFs directly from the right-click menu.

### Images
- **Images to PDF** — package JPG, PNG, and WebP files into a multi-page PDF, pixel-perfect.
- **Stitch** — join multiple images vertically into a single long-form image.

### PDF Translation
Translates PDF content while preserving layout — heading hierarchy, typography, merged tables, fixed artwork, captions, and links — with CJK font scaling and re-joined technical identifiers.

### Conversion History
Locally tracks every conversion in a Fluent master-detail view with paths, operation types, timestamps, duration, and status.

## Installation

### Recommended: Microsoft Store (auto-updates)
[![Microsoft Store Badge](https://developer.microsoft.com/en-us/store/badges/images/English_get-it-from-MS.png)](https://apps.microsoft.com/detail/9NGLBF6P1KLD)

### Manual: GitHub Release
1. Download `Clickra.msix` from [Releases](../../releases).
2. Double-click to install — no additional runtime dependencies on Windows 10+.
   - The Fluent UI add-on is available from the AOT Dashboard Settings page.

## Local Development

### Requirements
- Windows 10 or 11
- .NET SDK 10
- Visual Studio with the C++ workload (for NativeAOT linking)

### Build
```bash
git clone https://github.com/Youchenjiang/Clickra.git
cd Clickra
powershell -File scripts/build_msix.ps1
```

### Test
```bash
dotnet run --project tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj -c Release
```

Full setup, packaging, and Git workflow details: [LOCAL_BUILD_NOTES.md](LOCAL_BUILD_NOTES.md)

## Contributing
Before opening a pull request, please see:
- [LOCAL_BUILD_NOTES.md](LOCAL_BUILD_NOTES.md) — build, packaging, and Git workflow
- [docs/development/release_guideline.md](docs/development/release_guideline.md) — versioning and release checklists
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — NativeAOT/Fluent architecture
- [docs/ROADMAP.md](docs/ROADMAP.md) — product roadmap and milestones
- [CHANGELOG.md](CHANGELOG.md) — full version history

## License
Licensed under the **Apache License 2.0**. Core components use **PDFsharp** (MIT License).