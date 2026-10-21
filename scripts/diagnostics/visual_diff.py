"""Visual diff: render orig and trans, output pixel difference overlay."""
import argparse
import fitz
import numpy as np
from PIL import Image
from pathlib import Path
from _pdf_utils import SOURCES, out_dir


def visual_diff(orig_pdf, trans_pdf, pages, out_dir_path, prefix="diff", zoom=2.0, threshold=30):
    o_doc = fitz.open(str(orig_pdf))
    t_doc = fitz.open(str(trans_pdf))
    mat = fitz.Matrix(zoom, zoom)
    results = []

    for p in pages:
        if p < 1 or p > max(len(o_doc), len(t_doc)):
            continue
        o_pix = o_doc[p - 1].get_pixmap(matrix=mat, alpha=False)
        t_pix = t_doc[p - 1].get_pixmap(matrix=mat, alpha=False)

        o_img = np.frombuffer(o_pix.samples, dtype=np.uint8).reshape(o_pix.height, o_pix.width, 3)
        t_img = np.frombuffer(t_pix.samples, dtype=np.uint8).reshape(t_pix.height, t_pix.width, 3)

        h = min(o_img.shape[0], t_img.shape[0])
        w = min(o_img.shape[1], t_img.shape[1])
        diff = np.abs(o_img[:h, :w].astype(int) - t_img[:h, :w].astype(int)).sum(axis=2)

        mask = (diff > threshold).astype(np.uint8) * 255
        mask_img = Image.fromarray(mask, mode="L")

        overlay = Image.new("RGB", (w, h), (255, 255, 255))
        t_pil = Image.frombytes("RGB", (t_pix.width, t_pix.height), t_pix.samples)
        overlay = Image.composite(t_pil, overlay, mask_img)

        changed_pixels = int(mask.sum() / 255)
        total_pixels = w * h
        pct = changed_pixels / total_pixels * 100

        out_path = out_dir_path / f"{prefix}_p{p}.png"
        overlay.save(out_path)
        results.append(out_path)
        print(f"Page {p}: {changed_pixels}/{total_pixels} changed ({pct:.1f}%) -> {out_path}")

    o_doc.close()
    t_doc.close()
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Visual diff between original and translated PDF")
    parser.add_argument("--orig", "-a", default="pentest", help="Original PDF or source key")
    parser.add_argument("--trans", "-b", help="Translated PDF or source key")
    parser.add_argument("pages", nargs="+", type=int, help="Page numbers (1-based)")
    parser.add_argument("--out", "-o", help="Output directory")
    parser.add_argument("--prefix", "-p", default="diff", help="Output prefix")
    parser.add_argument("--threshold", "-t", type=int, default=30, help="Pixel diff threshold")
    parser.add_argument("--zoom", "-z", type=float, default=2.0)
    args = parser.parse_args()
    key = args.orig
    orig = SOURCES.get(key, key)
    trans = SOURCES.get(args.trans, args.trans) if args.trans else str(orig).replace(".pdf", "_translated.pdf")
    out = Path(args.out) if args.out else out_dir(key)
    visual_diff(orig, trans, args.pages, out, args.prefix, args.zoom, args.threshold)
