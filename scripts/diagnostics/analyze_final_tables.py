"""Analyze pages 14-16 of final_project source vs translated."""
import fitz
import re
from pathlib import Path
from _pdf_utils import out_dir, SOURCES

SRC = SOURCES["final"]
TRN = Path(str(SRC).replace(".pdf", "_translated.pdf"))
OUT = out_dir("final_project")

PAGES = [13, 14, 15]  # 0-based: pages 14,15,16


def render_pair(doc_src, doc_trn, pi):
    zoom = 2.0
    mat = fitz.Matrix(zoom, zoom)
    for label, doc, suffix in [
        ("src", doc_src, "src"),
        ("trn", doc_trn, "trn"),
    ]:
        pix = doc[pi].get_pixmap(matrix=mat, alpha=False)
        out = OUT / f"final_project_p{pi+1}_{suffix}.png"
        pix.save(str(out))
        print(f"Rendered {out.name} ({pix.width}x{pix.height})")


def drawing_stats(page):
    drawings = page.get_drawings()
    lines = rects = curves = quads = 0
    for d in drawings:
        for item in d.get("items", []):
            kind = item[0]
            if kind == "l":
                lines += 1
            elif kind == "re":
                rects += 1
            elif kind == "c":
                curves += 1
            elif kind == "qu":
                quads += 1
    return len(drawings), lines, rects, curves, quads


def span_stats(page, y_min=None, y_max=None):
    eng_only = cjk_only = mixed = ghost = 0
    eng_samples = []
    cjk_samples = []
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            for span in line.get("spans", []):
                t = span.get("text", "").strip()
                if not t:
                    continue
                bbox = span.get("bbox", [0, 0, 0, 0])
                cy = (bbox[1] + bbox[3]) / 2
                if y_min is not None and cy < y_min:
                    continue
                if y_max is not None and cy > y_max:
                    continue
                has_cjk = bool(re.search(r"[\u4e00-\u9fff]", t))
                has_eng = bool(re.search(r"[A-Za-z]{3,}", t))
                color = span.get("color", 0)
                # ghost: near-white text on white bg
                is_ghost = color in (16777215, 0xFFFFFF) or (
                    isinstance(color, int) and color > 0xFEFEFE
                )
                if is_ghost:
                    ghost += 1
                if has_cjk and has_eng:
                    mixed += 1
                elif has_cjk:
                    cjk_only += 1
                    if len(cjk_samples) < 8:
                        cjk_samples.append(t[:80])
                elif has_eng:
                    eng_only += 1
                    if len(eng_samples) < 8:
                        eng_samples.append(t[:80])
    return {
        "eng_only": eng_only,
        "cjk_only": cjk_only,
        "mixed": mixed,
        "ghost": ghost,
        "eng_samples": eng_samples,
        "cjk_samples": cjk_samples,
    }


def find_table_captions(page):
    text = page.get_text()
    hits = []
    for pat in [
        r"(?mi)^TABLE\s+[IVXLCDM\d]+.*$",
        r"(?mi)^Table\s+[IVXLCDM\d]+.*$",
        r"(?mi)^?\s*[IVXLCDM\d??????????]+.*$",
        r"(?mi)^REFERENCES.*$",
        r"(?mi)^????.*$",
        r"(?mi)^APPENDIX.*$",
        r"(?mi)^??.*$",
        r"(?mi)^WORK\s+DIVISION.*$",
        r"(?mi)^????.*$",
    ]:
        for m in re.finditer(pat, text):
            hits.append(m.group(0).strip()[:100])
    return hits


def link_info(page):
    links = page.get_links()
    out = []
    for l in links:
        r = l.get("from")
        uri = l.get("uri", "")
        kind = l.get("kind")
        out.append({"kind": kind, "rect": r, "uri": uri[:80] if uri else ""})
    return out


def page_text_summary(page):
    t = page.get_text()
    lines = [ln.strip() for ln in t.splitlines() if ln.strip()]
    return lines[:25], lines[-10:]


def analyze_page(pi):
    doc_src = fitz.open(SRC)
    doc_trn = fitz.open(TRN)
    ps = doc_src[pi]
    pt = doc_trn[pi]
    pn = pi + 1
    print("=" * 72)
    print(f"PAGE {pn}")
    print("=" * 72)

    render_pair(doc_src, doc_trn, pi)

    ds = drawing_stats(ps)
    dt = drawing_stats(pt)
    print(f"Drawings: src paths={ds[0]} lines={ds[1]} rects={ds[2]} | trn paths={dt[0]} lines={dt[1]} rects={dt[2]}")
    print(f"  delta: paths {dt[0]-ds[0]:+d} lines {dt[1]-ds[1]:+d} rects {dt[2]-ds[2]:+d}")

    caps_s = find_table_captions(ps)
    caps_t = find_table_captions(pt)
    print(f"Captions/headings src: {caps_s}")
    print(f"Captions/headings trn: {caps_t}")

    head_s, tail_s = page_text_summary(ps)
    head_t, tail_t = page_text_summary(pt)
    print("Source first lines:")
    for ln in head_s[:12]:
        print(f"  | {ln[:100]}")
    print("Translated first lines:")
    for ln in head_t[:12]:
        print(f"  | {ln[:100]}")

    ss = span_stats(ps)
    st = span_stats(pt)
    print(f"Spans src: eng={ss['eng_only']} cjk={ss['cjk_only']} mixed={ss['mixed']} ghost={ss['ghost']}")
    print(f"Spans trn: eng={st['eng_only']} cjk={st['cjk_only']} mixed={st['mixed']} ghost={st['ghost']}")
    if st["cjk_samples"]:
        print("  CJK samples:", st["cjk_samples"][:5])
    if st["eng_samples"]:
        print("  ENG samples:", st["eng_samples"][:5])

  # table-ish region: middle 60% of page height
    h = ps.rect.height
    y0, y1 = h * 0.15, h * 0.85
    ss_mid = span_stats(ps, y0, y1)
    st_mid = span_stats(pt, y0, y1)
    print(f"Mid-region spans src: eng={ss_mid['eng_only']} cjk={ss_mid['cjk_only']}")
    print(f"Mid-region spans trn: eng={st_mid['eng_only']} cjk={st_mid['cjk_only']}")

    ls = link_info(ps)
    lt = link_info(pt)
    print(f"Links: src={len(ls)} trn={len(lt)}")
    for l in lt[:5]:
        print(f"  link {l}")

    doc_src.close()
    doc_trn.close()
    print()


def main():
    for pi in PAGES:
        analyze_page(pi)


if __name__ == "__main__":
    main()
