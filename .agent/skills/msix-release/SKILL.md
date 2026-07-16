---
description: "Version bump + MSIX build + manual post-fixes for Clickra releases. Covers bump_version.ps1 known issues, CHANGELOG/README fixups, and MSIX signing."
---

# MSIX Release Pipeline

Bump version, build MSIX package, and fix known script issues for Clickra releases.

## Arguments

`$ARGUMENTS` — Version bump type: `patch`, `minor`, or `major`. Default: `patch`.

## Version Convention

- **patch** = fixes, layout improvements, CI/store resubmission repairs, and other changes to existing modules
- **minor** = new features
- **major** = breaking changes

## Procedure

All commands run from the Clickra project root: `C:\Users\g1014308\Documents\GitHub\Youchen\Clickra`

### Step 1: Version bump

```powershell
powershell -File scripts/bump_version.ps1 -Build -Type patch
```

This updates:
- `src/Directory.Build.props` (version number)
- `packaging/msix/AppxManifest.xml` (2 locations)
- `CHANGELOG.md`
- `README.md`
- `README.zh-TW.md`
- `docs/ROADMAP.md` (milestone/version status, when applicable)

### Step 2: Fix CHANGELOG.md (REQUIRED)

`bump_version.ps1` CHANGELOG insertion via regex produces **malformed duplicate entries and misplaced headers**. Agent MUST manually rewrite the CHANGELOG after running the script:

1. Read the current CHANGELOG.md
2. Remove any duplicate version headers
3. Ensure the new version entry is at the top under the correct date
4. Verify no content from older entries was duplicated or displaced

### Step 3: Fix README version tables (REQUIRED)

The script inserts `"TODO: Add milestone description here."` placeholder instead of actual content. Agent MUST:

1. Read README.md and README.zh-TW.md
2. Replace the placeholder with an actual description of the changes
3. Ensure version table is consistent between English and Chinese READMEs

### Step 4: Build MSIX

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build_msix.ps1
```

Pipeline: NativeAOT publish of Clickra.CLI + ClickraShell → Layout assembly → makepri.exe (PRI index) → makeappx.exe → signtool.exe sign.

Depends on Windows SDK tools in `C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\`.

Output: `Clickra.msix` in the project root.

### Step 5: Verify build

```powershell
dotnet build src/Clickra.CLI/Clickra.csproj -c Release 2>&1
```

Ensure 0 errors, 0 warnings.

## Known Issues

- **bump_version.ps1 CHANGELOG corruption**: Regex insertion produces malformed entries. Always manually fix after running.
- **bump_version.ps1 README placeholder**: Inserts `"TODO: Add milestone description here."` — must replace with actual content.
- **MSIX install error 0x8007007E**: If user has a previous MSIX install with a different signing certificate, the new install fails with "找不到指定的模組". Fix: uninstall old version first. Store auto-update users are unaffected.
- **Branch naming**: Use `feature/*` or `hotfix/*` prefix with descriptive name. Do NOT include version numbers in branch names (avoid confusion with Git Tags).

## Tag and Release Order

Per `.agent/guidelines.md` §1:

1. Complete development on feature/hotfix branch
2. Push branch, create PR, merge into `main`
3. Switch to local `main`: `git checkout main && git pull`
4. Create Git Tag on merged main: `vX.Y.Z.0`
5. Never push directly to `main` or `release` branches
6. Push tag: `git push origin vX.Y.Z.0`

## Reference

- Version bump issues: `LOCAL_BUILD_NOTES.md` → Automated Packaging & Versioning Scripts
- MSIX build pipeline: `LOCAL_BUILD_NOTES.md` → Automated Packaging & Versioning Scripts
- Tag/release convention: `docs/development/release_guideline.md`
