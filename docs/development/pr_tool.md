# Clickra PR Tool (local workflow)

Use the high-level `scripts/pr_tool.py` interface rather than manually assembling
`gh`, GraphQL and pre-push commands. From the Clickra repository worktree:

Branch names must describe the work, not their PR sequence. The shared
preflight, push preview/execution, PR creation, and CI policy reject suffixes
such as `-pr1`, `-pr2`, or `-pr10`, while retaining meaningful identifiers such
as `oauth2`, `issue-180`, `net8`, or `cve-2026-12345`.

```powershell
python scripts/pr_tool.py status 177
python scripts/pr_tool.py review 177
python scripts/pr_tool.py audit 177
python scripts/pr_tool.py audit 177 --json
python scripts/pr_tool.py sonar 177
python scripts/pr_tool.py diagnose 177
python scripts/pr_tool.py diagnose 177 --reports failed
python scripts/pr_tool.py diagnose 177 --reports all
python scripts/pr_tool.py sonar 177 --json
python scripts/pr_tool.py audit 177 --sonar --json
python scripts/pr_tool.py verify 177
python scripts/pr_tool.py body 177 --output 'C:\path\to\recovered-pr-body.md'
python scripts/pr_tool.py ready --body-file 'C:\path\to\pr-body.md'
python scripts/pr_tool.py prepare --body-file 'C:\path\to\pr-body.md'
python scripts/pr_tool.py prepare --initial --body-file 'C:\path\to\pr-body.md'
python scripts/pr_tool.py push --body-file 'C:\path\to\pr-body.md'
python scripts/pr_tool.py create --title 'feat(agent): improve PR workflow' --body-file 'C:\path\to\pr-body.md' --label agent --milestone 'Plan'
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

`verify` is the **single read-only final PR acceptance check**. It confirms
the current PR HEAD matches the exact remote branch SHA, inventories all
GitHub review threads and SonarQube open/confirmed issues, checks the CI rollup,
and reports skipped checks distinctly. It exits nonzero for unresolved issues,
failed or pending checks, missing required provider jobs, a non-main target,
or a non-open PR. SonarQube access errors fail closed. A `Skipping` provider
is never claimed as having independently reviewed the code.

The `verify` JSON `checks` array includes each check's current status,
whether it is required, and its GitHub/provider details URL. This replaces
routine `gh pr checks` calls; use `diagnose` for failing check details. Native
read-only `gh` is an exception only for a specific missing diagnostic capability;
document that gap and the reason before invoking it. These are cooperative
agent instructions, not OS-enforced command interception or credential isolation.

For failing DeepSource checks, `diagnose` reads actionable inline review
comments from GitHub and displays their findings without another API token.
An unresolved outdated thread is still reported separately, not auto-resolved.
`diagnose --reports failed` (default) fetches reports only for failing supported
providers; `--reports all` fetches available reports for passing providers too.
The read-only HTML fetcher permits only HTTPS links to known provider domains,
limits response size, and reports inaccessible or client-rendered content as
incomplete rather than treating it as a passing analysis.

If a temporary PR body file was lost after creation, use the read-only `body`
command to export the existing PR description into a **new** UTF-8 file and
pass that path to `push`. The export refuses to overwrite any existing file
and verifies the returned PR number and nonempty body; this avoids falling
back to direct `gh pr view` merely to recover publication metadata.

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

For actual publication, use `push` instead of manually constructing a Git
command. By default `push` previews the exact branch and commit SHA without
writing. Only after explicit user approval for that specific branch and SHA:

```powershell
python scripts/pr_tool.py push --body-file 'C:\path\to\pr-body.md' --execute --authorize 'feature/my-branch:0123456789012345678901234567890123456789'
```

The push tool refuses protected branches, dirty worktrees, unexpected remotes,
or mismatched authorization. It pushes only the checked branch, without force,
and runs the **existing full versioned Git pre-push hook**; it checks the remote
SHA after success. Never use `--no-verify`. A preview string is not itself
approval: the agent needs current explicit user permission for a push.

For the **first push of a new remote branch**, the hook recognizes the absent
remote ref and validates completed draft PR metadata instead of querying a PR
that cannot exist yet. Normal ancestry, fresh remote main, commit policy,
clean-tree and full build gates remain mandatory. Subsequent pushes require
live GitHub PR metadata. `prepare --initial` is only a lightweight draft-body
preview; it does not replace the full initial-push verification.

The same rule also applies to a branch **already pushed remotely but still
without a PR**. Every pre-push checks GitHub's all-state PR list for the
exact repository and branch. Zero PRs permits full draft metadata validation;
one open PR requires live metadata; closed, duplicate, unparseable or
unavailable PR lookup fails closed. Existing mandatory build and NativeAOT
gates are unaffected. The draft path is not an exception for an existing PR.

PR creation is a **second, independent external write** and is never implied
by approval to push. Use `create` without `--execute` to preview the exact
repository, remote branch commit, body/title/options fingerprint, and ensure
there is no existing PR for the branch (including closed PRs). After a separate
explicit user authorization, run the same command with `--execute --authorize`
set to the exact preview value. It creates only one PR targeting `main`,
with explicit label and optional milestone/assignee, and verifies the PR HEAD
from GitHub afterward. Lost or ambiguous responses are **not** retried
automatically; inspect remote state first. It cannot comment, merge, or
resolve review threads. A fresh `verify` is required after CI providers run.

The `create` preview validates the selected label, milestone, and optional
assignee against live GitHub metadata, including pagination. An unknown
milestone fails **before authorization or creation**, rather than failing
inside `gh pr create`. If the operation fails after being sent, the tool
distinguishes rejected label/milestone from an unknown outcome, with no
automatic retry and no token, stderr, or remote payload disclosure. Inspect
the remote PR list before issuing a fresh, independently authorized attempt.
An upstream execution-environment safety denial is separate from a repository
tool failure; do not bypass it using an alternate command.

**Avoid duplicate full gates:** when an approved push is the next action, use
the lightweight `prepare` command followed by `push`, and let the mandatory
hook perform the full build, tests
and NativeAOT once. Use `ready` only for independently requested local
verification without a push. This avoids the former `ready`-then-`push`
double run without weakening mandatory checks or caching potentially stale
results. After push, use `verify` repeatedly until providers finish; resolve
threads only after independently verified fixes. Never interpret `verify` as
authorization to merge or create a PR.

`publish` (legacy) intentionally refuses to run. Creating a PR, pushing to a
remote, and merging remain separate GitHub writes requiring specific approval.
The new `push` command only handles an authorized branch push; it does not
create or merge a PR. Always check remote main freshness before publication.

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
