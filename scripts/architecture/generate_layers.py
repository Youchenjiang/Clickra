"""Layered architecture map (L0 entry + L1 presentation) for Clickra (hand-authored from manifests + startup code)."""
import html, os, json
script_dir = os.path.dirname(os.path.abspath(__file__))
os.chdir(script_dir)
repo_root = os.path.abspath(os.path.join(script_dir, "..", ".."))
docs_arch = os.path.join(repo_root, "docs", "architecture")
svg_dir = os.path.join(docs_arch, "svg")
os.makedirs(svg_dir, exist_ok=True)
from files_map import FILES
e = html.escape
W, H = 1556, 3540
F = 'font-family="Microsoft JhengHei UI,Microsoft JhengHei,Noto Sans CJK TC,Segoe UI,Arial,sans-serif"'
o = []
A = o.append
A(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" {F}>')
A('<defs>'
  '<marker id="a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="#344054"/></marker>'
  '<marker id="m" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="#98a2b3"/></marker>'
  '</defs>')
A('<rect width="100%" height="100%" fill="#fff"/>')
TITLE_AT = len(o)
A('<text x="40" y="44" font-size="24" font-weight="700" fill="#101828">Clickra 架構（逐層）</text>')
A('<text x="40" y="70" font-size="13" fill="#475467">由上往下讀：L0 入口 → L1 畫面。灰色虛線＝保留但目前未隨套件發佈（Fluent）。</text>')
G_OPEN = '<g transform="translate(76,0)">'
A(G_OPEN)


def band(y, h, label):
    A(f'<text x="40" y="{y}" font-size="12.5" font-weight="700" fill="#667085" letter-spacing=".5" paint-order="stroke" stroke="#fff" stroke-width="5">{e(label)}</text>')
    A(f'<line x1="40" y1="{y + 8}" x2="1440" y2="{y + 8}" stroke="#eaecf0"/>')


NODE_TEXT = {}


def box(x, y, w, h, title, lines=(), kind="door", tfs=15, nid=None, cls="", files=(), ffs=10.5, edge=None):
    NODE_TEXT[nid] = title + " " + " ".join(lines)
    A(f'<g class="node {cls}" data-id="{nid}" data-search="{e((title + " " + " ".join(lines) + " " + " ".join(f if isinstance(f, str) else " ".join(f[1]) for f in files)).lower())}">')
    stroke, fill, tcol = {"entry": ("#2e90fa", "#f5faff", "#101828"), "door": ("#344054", "#fff", "#101828"),
                          "land": ("#98a2b3", "#f9fafb", "#475467"), "warn": ("#f79009", "#fffaeb", "#101828")}[kind]
    stroke = edge or stroke  # box border = the colour of the lines it sends
    A(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="{fill}" stroke="{stroke}" stroke-width="{1.4 if kind == "land" else 1.8}"{' stroke-dasharray="7 5"' if kind == "land" else ""}/>')
    A(f'<text x="{x + 14}" y="{y + 26}" font-size="{tfs}" font-weight="700" fill="{tcol}">{e(title)}</text>')
    for i, l in enumerate(lines):
        mono = l.startswith("`")
        fam = ' font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"' if mono else ""
        col = "#b42318" if l.startswith("⚠") else "#475467"   # a warning line links the box to a todo item
        A(f'<text x="{x + 14}" y="{y + 47 + i * 18}" font-size="12.5" fill="{col}"{fam}>{e(l.strip("`"))}</text>')
    if files:
        fy = y + 47 + len(lines) * 18 - 6
        A(f'<line x1="{x + 14}" y1="{fy - 4}" x2="{x + w - 14}" y2="{fy - 4}" stroke="#eaecf0"/>')
        MONO = 'font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"'
        row = 0
        for f in files:
            by = fy + 12 + row * 14
            if isinstance(f, tuple):
                n, items = f
                cw = (w - 28) / n
                rows = (len(items) + n - 1) // n
                for k, it in enumerate(items):
                    c, r = divmod(k, rows)
                    A(f'<text x="{x + 14 + c * cw:.1f}" y="{by + r * 14}" font-size="10" fill="#344054" {MONO}>{e(it)}</text>')
                row += (len(items) + n - 1) // n
            elif f.endswith(("/", "：")):
                A(f'<text x="{x + 14}" y="{by}" font-size="9.5" fill="#98a2b3" {MONO}>{e(f)}</text>')
                row += 1
            else:
                A(f'<text x="{x + 14}" y="{by}" font-size="10" fill="#344054" {MONO}>{e(f)}</text>')
                row += 1
    A('</g>')
    return (x, y, w, h)



_NAV_ON = 'border:1px solid #101828;color:#fff;background:#101828;font-weight:700;'
_NAV_OFF = 'border:1px solid #bfc6d1;color:#111827;background:#fff;'


def from_l1_template(title, href):
    """The L2-L4 pages reuse the L1 template: swap the page title and move the nav highlight (not the nav labels)."""
    page = open("l1_template.html", encoding="utf-8").read().replace("— L1 畫面", "— " + title)
    page = page.replace('href="l1_presentation.html" style="text-decoration:none;font-size:12px;padding:6px 9px;border-radius:6px;' + _NAV_ON,
                        'href="l1_presentation.html" style="text-decoration:none;font-size:12px;padding:6px 9px;border-radius:6px;' + _NAV_OFF)
    return page.replace(f'href="{href}" style="text-decoration:none;font-size:12px;padding:6px 9px;border-radius:6px;' + _NAV_OFF,
                        f'href="{href}" style="text-decoration:none;font-size:12px;padding:6px 9px;border-radius:6px;' + _NAV_ON)

import re as _re


def need_h(role=(), folder="", classes=(), also=()):
    """Height l2card needs for this content."""
    cy = 38 + 14 * len(role)
    if folder or classes:
        cy += 6
    if folder:
        cy += 13
    cy += 13 * len(classes)
    if also:
        cy += 3 + 12 * len(also)
    return cy - 4


def wrap(names, maxch):
    """Break long identifiers at a camel-case boundary: 'MarkdownSingleFile-' / '  ProcessorBase'."""
    out = []
    for n in names:
        if not _re.fullmatch(r"[A-Za-z0-9_./-]+", n.strip()):   # only wrap plain identifiers / paths
            out.append(n)
            continue
        while len(n) > maxch:
            caps = [m.start() for m in _re.finditer(r"[A-Z]", n[:maxch]) if m.start() > 4]
            # prefer cutting before a whole suffix word (UseCase, Processor, Presentation…) over splitting inside one
            nice = [p for p in caps if _re.match(r"(UseCase|Processor|Presentation|Controller|Service|Registry|Metadata|Options|Renderer|Installer|Detector|Optimizer|Template|Command)", n[p:])]
            cut = max(nice or caps or [maxch - 1])
            out.append(n[:cut] + "-")
            n = "  " + n[cut:]
        out.append(n)
    return out


def pt(b, side, f=0.5):
    x, y, w, h = b
    return {"top": (x + w * f, y), "bottom": (x + w * f, y + h), "left": (x, y + h * f), "right": (x + w, y + h * f)}[side]


EDGES = []
MARKERS = set()


def path(points, label=None, dashed=False, muted=False, lpos=None, anchor="middle", src="", dst="", variant="both", cls="", color=None, width=None, arrow=True):
    EDGES.append((src, dst, variant))
    A(f'<g class="edge {cls}" data-src="{src}" data-dst="{dst}" data-variant="{variant}">')
    col, mk = ("#98a2b3", "m") if muted else ("#344054", "a")
    if color:
        col, mk = color, "c_" + color.strip("#")
        if mk not in MARKERS:
            MARKERS.add(mk)
            A(f'<defs><marker id="{mk}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="{color}"/></marker></defs>')
    da = ' stroke-dasharray="6 5"' if dashed else ""
    d = "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in points)
    A(f'<path d="{d}" fill="none" stroke="{col}" stroke-width="{width or (1.4 if muted else 1.9)}" stroke-linejoin="round"{da}{f' marker-end="url(#{mk})"' if arrow else ""}/>')
    if label:
        lx, ly = lpos
        A(f'<text x="{lx}" y="{ly}" font-size="12" fill="{col}" text-anchor="{anchor}" paint-order="stroke" stroke="#fff" stroke-width="5">{e(label)}</text>')
    A('</g>')


# ---------------- row A: entrances
band(108, 0, "使用者動作")
X = [40, 400, 760, 1120]
EW = 320
MAN = "packaging/msix/AppxManifest.xml"
DEP = "src/resources/AppxManifest.xml（--deploy）"
menu = box(X[0], 128, EW, 120, "① 檔案總管右鍵選單", ["Win11 新選單＋傳統選單", "對象：檔案／資料夾／資料夾背景"], "entry", nid="menu", ffs=10, files=[MAN, DEP])
tile = box(X[1], 128, EW, 120, "② 開始功能表「Clickra」", ["MSIX 安裝版 → ClickraLauncher.exe", "--deploy 部署（sparse 身分）→ Clickra.exe"], "entry", nid="tile", ffs=10, files=[MAN, DEP])
openw = box(X[2], 128, EW, 120, "③ 開啟檔案方式（檔案關聯）", [".pdf .docx .pptx .xlsx .md .png …14 種", "僅 MSIX 安裝版"], "entry", nid="open", ffs=10, files=[MAN])
term = box(X[3], 128, EW, 120, "④ 終端機／腳本", ["`Clickra.exe <command> <files>`", "`  [--quiet] [--out-dir] [--level] …`"], "entry", nid="term", ffs=10, files=["src/Clickra.CLI/Cli/ClickraStartup.cs", "src/Clickra.CLI/Cli/ClickraCli.Arguments.cs"])

# ---------------- doors
band(278, 0, "門（執行檔）")
shell = box(40, 300, 320, 164, "ClickraShell.dll", ["NativeAOT COM 擴充，跑在 dllhost.exe", "讀 ConvertCommandMetadata 決定選單", "無 Launcher（未封裝）時直接開 Fluent／Clickra.exe"], nid="shell", edge="#2e90fa", ffs=10,
            files=["src/ClickraShell/", (3, ["ComMethods.cs", "Exporter.cs", "Guids.cs", "Kernel32.cs", "ShellUtils.cs"]), "src/Shared/ConvertCommandMetadata.cs"])
launcher = box(400, 470, 640, 104, "ClickraLauncher.exe", ["NativeAOT、零相依；把動詞＋檔案原封轉交",
                                                          "嘗試以 COM 啟動 Fluent；目前套件沒有 Fluent → 立即轉給 Clickra.exe"], nid="launcher", edge="#2e90fa", ffs=10, files=["src/ClickraLauncher/Program.cs"])
fluent = box(40, 640, 480, 152, "Clickra.Fluent.exe（保留・目前未發佈）", ["WinUI 3；選用套件方案自 2026-10-08 凍結",
                                                    "不在 Clickra.msix 內，以下兩條路徑目前都不會成功"], "land", nid="fluent", cls="unshipped", ffs=10,
             files=["src/Clickra.Fluent/", (3, ["App.xaml.cs", "MainWindow.xaml.cs", "TrayIcon.cs", "TrayService.cs", "ToastHelper.cs"]), "packaging/msix/AppxManifest.Fluent.xml"])
native = box(760, 640, 680, 170, "Clickra.exe", ["NativeAOT；入口 ClickraStartup.Run",
                                               "無參數 → 找 Clickra.Fluent.exe（目前找不到）→ Native Dashboard",
                                               "command＋檔案 → 進度視窗，或 --quiet 背景執行；另有 --version / --help / --deploy"], nid="native", ffs=10, edge="#2e90fa",
             files=["src/Clickra.CLI/Cli/", (4, ["ClickraStartup.cs", "ClickraCli.cs", "ClickraCli.Arguments.cs", "ClickraCli.Deploy.cs"]), "src/Clickra.CLI/", (4, ["GlobalUsings.Application.cs", "", "GlobalUsings.Architecture.cs"])])

# ---------------- edges: entrances -> doors
path([(200, 248), (200, 300)], "點選指令", lpos=(210, 272), anchor="start", src="menu", dst="shell", color="#2e90fa")
path([(512, 248), (512, 470)], "MSIX", lpos=(512, 384), src="tile", dst="launcher", variant="msix", color="#2e90fa")
path([(920, 248), (920, 470)], "檔案路徑", lpos=(920, 384), src="open", dst="launcher", variant="msix", color="#2e90fa")
path([(1280, 248), (1280, 640)], "參數", lpos=(1280, 430), src="term", dst="native", color="#2e90fa")
gx = pt(tile, "bottom", .82)[0]
path([(gx, 248), (gx, 264), (1080, 264), (1080, 640)], "--deploy 部署", dashed=True, lpos=(1000, 258), src="tile", dst="native", variant="deploy", color="#2e90fa")

# doors -> doors
path([(200, 464), (200, 522), (400, 522)], "動詞＋檔案", lpos=(300, 514), src="shell", dst="launcher", color="#2e90fa")
path([(477, 574), (477, 640)], "嘗試 Fluent（目前必失敗）", lpos=(486, 612), anchor="start", src="launcher", dst="fluent", muted=True, dashed=True, cls="unshipped")
path([(899, 574), (899, 640)], "實際路徑：立即轉交", lpos=(908, 612), anchor="start", src="launcher", dst="native", color="#2e90fa")
path([(760, 712), (520, 712)], "無參數：找 Fluent（目前找不到）", dashed=True, muted=True, lpos=(640, 705), src="native", dst="fluent", cls="unshipped")

# ======================= section frames =======================
def section(y0, y1, label, sub, col):
    num, name = label.split("・")
    # light tinted panel for the layer + a thin accent rule on its left edge
    A(f'<rect x="20" y="{y0}" width="{W - 76 - 40}" height="{y1 - y0}" rx="12" fill="{col}" fill-opacity=".035"/>')
    A(f'<line x1="20" y1="{y0 + 6}" x2="20" y2="{y1 - 6}" stroke="{col}" stroke-width="3" stroke-linecap="round"/>')
    # layer heading in the left gutter
    A(f'<text x="-52" y="{y0 + 34}" font-size="30" font-weight="800" fill="{col}">{e(num)}</text>')
    A(f'<text x="-52" y="{y0 + 58}" font-size="15" font-weight="700" fill="#101828">{e(name)}</text>')
    if sub:
        A(f'<text x="-52" y="{y0 + 76}" font-size="10.5" fill="#98a2b3">{e(sub)}</text>')


section(90, 826, "L0・入口", "Entry", "#2e90fa")
L1_AT = len(o)
A('<g transform="translate(0,120)">')
section(740, 1498, "L1・畫面", "Presentation", "#12b76a")

# ======================= L1: screens =======================
band(788, 0, "畫面（Renderer）")
fmain = box(40, 806, 195, 266, "Fluent 主頁", ["MainPage（WinUI）", "分頁同 Dashboard", "續跑 → 進度頁"], "land", 14, nid="fmain", cls="unshipped",
            files=["src/Clickra.Fluent/", "MainPage.xaml.cs", "MainPage.History.cs", "MainPage.LibreOffice.cs", "DynamicSettingsController.cs", "OfficeEnginePreflight.cs", "Controls/FluentDialogs.cs"])
fprog = box(255, 806, 200, 266, "Fluent 進度頁", ["TaskProgressPage", "續跑／帶參數啟動"], "land", 14, nid="fprog", cls="unshipped",
            files=["src/Clickra.Fluent/", "TaskProgressPage.xaml.cs", "src/Clickra.Fluent/Controls/", "VisualSplitterControl.xaml.cs", "SplitPagesOverlay.xaml.cs", "FluentDialogs.cs"])
ndash = box(695, 806, 305, 266, "Native Dashboard", ["DashboardWindow（Win32 自繪）", "總覽／轉換／歷史／設定／關於", "轉換、續跑 → 開進度視窗"], "door", 14, nid="ndash", edge="#05603a",
            files=["src/Clickra.CLI/Dashboard/", "DashboardWindow.*.cs（18 檔）：", (2, ["（主檔）", "State", "Lifecycle", "Convert", "ConvertRegistry", "HitTesting",
                   "Events", "Events.Click", "Events.History", "Events.LibreOffice", "Events.Settings", "Paint", "Paint.Overview", "Paint.History",
                   "Paint.Settings", "Paint.Settings.Dynamic", "Paint.Dropdowns", "Paint.About"])])
nprog = box(1020, 806, 250, 266, "Native 進度視窗", ["ProgressWindow", "附屬：密碼、Markdown 選項、", "視覺分割、系統匣", "⚠ 逐一辨認 8 個指令（C4）"], "door", 14, nid="nprog", edge="#0e9384",
            files=["src/Clickra.CLI/Progress/", "ProgressWindow.*.cs（8 檔）：", (2, ["（主檔）", "Process", "Paint", "Controls", "PasswordInput", "MarkdownOptions", "VisualSplitter", "Tray"]),
                   "NativeConversionExecutionAdapter.cs"])
nnone = box(1290, 806, 150, 266, "無畫面", ["--quiet 背景轉換", "--version / --help", "--deploy → 直接到 L2", "⚠ 逐一辨認指令（C4）"], "door", 14, nid="nnone", edge="#12b76a",
            files=["src/Clickra.CLI/Cli/", "ClickraCli.cs", "ClickraCli.Deploy.cs", "ClickraStartup.cs"])
SCREENS = 4

# L0 -> L1
path([(137, 672), (137, 806)], muted=True, dashed=True, src="fluent", dst="fmain", cls="unshipped", label="無參數", lpos=(144, 772), anchor="start")
path([(355, 672), (355, 806)], muted=True, dashed=True, src="fluent", dst="fprog", cls="unshipped", label="command＋檔案", lpos=(362, 772), anchor="start")
path([(847, 690), (847, 806)], src="native", dst="ndash", label="無參數", lpos=(854, 772), anchor="start", color="#2e90fa")
path([(1145, 690), (1145, 806)], src="native", dst="nprog", label="command＋檔案", lpos=(1152, 772), anchor="start", color="#2e90fa")
path([(1365, 690), (1365, 806)], src="native", dst="nnone", label="--quiet／工具", lpos=(1372, 772), anchor="start", color="#2e90fa")
# screen -> screen
path([(1000, 860), (1020, 860)], src="ndash", dst="nprog", color="#05603a")
path([(235, 860), (255, 860)], muted=True, dashed=True, src="fmain", dst="fprog", cls="unshipped")

# ======================= L1: Native-only layout helpers (live in Core) =======================
lay = box(475, 806, 200, 266, "Native 版面表", ["僅 Native 使用"], "door", 14, nid="m_lay", edge="#12b76a",
          files=["src/Clickra.CLI/UI/Layout/", "DashboardLayout.cs", "HistoryLayout.cs", "SettingsLayout.cs", "ProgressLayout.cs", "VisualSplitterLayout.cs",
                 "MarkdownOptionsLayout.cs", "VisualSplitThumbnailPolicy.cs", "LayoutRect.cs", "VectorIcons.cs", "非版面的輔助：", "UI/UIHelper.cs", "UI/LocalizedUiFontSelector.cs", "Native/Win32.cs"])
path([(695, 819), (675, 819)], src="ndash", dst="m_lay", color="#05603a", width=1.6)
path([(1060, 1072), (1060, 1088), (575, 1088), (575, 1072)], src="nprog", dst="m_lay", color="#0e9384", width=1.6)

# ======================= L1: shared presentation controllers =======================
band(1220, 0, "共用畫面狀態（Clickra.Core，Native 與 Fluent 共用）")
# file lists of the shared-state cards come from cards.py + the code graph (L2/L4 use the same mapping)
exec(open('l4_model.py', encoding='utf-8').read())
exec(open('cards.py', encoding='utf-8').read())
_L1BOX = assign(AF)
_L1_SCOPE = ["Clickra.Core/Presentation/*/*", "Clickra.Core/Modules/*/Presentation/*"]
_l1_orphans = [p for p in AF if any(_match(p, s) for s in _L1_SCOPE) and not _L1BOX.get(p, "").startswith(("m_", "d_"))]
assert not _l1_orphans, ("L1 presentation files not on any card — add them to cards.py", _l1_orphans)


def l1_files(k):
    return wrap(sorted(posixpath.basename(p) for p, b in _L1BOX.items() if b == k), 22)


def l1_link(a, b):
    return any(_L1BOX.get(x) == a and _L1BOX.get(y) == b for x, y in AE)


MODELS_DEF = [
    ("m_nav", "Dashboard 導覽", l1_files("m_nav"), "Presentation/Dashboard"),
    ("m_ws", "轉換工作區", l1_files("m_ws"), "Presentation/Convert"),
    ("m_exec", "轉換執行", l1_files("m_exec"), "Presentation/Convert"),
    ("m_prog", "任務進度", l1_files("m_prog"), "Presentation/Progress"),
    ("m_split", "視覺分割", l1_files("m_split"), "Modules/PdfSplit"),
    ("m_hist", "歷史／暫停任務", l1_files("m_hist"), "Modules/HistoryTasks"),
    ("m_set", "設定（含 Office 引擎）", l1_files("m_set"), "Modules/Settings"),
]
MODELS = len(MODELS_DEF)
MX0, MGAP = 40, 22
NSLOT = 8   # column grid shared with L2/L3; the 8th L1 slot is empty since Office settings moved into Settings
MW = (1480 - 80 - MGAP * (NSLOT - 1)) / NSLOT
MY, MH = 1244, 106
M = {}


def card(nid, x, y, w, h, title, lines, where, stroke, fill):
    A(f'<g class="node" data-id="{nid}" data-search="{e((title + " " + "".join(lines) + " " + where).lower())}">')
    A(f'<rect x="{x:.1f}" y="{y}" width="{w:.1f}" height="{h}" rx="9" fill="{fill}" stroke="{stroke}" stroke-width="1.6"/>')
    A(f'<text x="{x + 10:.1f}" y="{y + 20}" font-size="13" font-weight="700" fill="#101828">{e(title)}</text>')
    for j, l in enumerate(lines):
        A(f'<text x="{x + 10:.1f}" y="{y + 38 + j * 14}" font-size="9.8" fill="#475467" font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace" xml:space="preserve">{e(l)}</text>')
    A(f'<text x="{x + 10:.1f}" y="{y + h - 9}" font-size="10" fill="#98a2b3">{e(where)}</text>')
    A('</g>')


for i, (mid, title, cls_lines, where) in enumerate(MODELS_DEF):
    x = MX0 + i * (MW + MGAP)
    card(mid, x, MY, MW, MH, title, cls_lines, where, "#12b76a", "#f6fef9")
    M[mid] = (x, MY, MW, MH)

# ======================= L1: data / catalogs =======================
band(1372, 0, "資料／目錄（不持有狀態，供畫面狀態與畫面直接取用）")
DY, DH = 1394, 78
DATA = {}
for did, under, title, lines, where in [
    ("d_feed", "m_hist", "歷史顯示清單", ["HistoryFeed.cs"], "Modules/HistoryTasks"),
    ("d_set", "m_set", "設定目錄", ["SettingDescriptor.cs"], "Modules/Settings"),
]:
    x, _, w, _ = M[under]
    card(did, x, DY, w, DH, title, lines, where, "#12b76a", "#f6fef9")
    DATA[did] = (x, DY, w, DH)
    # controller uses its data helper
    path([(x + w / 2, MY + MH), (x + w / 2, DY)], src=under, dst=did, color="#12b76a", width=1.6)

# screen -> controller / data wiring, straight from the code graph
USES = {s: [t for t in [m[0] for m in MODELS_DEF] + ["d_feed", "d_set"] if l1_link(s, t)] for s in ("ndash", "nprog", "fmain", "fprog")}
SCREEN_BOX = {"ndash": ndash, "nprog": nprog, "fmain": fmain, "fprog": fprog}
LANE = {"fmain": 1108, "fprog": 1126, "ndash": 1144, "nprog": 1162}
COLOR = {"ndash": "#05603a", "nprog": "#0e9384", "fmain": "#98a2b3", "fprog": "#98a2b3"}
ORDER = ["fmain", "fprog", "ndash", "nprog"]
GAP_SLOT = {"fmain": -6, "ndash": 6}
for s_id in ORDER:
    sx, sy, sw, sh = SCREEN_BOX[s_id]
    slot = ORDER.index(s_id)
    x_out = sx + sw / 2
    unshipped = s_id.startswith("f")
    for t in USES[s_id]:
        cls_ = "unshipped" if unshipped else ""
        if t in M:
            tx, ty, tw, th = M[t]
            x_in = tx + tw / 2 + (slot - 1.5) * 12
            pts = [(x_out, sy + sh), (x_out, LANE[s_id]), (x_in, LANE[s_id]), (x_in, ty)]
        else:  # data box: drop through the gap left of its controller, enter from the left side
            dx, dy, dw, dh = DATA[t]
            gx = dx - MGAP / 2 + GAP_SLOT[s_id]
            ey = dy + dh / 2 + (GAP_SLOT[s_id] * 1.5)
            pts = [(x_out, sy + sh), (x_out, LANE[s_id]), (gx, LANE[s_id]), (gx, ey), (dx, ey)]
        path(pts, color=COLOR[s_id], width=1.6, dashed=unshipped, src=s_id, dst=t, cls=cls_)
# quiet: bypass presentation
path([(1440 - 20, 1072), (1440 - 20, 1198)], src="nnone", dst="", label="不經 Presentation，直接進 L2 ↓", lpos=(1412, 1192), anchor="end", color="#12b76a")

A('</g>')  # close L1 translate
L1_END = len(o)
exec(open('l2_block.py', encoding='utf-8').read())

svg_full = "\n".join(o)
open(os.path.join(svg_dir, "clickra_layers.svg"), "w", encoding="utf-8", newline="\n").write(svg_full)
body = o[:TITLE_AT] + o[TITLE_AT + 2:]
svg_html = "\n".join(body).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{H - 70}" viewBox="0 70 {W} {H - 70}"', 1)
page = open("layers_template.html", encoding="utf-8").read()
page = page.replace("__SVG__", svg_html).replace("__FILES__", json.dumps(FILES, ensure_ascii=False)).replace("__META__", f"L0：4 種使用者動作 · 4 個執行檔　L1：{SCREENS} 個畫面 · {MODELS} 個共用畫面狀態 · 9 個 Native 版面表　L2：{L2_SERVICES} 組應用服務 · 19 個轉換指令　L3：8 個模組處理器 · 儲存 · Office 引擎 · 6 組橫切支援　L4：全部 {len(AF)} 個檔的引用（{len(INNER) + len(CROSS)} 條）")
open(os.path.join(docs_arch, "overview.html"), "w", encoding="utf-8", newline="\n").write(page)
print("overview ok")



# ---------------- standalone L1 export ----------------
STUB = [
    '<g class="node unshipped" data-id="fluent" data-search="clickra.fluent.exe"><rect x="40" y="756" width="480" height="54" rx="10" fill="#f9fafb" stroke="#98a2b3" stroke-width="1.4" stroke-dasharray="7 5"/>'
    '<text x="54" y="780" font-size="14" font-weight="700" fill="#475467">來自 L0：Clickra.Fluent.exe（保留・目前未發佈）</text>'
    '<text x="54" y="799" font-size="12" fill="#98a2b3">WinUI 3</text></g>',
    '<g class="node" data-id="native" data-search="clickra.exe"><rect x="760" y="756" width="680" height="54" rx="10" fill="#fff" stroke="#2e90fa" stroke-width="1.8"/>'
    '<text x="774" y="780" font-size="14" font-weight="700" fill="#101828">來自 L0：Clickra.exe</text>'
    '<text x="774" y="799" font-size="12" fill="#475467">NativeAOT；ClickraStartup.Run 依參數分派</text></g>',
]
Y0 = 730
l1_parts = o[:TITLE_AT] + [G_OPEN] + STUB + o[L1_AT:L1_END] + [FOOT_L1, "</g>", "</svg>"]
L1H = L1_BOTTOM + 70
l1_svg = "\n".join(l1_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L1H - Y0}" viewBox="0 {Y0} {W} {L1H - Y0}"', 1)
open(os.path.join(svg_dir, "l1_presentation.svg"), "w", encoding="utf-8", newline="\n").write(l1_svg.replace(f'<rect width="100%" height="100%" fill="#fff"/>', f'<rect x="0" y="{Y0}" width="{W}" height="{L1H - Y0}" fill="#fff"/>'))
page = open("l1_template.html", encoding="utf-8").read()
page = page.replace("__SVG__", l1_svg).replace("__FILES__", json.dumps(FILES, ensure_ascii=False)).replace("__META__", f"{SCREENS} 個畫面 · {MODELS} 個共用畫面狀態 · 2 個資料目錄 · 9 個 Native 版面表（Clickra.CLI）")
open(os.path.join(docs_arch, "l1_presentation.html"), "w", encoding="utf-8", newline="\n").write(page)
print("l1 ok")

# ---------------- standalone L0 export ----------------
L0H = 870
l0_parts = o[:TITLE_AT] + o[TITLE_AT + 2:L1_AT] + ['<text x="40" y="852" font-size="11.5" fill="#667085">來源：packaging/msix/AppxManifest.xml（Store／GitHub 同一份 MSIX）、src/resources/AppxManifest.xml（--deploy）、ClickraShell/ComMethods.cs、ClickraLauncher/Program.cs、Clickra.CLI/Cli/ClickraStartup.cs、Clickra.Fluent/App.xaml.cs</text>', '</g>', '</svg>']
l0_svg = "\n".join(l0_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L0H - 70}" viewBox="0 70 {W} {L0H - 70}"', 1)
open(os.path.join(svg_dir, "l0_entry_points.svg"), "w", encoding="utf-8", newline="\n").write(l0_svg)
page = open("l0_template.html", encoding="utf-8").read()
L0_EDGES = sum(1 for x in o[:L1_AT] if x.startswith('<g class="edge'))
page = page.replace("__SVG__", l0_svg).replace("__FILES__", json.dumps(FILES, ensure_ascii=False)).replace("__META__", f"4 種使用者動作 · 4 個執行檔 · {L0_EDGES} 條轉交")
open(os.path.join(docs_arch, "l0_entry_points.html"), "w", encoding="utf-8", newline="\n").write(page)
print("l0 ok")

# ---------------- standalone L2 export ----------------
chips = []
def chip(nid, x, w, label):
    chips.append(f'<g class="node" data-id="{nid}" data-search="{e(label.lower())}"><rect x="{x:.1f}" y="{L2_TOP - 64}" width="{w:.1f}" height="50" rx="9" fill="#f6fef9" stroke="#12b76a" stroke-width="1.4"/>'
                 f'<text x="{x + 10:.1f}" y="{L2_TOP - 44}" font-size="10.5" fill="#667085">來自 L1</text>'
                 f'<text x="{x + 10:.1f}" y="{L2_TOP - 26}" font-size="12.5" font-weight="700" fill="#101828">{e(label)}</text></g>')
chip("m_ws", COL(1), MW, "轉換工作區")
chip("m_exec", COL(2), MW, "轉換執行")
chip("m_split", COL(4) - MGAP, MW + MGAP, "視覺分割")
chip("m_hist", COL(5), MW, "歷史・歷史清單")
chip("m_set", COL(6), MW, "設定・設定目錄")
chip("m_set", COL(7), MW, "設定（Office 引擎）")
L2V0 = L2_TOP - 84
_l2 = o[L2_AT:L2_END]
_c0, _c1 = COMBINED_ONLY[0] - L2_AT, COMBINED_ONLY[1] - L2_AT
_l2 = _l2[:_c0] + _l2[_c1:]
_l2 = _l2[:-1] + STANDALONE_ONLY + _l2[-1:]          # chips go inside the L2 group (before its closing tag)
l2_parts = o[:TITLE_AT] + [G_OPEN] + chips + _l2 + [FOOT_L2, "</g>", "</svg>"]
L2V1 = L2_TOP + L2H + 50
l2_svg = "\n".join(l2_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L2V1 - L2V0}" viewBox="0 {L2V0} {W} {L2V1 - L2V0}"', 1)
open(os.path.join(svg_dir, "l2_application.svg"), "w", encoding="utf-8", newline="\n").write(l2_svg.replace('<rect width="100%" height="100%" fill="#fff"/>', f'<rect x="0" y="{L2V0}" width="{W}" height="{L2V1 - L2V0}" fill="#fff"/>'))
page = from_l1_template("L2 應用", "l2_application.html").replace("上排是畫面（Renderer），下排是 Native 與 Fluent 共用的畫面模型。", "上排是應用服務（與 L1 共用畫面狀態同一欄），左下是各模組轉換用例，最底是往 L3 的出口。")
page = page.replace("__SVG__", l2_svg).replace("__META__", f"{L2_SERVICES} 組應用服務 · 8 個模組 · 19 個轉換指令 · 3 個往 L3 的出口")
open(os.path.join(docs_arch, "l2_application.html"), "w", encoding="utf-8", newline="\n").write(page)
print("l2 ok")

# ---------------- standalone L3 export ----------------
L3V0 = L3_TOP - 84
L3V1 = L3_TOP + L3H + 50
l3_parts = o[:TITLE_AT] + [G_OPEN] + L3_CHIPS + o[L3_AT:L3_END] + [FOOT_L3, "</g>", "</svg>"]
l3_svg = "\n".join(l3_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L3V1 - L3V0}" viewBox="0 {L3V0} {W} {L3V1 - L3V0}"', 1)
open(os.path.join(svg_dir, "l3_infrastructure.svg"), "w", encoding="utf-8", newline="\n").write(l3_svg.replace('<rect width="100%" height="100%" fill="#fff"/>', f'<rect x="0" y="{L3V0}" width="{W}" height="{L3V1 - L3V0}" fill="#fff"/>'))
page = from_l1_template("L3 基礎", "l3_infrastructure.html").replace("上排是畫面（Renderer），下排是 Native 與 Fluent 共用的畫面模型。", "上排是處理器、儲存與 Office 引擎（與 L2 出口同一欄），中排是橫切支援，最底是外部依賴。")
page = page.replace("__SVG__", l3_svg).replace("__META__", "8 個模組處理器 · 儲存 15 檔 · Office 引擎 4 檔 · 6 組橫切支援 · 8 項外部依賴")
open(os.path.join(docs_arch, "l3_infrastructure.html"), "w", encoding="utf-8", newline="\n").write(page)
print("l3 ok")

# ---------------- standalone L4 export ----------------
L4V0 = L4_TOP - 20
L4V1 = L4_TOP + L4H + 50
l4_parts = o[:TITLE_AT] + [G_OPEN] + o[L4_AT:L4_END] + [FOOT_L4, "</g>", "</svg>"]
l4_svg = "\n".join(l4_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L4V1 - L4V0}" viewBox="0 {L4V0} {W} {L4V1 - L4V0}"', 1)
open(os.path.join(svg_dir, "l4_inside.svg"), "w", encoding="utf-8", newline="\n").write(l4_svg.replace('<rect width="100%" height="100%" fill="#fff"/>', f'<rect x="0" y="{L4V0}" width="{W}" height="{L4V1 - L4V0}" fill="#fff"/>'))
page = from_l1_template("L4 模組內部", "l4_modules_internal.html").replace("上排是畫面（Renderer），下排是 Native 與 Fluent 共用的畫面模型。", "src/ 底下每個檔案都在這裡：上方是呼叫者，下方是被用的檔案；灰虛線小方塊是引用到別的面板的檔案，點它會連同本尊一起亮；紅虛線框是互相引用的循環。")
page = page.replace("__SVG__", l4_svg).replace("__META__", f"src/ 全部 {len(AF)} 個 .cs 檔 · {len(PANEL_INFO)} 張面板 · {len(INNER)} 條面板內引用 · {len(CROSS)} 條跨面板引用（灰虛線替身）")
open(os.path.join(docs_arch, "l4_modules_internal.html"), "w", encoding="utf-8", newline="\n").write(page)
print("l4 ok")


# ---------------- check hand-written names against the code ----------------
# L0-L3 boxes are laid out by hand; every *.cs name they mention must still exist under src/.
import html as _html
_known = {posixpath.basename(p) for p in AF}
_stems = {b.split(".")[0] for b in _known}
_l03 = _html.unescape("\n".join(o[:L4_AT]))
_l03 = _re.sub(r"-</text>\s*<text[^>]*>\s*", "", _l03)          # join names wrapped at a suffix word
_texts = " ".join(_re.findall(r"<text[^>]*>([^<]*)</text>", _l03))    # visible text only (not data-search attributes)
_named = set(_re.findall(r"[A-Za-z_][\w.*]*\.cs\b", _texts))
_missing = sorted(n for n in _named if n not in _known and not (".*." in n and n.split(".")[0] in _stems)
                  and not n.startswith(("Modules", "Clickra", "src")))
if _missing:
    print("⚠ L0～L3 圖上寫的檔名在 src/ 找不到（改名或刪除了，請更新手寫內容）：", ", ".join(_missing))
else:
    print("names ok")
