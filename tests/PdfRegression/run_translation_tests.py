"""Single entry point for Clickra's offline translation test suite."""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

from pdf_test_common import ROOT, SOURCE_DIR, SOURCE_MAP, TRANSLATED_DIR, TRANSLATED_MAP


def missing_pdf_fixtures() -> list[str]:
    missing: list[str] = []
    for source_name in SOURCE_MAP.values():
        path = SOURCE_DIR / source_name
        if not path.is_file():
            missing.append(str(path.relative_to(ROOT)))
    for translated_name in TRANSLATED_MAP.values():
        path = TRANSLATED_DIR / translated_name
        if not path.is_file():
            missing.append(str(path.relative_to(ROOT)))
    return missing


def run(name: str, command: list[str], env: dict[str, str] | None = None) -> bool:
    print(f"\n{'=' * 60}\n{name}\n{'=' * 60}", flush=True)
    completed = subprocess.run(
        command,
        cwd=ROOT,
        env=env,
        check=False,
        capture_output=True,
        text=True,
        errors="replace",
    )
    if completed.returncode:
        if completed.stdout:
            print(completed.stdout.rstrip())
        if completed.stderr:
            print(completed.stderr.rstrip(), file=sys.stderr)
        print(f"FAIL: {name} exited with {completed.returncode}")
        return False

    summary_prefixes = ("PASS", "WARN", "Warnings:", "  - ")
    summary = [
        line
        for line in completed.stdout.splitlines()
        if line.startswith(summary_prefixes)
    ]
    if summary:
        print("\n".join(summary))
    if completed.stderr:
        diagnostic = [
            line
            for line in completed.stderr.splitlines()
            if line.startswith(("[Translate]", "WARN", "FAIL"))
        ]
        if diagnostic:
            print("\n".join(diagnostic), file=sys.stderr)
    print(f"PASS: {name}")
    return True


def _check_fixtures(args: argparse.Namespace) -> int | None:
    missing = missing_pdf_fixtures()
    if not missing:
        return None
    print("SKIP: PDF regression fixtures are not available.")
    print("Expected fixture roots:")
    print(f"  - {SOURCE_DIR.relative_to(ROOT)}")
    print(f"  - {TRANSLATED_DIR.relative_to(ROOT)}")
    print("Missing examples:")
    for path in missing[:8]:
        print(f"  - {path}")
    if len(missing) > 8:
        print(f"  - ... and {len(missing) - 8} more")
    print("Provide fixtures locally, or run with --require-fixtures to enforce them.")
    return 1 if args.require_fixtures else 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--strict-render",
        action="store_true",
        help="treat mask/text geometry warnings as failures",
    )
    parser.add_argument(
        "--provider-smoke",
        action="store_true",
        help="include the networked MyMemory smoke test",
    )
    parser.add_argument(
        "--identity-e2e",
        action="store_true",
        help="run the offline 16-page identity translation pipeline",
    )
    parser.add_argument(
        "--real-e2e",
        action="store_true",
        help="run the live-provider 16-page translation pipeline",
    )
    parser.add_argument(
        "--legacy-diagnostics",
        action="store_true",
        help="run noisy legacy geometry/rules scripts (may report known false positives)",
    )
    parser.add_argument(
        "--require-fixtures",
        action="store_true",
        help="fail instead of skipping when test_pdfs fixtures are missing",
    )
    parser.add_argument(
        "--aster-e2e",
        action="store_true",
        help="run the local ASTER fixture gate without requiring test_pdfs fixtures",
    )
    args = parser.parse_args()

    if args.aster_e2e:
        return 0 if run(
            "ASTER PDF E2E",
            [sys.executable, "tests/PdfRegression/test_aster_e2e.py", "--engine", "synthetic-cjk"],
            None,
        ) else 1

    fixture_res = _check_fixtures(args)
    if fixture_res is not None:
        return fixture_res

    test_dll = (
        ROOT
        / "tests"
        / "Clickra.Core.Tests"
        / "bin"
        / "Debug"
        / "net10.0-windows"
        / "Clickra.Core.Tests.dll"
    )
    if not test_dll.is_file():
        print(f"FAIL: native test binary missing: {test_dll}")
        return 1

    env = os.environ.copy()
    if args.provider_smoke:
        env["CLICKRA_RUN_TRANSLATION_SMOKE"] = "1"

    checks = [
        (
            "Native translation contracts",
            ["dotnet", str(test_dll)],
            env,
        ),
        (
            "Generate translation health",
            [sys.executable, "tests/PdfRegression/generate_translation_health.py"],
            None,
        ),
        (
            "Translation health",
            [sys.executable, "tests/PdfRegression/test_translation_health.py"],
            None,
        ),
        (
            "Health heuristic contracts",
            [sys.executable, "tests/PdfRegression/test_health_heuristics.py"],
            None,
        ),
        (
            "Layout occupancy heuristic contracts",
            [sys.executable, "tests/PdfRegression/test_layout_occupancy_heuristics.py"],
            None,
        ),
        (
            "Translation output quality",
            [sys.executable, "tests/PdfRegression/test_translation_output_quality.py"],
            None,
        ),
        (
            "Source/translated layout occupancy",
            [sys.executable, "tests/PdfRegression/test_translation_layout_occupancy.py"],
            None,
        ),
        (
            "Render review",
            [
                sys.executable,
                "tests/PdfRegression/test_translation_render_review.py",
                *(["--strict"] if args.strict_render else []),
            ],
            None,
        ),
    ]
    if args.identity_e2e:
        checks.append(
            (
                "Identity translation E2E",
                [sys.executable, "tests/PdfRegression/test_translation_e2e.py", "--engine", "identity"],
                None,
            )
        )
    if args.real_e2e:
        checks.append(
            (
                "Real-provider translation E2E",
                [sys.executable, "tests/PdfRegression/test_translation_e2e.py", "--engine", "real"],
                None,
            )
        )
    if args.legacy_diagnostics:
        checks.extend(
            [
                (
                    "Legacy translation rules diagnostics",
                    [sys.executable, "tests/PdfRegression/test_translation_rules.py"],
                    None,
                ),
                (
                    "Legacy mask coverage diagnostics",
                    [sys.executable, "tests/PdfRegression/test_mask_coverage.py"],
                    None,
                ),
            ]
        )

    failed = [
        name
        for name, command, command_env in checks
        if not run(name, command, command_env)
    ]
    if failed:
        print("\nFailed suites:")
        for name in failed:
            print(f"  - {name}")
        return 1
    print("\nPASS: all offline translation suites")
    return 0


if __name__ == "__main__":
    sys.exit(main())

