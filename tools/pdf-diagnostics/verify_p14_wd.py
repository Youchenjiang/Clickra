"""Verify WORK DIVISION table text on page 14 after reclassify fix."""
import fitz
import re
from pathlib import Path
from _pdf_utils import out_dir, SOURCES

SRC = SOURCES["final"]
TRN = Path(str(SRC).replace(".pdf", "_translated.pdf"))
OUT = out_dir("final_project")

KEYWORDS = [
    "Work description", "Image embedding", "workflow design", "Search Related",
    "Establish the workflow", "Writing the papper", "Searching Text", "Embedding topic",
    "Do Experiment", "Generate final text", "Retrieval and Cross", "Fusion Model",
    "Explanation of the model", "Dataset design", "External evaluation",
    "Baseline comparison", "Research questions", "Contribution", "Name",
]
NAMES = ["???", "???", "???", "???"]


def render_p14():
    zoom = 2.0
    mat = fitz.Matrix(zoom, zoom)
    for label, path in [("src", SRC), ("trn", TRN)]:
        doc = fitz.open(path)
        pix = doc[13].get_pixmap(matrix=mat, alpha=False)
        out = OUT / f"final_project_p14_{label}.png"
        pix.save(str(out))
        print(f"Rendered {out.name}")
        doc.close()


def check_text(label, path):
    doc = fitz.open(path)
    text = doc[13].get_text()
    doc.close()
    found = [k for k in KEYWORDS if k.lower() in text.lower()]
    names = [n for n in NAMES if n in text]
    lines = [ln.strip() for ln in text.splitlines() if ln.strip()]
    wd_idx = next((i for i, l in enumerate(lines) if "WORK DIVISION" in l.upper()), None)
    print(f"\n=== {label} page 14 ===")
    print(f"keywords: {len(found)}/{len(KEYWORDS)}")
    for k in found:
        print(f"  + {k}")
    print(f"names: {names}")
    if wd_idx is not None:
        print("WD block:")
        for ln in lines[wd_idx : wd_idx + 22]:
            print(f"  | {ln[:95]}")
    return len(found), names


def main():
    render_p14()
    src_kw, src_names = check_text("src", SRC)
    trn_kw, trn_names = check_text("trn", TRN)
    ok = trn_kw >= src_kw and len(trn_names) == len(src_names)
    print(f"\nPASS={ok} (trn keywords {trn_kw} vs src {src_kw}, names {trn_names})")


if __name__ == "__main__":
    main()
