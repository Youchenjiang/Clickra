<!--
PR descriptions are separate from commit bodies. Keep the entire description in English.
Keep ordinary PR descriptions concise and reviewer-facing. The default structure is
Summary + Changes + Validation, with Scope only when a boundary or exclusion matters.
Do not turn the PR body into an internal audit log: provenance refs, commit-hash ledgers,
ancestry math, exhaustive per-job CI output, and publication-policy narration belong in
internal rollout evidence instead.

PR metadata is validated by the Repository Policy workflow (assignee, labels, milestone)
and listed in the checklist below.

Release-preparation PRs are the exception: the tag-triggered release workflow uses the
merged release-preparation PR body as GitHub Release notes. Keep that PR public-facing and
concise, and summarize the release rather than dumping internal rollout evidence.

Title rule: use a plain descriptive title (type(scope): what changed), never internal
roadmap codes like "R1-3" -- those belong in the milestone only.
-->

## Summary
<!-- Write 1-2 sentences describing what changed and why. Do not list file names here. -->

## Changes
<!-- Use short reviewer-relevant bullets. Describe behavior/contracts, not an internal commit ledger. -->
- Describe the change.

## Validation
<!-- Summarize meaningful build/test/manual validation. Do not enumerate every CI job unless it matters to review. -->
- Describe the build, test, or manual verification performed.

## Scope
<!-- Optional: include only when exclusions or ownership boundaries are important to review. -->

## PR Metadata
<!-- Checked by the Repository Policy workflow; fill these in before opening the PR. -->
- [ ] **Assignee**: self-assigned (the workflow auto-assigns the author if left empty).
- [ ] **Label**: added at least one matching the title scope (`cli` / `core` / `shell` / `msix` / `docs` / `ci` / `deps` / `store` / `agent`).
- [ ] **Milestone**: linked to the roadmap phase or target version (exempt for `release` / `hotfix` / `deps` / `dependencies` / `docs`-labeled PRs).
- [ ] **Development**: linked to the issue(s) this PR closes, if any.
