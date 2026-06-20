"""Side-by-side table comparison for final_project pages 14-16."""
import fitz
import re
import json
import numpy as np
from pathlib import Path
from PIL import Image
from _pdf_utils import out_dir, SOURCES

SRC = SOURCES["final"]
TRN = Path(str(SRC).replace(".pdf", "_translated.pdf"))
OUT = out_dir("final_project")
ZOOM = 2.5

# PDF coords from dump-layout (PdfPig bottom-left origin). Convert to fitz top-left.
TABLE_REGIONS = {
    14: {
        "name": "WORK DIVISION (bottom right)",
        "pig_bbox": (309, 200, 560, 475),  # full mask from dump-layout Y=[204,468.5]
        "ref_pig_bbox": (50, 310, 300, 420),  # references area near table
    },
    15: {
        "name": "Table 18 full table",
        "pig_bbox": (50, 108, 562, 662),
    },
    16: {
        "name": "Table 19 full table",
        "pig_bbox": (50, 398, 562, 678),
    },
}


def pig_to_fitz_bbox(page, pig_bbox):
    """Convert PdfPig bottom-left bbox to fitz top-left Rect."""
    x0, y0, x1, y1 = pig_bbox
    h = page.rect.height
    # pig y0 is bottom, y1 is top
    return fitz.Rect(x0, h - y1, x1, h - y0)


def drawing_stats_in_rect(page, rect):
    drawings = page.get_drawings()
    lines, rects, curves = [], [], []
    rx0, ry0, rx1, ry1 = rect
    for d in drawings:
        for item in d.get("items", []):
            kind = item[0]
            if kind == "l":
                p1, p2 = item[1], item[2]
                cx = (p1.x + p2.x) / 2
                cy = (p1.y + p2.y) / 2
                if rx0 <= cx <= rx1 and ry0 <= cy <= ry1:
                    lines.append((round(p1.x, 1), round(p1.y, 1), round(p2.x, 1), round(p2.y, 1)))
            elif kind == "re":
                r = item[1]
                cx = (r.x0 + r.x1) / 2
                cy = (r.y0 + r.y1) / 2
                if rx0 <= cx <= rx1 and ry0 <= cy <= ry1:
                    rects.append((round(r.x0, 1), round(r.y0, 1), round(r.x1, 1), round(r.y1, 1)))
            elif kind == "c":
                curves.append(item)
    return {"line_count": len(lines), "rect_count": len(rects), "curve_count": len(curves), "lines": lines[:30], "rects": rects[:20]}


def spans_in_rect(page, rect, label=""):
    spans = []
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            for span in line.get("spans", []):
                sb = fitz.Rect(span["bbox"])
                if not sb.intersects(rect):
                    continue
                t = span.get("text", "").strip()
                if not t:
                    continue
                spans.append({
                    "text": t,
                    "bbox": [round(x, 1) for x in span["bbox"]],
                    "font": span.get("font", ""),
                    "size": round(span.get("size", 0), 2),
                    "color": span.get("color", 0),
                    "cjk": bool(re.search(r"[\u4e00-\u9fff]", t)),
                    "eng": bool(re.search(r"[A-Za-z]{2,}", t)),
                })
    spans.sort(key=lambda s: (s["bbox"][1], s["bbox"][0]))
    return spans


def white_ratio_bands(pix, n_bands=8):
    """Horizontal bands white pixel ratio (0=black ink, 1=white)."""
    arr = np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, pix.n)
    gray = arr[:, :, 0].astype(float)  # grayscale approx
    band_h = max(1, pix.height // n_bands)
    ratios = []
    for i in range(n_bands):
        y0 = i * band_h
        y1 = min(pix.height, (i + 1) * band_h)
        band = gray[y0:y1, :]
        white = (band > 240).mean()
        ratios.append(round(float(white), 3))
    return ratios


def render_side_by_side(doc_src, doc_trn, page_idx, pig_bbox, out_path, pad=8):
    ps = doc_src[page_idx]
    pt = doc_trn[page_idx]
    rect = pig_to_fitz_bbox(ps, pig_bbox)
    rect = rect + (-pad, -pad, pad, pad)
    mat = fitz.Matrix(ZOOM, ZOOM)
    pix_s = ps.get_pixmap(matrix=mat, clip=rect, alpha=False)
    pix_t = pt.get_pixmap(matrix=mat, clip=rect, alpha=False)
    h = max(pix_s.height, pix_t.height)
    w = pix_s.width + pix_t.width + 4
    combined = Image.new("RGB", (w, h), (200, 200, 200))
    combined.paste(Image.frombytes("RGB", [pix_s.width, pix_s.height], pix_s.samples), (0, 0))
    combined.paste(Image.frombytes("RGB", [pix_t.width, pix_t.height], pix_t.samples), (pix_s.width + 4, 0))
    combined.save(out_path)
    return rect, pix_s, pix_t


def compare_spans(src_spans, trn_spans):
    src_texts = [s["text"] for s in src_spans]
    trn_texts = [s["text"] for s in trn_spans]
    src_set = set(src_texts)
    trn_set = set(trn_texts)
    missing = [t for t in src_texts if t not in trn_set]
    extra = [t for t in trn_texts if t not in src_set]
    cjk_overlay = [s for s in trn_spans if s["cjk"] and any(
        re.sub(r"[\u4e00-\u9fff\s]", "", s["text"]).lower() in t.lower() or t.lower() in s["text"].lower()
        for t in src_texts if re.search(r"[A-Za-z]{3,}", t)
    )]
    eng_in_trn = [s for s in trn_spans if s["eng"] and not s["cjk"]]
    cjk_in_trn = [s for s in trn_spans if s["cjk"]]
    double_layer = []
    for ts in trn_spans:
        for ss in src_spans:
            if ts["text"] == ss["text"] and ts["text"]:
                tb, sb = ts["bbox"], ss["bbox"]
                if abs(tb[0] - sb[0]) < 3 and abs(tb[1] - sb[1]) < 3:
                    double_layer.append(ts["text"][:60])
    font_changes = []
    for ss in src_spans:
        for ts in trn_spans:
            if ss["text"] == ts["text"] and abs(ss["size"] - ts["size"]) > 0.5:
                font_changes.append(f"{ss['text'][:40]}: {ss['size']}->{ts['size']}")
    return {
        "src_span_count": len(src_spans),
        "trn_span_count": len(trn_spans),
        "missing_in_trn": missing[:25],
        "extra_in_trn": extra[:25],
        "trn_cjk_count": len(cjk_in_trn),
        "trn_eng_only_count": len(eng_in_trn),
        "cjk_samples": [s["text"][:70] for s in cjk_in_trn[:12]],
        "eng_samples_trn": [s["text"][:70] for s in eng_in_trn[:12]],
        "likely_double_layer": list(set(double_layer))[:10],
        "font_size_changes": font_changes[:10],
    }


def pixel_diff_stats(pix_s, pix_t):
  w = min(pix_s.width, pix_t.width)
  h = min(pix_s.height, pix_t.height)
  a = np.frombuffer(pix_s.samples, dtype=np.uint8).reshape(pix_s.height, pix_s.width, pix_s.n)[:h, :w, 0]
  b = np.frombuffer(pix_t.samples, dtype=np.uint8).reshape(pix_t.height, pix_t.width, pix_t.n)[:h, :w, 0]
  diff = np.abs(a.astype(int) - b.astype(int))
  changed = (diff > 15).mean()
  return {"changed_pixel_ratio": round(float(changed), 4), "mean_abs_diff": round(float(diff.mean()), 2)}


def analyze_page(page_num):
    page_idx = page_num - 1
    cfg = TABLE_REGIONS[page_num]
    doc_src = fitz.open(SRC)
    doc_trn = fitz.open(TRN)
    out_png = OUT / f"final_project_p{page_num}_table_compare.png"
    rect, pix_s, pix_t = render_side_by_side(doc_src, doc_trn, page_idx, cfg["pig_bbox"], out_png)
    print(f"\n{'='*70}")
    print(f"PAGE {page_num}: {cfg['name']}")
    print(f"PNG: {out_png}")
    print(f"Fitz clip rect: {rect}")

    ds = drawing_stats_in_rect(doc_src[page_idx], rect)
    dt = drawing_stats_in_rect(doc_trn[page_idx], rect)
    print(f"Drawings in region: src lines={ds['line_count']} rects={ds['rect_count']} | trn lines={dt['line_count']} rects={dt['rect_count']}")
    print(f"  delta lines={dt['line_count']-ds['line_count']:+d} rects={dt['rect_count']-ds['rect_count']:+d}")

    ss = spans_in_rect(doc_src[page_idx], rect)
    st = spans_in_rect(doc_trn[page_idx], rect)
    sc = compare_spans(ss, st)
    print(f"Spans: src={sc['src_span_count']} trn={sc['trn_span_count']} (cjk in trn={sc['trn_cjk_count']} eng-only={sc['trn_eng_only_count']})")
    if sc["missing_in_trn"]:
        print("MISSING in translated (present in source):")
        for t in sc["missing_in_trn"][:15]:
            print(f"  - {t[:90]}")
    if sc["extra_in_trn"]:
        print("EXTRA in translated:")
        for t in sc["extra_in_trn"][:15]:
            print(f"  + {t[:90]}")
    if sc["cjk_samples"]:
        print("CJK in translated table region:")
        for t in sc["cjk_samples"]:
            print(f"  [?] {t}")
    if sc["likely_double_layer"]:
        print("Likely double-layer (same text same position):")
        for t in sc["likely_double_layer"]:
            print(f"  = {t}")
    if sc["font_size_changes"]:
        print("Font size changes:")
        for t in sc["font_size_changes"]:
            print(f"  {t}")

    wr_s = white_ratio_bands(pix_s)
    wr_t = white_ratio_bands(pix_t)
    print(f"White ratio bands (top->bottom): src={wr_s}")
    print(f"White ratio bands (top->bottom): trn={wr_t}")

    pd = pixel_diff_stats(pix_s, pix_t)
    print(f"Pixel diff: changed_ratio={pd['changed_pixel_ratio']} mean_abs_diff={pd['mean_abs_diff']}")

    result = {
        "page": page_num,
        "name": cfg["name"],
        "png": str(out_png),
        "drawings": {"src": {k: ds[k] for k in ("line_count", "rect_count")}, "trn": {k: dt[k] for k in ("line_count", "rect_count")}},
        "spans": sc,
        "white_bands": {"src": wr_s, "trn": wr_t},
        "pixel_diff": pd,
    }

    if "ref_pig_bbox" in cfg:
        ref_png = OUT / f"final_project_p{page_num}_ref_compare.png"
        ref_rect, rp_s, rp_t = render_side_by_side(doc_src, doc_trn, page_idx, cfg["ref_pig_bbox"], ref_png, pad=4)
        rss = spans_in_rect(doc_src[page_idx], ref_rect)
        rts = spans_in_rect(doc_trn[page_idx], ref_rect)
        rsc = compare_spans(rss, rts)
        print(f"\nReferences area PNG: {ref_png}")
        print(f"Ref spans: src={rsc['src_span_count']} trn={rsc['trn_span_count']} missing={len(rsc['missing_in_trn'])} extra={len(rsc['extra_in_trn'])}")
        result["references"] = rsc

    doc_src.close()
    doc_trn.close()
    return result


def main():
    results = []
    for pn in [14, 15, 16]:
        results.append(analyze_page(pn))
    report = OUT / "final_project_table_compare_report.json"
    report.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nJSON report: {report}")


if __name__ == "__main__":
    main()
