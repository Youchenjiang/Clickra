"""Deterministic ASTER PDF regression gate for the local user fixture."""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

import fitz
from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ASTER .pdf"
CLI = ROOT / "src" / "Clickra.CLI" / "bin" / "Debug" / "net10.0-windows" / "Clickra.dll"
DIAGNOSTICS = ROOT / "tools" / "pdf-diagnostics"
sys.path.insert(0, str(DIAGNOSTICS))
from pdf_health import health_report  # noqa: E402


def _aster_abstract_residuals(pdf_path: Path) -> list[str]:
    """Return source English that must not survive in ASTER's translated abstract.

    The abstract is the left column below the author block on page 1.  Keep
    this check focused on stable source phrases so legitimate technical terms
    in a translated paragraph do not make the regression flaky.
    """
    with fitz.open(pdf_path) as document:
        page = document[0]
        abstract_text = "\n".join(
            block[4]
            for block in page.get_text("blocks")
            if block[0] < 310 and 240 <= block[1] < 630
        ).casefold()
    source_phrases = (
        "abstract",
        "implementing automated unit tests",
        "time-consuming activity",
    )
    return [phrase for phrase in source_phrases if phrase in abstract_text]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--engine", choices=("identity", "synthetic-cjk"), default="identity")
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args()

    if not SOURCE.is_file():
        print(f"SKIP: ASTER fixture not found: {SOURCE}")
        return 0
    if not CLI.is_file():
        print(f"FAIL: compiled CLI not found: {CLI}")
        return 1

    output_dir = ROOT / "tmp" / "pdfs" / f"aster-regression-{args.engine}"
    output_dir.mkdir(parents=True, exist_ok=True)
    output_pdf = output_dir / "ASTER _translated.pdf"
    health_path = output_dir / "ASTER _translated_health.json"
    for path in (output_pdf, health_path):
        if path.exists():
            path.unlink()

    env = os.environ.copy()
    env["CLICKRA_TRANSLATION_ENGINE"] = args.engine
    started = time.monotonic()
    try:
        completed = subprocess.run(
            ["dotnet", str(CLI), "translate-pdf", "--no-ui", "--out-dir", str(output_dir), str(SOURCE)],
            cwd=ROOT,
            env=env,
            timeout=args.timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print(f"FAIL: ASTER {args.engine} E2E exceeded {args.timeout}s")
        return 1

    if completed.returncode != 0:
        print(f"FAIL: ASTER {args.engine} CLI exited with {completed.returncode}")
        return 1
    if not output_pdf.is_file() or not health_path.is_file():
        print("FAIL: ASTER output or health report was not created")
        return 1

    health = json.loads(health_path.read_text(encoding="utf-8"))
    with output_pdf.open("rb") as stream:
        pages = len(PdfReader(stream).pages)
    diagnostic = health_report(
        output_pdf,
        output_dir / "ASTER _pdf_health.json",
    )

    failures: list[str] = []
    if pages != 12:
        failures.append(f"page count changed: {pages}")
    if not health.get("Succeeded"):
        failures.append("health report is not successful")
    if health.get("OutputPages") != 12:
        failures.append(f"health output pages: {health.get('OutputPages')}")
    if health.get("OverflowEntries", 0) != 0:
        failures.append(f"layout overflow entries: {health.get('OverflowEntries')}")
    if health.get("GuardClipEntries", 0) != 0:
        failures.append(f"guard clip entries: {health.get('GuardClipEntries')}")
    if health.get("TranslationFailures"):
        failures.append(f"translation failures: {health['TranslationFailures']}")
    if diagnostic.get("total_tofu", 0) != 0:
        failures.append(f"PDF diagnostic tofu/NUL count: {diagnostic.get('total_tofu')}")
    if diagnostic.get("total_simp", 0) != 0:
        failures.append(f"PDF diagnostic simplified-character count: {diagnostic.get('total_simp')}")
    if args.engine != "identity":
        residuals = _aster_abstract_residuals(output_pdf)
        if residuals:
            failures.append(f"ASTER page 1 abstract retains source text: {residuals}")

    render_log = output_dir / "ASTER _renderdbg.log"
    if render_log.exists() and "clipped=true" in render_log.read_text(encoding="utf-8").casefold():
        failures.append("render debug still reports clipped=true")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    elapsed = time.monotonic() - started
    print(f"PASS: ASTER {args.engine} E2E completed in {elapsed:.1f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
