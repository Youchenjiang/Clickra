# ======================= L4 part 2: PDF translation, file level (exec'd from l4_block.py, inside the L4 group) =======================
# Data: l5.json, produced on the repo machine by intra.py + l5data.py (translation engines are one node; L4 draws them).
# Folder level the translation module is one big loop (E8). Here:
#   1. a dependency matrix of the folders, rows ordered to need the fewest "back" references (exact order via DP over subsets);
#      every back reference (lower-left, red) is one file referencing a file in a folder that should sit below it;
#   2. the list of those references and the file moves that remove them (checked by recomputing the matrix);
#   3. each folder's files as a layered graph (same engine as L4), with shared helpers marked.
import collections as _co

CY, CY_FILL, CY_SOFT, RED = MAG, MAG_FILL, MAG_SOFT, "#d92d20"
# translation subset of the whole-repo model (l4_model.py); the 11 engine files are one node here
def _tr_key(n):
    return "Translation/" if tr_folder(n) == "Translation" else os.path.basename(n)


N5 = {}
for _n in NODES:
    if _n.startswith(TR):
        _k = _tr_key(_n)
        _f = tr_folder(_n)
        N5.setdefault(_k, {"folder": (_f if _f.endswith("/") else _f + "/"), "lines": 0})["lines"] += NODES[_n]["lines"]
E5 = sorted({(_tr_key(a), _tr_key(b)) for a, b in NEDGES if a.startswith(TR) and b.startswith(TR) and _tr_key(a) != _tr_key(b)})
SHARED = "Pipeline/Shared/"
MOVES = {f: SHARED for f in ["PdfParagraphRoleClassifier.cs", "PdfParagraphSemanticClassifier.cs", "PdfChartLabelClassifier.cs",
                             "PdfGrayPromptClassifier.cs", "ReferenceSectionDetector.cs",
                             "PdfMaskGeometry.cs", "TableMaskRegion.cs", "PdfLayoutElement.cs", "PdfLayoutRow.cs"]}
MOVES["PdfTranslationLayoutPlanner.cs"] = "Pipeline/Rendering/"


def fold_now(n):
    return N5[n]["folder"]


def fold_after(n):
    return MOVES.get(n, N5[n]["folder"])


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
ORDER_AFT, CNT_AFT, BACK_AFT = best_order(fold_after)
E6_REFS = [(a, b) for a, b in BACK_AFT if a == "PdfParagraph.cs"]
assert len(BACK_AFT) == len(E6_REFS), BACK_AFT       # after the moves only the Model → Pipeline loop (E6) is left


def short(f):
    return f.replace("Pipeline/", "") if f != "Pipeline/" else "Pipeline/（根）"


def label_of(f):
    return {"Translation/": "Translation/（引擎）", "Pipeline/": "Pipeline/（根目錄）"}.get(f, f)


# ---- drawing helpers
def dsm(x, y, order, cnt, back, title, sub):
    """Dependency matrix: row folder references column folder; upper right = downward (fine), lower left = back (red)."""
    cell, lw, top = 22, 166, 64
    k = len(order)
    w, h = lw + k * cell + 16, top + k * cell + 40
    backset = {(fold_after(a) if order is ORDER_AFT else fold_now(a), fold_after(b) if order is ORDER_AFT else fold_now(b)) for a, b in back}
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
band(yy + 20, 0, f"翻譯模組（{len(N5) - 1} 個檔＋引擎，檔案太多，分資料夾畫）：資料夾層級看起來 10 個資料夾全部互相引用，檔案層級其實只有 {len(E5)} 條引用中的 {len(BACK_NOW)} 條在回頭（待辦 E6、E8）")
yy += 40
w1, h1 = dsm(40, yy, ORDER_NOW, CNT_NOW, BACK_NOW, "資料夾之間：現況", f"⚠ {len(BACK_NOW)} 條回頭引用 → 資料夾形成循環（待辦 E8）")
w2, h2 = dsm(40 + w1 + 12, yy, ORDER_AFT, CNT_AFT, BACK_AFT, "資料夾之間：照建議搬移後", f"只剩 {len(BACK_AFT)} 條（PdfParagraph.cs → 分析器，要改程式，待辦 E6）")
_lx = 40 + w1 + w2 + 24
_files_loops = ["檔案層級的循環只有兩組：",
                "① PdfParagraph.cs（Model/）與 4 個 Paragraphs/ 分析器",
                "   ——資料型別呼叫分析邏輯（待辦 E6，要改程式）",
                "② GrayPrompts/ 裡 Geometry ↔ RegionBuilder 兩個檔",
                "   ——同一資料夾內，可接受"]
tcard(_lx, yy, 1440 - _lx, [("怎麼讀", "h"),
                            ("資料夾依「回頭引用最少」的順序排列（精確解）。", "t"),
                            ("紅格不是整個資料夾錯，而是少數檔案放錯資料夾：", "t"),
                            ("共用的分類器、幾何型別放在高層資料夾裡，", "t"),
                            ("低層資料夾為了用它們只好回頭引用。", "t"),
                            ("", "s")] + [(t, "r" if i == 0 else "t") for i, t in enumerate(_files_loops)])
yy += max(h1, h2) + 14

# ---- fixes: which references each move removes (recomputed, not hand-counted)
def _back_with(moves):
    fo = lambda n: moves.get(n, N5[n]["folder"])
    return best_order(fo)[2]


_m1 = {f: d for f, d in MOVES.items() if d == SHARED}
_b0, _b1, _b3 = len(BACK_NOW), len(_back_with(_m1)), len(BACK_AFT)
_users = _co.defaultdict(set)
for a, b in E5:
    if fold_now(a) != fold_now(b):
        _users[b].add(fold_now(a))
fix = [(f"已完成搬移（PR #194，共 {len(MOVES)} 個檔）", "h"),
       (f"① 新資料夾 Pipeline/Shared/：被多個資料夾共用的分類器與幾何型別（已生效）", "t")]
for f in [x for x, d in MOVES.items() if d == SHARED]:
    fix.append((f"{f:<36}目前 {short(fold_now(f)):<17}被 {len(_users[f])} 個資料夾用", "m"))
fix += [("", "s"),
        (f"② PdfTranslationLayoutPlanner.cs 搬到 Rendering/：與 PdfTranslatedPdfRebuilder 互相需要（已生效）", "t"),
        ("回頭引用已由 23 條成功降至 4 條", "t"),
        ("", "s"),
        (f"③ 剩下 {_b3} 條要改程式（待辦 E6）：Model/PdfParagraph.cs 直接呼叫分析器", "r"),
        ("   建議把分析結果放進 Model（純資料），分析邏輯留在 Pipeline", "t")]
refs = [("現在的回頭引用（全部列出）", "h")]
for a, b in sorted(BACK_NOW, key=lambda t: (fold_now(t[0]), t[0], t[1])):
    refs.append((f"{short(fold_now(a))}{a} → {short(fold_now(b))}{b}", "m"))
_fw = 700
_h3 = tcard(40, yy, _fw, fix)
_h4 = tcard(40 + _fw + 12, yy, 1440 - 40 - _fw - 12, refs, stroke=RED)
yy += max(_h3, _h4) + 14

