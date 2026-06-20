"""Contracts for PDF health heuristics that previously produced false alarms."""
from __future__ import annotations

import sys
from pathlib import Path

from opencc import OpenCC


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scratch" / "scripts"
sys.path.insert(0, str(SCRIPTS))

from pdf_health import is_citation_dense_reference_page  # noqa: E402


def main() -> int:
    failures: list[str] = []
    cc = OpenCC("s2tw")

    if cc.convert("群") != "群":
        failures.append("Taiwan validator must accept common character '群'")
    if cc.convert("软件") == "软件":
        failures.append("Taiwan validator must still detect '软件' as simplified")

    references = "\n".join(
        [
            "[13] First reference",
            "continued title",
            "[14] Second reference",
            "[15] Third reference",
        ]
    )
    if not is_citation_dense_reference_page(references):
        failures.append("citation-dense continuation page was not recognized")
    if is_citation_dense_reference_page("[1] One citation\nordinary body prose"):
        failures.append("single citation must not classify a body page as references")

    for failure in failures:
        print(f"FAIL: {failure}")
    if failures:
        return 1
    print("PASS: health heuristics")
    return 0


if __name__ == "__main__":
    sys.exit(main())

