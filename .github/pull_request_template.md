<!--
PR descriptions are separate from commit bodies. Keep the entire description in English.
Keep ordinary PR descriptions concise and reviewer-facing. The default structure is
Summary + Changes + Validation, with Scope only when a boundary or exclusion matters.
Do not turn the PR body into an internal audit log: provenance refs, commit-hash ledgers,
ancestry math, exhaustive per-job CI output, and publication-policy narration belong in
internal rollout evidence instead.

Do not publish replies, edits, reposts, or deletions of review comments. Verify the fix, then resolve only with explicit authorization. Rebase/rebuild each next PR on freshly fetched origin/main and pass local push checks before publication.

GitHub assignees, labels and milestones are checked from actual PR metadata,
not self-reported checkboxes. The provider decision remains a required
acknowledgement, because tooling cannot infer prior user authorization.

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

## Review Checklist
<!-- Check only what you actually reviewed. Explain any relevant exception or N/A in Validation or Scope.
     These are reviewer prompts, not substitutes for test results or evidence. -->
- [ ] **Behavior**: reviewed intentional behavior changes and unintended side effects.
- [ ] **Regression**: assessed affected paths and recorded relevant test evidence above.
- [ ] **Compatibility**: reviewed compatibility and migration impact, if applicable.
- [ ] **Security**: assessed security implications, if applicable.
- [ ] **Documentation**: updated relevant docs or confirmed no change is required.
- [ ] **External provider**: no new named provider is introduced, or any new provider was explicitly selected by the user before implementation (required acknowledgement).
