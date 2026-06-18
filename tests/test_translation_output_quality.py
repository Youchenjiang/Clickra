"""Strong, low-noise structural checks for translated PDF baselines.

These checks intentionally avoid judging translation style. They catch stale
fixtures, missing/duplicated output at document scale, invalid text geometry,
and pages that unexpectedly lose all extractable content.
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path

import fitz

from pdf_test_common import (
    DIAGNOSTIC_DIR,
    SOURCE_DIR,
    SOURCE_MAP,
    TRANSLATED_DIR,
    TRANSLATED_MAP,
    require_file,
)


CJK_RE = re.compile(r"[\u3400-\u9fff]")
SPACE_RE = re.compile(r"\s+")


def health_path(source_name: str) -> Path:
    return DIAGNOSTIC_DIR / f"{Path(source_name).stem}_translated_health.json"


def normalized_lines(page: fitz.Page) -> list[str]:
    lines: list[str] = []
    for block in page.get_text("dict").get("blocks", []):
        if block.get("type") != 0:
            continue
        for line in block.get("lines", []):
            text = "".join(span.get("text", "") for span in line.get("spans", []))
            text = SPACE_RE.sub(" ", text).strip()
            if len(text) >= 12:
                lines.append(text)
    return lines


def check_pdf(key: str, failures: list[str], warnings: list[str]) -> None:
    source = SOURCE_DIR / SOURCE_MAP[key]
    translated = TRANSLATED_DIR / TRANSLATED_MAP[key]
    health = health_path(SOURCE_MAP[key])

    present = (
        require_file(source, failures, f"{key} source")
        & require_file(translated, failures, f"{key} translated")
        & require_file(health, failures, f"{key} health")
    )
    if not present:
        return

    if translated.stat().st_mtime < source.stat().st_mtime:
        failures.append(f"{key}: translated baseline is older than its source PDF")
    if health.stat().st_mtime < translated.stat().st_mtime:
        failures.append(f"{key}: health JSON is older than its translated PDF")

    with health.open("r", encoding="utf-8") as stream:
        health_data = json.load(stream)

    with fitz.open(source) as src_doc, fitz.open(translated) as out_doc:
        if len(src_doc) != len(out_doc):
            failures.append(f"{key}: page count changed {len(src_doc)} -> {len(out_doc)}")
            return
        if int(health_data.get("total_pages", -1)) != len(out_doc):
            failures.append(f"{key}: health JSON page count does not match translated PDF")

        source_chars = 0
        output_chars = 0
        for page_index, (src_page, out_page) in enumerate(zip(src_doc, out_doc), start=1):
            src_text = src_page.get_text().strip()
            out_text = out_page.get_text().strip()
            source_chars += len(src_text)
            output_chars += len(out_text)

            if len(src_text) >= 80 and len(out_text) < 20:
                failures.append(
                    f"{key}: p{page_index} lost extractable text "
                    f"({len(src_text)} source chars -> {len(out_text)} output chars)"
                )

            page_rect = out_page.rect
            for block in out_page.get_text("dict").get("blocks", []):
                if block.get("type") != 0:
                    continue
                rect = fitz.Rect(block["bbox"])
                if (
                    rect.x0 < page_rect.x0 - 2
                    or rect.y0 < page_rect.y0 - 2
                    or rect.x1 > page_rect.x1 + 2
                    or rect.y1 > page_rect.y1 + 2
                ):
                    failures.append(
                        f"{key}: p{page_index} text block outside page bounds: "
                        f"{tuple(round(v, 1) for v in rect)}"
                    )
                    break

            counts = Counter(normalized_lines(out_page))
            repeated = [
                (text, count)
                for text, count in counts.items()
                if count >= 4 and (CJK_RE.search(text) or len(text) >= 30)
            ]
            if repeated:
                sample, count = max(repeated, key=lambda item: item[1])
                warnings.append(
                    f"{key}: p{page_index} repeated line {count}x: {sample[:70]!r}"
                )

        if source_chars and output_chars < source_chars * 0.25:
            failures.append(
                f"{key}: document lost too much text "
                f"({source_chars} source chars -> {output_chars} output chars)"
            )
        if source_chars and output_chars > source_chars * 4:
            failures.append(
                f"{key}: document text expanded suspiciously "
                f"({source_chars} source chars -> {output_chars} output chars)"
            )


def main() -> int:
    failures: list[str] = []
    warnings: list[str] = []
    for key in SOURCE_MAP:
        check_pdf(key, failures, warnings)

    print("=" * 60)
    print("Clickra Translation Output Quality")
    print("=" * 60)
    for warning in warnings:
        print(f"WARN: {warning}")
    for failure in failures:
        print(f"FAIL: {failure}")
    if failures:
        return 1
    print(f"PASS: {len(SOURCE_MAP)} translated baselines passed structural checks")
    return 0


if __name__ == "__main__":
    sys.exit(main())
