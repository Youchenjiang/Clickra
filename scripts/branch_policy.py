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
PR_SEQUENCE_PATTERN = re.compile(
    r"(?:^|[-_/])(?:pr\d+|post-(?:[a-z]+|\d+)|phase-\d+)(?=$|[-_/])", re.IGNORECASE | re.ASCII
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

    if PR_SEQUENCE_PATTERN.search(branch):
        errors.append(
            "branch name must describe the change; PR sequence suffixes or markers such as -pr2 or post-nine are prohibited"
        )

    if VERSION_PATTERN.search(suffix):
        errors.append("branch name must not contain version identifiers (e.g., v1.0.0)")

    return errors
