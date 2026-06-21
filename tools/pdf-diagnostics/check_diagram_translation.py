"""Check if diagram labels were incorrectly translated by comparing original vs translated.

Strategy: In the ORIGINAL PDF, find short English spans in the top region (likely diagram labels).
In the TRANSLATED PDF, check if those same positions now contain CJK text.
If yes, the label was translated (violation of §2.A).
"""
import argparse
import fitz
import re
from _pdf_utils import open_pdf, SOURCES, TRANSLATED, CJK_RE, ENG_RE, out_dir
from pathlib import Path


SECTION_HEADING_RE = re.compile(
    r"^(?:I{1,3}V?|VI{0,3}|IX|XI{0,2})\.\s|"
    r"^(?:ABSTRACT|INTRODUCTION|RELATED\s|CONCLUSION|DISCUSSION|REFERENCES?|BIBLIOGRAPHY|APPENDIX|ACKNOWLEDGMENT|ACKNOWLEDGEMENT|EVALUATION|THREAT\s|IMPLEMENTATION|APPROACH|METHODOLOGY|RESULT|FINDINGS|LIMITATIONS)\b|"
    r"^(?:摘要|簡介|相關工作|結論|討論|參考文獻|附錄|致謝|評估|威脅模型|實作|方法|結果|發現|限制)|"
    r"^[A-Z]\.\s|"
    r"^(?:TABLE|Fig\.?|Figure)\s|"
    r"^(?:表|圖)\s*\d",
    re.IGNORECASE
)


def get_english_label_spans(page, max_len=20):
    spans = []
    page_h = page.rect.height
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
            if not line_text or len(line_text) > max_len:
                continue
            if not ENG_RE.search(line_text):
                continue
            if SECTION_HEADING_RE.match(line_text):
                continue
            if re.match(r"^\d|^\(|^Note:|^http", line_text):
                continue
            x0 = min(s["bbox"][0] for s in line["spans"])
            y0 = min(s["bbox"][1] for s in line["spans"])
            x1 = max(s["bbox"][2] for s in line["spans"])
            y1 = max(s["bbox"][3] for s in line["spans"])
            cy = (y0 + y1) / 2
            spans.append({
                "text": line_text,
                "x0": x0, "y0": y0, "x1": x1, "y1": y1,
                "cy": cy, "y_ratio": cy / page_h,
            })
    return spans


def get_cjk_at_position(page, target_rect, tolerance=15):
    expanded = fitz.Rect(
        target_rect.x0 - tolerance, target_rect.y0 - tolerance,
        target_rect.x1 + tolerance, target_rect.y1 + tolerance
    )
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b.get("lines", []):
            line_text = "".join(s.get("text", "") for s in line["spans"]).strip()
            if not line_text or not CJK_RE.search(line_text):
                continue
            x0 = min(s["bbox"][0] for s in line["spans"])
            y0 = min(s["bbox"][1] for s in line["spans"])
            x1 = max(s["bbox"][2] for s in line["spans"])
            y1 = max(s["bbox"][3] for s in line["spans"])
            line_rect = fitz.Rect(x0, y0, x1, y1)
            if expanded.intersects(line_rect):
                cx = (x0 + x1) / 2
                cy = (y0 + y1) / 2
                tcx = (target_rect.x0 + target_rect.x1) / 2
                tcy = (target_rect.y0 + target_rect.y1) / 2
                dist = ((cx - tcx) ** 2 + (cy - tcy) ** 2) ** 0.5
                if dist < 40:
                    return line_text
    return None


def check_page(orig_page, trans_page, page_idx):
    orig_spans = get_english_label_spans(orig_page)
    violations = []

    for span in orig_spans:
        target = fitz.Rect(span["x0"], span["y0"], span["x1"], span["y1"])
        cjk_text = get_cjk_at_position(trans_page, target)
        if cjk_text:
            violations.append({
                "orig": span["text"],
                "trans": cjk_text,
                "x0": round(span["x0"], 1),
                "y": round(span["cy"], 1),
            })

    return {"orig_labels": len(orig_spans), "violations": violations}


def check_document(orig_path, trans_path, pages=None):
    orig_doc = fitz.open(str(orig_path))
    trans_doc = fitz.open(str(trans_path))
    pages = pages or range(1, min(len(orig_doc), len(trans_doc)) + 1)

    print(f"=== Diagram label translation check ===")
    print(f"Original: {orig_path}")
    print(f"Translated: {trans_path}")

    total_violations = 0
    for p in pages:
        result = check_page(orig_doc[p - 1], trans_doc[p - 1], p - 1)
        if result["violations"]:
            total_violations += len(result["violations"])
            print(f"\nPage {p} ({len(result['violations'])} labels translated):")
            for v in result["violations"]:
                print(f"  [{v['x0']:.0f},{v['y']:.0f}] '{v['orig']}' -> '{v['trans']}'")

    orig_doc.close()
    trans_doc.close()
    print(f"\nTotal diagram labels incorrectly translated: {total_violations}")
    return total_violations


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Check diagram label translation via orig vs trans comparison")
    parser.add_argument("--orig", "-a", required=True, help="Original PDF path or source key (2407/pentest/togll/final)")
    parser.add_argument("--trans", "-b", required=True, help="Translated PDF path or source key")
    parser.add_argument("--pages", nargs="*", type=int, help="Pages to check")
    args = parser.parse_args()
    orig = TRANSLATED.get(args.orig, SOURCES.get(args.orig, args.orig))
    trans = TRANSLATED.get(args.trans, args.trans)
    check_document(orig, trans, args.pages)
