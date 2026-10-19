# ======================= L4: inside each L3 module (exec'd from l3_block.py) =======================
# Data: l4.json, produced on the repo machine by intra.py + l4data.py (file-to-file type references inside each module).
# Layout: layered (Sugiyama-style) graph per module —
#   1. files that reference each other in a loop are merged into one red dashed group (strongly connected component);
#   2. rank = longest path from a caller, so callers sit on top and helpers at the bottom;
#   3. an edge that skips ranks gets an invisible waypoint in every rank it crosses, so it runs in its own lane;
#   4. order inside a rank is improved by barycentre sweeps (down / up / down) to cut crossings;
#   5. lines leave and enter boxes at spread-out ports and turn only inside the gaps between ranks.
import json as _json

MAG, MAG_FILL, MAG_SOFT = "#c11574", "#fdf2fa", "#f670c7"
L4_TOP = L3_TOP + L3H + 34
D4 = _json.load(open("l4.json", encoding="utf-8"))
MONO4 = 'font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"'
CH_W, NODE_H, GAP_X, PAD = 6.0, 36, 18, 18
PORT, LANE, GAP_PAD = 16, 8, 14
GROUP_MAX_W = 1000
PCOL, PFILL, PSOFT = MAG, MAG_FILL, MAG_SOFT   # panel colours
PUN = False   # True while drawing an unshipped panel (Fluent): grey, dashed, hidden by the "隱藏未發佈" toggle


def _un(extra=""):
    return (extra + " unshipped").strip() if PUN else extra


def _dash(d="6 4"):
    return f' stroke-dasharray="{d}"' if PUN else ""


def sub_w(t):   # rough width of a 9px mixed CJK/latin string
    return sum(9.2 if ord(c) > 0x2e80 else 5.3 for c in t)


def scc(nodes, edges):
    adj = {n: [] for n in nodes}
    for a, b in edges:
        adj[a].append(b)
    idx, low, st, on, out, k = {}, {}, [], set(), [], [0]

    def go(v):
        idx[v] = low[v] = k[0]; k[0] += 1; st.append(v); on.add(v)
        for w in adj[v]:
            if w not in idx:
                go(w); low[v] = min(low[v], low[w])
            elif w in on:
                low[v] = min(low[v], idx[w])
        if low[v] == idx[v]:
            comp = []
            while True:
                w = st.pop(); on.discard(w); comp.append(w)
                if w == v:
                    break
            out.append(sorted(comp))
    for n in nodes:
        if n not in idx:
            go(n)
    return out


def mono_w(t, px=9.5):   # width of a monospace line that may contain CJK
    return sum(px if ord(c) > 0x2e80 else px * 0.6 for c in t)


STACK_ROW = 20


def node_box(label, info):
    if info.get("stack"):                          # cross-panel stand-ins: one chip per referenced file
        return max(mono_w(t) for _, t in info["stack"]) + 34, len(info["stack"]) * STACK_ROW + 6
    if label.endswith("/") or info.get("card"):    # folder / partial-class card: title + every file name
        return 236, 30 + 13 * len(wrap(sorted(info["files"]), 34)) + 8
    return max(len(label) * CH_W + 22, sub_w(info.get("sub", "")) + 22, 96), NODE_H


def layout(data, maxw=1340):
    """Layered layout; a rank wider than maxw pushes some of its nodes (sinks first) one rank down and retries."""
    minr = {}
    for _ in range(60):
        Lo = _layout_once(data, minr)
        wide = [r for r in range(Lo["nr"]) if _rank_w(Lo, r) > maxw - 2 * PAD]
        if not wide:
            return Lo
        r = wide[0]
        size, order = Lo["size"], Lo["order"]
        has_out = {p for p, q in Lo["links"]}
        cand = sorted([i for i in order[r] if not isinstance(i, tuple)], key=lambda i: (i in has_out, -size[i][0]))
        excess = _rank_w(Lo, r) - (maxw - 2 * PAD)
        moved, left = False, len(cand)
        for i in cand:
            if excess <= 0 or left <= 1:
                break
            minr[i] = r + 1; excess -= size[i][0] + GAP_X; moved = True; left -= 1
        assert moved, ("rank too wide and nothing to move", r, [(Lo["comps"][i] if not isinstance(i, tuple) else "dummy", size[i][0]) for i in order[r]])
    raise AssertionError("layout did not converge")


def _rank_w(Lo, r):
    return sum(Lo["size"][i][0] for i in Lo["order"][r]) + GAP_X * (len(Lo["order"][r]) - 1)


def _layout_once(data, minr):
    nodes = list(data["nodes"])
    edges = [tuple(x) for x in data["edges"]]
    comps = scc(nodes, edges)
    comp_of = {n: i for i, c in enumerate(comps) for n in c}
    cedges = sorted({(comp_of[a], comp_of[b]) for a, b in edges if comp_of[a] != comp_of[b]})
    rank = {i: minr.get(i, 0) for i in range(len(comps))}
    for _ in range(len(comps) + 1):
        for a, b in cedges:
            rank[b] = max(rank[b], rank[a] + 1)
    size, grid = {}, {}
    for i, c in enumerate(comps):
        if len(c) == 1:
            size[i] = node_box(c[0], data["nodes"][c[0]])
        else:
            boxes = [node_box(n, data["nodes"][n]) for n in c]
            cw = max(b[0] for b in boxes)
            cols = max(1, min(len(c), int((GROUP_MAX_W - 24 + 12) // (cw + 12))))
            rows_ = [boxes[r:r + cols] for r in range(0, len(boxes), cols)]
            gh = sum(max(b[1] for b in r) for r in rows_) + 10 * (len(rows_) - 1)
            size[i] = (cols * cw + (cols - 1) * 12 + 24, gh + 40)
            grid[i] = (cols, cw)
    chains = {}
    for a, b in cedges:
        ch = [a]
        for r in range(rank[a] + 1, rank[b]):
            d = ("d", a, b, r)
            rank[d] = r; size[d] = (10, 0); ch.append(d)
        ch.append(b)
        chains[(a, b)] = ch
    links = [(ch[k], ch[k + 1]) for ch in chains.values() for k in range(len(ch) - 1)]
    for i in list(size):                           # wide enough that every port gets its own slot
        if isinstance(i, tuple):
            continue
        deg = max(sum(1 for p, q in links if p == i), sum(1 for p, q in links if q == i))
        size[i] = (max(size[i][0], (deg - 1) * PORT + 24), size[i][1])
    nr = max(rank.values()) + 1
    order = {r: [i for i in rank if rank[i] == r] for r in range(nr)}
    preds = {i: [p for p, q in links if q == i] for i in rank}
    succs = {i: [q for p, q in links if p == i] for i in rank}
    for sweep in ("down", "up", "down", "up", "down"):
        rs = range(1, nr) if sweep == "down" else range(nr - 2, -1, -1)
        for r in rs:
            nb = preds if sweep == "down" else succs
            ref = {i: k for rr in order for k, i in enumerate(order[rr])}

            def key(i, nb=nb, ref=ref):
                ps = [ref[j] for j in nb[i]]
                return sum(ps) / len(ps) if ps else ref[i]
            order[r].sort(key=key)
    nat_w = max(sum(size[i][0] for i in order[r]) + GAP_X * (len(order[r]) - 1) for r in range(nr)) + 2 * PAD
    rowh = {r: max([size[i][1] for i in order[r]] + [0]) for r in range(nr)}
    nlinks = {r: sum(1 for p, q in links if rank[p] == r) for r in range(nr)}
    return dict(comps=comps, rank=rank, size=size, grid=grid, chains=chains, links=links, order=order, nr=nr,
                nat_w=nat_w, rowh=rowh, nlinks=nlinks, data=data)


def draw_panel(pid, Lo, x, y, w, title, sub, foot):
    comps, rank, size, order, nr = Lo["comps"], Lo["rank"], Lo["size"], Lo["order"], Lo["nr"]
    data = Lo["data"]
    xs = {}
    for r in range(nr):
        tw = sum(size[i][0] for i in order[r]) + GAP_X * (len(order[r]) - 1)
        cx = x + (w - tw) / 2
        for i in order[r]:
            xs[i] = cx
            cx += size[i][0] + GAP_X
    out_l, in_l = {}, {}
    for p, q in Lo["links"]:
        out_l.setdefault(p, []).append(q); in_l.setdefault(q, []).append(p)

    def ports(i, others):
        px, pw = xs[i], size[i][0]
        if isinstance(i, tuple):
            return {o: px + pw / 2 for o in others}
        if data["nodes"][comps[i][0]].get("stack"):
            return {o: px + 8 for o in others}
        others = sorted(others, key=lambda o: xs[o] + size[o][0] / 2)
        n = len(others)
        span = PORT * (n - 1)
        return {o: px + pw / 2 - span / 2 + PORT * k for k, o in enumerate(others)}
    outp = {i: ports(i, v) for i, v in out_l.items()}
    inp = {i: ports(i, v) for i, v in in_l.items()}
    # one private lane per bent link in each gap; ordered so same-direction links never cross
    lane_of, nlane = {}, {}
    for r in range(nr):
        bent = [(p, q) for p, q in Lo["links"] if rank[p] == r and abs(outp[p][q] - inp[q][p]) > 0.5]
        right = sorted([l for l in bent if inp[l[1]][l[0]] > outp[l[0]][l[1]]], key=lambda l: -outp[l[0]][l[1]])
        left = sorted([l for l in bent if inp[l[1]][l[0]] < outp[l[0]][l[1]]], key=lambda l: outp[l[0]][l[1]])
        for k, l in enumerate(right + left):
            lane_of[l] = k
        nlane[r] = len(bent)
    pos, row_top, cy = {}, {}, y + 50
    for r in range(nr):
        for i in order[r]:
            pos[i] = (xs[i], cy, size[i][0], size[i][1])
        row_top[r] = cy
        cy += Lo["rowh"][r] + 2 * GAP_PAD + LANE * max(nlane[r] - 1, 0)
    ph = cy - y - 10 + 15 * len(foot) + 8
    A(f'<g class="{_un("node")}" data-id="{pid}" data-search="{e((title + " " + sub).lower())}"><rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{ph:.1f}" rx="12" fill="{PFILL}" stroke="{PCOL}" stroke-width="1.6"{_dash("8 5")}/>'
      f'<text x="{x + 14:.1f}" y="{y + 22:.1f}" font-size="13.5" font-weight="700" fill="#101828">{e(title)}</text>'
      f'<text x="{x + 14:.1f}" y="{y + 37:.1f}" font-size="9.5" fill="#98a2b3" {MONO4}>{e(sub)}</text>')
    for j, l in enumerate(foot):
        A(f'<text x="{x + 14:.1f}" y="{y + ph - 12 - 15 * (len(foot) - 1 - j):.1f}" font-size="10" fill="{"#b42318" if l.startswith("⚠") else "#667085"}">{e(l)}</text>')
    A('</g>')
    for (a, b), ch in Lo["chains"].items():
        pts = [(outp[ch[0]][ch[1]], pos[ch[0]][1] + pos[ch[0]][3])]
        for k in range(len(ch) - 1):
            p, q = ch[k], ch[k + 1]
            x1, x2 = outp[p][q], inp[q][p]
            r = rank[p]
            ym = row_top[r] + Lo["rowh"][r] + GAP_PAD + LANE * lane_of.get((p, q), 0)
            pts += [(x1, ym), (x2, ym), (x2, row_top[rank[q]] + (Lo["rowh"][rank[q]] if isinstance(q, tuple) else 0))]
        clean = [pts[0]]
        for pt in pts[1:]:
            if abs(pt[0] - clean[-1][0]) > 0.1 or abs(pt[1] - clean[-1][1]) > 0.1:
                clean.append(pt)
        src = comps[a][0] if len(comps[a]) == 1 else f"{pid}_loop{a}"
        dst = comps[b][0] if len(comps[b]) == 1 else f"{pid}_loop{b}"
        path(clean, src=f"{pid}:{src}", dst=f"{pid}:{dst}", color=PSOFT, width=1.2, dashed=PUN, cls=_un())
    for i, c in enumerate(comps):
        px, py, pw, ph_ = pos[i]
        if len(c) == 1:
            if data["nodes"][c[0]].get("stack"):
                srcs = [a for a, b in data["edges"] if b == c[0]]
                draw_stack(pid, c[0], data["nodes"][c[0]], px, py, pw, ph_, f"{pid}:{srcs[0]}")
            else:
                draw_node(pid, c[0], data["nodes"][c[0]], px, py, pw, ph_)
            continue
        A(f'<g class="{_un("node")}" data-id="{pid}:{pid}_loop{i}" data-search="互相引用 {e(" ".join(c).lower())}"><rect x="{px:.1f}" y="{py:.1f}" width="{pw:.1f}" height="{ph_:.1f}" rx="10" fill="#fef3f2" stroke="#d92d20" stroke-width="1.4" stroke-dasharray="6 4"/>'
          f'<text x="{px + 12:.1f}" y="{py + 21:.1f}" font-size="11" font-weight="700" fill="#b42318">⚠ 互相引用（{len(c)} 個，彼此有循環）</text></g>')
        cols, cw = Lo["grid"][i]
        boxes = [(n, node_box(n, data["nodes"][n])) for n in c]
        yy_ = py + 32
        for r0 in range(0, len(boxes), cols):
            row = boxes[r0:r0 + cols]
            rh = max(b[1][1] for b in row)
            for j, (n, _) in enumerate(row):
                draw_node(pid, n, data["nodes"][n], px + 12 + j * (cw + 12), yy_, cw, rh)
            yy_ += rh + 10
    return ph


def draw_stack(pid, label, info, x, y, w, h, src_id):
    """Stand-ins for files in other panels: a bus down the left edge, one arrow per referenced file.
    Each chip carries the real file's data-id, so clicking either one lights up both."""
    A(f'<rect class="{_un()}" x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="6" fill="none" stroke="#d0d5dd" stroke-dasharray="3 3"/>')
    last = y + 3 + (len(info["stack"]) - 1) * STACK_ROW + 9
    path([(x + 8, y), (x + 8, last)], src=src_id, dst=f"{pid}:{label}", color=PSOFT, width=1.2, arrow=False, dashed=PUN, cls=_un())
    for k, (tid, text) in enumerate(info["stack"]):
        cy_ = y + 3 + k * STACK_ROW
        path([(x + 8, cy_ + 9), (x + 18, cy_ + 9)], src=src_id, dst=tid, color=PSOFT, width=1.2, cls=_un())
        A(f'<g class="{_un("node stub")}" data-id="{e(tid)}" data-search="{e(text.lower())}"><rect x="{x + 18:.1f}" y="{cy_:.1f}" width="{w - 22:.1f}" height="{STACK_ROW - 2}" rx="4" fill="#f9fafb" stroke="#98a2b3" stroke-dasharray="4 3"/>'
          f'<text x="{x + 24:.1f}" y="{cy_ + 12.5:.1f}" font-size="9.5" fill="#475467" {MONO4}>{e(text)}</text></g>')


def draw_node(pid, label, info, x, y, w, h):
    lines = info["lines"]
    A(f'<g class="{_un("node")}" data-id="{pid}:{label}" data-search="{e((label + " " + " ".join(info["files"])).lower())}"><rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="7" fill="#fff" stroke="{PCOL}" stroke-width="1.3"{_dash()}/>')
    if label.endswith("/") or info.get("card"):
        A(f'<text x="{x + 10:.1f}" y="{y + 17:.1f}" font-size="11" font-weight="700" fill="#101828" {MONO4}>{e(label)}</text>'
          f'<text x="{x + w - 8:.1f}" y="{y + 17:.1f}" font-size="9" fill="#98a2b3" text-anchor="end">{len(info["files"])} 檔・{lines} 行</text>')
        for j, f in enumerate(wrap(sorted(info["files"]), 34)):
            A(f'<text x="{x + 10:.1f}" y="{y + 32 + j * 13:.1f}" font-size="9.5" fill="#344054" {MONO4} xml:space="preserve">{e(f)}</text>')
    else:
        more = f"・{len(info['files'])} 檔" if len(info["files"]) > 1 else ""
        A(f'<text x="{x + 10:.1f}" y="{y + 15:.1f}" font-size="10" fill="#101828" {MONO4}>{e(label)}</text>'
          f'<text x="{x + 10:.1f}" y="{y + 29:.1f}" font-size="9" fill="{"#b42318" if info.get("warn") else "#98a2b3"}">{e(info.get("sub", f"{lines} 行{more}"))}</text>')
    A('</g>')


# ---- L4 section: every .cs file under src/, grouped into panels
exec(open('l4_model.py', encoding='utf-8').read())
L4_AT = len(o)
A(f'<g transform="translate(0,{L4_TOP})">')
_sec4_at = len(o)
PANEL_INFO = {  # key: (title, folder line, short name used on stand-ins)
    "entry": ("入口：ClickraShell・ClickraLauncher・Shared", "src/ClickraShell/、src/ClickraLauncher/、src/Shared/", "入口"),
    "cli": ("Clickra.CLI（Native 畫面＋CLI）", "src/Clickra.CLI/", "CLI"),
    "fluent": ("Clickra.Fluent（保留・目前未發佈；灰虛線）", "src/Clickra.Fluent/", "Fluent"),
    "capp": ("Core 應用層與共用畫面狀態", "Core/Application/、Core/Presentation/", "Core 應用"),
    "m_image": ("模組 Image", "Core/Modules/Image/", "Image"),
    "m_markdown": ("模組 Markdown", "Core/Modules/Markdown/", "Markdown"),
    "m_office": ("模組 Office", "Core/Modules/Office/", "Office"),
    "m_pdfcompression": ("模組 PdfCompression", "Core/Modules/PdfCompression/", "PdfCompression"),
    "m_pdfdecrypt": ("模組 PdfDecrypt", "Core/Modules/PdfDecrypt/", "PdfDecrypt"),
    "m_pdfmerge": ("模組 PdfMerge", "Core/Modules/PdfMerge/", "PdfMerge"),
    "m_pdfsplit": ("模組 PdfSplit", "Core/Modules/PdfSplit/", "PdfSplit"),
    "m_settings": ("模組 Settings", "Core/Modules/Settings/", "Settings"),
    "m_historytasks": ("模組 HistoryTasks", "Core/Modules/HistoryTasks/", "HistoryTasks"),
    "tr_misc": ("翻譯・入口與小資料夾", "PdfTranslation/Application/、Execution/、Pipeline/（根）、Pipeline/Layout/、Pipeline/Fonts/", "翻譯"),
    "tr_translation": ("翻譯・引擎", "PdfTranslation/Translation/", "翻譯引擎"),
    "tr_rendering": ("翻譯・Rendering", "PdfTranslation/Pipeline/Rendering/", "翻譯 Rendering"),
    "tr_annotations": ("翻譯・Annotations", "PdfTranslation/Pipeline/Annotations/", "翻譯 Annotations"),
    "tr_textprocessing": ("翻譯・TextProcessing", "PdfTranslation/Pipeline/TextProcessing/", "翻譯 TextProcessing"),
    "tr_grayprompts": ("翻譯・GrayPrompts", "PdfTranslation/Pipeline/GrayPrompts/", "翻譯 GrayPrompts"),
    "tr_paragraphs": ("翻譯・Paragraphs", "PdfTranslation/Pipeline/Paragraphs/", "翻譯 Paragraphs"),
    "tr_diagrams": ("翻譯・Diagrams", "PdfTranslation/Pipeline/Diagrams/", "翻譯 Diagrams"),
    "tr_tables": ("翻譯・Tables", "PdfTranslation/Pipeline/Tables/", "翻譯 Tables"),
    "tr_model": ("翻譯・Model", "PdfTranslation/Model/", "翻譯 Model"),
    "tr_shared": ("翻譯・Shared（共用分類器與幾何）", "PdfTranslation/Pipeline/Shared/", "翻譯 Shared"),
    "proc": ("共用處理基底", "Core/Processors/", "處理基底"),
    "store": ("儲存", "Core/Storage/", "儲存"),
    "cross": ("橫切支援", "Core/Localization/、Diagnostics/、Fonts/、Rendering/、Properties/、Core 根目錄", "橫切"),
}
assert set(PANEL.values()) <= set(PANEL_INFO), set(PANEL.values()) - set(PANEL_INFO)
PANEL_NOTES = {
    "m_office": ["⚠ Processing 與 Infrastructure 互相引用（待辦 E5）"],
    "store": ["⚠ 門面 ClickraStorage 與大部分 Store 互相引用（待辦 E7）"],
    "tr_model": ["⚠ PdfParagraph.cs 回頭呼叫 Paragraphs 的分析器（待辦 E6）"],
}
GROUPS = [
    ("入口與畫面：Shell、Launcher、Native（Clickra.CLI）、Fluent", ["entry", "cli", "fluent"]),
    ("Core 應用層：共用畫面狀態（L1）＋應用服務與用例（L2）", ["capp"]),
    ("各功能模組：每個模組含自己的 Application／Presentation／Processing", ["m_image", "m_markdown", "m_office", "m_pdfcompression", "m_pdfdecrypt", "m_pdfmerge", "m_pdfsplit", "m_settings", "m_historytasks"]),
    ("TRANSLATION", ["tr_misc", "tr_translation", "tr_rendering", "tr_annotations", "tr_textprocessing", "tr_grayprompts", "tr_paragraphs", "tr_diagrams", "tr_tables", "tr_model", "tr_shared"]),
    ("共用基礎：處理基底、儲存、橫切支援", ["proc", "store", "cross"]),
]
assert sorted(k for _, ks in GROUPS for k in ks) == sorted(PANEL_INFO)


def _label(n):
    return os.path.basename(n)


def _root(key):
    ns = [n for n in NODES if PANEL[n] == key]
    return os.path.commonpath([os.path.dirname(n) for n in ns]) if len(ns) > 1 else os.path.dirname(ns[0])


def panel_data(key):
    root = _root(key)
    ns = sorted(n for n in NODES if PANEL[n] == key)
    labels = [_label(n) for n in ns]
    assert len(set(labels)) == len(labels), (key, labels)
    nodes = {}
    for n in ns:
        nd = NODES[n]
        rel = os.path.relpath(os.path.dirname(n), root)
        sub = f"{nd['lines']} 行" + ("" if rel == "." else f"・{rel}/")
        warn = False
        if key.startswith("tr_") and "MOVES" in globals() and _label(n) in MOVES:
            sub += "・→ " + ("Shared/" if MOVES[_label(n)] == SHARED else short(MOVES[_label(n)]))
            warn = True
        nodes[_label(n)] = {"files": nd["files"], "lines": nd["lines"], "sub": sub, "warn": warn, "card": len(nd["files"]) > 1}
    edges = [(_label(a), _label(b)) for a, b in INNER if PANEL[a] == key]
    out = collections.defaultdict(list)
    for a, b in CROSS:
        if PANEL[a] == key:
            out[_label(a)].append(b)
    for src, tgts in out.items():
        tgts = sorted(tgts, key=lambda b: (list(PANEL_INFO).index(PANEL[b]), _label(b)))
        sl = f"⇢{src}"
        nodes[sl] = {"files": [], "lines": 0, "stack": [(f"q_{PANEL[b]}:{_label(b)}", f"{PANEL_INFO[PANEL[b]][2]} › {_label(b)}") for b in tgts]}
        edges.append((src, sl))
    return {"nodes": nodes, "edges": edges}


def draw_group(keys, yy):
    items = []
    for key in keys:
        Lo = layout(panel_data(key))
        title, sub, _ = PANEL_INFO[key]
        nf = sum(len(NODES[n]["files"]) for n in NODES if PANEL[n] == key)
        foot = PANEL_NOTES.get(key, [])
        if key.startswith("tr_") and "BACK_NOW" in globals():
            labels = {_label(n) for n in NODES if PANEL[n] == key}
            nb = sum(1 for a, b in BACK_NOW if a in labels)
            if nb:
                foot = foot + [f"⚠ 這裡發出 {nb} 條回頭引用（見上方清單）"]
        items.append((f"q_{key}", Lo, max(320, Lo["nat_w"]), f"{title}（{nf} 檔）", sub, foot))
    rows = []
    for it in sorted(items, key=lambda t: -t[2]):
        for row in rows:
            if sum(r[2] for r in row) + 10 * len(row) + it[2] <= 1400:
                row.append(it); break
        else:
            rows.append([it])
    ix = [t[0] for t in items]
    for row in rows:
        row.sort(key=lambda t: ix.index(t[0]))
    rows.sort(key=lambda row: ix.index(row[0][0]))
    for row in rows:
        spare = 1400 - sum(it[2] for it in row) - 10 * (len(row) - 1)
        tot = sum(it[2] for it in row)
        xx, hs = 40, []
        for (pid, Lo, nw, title, sub, foot) in row:
            w = nw + spare * nw / tot
            global PCOL, PFILL, PSOFT, PUN
            if pid == "q_fluent":     # unshipped: grey dashed like Fluent on every other layer
                PCOL, PFILL, PSOFT, PUN = "#98a2b3", "#f9fafb", "#98a2b3", True
            hs.append(draw_panel(pid, Lo, xx, yy, w, title, sub, foot))
            PCOL, PFILL, PSOFT, PUN = MAG, MAG_FILL, MAG_SOFT, False
            xx += w + 10
        yy += max(hs) + 18
    return yy


band(38, 0, f"src/ 底下全部 {len(AF)} 個 .cs 檔（partial class 合成一個方塊）：箭頭＝檔案引用另一個檔案的型別；灰虛線小方塊＝引用到別的面板的檔案（點它會連同本尊一起亮）")
yy = 58
for gtitle, keys in GROUPS:
    if gtitle == "TRANSLATION":
        exec(open('l4_tr_block.py', encoding='utf-8').read())
        band(yy + 18, 0, "翻譯模組各資料夾的檔案（第二行紅字＝建議搬走的檔）")
        yy += 38
    else:
        band(yy + 18, 0, gtitle)
        yy += 38
    yy = draw_group(keys, yy)
L4H = yy + 12
_m = len(o)
section(0, L4H, "L4・模組內部", "Inside", MAG)
_sec = o[_m:]
del o[_m:]
o[_sec4_at:_sec4_at] = _sec
A('</g>')
L4_END = len(o)

RULE = RULE.replace("L3 紫；", "L3 紫、L4 粉紅；")
FOOT_L3 = f'<text x="40" y="{L3_TOP + L3H + 30}" font-size="11.5" fill="#667085">{e(RULE)}</text>'
FOOT_L4 = f'<text x="40" y="{L4_TOP + L4H + 30}" font-size="11.5" fill="#667085">{e(RULE)}</text>'
A(FOOT_L4)
A('</g>')
A('</svg>')
_H2 = L4_TOP + L4H + 60
o[0] = o[0].replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{_H2}" viewBox="0 0 {W} {_H2}"', 1)
H = _H2
