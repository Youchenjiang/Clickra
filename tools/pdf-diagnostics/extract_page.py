"""Extract specific pages from PDF to a new file."""
import argparse
import fitz
from pathlib import Path
from _pdf_utils import out_dir, SOURCES


def extract(pdf_path, pages, out_path=None, project="misc"):
    doc = fitz.open(str(pdf_path))
    new_doc = fitz.open()
    for p in pages:
        if 1 <= p <= len(doc):
            new_doc.insert_pdf(doc, from_page=p - 1, to_page=p - 1)
        else:
            print(f"Skip page {p} (out of range)")
    if out_path is None:
        name = Path(pdf_path).stem
        out_path = out_dir(project) / f"{name}_p{'_'.join(map(str, pages))}.pdf"
    new_doc.save(str(out_path))
    new_doc.close()
    doc.close()
    print(f"Saved {out_path} ({len(pages)} pages)")
    return out_path


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Extract pages from PDF")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("pages", nargs="+", type=int, help="Page numbers (1-based)")
    parser.add_argument("--out", "-o", help="Output path")
    args = parser.parse_args()
    key = args.pdf
    pdf = SOURCES.get(key, key)
    out = Path(args.out) if args.out else None
    extract(pdf, args.pages, out, key)
