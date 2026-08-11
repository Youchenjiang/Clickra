<!--
PR descriptions are separate from commit bodies. Keep the entire description in English.
Use the structure that matches the number of changed files:
  - Small (<10 files): Summary (1-2 sentences) + numbered list.
  - Medium (10-50 files): Summary + Key Changes + Verification.
  - Large (50+ files): Overview + Key Changes (numbered sections) + Verification.
The medium structure below is the default. Remove unused sections and replace every placeholder.

PR metadata is validated by the Repository Policy workflow (assignee, labels, milestone)
and listed in the checklist below. The PR description also becomes the GitHub Release
notes for the merged version, so write it as public-facing copy.

Title rule: use a plain descriptive title (type(scope): what changed), never internal
roadmap codes like "R1-3" -- those belong in the milestone only.
-->

## Summary
<!-- Write 1-2 sentences describing what changed and why. Do not list file names here. -->

## Key Changes
<!-- Group technical changes by area. Use bullets and include relevant files or APIs. -->
* Area
  * Describe the change.

## Verification
<!-- Use a checklist. Mark completed checks with [x] and incomplete checks with [ ]. -->
- [ ] Describe the build, test, or manual verification performed.
- [ ] Describe any verification that remains outstanding.

## Notes
<!-- Optional: record non-obvious decisions, limitations, or rollout considerations. -->

## PR Metadata
<!-- Checked by the Repository Policy workflow; fill these in before opening the PR. -->
- [ ] **Assignee**: self-assigned (the workflow auto-assigns the author if left empty).
- [ ] **Label**: added at least one matching the title scope (`cli` / `core` / `shell` / `msix` / `docs` / `ci` / `deps` / `store` / `agent`).
- [ ] **Milestone**: linked to the roadmap phase or target version (exempt for `release` / `hotfix` / `deps` / `dependencies` / `docs`-labeled PRs).
- [ ] **Development**: linked to the issue(s) this PR closes, if any.
