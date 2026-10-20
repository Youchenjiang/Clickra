"""Whole-repo L4 model: nodes (partial classes merged), panels, inner edges, cross-panel references."""
import json, os, collections, posixpath   # repo paths always use "/", also when this runs on Windows
# semantic-msbuild.json comes from the repo's Roslyn tool (scripts/architecture/SemanticGraph): every project is
# compiled with MSBuild, and an edge means a file uses a symbol (type or member) declared in another file.
_GRAPH = os.environ.get("CLICKRA_ARCH_GRAPH", "semantic-msbuild.json")   # override only for tests
_g = json.load(open(_GRAPH, encoding="utf-8"))
assert not any(_g["diagnostics"]["projectErrors"].values()), _g["diagnostics"]   # only trust a graph that compiled cleanly
_strip = lambda p: p[4:] if p.startswith("src/") else p
_lines = json.load(open(os.environ.get("CLICKRA_ARCH_LINES", "file_lines.json"), encoding="utf-8"))
AF = {_strip(p): _lines[_strip(p)] for p in _g["files"]}
AE = sorted({(_strip(e["source"]), _strip(e["target"])) for e in _g["edges"] if e["source"] != e["target"]})
ASYM = {(_strip(e["source"]), _strip(e["target"])): e["symbols"] for e in _g["edges"]}
# partial classes: Name.cs (or Name.xaml.cs) plus Name.*.cs in the same folder, checked against the expected set below
_by = collections.defaultdict(list)
for p in AF:
    _by[(posixpath.dirname(p), posixpath.basename(p).split(".")[0])].append(p)
_grp = {}
for (d, stem), fs in _by.items():
    if len(fs) > 1 and any(posixpath.basename(f) in (stem + ".cs", stem + ".xaml.cs") for f in fs):
        for f in fs:
            _grp[f] = d + "/" + stem + ".*.cs"
PARTIAL_GROUPS = sorted(set(_grp.values()))


def anode(p):
    return _grp.get(p, p)


TR = "Clickra.Core/Modules/PdfTranslation/"
TR_SMALL = {"Application", "Execution", "Pipeline/", "Pipeline/Layout", "Pipeline/Fonts"}


def tr_folder(n):
    r = n[len(TR):].split("/")
    if r[0] == "Pipeline":
        return "Pipeline/" + (r[1] if len(r) > 2 else "")
    return r[0]


def panel_of(n):
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


NODES = collections.defaultdict(lambda: {"files": [], "lines": 0})
for p, n in AF.items():
    nd = NODES[anode(p)]
    nd["files"].append(posixpath.basename(p)); nd["lines"] += n
NEDGES = sorted({(anode(a), anode(b)) for a, b in AE if anode(a) != anode(b)})
PANEL = {n: panel_of(n) for n in NODES}
INNER = [(a, b) for a, b in NEDGES if PANEL[a] == PANEL[b]]
CROSS = [(a, b) for a, b in NEDGES if PANEL[a] != PANEL[b]]
