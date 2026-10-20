# ======================= L2 from the code graph (exec'd at the top of l2_block.py) =======================
# Card file lists, "也被" lines and which lines may be drawn all come from semantic-msbuild.json + cards.py.
# Layout (columns, rows, routes) stays hand-placed; a planned line whose reference no longer exists is not drawn.
exec(open('l4_model.py', encoding='utf-8').read())
exec(open('cards.py', encoding='utf-8').read())
BOX = assign(AF)
MEMBERS = collections.defaultdict(list)
for _p, _b in BOX.items():
    MEMBERS[_b].append(_p)
GROUP = {"UC": ["a_base"] + [k for k in CARDS if k.startswith("u_")]}
_BLINK = collections.Counter((BOX.get(a), BOX.get(b)) for a, b in AE if BOX.get(a) and BOX.get(b) and BOX[a] != BOX[b])


def boxes(k):
    return GROUP.get(k, [k])


def linked(src, dst):
    return any(_BLINK[(a, b)] for a in boxes(src) for b in boxes(dst))


NAME = {"ndash": "Native Dashboard", "nprog": "Native 進度視窗", "nnone": "CLI／ClickraStartup", "m_lay": "Native 版面表",
        "fmain": "Fluent 主頁", "fprog": "Fluent 進度頁", "a_cmd": "指令目錄", "a_uc": "用例目錄", "a_pages": "頁數查詢",
        "a_hist": "歷史服務", "a_set": "設定服務", "a_off": "Office 引擎設定", "a_base": "用例基底", "a_life": "任務生命週期",
        "UC": "各模組用例"}
NAME.update({m[0]: m[1] + "（狀態）" for m in MODELS_DEF})
NAME.update({"d_feed": "歷史顯示清單", "d_set": "設定目錄"})
for _k in CARDS:
    if _k.startswith("u_"):
        NAME.setdefault(_k, _k[2:] + " 用例")

# every line the L2 drawing plans (src, dst); a line is drawn only if linked(src, dst)
PLANNED = [("m_ws", "a_cmd"), ("m_exec", "a_uc"), ("m_split", "a_pages"), ("m_hist", "a_hist"), ("d_feed", "a_hist"), ("m_set", "a_set"), ("d_set", "a_set"),
           ("m_set", "a_off"), ("nprog", "a_pages"), ("fprog", "a_pages"), ("m_split", "s_proc"), ("a_uc", "UC"), ("UC", "a_life"),
           ("a_cmd", "s_shared"), ("UC", "s_proc"), ("a_pages", "s_proc"), ("a_hist", "s_store"), ("a_life", "s_store"),
           ("a_set", "s_store"), ("a_off", "s_office")]
DRAWN, STALE = [], []


def link(src, dst):
    """True when the code has src -> dst; records stale lines so the build can report them."""
    ok = linked(src, dst)
    (DRAWN if ok else STALE).append((src, dst))
    return ok


def problem_link(src, dst):
    """A line that only exists to show a known problem (skip-layer etc.): drawn while the code has it, silently gone once fixed."""
    ok = linked(src, dst)
    if ok:
        DRAWN.append((src, dst))
    return ok


def files_of(k, notes=None, maxch=22):
    notes = notes or {}
    names = sorted(posixpath.basename(p) for p in MEMBERS[k])
    out = []
    for n in names:
        out += wrap([n], maxch)
        if n in notes:
            out.append("  " + notes[n])
    return out


def callers_of(target, exclude=()):
    """Boxes that reference `target` but have no planned line into it (shown as 也被)."""
    planned_src = {s for s, d in PLANNED if target in boxes(d)}
    found = []
    for k in ORDER:
        kk = "UC" if k in GROUP["UC"] else k
        if kk in found or kk in exclude or k in boxes(target) or k.startswith("s_"):
            continue
        if any(_BLINK[(k, t)] for t in boxes(target)) and kk not in planned_src and not any(k in boxes(s) for s in planned_src):
            found.append(kk)
    return [NAME.get(k, k) for k in found]


def wrap_text(text, width_px, px=9.5):
    """Wrap mixed CJK/latin text to a pixel width; prefer breaking after 、 or ：, else between characters."""
    cw = lambda c: px if ord(c) > 0x2e80 else px * 0.6
    lines, cur, w = [], "", 0.0
    toks = [t for t in text.replace("、", "、\0").replace("：", "：\0").split("\0") if t]
    if len(toks) > 1 and len(toks[0]) <= 4 and toks[0].endswith("："):   # keep a short label such as 也被： with its first item
        toks[:2] = [toks[0] + toks[1]]
    for tok in toks:
        tw = sum(cw(c) for c in tok)
        if cur.strip() and w + tw > width_px:
            lines.append(cur); cur, w = "  ", px * 1.2
        for c in tok:            # a single token longer than the line: break inside it
            if w + cw(c) > width_px and cur.strip():
                lines.append(cur); cur, w = "  ", px * 1.2
            cur += c; w += cw(c)
    if cur.strip():
        lines.append(cur)
    return lines


def also_lines(target, width, prefix="也被：", extra=()):
    who = callers_of(target) + list(extra)
    return wrap_text(prefix + "、".join(who), width - 20) if who else []


L1_BOXES = {"ndash", "nprog", "nnone", "m_lay", "fmain", "fprog"} | {k for k in CARDS if k.startswith(("m_", "d_"))}
SKIP_TODO = {"s_proc": "C1", "s_office": "C5", "s_store": "?"}


def stub_lines(stub, width):
    """Notes for an L3 exit box: L1 boxes that reach it directly (skipping L2, red) and other L2 callers (grey)."""
    who = callers_of(stub)
    l1 = [n for n in who if any(NAME.get(k) == n for k in L1_BOXES)]
    l2 = [n for n in who if n not in l1]
    out = []
    if l1:
        out += wrap_text("⚠ L1 直接用到、跳過 L2（" + SKIP_TODO.get(stub, "?") + "）：" + "、".join(l1), width - 20)
    if l2:
        out += wrap_text("也被：" + "、".join(l2), width - 20)
    return out
