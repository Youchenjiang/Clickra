"""Small public-PDF input compatibility smoke test (not translation E2E)."""
from __future__ import annotations

import sys
from pathlib import Path

import pymupdf

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from fetch_public_pdf_fixtures import SOURCES, fetch  # noqa: E402


def main() -> int:
    directory = ROOT / "test_pdfs" / "downloaded_candidates"
    for key in SOURCES:
        path = fetch(key, directory)
        with pymupdf.open(path) as document:
            if len(document) == 0:
                raise ValueError(f"{key}: PDF has no pages")
            text = sum(len(page.get_text().strip()) for page in document)
            if text < 100:
                raise ValueError(f"{key}: missing expected extractable text")
            print(f"PASS {key}: {len(document)} pages, {text} text chars")
    print("PASS: three public PDF source compatibility checks (not translation output validation)")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        sys.exit(1)
