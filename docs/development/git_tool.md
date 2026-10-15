# Clickra Git Workflow Foundation

`scripts/git_tool.py` is a local workflow companion to `scripts/pr_tool.py`.
It does not create, merge, delete, or publish PRs or branches. Run it from an
existing Clickra worktree with the latest `origin/main` fetched first.

```powershell
git fetch origin main
python scripts/git_tool.py status
python scripts/git_tool.py sync
python scripts/git_tool.py rebase
```

`sync` previews a fast-forward of the local `main` ref to `origin/main`.
It only works from another worktree while `main` itself is not checked out,
and refuses divergent history. `rebase` previews rebasing the **current**
unpublished branch onto `origin/main`. It refuses dirty worktrees, detached
HEAD, stale remote tracking refs, shallow histories, and branches already
published to `origin`. Neither operation runs by default.

To execute, copy the exact `authorize` value in the preview and pass it with
`--execute --authorize <value>`. Approval is bound to the branch and commit
IDs; the tool rechecks the repository state immediately before mutation.
No automatic stash, force push, reset, worktree switch, or cleanup is performed.
If a rebase conflicts, Git retains the rebase state for explicit recovery
(`git rebase --abort` or manual resolution). Always run relevant tests and
`pr_tool.py` preflight before any PR publication.

Post-merge PR verification and safe worktree cleanup are planned for a separate
follow-up PR, not this foundation.

## Post-merge lifecycle

Run `cleanup` from a **different clean worktree** after the operator merges the PR
and deletes its remote branch:

```powershell
git fetch origin main --prune
python scripts/git_tool.py cleanup --pr 185
# After reviewing the preview:
python scripts/git_tool.py cleanup --pr 185 --execute --authorize '<preview token>'
```

`cleanup` requires GitHub MERGED status, the merge commit and original PR
head reachable from the latest `origin/main`, the deleted remote feature
branch, a matching local head, and a clean dedicated sibling worktree named
`Clickra-pr-*`. It refuses squash/rebase merges when the original head is
not in ancestry: those require a separately reviewed equivalence mechanism,
not a guess that MERGED means the local branch can be removed. It fast-forwards
the local main ref using compare-and-swap before removing the dedicated
worktree and deleting the local branch with `git branch -d`.
If an operation fails partway, inspect the resulting state and do not blindly
retry with a stale token.

Create a **new**, unpublished worktree from the latest remote main:

```powershell
python scripts/git_tool.py next --branch feature/example-task
python scripts/git_tool.py next --branch feature/example-task --execute --authorize '<preview token>'
```

`next` refuses existing local or remote branch names and existing target paths;
it never rebases a previously published branch. Both lifecycle actions are
preview-only by default; the token includes PR/branch, paths, repository origin,
current HEAD and main SHAs, and is revalidated before mutation. GitHub PR
merges and remote branch deletion remain operator actions.
