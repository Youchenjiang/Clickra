"""Find problematic characters (tofu, CJK-font-Latin, null). Merged from 6 find_tofus/check_*_tofu scripts."""
import argparse
from _pdf_utils import open_pdf, find_problematic_chars, SOURCES


def scan(pdf_path, pages=None, checks=None):
    doc = open_pdf(pdf_path)
    pages = pages or list(range(1, len(doc) + 1))
    total = 0
    for p in pages:
        results = find_problematic_chars(doc[p - 1], checks)
        if results:
            print(f"=== Page {p} ===")
            for r in results:
                if r["type"] == "tofu":
                    print(f"  TOFU: {repr(r['char'])} | Font: {r['font']} | Text: {r['text']}")
                elif r["type"] == "cjk_latin":
                    print(f"  CJK-LATIN: {repr(r['char'])} ({r['codepoint']}) | Font: {r['font']} | Text: {r['text']}")
            total += len(results)
    doc.close()
    print(f"\nTotal problematic chars: {total}")
    return total


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Find problematic characters in PDF")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("--pages", nargs="*", type=int, help="Pages to check (default: all)")
    parser.add_argument("--checks", nargs="+", default=["tofu", "cjk_font_latin"], choices=["tofu", "cjk_font_latin", "null"])
    args = parser.parse_args()
    pdf = SOURCES.get(args.pdf, args.pdf)
    scan(pdf, args.pages, args.checks)
