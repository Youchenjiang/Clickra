#!/usr/bin/env python3
"""Fail-closed Git publication preflight; no branch or remote mutations."""
import argparse
import re
import subprocess
import sys
from pathlib import Path

ALLOWED_TYPES = "feat|fix|refactor|docs|test|chore|style|perf|security"
ALLOWED_SCOPES = "cli|core|shell|msix|docs|ci|deps|store|agent"
SUBJECT = re.compile(rf"^(?:{ALLOWED_TYPES})(?:\((?:{ALLOWED_SCOPES})\))?: [a-z0-9].*$")
BODY = re.compile(r"(?m)^\s*1[.)]\s+.+$")
REQUIRED_SECTIONS = ("## Summary", "## Changes", "## Validation", "## PR Metadata")


def git(*args):
    return subprocess.run(["git", *args], text=True, capture_output=True, check=False)


def validate_message(message):
    lines = message.splitlines()
    header = lines[0].strip() if lines else ""
    errors = []
    if not SUBJECT.fullmatch(header) or header.endswith(".") or len(header) > 72:
        errors.append("invalid conventional commit header")
    if not BODY.search("\n".join(lines[1:])):
        errors.append("missing numbered English commit body")
    return errors


def validate_pr_body(body):
    missing = [section for section in REQUIRED_SECTIONS if section not in body]
    if not re.search(r"- \[x\] \*\*External provider\*\*:", body, re.I):
        missing.append("checked External provider metadata")
    return missing


def check(base, body_file=None):
    errors = []
    branch = git("branch", "--show-current").stdout.strip()
    if not branch or branch in ("main", "master"):
        errors.append("push must come from a named non-main development branch")
    local = git("rev-parse", base)
    remote = git("ls-remote", "--heads", "origin", "main")
    if local.returncode or remote.returncode or not remote.stdout.strip():
        errors.append("cannot verify current origin/main against remote; fetch and retry")
    else:
        sha = remote.stdout.split()[0]
        if local.stdout.strip() != sha:
            errors.append("origin/main is stale; fetch and rebuild before push")
        if git("merge-base", "--is-ancestor", base, "HEAD").returncode:
            errors.append("branch is not based on latest origin/main; rebuild/rebase and revalidate")
        commits = git("log", "--format=%B%x00", f"{base}..HEAD")
        if commits.returncode:
            errors.append("cannot read commit range")
        else:
            for n, message in enumerate(commits.stdout.split("\0"), 1):
                if message.strip():
                    errors.extend(f"commit {n}: {e}" for e in validate_message(message.strip()))
        if git("diff", "--quiet", base, "HEAD").returncode == 0:
            errors.append("PR branch has no changes against latest origin/main")
    if git("status", "--porcelain").stdout.strip():
        errors.append("worktree is dirty; test and push exactly the committed contents")
    if not body_file:
        errors.append("PR body draft required: provide --body-file")
    else:
        path = Path(body_file)
        if not path.is_file():
            errors.append("PR body file missing")
        else:
            errors.extend(validate_pr_body(path.read_text(encoding="utf-8")))
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--body-file")
    args = parser.parse_args()
    if not args.check:
        parser.error("specify --check")
    errors = check(args.base, args.body_file)
    for error in errors:
        print(f"PR PREFLIGHT FAIL: {error}", file=sys.stderr)
    if errors:
        return 1
    print("PR preflight OK: current main, ancestry, commits and clean worktree")
    return 0


if __name__ == "__main__":
    sys.exit(main())
