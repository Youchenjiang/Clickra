# ======================= L4 part 2: PDF translation, file level (exec'd from l4_block.py, inside the L4 group) =======================
# Data: the whole-repo model (l4_model.py); the translation engines are one node here (they have their own panel).
#   1. a dependency matrix of the translation folders, rows ordered to need the fewest "back" references
#      (exact order via DP over subsets); a back reference (lower-left, red) is a file using a file in a folder that
#      should sit below it — any back reference means the folders form a cycle again (E8 regression);
#   2. the back references, if any, and the file-level loops (strongly connected components);
#   3. after this block, each folder's files are drawn by the normal L4 panels.
import collections as _co

CY, CY_FILL, CY_SOFT, RED = MAG, MAG_FILL, MAG_SOFT, "#d92d20"
# translation subset of the whole-repo model (l4_model.py); the 11 engine files are one node here
def _tr_key(n):
    return "Translation/" if tr_folder(n) == "Translation" else posixpath.basename(n)


N5 = {}
for _n in NODES:
    if _n.startswith(TR):
        _k = _tr_key(_n)
        _f = tr_folder(_n)
        N5.setdefault(_k, {"folder": (_f if _f.endswith("/") else _f + "/"), "lines": 0})["lines"] += NODES[_n]["lines"]
E5 = sorted({(_tr_key(a), _tr_key(b)) for a, b in NEDGES if a.startswith(TR) and b.startswith(TR) and _tr_key(a) != _tr_key(b)})
def fold_now(n):
    return N5[n]["folder"]


def best_order(fo):
    """Folder order (callers first) with the fewest file references pointing back up; exact for this size."""
    cnt = _co.Counter((fo(a), fo(b)) for a, b in E5 if fo(a) != fo(b))
    fs = sorted({fo(n) for n in N5}); k = len(fs)
    w = [[cnt.get((fs[i], fs[j]), 0) for j in range(k)] for i in range(k)]
    inf = 10 ** 9; dp = [inf] * (1 << k); par = [None] * (1 << k); dp[0] = 0
    for m in range(1 << k):
        if dp[m] == inf:
            continue
        for j in range(k):
            if not m >> j & 1:
                c = dp[m] + sum(w[j][s] for s in range(k) if m >> s & 1)
                if c < dp[m | 1 << j]:
                    dp[m | 1 << j] = c; par[m | 1 << j] = j
    m, order = (1 << k) - 1, []
    while m:
        j = par[m]; order.append(fs[j]); m ^= 1 << j
    order = order[::-1]
    pos = {f: i for i, f in enumerate(order)}
    back = [(a, b) for a, b in E5 if fo(a) != fo(b) and pos[fo(b)] < pos[fo(a)]]
    return order, cnt, back


ORDER_NOW, CNT_NOW, BACK_NOW = best_order(fold_now)
_LOOPS = [sorted(c) for c in scc(list(N5), E5) if len(c) > 1]


PIPELINE_ROOT = "Pipeline/"


def short(f):
    return f.replace(PIPELINE_ROOT, "") if f != PIPELINE_ROOT else "Pipeline/（根）"


def label_of(f):
    return {"Translation/": "Translation/（引擎）", PIPELINE_ROOT: "Pipeline/（根目錄）"}.get(f, f)


# ---- drawing helpers
def dsm(x, y, order, cnt, back, title, sub, fo=None):
    """Dependency matrix: row folder references column folder; upper right = downward (fine), lower left = back (red)."""
    cell, lw, top = 22, 166, 64
    k = len(order)
    w, h = lw + k * cell + 16, top + k * cell + 40
    fo = fo or fold_now
    backset = {(fo(a), fo(b)) for a, b in back}
    A(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="#fff" stroke="{CY}" stroke-width="1.6"/>'
      f'<text x="{x + 14}" y="{y + 22}" font-size="13.5" font-weight="700" fill="#101828">{e(title)}</text>'
      f'<text x="{x + 14}" y="{y + 39}" font-size="10" fill="{RED if back else "#067647"}">{e(sub)}</text>')
    gx, gy = x + lw, y + top
    for j in range(k):
        A(f'<text x="{gx + j * cell + cell / 2}" y="{gy - 6}" font-size="9.5" fill="#667085" text-anchor="middle">{j + 1}</text>')
    for i, fa in enumerate(order):
        A(f'<text x="{gx - 8}" y="{gy + i * cell + 15}" font-size="10" fill="#344054" text-anchor="end" {MONO4}>{i + 1:>2} {e(label_of(fa).replace("Pipeline/", "") if fa not in ("Pipeline/",) else "Pipeline/（根目錄）")}</text>')
        for j, fb in enumerate(order):
            cx, cy_ = gx + j * cell, gy + i * cell
            if i == j:
                A(f'<rect x="{cx}" y="{cy_}" width="{cell}" height="{cell}" fill="#eaecf0" stroke="#fff"/>'); continue
            c = cnt.get((fa, fb), 0)
            if not c:
                A(f'<rect x="{cx}" y="{cy_}" width="{cell}" height="{cell}" fill="#fcfcfd" stroke="#f2f4f7"/>'); continue
            bad = (fa, fb) in backset
            A(f'<rect x="{cx}" y="{cy_}" width="{cell}" height="{cell}" fill="{"#fee4e2" if bad else "#fce7f6"}" stroke="#fff"/>'
              f'<text x="{cx + cell / 2}" y="{cy_ + 15}" font-size="10" font-weight="{700 if bad else 400}" fill="{"#b42318" if bad else "#c11574"}" text-anchor="middle">{c}</text>')
    A(f'<text x="{x + 14}" y="{y + h - 12}" font-size="9.5" fill="#667085">列＝引用方、欄＝被引用方；數字＝檔案引用數。右上＝往下引用，左下紅＝回頭引用</text>')
    return w, h


def tcard(x, y, w, lines, stroke=CY):
    """Plain text card: (text, kind) with kind h=heading, t=text, m=monospace, r=warning, s=spacer."""
    hh = {"h": 20, "t": 16, "m": 14, "r": 16, "s": 8}
    h = 18 + sum(hh[k] for _, k in lines) + 6
    A(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="#fff" stroke="{stroke}" stroke-width="1.6"/>')
    yy_ = y + 14
    for t, k in lines:
        yy_ += hh[k]
        if k == "h":
            A(f'<text x="{x + 14}" y="{yy_ - 4}" font-size="12.5" font-weight="700" fill="#101828">{e(t)}</text>')
        elif k == "t":
            A(f'<text x="{x + 14}" y="{yy_ - 4}" font-size="11" fill="#475467">{e(t)}</text>')
        elif k == "r":
            A(f'<text x="{x + 14}" y="{yy_ - 4}" font-size="11" fill="#b42318">{e(t)}</text>')
        elif k == "m":
            A(f'<text x="{x + 22}" y="{yy_ - 3}" font-size="9.8" fill="#344054" {MONO4} xml:space="preserve">{e(t)}</text>')
    return h


# ---- translation part of L4 (starts at the current yy)
band(yy + 20, 0, f"翻譯模組（{len(N5) - 1} 個檔＋引擎，檔案多，分資料夾畫）：{len(E5)} 條檔案引用中，{len(BACK_NOW)} 條在回頭（待辦 E6、E8）")
yy += 40
_ok = not BACK_NOW
w1, h1 = dsm(40, yy, ORDER_NOW, CNT_NOW, BACK_NOW, "資料夾之間的引用",
             "✓ 沒有回頭引用：資料夾之間沒有循環" if _ok else f"⚠ {len(BACK_NOW)} 條回頭引用 → 資料夾又形成循環（待辦 E8）")
_lx = 40 + w1 + 12
_info = [("怎麼讀", "h"),
         ("資料夾依「回頭引用最少」的順序排列（精確解）。", "t"),
         ("右上＝往下引用（正常），左下紅格＝回頭引用。", "t"),
         ("", "s"),
         ("歷史", "h"),
         ("10/16：23 條回頭引用，10 個資料夾全部互相引用。", "t"),
         ("PR #194 把 9 個共用分類器／幾何型別搬進 Pipeline/Shared/、", "t"),
         ("PdfTranslationLayoutPlanner.cs 搬進 Rendering/，並修掉 E6。", "t"),
         ("", "s"),
         ("檔案層級的循環", "h")]
_info += [("無", "t")] if not _LOOPS else [("、".join(c), "r") for c in _LOOPS]
if BACK_NOW:
    _info += [("", "s"), ("現在的回頭引用", "h")] + [(f"{short(fold_now(a))}{a} → {short(fold_now(b))}{b}", "m")
                                                 for a, b in sorted(BACK_NOW, key=lambda t: (fold_now(t[0]), t[0], t[1]))]
_h2 = tcard(_lx, yy, 1440 - _lx, _info, stroke=RED if BACK_NOW else CY)
yy += max(h1, _h2) + 14
