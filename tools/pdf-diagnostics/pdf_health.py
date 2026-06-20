"""Full-document translation health report. Outputs per-page metrics.
Excludes bypass regions (diagrams, code, math, tables, references, author block) per §2 rules."""
import argparse
import fitz
import re
import json
from pathlib import Path
from _pdf_utils import open_pdf, SOURCES, CJK_RE, ENG_RE, out_dir, count_simplified_hits

MONO_FONTS = ("Courier", "Console", "Inconsolata", "Typewriter", "NimbusMon",
              "MonL", "cmtt", "ectt", "sftt", "Teletype", "Mono", "Code")
MATH_FONTS = ("Math", "Symbol", "MSAM", "MSBM", "CMSY", "CMR", "CMMI")

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
CITATION_LINE_RE = re.compile(r"^\s*\[\d+\]")


def get_diagram_regions(page):
    regions = []
    for d in page.get_drawings():
        r = fitz.Rect(d["rect"])
        if (r.width > 80 and r.height > 30) or (r.width > 30 and r.height > 60):
            regions.append(r)
    merged = []
    for r in regions:
        merged_flag = False
        for i, m in enumerate(merged):
            if r.intersects(m):
                merged[i] = m | r
                merged_flag = True
                break
        if not merged_flag:
            merged.append(r)
    return merged


def in_references_section(page_texts, page_idx):
    in_ref = False
    for pi in range(page_idx + 1):
        for line in page_texts.get(pi, "").splitlines():
            if REF_HEADING_RE.match(line.strip()):
                in_ref = True
            elif in_ref and REF_TERMINATORS.match(line.strip()):
                in_ref = False
    return in_ref


def is_citation_dense_reference_page(page_text):
    lines = [line.strip() for line in page_text.splitlines() if line.strip()]
    if not lines:
        return False
    citation_lines = sum(bool(CITATION_LINE_RE.match(line)) for line in lines)
    # Continuation reference pages often lose their REFERENCES heading during
    # extraction, but several numbered entries remain a strong low-noise cue.
    return citation_lines >= 3


def is_bypass_line(line, page, diagram_regions, in_refs):
    line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
    if not line_text:
        return True

    fonts = [s.get("font", "") for s in line["spans"]]
    if any(any(m in f for m in MONO_FONTS) for f in fonts):
        return True
    if any(any(m in f for m in MATH_FONTS) for f in fonts):
        return True

    if in_refs:
        return True

    y0 = min(s["bbox"][1] for s in line["spans"])
    y1 = max(s["bbox"][3] for s in line["spans"])
    if y0 < 40 or y1 > page.rect.height - 30:
        return True

    x0 = min(s["bbox"][0] for s in line["spans"])
    x1 = max(s["bbox"][2] for s in line["spans"])
    cy = (y0 + y1) / 2
    span_rect = fitz.Rect(x0, y0, x1, y1)
    for dr in diagram_regions:
        if dr.intersects(span_rect):
            overlap = (dr & span_rect).width * (dr & span_rect).height / (span_rect.width * span_rect.height) if span_rect.width * span_rect.height > 0 else 0
            if overlap > 0.3:
                return True

    return False


def page_health(page, page_idx, page_texts):
    text = page.get_text()
    cjk_chars = len(CJK_RE.findall(text))
    simp_chars = count_simplified_hits(text)
    tofu = text.count("\ufffd") + text.count("\x00")
    links = len(page.get_links())

    diagram_regions = get_diagram_regions(page)
    in_refs = (
        in_references_section(page_texts, page_idx)
        or is_citation_dense_reference_page(page_texts.get(page_idx, ""))
    )

    body_eng = body_cjk = body_total = 0
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
            if not line_text or len(line_text) < 4:
                continue
            bbox = line["spans"][0]["bbox"]
            x0 = bbox[0]
            if is_bypass_line(line, page, diagram_regions, in_refs):
                continue
            body_total += 1
            has_cjk = bool(CJK_RE.search(line_text))
            has_eng = bool(ENG_RE.search(line_text))
            if has_cjk:
                body_cjk += 1
            elif has_eng and x0 > 300:
                body_eng += 1

    return {
        "cjk_chars": cjk_chars,
        "simp_chars": simp_chars,
        "tofu": tofu,
        "links": links,
        "body_cjk": body_cjk,
        "body_eng_right": body_eng,
        "body_total": body_total,
        "eng_ratio": body_eng / body_total if body_total else 0,
    }


def health_report(pdf_path, out_path=None):
    doc = fitz.open(str(pdf_path))
    page_texts = {}
    for i in range(len(doc)):
        page_texts[i] = doc[i].get_text()

    pages = []
    for i in range(len(doc)):
        h = page_health(doc[i], i, page_texts)
        h["page"] = i + 1
        pages.append(h)
    doc.close()

    total_cjk = sum(p["cjk_chars"] for p in pages)
    total_simp = sum(p["simp_chars"] for p in pages)
    total_tofu = sum(p["tofu"] for p in pages)
    bad_pages = [p["page"] for p in pages if p["tofu"] > 0 or p["simp_chars"] > 0]
    warn_pages = [p["page"] for p in pages if p["eng_ratio"] > 0.3]

    report = {
        "pdf": str(pdf_path),
        "total_pages": len(pages),
        "total_cjk": total_cjk,
        "total_simp": total_simp,
        "total_tofu": total_tofu,
        "bad_pages": bad_pages,
        "warn_pages": warn_pages,
        "pages": pages,
    }

    print(f"=== Health Report: {pdf_path} ===")
    print(f"Pages: {len(pages)}  CJK: {total_cjk}  Simp: {total_simp}  Tofu: {total_tofu}")
    if bad_pages:
        print(f"PROBLEM pages: {bad_pages}")
    if warn_pages:
        print(f"WARNING (high eng ratio): {warn_pages}")
    for p in pages:
        flag = ""
        if p["tofu"] > 0:
            flag += " TOFU"
        if p["simp_chars"] > 0:
            flag += f" SIMP={p['simp_chars']}"
        if p["eng_ratio"] > 0.3:
            flag += f" ENG_R={p['eng_ratio']:.0%}"
        if flag:
            print(f"  p{p['page']:2d}: cjk={p['cjk_chars']:4d} eng_r={p['body_eng_right']:2d}/{p['body_total']:2d}{flag}")
        elif p["page"] <= 3 or p["page"] % 5 == 0:
            print(f"  p{p['page']:2d}: cjk={p['cjk_chars']:4d} ok")

    if out_path:
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        print(f"\nFull report -> {out_path}")

    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="PDF translation health report")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("--out", "-o", help="Save JSON report")
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    out = Path(args.out) if args.out else out_dir(pdf) / f"{Path(str(pdf)).stem}_health.json"
    health_report(pdf, out)
