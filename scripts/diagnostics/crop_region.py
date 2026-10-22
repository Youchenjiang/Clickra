"""Crop rectangular regions from PDF pages. Merged from crop_eq/crop_p14_details/crop_p5_list_details."""
import argparse
import fitz
from pathlib import Path
from _pdf_utils import out_dir, SOURCES


def crop(pdf_path, page_idx, rect, out_path=None, zoom=2.0, project="misc"):
    doc = fitz.open(str(pdf_path))
    page = doc[page_idx]
    r = fitz.Rect(rect)
    clip = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), clip=r)
    if out_path is None:
        out_path = out_dir(project) / f"crop_p{page_idx+1}_{int(r.x0)}_{int(r.y0)}.png"
    clip.save(str(out_path))
    print(f"Saved {out_path}")
    doc.close()
    return out_path


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Crop PDF page region")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("page", type=int, help="Page number (1-based)")
    parser.add_argument("rect", nargs=4, type=float, metavar=("X0", "Y0", "X1", "Y1"), help="Crop rectangle")
    parser.add_argument("--out", "-o", help="Output path")
    parser.add_argument("--zoom", "-z", type=float, default=2.0)
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    out = Path(args.out) if args.out else None
    crop(pdf, args.page - 1, args.rect, out, args.zoom, key)
