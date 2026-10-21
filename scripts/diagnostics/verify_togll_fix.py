"""Verify TOGLL fix: p1 title, p7/p8 findings, p8 RQ4, regression PNGs."""
import fitz
import re
from pathlib import Path
from _pdf_utils import out_dir, SOURCES

OUT = out_dir("togll")
TRN = Path(str(SOURCES["togll"]).replace(".pdf", "_translated.pdf"))
SRC = SOURCES["togll"]

REGRESSION = [
    ("2407_p1", Path(str(SOURCES["2407"]).replace(".pdf", "_translated.pdf")), 1),
    ("2407_p13", Path(str(SOURCES["2407"]).replace(".pdf", "_translated.pdf")), 13),
    ("final_p14", Path(str(SOURCES["final"]).replace(".pdf", "_translated.pdf")), 14),
    ("sem_p1", Path(str(SOURCES["sem"]).replace(".pdf", "_translated.pdf")), 1),
]


def render(pdf: Path, page_num: int, name: str) -> str:
    doc = fitz.open(str(pdf))
    pix = doc[page_num - 1].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
    out = OUT / f"TOGLL_fix_{name}_p{page_num}.png"
    pix.save(str(out))
    doc.close()
    return str(out)


def zone_text(page, y0, y1, x0=0, x1=None):
    if x1 is None:
        x1 = page.rect.width
    parts = []
    for b in page.get_text("dict")["blocks"]:
        if b.get("type") != 0:
            continue
        for line in b["lines"]:
            for sp in line["spans"]:
                t = sp["text"].strip()
                if not t:
                    continue
                bbox = sp["bbox"]
                cy = (bbox[1] + bbox[3]) / 2
                cx = (bbox[0] + bbox[2]) / 2
                if y0 <= cy <= y1 and x0 <= cx <= x1:
                    parts.append(t)
    return " ".join(parts)


def has_cjk(s: str) -> bool:
    return bool(re.search(r"[\u4e00-\u9fff]", s))


def eng_ghost_ratio(s: str) -> float:
    words = re.findall(r"[A-Za-z]{4,}", s)
    cjk = re.findall(r"[\u4e00-\u9fff]", s)
    if not cjk:
        return 1.0 if words else 0.0
    return len(words) / (len(words) + len(cjk))


doc = fitz.open(str(TRN))
h = doc[0].rect.height

# p1 title zone (top ~120pt in fitz coords)
p1_text = zone_text(doc[0], 40, 130)
p1_ok = has_cjk(p1_text) and "with LLMs" not in p1_text and ("LLM" in p1_text or "??" in p1_text or "??" in p1_text)

# p7 findings box (left column, mid page) - fitz y ~350-450 for RQ2 area
p7_left = zone_text(doc[6], 280, 380, 40, 300)
p7_ok = has_cjk(p7_left) and "Findings" not in p7_left and ("RQ2" in p7_left or "??" in p7_left or "TOGLL" in p7_left)

# p8 RQ3 finding + RQ4 intro
p8_find = zone_text(doc[7], 300, 400, 40, 300)
p8_rq4 = zone_text(doc[7], 130, 200, 40, 300)
p8_rq4_right = zone_text(doc[7], 130, 200, 300, 580)
p8_find_ok = has_cjk(p8_find) and "Finding" not in p8_find
p8_rq4_ok = has_cjk(p8_rq4) and eng_ghost_ratio(p8_rq4 + " " + p8_rq4_right) < 0.35

pngs = {
    "p1": render(TRN, 1, "trans"),
    "p7": render(TRN, 7, "trans"),
    "p8": render(TRN, 8, "trans"),
    "p1_src": render(SRC, 1, "src"),
    "p7_src": render(SRC, 7, "src"),
    "p8_src": render(SRC, 8, "src"),
}

reg_status = {}
for name, path, pnum in REGRESSION:
    if not path.exists():
        reg_status[name] = "MISSING"
        continue
    out = render(path, pnum, f"reg_{name}")
    reg_status[name] = f"OK -> {out}"

doc.close()

print("=== TOGLL Fix Verification ===")
print(f"p1_title_ok={p1_ok} text_sample={p1_text[:120]!r}")
print(f"p7_findings_ok={p7_ok} text_sample={p7_left[:120]!r}")
print(f"p8_rq3_finding_ok={p8_find_ok} text_sample={p8_find[:120]!r}")
print(f"p8_rq4_ok={p8_rq4_ok} left={p8_rq4[:80]!r} right={p8_rq4_right[:80]!r}")
print("pngs:", pngs)
print("regression:", reg_status)
