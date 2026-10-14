"""Shared branch naming policy for PR preparation and publication."""

import re

PR_SEQUENCE_SUFFIX = re.compile(r"(?:^|[-_/])pr[0-9]+$", re.IGNORECASE)


def validate_branch_name(branch):
    """Reject workflow sequence suffixes without banning meaningful digits."""
    if PR_SEQUENCE_SUFFIX.search(branch):
        return ["branch name must describe the change; PR sequence suffixes such as -pr2 are prohibited"]
    return []
