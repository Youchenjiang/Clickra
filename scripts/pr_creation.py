"""Explicitly authorized creation of one PR, with duplicate and state checks."""
import hashlib
import json
import re
import subprocess
from pathlib import Path

from process_guard import executable, invoke
from pr_preflight import valid_commit_subject, validate_pr_body
from branch_policy import validate_branch_name
from pr_publication import BRANCH, SHA, remote_head

REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
LABEL = re.compile(r"[A-Za-z0-9_.-]+\Z")
REPO_ROOT = Path(__file__).resolve().parent.parent
MILESTONE_EXEMPT_LABELS = {"release", "hotfix", "deps", "dependencies", "docs", "chore"}


def _read(argv):
    result = invoke(argv)
    if result.returncode:
        raise RuntimeError(f"read-only command failed: {' '.join(argv[:3])}")
    return result.stdout.strip()


def _fetch_github_entries(repo, kind):
    result = subprocess.run(
        [executable("gh"), "api", f"repos/{repo}/{kind}?per_page=100",
         "--paginate", "--slurp"],
        shell=False, check=False, capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError(f"cannot verify repository {kind}; no PR created")
    pages = json.loads(result.stdout)
    if not isinstance(pages, list) or not all(isinstance(page, list) for page in pages):
        raise RuntimeError(f"repository {kind} returned invalid data; no PR created")
    return [record for page in pages for record in page]


def _validate_remote_entries(repo, label, milestone):
    labels = {item.get("name") for item in _fetch_github_entries(repo, "labels")}
    if label not in labels:
        raise ValueError(f"GitHub label does not exist: {label}")
    if milestone:
        milestones = {item.get("title") for item in _fetch_github_entries(repo, "milestones")}
        if milestone not in milestones:
            raise ValueError(f"GitHub milestone does not exist: {milestone}")


def _validate_remote_assignee(repo, assignee):
    if not assignee:
        return
    result = subprocess.run(
        [executable("gh"), "api", f"repos/{repo}/assignees/{assignee}"],
        shell=False, check=False, capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise ValueError("GitHub assignee is not eligible")


def _validate_spec_roadmap_alignment(head=None):
    target = head or "HEAD"
    diff_output = _read(["git", "diff", "-z", "--name-only", "--no-renames", f"origin/main...{target}"])
    changed_files = [p.strip().replace("\\", "/") for p in diff_output.split("\0") if p.strip()]
    from pr_preflight import check_spec_roadmap_alignment
    spec_errors = check_spec_roadmap_alignment(REPO_ROOT, changed_files)
    if spec_errors:
        raise ValueError("; ".join(spec_errors))


def _validate_roadmap_milestone(milestone, head=None):
    if not milestone:
        return
    target = head or "HEAD"
    diff_output = _read(["git", "diff", "-z", "--name-only", "--no-renames", f"origin/main...{target}"])
    changed_files = [p.strip().replace("\\", "/") for p in diff_output.split("\0") if p.strip()]
    from pr_preflight import check_roadmap_alignment
    roadmap_errors = check_roadmap_alignment(REPO_ROOT, milestone, changed_files)
    if roadmap_errors:
        raise ValueError("; ".join(roadmap_errors))


def validate_metadata(repo, label, milestone, assignee, head=None):
    """Validate selected GitHub metadata before exposing an executable preview."""
    _validate_remote_entries(repo, label, milestone)
    _validate_remote_assignee(repo, assignee)
    _validate_spec_roadmap_alignment(head=head)
    _validate_roadmap_milestone(milestone, head=head)


_metadata = validate_metadata


def current_branch(repo, branch):
    if not REPOSITORY.fullmatch(repo) or not BRANCH.fullmatch(branch):
        raise ValueError("invalid repository or development branch")
    if problems := validate_branch_name(branch):
        raise ValueError("; ".join(problems))
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
    existing = json.loads(_read(["gh", "pr", "list", "--state", "open", "--head", branch,
                                 "--repo", repo, "--json", "number"]))
    if not isinstance(existing, list) or existing:
        raise RuntimeError("an existing or ambiguous PR was found for this branch")
    return head


def _validate_title(title):
    if not title or any(ord(c) < 32 for c in title):
        raise ValueError("invalid PR title")
    if not valid_commit_subject(title.strip()):
        raise ValueError(
            "PR title must match <type>(<scope>): <description> or <type>: <description> "
            "with an allowed type/scope and be 72 characters or less."
        )


def _validate_milestone_and_assignee(label, milestone, assignee):
    if not LABEL.fullmatch(label or ""):
        raise ValueError("explicit label required")
    if not milestone and (label or "").lower() not in MILESTONE_EXEMPT_LABELS:
        raise ValueError(
            f"GitHub milestone required for PR with label '{label}'. "
            "Release, hotfix, dependency, and docs PRs are exempt."
        )
    if milestone and (len(milestone) > 100 or any(ord(c) < 32 for c in milestone)):
        raise ValueError("invalid milestone")
    if assignee and not LABEL.fullmatch(assignee):
        raise ValueError("invalid assignee")


def _read_body_file(body_file):
    body = Path(body_file).resolve()
    if not body.is_file():
        raise ValueError("finalized PR body file missing")
    data = body.read_text(encoding="utf-8")
    problems = validate_pr_body(data)
    if problems:
        raise ValueError(f"incomplete PR body: {', '.join(problems)}")
    return body, data


def validate_creation_inputs(title, body_file, label, milestone, assignee):
    _validate_title(title)
    _validate_milestone_and_assignee(label, milestone, assignee)
    return _read_body_file(body_file)


_requested_details = validate_creation_inputs


def _handle_create_result(result, repo, head):
    if result.returncode:
        message = (result.stderr or "").lower()
        if "milestone" in message:
            raise RuntimeError("GitHub rejected PR milestone; inspect PR list before retry")
        if "label" in message:
            raise RuntimeError("GitHub rejected PR label; inspect PR list before retry")
        raise RuntimeError("PR create failed or outcome unknown; inspect PR list before retry")
    created_url = (result.stdout or "").strip()
    match = re.fullmatch(rf"https://github\.com/{re.escape(repo)}/pull/([1-9][0-9]*)", created_url)
    if not match:
        raise RuntimeError("PR create returned no trusted PR number; inspect remote list before retry")
    details = json.loads(_read(["gh", "pr", "view", match.group(1), "--repo", repo,
                                "--json", "number,headRefOid,baseRefName,url"]))
    if details.get("headRefOid") != head or details.get("baseRefName") != "main":
        raise RuntimeError("created PR state differs from authorized commit; stop for review")
    return {"created": details.get("url"), "head": head, "number": details.get("number")}


def create(repo, branch, *, title, body_file, label, milestone=None, assignee=None,
           execute=False, authorization=None, runner=subprocess.run):
    """Preview first; one authorized create attempt, then independently re-read."""
    body, data = validate_creation_inputs(title, body_file, label, milestone, assignee)
    head = current_branch(repo, branch)
    validate_metadata(repo, label, milestone, assignee, head=head)
    fingerprint = hashlib.sha256(
        json.dumps([title, data, label, milestone, assignee], ensure_ascii=False).encode("utf-8")
    ).hexdigest()[:16]
    approval = f"{repo}:{branch}:{head}:{fingerprint}"
    if not execute:
        return {"preview": approval}
    if authorization != approval:
        raise ValueError("exact PR creation authorization required")
    current_head = current_branch(repo, branch)
    if current_head != head:
        raise RuntimeError("HEAD has changed since authorization; PR creation rejected")
    args = [executable("gh"), "pr", "create", "--repo", repo, "--base", "main",
            "--head", branch, "--title", title, "--body-file", str(body), "--label", label]
    if milestone:
        args.extend(["--milestone", milestone])
    if assignee:
        args.extend(["--assignee", assignee])
    try:
        result = runner(args, shell=False, check=False, capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuntimeError("PR create outcome unknown; inspect PR list before any retry") from exc
    return _handle_create_result(result, repo, head)
