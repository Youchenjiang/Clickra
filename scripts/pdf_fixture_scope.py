#!/usr/bin/env python3
"""Identify changes requiring the public PDF translation E2E regression suite."""
import argparse
import sys

from process_guard import invoke


TRANSLATION_PREFIXES = (
    "src/Clickra.Core/Modules/PdfTranslation/",
    "src/Clickra.Core/Processors/PdfTranslation/",
    "tests/PdfRegression/",
)
TRANSLATION_FILES = (
    "src/Clickra.Core/Processors/FileProcessing/PdfTranslateProcessor.cs",
    "tests/Clickra.Core.Tests/TestSuite.Translation.cs",
    "scripts/fetch_public_pdf_fixtures.py",
    "scripts/pdf_fixture_scope.py",
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
        if args.base != "origin/main":
            raise ValueError("unsupported PDF scope base")
        result = invoke(["git", "diff", "--name-only", "origin/main...HEAD"])
        if result.returncode:
            raise OSError("Git diff failed")
        changed = result.stdout.splitlines()
    except (OSError, ValueError) as exc:
        print(f"Cannot determine PDF test scope: {exc}", file=sys.stderr)
        return 2
    required = requires_fixtures(changed)
    print("required" if required else "supplementary")
    return 0 if required else 1


if __name__ == "__main__":
    sys.exit(main())
