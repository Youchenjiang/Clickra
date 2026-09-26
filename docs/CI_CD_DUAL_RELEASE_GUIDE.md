# Clickra CI/CD Release Pipeline Guide

> **Document role**: this file describes the release pipeline that is implemented by
> `.github/workflows/release.yml` and `scripts/build_msix.ps1`. It is not the authority
> for deciding *when* a release may start, which version to choose, or whether a Store
> publication gate has cleared.

## 1. Current automated release artifact

The current release workflow builds one shipping artifact:

| Artifact | Contents | Release targets |
|---|---|---|
| `Clickra.msix` | NativeAOT `ClickraLauncher.exe`, `Clickra.exe`, `ClickraShell.dll`, resources, and package assets | GitHub Release + Microsoft Store |

`scripts/build_msix.ps1` publishes the CLI, Shell, and Launcher as NativeAOT binaries and
assembles them with `packaging/msix/AppxManifest.xml`. The current workflow does **not**
publish `Clickra.Fluent.exe`, a portable ZIP, or a Fluent optional package.

`packaging/msix/AppxManifest.Fluent.xml` describes the proposed Fluent optional-package
shape, but that package is not built or uploaded by the current `release.yml` pipeline.
The optional-package migration remains governed by
`docs/development/store_optional_fluent_plan.md` and must not be described as an active
Store release path until its gates are explicitly cleared.

## 2. Trigger behavior

`release.yml` supports two trigger modes with intentionally different effects:

1. **Tag push (`v*`)** — real release.
   - Builds and validates `Clickra.msix`.
   - Uploads the MSIX as a workflow artifact.
   - Creates the GitHub Release.
   - Publishes the same MSIX to Microsoft Store through `scripts/publish_store.py`.
2. **Manual `workflow_dispatch`** — package validation only.
   - Builds and uploads the MSIX workflow artifact.
   - Does **not** create a GitHub Release.
   - Does **not** submit anything to Microsoft Store.

The workflow's `Detect Release Trigger` step is the single implementation switch for this
behavior. Do not infer a Store submission from a successful manual dispatch run.

## 3. GitHub Release notes

For a tag-triggered release, the workflow looks up the PR associated with the tagged merge
commit and uses that PR body as the GitHub Release notes. Under the project PR-writing
rules, this should be the dedicated **release-preparation PR**, not an arbitrary content PR.

Ordinary content PRs remain concise and reviewer-facing. The release-preparation PR should
summarize the release in public-facing language because its body is the input consumed by
the release workflow.

## 4. Microsoft Store submission

The `store-publish` job runs only for a tag-triggered release. It:

1. Downloads the already-built `Clickra.msix` artifact.
2. Creates `scripts/local_store_config.json` from repository secrets.
3. Runs `python scripts/publish_store.py`.

A successful workflow means the submission step completed; it does **not** prove that the
Store later reached `Published`. Publication/certification state must be verified separately
before any later release boundary that requires a `Published + no pending submission` gate.

## 5. Source-of-truth boundaries

- **Pipeline implementation**: `.github/workflows/release.yml`, `scripts/build_msix.ps1`.
- **Version format and synchronized version surfaces**: `docs/development/release_guideline.md`.
- **Optional Fluent package proposal/gates**: `docs/development/store_optional_fluent_plan.md`.
- **Historical release observations** are evidence only; they do not replace a fresh check of
  the current GitHub workflow, remote repository, or Partner Center state.
