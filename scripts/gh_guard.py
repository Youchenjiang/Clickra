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
from process_guard import invoke

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


def _github_command(argv):
    """Internal, narrowly scoped GraphQL transport; never a shell command."""
    if argv[:3] != ["gh", "api", "graphql"]:
        raise ValueError("review transport only supports GitHub GraphQL")
    result = invoke(argv)
    if result.returncode:
        raise RuntimeError(f"GitHub GraphQL transport failed: {result.stderr.strip()}")
    return result.stdout


def _review_page(repo, pr_number, cursor, execute):
    """Fetch and validate one review-thread page."""
    owner, name = repo.split("/")
    query = "query($owner:String!,$name:String!,$number:Int!,$after:String){repository(owner:$owner,name:$name){pullRequest(number:$number){reviewThreads(first:100,after:$after){pageInfo{hasNextPage endCursor} nodes{id isResolved isOutdated comments(last:1){totalCount nodes{id}}}}}}}"
    argv = ["gh", "api", "graphql", "-f", f"query={query}", "-f", f"owner={owner}", "-f", f"name={name}", "-F", f"number={pr_number}"]
    if cursor:
        argv.extend(["-f", f"after={cursor}"])
    data = json.loads(execute(argv))
    if data.get("errors"):
        raise RuntimeError(f"GitHub GraphQL query failed: {data['errors']}")
    return data["data"]["repository"]["pullRequest"]["reviewThreads"]


def _thread_state(repo, pr_number, thread_id, execute):
    """Read exactly one thread, including its current resolution state."""
    matches = []
    cursor = None
    seen = set()
    while True:
        page = _review_page(repo, pr_number, cursor, execute)
        matches.extend(t for t in page["nodes"] if t["id"] == thread_id)
        if not page["pageInfo"]["hasNextPage"]:
            break
        next_cursor = page["pageInfo"].get("endCursor")
        if not next_cursor or next_cursor in seen:
            raise RuntimeError("invalid or repeated review-thread pagination cursor")
        seen.add(next_cursor)
        cursor = next_cursor
    if len(matches) != 1:
        raise ValueError("review thread absent or duplicated; stop for review")
    comments = matches[0].get("comments")
    if not isinstance(comments, dict) or "totalCount" not in comments or "nodes" not in comments:
        raise RuntimeError("cannot verify review comment snapshot; stop for review")
    return (matches[0]["isResolved"], matches[0]["isOutdated"],
            comments["totalCount"], tuple(node["id"] for node in comments["nodes"]))


def _resolve_mutation(thread_id, execute):
    """Issue at most one mutation; its transport response is not authoritative."""
    mutation = "mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{id isResolved}}}"
    mutation_error = None
    try:
        response = json.loads(execute(["gh", "api", "graphql", "-f", f"query={mutation}", "-f", f"id={thread_id}"]))
        if response.get("errors"):
            raise RuntimeError(f"GitHub GraphQL resolution failed: {response['errors']}")
        thread = response["data"]["resolveReviewThread"]["thread"]
        if thread["id"] != thread_id or not thread["isResolved"]:
            raise RuntimeError("resolution response not confirmed")
    except (RuntimeError, OSError, ValueError, KeyError, TypeError) as exc:
        mutation_error = exc
    return mutation_error


def _confirm_resolved(repo, pr_number, thread_id, execute, mutation_error):
    # A lost mutation response may follow a successful write. Never issue a second mutation.
    try:
        state = _thread_state(repo, pr_number, thread_id, execute)
    except (RuntimeError, OSError, ValueError, KeyError, TypeError) as exc:
        raise RuntimeError("cannot verify review thread after mutation; outcome unknown") from exc
    if not state[0]:
        raise RuntimeError("resolution not confirmed by fresh GitHub state") from mutation_error
    return {"id": thread_id, "isResolved": True}


def resolve_thread(repo, pr_number, thread_id, authorization, *, execute, dry_run=False,
                   repair_evidence=None):
    """Resolve a verified repair with exact permission; evidence must be checked independently."""
    if not REPO.fullmatch(repo) or not pr_number.isdecimal() or int(pr_number) < 1 or not THREAD_ID.fullmatch(thread_id):
        raise ValueError("invalid repository, PR number or review thread ID")
    if authorization != f"{pr_number}:{thread_id}":
        raise ValueError("exact per-thread authorization required")
    snapshot = _thread_state(repo, pr_number, thread_id, execute)
    if snapshot[0]:
        raise ValueError("thread already resolved; stop for review")
    if snapshot[1] and (not isinstance(repair_evidence, str) or not repair_evidence.strip()):
        raise ValueError("outdated thread requires independently verified repair evidence")
    if dry_run:
        return {"id": thread_id, "isResolved": False, "previewOnly": True}
    if _thread_state(repo, pr_number, thread_id, execute) != snapshot:
        raise RuntimeError("review thread changed since verification; refuse resolution")
    return _confirm_resolved(repo, pr_number, thread_id, execute, _resolve_mutation(thread_id, execute))


def _flag_error(item, args, index):
    if "=" in item:
        return None if item.split("=", 1)[1] else f"missing value for {item.split('=', 1)[0]}"
    if index + 1 >= len(args) or args[index + 1].startswith("-") or not args[index + 1]:
        return f"missing value for {item}"
    return None


def _positional_error(item, seen_positionals):
    if item.startswith("-"):
        return f"unrecognized or unsafe option: {item}"
    if not POSITIONAL.fullmatch(item) or seen_positionals:
        return "unexpected argument or second positional argument"
    return None


def _validate_gh_tail(args, allowed_flags):
    seen_positionals = 0
    i = 0
    while i < len(args):
        item = args[i]
        if item in BOOL_FLAGS:
            i += 1
            continue
        flag = item.split("=", 1)[0]
        if flag in allowed_flags:
            error = _flag_error(item, args, i)
            if error:
                return error
            if "=" not in item:
                i += 1
            i += 1
            continue
        error = _positional_error(item, seen_positionals)
        if error:
            return error
        seen_positionals += 1
        i += 1
    return None


def validate_gh_args(args):
    if len(args) < 2 or tuple(args[:2]) not in SAFE_COMMANDS:
        return "only allowlisted read-only gh commands are permitted"
    return _validate_gh_tail(args[2:], SAFE_FLAGS[tuple(args[:2])])


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "resolve-thread":
        resolver = argparse.ArgumentParser(description="Resolve one authorized, verified PR review thread only")
        resolver.add_argument("resolve-thread")
        resolver.add_argument("--repo", required=True)
        resolver.add_argument("--pr", required=True)
        resolver.add_argument("--thread", required=True)
        resolver.add_argument("--authorize")
        resolver.add_argument("--repair-evidence", help="Independent verification reference for an outdated thread; does not grant permission")
        resolver.add_argument("--execute", action="store_true", help="Perform authorized GitHub mutation; otherwise preview only")
        args = resolver.parse_args()
        try:
            if args.execute and not args.authorize:
                raise ValueError("--execute requires --authorize and explicit user consent")
            result = resolve_thread(args.repo, args.pr, args.thread,
                                    args.authorize if args.execute else f"{args.pr}:{args.thread}",
                                    execute=_github_command,
                                    dry_run=not args.execute, repair_evidence=args.repair_evidence)
            print(f"Review thread {'resolved' if args.execute else 'eligible (preview only; not resolved)'}: {result['id']}")
            return 0
        except (ValueError, KeyError, TypeError, subprocess.CalledProcessError, OSError, RuntimeError) as exc:
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
        return invoke(["gh", *options.gh_args], capture=False).returncode
    except OSError as exc:
        print(f"GH GUARD ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
