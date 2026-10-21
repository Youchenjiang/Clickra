"""Render PDF pages with link overlay. Merged from render_after/validate_after/render_link_overlay."""
import argparse
from _pdf_utils import render_link_overlay, out_dir, SOURCES
from pathlib import Path


def overlay(pdf_path, pages, out_path=None, prefix="links", zoom=2.0, project="misc"):
    out_path = out_path or out_dir(project)
    results = []
    for p in pages:
        save_path = out_path / f"{prefix}_p{p}.png"
        render_link_overlay(pdf_path, p - 1, save_path, zoom)
        results.append(save_path)
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Render PDF pages with link overlay")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("pages", nargs="+", type=int, help="Page numbers (1-based)")
    parser.add_argument("--out", "-o", help="Output directory")
    parser.add_argument("--prefix", "-p", default="links", help="Output prefix")
    parser.add_argument("--zoom", "-z", type=float, default=2.0)
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    out = Path(args.out) if args.out else out_dir(key)
    overlay(pdf, args.pages, out, args.prefix, args.zoom)
