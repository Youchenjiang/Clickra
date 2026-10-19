"""Whole-repo L4 model: nodes (partial classes merged), panels, inner edges, cross-panel references."""
import json, os, collections
_d = json.load(open("all.json", encoding="utf-8"))
AF, AE = _d["files"], [tuple(x) for x in _d["edges"]]
_grp = {}
for t, fs in _d["partial"].items():
    for f in fs:
        _grp[f] = os.path.dirname(fs[0]) + "/" + t + ".*.cs"


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
    nd["files"].append(os.path.basename(p)); nd["lines"] += n
NEDGES = sorted({(anode(a), anode(b)) for a, b in AE if anode(a) != anode(b)})
PANEL = {n: panel_of(n) for n in NODES}
INNER = [(a, b) for a, b in NEDGES if PANEL[a] == PANEL[b]]
CROSS = [(a, b) for a, b in NEDGES if PANEL[a] != PANEL[b]]
