"""Clickra Architecture layered diagram generator.

Generates interactive HTML diagrams and SVG vector maps for Clickra layers
(L0 to L4) using Roslyn semantic dependency analysis.
"""
from __future__ import annotations
from typing import Any
import collections
import fnmatch
import html
import json
import os
import posixpath
import re

script_dir = os.path.dirname(os.path.abspath(__file__))
os.chdir(script_dir)
repo_root = os.path.abspath(os.path.join(script_dir, "..", ".."))
docs_arch = os.path.join(repo_root, "docs", "architecture", "diagrams")
svg_dir = os.path.join(docs_arch, "svg")
os.makedirs(svg_dir, exist_ok=True)
e = html.escape
W, H = 1556, 3540
F = 'font-family="Microsoft JhengHei UI,Microsoft JhengHei,Noto Sans CJK TC,Segoe UI,Arial,sans-serif"'
MONO_FONT = 'font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"'
# Reused string literals (Sonar S1192)
STR_PIPELINE = "Pipeline/"
STR_CORE_APP = "Core/Application/"
STR_FROM_L2 = "來自 L2"
STR_NATIVE_PROG = "Native 進度視窗"
STR_OFFICE_SETTINGS = "Office 引擎設定"
STR_EXT_PDFSHARP = "外部：PDFsharp"
SVG_CLOSE = "</svg>"
PAGE_OVERVIEW = "overview.html"
PAGE_L0 = "l0_entry_points.html"
PAGE_L1 = "l1_presentation.html"
PAGE_L2 = "l2_application.html"
PAGE_L3 = "l3_infrastructure.html"
PAGE_L4 = "l4_modules_internal.html"
SRC_FLUENT = "src/Clickra.Fluent/"


def _count_registered_use_cases() -> int:
    """Dynamically count registered conversion use cases in ConversionUseCases.cs."""
    cs_path = os.path.join(repo_root, "src", "Clickra.Core", "Application", "ConversionUseCases.cs")
    if os.path.isfile(cs_path):
        with open(cs_path, encoding="utf-8") as f:
            return len([m for m in re.findall(r"new\s+([A-Za-z0-9_]+UseCase)\b", f.read()) if not m.startswith("IConversionUseCase")])
    return 19


def _count_native_layouts() -> int:
    """Dynamically count native layout files in Clickra.CLI/UI/Layout."""
    layout_dir = os.path.join(repo_root, "src", "Clickra.CLI", "UI", "Layout")
    if os.path.isdir(layout_dir):
        return len([f for f in os.listdir(layout_dir) if f.endswith(".cs")])
    return 9


o: list[str] = []
A = o.append
A(f'<svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Clickra 架構圖" width="{W}" height="{H}" viewBox="0 0 {W} {H}" {F}>')
A('<defs>'
  '<marker id="a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="#344054"/></marker>'
  '<marker id="m" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="#98a2b3"/></marker>'
  '</defs>')
WHITE_BG = '<rect width="100%" height="100%" fill="#fff"/>'
A(WHITE_BG)
TITLE_AT = len(o)
A('<text x="40" y="44" font-size="24" font-weight="700" fill="#101828">Clickra 架構（逐層）</text>')
A('<text x="40" y="70" font-size="13" fill="#475467">由上往下讀：L0 入口 → L1 畫面。灰色虛線＝保留但目前未隨套件發佈（Fluent）。</text>')
G_OPEN = '<g transform="translate(76,0)">'
A(G_OPEN)


def band(y, _h, label):
    """Draw section band header and divider line."""
    A(f'<text x="40" y="{y}" font-size="12.5" font-weight="700" fill="#667085" letter-spacing=".5" paint-order="stroke" stroke="#fff" stroke-width="5">{e(label)}</text>')
    A(f'<line x1="40" y1="{y + 8}" x2="1440" y2="{y + 8}" stroke="#eaecf0"/>')


NODE_TEXT: dict[str, str] = {}


def _draw_box_files(x, w, fy, files):
    """Draw file listings within an architecture card node."""
    A(f'<line x1="{x + 14}" y1="{fy - 4}" x2="{x + w - 14}" y2="{fy - 4}" stroke="#eaecf0"/>')
    row = 0
    for f in files:
        by = fy + 12 + row * 14
        if isinstance(f, tuple):
            n, items = f
            cw = (w - 28) / n
            rows = (len(items) + n - 1) // n
            for k, it in enumerate(items):
                c, r = divmod(k, rows)
                A(f'<text x="{x + 14 + c * cw:.1f}" y="{by + r * 14}" font-size="10" fill="#344054" {MONO_FONT}>{e(it)}</text>')
            row += (len(items) + n - 1) // n
        elif f.endswith(("/", "：")):
            A(f'<text x="{x + 14}" y="{by}" font-size="9.5" fill="#98a2b3" {MONO_FONT}>{e(f)}</text>')
            row += 1
        else:
            A(f'<text x="{x + 14}" y="{by}" font-size="10" fill="#344054" {MONO_FONT}>{e(f)}</text>')
            row += 1


def box(x, y, w, h, title, lines=(), kind="door", tfs=15, nid=None, cls="", files=(), edge=None):
    """Draw an architecture card node with optional file listings."""
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
        _draw_box_files(x, w, fy, files)
    A('</g>')
    return (x, y, w, h)



_NAV_ON = 'border:1px solid #101828;color:#fff;background:#101828;font-weight:700;'
_NAV_OFF = 'border:1px solid #bfc6d1;color:#111827;background:#fff;'


def render_page(target_name, title, h1, active_href, controls, meta, note, svg_content):
    """Render full HTML page wrapping SVG diagram and navigation."""
    nav_links = [
        (PAGE_OVERVIEW, "總表"),
        (PAGE_L0, "L0 入口"),
        (PAGE_L1, "L1 畫面"),
        (PAGE_L2, "L2 應用"),
        (PAGE_L3, "L3 基礎"),
        (PAGE_L4, "L4 模組內部"),
    ]
    nav_html = []
    for href, label in nav_links:
        style = _NAV_ON if href == active_href else _NAV_OFF
        nav_html.append(f'<a href="{href}" style="text-decoration:none;font-size:12px;padding:6px 9px;border-radius:6px;{style}">{label}</a>')
    with open("template.html", encoding="utf-8") as fh:
        tmpl = fh.read()
    html_out = (
        tmpl.replace("__TITLE__", title)
            .replace("__H1__", h1)
            .replace("__NAV__", "\n".join(nav_html))
            .replace("__CONTROLS__", controls)
            .replace("__META__", meta)
            .replace("__NOTE__", note)
            .replace("__SVG__", svg_content)
    )
    target_file = os.path.join(docs_arch, os.path.basename(target_name))
    with open(target_file, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(html_out)



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
        if not re.fullmatch(r"[A-Za-z0-9_./-]+", n.strip()):   # only wrap plain identifiers / paths
            out.append(n)
            continue
        while len(n) > maxch:
            caps = [m.start() for m in re.finditer(r"[A-Z]", n[:maxch]) if m.start() > 4]
            # prefer cutting before a whole suffix word (UseCase, Processor, Presentation…) over splitting inside one
            nice = [p for p in caps if re.match(r"(UseCase|Processor|Presentation|Controller|Service|Registry|Metadata|Options|Renderer|Installer|Detector|Optimizer|Template|Command)", n[p:])]
            cut = max(nice or caps or [maxch - 1])
            out.append(n[:cut] + "-")
            n = "  " + n[cut:]
        out.append(n)
    return out


def pt(b, side, f=0.5):
    """Get connection point on the side of a bounding box."""
    x, y, w, h = b
    return {"top": (x + w * f, y), "bottom": (x + w * f, y + h), "left": (x, y + h * f), "right": (x + w, y + h * f)}[side]


EDGES: list[tuple[int, int, int, int]] = []
MARKERS: set[str] = set()


def path(points, label=None, dashed=False, muted=False, lpos=None, anchor="middle", src="", dst="", variant="both", cls="", color=None, width=None, arrow=True):
    """Draw a routed SVG path connecting nodes."""
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
X: list[int] = [40, 400, 760, 1120]
EW = 320
MAN = "packaging/msix/AppxManifest.xml"
DEP = "src/resources/AppxManifest.xml（--deploy）"
menu = box(X[0], 128, EW, 120, "① 檔案總管右鍵選單", ["Win11 新選單＋傳統選單", "對象：檔案／資料夾／資料夾背景"], "entry", nid="menu", files=[MAN, DEP])
tile = box(X[1], 128, EW, 120, "② 開始功能表「Clickra」", ["MSIX 安裝版 → ClickraLauncher.exe", "--deploy 部署（sparse 身分）→ Clickra.exe"], "entry", nid="tile", files=[MAN, DEP])
openw = box(X[2], 128, EW, 120, "③ 開啟檔案方式（檔案關聯）", [".pdf .docx .pptx .xlsx .md .png …14 種", "僅 MSIX 安裝版"], "entry", nid="open", files=[MAN])
term = box(X[3], 128, EW, 120, "④ 終端機／腳本", ["`Clickra.exe <command> <files>`", "`  [--quiet] [--out-dir] [--level] …`"], "entry", nid="term", files=["src/Clickra.CLI/Cli/ClickraStartup.cs", "src/Clickra.CLI/Cli/ClickraCli.Arguments.cs"])

# ---------------- doors
band(278, 0, "門（執行檔）")
shell = box(40, 300, 320, 164, "ClickraShell.dll", ["NativeAOT COM 擴充，跑在 dllhost.exe", "讀 ConvertCommandMetadata 決定選單", "無 Launcher（未封裝）時直接開 Fluent／Clickra.exe"], nid="shell", edge="#2e90fa",
            files=["src/ClickraShell/", (3, ["ComMethods.cs", "Exporter.cs", "Guids.cs", "Kernel32.cs", "ShellUtils.cs"]), "src/Shared/ConvertCommandMetadata.cs"])
launcher = box(400, 470, 640, 104, "ClickraLauncher.exe", ["NativeAOT、零相依；把動詞＋檔案原封轉交",
                                                          "嘗試以 COM 啟動 Fluent；目前套件沒有 Fluent → 立即轉給 Clickra.exe"], nid="launcher", edge="#2e90fa", files=["src/ClickraLauncher/Program.cs"])
fluent = box(40, 640, 480, 152, "Clickra.Fluent.exe（保留・目前未發佈）", ["WinUI 3；選用套件方案自 2026-10-08 凍結",
                                                    "不在 Clickra.msix 內，以下兩條路徑目前都不會成功"], "land", nid="fluent", cls="unshipped",
             files=[SRC_FLUENT, (3, ["App.xaml.cs", "MainWindow.xaml.cs", "TrayIcon.cs", "TrayService.cs", "ToastHelper.cs"]), "packaging/msix/AppxManifest.Fluent.xml"])
native = box(760, 640, 680, 170, "Clickra.exe", ["NativeAOT；入口 ClickraStartup.Run",
                                               "無參數 → 找 Clickra.Fluent.exe（目前找不到）→ Native Dashboard",
                                               "command＋檔案 → 進度視窗，或 --quiet 背景執行；另有 --version / --help / --deploy"], nid="native", edge="#2e90fa",
             files=["src/Clickra.CLI/Cli/", (4, ["ClickraStartup.cs", "ClickraCli.cs", "ClickraCli.Arguments.cs", "ClickraCli.Deploy.cs"]), "src/Clickra.CLI/", (4, ["GlobalUsings.Application.cs", "", "GlobalUsings.Architecture.cs"])])

# ---------------- edges: entrances -> doors
path([(200, 248), (200, 300)], "點選指令", lpos=(210, 272), anchor="start", src="menu", dst="shell", color="#2e90fa")
path([(512, 248), (512, 470)], "MSIX", lpos=(512, 384), src="tile", dst="launcher", variant="msix", color="#2e90fa")
path([(920, 248), (920, 470)], "檔案路徑", lpos=(920, 384), src="open", dst="launcher", variant="msix", color="#2e90fa")
path([(1280, 248), (1280, 640)], "參數", lpos=(1280, 430), src="term", dst="native", color="#2e90fa")
_gx_pt = pt(tile, "bottom", .82)[0]
path([(_gx_pt, 248), (_gx_pt, 264), (1080, 264), (1080, 640)], "--deploy 部署", dashed=True, lpos=(1000, 258), src="tile", dst="native", variant="deploy", color="#2e90fa")

# doors -> doors
path([(200, 464), (200, 522), (400, 522)], "動詞＋檔案", lpos=(300, 514), src="shell", dst="launcher", color="#2e90fa")
path([(477, 574), (477, 640)], "嘗試 Fluent（目前必失敗）", lpos=(486, 612), anchor="start", src="launcher", dst="fluent", muted=True, dashed=True, cls="unshipped")
path([(899, 574), (899, 640)], "實際路徑：立即轉交", lpos=(908, 612), anchor="start", src="launcher", dst="native", color="#2e90fa")
path([(760, 712), (520, 712)], "無參數：找 Fluent（目前找不到）", dashed=True, muted=True, lpos=(640, 705), src="native", dst="fluent", cls="unshipped")

# ======================= section frames =======================
def section(y0, y1, label, sub, col):
    """Draw high-level layer section background and header."""
    num, name = label.split("・")
    layer_id = f"sec-{num.strip().lower()}"
    # light tinted panel for the layer + a thin accent rule on its left edge
    A(f'<rect id="{layer_id}" class="layer-section" data-layer="{num.strip().lower()}" x="20" y="{y0}" width="{W - 76 - 40}" height="{y1 - y0}" rx="12" fill="{col}" fill-opacity=".035"/>')
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
            files=[SRC_FLUENT, "MainPage.xaml.cs", "MainPage.History.cs", "MainPage.LibreOffice.cs", "DynamicSettingsController.cs", "OfficeEnginePreflight.cs", "Controls/FluentDialogs.cs"])
fprog = box(255, 806, 200, 266, "Fluent 進度頁", ["TaskProgressPage", "續跑／帶參數啟動"], "land", 14, nid="fprog", cls="unshipped",
            files=[SRC_FLUENT, "TaskProgressPage.xaml.cs", "src/Clickra.Fluent/Controls/", "VisualSplitterControl.xaml.cs", "SplitPagesOverlay.xaml.cs", "FluentDialogs.cs"])
ndash = box(695, 806, 305, 266, "Native Dashboard", ["DashboardWindow（Win32 自繪）", "總覽／轉換／歷史／設定／關於", "轉換、續跑 → 開進度視窗"], "door", 14, nid="ndash", edge="#05603a",
            files=["src/Clickra.CLI/Dashboard/", "DashboardWindow.*.cs（18 檔）：", (2, ["（主檔）", "State", "Lifecycle", "Convert", "ConvertRegistry", "HitTesting",
                   "Events", "Events.Click", "Events.History", "Events.LibreOffice", "Events.Settings", "Paint", "Paint.Overview", "Paint.History",
                   "Paint.Settings", "Paint.Settings.Dynamic", "Paint.Dropdowns", "Paint.About"])])
nprog = box(1020, 806, 250, 266, STR_NATIVE_PROG, ["ProgressWindow", "附屬：密碼、Markdown 選項、", "視覺分割、系統匣", "⚠ 逐一辨認 8 個指令（C4）"], "door", 14, nid="nprog", edge="#0e9384",
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
path([(1365, 690), (1365, 806)], src="native", dst="nnone", label="--quiet／工具", lpos=(1358, 772), anchor="end", color="#2e90fa")
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
# semantic-msbuild.json comes from the repo's Roslyn tool (scripts/architecture/SemanticGraph): every project is
# compiled with MSBuild, and an edge means a file uses a symbol (type or member) declared in another file.
_GRAPH = os.environ.get("CLICKRA_ARCH_GRAPH", "semantic-msbuild.json")   # override only for tests
_g = json.load(open(_GRAPH, encoding="utf-8"))
if any(_g["diagnostics"]["projectErrors"].values()):
    raise RuntimeError(f"Semantic graph failed: {_g['diagnostics']}")
def _strip(p):
    """Strip src/ prefix if present."""
    return p[4:] if p.startswith("src/") else p
_lines = json.load(open(os.environ.get("CLICKRA_ARCH_LINES", "file_lines.json"), encoding="utf-8"))
AF = {_strip(p): _lines[_strip(p)] for p in _g["files"]}
AE = sorted({(_strip(e["source"]), _strip(e["target"])) for e in _g["edges"] if e["source"] != e["target"]})
ASYM = {(_strip(e["source"]), _strip(e["target"])): e["symbols"] for e in _g["edges"]}
# partial classes: Name.cs (or Name.xaml.cs) plus Name.*.cs in the same folder, checked against the expected set below
_by: dict[tuple[str, str], list[str]] = collections.defaultdict(list)
for _p in AF:
    _by[(posixpath.dirname(_p), posixpath.basename(_p).split(".")[0])].append(_p)
_grp: dict[str, str] = {}
for (_dir_path, _stem_name), _fs_list in _by.items():
    if len(_fs_list) > 1 and any(posixpath.basename(f) in (_stem_name + ".cs", _stem_name + ".xaml.cs") for f in _fs_list):
        for _file_p in _fs_list:
            _grp[_file_p] = _dir_path + "/" + _stem_name + ".*.cs"
PARTIAL_GROUPS = sorted(set(_grp.values()))


def anode(p):
    """Resolve partial class root node identifier."""
    return _grp.get(p, p)


TR = "Clickra.Core/Modules/PdfTranslation/"
TR_SMALL: set[str] = {"Application", "Execution", STR_PIPELINE, "Pipeline/Layout", "Pipeline/Fonts"}


def tr_folder(n):
    """Map translation module node to its pipeline folder."""
    r = n[len(TR):].split("/")
    if r[0] == "Pipeline":
        return STR_PIPELINE + (r[1] if len(r) > 2 else "")
    return r[0]


def panel_of(n):
    """Determine which architecture panel owns a given node."""
    s = n.split("/")
    if s[0] in ("ClickraShell", "ClickraLauncher", "Shared"):
        return "entry"
    if s[0] == "Clickra.CLI":
        return "cli"
    if s[0] == "Clickra.Fluent":
        return "fluent"
    if n.startswith(TR):
        f = tr_folder(n)
        if f in TR_SMALL:
            return "tr_misc"
        return "tr_" + f.split("/")[-1].lower()
    if s[1] == "Modules":
        return "m_" + s[2].lower()
    if s[1] in ("Application", "Presentation"):
        return "capp"
    if s[1] == "Storage":
        return "store"
    if s[1] == "Processors":
        return "proc"
    return "cross"


NODES: dict[str, dict[str, Any]] = collections.defaultdict(lambda: {"files": [], "lines": 0})
for _p, _lines_cnt in AF.items():
    _node_ent = NODES[anode(_p)]
    _node_ent["files"].append(posixpath.basename(_p)); _node_ent["lines"] += _lines_cnt
NEDGES = sorted({(anode(a), anode(b)) for a, b in AE if anode(a) != anode(b)})
PANEL = {n: panel_of(n) for n in NODES}
INNER = [(a, b) for a, b in NEDGES if PANEL[a] == PANEL[b]]
CROSS = [(a, b) for a, b in NEDGES if PANEL[a] != PANEL[b]]
"""Which source files each diagram box stands for (paths under src/, fnmatch patterns; '**' = any depth).
The generator fills the boxes' file lists from here and checks every drawn line against the Roslyn graph:
a line is drawn only when the code really has that reference, and a reference with no line is reported."""

CARDS: dict[str, list[str]] = {
    # ---- L1 screens (renderers)
    "ndash": ["Clickra.CLI/Dashboard/*"],
    "nprog": ["Clickra.CLI/Progress/*"],
    "nnone": ["Clickra.CLI/Cli/*"],
    "m_lay": ["Clickra.CLI/UI/**", "Clickra.CLI/Native/*"],
    "fmain": ["Clickra.Fluent/MainPage*", "Clickra.Fluent/DynamicSettingsController.cs", "Clickra.Fluent/OfficeEnginePreflight.cs",
              "Clickra.Fluent/Controls/FluentDialogs.cs"],
    "fprog": ["Clickra.Fluent/TaskProgressPage.xaml.cs", "Clickra.Fluent/Controls/VisualSplitterControl.xaml.cs",
              "Clickra.Fluent/Controls/SplitPagesOverlay.xaml.cs"],
    # ---- L1 shared presentation state and data catalogs
    "m_nav": ["Clickra.Core/Presentation/Dashboard/*"],
    "m_ws": ["Clickra.Core/Presentation/Convert/ConvertWorkspaceController.cs"],
    "m_exec": ["Clickra.Core/Presentation/Convert/ConversionExecutionController.cs"],
    "m_prog": ["Clickra.Core/Presentation/Progress/*"],
    "m_split": ["Clickra.Core/Modules/PdfSplit/Presentation/*"],
    "m_hist": ["Clickra.Core/Modules/HistoryTasks/Presentation/*HistoryPresentationController.cs"],
    "d_feed": ["Clickra.Core/Modules/HistoryTasks/Presentation/HistoryFeed.cs"],
    "m_set": ["Clickra.Core/Modules/Settings/Presentation/*SettingsPresentationController.cs"],
    "d_set": ["Clickra.Core/Modules/Settings/Presentation/SettingDescriptor.cs"],
    # ---- L2 application services (row 1)
    "a_cmd": ["Clickra.Core/Application/ConvertCommandRegistry.cs"],
    "a_uc": ["Clickra.Core/Application/ConversionUseCases.cs", "Clickra.Core/Application/ConversionUseCaseRegistry.cs",
             "Clickra.Core/Application/IConversionUseCase.cs", "Clickra.Core/Application/ConversionRequest.cs",
             "Clickra.Core/Application/ConversionPlan.cs", "Clickra.Core/Application/ConversionResult.cs",
             "Clickra.Core/Application/ConversionInteraction.cs"],
    "a_pages": ["Clickra.Core/Modules/PdfSplit/Application/PdfPageCountQuery.cs"],
    "a_hist": ["Clickra.Core/Modules/HistoryTasks/Application/*"],
    "a_off": ["Clickra.Core/Modules/Settings/Application/LibreOfficeSettingsController.cs"],   # before a_set: first match wins
    "a_set": ["Clickra.Core/Modules/Settings/Application/*"],
    # ---- L2 use cases
    "a_base": ["Clickra.Core/Application/*UseCaseBase.cs"],
    "a_life": ["Clickra.Core/Application/ConversionTask*.cs"],
    "u_Image": ["Clickra.Core/Modules/Image/Application/*"],
    "u_Markdown": ["Clickra.Core/Modules/Markdown/Application/*"],
    "u_Office": ["Clickra.Core/Modules/Office/Application/*"],
    "u_PdfCompression": ["Clickra.Core/Modules/PdfCompression/Application/*"],
    "u_PdfDecrypt": ["Clickra.Core/Modules/PdfDecrypt/Application/*"],
    "u_PdfMerge": ["Clickra.Core/Modules/PdfMerge/Application/*"],
    "u_PdfTranslation": ["Clickra.Core/Modules/PdfTranslation/Application/*"],
    "u_PdfSplit": ["Clickra.Core/Modules/PdfSplit/Application/*"],
    # ---- below L2 (exits)
    "s_shared": ["Shared/*"],
    "s_office": ["Clickra.Core/Modules/Office/Infrastructure/*"],
    "s_store": ["Clickra.Core/Storage/*"],
    "s_proc": ["Clickra.Core/Modules/*/Processing/**", "Clickra.Core/Processors/**", "Clickra.Core/Modules/PdfTranslation/**"],
}
# boxes later in this list do not take files an earlier box already took
ORDER = list(CARDS)
L2_CARDS = [k for k in CARDS if k.startswith(("a_", "u_"))]
L2_SCOPE: list[str] = ["Clickra.Core/Application/*", "Clickra.Core/Modules/*/Application/*"]   # every file here must sit in an L2 card


def _match(file_path, pat):
    """Match file path against glob pattern."""
    if "**" in pat:
        return fnmatch.fnmatch(file_path, pat.replace("/**", "/*")) or fnmatch.fnmatch(file_path, pat.replace("**", "*"))
    return fnmatch.fnmatch(file_path, pat) and file_path.count("/") == pat.count("/")


def assign(paths):
    """Assign file paths to matching architecture cards."""
    out = {}
    for p in sorted(paths):
        for k in ORDER:
            if any(_match(p, pat) for pat in CARDS[k]):
                out[p] = k
                break
    return out
_L1BOX = assign(AF)
_L1_SCOPE: list[str] = ["Clickra.Core/Presentation/*/*", "Clickra.Core/Modules/*/Presentation/*"]
_l1_orphans = [p for p in AF if any(_match(p, s) for s in _L1_SCOPE) and not _L1BOX.get(p, "").startswith(("m_", "d_"))]
if _l1_orphans:
    raise RuntimeError(f"L1 presentation files not on any card: {_l1_orphans}")


def l1_files(k):
    """Get list of presentation files belonging to an L1 card."""
    return wrap(sorted(posixpath.basename(p) for p, b in _L1BOX.items() if b == k), 22)


def l1_link(a, b):
    """Check if an architecture link exists between two L1 cards."""
    return any(_L1BOX.get(x) == a and _L1BOX.get(y) == b for x, y in AE)


MODELS_DEF: list[tuple[str, str, list[str], str]] = [
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
M: dict[str, tuple[float, float, float, float]] = {}


def card(nid, x, y, w, h, title, lines, where, stroke, fill):
    """Draw an L1 presentation card component."""
    A(f'<g class="node" data-id="{nid}" data-search="{e((title + " " + "".join(lines) + " " + where).lower())}">')
    A(f'<rect x="{x:.1f}" y="{y}" width="{w:.1f}" height="{h}" rx="9" fill="{fill}" stroke="{stroke}" stroke-width="1.6"/>')
    A(f'<text x="{x + 10:.1f}" y="{y + 20}" font-size="13" font-weight="700" fill="#101828">{e(title)}</text>')
    for j, l in enumerate(lines):
        A(f'<text x="{x + 10:.1f}" y="{y + 38 + j * 14}" font-size="9.8" fill="#475467" font-family="Cascadia Mono,Consolas,DejaVu Sans Mono,monospace" xml:space="preserve">{e(l)}</text>')
    A(f'<text x="{x + 10:.1f}" y="{y + h - 9}" font-size="10" fill="#98a2b3">{e(where)}</text>')
    A('</g>')


for _i, (_mid, _m_title, _m_cls_lines, _m_where) in enumerate(MODELS_DEF):
    _mx = MX0 + _i * (MW + MGAP)
    card(_mid, _mx, MY, MW, MH, _m_title, _m_cls_lines, _m_where, "#12b76a", "#f6fef9")
    M[_mid] = (_mx, MY, MW, MH)

# ======================= L1: data / catalogs =======================
band(1372, 0, "資料／目錄（不持有狀態，供畫面狀態與畫面直接取用）")
DY, DH = 1394, 78
DATA: dict[str, tuple[float, float, float, float]] = {}
for did, under, _d_title, _d_lines, _d_where in [
    ("d_feed", "m_hist", "歷史顯示清單", ["HistoryFeed.cs"], "Modules/HistoryTasks"),
    ("d_set", "m_set", "設定目錄", ["SettingDescriptor.cs"], "Modules/Settings"),
]:
    _did_x, _, _did_w, _ = M[under]
    card(did, _did_x, DY, _did_w, DH, _d_title, _d_lines, _d_where, "#12b76a", "#f6fef9")
    DATA[did] = (_did_x, DY, _did_w, DH)
    # controller uses its data helper
    path([(_did_x + _did_w / 2, MY + MH), (_did_x + _did_w / 2, DY)], src=under, dst=did, color="#12b76a", width=1.6)

# screen -> controller / data wiring, straight from the code graph
USES = {s: [t for t in [m[0] for m in MODELS_DEF] + ["d_feed", "d_set"] if l1_link(s, t)] for s in ("ndash", "nprog", "fmain", "fprog")}
SCREEN_BOX: dict[str, tuple[float, float, float, float]] = {"ndash": ndash, "nprog": nprog, "fmain": fmain, "fprog": fprog}
LANE: dict[str, int] = {"fmain": 1108, "fprog": 1126, "ndash": 1144, "nprog": 1162}
COLOR: dict[str, str] = {"ndash": "#05603a", "nprog": "#0e9384", "fmain": "#98a2b3", "fprog": "#98a2b3"}
ORDER: list[str] = ["fmain", "fprog", "ndash", "nprog"]
GAP_SLOT: dict[str, int] = {"fmain": -6, "ndash": 6}
for s_id in ORDER:
    sx, sy, sw, sh = SCREEN_BOX[s_id]
    slot = ORDER.index(s_id)
    x_out = sx + sw / 2
    unshipped = s_id.startswith("f")
    for _use_t in USES[s_id]:
        _use_cls = "unshipped" if unshipped else ""
        if _use_t in M:
            tx, ty, _use_tw, th = M[_use_t]
            x_in = tx + _use_tw / 2 + (slot - 1.5) * 12
            _use_pts = [(x_out, sy + sh), (x_out, LANE[s_id]), (x_in, LANE[s_id]), (x_in, ty)]
        else:  # data box: drop through the gap left of its controller, enter from the left side
            dx, dy, dw, dh = DATA[_use_t]
            _use_gx = dx - MGAP / 2 + GAP_SLOT[s_id]
            ey = dy + dh / 2 + (GAP_SLOT[s_id] * 1.5)
            _use_pts = [(x_out, sy + sh), (x_out, LANE[s_id]), (_use_gx, LANE[s_id]), (_use_gx, ey), (dx, ey)]
        path(_use_pts, color=COLOR[s_id], width=1.6, dashed=unshipped, src=s_id, dst=_use_t, cls=_use_cls)
# quiet: bypass presentation
path([(1440 - 20, 1072), (1440 - 20, 1198)], src="nnone", dst="", label="不經 Presentation，直接進 L2 ↓", lpos=(1412, 1192), anchor="end", color="#12b76a")

A('</g>')  # close L1 translate
L1_END = len(o)

ORDER = list(CARDS)
# ======================= L2 from the code graph (exec'd at the top of l2_block.py) =======================
# Card file lists, "也被" lines and which lines may be drawn all come from semantic-msbuild.json + cards.py.
# Layout (columns, rows, routes) stays hand-placed; a planned line whose reference no longer exists is not drawn.
BOX = assign(AF)
MEMBERS: dict[str, list[str]] = collections.defaultdict(list)
for _p, _b in BOX.items():
    MEMBERS[_b].append(_p)
GROUP: dict[str, list[str]] = {"UC": ["a_base"] + [k for k in CARDS if k.startswith("u_")]}
_BLINK = collections.Counter((BOX.get(a), BOX.get(b)) for a, b in AE if BOX.get(a) and BOX.get(b) and BOX[a] != BOX[b])


def boxes(k):
    """Get all node box identifiers associated with a card key."""
    return GROUP.get(k, [k])


def linked(src, dst):
    """Check if two components have semantic edges connecting them."""
    return any(_BLINK[(a, b)] for a in boxes(src) for b in boxes(dst))


NAME: dict[str, str] = {"ndash": "Native Dashboard", "nprog": STR_NATIVE_PROG, "nnone": "CLI／ClickraStartup", "m_lay": "Native 版面表",
        "fmain": "Fluent 主頁", "fprog": "Fluent 進度頁", "a_cmd": "指令目錄", "a_uc": "用例目錄", "a_pages": "頁數查詢",
        "a_hist": "歷史服務", "a_set": "設定服務", "a_off": STR_OFFICE_SETTINGS, "a_base": "用例基底", "a_life": "任務生命週期",
        "UC": "各模組用例"}
NAME.update({m[0]: m[1] + "（狀態）" for m in MODELS_DEF})
NAME.update({"d_feed": "歷史顯示清單", "d_set": "設定目錄"})
for _card_k in CARDS:
    if _card_k.startswith("u_"):
        NAME.setdefault(_card_k, _card_k[2:] + " 用例")

# every line the L2 drawing plans (src, dst); a line is drawn only if linked(src, dst)
PLANNED: list[tuple[str, str]] = [("m_ws", "a_cmd"), ("m_exec", "a_uc"), ("m_split", "a_pages"), ("m_hist", "a_hist"), ("d_feed", "a_hist"), ("m_set", "a_set"), ("d_set", "a_set"),
           ("m_set", "a_off"), ("nprog", "a_pages"), ("fprog", "a_pages"), ("m_split", "s_proc"), ("a_uc", "UC"), ("UC", "a_life"),
           ("a_cmd", "s_shared"), ("UC", "s_proc"), ("a_pages", "s_proc"), ("a_hist", "s_store"), ("a_life", "s_store"),
           ("a_set", "s_store"), ("a_off", "s_office")]
DRAWN: list[tuple[str, str]] = []
STALE: list[tuple[str, str]] = []


def link(src, dst):
    """Draw a normal semantic link between two cards."""
    ok = linked(src, dst)
    (DRAWN if ok else STALE).append((src, dst))
    return ok


def problem_link(src, dst):
    """Draw an architectural violation or bypass link."""
    ok = linked(src, dst)
    if ok:
        DRAWN.append((src, dst))
    return ok


def files_of(k, notes=None, maxch=22):
    """Format member file list for display on a card."""
    notes = notes or {}
    names = sorted(posixpath.basename(p) for p in MEMBERS[k])
    out = []
    for n in names:
        out += wrap([n], maxch)
        if n in notes:
            out.append("  " + notes[n])
    return out


def callers_of(target, exclude=()):
    """Find upstream callers and consumers of an architecture target."""
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
    """Wrap descriptive text lines with CJK character width support."""
    def _char_w(c):
        """Calculate character width for text wrapping."""
        return px if ord(c) > 0x2e80 else px * 0.6
    lines, cur, w = [], "", 0.0
    toks = [t for t in text.replace("、", "、\0").replace("：", "：\0").split("\0") if t]
    if len(toks) > 1 and len(toks[0]) <= 4 and toks[0].endswith("："):   # keep a short label such as 也被： with its first item
        toks[:2] = [toks[0] + toks[1]]
    for tok in toks:
        tw = sum(_char_w(c) for c in tok)
        if cur.strip() and w + tw > width_px:
            lines.append(cur); cur, w = "  ", px * 1.2
        for c in tok:            # a single token longer than the line: break inside it
            if w + _char_w(c) > width_px and cur.strip():
                lines.append(cur); cur, w = "  ", px * 1.2
            cur += c; w += _char_w(c)
    if cur.strip():
        lines.append(cur)
    return lines


def also_lines(target, width, prefix="也被：", extra=()):
    """Format secondary consumer description lines."""
    who = callers_of(target) + list(extra)
    return wrap_text(prefix + "、".join(who), width - 20) if who else []


L1_BOXES: set[str] = {"ndash", "nprog", "nnone", "m_lay", "fmain", "fprog"} | {k for k in CARDS if k.startswith(("m_", "d_"))}
SKIP_TODO: dict[str, str] = {"s_proc": "C1", "s_office": "C5", "s_store": "?"}


def stub_lines(stub, width):
    """Format bypass and secondary caller warning lines."""
    who = callers_of(stub)
    l1 = [n for n in who if any(NAME.get(k) == n for k in L1_BOXES)]
    l2 = [n for n in who if n not in l1]
    out = []
    if l1:
        out += wrap_text("⚠ L1 直接用到、跳過 L2（" + SKIP_TODO.get(stub, "?") + "）：" + "、".join(l1), width - 20)
    if l2:
        out += wrap_text("也被：" + "、".join(l2), width - 20)
    return out
# ======================= L2: application (exec'd from layers.py) =======================
# Columns line up with the L1 shared-state row so L1 -> L2 calls drop straight down.
L1_OFF = 120                      # L1 group translate
L1_BOTTOM = 1498 + L1_OFF         # abs bottom of the L1 section
L2_TOP = L1_BOTTOM + 34           # abs top of the L2 section
L2H = 756
def col_pos(i):
    """Calculate column X coordinate."""
    return MX0 + i * (MW + MGAP)
AMBER, AMBER_FILL = "#dc6803", "#fffaeb"




def l2card(nid, pos, title, role=(), folder="", classes=(), also=(), stroke=AMBER, fill="#fff", dashed=False, tcol="#101828"):
    """Draw an L2 application service card."""
    x, y, w, h = pos
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
        A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="9" fill="#98a2b3" {MONO_FONT}>{e(folder)}</text>')
        cy += 13
    for c in classes:
        A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="9.8" fill="#344054" {MONO_FONT} xml:space="preserve">{e(c)}</text>')
        cy += 13
    if also:
        cy += 3
        red = False
        for l in also:
            red = l.startswith("⚠") or (red and l.startswith("  "))   # a ⚠ note and its continuation lines are red
            A(f'<text x="{x + 10:.1f}" y="{cy}" font-size="9.5" fill="{"#b42318" if red else "#98a2b3"}">{e(l)}</text>')
            cy += 12
    A('</g>')
    if cy - y > h + 4:
        raise RuntimeError(f"Card overflow: {nid}, {cy - y}, {h}")
    return (x, y, w, h)


# ---- part A (combined view only): L1 sources -> top edge of L2, drawn in absolute coords
CTRL_BOTTOM = MY + MH + L1_OFF
DATA_BOTTOM = DY + DH + L1_OFF
ENTRY = L2_TOP - 6                # where every L1 -> L2 line enters the L2 section
R1Y = 56                          # row-1 top (L2 local)


def c_mid(i):
    """Calculate horizontal midpoint of an L2 column."""
    return col_pos(i) + MW / 2


# (src, dst, x at L1 source, x where it enters L2)
CALLS: list[tuple[str, str, float, float]] = [
    ("m_ws", "a_cmd", c_mid(1), c_mid(1)),
    ("m_exec", "a_uc", c_mid(2), c_mid(2)),
    ("m_split", "a_pages", c_mid(4), c_mid(4)),   # page count through the visual-split state (was D2: screens called it directly)
    ("m_hist", "a_hist", col_pos(5) + MW - 22, col_pos(5) + MW - 22),
    ("d_feed", "a_hist", c_mid(5), c_mid(5)),
    ("m_set", "a_set", col_pos(6) + MW - 22, col_pos(6) + MW - 22),
    ("d_set", "a_set", c_mid(6), c_mid(6)),
    ("m_set", "a_off", c_mid(7), c_mid(7)),    # settings state -> Office engine settings (the empty 8th L1 slot)
]
for _c_src, _c_dst, sx, ex in CALLS:
    if not link(_c_src, _c_dst):
        continue
    if _c_dst == "a_off":
        # leave the settings card on its right side, cross the empty slot, drop into column 7
        my = MY + MH / 2 + L1_OFF
        _line_pts = [(col_pos(6) + MW, my), (ex, my), (ex, ENTRY)]
    elif _c_src in ("m_hist", "m_set"):
        # controller sits above its data card: go round it through the gap on its right
        _line_gx = sx + 22 + MGAP / 2
        _line_pts = [(sx, CTRL_BOTTOM), (sx, CTRL_BOTTOM + 10), (_line_gx, CTRL_BOTTOM + 10), (_line_gx, ENTRY - 10), (ex, ENTRY - 10), (ex, ENTRY)]
    elif _c_src.startswith("d_"):
        _line_pts = [(sx, DATA_BOTTOM), (ex, ENTRY)]
    else:
        _line_pts = [(sx, CTRL_BOTTOM), (ex, ENTRY)]
    path(_line_pts, src=_c_src, dst=_c_dst, color="#12b76a", width=1.6, arrow=False)
# visual-split bypass (L1 -> L3), red
BYX = col_pos(4) - MGAP / 2
SPLIT_BYPASS = problem_link("m_split", "s_proc")      # C1
if SPLIT_BYPASS:
    path([(col_pos(4) + 16, CTRL_BOTTOM), (col_pos(4) + 16, CTRL_BOTTOM + 10), (BYX, CTRL_BOTTOM + 10), (BYX, ENTRY)], src="m_split", dst="s_proc", color="#d92d20", width=1.6, dashed=True, arrow=False)
# renderers that call the page-count query directly (they skip L1 shared state)
SCR_BOTTOM = 806 + 266 + L1_OFF            # bottom of the L1 screen row (abs)
JOG = 1098 + L1_OFF                        # between the screen row and the L1 lanes
NPX, FPX = col_pos(5) - MGAP / 2, col_pos(3) - MGAP / 2   # gaps used to pass the shared-state row
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
for _c_src, _c_dst, sx, ex in CALLS:
    if (_c_src, _c_dst) not in DRAWN:
        continue
    path([(ex, -6), (ex, R1Y)], src=_c_src, dst=_c_dst, color="#12b76a", width=1.6)

# row-1 cards: titles and roles are hand-written; file lists and 也被 come from the code graph
ROW1: dict[str, tuple[float, str, list[str], str, list[str]]] = {  # id: (x, title, role lines, folder, extra lines under the files)
    "a_cmd": (col_pos(1), "指令目錄", ["指令、可接受副檔名、", "最少檔數、顯示名稱"], STR_CORE_APP, ["↳ src/Shared/"] + wrap(["  ConvertCommandMetadata.cs"], 22)),
    "a_uc": (col_pos(2), "轉換用例目錄", ["指令 → 唯一的用例", "Validate→Plan→Execute"], STR_CORE_APP, []),
    "a_pages": (col_pos(4), "頁數查詢", ["讀 PDF 頁數"], "Modules/PdfSplit/App…/", []),
    "a_hist": (col_pos(5), "歷史與暫停任務", ["清除與刪除／讀清單／", "認領暫停任務續跑"], "Modules/HistoryTasks/App…/", []),
    "a_set": (col_pos(6), "設定", ["讀寫偏好與儲存路徑"], "Modules/Settings/App…/", []),
    "a_off": (col_pos(7), STR_OFFICE_SETTINGS, ["安裝／移除 LibreOffice、", "回報引擎是否就緒"], "Modules/Settings/App…/", []),
}
ROW1_NOTES: dict[str, list[str]] = {"a_uc": ["需輸入時回呼 L1：", "密碼／分割頁／MD 選項"]}
ROW1_CONTENT: dict[str, tuple[list[str], list[str]]] = {}
for _k_card, (_c_x, _c_title, _c_role, _c_folder, _c_extra) in ROW1.items():
    _c_cls = files_of(_k_card) + _c_extra
    _c_also = also_lines(_k_card, MW) + ROW1_NOTES.get(_k_card, [])
    ROW1_CONTENT[_k_card] = (_c_cls, _c_also)
H1 = max(need_h(ROW1[k][2], ROW1[k][3], c, a) for k, (c, a) in ROW1_CONTENT.items()) + 8


def row1_card(k):
    """Draw a row-1 L2 application service card."""
    x, title, role, folder, _ = ROW1[k]
    cls_, also = ROW1_CONTENT[k]
    return l2card(k, (x, R1Y, MW, H1), title, role, folder, cls_, also, stroke=AMBER, fill=AMBER_FILL)


cmd, uc, pq = row1_card("a_cmd"), row1_card("a_uc"), row1_card("a_pages")
# upstream of the page-count query: two renderers (they skip L1 shared state)
# combined view: continue the lines that come down from the real L1 screen boxes
_c = len(o)
if NPROG_PAGES:
    path([(NPX, -6), (NPX, R1Y + 40), (col_pos(4) + MW, R1Y + 40)], src="nprog", dst="a_pages", color="#0e9384", width=1.6)
if FPROG_PAGES:
    path([(FPX, -6), (FPX, R1Y + 120), (col_pos(4), R1Y + 120)], src="fprog", dst="a_pages", color="#98a2b3", width=1.6, dashed=True, cls="unshipped")
COMBINED_ONLY = (_c, len(o))
# standalone L2: those screens are not on the page, so stand them in with chips in the free col-3 slot
_mark = len(o)
CH = (H1 - 10) / 2
for _k_chip, (_chip_nid, _chip_l1, _chip_l2, _chip_file, _chip_cls) in enumerate([
        ("nprog", "來自 L1 畫面", STR_NATIVE_PROG, "ProgressWindow.VisualSplitter.cs", ""),
        ("fprog", "來自 L1 畫面（未發佈）", "Fluent 視覺分割", "VisualSplitterControl.xaml.cs", "unshipped")]):
    if (_chip_nid, "a_pages") not in DRAWN:
        continue
    cy0 = R1Y + _k_chip * (CH + 10)
    _c_stroke, _c_dash = ("#98a2b3", ' stroke-dasharray="6 4"') if _chip_cls else ("#0e9384", "")
    A(f'<g class="node {_chip_cls}" data-id="{_chip_nid}" data-search="{e((_chip_l2 + " " + _chip_file).lower())}"><rect x="{col_pos(3):.1f}" y="{cy0:.1f}" width="{MW:.1f}" height="{CH:.1f}" rx="9" fill="#f6fef9" stroke="{_c_stroke}" stroke-width="1.4"{_c_dash}/>'
      f'<text x="{col_pos(3) + 10:.1f}" y="{cy0 + 18:.1f}" font-size="10" fill="#667085">{e(_chip_l1)}</text>'
      f'<text x="{col_pos(3) + 10:.1f}" y="{cy0 + 36:.1f}" font-size="12.5" font-weight="700" fill="#101828">{e(_chip_l2)}</text>'
      f'<text x="{col_pos(3) + 10:.1f}" y="{cy0 + 53:.1f}" font-size="9" fill="#344054" {MONO_FONT}>{e(_chip_file.split(".", 1)[0])}</text>'
      f'<text x="{col_pos(3) + 10:.1f}" y="{cy0 + 65:.1f}" font-size="9" fill="#344054" {MONO_FONT}>{e("." + _chip_file.split(".", 1)[1])}</text></g>')
    path([(col_pos(3) + MW, cy0 + CH / 2), (col_pos(4), cy0 + CH / 2)], src=_chip_nid, dst="a_pages", color="#98a2b3" if _chip_cls else "#0e9384", width=1.6, dashed=bool(_chip_cls), cls=_chip_cls)
STANDALONE_ONLY = o[_mark:]
del o[_mark:]
row1_card("a_hist")
row1_card("a_set")
row1_card("a_off")
L2_SERVICES = 6

# ---- conversion use cases (cols 0-3)
UCX0, UCX1 = col_pos(0), col_pos(3) + MW
R2 = R1Y + H1 + 40
A(f'<text x="{UCX0}" y="{R2}" font-size="12.5" font-weight="700" fill="#667085" letter-spacing=".5">轉換用例（ConversionUseCases 登記的 {_count_registered_use_cases()} 個指令）</text>')
BASEY = R2 + 16
base = l2card("a_base", (UCX0, BASEY, UCX1 - UCX0, 88), "共用執行基底　Core/Application/",
              [], "", ["ConversionUseCaseBase.cs（所有用例的父類別）",
                       "├ PerFileConversionUseCaseBase.cs（逐檔處理）",
                       "└ SingleOutputConversionUseCaseBase.cs（多檔合成一個輸出）"], stroke=AMBER, fill=AMBER_FILL)
if sorted(posixpath.basename(p) for p in MEMBERS["a_base"]) != ["ConversionUseCaseBase.cs", "PerFileConversionUseCaseBase.cs", "SingleOutputConversionUseCaseBase.cs"]:
    raise RuntimeError(f"Unexpected base members: {MEMBERS['a_base']}")
MOD_ORDER: list[str] = ["Image", "Markdown", "Office", "PdfCompression", "PdfDecrypt", "PdfMerge", "PdfTranslation", "PdfSplit"]
if sorted(MOD_ORDER) != sorted(k[2:] for k in CARDS if k.startswith("u_")):
    raise RuntimeError("a module was added or removed: place it in MOD_ORDER")
MOD_NOTES: dict[str, str] = {"ImageFormatConvertUseCase.cs": "（5 個指令）", "ImgMergeUseCase.cs": "（合一）", "ImgStitchUseCase.cs": "（合一）",
             "OfficeToPdfUseCaseBase.cs": "（查 Office 引擎）", "DecryptPdfUseCase.cs": "（需密碼）", "MergePdfUseCase.cs": "（合一）",
             "SplitPdfUseCase.cs": "（需分割頁）"}
SHORT: dict[str, str] = {STR_NATIVE_PROG: "Native", "CLI／ClickraStartup": "CLI"}


def mod_also(k):
    """Format secondary callers for a module use case card."""
    who = [SHORT.get(n, n) for n in callers_of(k)]
    if not who:
        return []
    c4 = "（C4）" if {"Native", "CLI"} & set(who) else ""
    return wrap_text("也被：" + "、".join(who) + c4, MWID - 20)


MG = 12
MWID = (UCX1 - UCX0 - 3 * MG) / 4
MODS = [(n, files_of("u_" + n, MOD_NOTES, 25), mod_also("u_" + n)) for n in MOD_ORDER]
MOD_RH: list[int] = [max(need_h(folder="x", classes=f, also=a) for _, f, a in MODS[:4]) + 6, max(need_h(folder="x", classes=f, also=a) for _, f, a in MODS[4:]) + 6]
MOD_Y: list[int] = [BASEY + 100, BASEY + 100 + MOD_RH[0] + 12]
for _k_mod, (_m_name, _m_items, _m_also) in enumerate(MODS):
    _mod_r, _mod_c = divmod(_k_mod, 4)
    mh = MOD_RH[_mod_r]
    l2card(f"u_{_m_name}", (UCX0 + _mod_c * (MWID + MG), MOD_Y[_mod_r], MWID, mh), _m_name, [], "…/Application/", _m_items, _m_also, stroke="#fdb022")
MOD_BOTTOM = MOD_Y[1] + MOD_RH[1]
if link("a_uc", "UC"):
    path([(c_mid(2), R1Y + H1), (c_mid(2), BASEY)], "依指令找到用例", lpos=(c_mid(2) + 8, R1Y + H1 + 24), anchor="start", src="a_uc", dst="a_base", color=AMBER, width=1.6)

# ---- task lifecycle (cols 5-6)
LCX, LCW = col_pos(5), col_pos(6) + MW - col_pos(5)
LIFE_FILES = files_of("a_life", maxch=60)
LIFE_ALSO = also_lines("a_life", LCW)
LIFE_H = need_h(["x"], STR_CORE_APP, LIFE_FILES, LIFE_ALSO) + 8
lc = l2card("a_life", (LCX, BASEY, LCW, LIFE_H), "任務生命週期", ["開始 → 進行中 → 完成／失敗／暫停，寫入任務紀錄"], STR_CORE_APP,
            LIFE_FILES, LIFE_ALSO, stroke=AMBER, fill=AMBER_FILL)
if link("UC", "a_life"):
    path([(UCX1, BASEY + 25), (LCX, BASEY + 25)], "執行時建立與結束任務紀錄", lpos=((UCX1 + LCX) / 2, BASEY + 18), src="a_base", dst="a_life", color=AMBER, width=1.6)

# ---- exits to L3
S3Y = MOD_BOTTOM + 58
STUB_W: dict[str, float] = {"s_proc": col_pos(4) + MW - col_pos(2), "s_store": col_pos(7) - 4 - (col_pos(5) - MGAP), "s_office": MW}
STUB_NOTE: dict[str, list[str]] = {"s_proc": ["紅色虛線：L1 視覺分割狀態（VisualSplitPresentationController:187）", "直接呼叫 PdfSplitProcessor.BuildSegmentSpec，跳過 L2（C1）"] if SPLIT_BYPASS else []}
STUB_ALSO = {k: STUB_NOTE.get(k, []) + stub_lines(k, w) for k, w in STUB_W.items()}
STUB_ROLE: dict[str, list[str]] = {"s_proc": ["Modules/*/Processing、Processors/FileProcessing"], "s_store": ["ClickraStorage／ClickraSettings 等 15 檔"],
             "s_office": ["安裝／偵測 LibreOffice、", "Modules/Office/Infra…"]}
S3H = max(78, max(need_h(STUB_ROLE[k], "", [], STUB_ALSO[k]) + 8 for k in STUB_W))
GREY = AMBER  # L2 -> L3 lines take the caller (L2) colour
# shared-definition node: stays in both views (it is not an L3 node)
shared = l2card("s_shared", (col_pos(0), S3Y, col_pos(1) + MW - col_pos(0), S3H), "共用指令表（src/Shared）", ["ConvertCommandMetadata.cs：指令、副檔名、", "最少檔數、顯示名稱；同一份也編進 ClickraShell"], "",
                [], ["指令集合與轉換用例目錄一致（架構測試保證）"], stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
if link("a_cmd", "s_shared"):
    path([(col_pos(1), R1Y + H1 - 24), (28, R1Y + H1 - 24), (28, S3Y + S3H / 2), (col_pos(0), S3Y + S3H / 2)], "查表", lpos=(34, R1Y + H1 - 30), anchor="start",
         src="a_cmd", dst="s_shared", color=GREY, width=1.4)
X_MOD, X_PAGES, X_HIST, X_LIFE, X_SET, X_OFF = c_mid(2) + 40, c_mid(4), col_pos(5) - MGAP / 2, LCX + LCW / 2, col_pos(7) - MGAP / 2, c_mid(7)
# (src, x, colour, dashed, stub id, real L3 id) — every L2 -> L3 exit; the L2 part ends at S3Y without an arrowhead
if BASEY + LIFE_H >= S3Y - 30:
    raise RuntimeError("task lifecycle card runs into the L3 exits")
# (src, planned link, x, colour, dashed, stub id, real L3 id) — every L2 -> L3 exit; drawn only if the code has it
EXITS: list[tuple] = [("a_base", ("UC", "s_proc"), X_MOD, GREY, False, "s_proc", "p_box"),
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
    path([(col_pos(5), R1Y + H1 - 30), (X_HIST, R1Y + H1 - 30), (X_HIST, S3Y)], src="a_hist", dst="t_store", color=GREY, width=1.4, arrow=False)
if "a_set" in _live:
    path([(col_pos(6) + MW, R1Y + H1 - 30), (X_SET, R1Y + H1 - 30), (X_SET, S3Y)], src="a_set", dst="t_store", color=GREY, width=1.4, arrow=False)
if "a_life" in _live:
    path([(X_LIFE, BASEY + LIFE_H), (X_LIFE, S3Y)], src="a_life", dst="t_store", color=GREY, width=1.4, arrow=False)
if "a_off" in _live:
    path([(X_OFF, R1Y + H1), (X_OFF, S3Y)], src="a_off", dst="t_offinf", color=GREY, width=1.4, arrow=False)
# standalone L2 only: the L3 stubs and the arrow tips that land on them
_mark = len(o)
A(f'<text x="{col_pos(2)}" y="{S3Y - 18}" font-size="12.5" font-weight="700" fill="#667085" letter-spacing=".5">往下 → L3</text>')
l2card("s_proc", (col_pos(2), S3Y, STUB_W["s_proc"], S3H), "L3 處理器（Processing）", STUB_ROLE["s_proc"], "", [], STUB_ALSO["s_proc"],
       stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
l2card("s_store", (col_pos(5) - MGAP, S3Y, STUB_W["s_store"], S3H), "L3 儲存（Storage）", STUB_ROLE["s_store"], "", [], STUB_ALSO["s_store"],
       stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
l2card("s_office", (col_pos(7), S3Y, MW, S3H), "L3 Office 引擎", STUB_ROLE["s_office"], "", [], STUB_ALSO["s_office"],
       stroke="#98a2b3", fill="#f9fafb", dashed=True, tcol="#475467")
for _src, _unused_dst, _x, _col, _dsh, _stub, _real in EXITS:
    path([(_x, S3Y - 10), (_x, S3Y)], src=_src, dst=_real, color=_col, width=1.6 if _dsh else 1.4, dashed=_dsh)
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
# ======================= L3: processing / storage / infrastructure (exec'd from l2_block.py) =======================

PURPLE, PURPLE_FILL, PURPLE_SOFT = "#7a5af8", "#f9f5ff", "#bdb4fe"
L3_TOP = L2_TOP + L2H + 34


# ---- part A (combined only): continue every L2 exit from where L2 stopped down to the L3 section
ENTRY3 = L3_TOP - 6
for _src, _unused_dst, _x, _col, _dsh, _stub, _real in EXITS:
    path([(_x, L2_TOP + S3Y), (_x, ENTRY3)], src=_src, dst=_real, color=_col, width=1.6 if _dsh else 1.4, dashed=_dsh, arrow=False)

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
    """Get sorted list of processor files for a module."""
    pre = f"Clickra.Core/Modules/{mod}/Processing/"
    return sorted(posixpath.relpath(p, pre) for p in AF if p.startswith(pre))


def _tr_summary():
    """Summarize translation pipeline file counts by folder."""
    pre = "Clickra.Core/Modules/PdfTranslation/"
    cnt = collections.Counter()
    for p in AF:
        if p.startswith(pre):
            r = p[len(pre):].split("/")
            cnt[r[0] + ("/" + r[1] if r[0] == "Pipeline" and len(r) > 2 else "")] += 1
    pipe = sorted(((k.split("/", 1)[1], v) for k, v in cnt.items() if k.startswith(STR_PIPELINE)), key=lambda t: -t[1])
    out = [f"Execution/（{cnt['Execution']}）", f"Pipeline/（{sum(v for k, v in cnt.items() if k.startswith('Pipeline'))} 檔）", "  PdfTranslationPipeline.cs"]
    out += [f"  {k}/（{v}）" for k, v in pipe]
    return out + [f"Model/（{cnt['Model']} 檔）", f"Translation/（{cnt['Translation']} 檔）"]


PROC: list[tuple[str, list[str], list[str]]] = [
    ("Image", _proc_files("Image"), ["外部：Imazen.WebP、WIC、", "System.Drawing、PDFsharp"]),
    ("Markdown", _proc_files("Markdown"), ["外部：Markdig、PDFsharp"]),
    ("Office", _proc_files("Office"), ["經 PowerShellHelper 叫 Office"]),
    ("PdfCompression", _proc_files("PdfCompression"), [STR_EXT_PDFSHARP]),
    ("PdfDecrypt", _proc_files("PdfDecrypt"), [STR_EXT_PDFSHARP]),
    ("PdfMerge", _proc_files("PdfMerge"), [STR_EXT_PDFSHARP]),
    ("PdfTranslation", _tr_summary(), ["外部：PdfPig、PDFsharp、", "網路翻譯（Google、MyMemory）"]),
    ("PdfSplit", _proc_files("PdfSplit"), [STR_EXT_PDFSHARP] + (["⚠ 也被 L1 視覺分割直接呼叫", "  （紅虛線，待辦 C1）"] if SPLIT_BYPASS else [])),
]
BASE3_Y = R1Y3 + 34
BASE3_H = 86
_proc_rows: list[list[tuple[str, list[str], list[str]]]] = [PROC[:4], PROC[4:]]
row_h: list[int] = [max(need_h(folder="x", classes=wrap(f, MAXCH), also=x) for _, f, x in r) for r in _proc_rows]
P_Y: list[float] = [BASE3_Y + BASE3_H + 12, BASE3_Y + BASE3_H + 12 + row_h[0] + 12]
CBOT = P_Y[1] + row_h[1] + 10
A(f'<g class="node" data-id="p_box" data-search="各模組處理器 processing"><rect x="{CX0:.1f}" y="{R1Y3}" width="{CX1 - CX0:.1f}" height="{CBOT - R1Y3:.1f}" rx="12" fill="{PURPLE_FILL}" stroke="{PURPLE}" stroke-width="1.6"/>'
  f'<text x="{CX0 + 12:.1f}" y="{R1Y3 + 22}" font-size="13" font-weight="700" fill="#101828">各模組處理器（Modules/*/Processing）</text></g>')
l2card("p_base", (UCX0, BASE3_Y, UCX1 - UCX0, BASE3_H), "共用處理基底　Processors/", [], "",
       ["FileProcessing/：IFileProcessor.cs ─ SingleFileProcessorBase.cs／MultiFileProcessorBase.cs",
        "                FileBackupScope.cs・ProgressCalculator.cs",
        "ProcessorExecutionOptions.cs・ExecutionLocalizationOptions.cs"], stroke=PURPLE, fill="#fff")
P_POS: dict[str, tuple[float, float, float, float]] = {}
for _r, _proc_row in enumerate(_proc_rows):
    for _proc_c, (_pr_name, _pr_files, _pr_ext) in enumerate(_proc_row):
        _px = UCX0 + _proc_c * (MWID + MG)
        P_POS[_pr_name] = (_px, P_Y[_r], MWID, row_h[_r])
        l2card(f"p_{_pr_name}", (_px, P_Y[_r], MWID, row_h[_r]), _pr_name, [], "…/Processing/", wrap(_pr_files, MAXCH), _pr_ext, stroke=PURPLE_SOFT, fill="#fff")

# part B entries into the processors
path([(X_MOD, -6), (X_MOD, R1Y3)], src="a_base", dst="p_box", color=GREY, width=1.4)
sx, sy, sw, sh = P_POS["PdfSplit"]
path([(X_PAGES, -6), (X_PAGES, sy + sh / 2 - 10), (sx + sw, sy + sh / 2 - 10)], src="a_pages", dst="p_PdfSplit", color=GREY, width=1.4)
if SPLIT_BYPASS:
    path([(BYX, -6), (BYX, sy + sh / 2 + 10), (sx + sw, sy + sh / 2 + 10)], src="m_split", dst="p_PdfSplit", color="#d92d20", width=1.6, dashed=True)

# ---- storage (cols 5-6) and Office engine (col 7)
STX0, STX1 = col_pos(5) - MGAP, col_pos(7) - 4
_st = sorted(posixpath.basename(p) for p in AF if p.startswith("Clickra.Core/Storage/"))
_facade = [f for f in _st if f.startswith("ClickraStorage")]
ST_FILES = [f"ClickraStorage.*.cs（門面，{len(_facade)} 檔）"] + [f for f in _st if f not in _facade]
store_h = max(need_h(["a", "b"], "x", ST_FILES, ["a", "b", "c"]), need_h(["a", "b"], "x", ["a"] * 6, ["a"] * 4)) + 8
l2card("t_store", (STX0, R1Y3, STX1 - STX0, store_h), "儲存（Storage）", ["對外只有靜態門面 ClickraStorage，其餘是各 Store；", "資料寫在使用者資料夾（設定、任務紀錄、歷史）"],
       f"Core/Storage/（{len(_st)} 檔）", ST_FILES,
       ["被：L2 歷史／設定／任務生命週期（上方三條線）", "另被：處理器取 ClickraSettings 預設值常數"], stroke=PURPLE, fill=PURPLE_FILL)
for _x in (X_HIST, X_LIFE, X_SET):
    _src = {X_HIST: "a_hist", X_LIFE: "a_life", X_SET: "a_set"}[_x]
    path([(_x, -6), (_x, R1Y3)], src=_src, dst="t_store", color=GREY, width=1.4)
l2card("t_offinf", (col_pos(7), R1Y3, MW, store_h), "Office 引擎", ["偵測、安裝、呼叫", "LibreOffice／Office"], "Modules/Office/Infra…/",
       wrap(sorted(posixpath.basename(p) for p in AF if p.startswith("Clickra.Core/Modules/Office/Infrastructure/")), 22),
       ["外部：LibreOffice 下載（HTTP）、", "soffice、PowerShell／Office"], stroke=PURPLE, fill=PURPLE_FILL)
path([(X_OFF, -6), (X_OFF, R1Y3)], src="a_off", dst="t_offinf", color=GREY, width=1.4)

# ---- external dependencies: right under the L3 nodes that use them, so the lines stay short and straight
EY = CBOT + 46
EH = 78
band(EY - 10, 0, "外部依賴（Clickra.Core.csproj 的套件、Windows 元件、外部程式與網路）")
EXT_DEF: list[tuple[str, str, list[str]]] = [  # (id, x0, x1, name, lines)
    ("ext_pdfsharp", "PDFsharp 6.2.4", ["壓縮・解密・合併", "分割・翻譯・圖片", "Markdown"]),
    ("ext_pdfpig", "PdfPig 0.1.14", ["翻譯（版面解析）", "縮圖（橫切）"]),
    ("ext_markdig", "Markdig 1.4.0", ["Markdown 解析"]),
    ("ext_webp", "Imazen.WebP 11", ["WebP 編碼（Image）"]),
    ("ext_drawing", "System.Drawing 8", ["Image・Markdown", "縮圖（橫切）"]),
    ("ext_wic", "Windows WIC", ["HEIC 等解碼（Image）"]),
]
EXW = (CX1 - CX0 - 5 * 8) / 6
EXT_POS: dict[str, tuple[float, float]] = {}


def ext_chip(nid, x0, x1, name, lines):
    """Draw external dependency chip component."""
    EXT_POS[nid] = (x0, x1)
    A(f'<g class="node" data-id="{nid}" data-search="{e((name + " " + " ".join(lines)).lower())}"><rect x="{x0:.1f}" y="{EY}" width="{x1 - x0:.1f}" height="{EH}" rx="9" fill="#f9fafb" stroke="#98a2b3" stroke-width="1.4" stroke-dasharray="6 4"/>'
      f'<text x="{x0 + 9:.1f}" y="{EY + 19}" font-size="11" font-weight="700" fill="#344054">{e(name)}</text>')
    for j, l in enumerate(lines):
        A(f'<text x="{x0 + 9:.1f}" y="{EY + 36 + j * 13}" font-size="9.5" fill="#667085">{e(l)}</text>')
    A('</g>')


for _k_ext, (_ext_nid, _ext_name, _ext_lines_list) in enumerate(EXT_DEF):
    _ext_x0 = CX0 + _k_ext * (EXW + 8)
    ext_chip(_ext_nid, _ext_x0, _ext_x0 + EXW, _ext_name, _ext_lines_list)
    path([(_ext_x0 + EXW / 2, CBOT), (_ext_x0 + EXW / 2, EY)], src="p_box", dst=_ext_nid, color=PURPLE, width=1.4)
ext_chip("ext_net", col_pos(4), col_pos(4) + MW, "網路", ["翻譯服務（Google、", "MyMemory）", "LibreOffice 下載"])
ext_chip("ext_data", STX0, STX1, "%LocalAppData%\\Clickra（本機）", ["設定、任務紀錄、歷史、暫停任務"])
ext_chip("ext_office", col_pos(7), col_pos(7) + MW, "LibreOffice／Office", ["soffice（LibreOffice）", "PowerShell＋Office COM"])
NETC = col_pos(4) + MW / 2
path([(CX1 - 6, CBOT), (CX1 - 6, EY - 16), (NETC - 24, EY - 16), (NETC - 24, EY)], src="p_box", dst="ext_net", color=PURPLE, width=1.4)
ST_BOTTOM = R1Y3 + store_h
path([((STX0 + STX1) / 2, ST_BOTTOM), ((STX0 + STX1) / 2, EY)], src="t_store", dst="ext_data", color=PURPLE, width=1.4)
path([(X_OFF, ST_BOTTOM), (X_OFF, EY)], src="t_offinf", dst="ext_office", color=PURPLE, width=1.4)
path([(col_pos(7) + 16, ST_BOTTOM), (col_pos(7) + 16, EY - 26), (NETC + 24, EY - 26), (NETC + 24, EY)], "下載 LibreOffice", lpos=(col_pos(6), EY - 30), src="t_offinf", dst="ext_net", color=PURPLE, width=1.4)

# ---- cross-cutting support (no lines: used almost everywhere; callers listed as text)
XY = EY + EH + 46
band(XY - 10, 0, "橫切支援（到處都在用，不畫線；呼叫者寫在「被：」）")
L3_CROSS: list[tuple[Any, ...]] = [
    ("x_loc", 0, 1, "在地化", "Core/Localization/", ["Localization.*.cs（5 檔）", "  （主檔）・.General・.Cli", "  .Fluent", "  .DocumentProcessing", "ChineseTextConverter", "  .*.cs（主檔・.Pairs）"],
     ["被：CLI、Fluent、處理器、用例"]),
    ("x_font", 1, 1, "字型", "Core/Fonts/", wrap(["ClickraFontResolver.cs"], 22), ["被：ClickraStartup、", "Markdown／壓縮／翻譯處理器", "外部：PDFsharp.Fonts"]),
    ("x_diag", 2, 1, "診斷與外部程序", "Core/Diagnostics/", ["ClickraDebug.cs", "ClickraShellProcess.cs"], ["被：Native 畫面、Fluent App、", "翻譯管線（除錯紀錄）"]),
    ("x_render", 3, 1, "PDF 縮圖", "Core/Rendering/", wrap(["PdfPageThumbnailRenderer.cs"], 22), ["⚠ 被 Native 進度視窗、", "Fluent 視覺分割直接呼叫", "（跳過 L1/L2，待辦 E3）", "外部：PdfPig、System.Drawing"]),
    ("x_root", 4, 1, "Core 根目錄", "Clickra.Core/", wrap(sorted(posixpath.basename(p) for p in AF if p.count("/") == 1 and p.startswith("Clickra.Core/")), 22), ["SystemPaths 被 Fluent 用"]),
    ("x_model", 5, 1, "設定數值範圍", "Modules/Settings/Model/", ["NumericSettingRange.cs"], ["被：L1 Dashboard／Fluent、", "L2 設定、Storage"]),
]
XH = max(need_h(folder=f, classes=c, also=a) for _, _, _, _, f, c, a in L3_CROSS)
for _c_nid, _c_col, _c_span, _c_title, _c_folder, _c_cls, _c_also in L3_CROSS:
    _cross_w = col_pos(_c_col + _c_span - 1) + MW - col_pos(_c_col)
    l2card(_c_nid, (col_pos(_c_col), XY, _cross_w, XH), _c_title, [], _c_folder, _c_cls, _c_also, stroke=PURPLE, fill=PURPLE_FILL)

L3H = XY + XH + 28
_mark = len(o)
section(0, L3H, "L3・基礎", "Infrastructure", PURPLE)
_sec = o[_mark:]
del o[_mark:]
o[_sec_at:_sec_at] = _sec                 # draw the section frame behind everything in L3
A('</g>')
L3_END = len(o)

# ---- standalone L3: stand-ins for the L2 / L1 sources above
L3_CHIPS: list[str] = []


def chip3(nid, x0, x1, l1, l2, col="#dc6803", fill="#fffaeb"):
    """Draw L3 infrastructure chip component."""
    L3_CHIPS.append(f'<g class="node" data-id="{nid}" data-search="{e(l2.lower())}"><rect x="{x0:.1f}" y="{L3_TOP - 64}" width="{x1 - x0:.1f}" height="50" rx="9" fill="{fill}" stroke="{col}" stroke-width="1.4"/>'
                    f'<text x="{x0 + 10:.1f}" y="{L3_TOP - 44}" font-size="10.5" fill="#667085">{e(l1)}</text>'
                    f'<text x="{x0 + 10:.1f}" y="{L3_TOP - 26}" font-size="12.5" font-weight="700" fill="#101828">{e(l2)}</text></g>')


chip3("a_base", col_pos(0), col_pos(2) + MW, STR_FROM_L2, "各模組轉換用例")
if SPLIT_BYPASS:
    chip3("m_split", col_pos(3), col_pos(4) - 6, "來自 L1（跳層）", "視覺分割狀態", col="#d92d20", fill="#fef3f2")
chip3("a_pages", col_pos(4), col_pos(4) + MW, STR_FROM_L2, "頁數查詢")
chip3("a_life", STX0, STX1, STR_FROM_L2, "歷史・任務生命週期・設定")
chip3("a_off", col_pos(7), col_pos(7) + MW, STR_FROM_L2, STR_OFFICE_SETTINGS)

RULE = ("線色規則：線的顏色＝發出呼叫的那一方。L0 藍、L1 綠（畫面另分 深綠＝Native Dashboard、青綠＝Native 進度視窗）、L2 橘、L3 紫；"
        "紅虛線＝跳層、灰虛線＝未發佈（Fluent）或外部依賴。")
FOOT_L3 = f'<text x="40" y="{L3_TOP + L3H + 30}" font-size="11.5" fill="#667085">{e(RULE)}</text>'

MAG, MAG_FILL, MAG_SOFT = "#c11574", "#fdf2fa", "#f670c7"
RED = "#d92d20"


def _tr_key(n):
    """Resolve translation pipeline node key."""
    return "Translation/" if tr_folder(n) == "Translation" else posixpath.basename(n)


def _tr_folder_label(f):
    """Format human-readable label for translation folder."""
    return {"Translation/": "Translation/（引擎）", STR_PIPELINE: "Pipeline/（根目錄）"}.get(f, f)


def _solve_order_dp(fs, w):
    """Solve minimum feedback edge ordering via DP over subsets."""
    k = len(fs)
    inf = 10 ** 9
    dp = [inf] * (1 << k)
    par = [None] * (1 << k)
    dp[0] = 0
    for m in range(1 << k):
        if dp[m] == inf:
            continue
        for j in range(k):
            if not (m >> j & 1):
                c = dp[m] + sum(w[j][s] for s in range(k) if m >> s & 1)
                if c < dp[m | 1 << j]:
                    dp[m | 1 << j] = c
                    par[m | 1 << j] = j
    m, order = (1 << k) - 1, []
    while m:
        j = par[m]
        order.append(fs[j])
        m ^= 1 << j
    return order[::-1]


def _tr_best_order(n5, e5, fo):
    """Calculate optimal folder ordering minimizing feedback edges."""
    cnt = collections.Counter((fo(a), fo(b)) for a, b in e5 if fo(a) != fo(b))
    fs = sorted({fo(n) for n in n5})
    k = len(fs)
    w = [[cnt.get((fs[i], fs[j]), 0) for j in range(k)] for i in range(k)]
    order = _solve_order_dp(fs, w)
    pos = {f: i for i, f in enumerate(order)}
    back = [(a, b) for a, b in e5 if fo(a) != fo(b) and pos[fo(b)] < pos[fo(a)]]
    return order, cnt, back


def _draw_dsm_cell(cx, cy_, cell, i, j, fa, fb, cnt, backset):
    """Draw a single cell in the DSM table."""
    if i == j:
        A(f'<rect x="{cx}" y="{cy_}" width="{cell}" height="{cell}" fill="#eaecf0" stroke="#fff"/>')
        return
    c = cnt.get((fa, fb), 0)
    if not c:
        A(f'<rect x="{cx}" y="{cy_}" width="{cell}" height="{cell}" fill="#fcfcfd" stroke="#f2f4f7"/>')
        return
    bad = (fa, fb) in backset
    A(f'<rect x="{cx}" y="{cy_}" width="{cell}" height="{cell}" fill="{"#fee4e2" if bad else "#fce7f6"}" stroke="#fff"/>'
      f'<text x="{cx + cell / 2}" y="{cy_ + 15}" font-size="10" font-weight="{700 if bad else 400}" fill="{"#b42318" if bad else "#c11574"}" text-anchor="middle">{c}</text>')


def _draw_dsm(x, y, order, cnt, back, title, sub, fo):
    """Draw Design Structure Matrix table for translation pipeline."""
    cell, lw, top = 22, 166, 64
    k = len(order)
    w, h = lw + k * cell + 16, top + k * cell + 40
    backset = {(fo(a), fo(b)) for a, b in back}
    A(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="#fff" stroke="{MAG}" stroke-width="1.6"/>'
      f'<text x="{x + 14}" y="{y + 22}" font-size="13.5" font-weight="700" fill="#101828">{e(title)}</text>'
      f'<text x="{x + 14}" y="{y + 39}" font-size="10" fill="{"#d92d20" if back else "#067647"}">{e(sub)}</text>')
    gx, gy = x + lw, y + top
    for j in range(k):
        A(f'<text x="{gx + j * cell + cell / 2}" y="{gy - 6}" font-size="9.5" fill="#667085" text-anchor="middle">{j + 1}</text>')
    for i, fa in enumerate(order):
        lbl = _tr_folder_label(fa).replace(STR_PIPELINE, "") if fa != STR_PIPELINE else "Pipeline/（根目錄）"
        A(f'<text x="{gx - 8}" y="{gy + i * cell + 15}" font-size="10" fill="#344054" text-anchor="end" {MONO_FONT}>{i + 1:>2} {e(lbl)}</text>')
        for j, fb in enumerate(order):
            cx, cy_ = gx + j * cell, gy + i * cell
            _draw_dsm_cell(cx, cy_, cell, i, j, fa, fb, cnt, backset)
    A(f'<text x="{x + 14}" y="{y + h - 12}" font-size="9.5" fill="#667085">列＝引用方、欄＝被引用方；數字＝檔案引用數。右上＝往下引用，左下紅＝回頭引用</text>')
    return w, h


def _draw_tcard(x, y, w, lines, stroke=MAG):
    """Draw translation pipeline summary card."""
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
            A(f'<text x="{x + 22}" y="{yy_ - 3}" font-size="9.8" fill="#344054" {MONO_FONT} xml:space="preserve">{e(t)}</text>')
    return h


def draw_translation_section(yy):
    # ======================= L4 part 2: PDF translation, file level (exec'd from l4_block.py, inside the L4 group) =======================
    # Data: the whole-repo model (l4_model.py); the translation engines are one node here (they have their own panel).
    #   1. a dependency matrix of the translation folders, rows ordered to need the fewest "back" references
    #      (exact order via DP over subsets); a back reference (lower-left, red) is a file using a file in a folder that
    #      should sit below it — any back reference means the folders form a cycle again (E8 regression);
    #   2. the back references, if any, and the file-level loops (strongly connected components);
    #   3. after this block, each folder's files are drawn by the normal L4 panels.
    """Draw translation pipeline Design Structure Matrix and cards."""
    CY = MAG
    N5 = {}
    for _n in NODES:
        if _n.startswith(TR):
            _k = _tr_key(_n)
            _f = tr_folder(_n)
            N5.setdefault(_k, {"folder": (_f if _f.endswith("/") else _f + "/"), "lines": 0})["lines"] += NODES[_n]["lines"]
    E5 = sorted({(_tr_key(a), _tr_key(b)) for a, b in NEDGES if a.startswith(TR) and b.startswith(TR) and _tr_key(a) != _tr_key(b)})

    def fold_now(n):
        """Get current folder of a translation node."""
        return N5[n]["folder"]

    ORDER_NOW, CNT_NOW, BACK_NOW = _tr_best_order(N5, E5, fold_now)
    _LOOPS = [sorted(c) for c in scc(list(N5), E5) if len(c) > 1]

    def short(f):
        """Format shortened folder name for display."""
        return f.replace(STR_PIPELINE, "") if f != STR_PIPELINE else "Pipeline/（根）"

    band(yy + 20, 0, f"翻譯模組（{len(N5) - 1} 個檔＋引擎，檔案多，分資料夾畫）：{len(E5)} 條檔案引用中，{len(BACK_NOW)} 條在回頭（待辦 E6、E8）")
    yy += 40
    _ok = not BACK_NOW
    w1, h1 = _draw_dsm(40, yy, ORDER_NOW, CNT_NOW, BACK_NOW, "資料夾之間的引用",
                       "✓ 沒有回頭引用：資料夾之間沒有循環" if _ok else f"⚠ {len(BACK_NOW)} 條回頭引用 → 資料夾又形成循環（待辦 E8）",
                       fold_now)
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
    _h2 = _draw_tcard(_lx, yy, 1440 - _lx, _info, stroke=RED if BACK_NOW else CY)
    yy += max(h1, _h2) + 14
    return yy

# ======================= L4: inside each L3 module (exec'd from l3_block.py) =======================
# Data: semantic-msbuild.json (Roslyn, scripts/architecture/SemanticGraph) + file_lines.json, read by l4_model.py.
# Layout: layered (Sugiyama-style) graph per module —
#   1. files that reference each other in a loop are merged into one red dashed group (strongly connected component);
#   2. rank = longest path from a caller, so callers sit on top and helpers at the bottom;
#   3. an edge that skips ranks gets an invisible waypoint in every rank it crosses, so it runs in its own lane;
#   4. order inside a rank is improved by barycentre sweeps (down / up / down) to cut crossings;
#   5. lines leave and enter boxes at spread-out ports and turn only inside the gaps between ranks.

L4_TOP = L3_TOP + L3H + 34
CH_W, NODE_H, GAP_X, PAD = 6.0, 36, 18, 18
PORT, LANE_H, GAP_PAD = 16, 8, 14
GROUP_MAX_W = 1000
P_STYLE: dict[str, Any] = {
    "col": MAG,
    "fill": MAG_FILL,
    "soft": MAG_SOFT,
    "un": False,
}


def _un(extra=""):
    """Format CSS classes for unshipped/fluent elements."""
    return (extra + " unshipped").strip() if P_STYLE["un"] else extra


def _dash(d="6 4"):
    """Return stroke-dasharray attribute if unshipped."""
    return f' stroke-dasharray="{d}"' if P_STYLE["un"] else ""


def sub_w(t):   # rough width of a 9px mixed CJK/latin string
    """Calculate display width of a subtitle string."""
    return sum(9.2 if ord(c) > 0x2e80 else 5.3 for c in t)


def _tarjan_strongconnect(v, adj, idx, low, st, on, out, k):
    """Tarjan strongconnect recursive traversal."""
    idx[v] = low[v] = k[0]
    k[0] += 1
    st.append(v)
    on.add(v)
    for w in adj[v]:
        if w not in idx:
            _tarjan_strongconnect(w, adj, idx, low, st, on, out, k)
            low[v] = min(low[v], low[w])
        elif w in on:
            low[v] = min(low[v], idx[w])
    if low[v] == idx[v]:
        comp = []
        while True:
            w = st.pop()
            on.discard(w)
            comp.append(w)
            if w == v:
                break
        out.append(sorted(comp))


def scc(nodes, edges):
    """Compute strongly connected components via Tarjan algorithm."""
    adj = {n: [] for n in nodes}
    for a, b in edges:
        adj[a].append(b)
    idx, low, st, on, out, k = {}, {}, [], set(), [], [0]
    for n in nodes:
        if n not in idx:
            _tarjan_strongconnect(n, adj, idx, low, st, on, out, k)
    return out


def mono_w(t, px=9.5):   # width of a monospace line that may contain CJK
    """Calculate monospace text display width."""
    return sum(px if ord(c) > 0x2e80 else px * 0.6 for c in t)


STACK_ROW = 20


def node_box(label, info):
    """Calculate dimensions and layout of a single node box."""
    if info.get("stack"):                          # cross-panel stand-ins: one chip per referenced file
        return max(mono_w(t) for _, t in info["stack"]) + 34, len(info["stack"]) * STACK_ROW + 6
    if label.endswith("/") or info.get("card"):    # folder / partial-class card: title + every file name
        return 236, 30 + 13 * len(wrap(sorted(info["files"]), 34)) + 8
    return max(len(label) * CH_W + 22, sub_w(info.get("sub", "")) + 22, 96), NODE_H


def layout(data, maxw=1340):
    """Compute multi-rank layout for panel nodes."""
    minr = {}
    for _ in range(60):
        lo = _layout_once(data, minr)
        wide = [r for r in range(lo["nr"]) if _rank_w(lo, r) > maxw - 2 * PAD]
        if not wide:
            return lo
        r = wide[0]
        size, order = lo["size"], lo["order"]
        has_out = {p for p, q in lo["links"]}
        cand = sorted([i for i in order[r] if not isinstance(i, tuple)], key=lambda i, ho=has_out, sz=size: (i in ho, -sz[i][0]))
        excess = _rank_w(lo, r) - (maxw - 2 * PAD)
        moved, left = False, len(cand)
        for i in cand:
            if excess <= 0 or left <= 1:
                break
            minr[i] = r + 1; excess -= size[i][0] + GAP_X; moved = True; left -= 1
        if not moved:
            raise RuntimeError(f"rank too wide and nothing to move: {r}")
    raise AssertionError("layout did not converge")


def _rank_w(lo, r):
    """Calculate total width of a single rank in panel layout."""
    return sum(lo["size"][i][0] for i in lo["order"][r]) + GAP_X * (len(lo["order"][r]) - 1)


def _compute_comp_boxes(comps, data):
    """Compute component bounding boxes and grid layout."""
    size, grid = {}, {}
    for i, c in enumerate(comps):
        if len(c) == 1:
            size[i] = node_box(c[0], data["nodes"][c[0]])
        else:
            node_boxes = [node_box(n, data["nodes"][n]) for n in c]
            cw = max(b[0] for b in node_boxes)
            cols = max(1, min(len(c), int((GROUP_MAX_W - 24 + 12) // (cw + 12))))
            rows_ = [node_boxes[r:r + cols] for r in range(0, len(node_boxes), cols)]
            gh = sum(max(b[1] for b in r) for r in rows_) + 10 * (len(rows_) - 1)
            size[i] = (cols * cw + (cols - 1) * 12 + 24, gh + 40)
            grid[i] = (cols, cw)
    return size, grid


def _compute_chains_and_links(cedges, rank, size):
    """Build routing chains and links between components."""
    chains = {}
    for a, b in cedges:
        ch = [a]
        for r in range(rank[a] + 1, rank[b]):
            d = ("d", a, b, r)
            rank[d] = r
            size[d] = (10, 0)
            ch.append(d)
        ch.append(b)
        chains[(a, b)] = ch
    links = [(ch[k], ch[k + 1]) for ch in chains.values() for k in range(len(ch) - 1)]
    for i in size:
        if not isinstance(i, tuple):
            deg = max(sum(1 for p, q in links if p == i), sum(1 for p, q in links if q == i))
            size[i] = (max(size[i][0], (deg - 1) * PORT + 24), size[i][1])
    return chains, links


def _init_rank_structures(rank, links, nr):
    """Initialize rank order and predecessor/successor mappings."""
    order = {r: [] for r in range(nr)}
    preds = {i: [] for i in rank}
    succs = {i: [] for i in rank}
    for i, r in rank.items():
        order[r].append(i)
    for p, q in links:
        succs[p].append(q)
        preds[q].append(p)
    return order, preds, succs


def _calc_rank_metrics(order, nr, size, links, rank):
    """Compute width, heights, and link counts per rank."""
    nat_w = 2 * PAD
    rowh = {}
    nlinks = dict.fromkeys(range(nr), 0)
    for p, _ in links:
        nlinks[rank[p]] += 1
    for r in range(nr):
        rw = sum(size[i][0] for i in order[r]) + GAP_X * max(0, len(order[r]) - 1)
        if rw + 2 * PAD > nat_w:
            nat_w = rw + 2 * PAD
        rowh[r] = max([size[i][1] for i in order[r]] + [0])
    return nat_w, rowh, nlinks


def _sweep_rank_order(order, nr, preds, succs):
    """Execute barycentric passes to optimize layer ordering."""
    for sweep in ("down", "up", "down", "up", "down"):
        rs = range(1, nr) if sweep == "down" else range(nr - 2, -1, -1)
        nb = preds if sweep == "down" else succs
        for r in rs:
            ref = {i: k for rr in order for k, i in enumerate(order[rr])}

            def _bary_key(i, nb=nb, ref=ref):
                ps = [ref[j] for j in nb[i]]
                return sum(ps) / len(ps) if ps else ref[i]
            order[r].sort(key=_bary_key)


def _barycentric_order(rank, links, size):
    """Optimize node order in ranks via barycentric sweeps."""
    nr = max(rank.values()) + 1
    order, preds, succs = _init_rank_structures(rank, links, nr)
    _sweep_rank_order(order, nr, preds, succs)
    nat_w, rowh, nlinks = _calc_rank_metrics(order, nr, size, links, rank)
    return order, nr, nat_w, rowh, nlinks


def _layout_once(data, minr):
    """Execute single iteration of panel layout optimization."""
    nodes = list(data["nodes"])
    edges = [tuple(x) for x in data["edges"]]
    comps = scc(nodes, edges)
    comp_of = {n: i for i, c in enumerate(comps) for n in c}
    cedges = sorted({(comp_of[a], comp_of[b]) for a, b in edges if comp_of[a] != comp_of[b]})
    rank = {i: minr.get(i, 0) for i in range(len(comps))}
    for _ in range(len(comps) + 1):
        for a, b in cedges:
            rank[b] = max(rank[b], rank[a] + 1)
    size, grid = _compute_comp_boxes(comps, data)
    chains, links = _compute_chains_and_links(cedges, rank, size)
    order, nr, nat_w, rowh, nlinks = _barycentric_order(rank, links, size)
    return {
        "comps": comps, "rank": rank, "size": size, "grid": grid, "chains": chains,
        "links": links, "order": order, "nr": nr, "nat_w": nat_w, "rowh": rowh,
        "nlinks": nlinks, "data": data,
    }


def _calc_panel_ports(comps, data, xs, size, out_l, in_l):
    """Compute ports dict for panel component links."""
    def ports(i, others):
        px, pw = xs[i], size[i][0]
        if isinstance(i, tuple):
            return {o: px + pw / 2 for o in others}
        if data["nodes"][comps[i][0]].get("stack"):
            return {o: px + 8 for o in others}
        others = sorted(others, key=lambda o: xs[o] + size[o][0] / 2)
        span = PORT * (len(others) - 1)
        return {o: px + pw / 2 - span / 2 + PORT * k for k, o in enumerate(others)}
    outp = {i: ports(i, v) for i, v in out_l.items()}
    inp = {i: ports(i, v) for i, v in in_l.items()}
    return outp, inp


def _nudge_panel_waypoints(xs, size, order, rank, outp, inp):
    """Nudge waypoints sideways to avoid overlapping box ports."""
    real_ports = collections.defaultdict(list)
    for i, d in list(outp.items()) + list(inp.items()):
        if not isinstance(i, tuple):
            real_ports[rank[i]] += list(d.values())
    for d in [i for i in xs if isinstance(i, tuple)]:
        near = real_ports[rank[d] - 1] + real_ports[rank[d] + 1] + real_ports[rank[d]]
        row = order[rank[d]]
        k = row.index(d)
        low_bound = xs[row[k - 1]] + size[row[k - 1]][0] + 4 if k > 0 else -1e9
        hi = xs[row[k + 1]] - 4 - size[d][0] if k + 1 < len(row) else 1e9
        for off in (0, 6, -6, 9, -9):
            nx = xs[d] + off
            if low_bound <= nx <= hi and all(abs(nx + size[d][0] / 2 - p) >= 6 for p in near):
                xs[d] = nx
                break


def _build_chain_path_points(ch, outp, inp, rank, pos, row_top, lo, lane_of):
    """Compute and simplify SVG path waypoints for a link chain."""
    pts = [(outp[ch[0]][ch[1]], pos[ch[0]][1] + pos[ch[0]][3])]
    for k in range(len(ch) - 1):
        p, q = ch[k], ch[k + 1]
        x1, x2 = outp[p][q], inp[q][p]
        r = rank[p]
        ym = row_top[r] + lo["rowh"][r] + GAP_PAD + LANE_H * lane_of.get((p, q), 0)
        pts += [(x1, ym), (x2, ym), (x2, row_top[rank[q]] + (lo["rowh"][rank[q]] if isinstance(q, tuple) else 0))]
    clean = [pts[0]]
    for _pt in pts[1:]:
        if abs(_pt[0] - clean[-1][0]) > 0.1 or abs(_pt[1] - clean[-1][1]) > 0.1:
            clean.append(_pt)
    return clean


def _draw_panel_chains(pid, lo, pos, row_top, rank, lane_of, outp, inp, comps):
    """Draw link chains between panel nodes."""
    for (a, b), ch in lo["chains"].items():
        clean = _build_chain_path_points(ch, outp, inp, rank, pos, row_top, lo, lane_of)
        src = comps[a][0] if len(comps[a]) == 1 else f"{pid}_loop{a}"
        dst = comps[b][0] if len(comps[b]) == 1 else f"{pid}_loop{b}"
        path(clean, src=f"{pid}:{src}", dst=f"{pid}:{dst}", color=P_STYLE["soft"], width=1.2, dashed=P_STYLE["un"], cls=_un())


def _draw_panel_comps(pid, comps, data, pos, lo):
    """Draw components and mutual loops inside panel."""
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
        cols, cw = lo["grid"][i]
        node_boxes = [(n, node_box(n, data["nodes"][n])) for n in c]
        yy_ = py + 32
        for r0 in range(0, len(node_boxes), cols):
            row = node_boxes[r0:r0 + cols]
            rh = max(b[1][1] for b in row)
            for j, (n, _) in enumerate(row):
                draw_node(pid, n, data["nodes"][n], px + 12 + j * (cw + 12), yy_, cw, rh)
            yy_ += rh + 10


def _calc_panel_x_coords(nr, order, size, x, w):
    """Compute X positions of nodes within each rank."""
    xs = {}
    for r in range(nr):
        tw = sum(size[i][0] for i in order[r]) + GAP_X * (len(order[r]) - 1)
        cx = x + (w - tw) / 2
        for i in order[r]:
            xs[i] = cx
            cx += size[i][0] + GAP_X
    return xs


def _assign_panel_lanes(nr, lo, rank, outp, inp):
    """Assign routing lanes for bent links per rank."""
    lane_of, nlane = {}, {}
    for r in range(nr):
        bent = [(p, q) for p, q in lo["links"] if rank[p] == r and abs(outp[p][q] - inp[q][p]) > 0.5]
        right = sorted([l for l in bent if inp[l[1]][l[0]] > outp[l[0]][l[1]]], key=lambda l: -outp[l[0]][l[1]])
        left = sorted([l for l in bent if inp[l[1]][l[0]] < outp[l[0]][l[1]]], key=lambda l: outp[l[0]][l[1]])
        for k, l in enumerate(right + left):
            lane_of[l] = k
        nlane[r] = len(bent)
    return lane_of, nlane


def draw_panel(pid, lo, x, y, w, title, sub, foot):
    """Draw complete subsystem panel with internal node graph."""
    comps, rank, size, order, nr = lo["comps"], lo["rank"], lo["size"], lo["order"], lo["nr"]
    data = lo["data"]
    xs = _calc_panel_x_coords(nr, order, size, x, w)
    out_l, in_l = {}, {}
    for p, q in lo["links"]:
        out_l.setdefault(p, []).append(q)
        in_l.setdefault(q, []).append(p)

    outp, inp = _calc_panel_ports(comps, data, xs, size, out_l, in_l)
    _nudge_panel_waypoints(xs, size, order, rank, outp, inp)
    outp, inp = _calc_panel_ports(comps, data, xs, size, out_l, in_l)
    lane_of, nlane = _assign_panel_lanes(nr, lo, rank, outp, inp)

    pos, row_top, cy = {}, {}, y + 50
    for r in range(nr):
        for i in order[r]:
            pos[i] = (xs[i], cy, size[i][0], size[i][1])
        row_top[r] = cy
        cy += lo["rowh"][r] + 2 * GAP_PAD + LANE_H * max(nlane[r] - 1, 0)
    ph = cy - y - 10 + 15 * len(foot) + 8
    A(f'<g class="{_un("node")}" data-id="{pid}" data-search="{e((title + " " + sub).lower())}"><rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{ph:.1f}" rx="12" fill="{P_STYLE["fill"]}" stroke="{P_STYLE["col"]}" stroke-width="1.6"{_dash("8 5")}/>'
      f'<text x="{x + 14:.1f}" y="{y + 22:.1f}" font-size="13.5" font-weight="700" fill="#101828">{e(title)}</text>'
      f'<text x="{x + 14:.1f}" y="{y + 37:.1f}" font-size="9.5" fill="#98a2b3" {MONO_FONT}>{e(sub)}</text>')
    for j, l in enumerate(foot):
        A(f'<text x="{x + 14:.1f}" y="{y + ph - 12 - 15 * (len(foot) - 1 - j):.1f}" font-size="10" fill="{"#b42318" if l.startswith("⚠") else "#667085"}">{e(l)}</text>')
    A('</g>')
    _draw_panel_chains(pid, lo, pos, row_top, rank, lane_of, outp, inp, comps)
    _draw_panel_comps(pid, comps, data, pos, lo)
    return ph


def draw_stack(pid, label, info, x, y, w, h, src_id):
    """Stand-ins for files in other panels: a bus down the left edge, one arrow per referenced file.
    Each chip carries the real file's data-id, so clicking either one lights up both."""
    A(f'<rect class="{_un()}" x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="6" fill="none" stroke="#d0d5dd" stroke-dasharray="3 3"/>')
    last = y + 3 + (len(info["stack"]) - 1) * STACK_ROW + 9
    path([(x + 8, y), (x + 8, last)], src=src_id, dst=f"{pid}:{label}", color=P_STYLE["soft"], width=1.2, arrow=False, dashed=P_STYLE["un"], cls=_un())
    for k, (tid, text) in enumerate(info["stack"]):
        cy_ = y + 3 + k * STACK_ROW
        path([(x + 8, cy_ + 9), (x + 18, cy_ + 9)], src=src_id, dst=tid, color=P_STYLE["soft"], width=1.2, cls=_un())
        A(f'<g class="{_un("node stub")}" data-id="{e(tid)}" data-search="{e(text.lower())}"><rect x="{x + 18:.1f}" y="{cy_:.1f}" width="{w - 22:.1f}" height="{STACK_ROW - 2}" rx="4" fill="#f9fafb" stroke="#98a2b3" stroke-dasharray="4 3"/>'
          f'<text x="{x + 24:.1f}" y="{cy_ + 12.5:.1f}" font-size="9.5" fill="#475467" {MONO_FONT}>{e(text)}</text></g>')


def draw_node(pid, label, info, x, y, w, h):
    """Draw individual node within a panel layout."""
    lines = info["lines"]
    A(f'<g class="{_un("node")}" data-id="{pid}:{label}" data-search="{e((label + " " + " ".join(info["files"])).lower())}"><rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="7" fill="#fff" stroke="{P_STYLE["col"]}" stroke-width="1.3"{_dash()}/>')
    if label.endswith("/") or info.get("card"):
        A(f'<text x="{x + 10:.1f}" y="{y + 17:.1f}" font-size="11" font-weight="700" fill="#101828" {MONO_FONT}>{e(label)}</text>'
          f'<text x="{x + w - 8:.1f}" y="{y + 17:.1f}" font-size="9" fill="#98a2b3" text-anchor="end">{len(info["files"])} 檔・{lines} 行</text>')
        for j, f in enumerate(wrap(sorted(info["files"]), 34)):
            A(f'<text x="{x + 10:.1f}" y="{y + 32 + j * 13:.1f}" font-size="9.5" fill="#344054" {MONO_FONT} xml:space="preserve">{e(f)}</text>')
    else:
        more = f"・{len(info['files'])} 檔" if len(info["files"]) > 1 else ""
        A(f'<text x="{x + 10:.1f}" y="{y + 15:.1f}" font-size="10" fill="#101828" {MONO_FONT}>{e(label)}</text>'
          f'<text x="{x + 10:.1f}" y="{y + 29:.1f}" font-size="9" fill="{"#b42318" if info.get("warn") else "#98a2b3"}">{e(info.get("sub", f"{lines} 行{more}"))}</text>')
    A('</g>')


# ---- L4 section: every .cs file under src/, grouped into panels
L4_AT = len(o)
A(f'<g transform="translate(0,{L4_TOP})">')
_sec4_at = len(o)
PANEL_INFO: dict[str, tuple[str, str, str]] = {  # key: (title, folder line, short name used on stand-ins)
    "entry": ("入口：ClickraShell・ClickraLauncher・Shared", "src/ClickraShell/、src/ClickraLauncher/、src/Shared/", "入口"),
    "cli": ("Clickra.CLI（Native 畫面＋CLI）", "src/Clickra.CLI/", "CLI"),
    "fluent": ("Clickra.Fluent（保留・目前未發佈；灰虛線）", SRC_FLUENT, "Fluent"),
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
    "tr_shared": ("翻譯・Shared（共用分類器與幾何）", "PdfTranslation/Pipeline/Shared/", "翻譯 Shared"),
    "tr_model": ("翻譯・Model", "PdfTranslation/Model/", "翻譯 Model"),
    "proc": ("共用處理基底", "Core/Processors/", "處理基底"),
    "store": ("儲存", "Core/Storage/", "儲存"),
    "cross": ("橫切支援", "Core/Localization/、Diagnostics/、Fonts/、Rendering/、Properties/、Core 根目錄", "橫切"),
}
if not (set(PANEL.values()) <= set(PANEL_INFO)):
    raise RuntimeError(f"Unknown panel values: {set(PANEL.values()) - set(PANEL_INFO)}")
PANEL_NOTES: dict[str, list[str]] = {
    "store": ["⚠ 門面 ClickraStorage 與大部分 Store 互相引用（待辦 E7）"],
}
GROUPS: list[tuple[str, list[str]]] = [
    ("入口與畫面：Shell、Launcher、Native（Clickra.CLI）、Fluent", ["entry", "cli", "fluent"]),
    ("Core 應用層：共用畫面狀態（L1）＋應用服務與用例（L2）", ["capp"]),
    ("各功能模組：每個模組含自己的 Application／Presentation／Processing", ["m_image", "m_markdown", "m_office", "m_pdfcompression", "m_pdfdecrypt", "m_pdfmerge", "m_pdfsplit", "m_settings", "m_historytasks"]),
    ("TRANSLATION", ["tr_misc", "tr_translation", "tr_rendering", "tr_annotations", "tr_textprocessing", "tr_grayprompts", "tr_paragraphs", "tr_diagrams", "tr_tables", "tr_model", "tr_shared"]),
    ("共用基礎：處理基底、儲存、橫切支援", ["proc", "store", "cross"]),
]
if sorted(k for _, ks in GROUPS for k in ks) != sorted(PANEL_INFO):
    raise RuntimeError("GROUPS does not match PANEL_INFO keys")


def _label(n):
    """Format file display label for panel node."""
    return posixpath.basename(n)


def _root(key):
    """Find root component of a partial class or node."""
    ns = [n for n in NODES if PANEL[n] == key]
    return posixpath.commonpath([posixpath.dirname(n) for n in ns]) if len(ns) > 1 else posixpath.dirname(ns[0])


def panel_data(key):
    """Extract graph node and edge data for a panel."""
    root = _root(key)
    ns = sorted(n for n in NODES if PANEL[n] == key)
    labels = [_label(n) for n in ns]
    if len(set(labels)) != len(labels):
        raise RuntimeError(f"Duplicate labels in panel: {key}, {labels}")
    nodes = {}
    for n in ns:
        nd = NODES[n]
        rel = posixpath.relpath(posixpath.dirname(n), root)
        sub = f"{nd['lines']} 行" + ("" if rel == "." else f"・{rel}/")
        warn = False
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


def _make_panel_item(key):
    """Build panel layout item from subsystem key."""
    lo = layout(panel_data(key))
    title, sub, _ = PANEL_INFO[key]
    nf = sum(len(NODES[n]["files"]) for n in NODES if PANEL[n] == key)
    foot = list(PANEL_NOTES.get(key, []))
    if key.startswith("tr_") and "BACK_NOW" in globals():
        labels = {_label(n) for n in NODES if PANEL[n] == key}
        nb = sum(1 for a, b in globals().get("BACK_NOW", []) if a in labels)
        if nb:
            foot.append(f"⚠ 這裡發出 {nb} 條回頭引用（見上方清單）")
    return (f"q_{key}", lo, max(320, lo["nat_w"]), f"{title}（{nf} 檔）", sub, foot)


def draw_group(keys, yy):
    """Draw group of subsystem panels arranged in responsive rows."""
    items = [_make_panel_item(k) for k in keys]
    rows = []
    for it in sorted(items, key=lambda t: -t[2]):
        for row in rows:
            if sum(r[2] for r in row) + 10 * len(row) + it[2] <= 1400:
                row.append(it)
                break
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
        for (pid, lo, nw, title, sub, foot) in row:
            w = nw + spare * nw / tot
            if pid == "q_fluent":     # unshipped: grey dashed like Fluent on every other layer
                P_STYLE.update({"col": "#98a2b3", "fill": "#f9fafb", "soft": "#98a2b3", "un": True})
            hs.append(draw_panel(pid, lo, xx, yy, w, title, sub, foot))
            P_STYLE.update({"col": MAG, "fill": MAG_FILL, "soft": MAG_SOFT, "un": False})
            xx += w + 10
        yy += max(hs) + 18
    return yy


band(38, 0, f"src/ 底下全部 {len(AF)} 個 .cs 檔（partial class 合成一個方塊）：箭頭＝檔案引用另一個檔案的型別；灰虛線小方塊＝引用到別的面板的檔案（點它會連同本尊一起亮）")
_group_yy = 58
for gtitle, _g_keys in GROUPS:
    if gtitle == "TRANSLATION":
        _group_yy = draw_translation_section(_group_yy)
        band(_group_yy + 18, 0, "翻譯模組各資料夾的檔案（第二行紅字＝建議搬走的檔）")
        _group_yy += 38
    else:
        band(_group_yy + 18, 0, gtitle)
        _group_yy += 38
    _group_yy = draw_group(_g_keys, _group_yy)
L4H = _group_yy + 12
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
A(SVG_CLOSE)
_H2 = L4_TOP + L4H + 60
o[0] = o[0].replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{_H2}" viewBox="0 0 {W} {_H2}"', 1)
H = _H2


svg_full = "\n".join(o)
with open(os.path.join(svg_dir, "clickra_layers.svg"), "w", encoding="utf-8", newline="\n") as _svg_fh:
    _svg_fh.write(svg_full)
# ---- Generate Overview Dashboard (High-level system topology) ----
def make_overview_svg():
    """Generate executive overview dashboard SVG."""
    ov = [
        f'<svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Clickra 架構總表" width="{W}" height="980" viewBox="0 0 {W} 980" {F}>',
        '<defs><marker id="arr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="#475467"/></marker></defs>',
        WHITE_BG,
        '<text x="40" y="44" font-size="24" font-weight="700" fill="#101828">Clickra 架構總表（Architecture Overview）</text>',
        '<text x="40" y="70" font-size="13" fill="#475467">五層架構全景與分層導覽：由上往下單向依賴。點擊各層方塊可開啟專屬互動式架構地圖。</text>',
    ]
    
    # Layer cards specification: (title, sub, href, color, fill, border, y, h, details)
    use_case_count = _count_registered_use_cases()
    module_count = len(MOD_ORDER)
    layout_count = _count_native_layouts()
    proc_count = len(PROC)
    dsm_pipeline_folders = len([f for f in set(tr_folder(n) for n in NODES if n.startswith(TR)) if f.startswith("Pipeline/") and f != "Pipeline/"])

    layers = [
        ("L0・入口（OS Entry Points & Handoffs）", "4 種使用者觸發方式 · 4 個進入點執行檔 · Win11 整合", PAGE_L0,
         "#2e90fa", "#f5faff", "#2e90fa", 100, 130,
         ["Win11 檔案總管右鍵選單、開始功能表「Clickra」捷徑、檔案類型關聯（.pdf .docx 等 14 種）、CLI 終端機指令",
          "進入點：ClickraShell（COM 選單）、ClickraLauncher（MSIX 啟動）、Clickra.CLI（NativeAOT 主程式）、Clickra.Fluent（WinUI 3）"]),
        ("L1・畫面（Presentation & Shared State Controllers）", f"{SCREENS} 個畫面 · {MODELS} 個共用畫面狀態 · {layout_count} 個 Native 版面表", PAGE_L1,
         "#12b76a", "#f6fef9", "#12b76a", 270, 130,
         ["畫面 Renderer：Native Dashboard（Win32 自繪）、Progress Window、Fluent UI（保留中，未隨套件發佈）",
          "共用控制器：ConvertWorkspace、ConversionExecution、TaskProgress、VisualSplit、History、Settings"]),
        ("L2・應用（Application Services & 19 Use Cases）", f"{L2_SERVICES} 組應用服務 · {module_count} 個功能模組 · {use_case_count} 個轉換用例目錄", PAGE_L2,
         "#dc6803", "#fffaeb", "#dc6803", 440, 130,
         ["核心服務：ConvertCommandRegistry（指令清單）、ConversionUseCases（用例目錄）、Task Lifecycle（任務生命週期）",
          f"{use_case_count} 個轉換用例：PDF 壓縮／解密／合併／分割／翻譯、圖片轉換與拼貼、Word／PPT／Excel 轉換、Markdown 文件"]),
        ("L3・基礎（Infrastructure, Storage & Engines）", f"{proc_count} 個模組處理器 · 儲存門面 · Office 引擎 · 6 組橫切支援", PAGE_L3,
         "#7a5af8", "#f9f5ff", "#7a5af8", 610, 130,
         ["處理器與引擎：SingleFileProcessorBase、MultiFileProcessorBase、ClickraStorage 門面、LibreOffice/Office 偵測與叫用",
          "外部依賴：PDFsharp 6.2.4、PdfPig 0.1.14、Imazen.WebP、System.Drawing、Markdig、Windows WIC"]),
        ("L4・模組內部（Modules Internal File Graphs & Cycles）", f"全部 {len(AF)} 個 C# 原始碼檔案 · {len(PANEL_INFO)} 個面板 · 循環依賴分析", PAGE_L4,
         "#c11574", "#fdf2fa", "#c11574", 780, 130,
         ["模組內部結構：src/ 所有 .cs 檔的語意引用圖（Roslyn MSBuild 分析）、強連通分量（SCC）循環偵測",
          f"翻譯管線 DSM 矩陣：{dsm_pipeline_folders} 個資料夾引用關係、回頭引用分析與架構邊界修復追蹤（E6/E8）"])
    ]
    
    for title, sub, href, col, fill, border, y, h, details in layers:
        ov.extend([
            f'<g class="node" data-id="{href}" onclick="location.href=\'{href}\'" style="cursor:pointer;">',
            f'<rect x="40" y="{y}" width="1476" height="{h}" rx="10" fill="{fill}" stroke="{border}" stroke-width="1.8"/>',
            f'<text x="64" y="{y + 32}" font-size="16" font-weight="700" fill="#101828">{e(title)}</text>',
            f'<text x="64" y="{y + 54}" font-size="12" font-weight="600" fill="{col}">{e(sub)}</text>',
            f'<line x1="64" y1="{y + 66}" x2="1492" y2="{y + 66}" stroke="#eaecf0"/>',
        ])
        for idx, dt in enumerate(details):
            ov.append(f'<text x="64" y="{y + 86 + idx * 20}" font-size="11.5" fill="#475467">{e(dt)}</text>')
        ov.extend([
            f'<rect x="1380" y="{y + 20}" width="112" height="32" rx="6" fill="#101828"/>',
            f'<text x="1436" y="{y + 41}" font-size="12" font-weight="700" fill="#ffffff" text-anchor="middle">開啟專屬圖 ➔</text>',
            '</g>',
        ])
        
        # Draw connector arrow to next layer
        if y < 780:
            arrow_y = y + h
            ov.append(f'<line x1="778" y1="{arrow_y}" x2="778" y2="{arrow_y + 40}" stroke="#475467" stroke-width="2" marker-end="url(#arr)"/>')

    ov.append('<text x="40" y="945" font-size="12" fill="#667085">💡 提示：點擊上方各層方塊可進入該層專屬詳細架構圖；若需檢視包含所有細節的完整長條總圖（1556×22330），請開啟 <a href="svg/clickra_layers.svg" target="_blank" style="fill:#2e90fa;font-weight:700;">🖼️ clickra_layers.svg</a>。</text>')
    ov.append(SVG_CLOSE)
    return "\n".join(ov)

meta_overview = (
    f"L0：4 種使用者動作 · 4 個執行檔　"
    f"L1：{SCREENS} 個畫面 · {MODELS} 個共用畫面狀態 · {_count_native_layouts()} 個 Native 版面表　"
    f"L2：{L2_SERVICES} 組應用服務 · {_count_registered_use_cases()} 個轉換指令　"
    f"L3：{len(PROC)} 個模組處理器 · 儲存 · Office 引擎 · 6 組橫切支援　"
    f"L4：全部 {len(AF)} 個檔的引用（{len(INNER) + len(CROSS)} 條）"
)
ctrl_overview = (
    '<button class="layer-jump on" data-layer="all">全部層級</button>\n'
    '<button class="layer-jump" data-layer="l0">L0 入口</button>\n'
    '<button class="layer-jump" data-layer="l1">L1 畫面</button>\n'
    '<button class="layer-jump" data-layer="l2">L2 應用</button>\n'
    '<button class="layer-jump" data-layer="l3">L3 基礎</button>\n'
    '<button class="layer-jump" data-layer="l4">L4 模組</button>\n'
    '<button id="traceBtn" class="var" title="切換端到端全路徑追蹤">跨層連鎖追蹤</button>\n'
    '<button id="unBtn">隱藏未發佈（Fluent）</button>'
)
note_overview = "L0 入口 → L1 畫面 → L2 應用 → L3 基礎 → L4 模組內部。由上往下單向依賴。點擊方塊可高亮連線；支援跨層連鎖追蹤與層級切換。"
render_page(PAGE_OVERVIEW, "Clickra Architecture — 總表", "Clickra — 架構總表", PAGE_OVERVIEW, ctrl_overview, meta_overview, note_overview, svg_full)
print("overview ok")



# ---------------- standalone L1 export ----------------
STUB: list[str] = [
    '<g class="node unshipped" data-id="fluent" data-search="clickra.fluent.exe"><rect x="40" y="756" width="480" height="54" rx="10" fill="#f9fafb" stroke="#98a2b3" stroke-width="1.4" stroke-dasharray="7 5"/>'
    '<text x="54" y="780" font-size="14" font-weight="700" fill="#475467">來自 L0：Clickra.Fluent.exe（保留・目前未發佈）</text>'
    '<text x="54" y="799" font-size="12" fill="#98a2b3">WinUI 3</text></g>',
    '<g class="node" data-id="native" data-search="clickra.exe"><rect x="760" y="756" width="680" height="54" rx="10" fill="#fff" stroke="#2e90fa" stroke-width="1.8"/>'
    '<text x="774" y="780" font-size="14" font-weight="700" fill="#101828">來自 L0：Clickra.exe</text>'
    '<text x="774" y="799" font-size="12" fill="#475467">NativeAOT；ClickraStartup.Run 依參數分派</text></g>',
]
Y0 = 730
l1_parts = o[:TITLE_AT] + [G_OPEN] + STUB + o[L1_AT:L1_END] + [FOOT_L1, "</g>", SVG_CLOSE]
L1H = L1_BOTTOM + 70
l1_svg = "\n".join(l1_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L1H - Y0}" viewBox="0 {Y0} {W} {L1H - Y0}"', 1)
with open(os.path.join(svg_dir, "l1_presentation.svg"), "w", encoding="utf-8", newline="\n") as _svg_fh:
    _svg_fh.write(l1_svg.replace(WHITE_BG, f'<rect x="0" y="{Y0}" width="{W}" height="{L1H - Y0}" fill="#fff"/>'))
ctrl_l1 = '<button id="unBtn">隱藏未發佈（Fluent）</button>'
meta_l1 = f"{SCREENS} 個畫面 · {MODELS} 個共用畫面狀態 · 2 個資料目錄 · {_count_native_layouts()} 個 Native 版面表（Clickra.CLI）"
note_l1 = "上排是畫面（Renderer），下排是 Native 與 Fluent 共用的畫面模型。點方塊可標出它的所有連線。"
render_page(PAGE_L1, "Clickra Architecture — L1 畫面", "Clickra — L1 畫面", PAGE_L1, ctrl_l1, meta_l1, note_l1, l1_svg)
print("l1 ok")

# ---------------- standalone L0 export ----------------
L0H = 870
l0_parts = o[:TITLE_AT] + o[TITLE_AT + 2:L1_AT] + ['<text x="40" y="852" font-size="11.5" fill="#667085">來源：packaging/msix/AppxManifest.xml（Store／GitHub 同一份 MSIX）、src/resources/AppxManifest.xml（--deploy）、ClickraShell/ComMethods.cs、ClickraLauncher/Program.cs、Clickra.CLI/Cli/ClickraStartup.cs、Clickra.Fluent/App.xaml.cs</text>', '</g>', SVG_CLOSE]
l0_svg = "\n".join(l0_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L0H - 70}" viewBox="0 70 {W} {L0H - 70}"', 1)
with open(os.path.join(svg_dir, "l0_entry_points.svg"), "w", encoding="utf-8", newline="\n") as _svg_fh:
    _svg_fh.write(l0_svg)
L0_EDGES = sum(1 for x in o[:L1_AT] if x.startswith('<g class="edge'))
ctrl_l0 = (
    '<button class="var on" data-v="all">全部路徑</button>\n'
    '<button class="var" data-v="msix">MSIX 安裝版</button>\n'
    '<button class="var" data-v="deploy">--deploy 部署</button>'
)
meta_l0 = f"4 種使用者動作 · 4 個執行檔 · {L0_EDGES} 條轉交"
note_l0 = "由上往下：使用者從哪裡進來 → 第一個碰到的執行檔 → 執行檔之間怎麼轉交。點方塊可標出它的所有路徑。"
render_page(PAGE_L0, "Clickra Architecture — L0 入口", "Clickra — L0 入口", PAGE_L0, ctrl_l0, meta_l0, note_l0, l0_svg)
print("l0 ok")

# ---------------- standalone L2 export ----------------
chips: list[str] = []
def chip(nid, x, w, label):
    """Draw layer navigation chip button."""
    chips.append(f'<g class="node" data-id="{nid}" data-search="{e(label.lower())}"><rect x="{x:.1f}" y="{L2_TOP - 64}" width="{w:.1f}" height="50" rx="9" fill="#f6fef9" stroke="#12b76a" stroke-width="1.4"/>'
                 f'<text x="{x + 10:.1f}" y="{L2_TOP - 44}" font-size="10.5" fill="#667085">來自 L1</text>'
                 f'<text x="{x + 10:.1f}" y="{L2_TOP - 26}" font-size="12.5" font-weight="700" fill="#101828">{e(label)}</text></g>')
chip("m_ws", col_pos(1), MW, "轉換工作區")
chip("m_exec", col_pos(2), MW, "轉換執行")
chip("m_split", col_pos(4) - MGAP, MW + MGAP, "視覺分割")
chip("m_hist", col_pos(5), MW, "歷史・歷史清單")
chip("m_set", col_pos(6), MW, "設定・設定目錄")
chip("m_set", col_pos(7), MW, "設定（Office 引擎）")
L2V0 = L2_TOP - 84
_l2 = o[L2_AT:L2_END]
_c0, _c1 = COMBINED_ONLY[0] - L2_AT, COMBINED_ONLY[1] - L2_AT
_l2 = _l2[:_c0] + _l2[_c1:]
_l2 = _l2[:-1] + STANDALONE_ONLY + _l2[-1:]          # chips go inside the L2 group (before its closing tag)
l2_parts = o[:TITLE_AT] + [G_OPEN] + chips + _l2 + [FOOT_L2, "</g>", SVG_CLOSE]
L2V1 = L2_TOP + L2H + 50
l2_svg = "\n".join(l2_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L2V1 - L2V0}" viewBox="0 {L2V0} {W} {L2V1 - L2V0}"', 1)
with open(os.path.join(svg_dir, "l2_application.svg"), "w", encoding="utf-8", newline="\n") as _svg_fh:
    _svg_fh.write(l2_svg.replace(WHITE_BG, f'<rect x="0" y="{L2V0}" width="{W}" height="{L2V1 - L2V0}" fill="#fff"/>'))
meta_l2 = f"{L2_SERVICES} 組應用服務 · {len(MOD_ORDER)} 個模組 · {_count_registered_use_cases()} 個轉換指令 · 3 個往 L3 的出口"
note_l2 = "上排是應用服務（與 L1 共用畫面狀態同一欄），左下是各模組轉換用例，最底是往 L3 的出口。"
render_page(PAGE_L2, "Clickra Architecture — L2 應用", "Clickra — L2 應用", PAGE_L2, "", meta_l2, note_l2, l2_svg)
print("l2 ok")

# ---------------- standalone L3 export ----------------
L3V0 = L3_TOP - 84
L3V1 = L3_TOP + L3H + 50
l3_parts = o[:TITLE_AT] + [G_OPEN] + L3_CHIPS + o[L3_AT:L3_END] + [FOOT_L3, "</g>", SVG_CLOSE]
l3_svg = "\n".join(l3_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L3V1 - L3V0}" viewBox="0 {L3V0} {W} {L3V1 - L3V0}"', 1)
with open(os.path.join(svg_dir, "l3_infrastructure.svg"), "w", encoding="utf-8", newline="\n") as _svg_fh:
    _svg_fh.write(l3_svg.replace(WHITE_BG, f'<rect x="0" y="{L3V0}" width="{W}" height="{L3V1 - L3V0}" fill="#fff"/>'))
_off_inf_count = len([p for p in AF if p.startswith("Clickra.Core/Modules/Office/Infrastructure/")])
meta_l3 = f"{len(PROC)} 個模組處理器 · 儲存 {len(_st)} 檔 · Office 引擎 {_off_inf_count} 檔 · 6 組橫切支援 · {len(EXT_DEF) + 3} 項外部依賴"
note_l3 = "上排是處理器、儲存與 Office 引擎（與 L2 出口同一欄），中排是橫切支援，最底是外部依賴。"
render_page(PAGE_L3, "Clickra Architecture — L3 基礎", "Clickra — L3 基礎", PAGE_L3, "", meta_l3, note_l3, l3_svg)
print("l3 ok")

# ---------------- standalone L4 export ----------------
L4V0 = L4_TOP - 20
L4V1 = L4_TOP + L4H + 50
l4_parts = o[:TITLE_AT] + [G_OPEN] + o[L4_AT:L4_END] + [FOOT_L4, "</g>", SVG_CLOSE]
l4_svg = "\n".join(l4_parts).replace(f'height="{H}" viewBox="0 0 {W} {H}"', f'height="{L4V1 - L4V0}" viewBox="0 {L4V0} {W} {L4V1 - L4V0}"', 1)
with open(os.path.join(svg_dir, "l4_inside.svg"), "w", encoding="utf-8", newline="\n") as _svg_fh:
    _svg_fh.write(l4_svg.replace(WHITE_BG, f'<rect x="0" y="{L4V0}" width="{W}" height="{L4V1 - L4V0}" fill="#fff"/>'))
meta_l4 = f"src/ 全部 {len(AF)} 個 .cs 檔 · {len(PANEL_INFO)} 張面板 · {len(INNER)} 條面板內引用 · {len(CROSS)} 條跨面板引用（灰虛線替身）"
note_l4 = "src/ 底下每個檔案都在這裡：上方是呼叫者，下方是被用的檔案；灰虛線小方塊是引用到別的面板的檔案，點它會連同本尊一起亮；紅虛線框是互相引用的循環。"
render_page(PAGE_L4, "Clickra Architecture — L4 模組內部", "Clickra — L4 模組內部", PAGE_L4, "", meta_l4, note_l4, l4_svg)
print("l4 ok")


# ---------------- check hand-written names against the code ----------------
# L0-L3 boxes are laid out by hand; every *.cs name they mention must still exist under src/.
_known: set[str] = {posixpath.basename(p) for p in AF}
_stems: set[str] = {b.split(".")[0] for b in _known}
_l03 = html.unescape("\n".join(o[:L4_AT]))
_l03 = re.sub(r"-</text>\s*<text[^>]*>\s*", "", _l03)          # join names wrapped at a suffix word
_texts = " ".join(re.findall(r"<text[^>]*>([^<]*)</text>", _l03))    # visible text only (not data-search attributes)
_named: set[str] = set(re.findall(r"[A-Za-z_][\w.*]*\.cs\b", _texts))
_missing: list[str] = sorted(n for n in _named if n not in _known and not (".*." in n and n.split(".")[0] in _stems)
                  and not n.startswith(("Modules", "Clickra", "src")))
if _missing:
    print("⚠ L0～L3 圖上寫的檔名在 src/ 找不到（改名或刪除了，請更新手寫內容）：", ", ".join(_missing))
else:
    print("names ok")
