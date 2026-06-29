# LibreOffice Offline Engine Plan

## Goal

Add an optional offline conversion engine for users who do not have Microsoft Office installed. The first supported scope is Office-to-PDF conversion for Word, Excel, and PowerPoint files.

LibreOffice should be treated as a user-consented system installation when Clickra performs setup. The installed LibreOffice is usable outside Clickra and is also used by Clickra as a conversion engine.

## Product Decision

- Default behavior: use Microsoft Office when available.
- Fallback behavior: use LibreOffice when Microsoft Office is unavailable or conversion fails.
- User control: allow users to choose `Auto`, `Microsoft Office`, or `LibreOffice`.
- File associations: do not register LibreOffice as the default app for `.docx`, `.xlsx`, or `.pptx`.
- General document editing: installing LibreOffice makes the app available to the user outside Clickra.

## Recommended Distribution

Use the official LibreOffice Windows MSI as the recommended download because it:

- is published directly by The Document Foundation;
- can be installed through Windows Installer with explicit user consent and UAC;
- produces a normal `Program Files\LibreOffice\program\soffice.exe` installation;
- can be used by the user outside Clickra;
- avoids the PortableApps installer behavior that can show blocking UI or leave an unusable extracted tree.

Default engine path:

```text
C:\Program Files\LibreOffice
```

Current package metadata checked on 2026-06-29:

```text
Version: 26.2.4
Edition: Windows x86-64 MSI
Download page: https://download.documentfoundation.org/libreoffice/stable/26.2.4/win/x86_64/LibreOffice_26.2.4_Win_x86-64.msi.mirrorlist
Direct download: https://download.documentfoundation.org/libreoffice/stable/26.2.4/win/x86_64/LibreOffice_26.2.4_Win_x86-64.msi
Download size: 372,539,392 bytes
SHA256: 202f26cda071c5aa4996a5a28412fddceb3891dceb0366982c62650456c0730f
License: MPL-2.0
```

Use the publisher direct download URL rather than a mirror-specific redirect.
The UI must show the source page, package size, install path, and checksum
before any download starts. Clickra downloads to a unique temporary file,
verifies SHA256, launches `msiexec` with explicit user consent, resolves the
system `soffice.exe`, and saves the path only after health validation.

PortableApps `.paf.exe` is not acceptable for automated setup in Clickra. Local
testing showed that common silent installer arguments can still show installer
UI, and a copied extracted tree can fail native LibreOffice startup checks.

## Installer Flow

1. Detect Microsoft Office and existing LibreOffice.
2. If no usable engine is found, show an "Enable offline conversion engine" card in Dashboard.
3. Explain source, size, install path, and license.
4. Ask for explicit user consent before downloading.
5. Download the official LibreOffice MSI to a unique temporary file.
6. Verify SHA256 before installing.
7. Run `msiexec` with Windows elevation when required.
8. Resolve and save `LibreOfficePath` from the system installation.
9. Run a smoke conversion with a generated tiny document.
10. Mark the engine as ready only after the smoke test passes.

## Settings

Add these settings to `settings.conf`:

```text
OfficeEngine=auto
LibreOfficePath=
```

Allowed `OfficeEngine` values:

- `auto`: Microsoft Office first, LibreOffice fallback.
- `microsoft`: only use Microsoft Office.
- `libreoffice`: only use LibreOffice.

## Implementation Phases

### Phase 1: Engine Detection and Manual Path

- Detect `soffice.exe` from configured path, environment variable, Program Files, and PATH.
- Add Core fallback conversion path using `soffice.exe --headless --convert-to pdf`.
- Add Dashboard status row for LibreOffice availability.
- Add setting UI for Office engine preference and manual `soffice.exe` path.

### Phase 2: Managed Engine Installer

- Add a managed download/install service.
- Add consent dialog and progress UI.
- Add SHA256 verification.
- Install the official MSI after explicit user consent.
- Add smoke test.

### Phase 3: Quality Matrix

Test the engine against:

- `.doc`, `.docx`
- `.xls`, `.xlsx`
- `.ppt`, `.pptx`
- Traditional Chinese filenames
- filenames with spaces
- image-heavy documents
- tables and merged cells
- multi-sheet Excel workbooks
- password-protected or corrupted files

### Phase 4: Maintenance

- Add engine version display.
- Add "repair engine" action.
- Add "remove engine" action.
- Add update check only after the first installer flow is stable.

## Risks

- MSI installation requires Windows installation permission and may show UAC.
- LibreOffice layout fidelity may differ from Microsoft Office.
- Download URLs and checksums change with releases.
- Microsoft Store policies may restrict silent third-party downloads if consent and attribution are unclear.
- Large downloads need robust cancellation and partial-file cleanup.

## Open Questions

- Should Clickra expose install path customization after the default managed installer is stable?
- Should Clickra add cancel/resume support for the large installer download?
