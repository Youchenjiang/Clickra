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
