"""Explicitly authorized creation of one PR, with duplicate and state checks."""
import hashlib
import json
import re
import subprocess
from pathlib import Path

from process_guard import executable, invoke
from pr_preflight import validate_pr_body
from pr_publication import BRANCH, SHA, remote_head

REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
LABEL = re.compile(r"[A-Za-z0-9_.-]+\Z")


def _read(argv):
    result = invoke(argv)
    if result.returncode:
        raise RuntimeError(f"read-only command failed: {' '.join(argv[:3])}")
    return result.stdout.strip()


def _current_branch(repo, branch):
    if not REPOSITORY.fullmatch(repo) or not BRANCH.fullmatch(branch):
        raise ValueError("invalid repository or development branch")
    head = _read(["git", "rev-parse", "HEAD"])
    if not SHA.fullmatch(head) or _read(["git", "branch", "--show-current"]) != branch:
        raise RuntimeError("cannot verify current development HEAD")
    if remote_head(branch) != head:
        raise RuntimeError("remote branch is not the committed local HEAD")
    if _read(["git", "status", "--porcelain"]):
        raise RuntimeError("worktree is not clean")
    base = _read(["git", "rev-parse", "origin/main"])
    remote_main = _read(["git", "ls-remote", "--heads", "origin", "main"]).split()[0]
    if base != remote_main:
        raise RuntimeError("origin/main is stale; fetch and rebuild first")
    existing = json.loads(_read(["gh", "pr", "list", "--state", "all", "--head", branch,
                                 "--repo", repo, "--json", "number"]))
    if not isinstance(existing, list) or existing:
        raise RuntimeError("an existing or ambiguous PR was found for this branch")
    return head


def _requested_details(title, body_file, label, milestone, assignee):
    if not title or len(title) > 120 or any(ord(c) < 32 for c in title):
        raise ValueError("invalid PR title")
    if not LABEL.fullmatch(label or ""):
        raise ValueError("explicit label required")
    if milestone and (len(milestone) > 100 or any(ord(c) < 32 for c in milestone)):
        raise ValueError("invalid milestone")
    if assignee and not LABEL.fullmatch(assignee):
        raise ValueError("invalid assignee")
    body = Path(body_file).resolve()
    if not body.is_file():
        raise ValueError("finalized PR body file missing")
    data = body.read_text(encoding="utf-8")
    problems = validate_pr_body(data)
    if problems:
        raise ValueError(f"incomplete PR body: {', '.join(problems)}")
    return body, data


def create(repo, branch, *, title, body_file, label, milestone=None, assignee=None,
           execute=False, authorization=None, runner=subprocess.run):
    """Preview first; one authorized create attempt, then independently re-read."""
    body, data = _requested_details(title, body_file, label, milestone, assignee)
    head = _current_branch(repo, branch)
    fingerprint = hashlib.sha256(
        json.dumps([title, data, label, milestone, assignee], ensure_ascii=False).encode("utf-8")
    ).hexdigest()[:16]
    approval = f"{repo}:{branch}:{head}:{fingerprint}"
    if not execute:
        return {"preview": approval}
    if authorization != approval:
        raise ValueError("exact PR creation authorization required")
    args = [executable("gh"), "pr", "create", "--repo", repo, "--base", "main",
            "--head", branch, "--title", title, "--body-file", str(body), "--label", label]
    if milestone:
        args.extend(["--milestone", milestone])
    if assignee:
        args.extend(["--assignee", assignee])
    result = runner(args, shell=False, check=False, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError("PR create failed; inspect GitHub state before retrying")
    details = json.loads(_read(["gh", "pr", "view", "--repo", repo,
                                "--json", "number,headRefOid,baseRefName,url"]))
    if details.get("headRefOid") != head or details.get("baseRefName") != "main":
        raise RuntimeError("created PR state differs from authorized commit; stop for review")
    return {"created": details.get("url"), "head": head, "number": details.get("number")}
