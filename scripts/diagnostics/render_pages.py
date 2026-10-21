"""Render PDF pages to PNG. Merged from 15+ render_*.py scripts."""
import argparse
from pathlib import Path
from _pdf_utils import open_pdf, out_dir, SOURCES
from PIL import Image
import fitz


def render(pdf_path, pages, out_path=None, prefix="page", zoom=2.0, project="misc"):
    out_path = out_path or out_dir(project)
    doc = fitz.open(str(pdf_path))
    mat = fitz.Matrix(zoom, zoom)
    results = []
    for p in pages:
        if p < 1 or p > len(doc):
            print(f"Skip page {p} (out of range)")
            continue
        pix = doc[p - 1].get_pixmap(matrix=mat)
        img = Image.frombytes("RGB", [pix.width, pix.height], pix.samples)
        save_path = out_path / f"{prefix}_p{p}.png"
        img.save(save_path)
        results.append(save_path)
        print(f"Page {p} -> {save_path}")
    doc.close()
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Render PDF pages to PNG")
    parser.add_argument("pdf", help="PDF path or source key (sem/2407/final/pentest/togll)")
    parser.add_argument("pages", nargs="+", type=int, help="Page numbers (1-based)")
    parser.add_argument("--out", "-o", help="Output directory")
    parser.add_argument("--prefix", "-p", default="page", help="Output prefix")
    parser.add_argument("--zoom", "-z", type=float, default=2.0, help="Zoom factor")
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    out = Path(args.out) if args.out else out_dir(key)
    render(pdf, args.pages, out, args.prefix, args.zoom)
