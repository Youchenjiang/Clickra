"""Find untranslated spans in translated PDF, respecting Clickra bypass rules.

Bypass rules from docs/translation_rules.md §2:
  §2.A Diagrams: large vector paths (w>80 && h>30) || (w>30 && h>60)
  §2.B Math: Math/Symbol/CMSY fonts, equation numbers (1), (2)
  §2.C Code: monospace fonts (Courier, Console, Inconsolata, Typewriter, etc.)
  §2.C Gray prompts: gray shaded vector regions
  §2.D Author block: small font between title and abstract on page 1
  §2.E Tables: rows near "Table N" / "表 N" captions
  §2.F References: after REFERENCES/BIBLIOGRAPHY heading
"""
import argparse
import re
import fitz
from _pdf_utils import SOURCES, CJK_RE, ENG_RE, get_diagram_regions

MONO_FONTS = ("Courier", "Console", "Inconsolata", "Typewriter", "NimbusMon",
              "MonL", "cmtt", "ectt", "sftt", "Teletype", "Mono", "Code")
MATH_FONTS = ("Math", "Symbol", "MSAM", "MSBM", "CMSY", "CMR", "CMMI", "CMSY")

REF_HEADING_RE = re.compile(
    r"^(\d{1,2})\.\s*(?:REFERENCES?|BIBLIOGRAPHY|參考文獻)\s*\.?\s*$|"
    r"^(?:REFERENCES?|BIBLIOGRAPHY)$|^參考文獻$",
    re.IGNORECASE
)
REF_TERMINATORS = re.compile(
    r"^APPENDIX|^Appendix\s+[A-Z]|^[A-Z]\.\s+|^WORK\s+DIVISION|^ACKNOWLEDGMENT|^ACKNOWLEDGEMENT|"
    r"^\d+\.\s+(?!REFERENCE)",
    re.IGNORECASE
)


def get_gray_regions(page):
    regions = []
    for d in page.get_drawings():
        fill = d.get("fill")
        if fill and len(fill) >= 3:
            r, g, b = fill[0], fill[1], fill[2]
            if 0.6 <= r <= 0.85 and 0.6 <= g <= 0.85 and 0.6 <= b <= 0.85:
                rect = fitz.Rect(d["rect"])
                if rect.width > 50 and rect.height > 30:
                    regions.append(rect)
    return regions


def is_mono_font(font_name):
    return any(m in font_name for m in MONO_FONTS)


def is_math_font(font_name):
    return any(m in font_name for m in MATH_FONTS)


def in_references_section(page_texts, page_idx):
    in_ref = False
    for pi in range(page_idx + 1):
        text = page_texts.get(pi, "")
        for line in text.splitlines():
            line_s = line.strip()
            if REF_HEADING_RE.match(line_s):
                in_ref = True
            elif in_ref and REF_TERMINATORS.match(line_s):
                in_ref = False
    return in_ref


def is_table_page(page):
    text = page.get_text()
    for line in text.splitlines():
        line_s = line.strip()
        if re.match(r"^(?:Table|TABLE|表)\s*\d+", line_s):
            words = line_s.split()
            if words[0] in ("Table", "TABLE", "表") or re.match(r"^(?:Table|TABLE|表)\s*\d+", line_s):
                prev_words = ("in", "see", "shown", "of", "and", "or", "from", "on", "with",
                              "below", "above", "shows", "depicts", "illustrates", "to", "for", "at", "using", "the")
                if len(words) > 1 and words[0].lower() in prev_words:
                    continue
                if len(words) > 1 and words[1].lower() in prev_words:
                    continue
                return True
    return False


def is_in_table_region(span_bbox, page, table_page):
    if not table_page:
        return False
    x0, y0, _x1, _y1 = span_bbox
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
            if re.match(r"^\d+[\.\)]\s", line_text) and len(line_text) < 80:
                lb = line["spans"][0]["bbox"]
                lx0, ly0, _lx1, _ly1 = lb
                if abs(ly0 - y0) < 5 and abs(lx0 - x0) < 20:
                    return True
    return False


def is_author_block(span_bbox, page_idx, page, title_bottom=None, abstract_top=None):
    if page_idx != 0:
        return False
    _x0, y0, _x1, y1 = span_bbox
    cy = (y0 + y1) / 2
    avg_size = (y1 - y0)
    if avg_size > 15:
        return False
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
            if re.match(r"(?i)^(?:ABSTRACT|摘要)", line_text):
                abstract_top = line["spans"][0]["bbox"][1]
            max_size = max((s.get("size", 0) for s in line["spans"]), default=0)
            if max_size > 20:
                title_bottom = line["spans"][0]["bbox"][3]
    if title_bottom and abstract_top and abstract_top <= cy <= title_bottom:
        return True
    return False


def _intersects_regions(span_rect, diagram_regions, gray_regions):
    for dr in diagram_regions:
        if dr.intersects(span_rect):
            area = span_rect.width * span_rect.height
            overlap = (dr & span_rect).width * (dr & span_rect).height / area if area > 0 else 0
            if overlap > 0.3:
                return "diagram"
    for gr in gray_regions:
        if gr.intersects(span_rect):
            return "gray_prompt"
    return None


def _should_skip_line(line, line_text, x0, y0, y1, cy, page_h, in_refs, diagram_regions, gray_regions, page, p, table_page):
    if in_refs:
        return "references"
    fonts = [s.get("font", "") for s in line["spans"]]
    if any(is_mono_font(f) for f in fonts):
        return "code"
    if any(is_math_font(f) for f in fonts):
        return "math"
    if cy < 40 or cy > page_h - 30:
        return "header_footer"

    x1 = max(s["bbox"][2] for s in line["spans"])
    span_rect = fitz.Rect(x0, y0, x1, y1)
    region_hit = _intersects_regions(span_rect, diagram_regions, gray_regions)
    if region_hit:
        return region_hit

    if is_author_block((x0, y0, x0 + 100, y1), p - 1, page):
        return "author_block"

    if is_in_table_region((x0, y0, x0 + 100, y1), page, table_page):
        return "table"

    if re.match(r"^(https?://|www\.|@|\d{1,3}\.\d{1,3})", line_text):
        return "url"

    return None


def _iter_text_lines(page):
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") == 0:
            yield from b.get("lines", [])


def _evaluate_line(line, p, in_refs, diagram_regions, gray_regions, table_page, page_h, page, x_split, min_len):
    line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
    if not line_text or len(line_text) < min_len:
        return None

    x0 = min(s["bbox"][0] for s in line["spans"])
    y0 = min(s["bbox"][1] for s in line["spans"])
    y1 = max(s["bbox"][3] for s in line["spans"])
    cy = (y0 + y1) / 2

    if x0 <= x_split or not ENG_RE.search(line_text) or CJK_RE.search(line_text):
        return None

    skip_reason = _should_skip_line(
        line, line_text, x0, y0, y1, cy, page_h,
        in_refs, diagram_regions, gray_regions, page, p, table_page
    )

    if skip_reason:
        return None

    return {
        "text": line_text[:120],
        "x0": round(x0, 1),
        "y": round(cy, 1),
    }


def _collect_page_hits(page, p, in_refs, x_split, min_len):
    diagram_regions = get_diagram_regions(page)
    gray_regions = get_gray_regions(page)
    table_page = is_table_page(page)
    page_h = page.rect.height
    page_hits = []

    for line in _iter_text_lines(page):
        hit = _evaluate_line(
            line, p, in_refs, diagram_regions, gray_regions,
            table_page, page_h, page, x_split, min_len
        )
        if hit:
            page_hits.append(hit)
    return page_hits


def find_untranslated(pdf_path, pages=None, x_split=300, min_len=15):
    doc = fitz.open(str(pdf_path))
    pages = pages or list(range(1, len(doc) + 1))
    page_texts = {i: doc[i].get_text() for i in range(len(doc))}

    results = []
    for p in pages:
        in_refs = in_references_section(page_texts, p - 1)
        page_hits = _collect_page_hits(doc[p - 1], p, in_refs, x_split, min_len)
        if page_hits:
            results.append({"page": p, "count": len(page_hits), "spans": page_hits})

    doc.close()

    print(f"=== Untranslated spans in {pdf_path} ===")
    print("(Diagram/Code/Math/Gray/Author/Table/Ref regions excluded)")
    total = 0
    for r in results:
        total += r["count"]
        print(f"\nPage {r['page']} ({r['count']} spans):")
        for s in r["spans"][:10]:
            print(f"  [{s['x0']:.0f},{s['y']:.0f}] {s['text']}")
    print(f"\nTotal untranslated: {total}")
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Find untranslated spans (bypass-aware)")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("--pages", nargs="*", type=int, help="Pages to check")
    parser.add_argument("--x-split", type=int, default=300, help="Column split position")
    parser.add_argument("--min-len", type=int, default=15, help="Minimum span length")
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    find_untranslated(pdf, args.pages, args.x_split, args.min_len)
