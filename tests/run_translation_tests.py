"""Single entry point for Clickra's offline translation test suite."""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

from pdf_test_common import ROOT


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
    args = parser.parse_args()

    test_dll = ROOT / "tests" / "Clickra.Core.Tests" / "bin" / "Debug" / "net10.0" / "Clickra.Core.Tests.dll"
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
            "Translation health",
            [sys.executable, "tests/test_translation_health.py"],
            None,
        ),
        (
            "Health heuristic contracts",
            [sys.executable, "tests/test_health_heuristics.py"],
            None,
        ),
        (
            "Translation output quality",
            [sys.executable, "tests/test_translation_output_quality.py"],
            None,
        ),
        (
            "Render review",
            [
                sys.executable,
                "tests/test_translation_render_review.py",
                *(["--strict"] if args.strict_render else []),
            ],
            None,
        ),
    ]
    if args.identity_e2e:
        checks.append(
            (
                "Identity translation E2E",
                [sys.executable, "tests/test_translation_e2e.py", "--engine", "identity"],
                None,
            )
        )
    if args.real_e2e:
        checks.append(
            (
                "Real-provider translation E2E",
                [sys.executable, "tests/test_translation_e2e.py", "--engine", "real"],
                None,
            )
        )
    if args.legacy_diagnostics:
        checks.extend(
            [
                (
                    "Legacy translation rules diagnostics",
                    [sys.executable, "tests/test_translation_rules.py"],
                    None,
                ),
                (
                    "Legacy mask coverage diagnostics",
                    [sys.executable, "tests/test_mask_coverage.py"],
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
