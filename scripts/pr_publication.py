"""Explicitly authorized, single-branch Git push; never bypass the versioned hook."""
import os
import re
import subprocess
from pathlib import Path
from process_guard import executable, invoke
from branch_policy import validate_branch_name

BRANCH = re.compile(r"(?!main\Z|master\Z)[A-Za-z0-9][A-Za-z0-9._/-]*\Z")
SHA = re.compile(r"[0-9a-f]{40}\Z")


def remote_head(branch):
    if not BRANCH.fullmatch(branch) or ".." in branch or "//" in branch:
        raise ValueError("invalid remote branch")
    result = subprocess.run(
        [executable("git"), "ls-remote", "origin", f"refs/heads/{branch}"],
        capture_output=True, text=True, check=False, shell=False, timeout=30)
    fields = result.stdout.split()
    if result.returncode or len(fields) != 2 or fields[1] != f"refs/heads/{branch}" or not SHA.fullmatch(fields[0]):
        raise RuntimeError("remote branch SHA cannot be verified")
    return fields[0]


def _validate_push_branch(branch):
    if (not BRANCH.fullmatch(branch) or branch.endswith(".lock")
            or ".." in branch or "//" in branch or "/." in branch):
        raise ValueError("invalid or protected branch")
    if problems := validate_branch_name(branch):
        raise ValueError("; ".join(problems))


def push(body_file, authorization, *, execute=False, runner=subprocess.run):
    """Run one exact non-forced push after approval; preview makes no writes."""
    body = Path(body_file).resolve()
    if not body.is_file():
        raise ValueError("PR body file missing")
    branch = invoke(["git", "branch", "--show-current"]).stdout.strip()
    sha = invoke(["git", "rev-parse", "HEAD"]).stdout.strip()
    _validate_push_branch(branch)
    if not SHA.fullmatch(sha):
        raise ValueError("invalid current commit")
    status = invoke(["git", "status", "--porcelain"])
    if status.returncode or status.stdout.strip():
        raise RuntimeError("worktree must be clean before publication")
    remote = invoke(["git", "remote", "get-url", "origin"]).stdout.strip()
    if not re.fullmatch(r"(?:https://github\.com/|git@github\.com:)[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:\.git)?", remote):
        raise ValueError("unexpected origin URL")
    expected = f"{branch}:{sha}"
    if not execute:
        return {"preview": expected}
    if authorization != expected:
        raise ValueError("exact branch:SHA approval required; preview first")
    env = dict(os.environ, CLICKRA_PR_BODY_FILE=str(body))
    # Force the repository's versioned hook for this push, even if a clone has
    # an unrelated global/user hooksPath configuration.
    result = runner([executable("git"), "-c", "core.hooksPath=scripts/hooks", "push", "origin",
                     f"refs/heads/{branch}:refs/heads/{branch}"],
                    env=env, check=False, shell=False)
    if result.returncode:
        raise RuntimeError(f"push failed ({result.returncode})")
    # A successful process is not proof that the remote now points at this commit.
    if remote_head(branch) != sha:
        raise RuntimeError("push outcome unconfirmed: remote SHA mismatch")
    return {"pushed": expected}
