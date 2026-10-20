# ======================= L2: application (exec'd from layers.py) =======================
# Columns line up with the L1 shared-state row so L1 -> L2 calls drop straight down.
L1_OFF = 120                      # L1 group translate
L1_BOTTOM = 1498 + L1_OFF         # abs bottom of the L1 section
L2_TOP = L1_BOTTOM + 34           # abs top of the L2 section
L2H = 756
COL = lambda i: MX0 + i * (MW + MGAP)
AMBER, AMBER_FILL = "#dc6803", "#fffaeb"
MONO = 'font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"'


exec(open('l2_auto.py', encoding='utf-8').read())


def l2card(nid, x, y, w, h, title, role=(), folder="", classes=(), also=(), stroke=AMBER, fill="#fff", dashed=False, tcol="#101828"):
    srch = " ".join([title, *role, folder, *classes, *also]).lower()
    A(f'<g class="node" data-id="{nid}" data-search="{e(srch)}">')
    da = ' stroke-dasharray="7 5"' if dashed else ""
    A(f'<rect x="{x:.1f}" y="{y}" width="{w:.1f}" height="{h}" rx="9" fill="{fill}" stroke="{stroke}" stroke-width="1.6"{da}/>')
    A(f'<text x="{x + 10:.1f}" y="{y + 20}" font-size="13" font-weight="700" fill="{tcol}">{e(title)}</text>')
    cy = y + 38
    for l in role:
        A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="10.5" fill="#475467">{e(l)}</text>')
        cy += 14
    if folder or classes:
        A(f'<line x1="{x + 10:.1f}" y1="{cy - 6}" x2="{x + w - 10:.1f}" y2="{cy - 6}" stroke="#eaecf0"/>')
        cy += 6
    if folder:
        A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="9" fill="#98a2b3" {MONO}>{e(folder)}</text>')
        cy += 13
    for c in classes:
        A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="9.8" fill="#344054" {MONO} xml:space="preserve">{e(c)}</text>')
        cy += 13
    if also:
        cy += 3
        red = False
        for l in also:
            red = l.startswith("⚠") or (red and l.startswith("  "))   # a ⚠ note and its continuation lines are red
            A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="9.5" fill="{"#b42318" if red else "#98a2b3"}">{e(l)}</text>')
            cy += 12
    A('</g>')
    assert cy - y <= h + 4, (nid, cy - y, h)
    return (x, y, w, h)


# ---- part A (combined view only): L1 sources -> top edge of L2, drawn in absolute coords
CTRL_BOTTOM = MY + MH + L1_OFF
DATA_BOTTOM = DY + DH + L1_OFF
ENTRY = L2_TOP - 6                # where every L1 -> L2 line enters the L2 section
R1Y = 56                          # row-1 top (L2 local)


def c_mid(i):
    return COL(i) + MW / 2


# (src, dst, x at L1 source, x where it enters L2)
CALLS = [
    ("m_ws", "a_cmd", c_mid(1), c_mid(1)),
    ("m_exec", "a_uc", c_mid(2), c_mid(2)),
    ("m_split", "a_pages", c_mid(4), c_mid(4)),   # page count through the visual-split state (was D2: screens called it directly)
    ("m_hist", "a_hist", COL(5) + MW - 22, COL(5) + MW - 22),
    ("d_feed", "a_hist", c_mid(5), c_mid(5)),
    ("m_set", "a_set", COL(6) + MW - 22, COL(6) + MW - 22),
    ("d_set", "a_set", c_mid(6), c_mid(6)),
    ("m_set", "a_off", c_mid(7), c_mid(7)),    # settings state -> Office engine settings (the empty 8th L1 slot)
]
for src, dst, sx, ex in CALLS:
    if not link(src, dst):
        continue
    if dst == "a_off":
        # leave the settings card on its right side, cross the empty slot, drop into column 7
        my = MY + MH / 2 + L1_OFF
        pts = [(COL(6) + MW, my), (ex, my), (ex, ENTRY)]
    elif src in ("m_hist", "m_set"):
        # controller sits above its data card: go round it through the gap on its right
        gx = sx + 22 + MGAP / 2
        pts = [(sx, CTRL_BOTTOM), (sx, CTRL_BOTTOM + 10), (gx, CTRL_BOTTOM + 10), (gx, ENTRY - 10), (ex, ENTRY - 10), (ex, ENTRY)]
    elif src.startswith("d_"):
        pts = [(sx, DATA_BOTTOM), (ex, ENTRY)]
    else:
        pts = [(sx, CTRL_BOTTOM), (ex, ENTRY)]
    path(pts, src=src, dst=dst, color="#12b76a", width=1.6, arrow=False)
# visual-split bypass (L1 -> L3), red
BYX = COL(4) - MGAP / 2
SPLIT_BYPASS = problem_link("m_split", "s_proc")      # C1
if SPLIT_BYPASS:
    path([(COL(4) + 16, CTRL_BOTTOM), (COL(4) + 16, CTRL_BOTTOM + 10), (BYX, CTRL_BOTTOM + 10), (BYX, ENTRY)], src="m_split", dst="s_proc", color="#d92d20", width=1.6, dashed=True, arrow=False)
# renderers that call the page-count query directly (they skip L1 shared state)
SCR_BOTTOM = 806 + 266 + L1_OFF            # bottom of the L1 screen row (abs)
JOG = 1098 + L1_OFF                        # between the screen row and the L1 lanes
NPX, FPX = COL(5) - MGAP / 2, COL(3) - MGAP / 2   # gaps used to pass the shared-state row
NPROG_PAGES, FPROG_PAGES = problem_link("nprog", "a_pages"), problem_link("fprog", "a_pages")   # D2
if NPROG_PAGES:
    path([(1040, SCR_BOTTOM), (1040, JOG), (NPX, JOG), (NPX, ENTRY)], src="nprog", dst="a_pages", color="#0e9384", width=1.6, arrow=False)
if FPROG_PAGES:
    path([(440, SCR_BOTTOM), (440, JOG + 6), (FPX, JOG + 6), (FPX, ENTRY)], src="fprog", dst="a_pages", color="#98a2b3", width=1.6, dashed=True, arrow=False, cls="unshipped")

# ---- L2 section (local coords)
L2_AT = len(o)
A(f'<g transform="translate(0,{L2_TOP})">')
_sec2_at = len(o)
band(38, 0, "應用服務（每一欄對應上方 L1 共用畫面狀態的同一欄）")

# part B: vertical entries into row-1 cards
for src, dst, sx, ex in CALLS:
    if (src, dst) not in DRAWN:
        continue
    path([(ex, -6), (ex, R1Y)], src=src, dst=dst, color="#12b76a", width=1.6)

# row-1 cards: titles and roles are hand-written; file lists and 也被 come from the code graph
ROW1 = {  # id: (x, title, role lines, folder, extra lines under the files)
    "a_cmd": (COL(1), "指令目錄", ["指令、可接受副檔名、", "最少檔數、顯示名稱"], "Core/Application/", ["↳ src/Shared/"] + wrap(["  ConvertCommandMetadata.cs"], 22)),
    "a_uc": (COL(2), "轉換用例目錄", ["指令 → 唯一的用例", "Validate→Plan→Execute"], "Core/Application/", []),
    "a_pages": (COL(4), "頁數查詢", ["讀 PDF 頁數"], "Modules/PdfSplit/App…/", []),
    "a_hist": (COL(5), "歷史與暫停任務", ["清除與刪除／讀清單／", "認領暫停任務續跑"], "Modules/HistoryTasks/App…/", []),
    "a_set": (COL(6), "設定", ["讀寫偏好與儲存路徑"], "Modules/Settings/App…/", []),
    "a_off": (COL(7), "Office 引擎設定", ["安裝／移除 LibreOffice、", "回報引擎是否就緒"], "Modules/Settings/App…/", []),
}
ROW1_NOTES = {"a_uc": ["需輸入時回呼 L1：", "密碼／分割頁／MD 選項"]}
ROW1_CONTENT = {}
for k, (x, title, role, folder, extra) in ROW1.items():
    cls_ = files_of(k) + extra
    also = also_lines(k, MW) + ROW1_NOTES.get(k, [])
    ROW1_CONTENT[k] = (cls_, also)
H1 = max(need_h(ROW1[k][2], ROW1[k][3], c, a) for k, (c, a) in ROW1_CONTENT.items()) + 8


def row1_card(k):
    x, title, role, folder, _ = ROW1[k]
    cls_, also = ROW1_CONTENT[k]
    return l2card(k, x, R1Y, MW, H1, title, role, folder, cls_, also, stroke=AMBER, fill=AMBER_FILL)


cmd, uc, pq = row1_card("a_cmd"), row1_card("a_uc"), row1_card("a_pages")
# upstream of the page-count query: two renderers (they skip L1 shared state)
# combined view: continue the lines that come down from the real L1 screen boxes
_c = len(o)
if NPROG_PAGES:
    path([(NPX, -6), (NPX, R1Y + 40), (COL(4) + MW, R1Y + 40)], src="nprog", dst="a_pages", color="#0e9384", width=1.6)
if FPROG_PAGES:
    path([(FPX, -6), (FPX, R1Y + 120), (COL(4), R1Y + 120)], src="fprog", dst="a_pages", color="#98a2b3", width=1.6, dashed=True, cls="unshipped")
COMBINED_ONLY = (_c, len(o))
# standalone L2: those screens are not on the page, so stand them in with chips in the free col-3 slot
_mark = len(o)
CH = (H1 - 10) / 2
for k, (nid, l1, l2, file, cls_) in enumerate([
        ("nprog", "來自 L1 畫面", "Native 進度視窗", "ProgressWindow.VisualSplitter.cs", ""),
        ("fprog", "來自 L1 畫面（未發佈）", "Fluent 視覺分割", "VisualSplitterControl.xaml.cs", "unshipped")]):
    if (nid, "a_pages") not in DRAWN:
        continue
    cy0 = R1Y + k * (CH + 10)
    stroke, dash = ("#98a2b3", ' stroke-dasharray="6 4"') if cls_ else ("#0e9384", "")
    A(f'<g class="node {cls_}" data-id="{nid}" data-search="{e((l2 + " " + file).lower())}"><rect x="{COL(3):.1f}" y="{cy0:.1f}" width="{MW:.1f}" height="{CH:.1f}" rx="9" fill="#f6fef9" stroke="{stroke}" stroke-width="1.4"{dash}/>'
      f'<text x="{COL(3) + 10:.1f}" y="{cy0 + 18:.1f}" font-size="10" fill="#667085">{e(l1)}</text>'
      f'<text x="{COL(3) + 10:.1f}" y="{cy0 + 36:.1f}" font-size="12.5" font-weight="700" fill="#101828">{e(l2)}</text>'
      f'<text x="{COL(3) + 10:.1f}" y="{cy0 + 53:.1f}" font-size="9" fill="#344054" {MONO}>{e(file.split(".", 1)[0])}</text>'
      f'<text x="{COL(3) + 10:.1f}" y="{cy0 + 65:.1f}" font-size="9" fill="#344054" {MONO}>{e("." + file.split(".", 1)[1])}</text></g>')
    path([(COL(3) + MW, cy0 + CH / 2), (COL(4), cy0 + CH / 2)], src=nid, dst="a_pages", color="#98a2b3" if cls_ else "#0e9384", width=1.6, dashed=bool(cls_), cls=cls_)
STANDALONE_ONLY = o[_mark:]
del o[_mark:]
hist, sett, off = row1_card("a_hist"), row1_card("a_set"), row1_card("a_off")
L2_SERVICES = 6

# ---- conversion use cases (cols 0-3)
UCX0, UCX1 = COL(0), COL(3) + MW
R2 = R1Y + H1 + 40
A(f'<text x="{UCX0}" y="{R2}" font-size="12.5" font-weight="700" fill="#667085" letter-spacing=".5">轉換用例（ConversionUseCases 登記的 19 個指令）</text>')
BASEY = R2 + 16
base = l2card("a_base", UCX0, BASEY, UCX1 - UCX0, 88, "共用執行基底　Core/Application/",
              [], "", ["ConversionUseCaseBase.cs（所有用例的父類別）",
                       "├ PerFileConversionUseCaseBase.cs（逐檔處理）",
                       "└ SingleOutputConversionUseCaseBase.cs（多檔合成一個輸出）"], stroke=AMBER, fill=AMBER_FILL)
assert sorted(posixpath.basename(p) for p in MEMBERS["a_base"]) == ["ConversionUseCaseBase.cs", "PerFileConversionUseCaseBase.cs", "SingleOutputConversionUseCaseBase.cs"], MEMBERS["a_base"]
MOD_ORDER = ["Image", "Markdown", "Office", "PdfCompression", "PdfDecrypt", "PdfMerge", "PdfTranslation", "PdfSplit"]
assert sorted(MOD_ORDER) == sorted(k[2:] for k in CARDS if k.startswith("u_")), "a module was added or removed: place it in MOD_ORDER"
MOD_NOTES = {"ImageFormatConvertUseCase.cs": "（5 個指令）", "ImgMergeUseCase.cs": "（合一）", "ImgStitchUseCase.cs": "（合一）",
             "OfficeToPdfUseCaseBase.cs": "（查 Office 引擎）", "DecryptPdfUseCase.cs": "（需密碼）", "MergePdfUseCase.cs": "（合一）",
             "SplitPdfUseCase.cs": "（需分割頁）"}
SHORT = {"Native 進度視窗": "Native", "CLI／ClickraStartup": "CLI"}


def mod_also(k):
    who = [SHORT.get(n, n) for n in callers_of(k)]
    return wrap_text("也被：" + "、".join(who) + ("（C4）" if {"Native", "CLI"} & set(who) else ""), MWID - 20) if who else []


MG = 12
MWID = (UCX1 - UCX0 - 3 * MG) / 4
MODS = [(n, files_of("u_" + n, MOD_NOTES, 25), mod_also("u_" + n)) for n in MOD_ORDER]
MOD_RH = [max(need_h(folder="x", classes=f, also=a) for _, f, a in MODS[:4]) + 6, max(need_h(folder="x", classes=f, also=a) for _, f, a in MODS[4:]) + 6]
MOD_Y = [BASEY + 100, BASEY + 100 + MOD_RH[0] + 12]
for k, (name, items, also) in enumerate(MODS):
    r, c = divmod(k, 4)
    mh = MOD_RH[r]
    l2card(f"u_{name}", UCX0 + c * (MWID + MG), MOD_Y[r], MWID, mh, name, [], "…/Application/", items, also, stroke="#fdb022")
MOD_BOTTOM = MOD_Y[1] + MOD_RH[1]
if link("a_uc", "UC"):
    path([(c_mid(2), R1Y + H1), (c_mid(2), BASEY)], "依指令找到用例", lpos=(c_mid(2) + 8, R1Y + H1 + 24), anchor="start", src="a_uc", dst="a_base", color=AMBER, width=1.6)

# ---- task lifecycle (cols 5-6)
LCX, LCW = COL(5), COL(6) + MW - COL(5)
LIFE_FILES = files_of("a_life", maxch=60)
LIFE_ALSO = also_lines("a_life", LCW)
LIFE_H = need_h(["x"], "Core/Application/", LIFE_FILES, LIFE_ALSO) + 8
lc = l2card("a_life", LCX, BASEY, LCW, LIFE_H, "任務生命週期", ["開始 → 進行中 → 完成／失敗／暫停，寫入任務紀錄"], "Core/Application/",
            LIFE_FILES, LIFE_ALSO, stroke=AMBER, fill=AMBER_FILL)
if link("UC", "a_life"):
    path([(UCX1, BASEY + 25), (LCX, BASEY + 25)], "執行時建立與結束任務紀錄", lpos=((UCX1 + LCX) / 2, BASEY + 18), src="a_base", dst="a_life", color=AMBER, width=1.6)

# ---- exits to L3
S3Y = MOD_BOTTOM + 58
STUB_W = {"s_proc": COL(4) + MW - COL(2), "s_store": COL(7) - 4 - (COL(5) - MGAP), "s_office": MW}
STUB_NOTE = {"s_proc": ["紅色虛線：L1 視覺分割狀態（VisualSplitPresentationController:187）", "直接呼叫 PdfSplitProcessor.BuildSegmentSpec，跳過 L2（C1）"] if SPLIT_BYPASS else []}
STUB_ALSO = {k: STUB_NOTE.get(k, []) + stub_lines(k, w) for k, w in STUB_W.items()}
STUB_ROLE = {"s_proc": ["Modules/*/Processing、Processors/FileProcessing"], "s_store": ["ClickraStorage／ClickraSettings 等 15 檔"],
             "s_office": ["安裝／偵測 LibreOffice、", "Modules/Office/Infra…"]}
S3H = max(78, max(need_h(STUB_ROLE[k], "", [], STUB_ALSO[k]) + 8 for k in STUB_W))
GREY = AMBER  # L2 -> L3 lines take the caller (L2) colour
# shared-definition node: stays in both views (it is not an L3 node)
shared = l2card("s_shared", COL(0), S3Y, COL(1) + MW - COL(0), S3H, "共用指令表（src/Shared）", ["ConvertCommandMetadata.cs：指令、副檔名、", "最少檔數、顯示名稱；同一份也編進 ClickraShell"], "",
                [], ["指令集合與轉換用例目錄一致（架構測試保證）"], stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
if link("a_cmd", "s_shared"):
    path([(COL(1), R1Y + H1 - 24), (28, R1Y + H1 - 24), (28, S3Y + S3H / 2), (COL(0), S3Y + S3H / 2)], "查表", lpos=(34, R1Y + H1 - 30), anchor="start",
         src="a_cmd", dst="s_shared", color=GREY, width=1.4)
X_MOD, X_PAGES, X_HIST, X_LIFE, X_SET, X_OFF = c_mid(2) + 40, c_mid(4), COL(5) - MGAP / 2, LCX + LCW / 2, COL(7) - MGAP / 2, c_mid(7)
# (src, x, colour, dashed, stub id, real L3 id) — every L2 -> L3 exit; the L2 part ends at S3Y without an arrowhead
assert BASEY + LIFE_H < S3Y - 30, "task lifecycle card runs into the L3 exits"
# (src, planned link, x, colour, dashed, stub id, real L3 id) — every L2 -> L3 exit; drawn only if the code has it
EXITS = [("a_base", ("UC", "s_proc"), X_MOD, GREY, False, "s_proc", "p_box"),
         ("a_pages", ("a_pages", "s_proc"), X_PAGES, GREY, False, "s_proc", "p_PdfSplit"),
         ("m_split", ("m_split", "s_proc"), BYX, "#d92d20", True, "s_proc", "p_PdfSplit"),
         ("a_hist", ("a_hist", "s_store"), X_HIST, GREY, False, "s_store", "t_store"),
         ("a_life", ("a_life", "s_store"), X_LIFE, GREY, False, "s_store", "t_store"),
         ("a_set", ("a_set", "s_store"), X_SET, GREY, False, "s_store", "t_store"),
         ("a_off", ("a_off", "s_office"), X_OFF, GREY, False, "s_office", "t_offinf")]
EXITS = [x for x in EXITS if (x[1] in DRAWN) if x[1] == ("m_split", "s_proc")] + [x for x in EXITS if x[1] != ("m_split", "s_proc") and link(*x[1])]
_live = {x[0] for x in EXITS}
if "a_base" in _live:
    path([(X_MOD, MOD_BOTTOM), (X_MOD, S3Y)], "各用例呼叫自己模組的處理器", lpos=(X_MOD + 8, MOD_BOTTOM + 30), anchor="start", src="a_base", dst="p_box", color=GREY, width=1.4, arrow=False)
if "a_pages" in _live:
    path([(X_PAGES, R1Y + H1), (X_PAGES, S3Y)], src="a_pages", dst="p_PdfSplit", color=GREY, width=1.4, arrow=False)
if "m_split" in _live:
    path([(BYX, -6), (BYX, S3Y)], src="m_split", dst="p_PdfSplit", color="#d92d20", width=1.6, dashed=True, arrow=False)
if "a_hist" in _live:
    path([(COL(5), R1Y + H1 - 30), (X_HIST, R1Y + H1 - 30), (X_HIST, S3Y)], src="a_hist", dst="t_store", color=GREY, width=1.4, arrow=False)
if "a_set" in _live:
    path([(COL(6) + MW, R1Y + H1 - 30), (X_SET, R1Y + H1 - 30), (X_SET, S3Y)], src="a_set", dst="t_store", color=GREY, width=1.4, arrow=False)
if "a_life" in _live:
    path([(X_LIFE, BASEY + LIFE_H), (X_LIFE, S3Y)], src="a_life", dst="t_store", color=GREY, width=1.4, arrow=False)
if "a_off" in _live:
    path([(X_OFF, R1Y + H1), (X_OFF, S3Y)], src="a_off", dst="t_offinf", color=GREY, width=1.4, arrow=False)
# standalone L2 only: the L3 stubs and the arrow tips that land on them
_mark = len(o)
A(f'<text x="{COL(2)}" y="{S3Y - 18}" font-size="12.5" font-weight="700" fill="#667085" letter-spacing=".5">往下 → L3</text>')
l2card("s_proc", COL(2), S3Y, STUB_W["s_proc"], S3H, "L3 處理器（Processing）", STUB_ROLE["s_proc"], "", [], STUB_ALSO["s_proc"],
       stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
l2card("s_store", COL(5) - MGAP, S3Y, STUB_W["s_store"], S3H, "L3 儲存（Storage）", STUB_ROLE["s_store"], "", [], STUB_ALSO["s_store"],
       stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
l2card("s_office", COL(7), S3Y, MW, S3H, "L3 Office 引擎", STUB_ROLE["s_office"], "", [], STUB_ALSO["s_office"],
       stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
for src, _, x, col, dsh, stub, real in EXITS:
    path([(x, S3Y - 10), (x, S3Y)], src=src, dst=real, color=col, width=1.6 if dsh else 1.4, dashed=dsh)
STANDALONE_ONLY += o[_mark:]
del o[_mark:]
if STALE:
    print("⚠ L2：這些線在程式碼裡已不存在，沒有畫：", STALE)
L2H = S3Y + S3H + 22
_m = len(o)
section(0, L2H, "L2・應用", "Application", AMBER)
_sec = o[_m:]
del o[_m:]
o[_sec2_at:_sec2_at] = _sec
COMBINED_ONLY = (COMBINED_ONLY[0] + len(_sec), COMBINED_ONLY[1] + len(_sec))   # indices moved by the inserted frame
A('</g>')
L2_END = len(o)

FOOT_L1 = (f'<text x="40" y="{L1_BOTTOM + 30}" font-size="11.5" fill="#667085">線色規則：線的顏色＝發出呼叫的那一方。L1 畫面：深綠＝Native Dashboard、青綠＝Native 進度視窗、灰虛線＝Fluent（未發佈）；L1 共用狀態綠。</text>')
FOOT_L2 = (f'<text x="40" y="{L2_TOP + L2H + 30}" font-size="11.5" fill="#667085">線色規則：線的顏色＝發出呼叫的那一方。L0 藍、L1 綠（畫面另分 深綠＝Native Dashboard、青綠＝Native 進度視窗）、L2 橘；紅虛線＝跳層、灰虛線＝未發佈（Fluent）。</text>')
exec(open('l3_block.py', encoding='utf-8').read())
