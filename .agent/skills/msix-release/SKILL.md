---
description: "Prepare an authorized Clickra release: synchronize version surfaces, update release copy, validate the NativeAOT Main MSIX, and stop at the release-preparation PR/tag gates."
---

# MSIX Release Preparation

Use this skill only for a dedicated **release-preparation** task after the active rollout has
reached a release boundary and any required Store/publication gate has cleared. Ordinary
content PRs do not bump versions.

## Preconditions

Before changing any version surface:

1. Confirm the user has explicitly authorized release preparation/version bump work.
2. Confirm the active rollout/release gate allows the next release preparation to start.
3. Fresh-fetch `origin/main` and create the release-preparation branch from that current
   remote main according to the canonical PR publication gate. Do not reuse an old
   release-preparation branch or replay its history.
4. Determine the intended `major` / `minor` / `patch` bump from the authorized release target.

Version format and synchronized surfaces are defined by
`docs/development/release_guideline.md`.

## Procedure

### Step 1: Synchronize version surfaces

Run from the repository root:

```powershell
powershell -File scripts/bump_version.ps1 -Type patch
```

Use the authorized bump type instead of `patch` when required. The script currently updates:

- `src/Directory.Build.props`
- `packaging/msix/AppxManifest.xml`
- `packaging/msix/AppxManifest.Fluent.xml`
- `src/resources/AppxManifest.xml`
- `CHANGELOG.md` with a new-version TODO entry
- all five `docs/StoreListing_*.md` version stamps

It preserves each target file's existing UTF-8 BOM state. It does **not** update README
version tables or invent release notes.

### Step 2: Finish release copy

1. Replace the new CHANGELOG TODO with the actual release summary.
2. Update `docs/ROADMAP.md` milestone state only where the release actually changes it.
3. Update `LOCAL_BUILD_NOTES.md` only if its architecture/version marker is genuinely affected.
4. Update the five Store listing content sections (Description / What's new / Product Features /
   Short description) as required; the bump script changes only their version stamps.

Keep version synchronization and substantive documentation updates atomic/reviewable according
to `.agent/guidelines.md`.

### Step 3: Validate

At minimum:

```powershell
dotnet build src/Clickra.Core/Clickra.Core.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/Clickra.CLI/Clickra.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/ClickraShell/ClickraShell.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build src/Clickra.Fluent/Clickra.Fluent.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet build tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet run --project tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build_msix.ps1
```

Run any additional NativeAOT/package/smoke gates required by the active release plan. The current
`build_msix.ps1` publishes NativeAOT CLI + Shell + Launcher and produces the Main `Clickra.msix`.

### Step 4: Publish the release-preparation PR

Before push/open PR, re-run the canonical latest-`origin/main` race gate. If main advanced,
rebuild/revalidate the branch on the new main first.

The release-preparation PR body is special: `.github/workflows/release.yml` uses the PR associated
with the tagged merge commit as GitHub Release notes. Keep the body concise, public-facing, and
focused on the release rather than internal provenance/CI ledgers.

Stop at the merge gate. The release-preparation PR must be merged before any release tag exists.

## Tag and release order

After the user merges the release-preparation PR:

1. Fresh-fetch and verify the resulting `main` commit and required post-merge checks.
2. Obtain explicit user authorization for the release/tag operation.
3. Create `vX.Y.Z.0` on the verified release-preparation merge result.
4. Push only the tag (`git push origin vX.Y.Z.0`); never push directly to `main`.
5. The tag-triggered `release.yml` creates the GitHub Release and submits the Main MSIX to Store.
6. A successful workflow submission is not proof of Store `Published`; later release boundaries
   that require `Published + no pending submission` must verify that state separately.

Manual `workflow_dispatch` is package validation only and must not be treated as a real release.

## Reference

- Version format/surfaces: `docs/development/release_guideline.md`
- Current automated release pipeline: `docs/CI_CD_DUAL_RELEASE_GUIDE.md`
- General agent/PR/tag rules: `.agent/guidelines.md`
- Actual implementation: `.github/workflows/release.yml`, `scripts/bump_version.ps1`,
  `scripts/build_msix.ps1`
