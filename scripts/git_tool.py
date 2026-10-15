#!/usr/bin/env python3
"""Conservative local Git workflow helper with state-bound execution approval.

Mutations are limited to local main synchronization and the currently checked-out,
unpublished feature branch. No stash, reset, force push, or worktree switching.
"""
import argparse
import hashlib
import json
import subprocess
import sys


def git(*args, check=True):
    proc = subprocess.run(["git", *args], capture_output=True, text=True,
                          timeout=30, check=False)
    if check and proc.returncode:
        raise RuntimeError(f"git {args[0]} failed: {proc.stderr.strip()}")
    return proc


def output(*args):
    return git(*args).stdout.strip()


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


def snapshot(action):
    branch = output("branch", "--show-current")
    if not branch:
        raise ValueError("detached HEAD is unsupported")
    if output("rev-parse", "--show-toplevel") == "":
        raise ValueError("repository root unavailable")
    assert_clean()
    local_base = output("rev-parse", "refs/heads/main")
    remote_base = output("ls-remote", "--heads", "origin", "main").split()
    if len(remote_base) != 2 or remote_base[1] != "refs/heads/main":
        raise ValueError("cannot verify remote main")
    if output("rev-parse", "refs/remotes/origin/main") != remote_base[0]:
        raise ValueError("origin/main is stale; fetch origin main first")
    current_head = output("rev-parse", "HEAD")
    if action == "sync":
        if branch == "main":
            raise ValueError("sync must run from a separate worktree")
        if not ancestor(local_base, remote_base[0]):
            raise ValueError("local main cannot fast-forward")
        worktrees = output("worktree", "list", "--porcelain")
        if "branch refs/heads/main\n" in worktrees + "\n":
            raise ValueError("main is checked out in another worktree")
        state = {"action": action, "branch": branch, "head": current_head,
                 "main": local_base, "remote_main": remote_base[0]}
    else:
        if branch == "main":
            raise ValueError("cannot rebase main")
        published = output("ls-remote", "--heads", "origin", branch)
        if published:
            raise ValueError("branch exists on origin; refuse to rewrite published history")
        if not ancestor(local_base, current_head):
            raise ValueError("branch does not descend from local main")
        state = {"action": action, "branch": branch, "head": current_head,
                 "main": local_base, "remote_main": remote_base[0]}
    return state


def authorization(state):
    fingerprint = json.dumps(state, sort_keys=True, separators=(",", ":"))
    digest = hashlib.sha256(fingerprint.encode("utf-8")).hexdigest()[:20]
    return f"{state['action']}:{state['branch']}:{digest}"


def status():
    print(json.dumps({"branch": output("branch", "--show-current"),
                      "head": output("rev-parse", "HEAD"),
                      "main": output("rev-parse", "refs/heads/main"),
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
            git("update-ref", "refs/heads/main", state["remote_main"], state["main"])
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
    args = parser.parse_args()
    try:
        if args.action == "status":
            status()
        else:
            run(args.action, args.execute, args.authorize)
    except (ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
        print(f"GIT TOOL ERROR: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
