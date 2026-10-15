"""Strict executable and argv validation for governance subprocesses.

All commands use argv arrays with shell disabled. This protects against shell
metacharacter interpretation; it is not a substitute for per-command authorization.
"""
from pathlib import Path
import shutil
import subprocess
import re

MAIN_REF = "origin/main"
GIT_READ_OPERATIONS = {
    ("branch", "--show-current"),
    ("remote", "get-url", "origin"),
    ("rev-parse", "HEAD"),
    ("rev-parse", MAIN_REF),
    ("ls-remote", "--heads", "origin", "main"),
    ("merge-base", "--is-ancestor", MAIN_REF, "HEAD"),
    ("log", "--format=%B%x00", f"{MAIN_REF}..HEAD"),
    ("diff", "--quiet", MAIN_REF, "HEAD"),
    ("diff", "--name-only", f"{MAIN_REF}...HEAD"),
    ("status", "--porcelain"),
}
GH_READ_VERBS = {("pr", "view"), ("pr", "checks"), ("pr", "list"),
                 ("run", "view"), ("run", "list"), ("workflow", "list")}
REST_READ_PATH = re.compile(r"^repos/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(?:"
                            r"commits/[0-9a-fA-F]{40}/check-runs\?per_page=100|"
                            r"(?:issues|pulls)/[1-9][0-9]*/(?:comments|reviews)\?per_page=100&page=[1-9][0-9]*"
                            r")$")


def executable(name):
    if name not in {"git", "gh"}:
        raise ValueError("unsupported governance executable")
    path = shutil.which(name)
    if not path:
        raise OSError(f"{name} executable not found")
    resolved = Path(path).resolve()
    if not resolved.is_file():
        raise OSError(f"invalid {name} executable")
    return str(resolved)


def _validate_github_args(argv):
    if len(argv) < 3:
        raise ValueError("unauthorized GitHub operation")
    command = tuple(argv[1:3])
    if command in GH_READ_VERBS or command == ("api", "graphql"):
        return
    if argv[1] == "api" and len(argv) == 3 and REST_READ_PATH.fullmatch(argv[2]):
        return
    raise ValueError("unauthorized GitHub operation")


def _validate_argv(argv):
    if not argv or argv[0] not in {"git", "gh"}:
        raise ValueError("only git and gh are allowed")
    if any(not isinstance(a, str) or "\x00" in a or "\n" in a or "\r" in a for a in argv):
        raise ValueError("invalid subprocess argument")
    if argv[0] == "git" and tuple(argv[1:]) not in GIT_READ_OPERATIONS:
        raise ValueError("unauthorized git operation")
    if argv[0] == "gh":
        _validate_github_args(argv)


def invoke(argv, *, capture=True, env=None, timeout=30):
    """Run a previously authorized git/gh command using an absolute executable."""
    _validate_argv(argv)
    if env is not None and not isinstance(env, dict):
        raise ValueError("invalid environment")
    return subprocess.run([executable(argv[0]), *argv[1:]], text=True,
                          capture_output=capture, env=env, timeout=timeout,
                          check=False, shell=False)
