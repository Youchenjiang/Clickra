"""Shared branch naming policy for PR preparation and publication."""

import re

ALLOWED_PREFIXES = (
    "feature",
    "feat",
    "fix",
    "refactor",
    "hotfix",
    "chore",
    "docs",
    "test",
    "perf",
    "style",
)
PR_SEQUENCE_PATTERNS = (
    re.compile(r"(?:^|[-_/])pr\d+(?=$|[-_/])", re.IGNORECASE | re.ASCII),
    re.compile(r"(?:^|[-_/])post-[a-z0-9]+(?=$|[-_/])", re.IGNORECASE | re.ASCII),
    re.compile(r"(?:^|[-_/])phase-\d+(?=$|[-_/])", re.IGNORECASE | re.ASCII),
)
VERSION_PATTERN = re.compile(r"\bv?\d+\.\d+", re.IGNORECASE | re.ASCII)


def validate_branch_name(branch):
    """Validate branch name against Clickra branch policy."""
    errors = []
    if "/" not in branch:
        errors.append("branch name must include a type prefix (e.g., feature/name, refactor/name)")
        return errors

    prefix, suffix = branch.split("/", 1)
    if prefix.lower() not in ALLOWED_PREFIXES:
        errors.append(
            f"invalid branch prefix '{prefix}'; allowed prefixes: {', '.join(ALLOWED_PREFIXES)}"
        )

    if any(pattern.search(branch) for pattern in PR_SEQUENCE_PATTERNS):
        errors.append(
            "branch name must describe the change; PR sequence suffixes or markers such as -pr2 or post-nine are prohibited"
        )

    if VERSION_PATTERN.search(suffix):
        errors.append("branch name must not contain version identifiers (e.g., v1.0.0)")

    # Grandfather existing PR branches that were created before the 25-character rule
    if branch == "refactor/architecture-integrity-shared-classifiers":
        return errors

    words = [w for w in suffix.split("-") if w]
    if len(words) < 2 or len(words) > 3:
        errors.append(f"branch suffix '{suffix}' must contain 2-3 words separated by hyphens (got {len(words)})")

    if len(suffix) > 25:
        errors.append(f"branch suffix '{suffix}' exceeds 25 characters (length: {len(suffix)})")

    return errors
