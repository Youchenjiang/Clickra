"""Render-based review pack for Clickra translated PDFs.

This is the visual/manual layer that complements native layout contract tests.
By default it renders known-risk pages, writes a JSON report, and only fails on
hard rendering problems such as missing PDFs, corrupt pages, or blank images.

Use --strict to fail on suspicious white-mask/text overlaps on the curated
review pages. Those heuristics are useful for triage, but intentionally not the
default release gate because PDF drawing geometry can be noisy.

Run:
    python tests/PdfRegression/test_translation_render_review.py
    python tests/PdfRegression/test_translation_render_review.py --strict
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import fitz
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
TEST_PDFS = ROOT / "test_pdfs"
DEFAULT_TRANSLATED_DIR = TEST_PDFS / "translated"
REVIEW_DIR = ROOT / "tmp" / "pdfs" / "render_review"

CJK_RE = re.compile(r"[\u4e00-\u9fff]")

PDFS = {
    "2407": "2407.11279v1_clean_translated.pdf",
    "pentest": "PentestAgent_Agent Pentest_translated.pdf",
    "togll": "TOGLL_Oracle Generation_translated.pdf",
    "final": "114423046_final_project_translated.pdf",
}

# Pages chosen from the visual review pass: high-risk tables, references,
# gray prompt boxes, and pages where the user-facing rendered output mattered.
REVIEW_PAGES = {
    "2407": [4, 6, 7, 10, 12, 13, 14, 15],
    "pentest": [1, 5, 7, 10, 13, 14, 15],
    "togll": [2, 3, 4, 7, 8, 10, 12],
    "final": [1, 3, 7, 11, 13, 14, 15, 16],
}


def translated_path(translated_dir: Path, key: str) -> Path:
    return translated_dir / PDFS[key]


def page_image_path(key: str, page_no: int) -> Path:
    return REVIEW_DIR / f"{key}_p{page_no:02d}.jpg"


def render_page(page: fitz.Page, out_path: Path, zoom: float = 2.0) -> dict:
    pix = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), alpha=False)
    pix.save(str(out_path))

    with Image.open(out_path) as img:
        gray = img.convert("L")
        pixels = gray.histogram()
        total = img.width * img.height
        nonwhite = sum(count for value, count in enumerate(pixels) if value < 250)
        dark = sum(count for value, count in enumerate(pixels) if value < 210)
        return {
            "width": img.width,
            "height": img.height,
            "nonwhite_ratio": nonwhite / total if total else 0.0,
            "dark_ratio": dark / total if total else 0.0,
        }


def white_rects(page: fitz.Page) -> list[fitz.Rect]:
    rects: list[fitz.Rect] = []
    page_w = page.rect.width
    page_h = page.rect.height

    for drawing in page.get_drawings():
        fill = drawing.get("fill")
        if not fill or len(fill) < 3:
            continue
        r, g, b = fill[:3]
        if r < 0.98 or g < 0.98 or b < 0.98:
            continue

        rect = fitz.Rect(drawing["rect"])
        if rect.width > page_w * 0.92 or rect.height > page_h * 0.92:
            continue
        if rect.width < 35 or rect.height < 8:
            continue
        rects.append(rect)

    return rects


def cjk_spans(page: fitz.Page) -> list[dict]:
    spans: list[dict] = []
    for block in page.get_text("dict")["blocks"]:
        if block.get("type") != 0:
            continue
        for line in block.get("lines", []):
            text = "".join(span.get("text", "") for span in line.get("spans", [])).strip()
            if len(text) < 3 or not CJK_RE.search(text):
                continue
            x0 = min(span["bbox"][0] for span in line["spans"])
            y0 = min(span["bbox"][1] for span in line["spans"])
            x1 = max(span["bbox"][2] for span in line["spans"])
            y1 = max(span["bbox"][3] for span in line["spans"])
            spans.append({"text": text[:80], "rect": fitz.Rect(x0, y0, x1, y1)})
    return spans


def suspicious_mask_hits(page: fitz.Page) -> list[dict]:
    hits: list[dict] = []
    rects = white_rects(page)
    spans = cjk_spans(page)

    for span in spans:
        span_rect = span["rect"]
        span_area = span_rect.width * span_rect.height
        if span_area <= 0:
            continue
        span_center_y = (span_rect.y0 + span_rect.y1) / 2

        for rect in rects:
            intersection = rect & span_rect
            if intersection.is_empty:
                continue
            ratio = (intersection.width * intersection.height) / span_area
            if 0.20 < ratio < 0.80 and rect.y0 <= span_center_y <= rect.y1:
                hits.append({
                    "text": span["text"],
                    "ratio": round(ratio, 3),
                    "span": rect_tuple(span_rect),
                    "mask": rect_tuple(rect),
                })
                break

    return hits


def rect_tuple(rect: fitz.Rect) -> list[float]:
    return [round(rect.x0, 1), round(rect.y0, 1), round(rect.x1, 1), round(rect.y1, 1)]


def make_contact_sheet(key: str, pages: list[int]) -> Path | None:
    image_paths = [page_image_path(key, p) for p in pages if page_image_path(key, p).exists()]
    if not image_paths:
        return None

    thumbs: list[Image.Image] = []
    target_w = 360
    label_h = 26
    margin = 12
    font = ImageFont.load_default()

    for path in image_paths:
        img = Image.open(path).convert("RGB")
        ratio = target_w / img.width
        target_h = int(img.height * ratio)
        img = img.resize((target_w, target_h), Image.LANCZOS)

        tile = Image.new("RGB", (target_w, target_h + label_h), "white")
        tile.paste(img, (0, label_h))
        draw = ImageDraw.Draw(tile)
        draw.text((6, 7), path.stem, fill=(20, 20, 20), font=font)
        thumbs.append(tile)

    cols = 2
    rows = (len(thumbs) + cols - 1) // cols
    tile_w = target_w
    tile_h = max(t.height for t in thumbs)
    sheet = Image.new(
        "RGB",
        (cols * tile_w + (cols + 1) * margin, rows * tile_h + (rows + 1) * margin),
        (245, 245, 245),
    )

    for i, thumb in enumerate(thumbs):
        row = i // cols
        col = i % cols
        x = margin + col * (tile_w + margin)
        y = margin + row * (tile_h + margin)
        sheet.paste(thumb, (x, y))

    out_path = REVIEW_DIR / f"{key}_contact_sheet.jpg"
    sheet.save(out_path, quality=90)
    return out_path


def review_pdf(translated_dir: Path, key: str) -> dict:
    path = translated_path(translated_dir, key)
    result = {
        "key": key,
        "pdf": str(path),
        "pages": [],
        "missing": not path.exists(),
        "contact_sheet": None,
    }
    if not path.exists():
        return result

    with fitz.open(str(path)) as doc:
        result["page_count"] = len(doc)
        for page_no in REVIEW_PAGES[key]:
            page_result = {"page": page_no, "png": str(page_image_path(key, page_no))}
            if page_no < 1 or page_no > len(doc):
                page_result["error"] = "page out of range"
                result["pages"].append(page_result)
                continue

            page = doc[page_no - 1]
            image_stats = render_page(page, page_image_path(key, page_no))
            hits = suspicious_mask_hits(page)
            page_result.update(image_stats)
            page_result["blank"] = image_stats["nonwhite_ratio"] < 0.003
            page_result["suspicious_mask_hit_count"] = len(hits)
            page_result["suspicious_mask_samples"] = hits[:8]
            result["pages"].append(page_result)

    contact_sheet = make_contact_sheet(key, REVIEW_PAGES[key])
    if contact_sheet:
        result["contact_sheet"] = str(contact_sheet)
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--strict", action="store_true", help="fail on suspicious mask hits")
    parser.add_argument(
        "--translated-dir",
        type=Path,
        default=DEFAULT_TRANSLATED_DIR,
        help="directory containing *_translated.pdf files",
    )
    parser.add_argument(
        "--only",
        choices=sorted(PDFS.keys()),
        action="append",
        help="review only one PDF key; can be provided multiple times",
    )
    args = parser.parse_args()

    REVIEW_DIR.mkdir(parents=True, exist_ok=True)
    report = {
        "strict": args.strict,
        "translated_dir": str(args.translated_dir),
        "output_dir": str(REVIEW_DIR),
        "pdfs": [],
    }

    failures: list[str] = []
    warnings: list[str] = []

    keys = args.only if args.only else list(PDFS.keys())
    for key in keys:
        item = review_pdf(args.translated_dir, key)
        report["pdfs"].append(item)
        if item["missing"]:
            failures.append(f"{key}: missing translated PDF: {item['pdf']}")
            continue

        for page in item["pages"]:
            page_no = page["page"]
            if page.get("error"):
                failures.append(f"{key}: p{page_no}: {page['error']}")
                continue
            if page.get("blank"):
                failures.append(f"{key}: p{page_no}: rendered image looks blank")

            hit_count = int(page.get("suspicious_mask_hit_count", 0))
            if hit_count:
                message = f"{key}: p{page_no}: {hit_count} suspicious mask/text overlaps"
                if args.strict:
                    failures.append(message)
                else:
                    warnings.append(message)

    report_path = REVIEW_DIR / "render_review.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print("=" * 60)
    print("Clickra Translation Render Review")
    print("=" * 60)
    print(f"Output: {REVIEW_DIR}")
    print(f"Report: {report_path}")

    for item in report["pdfs"]:
        status = "MISSING" if item["missing"] else "OK"
        print(f"\n--- {item['key']} [{status}] ---")
        if item.get("contact_sheet"):
            print(f"  contact sheet: {item['contact_sheet']}")
        for page in item["pages"][:3]:
            print(
                f"  p{page['page']:02d}: nonwhite={page.get('nonwhite_ratio', 0):.3f} "
                f"mask_hits={page.get('suspicious_mask_hit_count', 0)}"
            )
        if len(item["pages"]) > 3:
            print(f"  ... {len(item['pages'])} reviewed pages")

    if warnings:
        print("\nWarnings:")
        for warning in warnings:
            print(f"  - {warning}")

    if failures:
        print("\nFailures:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("\nPASS: render review pack created")
    return 0


if __name__ == "__main__":
    sys.exit(main())

