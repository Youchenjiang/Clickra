#!/usr/bin/env python3
"""Clickra PR assistant: readable status/review and one-command local readiness.

GitHub writes are deliberately not automatic. A separate, explicitly authorized
resolve command delegates to gh_guard; publishing requires per-action approval.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from gh_guard import resolve_thread


def command(argv):
    proc = subprocess.run(argv, capture_output=True, text=True, check=False)
    if proc.returncode:
        raise RuntimeError(f"{' '.join(argv[:3])} failed ({proc.returncode}): {proc.stderr.strip() or proc.stdout.strip()}")
    return proc.stdout


def context(run=command):
    branch = run(["git", "branch", "--show-current"]).strip()
    url = run(["git", "remote", "get-url", "origin"]).strip()
    import re
    match = re.search(r"(?:github\.com[:/])([^/]+)/([^/]+?)(?:\.git)?$", url)
    if not match:
        raise ValueError("origin is not a recognizable GitHub repository")
    return f"{match.group(1)}/{match.group(2)}", branch


def pr_number(value, repo, branch, run=command):
    if value:
        if not value.isdecimal() or int(value) < 1:
            raise ValueError("PR number must be a positive integer")
        return int(value)
    data = json.loads(run(["gh", "pr", "view", "--repo", repo, "--json", "number", "--jq", ".number"]))
    if not isinstance(data, int) or data < 1:
        raise ValueError(f"no PR found for branch {branch}; provide an explicit PR number")
    return data


def inspect(repo, number, run=command):
    raw = run(["gh", "pr", "view", str(number), "--repo", repo,
               "--json", "number,title,state,isDraft,baseRefName,headRefName,mergeable,reviewDecision,statusCheckRollup,url"])
    data = json.loads(raw)
    if not isinstance(data, dict) or data.get("number") != number:
        raise ValueError("GitHub returned an unexpected PR")
    return data


def review_threads(repo, number, run=command):
    """Read every page; never post comments or mutate review state."""
    owner, name = repo.split("/", 1)
    query = ("query($owner:String!,$name:String!,$number:Int!,$after:String){"
             "repository(owner:$owner,name:$name){pullRequest(number:$number){"
             "reviewThreads(first:100,after:$after){pageInfo{hasNextPage endCursor}"
             "nodes{id isResolved isOutdated path line comments(first:1){nodes{body url}}}}}}}")
    after = None
    seen = set()
    threads = []
    while True:
        argv = ["gh", "api", "graphql", "-f", f"query={query}", "-f", f"owner={owner}",
                "-f", f"name={name}", "-F", f"number={number}"]
        if after:
            argv.extend(["-f", f"after={after}"])
        data = json.loads(run(argv))
        if data.get("errors"):
            raise RuntimeError(f"GitHub review-thread query failed: {data['errors']}")
        page = data["data"]["repository"]["pullRequest"]["reviewThreads"]
        threads.extend(page["nodes"])
        info = page["pageInfo"]
        if not info["hasNextPage"]:
            return threads
        cursor = info.get("endCursor")
        if not cursor or cursor in seen:
            raise RuntimeError("invalid/repeated GitHub review-thread cursor")
        seen.add(cursor)
        after = cursor


def print_review(repo, number, run=command):
    items = [x for x in review_threads(repo, number, run) if not x["isResolved"]]
    print(f"PR #{number}: {len(items)} unresolved review thread(s)")
    for thread in items:
        comments = thread.get("comments", {}).get("nodes", [])
        first = comments[0] if comments else {}
        print(f"- {thread['id']} {thread.get('path') or '?'}:{thread.get('line') or '?'}"
              f"{' [outdated]' if thread.get('isOutdated') else ''}")
        print(f"  {str(first.get('body') or '(no comment text)').replace(chr(10), ' ')[:500]}")
        if first.get("url"):
            print(f"  {first['url']}")
    return items


def print_status(repo, number, run=command):
    pr = inspect(repo, number, run)
    print(f"PR #{number}: {pr.get('title')}\n{pr.get('url')}")
    print(f"State: {pr.get('state')} | Draft: {pr.get('isDraft')} | "
          f"Base: {pr.get('baseRefName')} | Head: {pr.get('headRefName')}")
    print(f"Mergeable: {pr.get('mergeable')} | Review: {pr.get('reviewDecision')}")
    checks = pr.get("statusCheckRollup") or []
    for check in checks:
        print(f"  CI {check.get('name') or check.get('context') or '?'}: "
              f"{check.get('conclusion') or check.get('state') or check.get('status') or 'UNKNOWN'}")
    unresolved = print_review(repo, number, run)
    if pr.get("baseRefName") != "main" or unresolved:
        print("Attention: base must be main and all findings must be addressed before merging.")


def _ready(body_file):
    body = Path(body_file).resolve()
    if not body.is_file():
        raise ValueError(f"PR body file does not exist: {body}")
    env = dict(os.environ, CLICKRA_PR_BODY_FILE=str(body))
    hook = Path(__file__).resolve().parent / "hooks" / "pre-push"
    shell = shutil.which("sh")
    if not shell and os.name == "nt":
        git = shutil.which("git")
        if git:
            candidate = Path(git).resolve().parent.parent / "bin" / "sh.exe"
            if candidate.is_file():
                shell = str(candidate)
    if not shell:
        shell = shutil.which("bash") if os.name != "nt" else None
    if not shell:
        raise RuntimeError("Git Bash/sh unavailable; install Git for Windows shell and retry")
    return subprocess.call([shell, str(hook)], env=env)


def _execute_action(args, run):
    if args.action == "publish":
        print("Publication disabled in this entry point: obtain explicit approval for push and PR creation separately.")
        return 2
    if args.action == "ready":
        return _ready(args.body_file)
    repo, branch = context(run)
    number = pr_number(args.pr, repo, branch, run)
    if args.action == "status":
        print_status(repo, number, run)
    elif args.action == "review":
        print_review(repo, number, run)
    else:
        if args.execute and not args.authorize:
            raise ValueError("--execute requires --authorize and explicit user consent")
        thread = resolve_thread(repo, str(number), args.thread,
                                args.authorize if args.execute else f"{number}:{args.thread}",
                                execute=run, dry_run=not args.execute)
        print(f"{'Resolved' if args.execute else 'Eligible for resolution (preview only)'}: {thread['id']}; no comment posted")
    return 0


def main(argv=None, run=command):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    for name in ("status", "review"):
        sub.add_parser(name).add_argument("pr", nargs="?")
    ready = sub.add_parser("ready", help="Execute local pre-push gate without pushing")
    ready.add_argument("--body-file", required=True)
    sub.add_parser("publish", help="Explain separately authorized publication steps")
    resolver = sub.add_parser("resolve", help="Resolve exactly one explicitly authorized thread")
    resolver.add_argument("pr")
    resolver.add_argument("--thread", required=True)
    resolver.add_argument("--authorize")
    resolver.add_argument("--execute", action="store_true", help="Execute explicitly authorized resolution; default is read-only preview")
    args = parser.parse_args(argv)
    try:
        return _execute_action(args, run)
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as exc:
        print(f"PR TOOL ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
