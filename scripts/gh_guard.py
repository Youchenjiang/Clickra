#!/usr/bin/env python3
"""Restricted gh entry point for PR inspection; GitHub writes are denied by default.

This is a cooperative wrapper, NOT a sandbox. Agents with direct gh or token
access can bypass it. Do not grant the wrapper credentials as a security boundary.
"""
import argparse
import re
import subprocess
import sys

# Exact read-only command verbs. Unknown options/subcommands fail closed.
SAFE_COMMANDS = {
    ("pr", "view"), ("pr", "checks"), ("pr", "list"),
    ("run", "view"), ("run", "list"),
    ("workflow", "list"),
}
SAFE_FLAGS = {
    "--json", "--jq", "--template", "--repo", "-R", "--limit", "-L",
    "--state", "--head", "--base", "--workflow", "--branch", "--event",
    "--status", "--commit", "--log", "--log-failed", "--attempt",
}
BOOL_FLAGS = {"--log", "--log-failed"}
POSITIONAL = re.compile(r"^[a-zA-Z0-9_.#/:-]+$")


def validate_gh_args(args):
    if len(args) < 2 or tuple(args[:2]) not in SAFE_COMMANDS:
        return "only allowlisted read-only gh commands are permitted"
    seen_positionals = 0
    i = 2
    while i < len(args):
        item = args[i]
        if item in BOOL_FLAGS:
            i += 1
            continue
        flag = item.split("=", 1)[0]
        if flag in SAFE_FLAGS:
            if "=" not in item:
                if i + 1 >= len(args) or args[i + 1].startswith("-"):
                    return f"missing value for {item}"
                i += 1
            i += 1
            continue
        if item.startswith("-"):
            return f"unrecognized or unsafe option: {item}"
        if not POSITIONAL.fullmatch(item) or seen_positionals:
            return "unexpected argument or second positional argument"
        seen_positionals += 1
        i += 1
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("gh_args", nargs=argparse.REMAINDER)
    options = parser.parse_args()
    error = validate_gh_args(options.gh_args)
    if error:
        print(f"GH GUARD DENIED: {error}", file=sys.stderr)
        return 2
    return subprocess.call(["gh", *options.gh_args])


if __name__ == "__main__":
    sys.exit(main())