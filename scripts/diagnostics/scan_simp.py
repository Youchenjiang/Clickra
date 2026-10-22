"""Scan simplified Chinese characters in translated PDF using opencc."""
import argparse
from _pdf_utils import open_pdf, find_simplified_chars, has_cjk, ENG_RE, SOURCES


def _get_cc():
    from opencc import OpenCC
    return OpenCC("s2t")


def _process_span(span, x_split, cc, stats):
    text = span.get("text", "").strip()
    if not text:
        return
    x0 = span["bbox"][0]
    col = "right" if x0 > x_split else "left"
    if has_cjk(text):
        stats[f"{col}_cjk"] += 1
        for ch in text:
            if ch != cc.convert(ch):
                stats["simp_hits"].append((col, ch, text[:80]))
    elif ENG_RE.search(text):
        stats[f"{col}_eng"] += 1


def scan_page(page, x_split=300, cc=None):
    cc = cc or _get_cc()
    stats = {"left_cjk": 0, "right_cjk": 0, "left_eng": 0, "right_eng": 0, "simp_hits": []}
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            for span in line.get("spans", []):
                _process_span(span, x_split, cc, stats)
    return stats


def scan_pdf(pdf_path, pages=None):
    cc = _get_cc()
    doc = open_pdf(pdf_path)
    pages = pages or list(range(len(doc)))
    total_simp = 0
    for p in pages:
        page = doc[p]
        text = page.get_text()
        found = find_simplified_chars(text)
        page_simp = sum(found.values())
        r = scan_page(page, cc=cc)
        if page_simp > 0 or r["left_cjk"] or r["right_cjk"]:
            top = sorted(found.items(), key=lambda x: -x[1])[:5]
            simp_detail = " ".join(f"{c}({n})" for c, n in top)
            print(f"Page {p + 1}: left_cjk={r['left_cjk']} right_cjk={r['right_cjk']} left_eng={r['left_eng']} right_eng={r['right_eng']} simp={page_simp} {simp_detail}")
        total_simp += page_simp
    doc.close()
    print(f"Total simplified hits: {total_simp}")
    return total_simp


def main():
    parser = argparse.ArgumentParser(description="Scan simplified Chinese chars in PDF")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("--pages", nargs="*", type=int, help="Pages to scan (default: all)")
    args = parser.parse_args()
    pdf = SOURCES.get(args.pdf, args.pdf)
    scan_pdf(pdf, args.pages)


if __name__ == "__main__":
    main()
