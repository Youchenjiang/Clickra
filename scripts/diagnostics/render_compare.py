"""Side-by-side orig|trans comparison. Merged from render_visual_audit/render_pdf_compare/render_regression_fix_pages."""
import argparse
from _pdf_utils import render_side_by_side, out_dir, SOURCES
from pathlib import Path


def compare(orig_pdf, trans_pdf, pages, out_path=None, prefix="compare", zoom=2.0, project="misc"):
    out_path = out_path or out_dir(project)
    return render_side_by_side(orig_pdf, trans_pdf, pages, out_path, prefix, zoom)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Side-by-side PDF comparison")
    parser.add_argument("--orig", "-a", default="pentest", help="Original PDF or source key")
    parser.add_argument("--trans", "-b", help="Translated PDF or source key")
    parser.add_argument("pages", nargs="+", type=int, help="Page numbers (1-based)")
    parser.add_argument("--out", "-o", help="Output directory")
    parser.add_argument("--prefix", "-p", default="compare", help="Output prefix")
    parser.add_argument("--zoom", "-z", type=float, default=2.0)
    args = parser.parse_args()
    key = args.orig
    orig = SOURCES.get(key, key)
    if args.trans:
        trans = SOURCES.get(args.trans, args.trans)
    else:
        trans = str(orig).replace(".pdf", "_translated.pdf")
    out = Path(args.out) if args.out else out_dir(key)
    compare(orig, trans, args.pages, out, args.prefix, args.zoom)
