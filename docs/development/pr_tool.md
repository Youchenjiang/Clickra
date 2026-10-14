# Clickra PR Tool (local workflow)

Use the high-level `scripts/pr_tool.py` interface rather than manually assembling
`gh`, GraphQL and pre-push commands. From the Clickra repository worktree:

```powershell
python scripts/pr_tool.py status 177
python scripts/pr_tool.py review 177
python scripts/pr_tool.py ready --body-file 'C:\path\to\pr-body.md'
```

`status` reports PR base/head, state, review decision, check rollup and all
unresolved review threads. `review` includes thread ID, source location,
first comment text and link. When a number is omitted the tool finds the PR
associated with the current branch. Read-only queries require GitHub CLI login.
Thread pagination supports PRs with more than 100 review threads. Provider
checks are shown when they appear in the GitHub check rollup; missing checks
must not be interpreted as passing.

`ready` runs the exact versioned `scripts/hooks/pre-push` gate locally, without
pushing anything. Supply the finalized PR body file; the gate requires a fresh
`origin/main`, clean worktree, compatible commits, builds, tests, NativeAOT,
and complete local PDF test fixtures. `ready` can fail when fixtures or tooling
are missing; do not bypass a failure or report a skipped test as passed.

`publish` intentionally refuses to run. Pushing to a remote and creating a PR
are separate GitHub writes, requiring explicit user approval for each. A local
successful `ready` does not authorize either operation. Always check remote
main freshness immediately before either write.

For a specific review thread **already fixed and explicitly authorized for
resolution in the current request**, use:

```powershell
python scripts/pr_tool.py resolve 177 --thread PRRT_xxx
# Only after explicit authorization for this exact thread and a verified fix:
python scripts/pr_tool.py resolve 177 --thread PRRT_xxx --execute --authorize '177:PRRT_xxx'
```

Without `--execute`, `resolve` checks eligibility read-only and never mutates
GitHub. The authorization string is only an exact-match operator confirmation, **not
proof of user approval**. Never issue it based on an earlier PR's authorization.
The command only runs `resolveReviewThread` through the lower-level guard;
never create, reply to, edit, repost or delete PR comments. Review findings
must be repaired and confirmed before resolution. GitHub access outside the
tool can bypass this cooperative guard; this is not credential isolation.
