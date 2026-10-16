#!/usr/bin/env python3
"""Clickra PR assistant: readable status/review and one-command local readiness.

GitHub writes are deliberately not automatic. A separate, explicitly authorized
resolve command delegates to gh_guard; publishing requires per-action approval.
"""
import argparse
import html
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys

from gh_guard import resolve_thread
from process_guard import invoke
from pr_audit import inventory, paged
from sonar_audit import inventory as sonar_inventory
from sonar_audit import quality_metrics
from pr_publication import push as guarded_push, remote_head
from pr_creation import create as guarded_create
from pr_verification import summarize
from pr_reports import fetch_report

SONAR_HOST_DEFAULT = "https://sonarcloud.io"


def safe_terminal(value):
    """Render untrusted GitHub fields without terminal control sequences."""
    return re.sub(r"[\x00-\x1f\x7f-\x9f]", " ", str(value))


def command(argv):
    proc = invoke(argv)
    if proc.returncode:
        raise RuntimeError(f"{' '.join(argv[:3])} failed ({proc.returncode}): {proc.stderr.strip() or proc.stdout.strip()}")
    return proc.stdout


def context(run=command):
    branch = run(["git", "branch", "--show-current"]).strip()
    url = run(["git", "remote", "get-url", "origin"]).strip()
    match = re.search(r"github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?$", url)
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
               "--json", "number,title,state,isDraft,baseRefName,headRefName,headRefOid,mergeable,reviewDecision,statusCheckRollup,files,url"])
    data = json.loads(raw)
    if not isinstance(data, dict) or data.get("number") != number:
        raise ValueError("GitHub returned an unexpected PR")
    return data


def export_body(repo, number, output, run=command):
    """Recover an existing PR body for the mandatory push policy gate."""
    payload = json.loads(run(["gh", "pr", "view", str(number), "--repo", repo,
                              "--json", "number,body,headRefOid"]))
    if (not isinstance(payload, dict) or payload.get("number") != number
            or not isinstance(payload.get("body"), str) or not payload["body"].strip()):
        raise ValueError("cannot verify nonempty PR body for requested PR")
    path = Path(output).resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as handle:
        handle.write(payload["body"])
    print(f"Saved PR #{number} body to {path}")


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


def _extract_comment_findings(items):
    """Detect reviews or comments reporting issues, changes requested, or blocker findings."""
    findings = []
    issue_pattern = re.compile(r"(?:blocking issues|finding details|### finding|\bissues found\b|\baction required\b)", re.IGNORECASE)
    for item in items:
        body = item.get("body") or ""
        state = item.get("state")
        author = (item.get("user") or {}).get("login") or "unknown"
        has_finding = (state == "CHANGES_REQUESTED" or bool(issue_pattern.search(body)))
        if has_finding:
            findings.append({
                "author": author,
                "state": state,
                "url": item.get("html_url") or "",
                "body": body,
            })
    return findings


def _print_thread_item(thread):
    comments = thread.get("comments", {}).get("nodes", [])
    first = comments[0] if comments else {}
    print(f"- {safe_terminal(thread['id'])} {safe_terminal(thread.get('path') or '?')}:{safe_terminal(thread.get('line') or '?')}"
          f"{' [outdated]' if thread.get('isOutdated') else ''}")
    print(f"  {safe_terminal(first.get('body') or '(no comment text)')[:500]}")
    if first.get("url"):
        print(f"  {safe_terminal(first['url'])}")


def _print_top_findings(repo, number, run):
    try:
        top_reviews = paged(repo, number, "pulls/reviews", run)
        top_findings = _extract_comment_findings(top_reviews)
    except (OSError, RuntimeError, ValueError):
        top_findings = []
    if not top_findings:
        return
    print(f"\nTop-level review finding(s): {len(top_findings)}")
    for finding in top_findings:
        author_tag = f"@{finding['author']}"
        state_tag = f" [{finding['state']}]" if finding["state"] else ""
        print(f"- {safe_terminal(author_tag)}{state_tag}")
        print(f"  {safe_terminal(finding['body'])[:600]}")
        if finding["url"]:
            print(f"  {safe_terminal(finding['url'])}")


def print_review(repo, number, run=command):
    items = [x for x in review_threads(repo, number, run) if not x["isResolved"]]
    print(f"PR #{number}: {len(items)} unresolved review thread(s)")
    for thread in items:
        _print_thread_item(thread)
    _print_top_findings(repo, number, run)
    return items


def print_status(repo, number, run=command):
    pr = inspect(repo, number, run)
    print(f"PR #{number}: {safe_terminal(pr.get('title'))}\n{safe_terminal(pr.get('url'))}")
    print(f"State: {pr.get('state')} | Draft: {pr.get('isDraft')} | "
          f"Base: {safe_terminal(pr.get('baseRefName'))} | Head: {safe_terminal(pr.get('headRefName'))}")
    print(f"Mergeable: {safe_terminal(pr.get('mergeable'))} | Review: {safe_terminal(pr.get('reviewDecision'))}")
    checks = pr.get("statusCheckRollup") or []
    for check in checks:
        print(f"  CI {safe_terminal(check.get('name') or check.get('context') or '?')}: "
              f"{safe_terminal(check.get('conclusion') or check.get('state') or check.get('status') or 'UNKNOWN')}")
    unresolved = print_review(repo, number, run)
    if pr.get("baseRefName") != "main" or unresolved:
        print("Attention: base must be main and all findings must be addressed before merging.")


def _check_run_details(repo, head_sha, run):
    """Fetch structured details only for an exact validated GitHub commit."""
    if head_sha and re.fullmatch(r"[0-9a-fA-F]{40}", head_sha) and re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repo):
        try:
            raw = run(["gh", "api", f"repos/{repo}/commits/{head_sha}/check-runs?per_page=100"])
            data = json.loads(raw)
            return {item["name"]: item.get("output") or {}
                    for item in data.get("check_runs", []) if isinstance(item, dict) and item.get("name")}
        except (OSError, RuntimeError, ValueError, TypeError, KeyError) as exc:
            print(f"Check Run details unavailable: {safe_terminal(exc)}")
    return {}


def _print_failed_check(item, repo, details, run):
    name = safe_terminal(item.get("name") or item.get("context") or "unknown check")
    url = item.get("detailsUrl") or item.get("targetUrl") or ""
    print(f"\nFAIL: {name}\nURL: {safe_terminal(url)}")
    match = re.search(r"github\.com/[^/]+/[^/]+/actions/runs/(\d+)", url)
    if match:
        try:
            log = run(["gh", "run", "view", match.group(1), "--repo", repo, "--log-failed"])
            print(safe_terminal(log[-12000:]) or "No failed-job log available")
        except (OSError, RuntimeError) as exc:
            print(f"Log unavailable: {safe_terminal(exc)}")
        return
    message = "\n".join(str(details.get(key) or "") for key in ("summary", "text")).strip()
    print(f"GitHub Check Run findings:\n{safe_terminal(message)[:12000]}" if message
          else "External provider: no detailed findings exposed in GitHub Check Run.")


def _is_historical_feedback(item, _passed):
    # A passing Secrets check does not clear a failing C# or Python comment.
    return bool(item.get("isOutdated"))


def _print_review_issue(item):
    comments = item.get("comments", {}).get("nodes", [])
    comment = comments[0] if comments else {}
    body = str(comment.get("body") or "")
    if "<!-- DeepSource:" in body:
        heading = re.search(r"<h3\b[^>]*>(.*?)</h3>", body, re.I | re.S)
        if heading:
            body = html.unescape(re.sub(r"<[^>]+>", "", heading.group(1))).strip()
    print(f"- {safe_terminal(item.get('path') or '?')}: {safe_terminal(body)[:700]}")
    if comment.get("url"):
        print(f"  {safe_terminal(comment['url'])}")


def _review_feedback(repo, number, passed, run):
    """Keep old/outdated threads visible as counts; never resolve them."""
    threads = [item for item in review_threads(repo, number, run) if not item.get("isResolved")]
    active = []
    historical = []
    for item in threads:
        if _is_historical_feedback(item, passed):
            historical.append(item)
        else:
            active.append(item)
    print(f"\nActionable unresolved review thread(s): {len(active)}")
    print(f"Outdated/potentially historical unresolved thread(s): {len(historical)} (not automatically resolved; check status is not proof of resolution)")
    for item in active:
        _print_review_issue(item)
    return active


def _print_provider_reports(selected, mode, fetch):
    """Fetch supported provider HTML without treating missing details as success."""
    print(f"\nProvider reports ({mode}):")
    for item in selected:
        name = item.get("name") or item.get("context") or "unnamed check"
        url = item.get("detailsUrl") or item.get("targetUrl") or ""
        if not url or not any(provider in name.lower() for provider in ("deepsource", "sonar", "sourcery")):
            continue
        print(f"- {safe_terminal(name)}: {safe_terminal(url)}")
        try:
            report = fetch(url)
            print(f"  HTML: {report['bytes']} bytes; embedded issues: {report['has_ssr_issues']}")
            print(f"  Page text: {safe_terminal(report['text'])[:1400]}")
        except (OSError, ValueError, RuntimeError) as exc:
            print(f"  Report unavailable: {safe_terminal(exc)}")


def diagnose(repo, number, run=command, reports="failed", fetch=fetch_report):
    """Read failing check logs and review feedback; never mutate GitHub state."""
    pr = inspect(repo, number, run)
    head_sha = pr.get("headRefOid")
    print(f"PR #{number} HEAD: {safe_terminal(head_sha or 'unknown')}")
    checks = pr.get("statusCheckRollup") or []
    failed = [item for item in checks if str(item.get("conclusion") or item.get("state") or "").upper() in
              ("FAILURE", "FAILED", "ERROR", "TIMED_OUT", "ACTION_REQUIRED")]
    passed = {str(item.get("name") or item.get("context") or "").lower()
              for item in checks if str(item.get("conclusion") or item.get("state") or "").upper() in ("SUCCESS", "PASS", "PASSED")}
    details = _check_run_details(repo, head_sha, run)
    print(f"Current reported checks: {len(failed)} failed")
    for item in failed:
        _print_failed_check(item, repo, details.get(item.get("name") or item.get("context")) or {}, run)
    _print_provider_reports(failed if reports == "failed" else checks, reports, fetch)
    return failed, _review_feedback(repo, number, passed, run)


def _print_open_threads(result):
    linked = {}
    for comment in result["inline_comments"]:
        if comment["thread_id"] and comment["thread_id"] not in linked:
            linked[comment["thread_id"]] = comment
    for thread in result["threads"]:
        if thread["resolved"]:
            continue
        _print_one_open_thread(thread, linked.get(thread["id"]))


def _print_one_open_thread(thread, first):
        status = "UNRESOLVED, OUTDATED" if thread["outdated"] else "UNRESOLVED"
        print(f"- [{status}] {safe_terminal(thread['id'])} {safe_terminal(thread['path'] or '?')}:"
              f"{safe_terminal(thread['line'] or '?')}")
        if first:
            print(f"  {safe_terminal(first['body'])[:180]}")
            if first["url"]:
                print(f"  {safe_terminal(first['url'])}")


def audit(repo, number, run=command, as_json=False, include_sonar=False):
    """Inventory every comment surface, without altering review state."""
    pr = inspect(repo, number, run)
    result = inventory(repo, number, pr.get("headRefOid"), review_threads(repo, number, run), run)
    if include_sonar:
        result["sonar"] = sonar_inventory(
            os.environ.get("SONAR_PROJECT_KEY") or repo.replace("/", "_"),
            number, base_url=os.environ.get("SONAR_HOST_URL", SONAR_HOST_DEFAULT),
            token=os.environ.get("SONAR_TOKEN"))
    if as_json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return result
    counts = result["counts"]
    print(f"PR #{number} HEAD: {safe_terminal(result['head'] or 'unknown')}")
    print(f"Review threads: {counts['threads']} total, {counts['unresolved']} unresolved"
          f" ({counts['outdated_unresolved']} outdated but unresolved)")
    print(f"All inline comments: {counts['inline']} | General comments: {counts['general']}"
          f" | Review summaries: {counts['reviews']}")
    if include_sonar:
        print_sonar(result["sonar"])
    _print_open_threads(result)
    current = sum(c["head_relation"] == "current" for c in result["inline_comments"])
    historical = sum(c["head_relation"] == "historical" for c in result["inline_comments"])
    print(f"Inline commit relation: {current} current, {historical} historical;"
          " unscoped comments require manual review")
    print("Resolved threads omitted from this view; use 'audit --json' for all records, full bodies and metadata.")
    return result


def print_sonar(data):
    print(f"SonarQube Cloud: {data['total']} open/confirmed issue(s)")
    for issue in data["issues"]:
        impacts = issue.get("impacts") or []
        severity = ", ".join(f"{x.get('softwareQuality')}:{x.get('severity')}" for x in impacts) if impacts else issue.get("severity")
        print(f"- {safe_terminal(issue.get('key'))} {safe_terminal(issue.get('component'))}:{safe_terminal(issue.get('line') or '?')}"
              f" [{safe_terminal(severity or '?')}] {safe_terminal(issue.get('rule'))}")
        print(f"  {safe_terminal(issue.get('message') or '')}")
    print(f"SonarQube details: {safe_terminal(data['url'])}")


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
    return subprocess.run([shell, str(hook)], env=env, check=False, shell=False).returncode


def _prepare(body_file, initial=False):
    """Run the versioned lightweight policy gate, never the full NativeAOT hook."""
    body = Path(body_file).resolve()
    if not body.is_file():
        raise ValueError(f"PR body file does not exist: {body}")
    from pr_preflight import _github_metadata, check
    if initial:
        from pr_preflight import validate_pr_body
        # This is only a preview: the actual first push verifies remote main
        # and the exact object through the mandatory versioned hook.
        problems = validate_pr_body(body.read_text(encoding="utf-8"))
    else:
        problems = check("origin/main", str(body), metadata=_github_metadata())
    for problem in problems:
        print(f"PR preflight FAIL: {safe_terminal(problem)}")
    if problems:
        return 1
    print("PR preflight OK; the mandatory full gate will run during push")
    return 0


def _execute_action(args, run):
    if args.action == "push":
        result = guarded_push(args.body_file, args.authorize, execute=args.execute)
        print(json.dumps(result))
        return 0
    if args.action == "publish":
        print("Publication disabled in this entry point: obtain explicit approval for push and PR creation separately.")
        return 2
    if args.action == "ready":
        return _ready(args.body_file)
    if args.action == "prepare":
        return _prepare(args.body_file, args.initial)
    repo, branch = context(run)
    if args.action == "create":
        result = guarded_create(repo, branch, title=args.title, body_file=args.body_file,
                                label=args.label, milestone=args.milestone,
                                assignee=args.assignee, execute=args.execute,
                                authorization=args.authorize)
        print(json.dumps(result))
        return 0
    number = pr_number(args.pr, repo, branch, run)
    return _execute_pr_action(args, run, repo, number)


def _show_sonar(args, repo, number):
    data = sonar_inventory(os.environ.get("SONAR_PROJECT_KEY") or repo.replace("/", "_"),
                           number, base_url=os.environ.get("SONAR_HOST_URL", SONAR_HOST_DEFAULT),
                           token=os.environ.get("SONAR_TOKEN"))
    if args.json:
        print(json.dumps(data, indent=2, ensure_ascii=False))
    else:
        print_sonar(data)


def _verify_pr(run, repo, number):
    pr = inspect(repo, number, run)
    report = inventory(repo, number, pr.get("headRefOid"), review_threads(repo, number, run), run)
    report["sonar"] = sonar_inventory(os.environ.get("SONAR_PROJECT_KEY") or repo.replace("/", "_"),
                                      number, base_url=os.environ.get("SONAR_HOST_URL", SONAR_HOST_DEFAULT),
                                      token=os.environ.get("SONAR_TOKEN"))
    report["sonar_quality"] = quality_metrics(
        os.environ.get("SONAR_PROJECT_KEY") or repo.replace("/", "_"), number,
        base_url=os.environ.get("SONAR_HOST_URL", SONAR_HOST_DEFAULT),
        token=os.environ.get("SONAR_TOKEN"))
    remote = (remote_head(pr.get("headRefName", ""))
              if pr.get("state") == "OPEN" else None)
    result = summarize(pr, report, remote_sha=remote)
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0 if result["ready"] else 2


def _execute_pr_action(args, run, repo, number):
    if args.action == "body":
        export_body(repo, number, args.output, run)
    elif args.action == "status":
        print_status(repo, number, run)
    elif args.action == "audit":
        audit(repo, number, run, args.json, args.sonar)
    elif args.action == "sonar":
        _show_sonar(args, repo, number)
    elif args.action == "diagnose":
        diagnose(repo, number, run, reports=args.reports)
    elif args.action == "verify":
        return _verify_pr(run, repo, number)
    elif args.action == "review":
        print_review(repo, number, run)
    else:
        _execute_resolution(repo, number, args, run)
    return 0


def _execute_resolution(repo, number, args, run):
    if args.execute and not args.authorize:
        raise ValueError("--execute requires --authorize and explicit user consent")
    thread = resolve_thread(repo, str(number), args.thread,
                            args.authorize if args.execute else f"{number}:{args.thread}",
                            execute=run, dry_run=not args.execute,
                            repair_evidence=args.repair_evidence)
    print(f"{'Resolved' if args.execute else 'Eligible for resolution (preview only)'}: {thread['id']}; no comment posted")

def main(argv=None, run=command):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    for name in ("status", "review", "diagnose", "verify"):
        p = sub.add_parser(name)
        p.add_argument("pr", nargs="?")
        if name == "diagnose":
            p.add_argument("--reports", choices=("failed", "all"), default="failed",
                           help="Fetch failed provider reports only (default), or all provider reports")
    body = sub.add_parser("body", help="Export an existing PR body without overwriting files")
    body.add_argument("pr", nargs="?")
    body.add_argument("--output", required=True)
    auditor = sub.add_parser("audit", help="Read all PR comment categories, no writes")
    auditor.add_argument("pr", nargs="?")
    auditor.add_argument("--json", action="store_true", help="Output full machine-readable inventory")
    auditor.add_argument("--sonar", action="store_true", help="Also fetch SonarQube Cloud issues (requires API access)")
    sonar = sub.add_parser("sonar", help="Read open/confirmed SonarQube Cloud PR issues")
    sonar.add_argument("pr", nargs="?")
    sonar.add_argument("--json", action="store_true")
    ready = sub.add_parser("ready", help="Execute local pre-push gate without pushing")
    ready.add_argument("--body-file", required=True)
    prepare = sub.add_parser("prepare", help="Run quick PR policy checks without full build/NativeAOT")
    prepare.add_argument("--body-file", required=True)
    prepare.add_argument("--initial", action="store_true",
                         help="Before a PR exists, validate the full draft metadata only")
    sub.add_parser("publish", help="Explain separately authorized publication steps")
    push = sub.add_parser("push", help="Preview or perform one explicitly authorized, hook-verified branch push")
    push.add_argument("--body-file", required=True)
    push.add_argument("--authorize", help="Exact branch:40-character-SHA from preview")
    push.add_argument("--execute", action="store_true", help="Requires explicit approval for this exact push")
    creator = sub.add_parser("create", help="Preview or explicitly authorize creation of one PR")
    creator.add_argument("--title", required=True)
    creator.add_argument("--body-file", required=True)
    creator.add_argument("--label", required=True)
    creator.add_argument("--milestone")
    creator.add_argument("--assignee")
    creator.add_argument("--authorize")
    creator.add_argument("--execute", action="store_true")
    resolver = sub.add_parser("resolve", help="Resolve exactly one explicitly authorized thread")
    resolver.add_argument("pr")
    resolver.add_argument("--thread", required=True)
    resolver.add_argument("--authorize")
    resolver.add_argument("--repair-evidence", help="Verified fix reference for an outdated thread")
    resolver.add_argument("--execute", action="store_true", help="Execute explicitly authorized resolution; default is read-only preview")
    args = parser.parse_args(argv)
    try:
        return _execute_action(args, run)
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as exc:
        print(f"PR TOOL ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
