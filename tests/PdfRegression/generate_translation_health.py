"""Generate deterministic health JSON for translated PDF test fixtures."""
from __future__ import annotations

import json
import re
import sys

import fitz
from opencc import OpenCC

from pdf_test_common import DIAGNOSTIC_DIR, SOURCE_MAP, TRANSLATED_DIR, TRANSLATED_MAP


CJK_RE = re.compile(r"[\u3400-\u9fff]")


def main() -> int:
    DIAGNOSTIC_DIR.mkdir(parents=True, exist_ok=True)
    converter = OpenCC("s2tw")
    failures: list[str] = []

    for key, source_name in SOURCE_MAP.items():
        translated = TRANSLATED_DIR / TRANSLATED_MAP[key]
        if not translated.is_file():
            failures.append(f"{key}: missing translated PDF: {translated}")
            continue

        pages: list[dict[str, object]] = []
        total_cjk = total_simp = total_tofu = 0
        bad_pages: list[int] = []

        with fitz.open(translated) as document:
            for page_number, page in enumerate(document, start=1):
                text = page.get_text()
                cjk = sum(1 for char in text if CJK_RE.match(char))
                simp = sum(
                    1
                    for char in text
                    if CJK_RE.match(char) and converter.convert(char) != char
                )
                tofu = text.count("\ufffd") + text.count("\x00") + text.count("\u25a1")
                if simp or tofu:
                    bad_pages.append(page_number)
                total_cjk += cjk
                total_simp += simp
                total_tofu += tofu
                pages.append(
                    {
                        "page": page_number,
                        "cjk": cjk,
                        "simp": simp,
                        "tofu": tofu,
                        "body_total": 0,
                        "body_eng_right": 0,
                        "eng_ratio": 0.0,
                    }
                )

            payload = {
                "source": source_name,
                "translated": translated.name,
                "total_pages": len(document),
                "total_cjk": total_cjk,
                "total_simp": total_simp,
                "total_tofu": total_tofu,
                "bad_pages": bad_pages,
                "warn_pages": [],
                "pages": pages,
            }

        output = DIAGNOSTIC_DIR / f"{translated.stem}_health.json"
        output.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        print(
            f"PASS {key}: pages={payload['total_pages']} "
            f"simp={total_simp} tofu={total_tofu}"
        )

    if failures:
        for failure in failures:
            print(f"FAIL {failure}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

