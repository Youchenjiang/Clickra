"""Test: detect white masks partially covering translated text.

A mask that covers part of a CJK span = the reader sees text with
white covering the bottom/top. This is the §4.D issue the user reported.
"""
import fitz
import re
import sys

from pdf_test_common import TRANSLATED_DIR, TRANSLATED_MAP

CJK_RE = re.compile(r"[\u4e00-\u9fff]")

passed = 0
failed = 0
errors = []


def test(name, condition, detail=""):
    global passed, failed
    if condition:
        passed += 1
        print(f"  PASS: {name}")
    else:
        failed += 1
        msg = f"  FAIL: {name}"
        if detail:
            msg += f" — {detail}"
        print(msg)
        errors.append(name)


def get_white_rects(page):
    rects = []
    page_w = page.rect.width
    page_h = page.rect.height
    for d in page.get_drawings():
        fill = d.get("fill")
        if fill and len(fill) >= 3:
            r, g, b = fill[0], fill[1], fill[2]
            if r > 0.95 and g > 0.95 and b > 0.95:
                rect = fitz.Rect(d["rect"])
                if rect.width < page_w * 0.9 and rect.height < page_h * 0.9:
                    if rect.width > 30 and rect.height > 10:
                        rects.append(rect)
    return rects


def get_cjk_spans(page):
    spans = []
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
            if not line_text or not CJK_RE.search(line_text) or len(line_text) < 3:
                continue
            x0 = min(s["bbox"][0] for s in line["spans"])
            y0 = min(s["bbox"][1] for s in line["spans"])
            x1 = max(s["bbox"][2] for s in line["spans"])
            y1 = max(s["bbox"][3] for s in line["spans"])
            spans.append({"text": line_text[:80], "rect": fitz.Rect(x0, y0, x1, y1)})
    return spans


def check_partial_overlap(pdf_key):
    print(f"\n--- Partial mask overlap: {pdf_key} ---")
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not trans_path.exists():
        test(f"{pdf_key}: translated fixture exists", False, str(trans_path))
        return

    doc = fitz.open(str(trans_path))
    total = 0
    page_violations = []

    for p in range(len(doc)):
        page = doc[p]
        rects = get_white_rects(page)
        spans = get_cjk_spans(page)
        if not rects:
            continue

        hits = []
        for span in spans:
            for rect in rects:
                intersection = rect & span["rect"]
                if intersection.is_empty:
                    continue
                inter_area = intersection.width * intersection.height
                span_area = span["rect"].width * span["rect"].height
                if span_area <= 0:
                    continue
                ratio = inter_area / span_area
                # Partial overlap: 20%-80% of span covered by white rect
                if 0.2 < ratio < 0.8:
                    hits.append(span["text"])
                    break

        if hits:
            total += len(hits)
            page_violations.append((p + 1, len(hits), hits))

    test(f"{pdf_key}: no partial mask overlap", total == 0,
         f"{total} spans on {len(page_violations)} pages" if total else "")

    for pn, count, samples in page_violations:
        print(f"    p{pn} ({count}):")
        for s in samples[:5]:
            print(f"      '{s}'")

    doc.close()


if __name__ == "__main__":
    print("=" * 60)
    print("Partial Mask Overlap Tests (§4.D)")
    print("=" * 60)

    for key in TRANSLATED_MAP:
        check_partial_overlap(key)

    print("\n" + "=" * 60)
    print(f"Results: {passed} passed, {failed} failed")
    if errors:
        print("Failed tests:")
        for e in errors:
            print(f"  - {e}")
    print("=" * 60)
    sys.exit(1 if failed else 0)
