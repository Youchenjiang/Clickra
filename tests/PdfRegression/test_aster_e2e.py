"""Deterministic ASTER PDF regression gate for the local user fixture."""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

import fitz
from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[2]
SOURCE_CANDIDATES = (
    ROOT / "ASTER .pdf",
    ROOT / "test_pdfs" / "source" / "ASTER .pdf",
)
SOURCE = next((path for path in SOURCE_CANDIDATES if path.is_file()), SOURCE_CANDIDATES[0])
CLI = ROOT / "src" / "Clickra.CLI" / "bin" / "Debug" / "net10.0-windows" / "Clickra.dll"
DIAGNOSTICS = ROOT / "tools" / "pdf-diagnostics"
sys.path.insert(0, str(DIAGNOSTICS))
from pdf_health import health_report  # noqa: E402
from layout_occupancy import compare_pdf_layout_occupancy  # noqa: E402


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


def _aster_author_email_is_intact(pdf_path: Path) -> bool:
    with fitz.open(pdf_path) as document:
        page = document[0]
        author_text = "\n".join(
            block[4]
            for block in page.get_text("blocks")
            if block[0] < 500 and block[1] < 225 and block[3] > 200
        )
    compact = re.sub(r"\s+", "", author_text).casefold()
    expected = "{1rangeet.pan,3rkrsn}@ibm.com,{4pavuluri,5sinhas}@us.ibm.com,2mkim754@gatech.edu"
    return expected in compact


def _aster_title_anchor_is_preserved(pdf_path: Path) -> tuple[float, float]:
    """Return source/output title center drift and output title height."""
    with fitz.open(SOURCE) as source_doc, fitz.open(pdf_path) as output_doc:
        source_blocks = [
            b for b in source_doc[0].get_text("blocks")
            if b[1] < 170 and b[4].strip().startswith("ASTER:")
        ]
        output_blocks = [
            b for b in output_doc[0].get_text("blocks")
            # Exclude the running header above the paper title. Synthetic-CJK
            # stress translation can legitimately translate that header too;
            # it must not be mistaken for the title anchor under test.
            if 80 <= b[1] < 180 and any("測" in ch for ch in b[4])
        ]
    if not source_blocks or not output_blocks:
        return 999.0, 0.0
    source = source_blocks[0]
    output = max(output_blocks, key=lambda b: b[2] - b[0])
    source_center = (source[0] + source[2]) / 2
    output_center = (output[0] + output[2]) / 2
    return abs(source_center - output_center), output[3] - output[1]


def _aster_table_iii_missing_rows(pdf_path: Path) -> list[int]:
    """Return survey row numbers lost or translated inside page 8 Table III."""
    with fitz.open(pdf_path) as document:
        table_text = "\n".join(
            block[4]
            for block in document[7].get_text("blocks")
            if block[0] >= 300 and block[1] < 220
        )
    rows = {int(value) for value in re.findall(r"\bQ(\d+)\.", table_text)}
    return [number for number in range(1, 20) if number not in rows]


def _evaluate_aster_results(
    output_pdf: Path,
    health_path: Path,
    output_dir: Path,
    engine: str,
) -> list[str]:
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
    if health.get("HeadingCount", 0) <= 0:
        failures.append("health report did not record any headings")
    if health.get("MinimumHeadingFontRatio", 0) < 1.0:
        failures.append(f"heading font ratio below source: {health.get('MinimumHeadingFontRatio')}")
    if health.get("MaximumAlignmentAnchorShift", 0) > 1.5:
        failures.append(f"heading anchor drift: {health.get('MaximumAlignmentAnchorShift')}")
    if diagnostic.get("total_tofu", 0) != 0:
        failures.append(f"PDF diagnostic tofu/NUL count: {diagnostic.get('total_tofu')}")
    if diagnostic.get("total_simp", 0) != 0:
        failures.append(f"PDF diagnostic simplified-character count: {diagnostic.get('total_simp')}")
    missing_table_rows = _aster_table_iii_missing_rows(output_pdf)
    if missing_table_rows:
        failures.append(f"ASTER page 8 Table III lost or translated rows: {missing_table_rows}")
    layout_report = compare_pdf_layout_occupancy(SOURCE, output_pdf)
    layout_report_path = output_dir / "ASTER _layout_occupancy.json"
    layout_report_path.write_text(
        json.dumps(layout_report, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    failures.extend(f"layout occupancy: {item}" for item in layout_report["failures"])
    if engine != "identity":
        residuals = _aster_abstract_residuals(output_pdf)
        if residuals:
            failures.append(f"ASTER page 1 abstract retains source text: {residuals}")
        if not _aster_author_email_is_intact(output_pdf):
            failures.append("ASTER page 1 author/email band was altered or redrawn incorrectly")
        drift, _ = _aster_title_anchor_is_preserved(output_pdf)
        if drift > 1.5:
            failures.append(f"ASTER page 1 title center drift: {drift:.2f}pt")

    render_log = output_dir / "ASTER _renderdbg.log"
    if render_log.exists() and "clipped=true" in render_log.read_text(encoding="utf-8").casefold():
        failures.append("render debug still reports clipped=true")

    return failures


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

    failures = _evaluate_aster_results(output_pdf, health_path, output_dir, args.engine)
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    elapsed = time.monotonic() - started
    print(f"PASS: ASTER {args.engine} E2E completed in {elapsed:.1f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
