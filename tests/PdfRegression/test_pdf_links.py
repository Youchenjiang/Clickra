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


def _fixture(name: str) -> Path:
    candidates = (ROOT / name, ROOT / "test_pdfs" / "source" / name)
    return next((path for path in candidates if path.is_file()), candidates[0])


LINKED_SOURCE = _fixture("ASTER- .pdf")
UNLINKED_SOURCE = _fixture("ASTER .pdf")


def _external_uris(page: fitz.Page) -> Counter[str]:
    """Return URI destinations, preserving duplicate links on a page."""
    return Counter(
        link.get("uri", "").strip()
        for link in page.get_links()
        if link.get("kind") == fitz.LINK_URI and link.get("uri", "").strip()
    )


def _internal_destinations(page: fitz.Page) -> Counter[tuple[str, int, float, float]]:
    """Return named citation targets without depending on movable link rectangles."""
    destinations: Counter[tuple[str, int, float, float]] = Counter()
    for link in page.get_links():
        if link.get("kind") != fitz.LINK_NAMED:
            continue
        target = link.get("to")
        if target is None:
            continue
        destinations[
            (
                link.get("nameddest", ""),
                int(link.get("page", -1)),
                round(float(target.x), 3),
                round(float(target.y), 3),
            )
        ] += 1
    return destinations


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

            expected_internal = _internal_destinations(source_page)
            actual_internal = _internal_destinations(output_page)
            if expected_internal != actual_internal:
                failures.append(
                    f"page {page_number} internal destinations changed: "
                    f"expected {sum(expected_internal.values())}, "
                    f"got {sum(actual_internal.values())}"
                )

            for link in output_page.get_links():
                if link.get("kind") != fitz.LINK_NAMED:
                    continue
                if not output_page.get_text("words", clip=link["from"]):
                    failures.append(
                        f"page {page_number} internal link rectangle no longer covers text: "
                        f"{link.get('nameddest', '<unnamed>')}"
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
        internal_count = sum(
            sum(_internal_destinations(page).values()) for page in output_doc
        )
    print(
        "PASS: translated ASTER preserved all link destinations "
        f"(external={uri_count}, internal={internal_count})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
