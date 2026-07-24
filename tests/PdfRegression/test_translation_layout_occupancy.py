"""Gate abnormal blank-space growth against each source PDF."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from pdf_test_common import ROOT, SOURCE_DIR, SOURCE_MAP, TRANSLATED_DIR, TRANSLATED_MAP
from layout_occupancy import compare_pdf_layout_occupancy


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path)
    parser.add_argument("--translated", type=Path)
    parser.add_argument(
        "--report",
        type=Path,
        default=ROOT / "tmp" / "pdfs" / "layout_occupancy_report.json",
    )
    args = parser.parse_args()
    if bool(args.source) != bool(args.translated):
        parser.error("--source and --translated must be provided together")

    pairs = (
        [("custom", args.source, args.translated)]
        if args.source
        else [
            (key, SOURCE_DIR / SOURCE_MAP[key], TRANSLATED_DIR / TRANSLATED_MAP[key])
            for key in SOURCE_MAP
        ]
    )

    reports: list[dict] = []
    failures: list[str] = []
    for key, source, translated in pairs:
        if not source.is_file() or not translated.is_file():
            failures.append(f"{key}: missing source or translated PDF")
            continue
        report = compare_pdf_layout_occupancy(source, translated)
        report["key"] = key
        reports.append(report)
        for failure in report["failures"]:
            failures.append(f"{key}: {failure}")

    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(
        json.dumps({"pdfs": reports, "failures": failures}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    for failure in failures:
        print(f"FAIL: {failure}")
    if failures:
        print(f"Report: {args.report}")
        return 1
    print(f"PASS: {len(reports)} source/translated PDFs passed layout occupancy checks")
    print(f"Report: {args.report}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
