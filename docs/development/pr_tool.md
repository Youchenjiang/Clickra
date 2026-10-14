# Clickra PR Tool (local workflow)

Use the high-level `scripts/pr_tool.py` interface rather than manually assembling
`gh`, GraphQL and pre-push commands. From the Clickra repository worktree:

```powershell
python scripts/pr_tool.py status 177
python scripts/pr_tool.py review 177
python scripts/pr_tool.py audit 177
python scripts/pr_tool.py audit 177 --json
python scripts/pr_tool.py sonar 177
python scripts/pr_tool.py sonar 177 --json
python scripts/pr_tool.py audit 177 --sonar --json
python scripts/pr_tool.py ready --body-file 'C:\path\to\pr-body.md'
```

`status` reports PR base/head, state, review decision, check rollup and all
unresolved review threads. `review` includes thread ID, source location,
first comment text and link. When a number is omitted the tool finds the PR
associated with the current branch. `audit` additionally reads all pages of
inline review comments, general PR comments and submitted review summaries;
`--json` includes the complete bodies and thread states. No review is resolved
by an audit. Read-only queries require GitHub CLI login.
Thread pagination supports PRs with more than 100 review threads. Provider
checks are shown when they appear in the GitHub check rollup; missing checks
must not be interpreted as passing.

`sonar` reads the SonarQube Cloud `api/issues/search` endpoint for the
selected PR, requesting `OPEN,CONFIRMED` issues and paging through all results.
It shows issue ID, file/line, rule, impact/severity, and message; `--json`
returns structured data. `audit --sonar` adds the inventory to the GitHub
audit (`sonar` property in JSON). A SonarQube Quality Gate passing does not
mean there are no open issues. The default project key is the GitHub repository
name with `/` replaced by `_`; use `SONAR_PROJECT_KEY` to override it.
Set `SONAR_TOKEN` for private projects and `SONAR_HOST_URL` for the US
instance (`https://sonarqube.us`); token is sent only as an Authorization
header and never displayed. Failed API requests stop the operation rather
than returning a misleading partial list. None of these commands modifies
SonarQube findings.

`ready` runs the exact versioned `scripts/hooks/pre-push` gate locally, without
pushing anything. Supply the finalized PR body file; the gate requires a fresh
`origin/main`, clean worktree, compatible commits, builds, tests, NativeAOT,
and the applicable fixture regressions. Private PDF fixtures are supplementary
for changes unrelated to PDF translation; PDF-translation changes require the
relevant fixture gate. `ready` can fail when required fixtures or tooling are
missing; do not bypass a failure or report a skipped test as passed.

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
# For a fixed outdated thread, also supply independently checked evidence:
python scripts/pr_tool.py resolve 177 --thread PRRT_xxx --execute --authorize '177:PRRT_xxx' --repair-evidence 'commit and regression test reference'
```

Without `--execute`, `resolve` checks eligibility read-only and never mutates
GitHub. The authorization string is only an exact-match operator confirmation, **not
proof of user approval**. Never issue it based on an earlier PR's authorization.
The command only runs `resolveReviewThread` through the lower-level guard;
never create, reply to, edit, repost or delete PR comments. Review findings
must be repaired and confirmed before resolution. GitHub access outside the
tool can bypass this cooperative guard; this is not credential isolation.
