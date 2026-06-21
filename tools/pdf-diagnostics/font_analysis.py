"""Analyze font usage per page: detect missing glyphs, CJK font coverage."""
import argparse
import fitz
from collections import Counter
from _pdf_utils import open_pdf, SOURCES, CJK_FONT_NAMES, out_dir
from pathlib import Path


def analyze_fonts(pdf_path, pages=None):
    doc = fitz.open(str(pdf_path))
    pages = pages or list(range(1, len(doc) + 1))

    for p in pages:
        page = doc[p - 1]
        font_counter = Counter()
        font_chars = {}
        font_ranges = {}

        for b in page.get_text("rawdict")["blocks"]:
            if "lines" not in b:
                continue
            for line in b["lines"]:
                for span in line["spans"]:
                    font = span.get("font", "unknown")
                    text = span.get("text", "")
                    font_counter[font] += len(text)
                    if font not in font_chars:
                        font_chars[font] = set()
                        font_ranges[font] = set()
                    for c in text:
                        font_chars[font].add(c)
                        o = ord(c)
                        if o > 127:
                            if o < 0x4E00:
                                font_ranges[font].add("latin_ext")
                            elif o <= 0x9FFF:
                                font_ranges[font].add("cjk")
                            elif o <= 0x303F:
                                font_ranges[font].add("symbols")
                            elif o <= 0x30FF:
                                font_ranges[font].add("kana")
                            else:
                                font_ranges[font].add("other")

        is_cjk_page = any(fn in font for fn in CJK_FONT_NAMES for fn in [fn])
        print(f"\n=== Page {p} ===")
        for font, count in font_counter.most_common(10):
            chars = font_chars[font]
            ranges = font_ranges[font]
            is_cjk = any(cjk in font for cjk in CJK_FONT_NAMES)
            flag = " [CJK]" if is_cjk else ""
            print(f"  {font}: {count} chars, {len(chars)} unique, ranges={ranges}{flag}")

        potential_issues = []
        for font, chars in font_chars.items():
            if not any(cjk in font for cjk in CJK_FONT_NAMES):
                continue
            latin_chars = [c for c in chars if 0x0080 <= ord(c) <= 0x024F]
            if latin_chars:
                potential_issues.append((font, latin_chars[:5]))

        if potential_issues:
            print(f"  ** Potential issues:")
            for font, chars in potential_issues:
                print(f"    {font}: CJK font rendering Latin chars: {[repr(c) for c in chars]}")

    doc.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Analyze PDF font usage")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("--pages", nargs="*", type=int, help="Pages to analyze")
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    analyze_fonts(pdf, args.pages)
