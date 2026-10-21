"""Inspect PDF page blocks/spans/drawings. Merged from 15+ inspect_*.py scripts."""
import argparse
import fitz
from _pdf_utils import open_pdf, get_spans, SOURCES


def inspect_blocks(pdf_path, page_idx, mode="rawdict"):
    doc = open_pdf(pdf_path)
    page = doc[page_idx]
    print(f"=== Page {page_idx + 1} ({mode}) ===")
    if mode == "rawdict":
        for b in page.get_text("rawdict")["blocks"]:
            if "lines" in b:
                for l in b["lines"]:
                    for s in l["spans"]:
                        text_run = "".join(c.get("c", "") for c in s.get("chars", []))
                        bbox = s.get("bbox")
                        print(f"[{bbox[0]:.1f}, {bbox[1]:.1f}, {bbox[2]:.1f}, {bbox[3]:.1f}] Font: {s.get('font')} | Size: {s.get('size', 0):.1f} | Text: '{text_run}'")
    elif mode == "blocks":
        for i, b in enumerate(page.get_text("blocks")):
            x0, y0, x1, y1, text, block_no, block_type = b
            print(f"Block {i} [{x0:.1f}, {y0:.1f}, {x1:.1f}, {y1:.1f}]:")
            print(repr(text))
            print("-" * 40)
    elif mode == "dict":
        for span in get_spans(page, "dict"):
            print(f"[{span['bbox'][0]:.1f}, {span['bbox'][1]:.1f}] Font: {span.get('font')} | Text: '{span['text']}'")
    elif mode == "drawings":
        for d in page.get_drawings():
            print(d)
    elif mode == "links":
        for i, link in enumerate(page.get_links()):
            print(f"Link {i}: from={link.get('from')} uri={link.get('uri', '')}")
    doc.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Inspect PDF page content")
    parser.add_argument("pdf", help="PDF path or source key")
    parser.add_argument("page", type=int, help="Page number (1-based)")
    parser.add_argument("--mode", "-m", default="rawdict", choices=["rawdict", "blocks", "dict", "drawings", "links"])
    args = parser.parse_args()
    pdf = SOURCES.get(args.pdf, args.pdf)
    inspect_blocks(pdf, args.page - 1, args.mode)
