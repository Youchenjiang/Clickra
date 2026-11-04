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
REPO_ROOT = Path(__file__).resolve().parent.parent
MILESTONE_EXEMPT_LABELS = {"release", "hotfix", "deps", "dependencies", "docs", "chore"}


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


REQUIRED_REVIEW_PROMPTS = (
    "Behavior",
    "Regression",
    "Compatibility",
    "Security",
    "Documentation",
)
CHECKLIST_METADATA_PATTERN = re.compile(r"(?mi)^- \[[ x]\] \*\*(?:Assignee|Label|Milestone)\*\*:")


def _checklist_metadata_errors(lines):
    if CHECKLIST_METADATA_PATTERN.search(lines):
        return [
            "assignees, labels and milestones must not be duplicated "
            "as PR-body checkboxes; verify via GitHub metadata"
        ]
    return []


def _checklist_prompt_errors(lines):
    errors = []
    for prompt in REQUIRED_REVIEW_PROMPTS:
        pattern = rf"(?mi)^- \[[ x]\] \*\*{re.escape(prompt)}\*\*:[ \t]*(\S[^\r\n]*)$"
        match = re.search(pattern, lines)
        if not match:
            errors.append(f"missing review checklist prompt: {prompt}")
        elif PLACEHOLDER.search(match.group(1)):
            errors.append(f"placeholder in review checklist prompt: {prompt}")
    return errors


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
    if not metadata.get("milestone") and not labels.intersection(MILESTONE_EXEMPT_LABELS):
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
    checklist_lines = "\n".join(sections.get(CHECKLIST_SECTION, []))
    errors.extend(_checklist_metadata_errors(checklist_lines))
    errors.extend(_checklist_prompt_errors(checklist_lines))
    errors.extend(_provider_acknowledgement_errors(checklist_lines))
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
    response = invoke(["gh", "pr", "list", "--repo", repository, "--state", "open",
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


def _tag_is_released(tag_name):
    local_tags = git("tag", "-l")
    if local_tags.returncode == 0:
        existing = {line.strip() for line in local_tags.stdout.splitlines() if line.strip()}
        if tag_name in existing:
            return True
    remote_tags = git("ls-remote", "--tags", "origin")
    if remote_tags.returncode == 0:
        for line in remote_tags.stdout.splitlines():
            parts = line.strip().split()
            if len(parts) >= 2 and parts[1] == f"refs/tags/{tag_name}":
                return True
    return False


def _check_manifest_versions(repo_root, version):
    errors = []
    manifest_paths = (
        repo_root / "packaging" / "msix" / "AppxManifest.xml",
        repo_root / "packaging" / "msix" / "AppxManifest.Fluent.xml",
        repo_root / "src" / "resources" / "AppxManifest.xml",
    )
    for mpath in manifest_paths:
        if not mpath.is_file():
            continue
        text_m = mpath.read_text(encoding="utf-8")
        m = re.search(r'Version="([^"]+)"', text_m)
        if not m:
            errors.append(f"{mpath.relative_to(repo_root)} does not declare Version attribute")
        elif m.group(1).strip() != version:
            errors.append(
                f"{mpath.relative_to(repo_root)} declares Version=\"{m.group(1).strip()}\", expected \"{version}\""
            )
    return errors


def _check_changelog_and_listings(repo_root, version, tag_name):
    errors = []
    changelog_path = repo_root / "CHANGELOG.md"
    if changelog_path.is_file():
        text_c = changelog_path.read_text(encoding="utf-8")
        if f"[{tag_name}]" not in text_c and f"[{version}]" not in text_c:
            errors.append(f"CHANGELOG.md missing header for version {tag_name} (expected '## [{tag_name}]')")

    store_listings = (
        repo_root / "docs" / "store" / "StoreListing_EN.md",
        repo_root / "docs" / "store" / "StoreListing_ZH.md",
        repo_root / "docs" / "store" / "StoreListing_JA.md",
        repo_root / "docs" / "store" / "StoreListing_KO.md",
        repo_root / "docs" / "store" / "StoreListing_ZH-CN.md",
    )
    for lpath in store_listings:
        if lpath.is_file():
            text_l = lpath.read_text(encoding="utf-8")
            if tag_name not in text_l and version not in text_l:
                errors.append(f"{lpath.relative_to(repo_root)} does not reference version {tag_name}")
    return errors


def _resolve_branch_name(pushed_ref=None):
    if pushed_ref:
        return pushed_ref.replace("refs/heads/", "").strip()
    branch_proc = git("branch", "--show-current")
    if branch_proc.returncode == 0:
        return branch_proc.stdout.strip()
    return ""


def _check_prefix_intent(prefix, props_changed):
    non_release_prefixes = ("chore", "docs", "test", "style")
    if prefix in non_release_prefixes and props_changed:
        return [
            f"Branch prefix '{prefix}/' is reserved for non-release maintenance. "
            f"Version bumps require an appropriate release-oriented branch: 'hotfix/', 'fix/', or 'feat/'."
        ]
    return []


RELEASE_FILE_PREFIXES = (
    "src/",
    "packaging/",
    ".github/workflows/release",
    "scripts/build_",
)


def _check_release_impact(changed_files, branch_name, labels):
    file_hit = any(f.startswith(RELEASE_FILE_PREFIXES) for f in changed_files)
    branch_hit = branch_name.startswith("hotfix/")
    label_hit = "hotfix" in labels
    reasons = []
    if file_hit:
        reasons.append("release-affecting files")
    if branch_hit:
        reasons.append(f"branch prefix '{branch_name.split('/')[0]}/'")
    if label_hit:
        reasons.append("release-related label")
    return bool(reasons), reasons


def _get_declared_version(repo_root):
    props_path = repo_root / "src" / "Directory.Build.props"
    if not props_path.is_file():
        return None, ["src/Directory.Build.props missing for version check"]
    content = props_path.read_text(encoding="utf-8")
    match = re.search(r"<Version>(.*?)</Version>", content)
    if not match:
        return None, ["src/Directory.Build.props does not declare <Version>"]
    return match.group(1).strip(), []


def _check_version_bump(base, metadata=None, pushed_ref=None):
    diff = git("diff", "--name-only", f"{base}...HEAD")
    if diff.returncode != 0:
        return ["cannot determine changed files against base for version bump check"]
    changed_files = [line.strip().replace("\\", "/") for line in diff.stdout.splitlines() if line.strip()]

    branch_name = _resolve_branch_name(pushed_ref)
    prefix = branch_name.split("/")[0] if "/" in branch_name else ""
    props_changed = "src/Directory.Build.props" in changed_files

    intent_errors = _check_prefix_intent(prefix, props_changed)
    if intent_errors:
        return intent_errors

    labels = {item.get("name", "").lower() for item in metadata.get("labels", [])} if metadata else set()
    if "no-release" in labels:
        return []

    affects_release, reasons = _check_release_impact(changed_files, branch_name, labels)
    if not affects_release and not props_changed:
        return []

    repo_root = Path(__file__).resolve().parent.parent
    version, version_errors = _get_declared_version(repo_root)
    if version_errors:
        return version_errors

    tag_name = f"v{version}"
    errors = []
    if affects_release and _tag_is_released(tag_name):
        reason_str = " / ".join(reasons) or "release changes"
        errors.append(
            f"PR affects release ({reason_str}) but version '{version}' is already tagged as an existing release ({tag_name}). "
            f"Bump version in src/Directory.Build.props and associated files using scripts/bump_version.ps1"
        )

    errors.extend(_check_manifest_versions(repo_root, version))
    errors.extend(_check_changelog_and_listings(repo_root, version, tag_name))
    return errors


def extract_milestone_id(milestone):
    """Extract milestone identifier, e.g. '[F2-10]' from '[F2-10] PDF to Image'."""
    if not milestone:
        return None
    match = re.search(r"\[([A-Za-z0-9_.-]+)\]", milestone)
    if match:
        return f"[{match.group(1)}]"
    return milestone.strip()


def check_roadmap_alignment(repo_root, milestone, changed_files):
    """Verify that milestone exists in docs/ROADMAP.md and roadmap status is maintained."""
    if not milestone:
        return []

    roadmap_path = repo_root / "docs" / "ROADMAP.md"
    if not roadmap_path.is_file():
        return ["docs/ROADMAP.md is missing; cannot verify milestone"]

    content = roadmap_path.read_text(encoding="utf-8")
    milestone_id = extract_milestone_id(milestone)

    # 1. Existence check: milestone_id or full milestone must exist in docs/ROADMAP.md
    if milestone_id not in content and milestone not in content:
        return [
            f"Milestone '{milestone}' (id: '{milestone_id}') is not tracked in docs/ROADMAP.md. "
            "All milestones must be registered in the project roadmap."
        ]

    # 2. Check if milestone is marked completed or updated in this PR
    pattern = rf"(?mi)^[ \t]*- \[([ xX])\][ \t]*\*\*{re.escape(milestone_id)}"
    match = re.search(pattern, content)
    if match:
        is_completed = match.group(1).lower() == "x"
        roadmap_changed = "docs/ROADMAP.md" in changed_files
        if not is_completed and not roadmap_changed:
            return [
                f"Milestone '{milestone}' is marked pending (- [ ]) in docs/ROADMAP.md, "
                "but docs/ROADMAP.md was not updated in this PR. "
                "Update docs/ROADMAP.md to record progress or mark completion."
            ]

    return []


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
    errors.extend(_check_version_bump(base, metadata, pushed_ref))

    diff = git("diff", "--name-only", f"{base}...HEAD")
    changed_files = [line.strip().replace("\\", "/") for line in diff.stdout.splitlines() if line.strip()] if diff.returncode == 0 else []
    milestone = None
    if metadata:
        m = metadata.get("milestone")
        if isinstance(m, dict):
            milestone = m.get("title")
        elif isinstance(m, str):
            milestone = m
    if milestone:
        errors.extend(check_roadmap_alignment(REPO_ROOT, milestone, changed_files))

    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--body-file")
    parser.add_argument("--push-ref")
    parser.add_argument("--push-oid")
    parser.add_argument("--milestone", help="Milestone title for draft / push validation")
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
    if metadata is None and args.milestone:
        metadata = {"labels": [], "milestone": {"title": args.milestone}}
    elif metadata and not metadata.get("milestone") and args.milestone:
        metadata["milestone"] = {"title": args.milestone}
    errors = check(args.base, args.body_file, args.push_ref, args.push_oid, metadata)
    for error in errors:
        print(f"PR PREFLIGHT FAIL: {error}", file=sys.stderr)
    if errors:
        return 1
    print("PR preflight OK: current main, ancestry, commits and clean worktree")
    return 0


if __name__ == "__main__":
    sys.exit(main())
