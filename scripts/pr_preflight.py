#!/usr/bin/env python3
"""Fail-closed Git publication preflight; no branch or remote mutations."""
import argparse
import re
import subprocess
import sys
import shutil
from pathlib import Path

ALLOWED_TYPES = "feat|fix|refactor|docs|test|chore|style|perf|security"
ALLOWED_SCOPES = "cli|core|shell|msix|docs|ci|deps|store|agent"
SUBJECT = re.compile(rf"^(?:{ALLOWED_TYPES})(?:\((?:{ALLOWED_SCOPES})\))?: [a-z0-9].*$")
BODY = re.compile(r"(?m)^\s*1[.)]\s+.+$")
REQUIRED_SECTIONS = ("## Summary", "## Changes", "## Validation", "## PR Metadata")
REQUIRED_METADATA = ("Assignee", "Label", "Milestone", "External provider")
PLACEHOLDER = re.compile(r"(?i)\b(?:todo|tbd|placeholder|describe the|assign the|add an appropriate|choose the|not run: full pre-push)\b")
VAGUE = {"update", "misc", "stuff", "changes", "fix bug", "bug fix"}
GIT_TIMEOUT = 30


def git(*args):
    executable = shutil.which("git")
    if not executable:
        raise OSError("Git executable not found")
    return subprocess.run([executable, *args], text=True, capture_output=True,
                          check=False, timeout=GIT_TIMEOUT)


def validate_message(message):
    lines = message.splitlines()
    header = lines[0].strip() if lines else ""
    errors = []
    if (not SUBJECT.fullmatch(header) or header.endswith(".") or len(header) > 72
            or header.split(": ", 1)[-1].lower() in VAGUE):
        errors.append("invalid conventional commit header")
    if not BODY.search("\n".join(line for line in lines[1:] if not line.startswith("#"))):
        errors.append("missing numbered English commit body")
    return errors


def validate_pr_body(body):
    errors = []
    sections = {}
    current = None
    body = re.sub(r"<!--.*?-->", "", body, flags=re.DOTALL)
    for line in body.splitlines():
        if line.startswith("## "):
            current = line.strip()
            sections.setdefault(current, [])
        elif current is not None:
            sections[current].append(line)
    for heading in REQUIRED_SECTIONS:
        if heading not in sections:
            errors.append(heading)
            continue
        content = "\n".join(sections[heading]).strip()
        if not content or (heading != "## PR Metadata" and PLACEHOLDER.search(content)):
            errors.append(f"empty or placeholder {heading}")
    for label in REQUIRED_METADATA:
        pattern = rf"(?mi)^- \[[xX]\] \*\*{re.escape(label)}\*\*:\s*(\S.*)$"
        match = re.search(pattern, "\n".join(sections.get("## PR Metadata", [])))
        if (not match or PLACEHOLDER.search(match.group(1))
                or (label == "Milestone" and match.group(1).strip().lower() in {"none", "n/a", "not applicable"}
                    and not re.search(r"(?mi)^- \[[xX]\] \*\*Label\*\*:\s*.*\b(?:release|hotfix|deps|dependencies|docs)\b", "\n".join(sections.get("## PR Metadata", []))))):
            errors.append(f"completed {label} metadata required")
    return errors


def _check_pushed_ref(pushed_ref, pushed_oid):
    errors = []
    branch = git("branch", "--show-current").stdout.strip()
    if not branch or branch in ("main", "master"):
        errors.append("push must come from a named non-main development branch")
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


def check(base, body_file=None, pushed_ref=None, pushed_oid=None):
    try:
        return _check(base, body_file, pushed_ref, pushed_oid)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return [f"Git operation failed: {exc}"]


def _check(base, body_file, pushed_ref, pushed_oid):
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
            errors.extend(validate_pr_body(path.read_text(encoding="utf-8")))
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
    args = parser.parse_args()
    if not args.check:
        parser.error("specify --check")
    errors = check(args.base, args.body_file, args.push_ref, args.push_oid)
    for error in errors:
        print(f"PR PREFLIGHT FAIL: {error}", file=sys.stderr)
    if errors:
        return 1
    print("PR preflight OK: current main, ancestry, commits and clean worktree")
    return 0


if __name__ == "__main__":
    sys.exit(main())
