#!/usr/bin/env python3
"""Restricted gh entry point for PR inspection; GitHub writes are denied by default.

This is a cooperative wrapper, NOT a sandbox. Agents with direct gh or token
access can bypass it. Do not grant the wrapper credentials as a security boundary.
"""
import argparse
import re
import subprocess
import sys
import json

# Exact read-only command verbs. Unknown options/subcommands fail closed.
SAFE_COMMANDS = {
    ("pr", "view"), ("pr", "checks"), ("pr", "list"),
    ("run", "view"), ("run", "list"),
    ("workflow", "list"),
}
SAFE_FLAGS = {
    ("pr", "view"): {"--json", "--jq", "--template", "--repo", "-R"},
    ("pr", "checks"): {"--repo", "-R", "--json", "--jq", "--limit"},
    ("pr", "list"): {"--repo", "-R", "--json", "--jq", "--template", "--limit", "-L", "--state", "--head", "--base"},
    ("run", "view"): {"--repo", "-R", "--json", "--jq", "--template", "--log", "--log-failed", "--attempt"},
    ("run", "list"): {"--repo", "-R", "--json", "--jq", "--limit", "-L", "--workflow", "--branch", "--event", "--status", "--commit"},
    ("workflow", "list"): {"--repo", "-R", "--json", "--jq", "--limit", "-L"},
}
BOOL_FLAGS = {"--log", "--log-failed"}
POSITIONAL = re.compile(r"^[a-zA-Z0-9_.#/:-]+$")
THREAD_ID = re.compile(r"^PRRT_[A-Za-z0-9_-]+$")
REPO = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")


def resolve_thread(repo, pr_number, thread_id, authorization, *, execute, dry_run=False):
    """Resolve only an existing, verified thread; caller confirms fix evidence.

    The per-invocation authorization is an operator confirmation, not proof of
    user consent. Agent rules separately require explicit user authorization.
    """
    if not REPO.fullmatch(repo) or not pr_number.isdecimal() or int(pr_number) < 1 or not THREAD_ID.fullmatch(thread_id):
        raise ValueError("invalid repository, PR number or review thread ID")
    if authorization != f"{pr_number}:{thread_id}":
        raise ValueError("exact per-thread authorization required")
    owner, name = repo.split("/")
    query = "query($owner:String!,$name:String!,$number:Int!,$after:String){repository(owner:$owner,name:$name){pullRequest(number:$number){reviewThreads(first:100,after:$after){pageInfo{hasNextPage endCursor} nodes{id isResolved isOutdated}}}}}"
    matches = []
    cursor = None
    seen = set()
    while True:
        argv = ["gh", "api", "graphql", "-f", f"query={query}", "-f", f"owner={owner}", "-f", f"name={name}", "-F", f"number={pr_number}"]
        if cursor:
            argv.extend(["-f", f"after={cursor}"])
        data = json.loads(execute(argv))
        if data.get("errors"):
            raise RuntimeError(f"GitHub GraphQL query failed: {data['errors']}")
        page = data["data"]["repository"]["pullRequest"]["reviewThreads"]
        matches.extend(t for t in page["nodes"] if t["id"] == thread_id)
        if not page["pageInfo"]["hasNextPage"]:
            break
        next_cursor = page["pageInfo"].get("endCursor")
        if not next_cursor or next_cursor in seen:
            raise RuntimeError("invalid or repeated review-thread pagination cursor")
        seen.add(next_cursor)
        cursor = next_cursor
    if len(matches) != 1 or matches[0]["isResolved"] or matches[0]["isOutdated"]:
        raise ValueError("thread absent, outdated or already resolved; stop for review")
    if dry_run:
        return {"id": thread_id, "isResolved": False, "previewOnly": True}
    mutation = "mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{id isResolved}}}"
    response = json.loads(execute(["gh", "api", "graphql", "-f", f"query={mutation}", "-f", f"id={thread_id}"]))
    if response.get("errors"):
        raise RuntimeError(f"GitHub GraphQL resolution failed: {response['errors']}")
    thread = response["data"]["resolveReviewThread"]["thread"]
    if thread["id"] != thread_id or not thread["isResolved"]:
        raise RuntimeError("resolution not confirmed")
    return thread


def validate_gh_args(args):
    if len(args) < 2 or tuple(args[:2]) not in SAFE_COMMANDS:
        return "only allowlisted read-only gh commands are permitted"
    seen_positionals = 0
    allowed_flags = SAFE_FLAGS[tuple(args[:2])]
    i = 2
    while i < len(args):
        item = args[i]
        if item in BOOL_FLAGS:
            i += 1
            continue
        flag = item.split("=", 1)[0]
        if flag in allowed_flags:
            if "=" not in item:
                if i + 1 >= len(args) or args[i + 1].startswith("-") or not args[i + 1]:
                    return f"missing value for {item}"
                i += 1
            elif not item.split("=", 1)[1]:
                return f"missing value for {flag}"
            i += 1
            continue
        if item.startswith("-"):
            return f"unrecognized or unsafe option: {item}"
        if not POSITIONAL.fullmatch(item) or seen_positionals:
            return "unexpected argument or second positional argument"
        seen_positionals += 1
        i += 1
    return None


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "resolve-thread":
        resolver = argparse.ArgumentParser(description="Resolve one authorized, verified PR review thread only")
        resolver.add_argument("resolve-thread")
        resolver.add_argument("--repo", required=True)
        resolver.add_argument("--pr", required=True)
        resolver.add_argument("--thread", required=True)
        resolver.add_argument("--authorize")
        resolver.add_argument("--execute", action="store_true", help="Perform authorized GitHub mutation; otherwise preview only")
        args = resolver.parse_args()
        try:
            if args.execute and not args.authorize:
                raise ValueError("--execute requires --authorize and explicit user consent")
            result = resolve_thread(args.repo, args.pr, args.thread,
                                    args.authorize if args.execute else f"{args.pr}:{args.thread}",
                                    execute=lambda argv: subprocess.check_output(argv, text=True),
                                    dry_run=not args.execute)
            print(f"Review thread {'resolved' if args.execute else 'eligible (preview only; not resolved)'}: {result['id']}")
            return 0
        except (ValueError, KeyError, TypeError, json.JSONDecodeError, subprocess.CalledProcessError, OSError, RuntimeError) as exc:
            print(f"GH GUARD DENIED: {exc}", file=sys.stderr)
            return 2
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("gh_args", nargs=argparse.REMAINDER)
    options = parser.parse_args()
    error = validate_gh_args(options.gh_args)
    if error:
        print(f"GH GUARD DENIED: {error}", file=sys.stderr)
        return 2
    try:
        return subprocess.call(["gh", *options.gh_args])
    except OSError as exc:
        print(f"GH GUARD ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
