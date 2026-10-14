#!/usr/bin/env python3
"""Identify changes requiring the public PDF translation E2E regression suite."""
import argparse
import subprocess
import sys


TRANSLATION_PREFIXES = (
    "src/Clickra.Core/Processors/PdfTranslation/",
    "tests/PdfRegression/",
)
TRANSLATION_FILES = (
    "src/Clickra.Core/Processors/FileProcessing/PdfTranslateProcessor.cs",
    "tests/Clickra.Core.Tests/TestSuite.Translation.cs",
    "src/Clickra.Core/FileProcessor.cs",
)


def requires_fixtures(paths):
    return any(
        path.replace("\\", "/").startswith(TRANSLATION_PREFIXES)
        or path.replace("\\", "/") in TRANSLATION_FILES
        for path in paths
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="origin/main")
    args = parser.parse_args()
    try:
        changed = subprocess.check_output(
            ["git", "diff", "--name-only", f"{args.base}...HEAD"], text=True
        ).splitlines()
    except (subprocess.CalledProcessError, OSError) as exc:
        print(f"Cannot determine PDF test scope: {exc}", file=sys.stderr)
        return 2
    required = requires_fixtures(changed)
    print("required" if required else "supplementary")
    return 0 if required else 1


if __name__ == "__main__":
    sys.exit(main())
