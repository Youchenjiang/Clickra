"""Analyze or compare chart-region text. Merged from analyze_chart_text/verify_chart_text."""
import argparse
import fitz
from _pdf_utils import SOURCES


def chart_regions(page):
    pw, ph = page.rect.width, page.rect.height
    regs = []
    for d in page.get_drawings():
        r = fitz.Rect(d["rect"])
        if r.width > pw * 0.9 or r.height > ph * 0.9:
            continue
        if (r.width > 80 and r.height > 30) or (r.width > 30 and r.height > 60):
            regs.append(r)
    return regs


def overlap_area(a, b):
    x0, y0 = max(a.x0, b.x0), max(a.y0, b.y0)
    x1, y1 = min(a.x1, b.x1), min(a.y1, b.y1)
    return max(0, x1 - x0) * max(0, y1 - y0)


def spans_in_regions(page):
    regs = chart_regions(page)
    out = []
    for block in page.get_text("dict")["blocks"]:
        if block.get("type") != 0:
            continue
        for line in block.get("lines", []):
            for span in line.get("spans", []):
                t = span.get("text", "").strip()
                if not t:
                    continue
                s = fitz.Rect(span["bbox"])
                cx, cy = (s.x0 + s.x1) / 2, (s.y0 + s.y1) / 2
                for ri, reg in enumerate(regs):
                    if reg.contains(fitz.Point(cx, cy)):
                        out.append((t, tuple(round(v, 1) for v in s), ri))
                        break
    return out


def analyze_overlap(pdf_path, pages):
    doc = fitz.open(str(pdf_path))
    for pn in pages:
        page = doc[pn - 1]
        regs = chart_regions(page)
        print(f"\n=== {pdf_path} p{pn} paths={len(regs)} ===")
        for block in page.get_text("dict")["blocks"]:
            if block.get("type") != 0:
                continue
            for line in block.get("lines", []):
                for span in line.get("spans", []):
                    text = span.get("text", "").strip()
                    if not text:
                        continue
                    srect = fitz.Rect(span["bbox"])
                    best, best_area = None, 0
                    for pi, prect in enumerate(regs):
                        area = overlap_area(srect, prect)
                        if area > best_area:
                            best_area = area
                            best = (pi, prect)
                    if best and best_area > 1:
                        pi, prect = best
                        print(f"  SPAN [{srect.x0:.0f},{srect.y0:.0f},{srect.x1:.0f},{srect.y1:.0f}] "
                              f"path#{pi} overlap={best_area:.0f} text={text[:60]!r}")
    doc.close()


def compare_regions(src_pdf, trn_pdf, pages):
    sdoc = fitz.open(str(src_pdf))
    tdoc = fitz.open(str(trn_pdf))
    for pn in pages:
        ss = spans_in_regions(sdoc[pn - 1])
        ts = spans_in_regions(tdoc[pn - 1])
        print(f"\n=== p{pn} src={len(ss)} trn={len(ts)} ===")
        for text, bbox, ri in ss[:25]:
            match = [x for x in ts if abs(x[1][0] - bbox[0]) < 3 and abs(x[1][1] - bbox[1]) < 3]
            status = "OK" if match and match[0][0] == text else ("POS" if match else "MISS")
            trn_text = match[0][0] if match else "-"
            if status != "OK":
                print(f"  [{status}] src={text!r} trn={trn_text!r} @ {bbox}")
    sdoc.close()
    tdoc.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Chart region text analysis")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("pages", nargs="+", type=int, help="Page numbers (1-based)")
    parser.add_argument("--compare", "-c", help="Translated PDF for comparison")
    args = parser.parse_args()
    pdf = SOURCES.get(args.pdf, args.pdf)
    if args.compare:
        trn = SOURCES.get(args.compare, args.compare)
        compare_regions(pdf, trn, args.pages)
    else:
        analyze_overlap(pdf, args.pages)
