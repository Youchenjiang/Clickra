#!/usr/bin/env python3
"""Conservative local Git workflow helper with state-bound execution approval.

Mutations are limited to local main synchronization and the currently checked-out,
unpublished feature branch. No stash, reset, force push, or worktree switching.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
import shutil
import subprocess
import sys

MAIN_BRANCH_REF = "refs/heads/main"
BRANCH_PATTERN = re.compile(r"^(feature|fix|refactor|docs|test)/[a-z0-9][a-z0-9-]*$")


def git(*args, check=True):
    executable = shutil.which("git")
    if not executable:
        raise RuntimeError("git executable not found")
    proc = subprocess.run([str(Path(executable).resolve()), *args], capture_output=True, text=True,
                          timeout=30, check=False)
    if check and proc.returncode:
        raise RuntimeError(f"git {args[0]} failed: {proc.stderr.strip()}")
    return proc


def output(*args):
    return git(*args).stdout.strip()


def gh(*args):
    executable = shutil.which("gh")
    if not executable:
        raise RuntimeError("gh executable not found")
    result = subprocess.run([str(Path(executable).resolve()), *args], capture_output=True,
                            text=True, timeout=30, check=False)
    if result.returncode:
        raise RuntimeError("cannot inspect GitHub pull request")
    return json.loads(result.stdout)


def worktree_branches():
    records = []
    for block in output("worktree", "list", "--porcelain").split("\n\n"):
        entry = dict(line.split(" ", 1) for line in block.splitlines() if " " in line)
        if entry:
            records.append(entry)
    return records


def common_state():
    assert_clean()
    root = Path(output("rev-parse", "--show-toplevel")).resolve()
    main_root = Path(output("rev-parse", "--path-format=absolute", "--git-common-dir")).resolve()
    remote_url = output("remote", "get-url", "origin")
    remote_main = output("ls-remote", "--heads", "origin", "main").split()
    if len(remote_main) != 2 or remote_main[1] != MAIN_BRANCH_REF:
        raise ValueError("cannot verify origin/main")
    if output("rev-parse", "refs/remotes/origin/main") != remote_main[0]:
        raise ValueError("origin/main is stale; fetch first")
    local_main = output("rev-parse", MAIN_BRANCH_REF)
    if not ancestor(local_main, remote_main[0]):
        raise ValueError("local main cannot fast-forward")
    if any(w.get("branch") == MAIN_BRANCH_REF for w in worktree_branches()):
        raise ValueError("main is checked out; cannot update main ref safely")
    return {"root": str(root), "common_dir": str(main_root), "remote": remote_url,
            "head": output("rev-parse", "HEAD"),
            "branch": output("branch", "--show-current"),
            "main": local_main, "remote_main": remote_main[0]}


def lifecycle_snapshot(action, *, pr=None, branch=None):
    if action not in ("cleanup", "next"):
        raise ValueError("unsupported lifecycle action")
    state = dict(common_state(), action=action)
    if not state["branch"]:
        raise ValueError("detached HEAD unsupported")
    if action == "next":
        if not branch or not BRANCH_PATTERN.fullmatch(branch):
            raise ValueError("next requires a safe new feature branch name")
        if git("show-ref", "--verify", "--quiet", f"refs/heads/{branch}", check=False).returncode == 0:
            raise ValueError("branch already exists; refuse to overwrite")
        if output("ls-remote", "--heads", "origin", branch):
            raise ValueError("remote branch already exists")
        # Restrict new worktrees to predictable siblings of the main repository.
        target = Path(state["root"]).parent / ("Clickra-pr-" + branch.split("/", 1)[1])
        if target.exists() or any(Path(w["worktree"]).resolve() == target for w in worktree_branches()):
            raise ValueError("target worktree path already exists")
        state.update(target_branch=branch, target_path=str(target))
    else:
        if not isinstance(pr, int) or pr < 1:
            raise ValueError("cleanup requires a positive PR number")
        info = gh("pr", "view", str(pr), "--json",
                  "state,headRefName,headRefOid,mergeCommit,baseRefName")
        target_branch = info.get("headRefName")
        if (info.get("state") != "MERGED" or info.get("baseRefName") != "main"
                or not target_branch or not info.get("mergeCommit", {}).get("oid")):
            raise ValueError("PR merge evidence is incomplete")
        head = info.get("headRefOid")
        if not head or not ancestor(head, state["remote_main"]):
            raise ValueError("merged PR head not reachable from origin/main")
        if not ancestor(info["mergeCommit"]["oid"], state["remote_main"]):
            raise ValueError("merge commit not reachable from origin/main")
        if output("ls-remote", "--heads", "origin", target_branch):
            raise ValueError("remote PR branch still exists")
        if target_branch == state["branch"] or target_branch == "main":
            raise ValueError("cleanup must run from another worktree")
        target_ref = f"refs/heads/{target_branch}"
        if git("show-ref", "--verify", "--quiet", target_ref, check=False).returncode:
            raise ValueError("local PR branch not found")
        target_head = output("rev-parse", target_ref)
        if target_head != head or not ancestor(target_head, state["remote_main"]):
            raise ValueError("local branch differs from merged PR head")
        matches = [w for w in worktree_branches() if w.get("branch") == target_ref]
        if len(matches) != 1:
            raise ValueError("expected exactly one dedicated PR worktree")
        target = Path(matches[0]["worktree"]).resolve()
        if (target.parent != Path(state["root"]).parent or
                not target.name.startswith("Clickra-pr-")):
            raise ValueError("refuse to remove a non-dedicated worktree")
        if git("-C", str(target), "status", "--porcelain=v1").stdout.strip():
            raise ValueError("target worktree has uncommitted changes")
        state.update(pr=pr, target_branch=target_branch, target_path=str(target),
                     target_head=target_head, merge_commit=info["mergeCommit"]["oid"])
    return state


def lifecycle_run(action, *, pr=None, branch=None, execute=False, authorize=None):
    state = lifecycle_snapshot(action, pr=pr, branch=branch)
    token = authorization(state)
    if not execute:
        print(json.dumps({"preview": state, "authorize": token}, indent=2))
        return
    if token != authorize:
        raise ValueError("execution requires current preview authorization")
    if lifecycle_snapshot(action, pr=pr, branch=branch) != state:
        raise ValueError("repository changed since authorization")
    if state["main"] != state["remote_main"]:
        git("update-ref", MAIN_BRANCH_REF, state["remote_main"], state["main"])
    if action == "cleanup":
        git("worktree", "remove", state["target_path"])
        git("branch", "-d", state["target_branch"])
        git("worktree", "prune")
    else:
        git("worktree", "add", "-b", state["target_branch"],
            state["target_path"], state["remote_main"])
    print(json.dumps({"executed": action, "target_branch": state["target_branch"]}))


def assert_clean():
    if output("status", "--porcelain=v1"):
        raise ValueError("worktree contains tracked or untracked changes")
    if output("rev-parse", "--is-shallow-repository") != "false":
        raise ValueError("shallow history cannot be safely evaluated")


def ancestor(older, newer):
    proc = git("merge-base", "--is-ancestor", older, newer, check=False)
    if proc.returncode not in (0, 1):
        raise RuntimeError("cannot verify commit ancestry")
    return proc.returncode == 0


def check_action_branch(action, branch, local_base, remote_head, current_head):
    if branch == "main":
        raise ValueError("sync must run from a separate worktree" if action == "sync"
                         else "cannot rebase main")
    if action == "sync":
        if not ancestor(local_base, remote_head):
            raise ValueError("local main cannot fast-forward")
        worktrees = output("worktree", "list", "--porcelain")
        if f"branch {MAIN_BRANCH_REF}\n" in worktrees + "\n":
            raise ValueError("main is checked out in another worktree")
        return
    if output("ls-remote", "--heads", "origin", branch):
        raise ValueError("branch exists on origin; refuse to rewrite published history")
    if not ancestor(local_base, current_head):
        raise ValueError("branch does not descend from local main")


def snapshot(action):
    branch = output("branch", "--show-current")
    if not branch:
        raise ValueError("detached HEAD is unsupported")
    if not output("rev-parse", "--show-toplevel"):
        raise ValueError("repository root unavailable")
    assert_clean()
    local_base = output("rev-parse", MAIN_BRANCH_REF)
    remote_base = output("ls-remote", "--heads", "origin", "main").split()
    if len(remote_base) != 2 or remote_base[1] != MAIN_BRANCH_REF:
        raise ValueError("cannot verify remote main")
    if output("rev-parse", "refs/remotes/origin/main") != remote_base[0]:
        raise ValueError("origin/main is stale; fetch origin main first")
    current_head = output("rev-parse", "HEAD")
    check_action_branch(action, branch, local_base, remote_base[0], current_head)
    return {"action": action, "branch": branch, "head": current_head,
            "main": local_base, "remote_main": remote_base[0]}


def authorization(state):
    fingerprint = json.dumps(state, sort_keys=True, separators=(",", ":"))
    digest = hashlib.sha256(fingerprint.encode("utf-8")).hexdigest()[:20]
    return f"{state['action']}:{state['branch']}:{digest}"


def status():
    print(json.dumps({"branch": output("branch", "--show-current"),
                      "head": output("rev-parse", "HEAD"),
                      "main": output("rev-parse", MAIN_BRANCH_REF),
                      "origin_main": output("rev-parse", "refs/remotes/origin/main"),
                      "dirty": bool(output("status", "--porcelain=v1")),
                      "worktrees": output("worktree", "list").splitlines()}, indent=2))


def run(action, execute=False, authorize=None):
    state = snapshot(action)
    token = authorization(state)
    if not execute:
        print(json.dumps({"preview": state, "authorize": token}, indent=2))
        return
    if authorize != token:
        raise ValueError("execution requires current preview authorization")
    # Re-read immediately before mutation so an earlier preview cannot authorize
    # a changed branch or upstream SHA.
    if snapshot(action) != state:
        raise ValueError("repository changed since authorization")
    if action == "sync":
        if state["main"] != state["remote_main"]:
            git("update-ref", MAIN_BRANCH_REF, state["remote_main"], state["main"])
    else:
        if state["head"] != state["remote_main"]:
            result = git("rebase", "--no-autostash", "origin/main", check=False)
            if result.returncode:
                raise RuntimeError("rebase stopped; resolve or abort in this worktree: " +
                                   result.stderr.strip())
    print(json.dumps({"executed": action, "branch": state["branch"]}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    sub.add_parser("status")
    for name in ("sync", "rebase"):
        command = sub.add_parser(name)
        command.add_argument("--execute", action="store_true")
        command.add_argument("--authorize")
    for name in ("cleanup", "next"):
        command = sub.add_parser(name)
        command.add_argument("--pr", type=int)
        command.add_argument("--branch")
        command.add_argument("--execute", action="store_true")
        command.add_argument("--authorize")
    args = parser.parse_args()
    try:
        if args.action == "status":
            status()
        elif args.action in ("cleanup", "next"):
            lifecycle_run(args.action, pr=args.pr, branch=args.branch,
                          execute=args.execute, authorize=args.authorize)
        else:
            run(args.action, args.execute, args.authorize)
    except (ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
        print(f"GIT TOOL ERROR: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
