"""Health-based smoke gate for Clickra PDF translation output.

This is intentionally stricter and less clever than the legacy geometry
heuristics. It trusts the generated health JSON for hard failures and reports
page-level warnings separately so noisy layout guesses do not become release
blockers.

Run: python tests/PdfRegression/test_translation_health.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import fitz

from pdf_test_common import DIAGNOSTIC_DIR, ROOT, SOURCE_DIR, SOURCE_MAP, TRANSLATED_DIR


PDFS = SOURCE_MAP
EXPECTATIONS_PATH = ROOT / "tests" / "PdfRegression" / "translation_baseline_expectations.json"


def translated_name(source_name: str) -> str:
    return f"{Path(source_name).stem}_translated.pdf"


def health_name(source_name: str) -> str:
    return f"{Path(source_name).stem}_translated_health.json"


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def page_count(path: Path) -> int:
    with fitz.open(str(path)) as doc:
        return len(doc)


def main() -> int:
    failures: list[str] = []
    warnings: list[str] = []
    expectations = load_json(EXPECTATIONS_PATH) if EXPECTATIONS_PATH.exists() else {}

    print("=" * 60)
    print("Clickra Translation Health Gate")
    print("=" * 60)

    for key, source_file in PDFS.items():
        src = SOURCE_DIR / source_file
        translated = TRANSLATED_DIR / translated_name(source_file)
        health_path = DIAGNOSTIC_DIR / health_name(source_file)

        print(f"\n--- {key} ---")

        if not src.exists():
            failures.append(f"{key}: missing source PDF: {src}")
            print("  FAIL: source PDF missing")
            continue
        if not translated.exists():
            failures.append(f"{key}: missing translated PDF: {translated}")
            print("  FAIL: translated PDF missing")
            continue
        if not health_path.exists():
            failures.append(f"{key}: missing health JSON: {health_path}")
            print("  FAIL: health JSON missing")
            continue

        src_pages = page_count(src)
        translated_pages = page_count(translated)
        health = load_json(health_path)
        health_pages = int(health.get("total_pages", -1))

        if src_pages != translated_pages:
            failures.append(f"{key}: page count mismatch source={src_pages} translated={translated_pages}")
        if translated_pages != health_pages:
            failures.append(f"{key}: health total_pages={health_pages} but PDF has {translated_pages}")

        total_tofu = int(health.get("total_tofu", 0))
        total_simp = int(health.get("total_simp", 0))
        bad_pages = list(health.get("bad_pages", []))
        warn_pages = list(health.get("warn_pages", []))
        expected = expectations.get(key, {})
        max_tofu = int(expected.get("max_tofu", 0))
        max_simp = int(expected.get("max_simplified", 0))
        allowed_bad_pages = set(expected.get("allowed_bad_pages", []))
        unexpected_bad_pages = sorted(set(bad_pages) - allowed_bad_pages)

        if total_tofu > max_tofu:
            failures.append(f"{key}: tofu glyphs regressed: {total_tofu} > baseline {max_tofu}")
        elif total_tofu:
            warnings.append(f"{key}: known tofu debt: {total_tofu}")
        if total_simp > max_simp:
            failures.append(
                f"{key}: simplified Chinese regressed: {total_simp} > baseline {max_simp}"
            )
        elif total_simp:
            warnings.append(f"{key}: known simplified-Chinese debt: {total_simp}")
        if unexpected_bad_pages:
            failures.append(f"{key}: new bad pages: {unexpected_bad_pages}")
        if bad_pages:
            warnings.append(f"{key}: known bad pages: {bad_pages}")

        if warn_pages:
            warnings.append(f"{key}: warning pages: {warn_pages}")

        for page in health.get("pages", []):
            page_no = page.get("page")
            body_total = int(page.get("body_total", 0))
            eng_ratio = float(page.get("eng_ratio", 0.0))
            body_eng = int(page.get("body_eng_right", 0))
            if body_total >= 10 and eng_ratio >= 0.30:
                warnings.append(
                    f"{key}: p{page_no} high remaining-English ratio "
                    f"{eng_ratio:.2f} ({body_eng}/{body_total})"
                )

        print(f"  pages: source={src_pages} translated={translated_pages} health={health_pages}")
        print(f"  totals: cjk={health.get('total_cjk', 0)} simp={total_simp} tofu={total_tofu}")
        print(f"  health: bad_pages={bad_pages} warn_pages={warn_pages}")

    print("\n" + "=" * 60)
    if warnings:
        print("Warnings:")
        for item in warnings:
            print(f"  - {item}")

    if failures:
        print("\nFailures:")
        for item in failures:
            print(f"  - {item}")
        print("=" * 60)
        return 1

    print("PASS: translation health gate")
    print("=" * 60)
    return 0


if __name__ == "__main__":
    sys.exit(main())

