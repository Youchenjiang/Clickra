"""Regression checks for preserving PDF link annotations during translation.

The repository carries two ASTER fixtures intentionally:

* ``ASTER .pdf`` has no annotations and is used by the text/layout gate.
* ``ASTER- .pdf`` contains the publisher's hyperlink annotations.

Keeping these fixtures distinct prevents a no-link input from masking a link
loss regression.  The check compares annotation destinations by page; link
rectangles are allowed to move because translated text may reflow.
"""
from __future__ import annotations

import argparse
import sys
from collections import Counter
from pathlib import Path

import fitz


ROOT = Path(__file__).resolve().parents[2]
LINKED_SOURCE = ROOT / "ASTER- .pdf"
UNLINKED_SOURCE = ROOT / "ASTER .pdf"


def _external_uris(page: fitz.Page) -> Counter[str]:
    """Return URI destinations, preserving duplicate links on a page."""
    return Counter(
        link.get("uri", "").strip()
        for link in page.get_links()
        if link.get("kind") == fitz.LINK_URI and link.get("uri", "").strip()
    )


def _annotation_counts(path: Path) -> list[int]:
    with fitz.open(path) as document:
        return [len(page.get_links()) for page in document]


def check_links(source: Path, translated: Path) -> list[str]:
    failures: list[str] = []
    with fitz.open(source) as source_doc, fitz.open(translated) as output_doc:
        if len(source_doc) != len(output_doc):
            return [f"page count changed: {len(source_doc)} -> {len(output_doc)}"]

        for page_number, (source_page, output_page) in enumerate(
            zip(source_doc, output_doc), start=1
        ):
            expected = _external_uris(source_page)
            actual = _external_uris(output_page)
            if expected != actual:
                failures.append(
                    f"page {page_number} URI destinations changed: "
                    f"expected {dict(expected)}, got {dict(actual)}"
                )
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=LINKED_SOURCE)
    parser.add_argument(
        "--translated",
        type=Path,
        help="translated linked ASTER PDF; omit to report the fixture distinction only",
    )
    args = parser.parse_args()

    if not args.source.is_file():
        print(f"SKIP: linked ASTER fixture not found: {args.source}")
        return 0
    if not UNLINKED_SOURCE.is_file():
        print(f"SKIP: unlinked ASTER fixture not found: {UNLINKED_SOURCE}")
        return 0

    source_counts = _annotation_counts(args.source)
    unlinked_counts = _annotation_counts(UNLINKED_SOURCE)
    if sum(source_counts) == 0:
        print("FAIL: linked ASTER fixture contains no annotations")
        return 1
    if sum(unlinked_counts) != 0:
        print("FAIL: unlinked ASTER fixture unexpectedly contains annotations")
        return 1

    if args.translated is None:
        print(
            "PASS: ASTER fixtures are distinct "
            f"(linked annotations={sum(source_counts)}, unlinked annotations=0)"
        )
        return 0
    if not args.translated.is_file():
        print(f"FAIL: translated PDF not found: {args.translated}")
        return 1

    failures = check_links(args.source, args.translated)
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    with fitz.open(args.translated) as output_doc:
        uri_count = sum(sum(_external_uris(page).values()) for page in output_doc)
    print(
        "PASS: translated ASTER preserved all per-page external URI destinations "
        f"({uri_count} links)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
