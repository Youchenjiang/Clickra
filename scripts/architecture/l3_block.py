# ======================= L3: processing / storage / infrastructure (exec'd from l2_block.py) =======================
import re as _re

PURPLE, PURPLE_FILL, PURPLE_SOFT = "#7a5af8", "#f9f5ff", "#bdb4fe"
L3_TOP = L2_TOP + L2H + 34


# ---- part A (combined only): continue every L2 exit from where L2 stopped down to the L3 section
ENTRY3 = L3_TOP - 6
for src, _, x, col, dsh, stub, real in EXITS:
    path([(x, L2_TOP + S3Y), (x, ENTRY3)], src=src, dst=real, color=col, width=1.6 if dsh else 1.4, dashed=dsh, arrow=False)

L3_AT = len(o)
A(f'<g transform="translate(0,{L3_TOP})">')
_sec_at = len(o)
band(38, 0, "處理器與儲存（每一欄對應上方 L2 的出口；處理器卡片與 L2 用例卡片同位置、一對一）")
R1Y3 = 56

# ---- module processors: a container whose cards sit exactly under the matching L2 use-case cards
CX0, CX1 = UCX0 - 8, UCX1 + 8
MAXCH = 25
# processor cards: hand-written notes, file lists straight from src/ (Modules/<X>/Processing/**)
def _proc_files(mod):
    pre = f"Clickra.Core/Modules/{mod}/Processing/"
    return sorted(posixpath.relpath(p, pre) for p in AF if p.startswith(pre))


def _tr_summary():
    pre = "Clickra.Core/Modules/PdfTranslation/"
    cnt = collections.Counter()
    for p in AF:
        if p.startswith(pre):
            r = p[len(pre):].split("/")
            cnt[r[0] + ("/" + r[1] if r[0] == "Pipeline" and len(r) > 2 else "")] += 1
    pipe = sorted(((k.split("/", 1)[1], v) for k, v in cnt.items() if k.startswith("Pipeline/")), key=lambda t: -t[1])
    out = [f"Execution/（{cnt['Execution']}）", f"Pipeline/（{sum(v for k, v in cnt.items() if k.startswith('Pipeline'))} 檔）", "  PdfTranslationPipeline.cs"]
    out += [f"  {k}/（{v}）" for k, v in pipe]
    return out + [f"Model/（{cnt['Model']} 檔）", f"Translation/（{cnt['Translation']} 檔）"]


PROC = [
    ("Image", _proc_files("Image"), ["外部：Imazen.WebP、WIC、", "System.Drawing、PDFsharp"]),
    ("Markdown", _proc_files("Markdown"), ["外部：Markdig、PDFsharp"]),
    ("Office", _proc_files("Office"), ["經 PowerShellHelper 叫 Office"]),
    ("PdfCompression", _proc_files("PdfCompression"), ["外部：PDFsharp"]),
    ("PdfDecrypt", _proc_files("PdfDecrypt"), ["外部：PDFsharp"]),
    ("PdfMerge", _proc_files("PdfMerge"), ["外部：PDFsharp"]),
    ("PdfTranslation", _tr_summary(), ["外部：PdfPig、PDFsharp、", "網路翻譯（Google、MyMemory）"]),
    ("PdfSplit", _proc_files("PdfSplit"), ["外部：PDFsharp"] + (["⚠ 也被 L1 視覺分割直接呼叫", "  （紅虛線，待辦 C1）"] if SPLIT_BYPASS else [])),
]
BASE3_Y = R1Y3 + 34
BASE3_H = 86
rows = [PROC[:4], PROC[4:]]
row_h = [max(need_h(folder="x", classes=wrap(f, MAXCH), also=x) for _, f, x in r) for r in rows]
P_Y = [BASE3_Y + BASE3_H + 12, BASE3_Y + BASE3_H + 12 + row_h[0] + 12]
CBOT = P_Y[1] + row_h[1] + 10
A(f'<g class="node" data-id="p_box" data-search="各模組處理器 processing"><rect x="{CX0:.1f}" y="{R1Y3}" width="{CX1 - CX0:.1f}" height="{CBOT - R1Y3:.1f}" rx="12" fill="{PURPLE_FILL}" stroke="{PURPLE}" stroke-width="1.6"/>'
  f'<text x="{CX0 + 12:.1f}" y="{R1Y3 + 22}" font-size="13" font-weight="700" fill="#101828">各模組處理器（Modules/*/Processing）</text></g>')
l2card("p_base", (UCX0, BASE3_Y, UCX1 - UCX0, BASE3_H), "共用處理基底　Processors/", [], "",
       ["FileProcessing/：IFileProcessor.cs ─ SingleFileProcessorBase.cs／MultiFileProcessorBase.cs",
        "                FileBackupScope.cs・ProgressCalculator.cs",
        "ProcessorExecutionOptions.cs・ExecutionLocalizationOptions.cs"], stroke=PURPLE, fill="#fff")
P_POS = {}
for r, row in enumerate(rows):
    for c, (name, files, ext) in enumerate(row):
        x = UCX0 + c * (MWID + MG)
        P_POS[name] = (x, P_Y[r], MWID, row_h[r])
        l2card(f"p_{name}", (x, P_Y[r], MWID, row_h[r]), name, [], "…/Processing/", wrap(files, MAXCH), ext, stroke=PURPLE_SOFT, fill="#fff")

# part B entries into the processors
path([(X_MOD, -6), (X_MOD, R1Y3)], src="a_base", dst="p_box", color=GREY, width=1.4)
sx, sy, sw, sh = P_POS["PdfSplit"]
path([(X_PAGES, -6), (X_PAGES, sy + sh / 2 - 10), (sx + sw, sy + sh / 2 - 10)], src="a_pages", dst="p_PdfSplit", color=GREY, width=1.4)
if SPLIT_BYPASS:
    path([(BYX, -6), (BYX, sy + sh / 2 + 10), (sx + sw, sy + sh / 2 + 10)], src="m_split", dst="p_PdfSplit", color="#d92d20", width=1.6, dashed=True)

# ---- storage (cols 5-6) and Office engine (col 7)
STX0, STX1 = COL(5) - MGAP, COL(7) - 4
_st = sorted(posixpath.basename(p) for p in AF if p.startswith("Clickra.Core/Storage/"))
_facade = [f for f in _st if f.startswith("ClickraStorage")]
ST_FILES = [f"ClickraStorage.*.cs（門面，{len(_facade)} 檔）"] + [f for f in _st if f not in _facade]
store_h = max(need_h(["a", "b"], "x", ST_FILES, ["a", "b", "c"]), need_h(["a", "b"], "x", ["a"] * 6, ["a"] * 4)) + 8
l2card("t_store", (STX0, R1Y3, STX1 - STX0, store_h), "儲存（Storage）", ["對外只有靜態門面 ClickraStorage，其餘是各 Store；", "資料寫在使用者資料夾（設定、任務紀錄、歷史）"],
       f"Core/Storage/（{len(_st)} 檔）", ST_FILES,
       ["被：L2 歷史／設定／任務生命週期（上方三條線）", "另被：處理器取 ClickraSettings 預設值常數"], stroke=PURPLE, fill=PURPLE_FILL)
for x in (X_HIST, X_LIFE, X_SET):
    src = {X_HIST: "a_hist", X_LIFE: "a_life", X_SET: "a_set"}[x]
    path([(x, -6), (x, R1Y3)], src=src, dst="t_store", color=GREY, width=1.4)
l2card("t_offinf", (COL(7), R1Y3, MW, store_h), "Office 引擎", ["偵測、安裝、呼叫", "LibreOffice／Office"], "Modules/Office/Infra…/",
       wrap(sorted(posixpath.basename(p) for p in AF if p.startswith("Clickra.Core/Modules/Office/Infrastructure/")), 22),
       ["外部：LibreOffice 下載（HTTP）、", "soffice、PowerShell／Office"], stroke=PURPLE, fill=PURPLE_FILL)
path([(X_OFF, -6), (X_OFF, R1Y3)], src="a_off", dst="t_offinf", color=GREY, width=1.4)

# ---- external dependencies: right under the L3 nodes that use them, so the lines stay short and straight
EY = CBOT + 46
EH = 78
band(EY - 10, 0, "外部依賴（Clickra.Core.csproj 的套件、Windows 元件、外部程式與網路）")
EXT_DEF = [  # (id, x0, x1, name, lines)
    ("ext_pdfsharp", "PDFsharp 6.2.4", ["壓縮・解密・合併", "分割・翻譯・圖片", "Markdown"]),
    ("ext_pdfpig", "PdfPig 0.1.14", ["翻譯（版面解析）", "縮圖（橫切）"]),
    ("ext_markdig", "Markdig 1.4.0", ["Markdown 解析"]),
    ("ext_webp", "Imazen.WebP 11", ["WebP 編碼（Image）"]),
    ("ext_drawing", "System.Drawing 8", ["Image・Markdown", "縮圖（橫切）"]),
    ("ext_wic", "Windows WIC", ["HEIC 等解碼（Image）"]),
]
EXW = (CX1 - CX0 - 5 * 8) / 6
EXT_POS = {}


def ext_chip(nid, x0, x1, name, lines):
    EXT_POS[nid] = (x0, x1)
    A(f'<g class="node" data-id="{nid}" data-search="{e((name + " " + " ".join(lines)).lower())}"><rect x="{x0:.1f}" y="{EY}" width="{x1 - x0:.1f}" height="{EH}" rx="9" fill="#f9fafb" stroke="#98a2b3" stroke-width="1.4" stroke-dasharray="6 4"/>'
      f'<text x="{x0 + 9:.1f}" y="{EY + 19}" font-size="11" font-weight="700" fill="#344054">{e(name)}</text>')
    for j, l in enumerate(lines):
        A(f'<text x="{x0 + 9:.1f}" y="{EY + 36 + j * 13}" font-size="9.5" fill="#667085">{e(l)}</text>')
    A('</g>')


for k, (nid, name, lines) in enumerate(EXT_DEF):
    x0 = CX0 + k * (EXW + 8)
    ext_chip(nid, x0, x0 + EXW, name, lines)
    path([(x0 + EXW / 2, CBOT), (x0 + EXW / 2, EY)], src="p_box", dst=nid, color=PURPLE, width=1.4)
ext_chip("ext_net", COL(4), COL(4) + MW, "網路", ["翻譯服務（Google、", "MyMemory）", "LibreOffice 下載"])
ext_chip("ext_data", STX0, STX1, "%LocalAppData%\\Clickra（本機）", ["設定、任務紀錄、歷史、暫停任務"])
ext_chip("ext_office", COL(7), COL(7) + MW, "LibreOffice／Office", ["soffice（LibreOffice）", "PowerShell＋Office COM"])
NETC = COL(4) + MW / 2
path([(CX1 - 6, CBOT), (CX1 - 6, EY - 16), (NETC - 24, EY - 16), (NETC - 24, EY)], src="p_box", dst="ext_net", color=PURPLE, width=1.4)
ST_BOTTOM = R1Y3 + store_h
path([((STX0 + STX1) / 2, ST_BOTTOM), ((STX0 + STX1) / 2, EY)], src="t_store", dst="ext_data", color=PURPLE, width=1.4)
path([(X_OFF, ST_BOTTOM), (X_OFF, EY)], src="t_offinf", dst="ext_office", color=PURPLE, width=1.4)
path([(COL(7) + 16, ST_BOTTOM), (COL(7) + 16, EY - 26), (NETC + 24, EY - 26), (NETC + 24, EY)], "下載 LibreOffice", lpos=(COL(6), EY - 30), src="t_offinf", dst="ext_net", color=PURPLE, width=1.4)

# ---- cross-cutting support (no lines: used almost everywhere; callers listed as text)
XY = EY + EH + 46
band(XY - 10, 0, "橫切支援（到處都在用，不畫線；呼叫者寫在「被：」）")
CROSS = [
    ("x_loc", 0, 1, "在地化", "Core/Localization/", ["Localization.*.cs（5 檔）", "  （主檔）・.General・.Cli", "  .Fluent", "  .DocumentProcessing", "ChineseTextConverter", "  .*.cs（主檔・.Pairs）"],
     ["被：CLI、Fluent、處理器、用例"]),
    ("x_font", 1, 1, "字型", "Core/Fonts/", wrap(["ClickraFontResolver.cs"], 22), ["被：ClickraStartup、", "Markdown／壓縮／翻譯處理器", "外部：PDFsharp.Fonts"]),
    ("x_diag", 2, 1, "診斷與外部程序", "Core/Diagnostics/", ["ClickraDebug.cs", "ClickraShellProcess.cs"], ["被：Native 畫面、Fluent App、", "翻譯管線（除錯紀錄）"]),
    ("x_render", 3, 1, "PDF 縮圖", "Core/Rendering/", wrap(["PdfPageThumbnailRenderer.cs"], 22), ["⚠ 被 Native 進度視窗、", "Fluent 視覺分割直接呼叫", "（跳過 L1/L2，待辦 E3）", "外部：PdfPig、System.Drawing"]),
    ("x_root", 4, 1, "Core 根目錄", "Clickra.Core/", wrap(sorted(posixpath.basename(p) for p in AF if p.count("/") == 1 and p.startswith("Clickra.Core/")), 22), ["SystemPaths 被 Fluent 用"]),
    ("x_model", 5, 1, "設定數值範圍", "Modules/Settings/Model/", ["NumericSettingRange.cs"], ["被：L1 Dashboard／Fluent、", "L2 設定、Storage"]),
]
XH = max(need_h(folder=f, classes=c, also=a) for _, _, _, _, f, c, a in CROSS)
for nid, col, span, title, folder, cls, also in CROSS:
    w = COL(col + span - 1) + MW - COL(col)
    l2card(nid, (COL(col), XY, w, XH), title, [], folder, cls, also, stroke=PURPLE, fill=PURPLE_FILL)

L3H = XY + XH + 28
_mark = len(o)
section(0, L3H, "L3・基礎", "Infrastructure", PURPLE)
_sec = o[_mark:]
del o[_mark:]
o[_sec_at:_sec_at] = _sec                 # draw the section frame behind everything in L3
A('</g>')
L3_END = len(o)

# ---- standalone L3: stand-ins for the L2 / L1 sources above
L3_CHIPS = []


def chip3(nid, x0, x1, l1, l2, col="#dc6803", fill="#fffaeb"):
    L3_CHIPS.append(f'<g class="node" data-id="{nid}" data-search="{e(l2.lower())}"><rect x="{x0:.1f}" y="{L3_TOP - 64}" width="{x1 - x0:.1f}" height="50" rx="9" fill="{fill}" stroke="{col}" stroke-width="1.4"/>'
                    f'<text x="{x0 + 10:.1f}" y="{L3_TOP - 44}" font-size="10.5" fill="#667085">{e(l1)}</text>'
                    f'<text x="{x0 + 10:.1f}" y="{L3_TOP - 26}" font-size="12.5" font-weight="700" fill="#101828">{e(l2)}</text></g>')


chip3("a_base", COL(0), COL(2) + MW, "來自 L2", "各模組轉換用例")
if SPLIT_BYPASS:
    chip3("m_split", COL(3), COL(4) - 6, "來自 L1（跳層）", "視覺分割狀態", col="#d92d20", fill="#fef3f2")
chip3("a_pages", COL(4), COL(4) + MW, "來自 L2", "頁數查詢")
chip3("a_life", STX0, STX1, "來自 L2", "歷史・任務生命週期・設定")
chip3("a_off", COL(7), COL(7) + MW, "來自 L2", "Office 引擎設定")

RULE = ("線色規則：線的顏色＝發出呼叫的那一方。L0 藍、L1 綠（畫面另分 深綠＝Native Dashboard、青綠＝Native 進度視窗）、L2 橘、L3 紫；"
        "紅虛線＝跳層、灰虛線＝未發佈（Fluent）或外部依賴。")
FOOT_L3 = f'<text x="40" y="{L3_TOP + L3H + 30}" font-size="11.5" fill="#667085">{e(RULE)}</text>'
exec(open('l4_block.py', encoding='utf-8').read())
