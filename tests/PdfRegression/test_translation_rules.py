"""Integration tests for Clickra translation rules.

Verifies that translated PDFs comply with translation_rules.md §2 bypass rules.
Run: python tests/PdfRegression/test_translation_rules.py
"""
import fitz
import re
import sys
from pathlib import Path

from pdf_test_common import SOURCE_DIR, SOURCE_MAP, TRANSLATED_DIR, TRANSLATED_MAP

CJK_RE = re.compile(r"[\u4e00-\u9fff]")
ENG_RE = re.compile(r"[A-Za-z]{3,}")
MONO_FONTS = ("Courier", "Console", "Inconsolata", "Typewriter", "NimbusMon", "MonL", "cmtt", "ectt", "sftt", "Teletype", "Mono", "Code")
CJK_FONT_NAMES = ("DFKai", "JhengHei", "YaHei", "SimSun", "MingLiU", "PMingLiU")

REF_HEADING_RE = re.compile(
    r"^(\d{1,2})\.\s*(?:REFERENCES?|BIBLIOGRAPHY)\s*\.?\s*$|"
    r"^(?:REFERENCES?|BIBLIOGRAPHY)$|^參考文獻$",
    re.IGNORECASE
)
REF_TERMINATORS = re.compile(
    r"^APPENDIX|^Appendix\s+[A-Z]|^[A-Z]\.\s+|^WORK\s+DIVISION|^ACKNOWLEDGMENT|^ACKNOWLEDGEMENT|"
    r"^\d+\.\s+(?!REFERENCE)",
    re.IGNORECASE
)

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


def get_diagram_regions(page):
    regions = []
    page_w = page.rect.width
    page_h = page.rect.height
    for d in page.get_drawings():
        r = fitz.Rect(d["rect"])
        if r.width > page_w * 0.85 or r.height > page_h * 0.85:
            continue
        if r.width < 30 or r.height < 20:
            continue
        if (r.width > 80 and r.height > 30) or (r.width > 30 and r.height > 60):
            regions.append(r)
    return regions


def is_mono_font(font_name):
    return any(m in font_name for m in MONO_FONTS)


# ============================================================
# Test: §2.F References section bypass
# ============================================================
def test_references_bypass(pdf_key):
    print(f"\n--- §2.F References bypass: {pdf_key} ---")
    src_path = SOURCE_DIR / SOURCE_MAP[pdf_key]
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not src_path.exists() or not trans_path.exists():
        test(f"{pdf_key}: references fixtures exist", False, f"{src_path} / {trans_path}")
        return

    src_doc = fitz.open(str(src_path))
    trans_doc = fitz.open(str(trans_path))

    in_refs = False
    violations = []

    for p in range(len(src_doc)):
        src_page = src_doc[p]
        trans_page = trans_doc[p]

        for b in src_page.get_text("dict")["blocks"]:
            if b.get("type") != 0:
                continue
            for line in b.get("lines", []):
                line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
                if not line_text:
                    continue

                if REF_HEADING_RE.match(line_text):
                    in_refs = True
                    continue

                if in_refs and REF_TERMINATORS.match(line_text):
                    in_refs = False
                    continue

                if in_refs and ENG_RE.search(line_text) and len(line_text) < 60:
                    x0 = min(s["bbox"][0] for s in line["spans"])
                    if x0 > 200:
                        trans_text = "".join(s.get("text", "") for s in trans_page.get_text("dict")["blocks"][0]["lines"][0]["spans"][:1]) if trans_page.get_text("dict")["blocks"] else ""
                        if CJK_RE.search(trans_text):
                            violations.append(f"p{p+1}: '{line_text[:40]}'")

    test(f"{pdf_key}: references not translated", len(violations) == 0,
         f"{len(violations)} violations" if violations else "")

    src_doc.close()
    trans_doc.close()


# ============================================================
# Test: §2.D Author block bypass
# ============================================================
def test_author_block(pdf_key):
    print(f"\n--- §2.D Author block: {pdf_key} ---")
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not trans_path.exists():
        test(f"{pdf_key}: author fixture exists", False, str(trans_path))
        return

    trans_doc = fitz.open(str(trans_path))
    page = trans_doc[0]
    text = page.get_text()
    lines = text.split("\n")

    author_violations = []
    for line in lines[:15]:
        stripped = line.strip()
        if not stripped:
            continue
        if CJK_RE.search(stripped) and ENG_RE.search(stripped):
            if re.match(r"^[A-Z][a-z]+\s[A-Z]", stripped) or "@" in stripped:
                author_violations.append(stripped)

    test(f"{pdf_key}: author names not translated", len(author_violations) == 0,
         f"found: {author_violations[:3]}" if author_violations else "")

    trans_doc.close()


# ============================================================
# Test: §2.A Diagram labels not translated
# ============================================================
def test_diagram_labels(pdf_key):
    print(f"\n--- §2.A Diagram labels: {pdf_key} ---")
    src_path = SOURCE_DIR / SOURCE_MAP[pdf_key]
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not src_path.exists() or not trans_path.exists():
        test(f"{pdf_key}: diagram fixtures exist", False, f"{src_path} / {trans_path}")
        return

    src_doc = fitz.open(str(src_path))
    trans_doc = fitz.open(str(trans_path))
    total_violations = 0

    for p in range(min(len(src_doc), len(trans_doc))):
        src_page = src_doc[p]
        trans_page = trans_doc[p]

        src_spans = []
        for b in src_page.get_text("dict")["blocks"]:
            if b.get("type") != 0:
                continue
            for line in b.get("lines", []):
                line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
                if not line_text or len(line_text) > 20:
                    continue
                if not ENG_RE.search(line_text):
                    continue
                if re.match(r"^\d|^\(|^Fig|^Table|^Note|^http", line_text):
                    continue
                x0 = min(s["bbox"][0] for s in line["spans"])
                y0 = min(s["bbox"][1] for s in line["spans"])
                x1 = max(s["bbox"][2] for s in line["spans"])
                y1 = max(s["bbox"][3] for s in line["spans"])
                src_spans.append({"text": line_text, "rect": fitz.Rect(x0, y0, x1, y1)})

        for span in src_spans:
            expanded = fitz.Rect(span["rect"].x0 - 15, span["rect"].y0 - 15,
                                 span["rect"].x1 + 15, span["rect"].y1 + 15)
            for b in trans_page.get_text("dict")["blocks"]:
                if b.get("type") != 0:
                    continue
                for line in b.get("lines", []):
                    line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
                    if not line_text or not CJK_RE.search(line_text):
                        continue
                    lx0 = min(s["bbox"][0] for s in line["spans"])
                    ly0 = min(s["bbox"][1] for s in line["spans"])
                    lx1 = max(s["bbox"][2] for s in line["spans"])
                    ly1 = max(s["bbox"][3] for s in line["spans"])
                    line_rect = fitz.Rect(lx0, ly0, lx1, ly1)
                    if expanded.intersects(line_rect):
                        cx = (lx0 + lx1) / 2
                        cy = (ly0 + ly1) / 2
                        tcx = (span["rect"].x0 + span["rect"].x1) / 2
                        tcy = (span["rect"].y0 + span["rect"].y1) / 2
                        dist = ((cx - tcx) ** 2 + (cy - tcy) ** 2) ** 0.5
                        if dist < 40:
                            total_violations += 1
                            break

    test(f"{pdf_key}: diagram labels not translated", total_violations == 0,
         f"{total_violations} labels translated" if total_violations else "")

    src_doc.close()
    trans_doc.close()


# ============================================================
# Test: §3.B Simplified chars (PostProcessTranslation)
# ============================================================
def test_simplified_chars(pdf_key):
    print(f"\n--- §3.B Simplified chars: {pdf_key} ---")
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not trans_path.exists():
        test(f"{pdf_key}: simplified-char fixture exists", False, str(trans_path))
        return

    from opencc import OpenCC
    cc = OpenCC("s2tw")

    trans_doc = fitz.open(str(trans_path))
    total_simp = 0
    simp_details = []

    for p in range(len(trans_doc)):
        text = trans_doc[p].get_text()
        for c in text:
            if CJK_RE.match(c) and cc.convert(c) != c:
                total_simp += 1
                if len(simp_details) < 10:
                    simp_details.append(f"p{p+1}: {c} (U+{ord(c):04X})")

    test(f"{pdf_key}: simplified chars < 10", total_simp < 10,
         f"found {total_simp}: {simp_details[:5]}" if total_simp else "")

    trans_doc.close()


# ============================================================
# Test: Tofu (missing glyphs)
# ============================================================
def test_tofu(pdf_key):
    print(f"\n--- Tofu: {pdf_key} ---")
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not trans_path.exists():
        test(f"{pdf_key}: tofu fixture exists", False, str(trans_path))
        return

    trans_doc = fitz.open(str(trans_path))
    total_tofu = 0
    tofu_details = []

    for p in range(len(trans_doc)):
        text = trans_doc[p].get_text()
        tofu_count = text.count("\ufffd") + text.count("\x00")
        if tofu_count > 0:
            total_tofu += tofu_count
            tofu_details.append(f"p{p+1}: {tofu_count}")

    test(f"{pdf_key}: no tofu", total_tofu == 0,
         f"found {total_tofu} on {tofu_details}" if total_tofu else "")

    trans_doc.close()


# ============================================================
# Test: Basic integrity (page count, no crashes)
# ============================================================
def test_integrity(pdf_key):
    print(f"\n--- Integrity: {pdf_key} ---")
    src_path = SOURCE_DIR / SOURCE_MAP[pdf_key]
    trans_path = TRANSLATED_DIR / TRANSLATED_MAP[pdf_key]
    if not src_path.exists() or not trans_path.exists():
        test(f"{pdf_key}: integrity fixtures exist", False, f"{src_path} / {trans_path}")
        return

    src_doc = fitz.open(str(src_path))
    trans_doc = fitz.open(str(trans_path))

    test(f"{pdf_key}: translation exists", trans_path.exists())
    test(f"{pdf_key}: page count matches", len(src_doc) == len(trans_doc),
         f"src={len(src_doc)} trans={len(trans_doc)}")

    src_doc.close()
    trans_doc.close()


# ============================================================
# Main
# ============================================================
if __name__ == "__main__":
    print("=" * 60)
    print("Clickra Translation Rules Integration Tests")
    print("=" * 60)

    for key in SOURCE_MAP:
        test_integrity(key)
        test_references_bypass(key)
        test_author_block(key)
        test_diagram_labels(key)
        test_simplified_chars(key)
        test_tofu(key)

    print("\n" + "=" * 60)
    print(f"Results: {passed} passed, {failed} failed")
    if errors:
        print("Failed tests:")
        for e in errors:
            print(f"  - {e}")
    print("=" * 60)
    sys.exit(1 if failed else 0)

