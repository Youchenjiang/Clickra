"""Shared utilities for Clickra PDF diagnostic tools."""
import fitz
import re
from pathlib import Path
from PIL import Image
from opencc import OpenCC

# Use the Taiwan character standard for validation. Plain s2t maps common
# Taiwan forms such as "群" to rare variants such as "羣", creating false
# simplified-Chinese alarms.
_cc = OpenCC("s2tw")

SCRIPTS_DIR = Path(__file__).parent
REPO_ROOT = SCRIPTS_DIR.parent.parent
OUTPUT_DIR = REPO_ROOT / "scratch" / "output"
TEST_PDFS = REPO_ROOT / "test_pdfs"

SOURCES = {
    "2407": TEST_PDFS / "source" / "2407.11279v1_clean.pdf",
    "pentest": TEST_PDFS / "source" / "PentestAgent_Agent Pentest.pdf",
    "togll": TEST_PDFS / "source" / "TOGLL_Oracle Generation.pdf",
    "final": TEST_PDFS / "source" / "114423046_final_project.pdf",
}

TRANSLATED = {
    "2407": TEST_PDFS / "translated" / "2407.11279v1_clean_translated.pdf",
    "pentest": TEST_PDFS / "translated" / "PentestAgent_Agent Pentest_translated.pdf",
    "togll": TEST_PDFS / "translated" / "TOGLL_Oracle Generation_translated.pdf",
    "final": TEST_PDFS / "translated" / "114423046_final_project_translated.pdf",
}

CJK_RE = re.compile(r"[\u4e00-\u9fff]")
ENG_RE = re.compile(r"[A-Za-z]{3,}")
CJK_FONT_NAMES = ("DFKai", "JhengHei", "YaHei", "SimSun", "MingLiU", "PMingLiU")

X_SPLIT_DEFAULT = 300


PROJECT_MAP = {
    "sem": "final_project",
    "final": "final_project",
    "pentest": "pentest",
    "togll": "togll",
    "2407": "2407",
}


def out_dir(project="misc"):
    name = PROJECT_MAP.get(project, Path(project).stem if Path(project).suffix else project)
    d = OUTPUT_DIR / name
    d.mkdir(parents=True, exist_ok=True)
    return d


def open_pdf(path):
    if isinstance(path, str) and path in SOURCES:
        path = SOURCES[path]
    return fitz.open(str(path))


def get_spans(page, mode="dict"):
    for b in page.get_text(mode)["blocks"]:
        if "lines" not in b:
            continue
        for line in b["lines"]:
            for span in line["spans"]:
                yield span


def get_text(page, mode="text"):
    return page.get_text(mode)


def has_cjk(text):
    return bool(CJK_RE.search(text))


def count_simplified_hits(text):
    count = 0
    for c in text:
        if CJK_RE.match(c) and _cc.convert(c) != c:
            count += 1
    return count


def find_simplified_chars(text):
    return {c: text.count(c) for c in set(text) if CJK_RE.match(c) and _cc.convert(c) != c}


def find_problematic_chars(page, checks=None):
    checks = checks or ("tofu", "cjk_font_latin")
    results = []
    for b in page.get_text("rawdict")["blocks"]:
        if "lines" not in b:
            continue
        for line in b["lines"]:
            for span in line["spans"]:
                font = span.get("font", "")
                text = span.get("text", "")
                if "tofu" in checks:
                    for c in text:
                        if c in ("\x00", "\ufffd"):
                            results.append({"type": "tofu", "char": c, "font": font, "text": text[:60]})
                if "cjk_font_latin" in checks:
                    if any(fn in font for fn in CJK_FONT_NAMES):
                        for c in text:
                            o = ord(c)
                            if 0x0080 <= o <= 0x024F:
                                results.append({"type": "cjk_latin", "char": c, "codepoint": f"U+{o:04X}", "font": font, "text": text[:60]})
    return results


def zone_spans(page, y0=None, y1=None, x0=None, x1=None):
    for span in get_spans(page):
        bbox = span["bbox"]
        if y0 is not None and bbox[1] < y0:
            continue
        if y1 is not None and bbox[3] > y1:
            continue
        if x0 is not None and bbox[0] < x0:
            continue
        if x1 is not None and bbox[2] > x1:
            continue
        yield span


def render_page(pdf_path, page_idx, zoom=2.0):
    doc = fitz.open(str(pdf_path))
    page = doc[page_idx]
    pix = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom))
    img = Image.frombytes("RGB", [pix.width, pix.height], pix.samples)
    doc.close()
    return img


def render_side_by_side(orig_pdf, trans_pdf, pages, out_dir, prefix, zoom=2.0, gap=20):
    out_dir = Path(out_dir)
    o_doc = fitz.open(str(orig_pdf))
    t_doc = fitz.open(str(trans_pdf))
    mat = fitz.Matrix(zoom, zoom)
    results = []
    for p in pages:
        o_pix = o_doc[p - 1].get_pixmap(matrix=mat)
        t_pix = t_doc[p - 1].get_pixmap(matrix=mat)
        o_img = Image.frombytes("RGB", [o_pix.width, o_pix.height], o_pix.samples)
        t_img = Image.frombytes("RGB", [t_pix.width, t_pix.height], t_pix.samples)
        w = max(o_pix.width, t_pix.width)
        h = max(o_pix.height, t_pix.height)
        combo = Image.new("RGB", (w * 2 + gap, h), (240, 240, 240))
        combo.paste(o_img, (0, 0))
        combo.paste(t_img, (w + gap, 0))
        out_path = out_dir / f"{prefix}_p{p}.png"
        combo.save(out_path)
        results.append(out_path)
        print(f"p{p} -> {out_path}")
    o_doc.close()
    t_doc.close()
    return results


def render_link_overlay(pdf_path, page_idx, out_path, zoom=2.0):
    doc = fitz.open(str(pdf_path))
    page = doc[page_idx]
    shape = page.new_shape()
    for link in page.get_links():
        r = fitz.Rect(link["from"])
        shape.draw_rect(r)
    shape.finish(color=(1, 0, 0), width=0.5)
    shape.commit()
    pix = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), annots=True)
    pix.save(str(out_path))
    doc.close()
    return out_path


def validate_link_alignment(src_pdf, trn_pdf):
    ds = fitz.open(str(src_pdf))
    dt = fitz.open(str(trn_pdf))
    ok = bad = 0
    samples = []
    for pno in range(min(len(ds), len(dt))):
        ps, pt = ds[pno], dt[pno]
        ls, lt = ps.get_links(), pt.get_links()
        for i in range(min(len(ls), len(lt))):
            r = fitz.Rect(ls[i]["from"])
            words_s = ps.get_text("words", clip=r)
            expected = "".join(w[4] for w in words_s).strip()
            if not expected:
                continue
            r_t = fitz.Rect(lt[i]["from"])
            words_t = pt.get_text("words", clip=r_t)
            actual = "".join(w[4] for w in words_t).strip()
            exp_core = re.sub(r"\s+", "", expected)
            act_core = re.sub(r"\s+", "", actual)
            m = re.search(r"\[[^\]]+\]", exp_core)
            if m:
                exp_core = m.group(0)
            if exp_core in actual or actual in expected or exp_core == act_core:
                ok += 1
            else:
                bad += 1
                if len(samples) < 15:
                    samples.append((pno + 1, i, expected, actual[:60]))
    ds.close()
    dt.close()
    return {"ok": ok, "bad": bad, "rate": ok / (ok + bad) * 100 if ok + bad else 0, "samples": samples}
