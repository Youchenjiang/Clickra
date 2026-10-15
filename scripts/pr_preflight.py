#!/usr/bin/env python3
"""Fail-closed Git publication preflight; no branch or remote mutations."""
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from process_guard import invoke
from branch_policy import validate_branch_name

ALLOWED_TYPES = "feat|fix|refactor|docs|test|chore|style|perf|security"
ALLOWED_SCOPES = "cli|core|shell|msix|docs|ci|deps|store|agent"
SUBJECT = re.compile(rf"^(?:{ALLOWED_TYPES})(?:\((?:{ALLOWED_SCOPES})\))?: [a-z0-9].*$")
BODY_NUMBER = re.compile(r"1[.)][ \t]+")
CHECKLIST_SECTION = "## Review Checklist"
REQUIRED_SECTIONS = ("## Summary", "## Changes", "## Validation", CHECKLIST_SECTION)
PLACEHOLDER = re.compile(r"(?i)\b(?:todo|tbd|placeholder|describe the|assign the|add an appropriate|choose the|not run: full pre-push)\b")
VAGUE = {"update", "misc", "stuff", "changes", "fix bug", "bug fix"}
GIT_TIMEOUT = 30


def git(*args):
    return invoke(["git", *args], timeout=GIT_TIMEOUT)


def _valid_commit_subject(header):
    return (bool(SUBJECT.fullmatch(header)) and not header.endswith(".")
            and len(header) <= 72 and header.split(": ", 1)[-1].lower() not in VAGUE)


def _numbered_commit_body(lines):
    return next((line.lstrip(" \t") for line in lines[1:]
                 if not line.startswith("#") and BODY_NUMBER.match(line.lstrip(" \t"))
                 and BODY_NUMBER.sub("", line.lstrip(" \t"), count=1).strip()), None)


def validate_message(message):
    lines = message.splitlines()
    header = lines[0].strip() if lines else ""
    errors = []
    if not _valid_commit_subject(header):
        errors.append("invalid conventional commit header")
    numbered = _numbered_commit_body(lines)
    if not numbered:
        errors.append("missing numbered English commit body")
    else:
        content = BODY_NUMBER.sub("", numbered, count=1)
        letters = [char for char in content if char.isalpha()]
        if letters and sum(char.isascii() for char in letters) / len(letters) < 0.7:
            errors.append("numbered commit body must be predominantly English/ASCII letters")
    return errors


def _split_pr_sections(body):
    sections = {}
    current = None
    body = re.sub(r"<!--.*?-->", "", body, flags=re.DOTALL)
    for line in body.splitlines():
        if line.startswith("## "):
            current = line.strip()
            sections.setdefault(current, [])
        elif current is not None:
            sections[current].append(line)
    return sections


def _provider_acknowledgement_errors(lines):
    match = re.search(r"(?mi)^- \[x\] \*\*External provider\*\*:[ \t]*(\S[^\r\n]*)$", lines)
    if not match or PLACEHOLDER.search(match.group(1)):
        return ["completed External provider acknowledgement required"]
    return []


def _live_metadata_errors(metadata):
    labels = {item.get("name", "").lower() for item in metadata.get("labels", [])}
    errors = []
    if not labels:
        errors.append("GitHub PR requires at least one label")
    if not metadata.get("milestone") and not labels.intersection({"release", "hotfix", "deps", "dependencies", "docs"}):
        errors.append("GitHub PR requires milestone unless exempt")
    return errors


def validate_pr_body(body, metadata=None):
    errors = []
    sections = _split_pr_sections(body)
    for heading in REQUIRED_SECTIONS:
        if heading not in sections:
            errors.append(heading)
            continue
        content = "\n".join(sections[heading]).strip()
        if not content or (heading != CHECKLIST_SECTION and PLACEHOLDER.search(content)):
            errors.append(f"empty or placeholder {heading}")
    errors.extend(_provider_acknowledgement_errors("\n".join(sections.get(CHECKLIST_SECTION, []))))
    if metadata is not None:
        errors.extend(_live_metadata_errors(metadata))
    return errors


def _check_pushed_ref(pushed_ref, pushed_oid):
    errors = []
    branch = git("branch", "--show-current").stdout.strip()
    if not branch or branch in ("main", "master"):
        errors.append("push must come from a named non-main development branch")
    errors.extend(validate_branch_name(branch))
    if pushed_ref:
        if pushed_ref != f"refs/heads/{branch}":
            errors.append("pushed branch does not match checked development branch")
        if not pushed_oid or not re.fullmatch(r"[0-9a-fA-F]{40,64}", pushed_oid):
            errors.append("invalid pushed object ID")
        elif git("rev-parse", "HEAD").stdout.strip().lower() != pushed_oid.lower():
            errors.append("pushed object is not the validated HEAD")
    return errors


def _check_commit_range(base):
    errors = []
    if git("merge-base", "--is-ancestor", base, "HEAD").returncode:
        errors.append("branch is not based on latest origin/main; rebuild/rebase and revalidate")
    commits = git("log", "--format=%B%x00", f"{base}..HEAD")
    if commits.returncode:
        errors.append("cannot read commit range")
    else:
        for n, message in enumerate(commits.stdout.split("\0"), 1):
            if message.strip():
                errors.extend(f"commit {n}: {e}" for e in validate_message(message.strip()))
    diff = git("diff", "--quiet", base, "HEAD")
    if diff.returncode == 0:
        errors.append("PR branch has no changes against latest origin/main")
    elif diff.returncode != 1:
        errors.append("cannot verify branch changes against base")
    return errors


def _check_history(base, pushed_ref=None, pushed_oid=None):
    errors = _check_pushed_ref(pushed_ref, pushed_oid)
    local = git("rev-parse", base)
    remote = git("ls-remote", "--heads", "origin", "main")
    if local.returncode or remote.returncode or not remote.stdout.strip():
        errors.append("cannot verify current origin/main against remote; fetch and retry")
    else:
        sha = remote.stdout.split()[0]
        if local.stdout.strip() != sha:
            errors.append("origin/main is stale; fetch and rebuild before push")
        errors.extend(_check_commit_range(base))
    return errors


def _github_metadata():
    response = invoke(["gh", "pr", "view", "--json", "assignees,labels,milestone"])
    if response.returncode:
        raise OSError("GitHub PR metadata cannot be verified")
    result = json.loads(response.stdout)
    if not isinstance(result, dict) or not isinstance(result.get("labels"), list):
        raise ValueError("GitHub returned invalid PR metadata")
    return result


def _publication_metadata():
    """Only use draft metadata when an exact, verified branch has no PR."""
    branch = git("branch", "--show-current")
    if branch.returncode or not branch.stdout.strip():
        raise OSError("cannot determine PR source branch")
    current = branch.stdout.strip()
    remote = git("remote", "get-url", "origin")
    if remote.returncode:
        raise OSError("cannot determine origin repository")
    match = re.fullmatch(
        r"(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)(?:\.git)?",
        remote.stdout.strip())
    if not match:
        raise ValueError("unrecognized origin repository")
    repository = match.group(1).removesuffix(".git")
    response = invoke(["gh", "pr", "list", "--repo", repository, "--state", "all",
                       "--head", current, "--json", "number,state,headRefName,headRefOid"])
    if response.returncode:
        raise OSError("cannot verify existing GitHub PRs for this branch")
    prs = json.loads(response.stdout)
    if not isinstance(prs, list) or any(not isinstance(pr, dict) for pr in prs):
        raise ValueError("invalid GitHub PR lookup")
    if not prs:
        return None
    if len(prs) != 1 or prs[0].get("state") != "OPEN" or prs[0].get("headRefName") != current:
        raise ValueError("ambiguous, closed, or mismatched PR; publication refused")
    return _github_metadata()


def check(base, body_file=None, pushed_ref=None, pushed_oid=None, metadata=None):
    try:
        return _check(base, body_file, pushed_ref, pushed_oid, metadata)
    except (OSError, ValueError, subprocess.TimeoutExpired) as exc:
        return [f"Git operation failed: {exc}"]


def _check(base, body_file, pushed_ref, pushed_oid, metadata=None):
    errors = _check_history(base, pushed_ref, pushed_oid)
    status = git("status", "--porcelain")
    if status.returncode:
        errors.append("cannot read worktree status")
    elif status.stdout.strip():
        errors.append("worktree is dirty; test and push exactly the committed contents")
    if not body_file:
        errors.append("PR body draft required: provide --body-file")
    else:
        path = Path(body_file)
        if not path.is_file():
            errors.append("PR body file missing")
        else:
            errors.extend(validate_pr_body(path.read_text(encoding="utf-8"), metadata))
    remote = git("ls-remote", "--heads", "origin", "main")
    local = git("rev-parse", base)
    if remote.returncode or local.returncode or not remote.stdout.strip() or local.stdout.strip() != remote.stdout.split()[0]:
        errors.append("origin/main changed or cannot be verified at end of preflight")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--body-file")
    parser.add_argument("--push-ref")
    parser.add_argument("--push-oid")
    parser.add_argument("--initial-push", action="store_true",
                        help="New remote branch without a PR: validate completed draft metadata instead")
    args = parser.parse_args()
    if not args.check:
        parser.error("specify --check")
    if args.initial_push and (not args.push_ref or not args.push_oid):
        print("PR PREFLIGHT FAIL: initial push requires an exact branch ref and object", file=sys.stderr)
        return 1
    try:
        metadata = _publication_metadata()
    except (OSError, ValueError, subprocess.TimeoutExpired) as exc:
        print(f"PR PREFLIGHT FAIL: unable to verify GitHub PR state: {exc}", file=sys.stderr)
        return 1
    errors = check(args.base, args.body_file, args.push_ref, args.push_oid, metadata)
    for error in errors:
        print(f"PR PREFLIGHT FAIL: {error}", file=sys.stderr)
    if errors:
        return 1
    print("PR preflight OK: current main, ancestry, commits and clean worktree")
    return 0


if __name__ == "__main__":
    sys.exit(main())
