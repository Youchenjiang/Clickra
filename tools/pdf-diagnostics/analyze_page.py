"""Analyze a specific page: column breakdown, CJK/ENG counts, simplified chars. Merged from analyze_page10/analyze_page11/metrics_page10."""
import argparse
import fitz
import re
from _pdf_utils import has_cjk, ENG_RE, SOURCES, find_simplified_chars


def body_spans(doc, page_idx, x_split=300):
    page = doc[page_idx]
    left_body, right_body = [], []
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line.get("spans", []))
            line_text = line_text.strip()
            if not line_text:
                continue
            x0 = min(s["bbox"][0] for s in line["spans"])
            if re.search(r"(ParcelFileDescriptor|openFile|Listing|Vulnerable|return\s)", line_text):
                continue
            is_cjk = has_cjk(line_text)
            is_eng = bool(ENG_RE.search(line_text)) and not is_cjk
            col = "right" if x0 > x_split else "left"
            entry = (round(x0, 1), line_text[:100])
            if is_cjk:
                (right_body if col == "right" else left_body).append(("cjk", entry))
            elif is_eng:
                (right_body if col == "right" else left_body).append(("eng", entry))
    return left_body, right_body


def analyze(pdf_path, page_idx, x_split=300):
    doc = fitz.open(str(pdf_path))
    left, right = body_spans(doc, page_idx, x_split)
    left_cjk = sum(1 for t, _ in left if t == "cjk")
    left_eng = sum(1 for t, _ in left if t == "eng")
    right_cjk = sum(1 for t, _ in right if t == "cjk")
    right_eng = sum(1 for t, _ in right if t == "eng")
    all_cjk_text = " ".join(e[1] for t, e in left + right if t == "cjk")
    found = find_simplified_chars(all_cjk_text)
    simp = list(found.keys())
    doc.close()
    return {
        "left_cjk": left_cjk, "left_eng": left_eng,
        "right_cjk": right_cjk, "right_eng": right_eng,
        "simp_hits": len(simp), "simp_unique": set(simp),
        "right_eng_samples": [e for t, e in right if t == "eng"][:10],
        "right_cjk_samples": [e for t, e in right[:12] if t == "cjk"],
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Analyze page column breakdown")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("page", type=int, help="Page number (1-based)")
    parser.add_argument("--x-split", type=int, default=300, help="Column split X position")
    args = parser.parse_args()
    pdf = SOURCES.get(args.pdf, args.pdf)
    r = analyze(pdf, args.page - 1, args.x_split)
    print(f"=== Page {args.page} analysis ===")
    for k, v in r.items():
        if k not in ("right_eng_samples", "right_cjk_samples", "simp_unique"):
            print(f"  {k}: {v}")
    if r["simp_unique"]:
        print(f"  simp_unique: {r['simp_unique']}")
    print("  Right ENG samples:")
    for s in r["right_eng_samples"]:
        print(f"    {s}")
    print("  Right CJK samples:")
    for s in r["right_cjk_samples"]:
        print(f"    {s}")
