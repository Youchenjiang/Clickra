# Clickra Git Workflow Foundation

`scripts/git_tool.py` is a local workflow companion to `scripts/pr_tool.py`.
It manages local branches, fast-forward synchronization, rebases, and cleanups
with strict state-bound authorization and preview guarantees. It forbids dangerous
whole-file overwrites (`git checkout <ref> -- <file>`), force pushes, and unverified branch reuse.

```powershell
git fetch origin main
python scripts/git_tool.py status
python scripts/git_tool.py sync
python scripts/git_tool.py start --branch fix/my-fix
python scripts/git_tool.py rebase
python scripts/git_tool.py pick --commit <commit-sha>
```

## Local Branch Workflow

### 1. Synchronizing main (`sync`)
`sync` previews a fast-forward of the local `main` ref to `origin/main`.
When executed while on `main` with a clean working tree, it fast-forwards the current
HEAD to `origin/main`. When executed from another branch, it updates the `main` ref
via compare-and-swap without checking out `main`. It refuses divergent history or dirty trees.

### 2. Creating a new branch from main (`start`)
`start` safely creates and checks out a new branch based on the latest `origin/main`
in the current repository:

```powershell
python scripts/git_tool.py start --branch feat/example-feature
# Review preview output, then execute:
python scripts/git_tool.py start --branch feat/example-feature --execute --authorize '<preview token>'
```

`start` strictly verifies:
- Working tree is clean (no untracked or uncommitted changes).
- Branch name satisfies `scripts/branch_policy.py` (valid prefix, 2-3 words, suffix <= 25 characters).
- Branch does not exist locally or on `origin`.
- Base is verified against the latest `origin/main`.

### 3. Rebasing current branch (`rebase`)
`rebase` previews rebasing the **current** unpublished branch onto `origin/main`.
It refuses dirty worktrees, detached HEAD, stale remote tracking refs, shallow histories,
and branches already published to `origin`.

### 4. Safely picking commits (`pick`)
`pick` transfers a commit from a backup or feature branch onto the current branch
using Git's 3-way merge machinery:

```powershell
python scripts/git_tool.py pick --commit a1b2c3d4
# Review preview output, then execute:
python scripts/git_tool.py pick --commit a1b2c3d4 --execute --authorize '<preview token>'
```

`pick` enforces 3-way conflict detection. Developers and agents must NEVER bypass
merge tracking by running `git checkout <branch> -- <file>` or `git restore --source`,
as whole-file replacement silently destroys intervening changes from `main`.

---

To execute any mutating command (`sync`, `start`, `rebase`, `pick`, `cleanup`, `next`),
copy the exact `authorize` value in the preview and pass it with `--execute --authorize <value>`.
Approval is bound to the branch and commit IDs; the tool rechecks the repository state immediately before mutation.
No automatic stash, force push, reset, or unapproved mutation is performed.
If a rebase or pick conflicts, Git retains the merge state for explicit recovery (`git rebase --abort` or `git cherry-pick --abort`).
Always run relevant tests and `pr_tool.py` preflight before any PR publication.

## Post-merge lifecycle

Run `cleanup` from a **different clean worktree** after the operator merges the PR
and deletes its remote branch:

```powershell
git fetch origin main --prune
python scripts/git_tool.py cleanup --pr 185
# After reviewing the preview:
python scripts/git_tool.py cleanup --pr 185 --execute --authorize '<preview token>'
```

`cleanup` requires GitHub MERGED status, a merge commit reachable from the
latest `origin/main`, the deleted remote feature
branch, a matching local head, and a clean dedicated sibling worktree named
`Clickra-pr-*`. Ordinary merges require original-head ancestry. Squash merges
require exact introduced patch equivalence; rebase merges require every original
commit patch to be equivalent upstream. Ambiguous integration, missing changes,
or an unexpected commit structure fails closed. It fast-forwards
the local main ref using compare-and-swap before removing the dedicated
worktree. Ancestry-based cleanup uses `git branch -d`; verified patch-equivalent
cleanup uses a compare-and-swap ref deletion at the exact approved HEAD.
If an operation fails partway, inspect the resulting state and do not blindly
retry with a stale token.

Create a **new**, unpublished worktree from the latest remote main (for multi-worktree setups):

```powershell
python scripts/git_tool.py next --branch feature/example-task
python scripts/git_tool.py next --branch feature/example-task --execute --authorize '<preview token>'
```

`next` refuses existing local or remote branch names and existing target paths;
it never rebases a previously published branch. Both lifecycle actions are
preview-only by default; the token includes PR/branch, paths, repository origin,
current HEAD and main SHAs, and is revalidated before mutation. GitHub PR
merges and remote branch deletion remain operator actions.
