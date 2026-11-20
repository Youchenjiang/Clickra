#!/usr/bin/env python3
"""Convert the official feature icons (assets/features/*.svg) into Avalonia vector resources.

Output: src/Clickra.Desktop/Assets/FeatureIcons.axaml, one DrawingImage per icon with the key
``Feature.<command>`` (for example ``Feature.split-pdf``). The desktop app shows these images in the
action catalog, on the action page and in history labels, so it uses the same icons as the Explorer
context menu without shipping an SVG library (keeps the NativeAOT exe small).

The generated file is committed. Run this again whenever an SVG in assets/features changes:

    python scripts/generate_desktop_feature_icons.py

Supported SVG subset (everything the feature icons use): rect (rx), circle, line, path, text,
fill / fill-opacity / stroke / stroke-width / stroke-linecap / stroke-linejoin / stroke-dasharray.
<text> is turned into outlines with fontTools (pip install fonttools) using the bundled Noto Sans TC.
"""
from __future__ import annotations

import sys
try:
    import defusedxml.ElementTree as ET  # type: ignore
except ImportError:
    import xml.etree.ElementTree as ET  # skipcq: PTC-W0017, BAN-B314, BAN-B405
from pathlib import Path
from xml.sax.saxutils import quoteattr

REPO = Path(__file__).resolve().parent.parent
FEATURES = REPO / "assets" / "features"
OUTPUT = REPO / "src" / "Clickra.Desktop" / "Assets" / "FeatureIcons.axaml"
FONT = REPO / "src" / "Clickra.Desktop" / "Assets" / "Fonts" / "NotoSansTC-SemiBold.ttf"
SVG_NS = "{http://www.w3.org/2000/svg}"


def num(value: str | None, default: float = 0.0) -> float:
    return float(value) if value is not None and value != "" else default


def fmt(value: float) -> str:
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return text if text not in ("", "-0") else "0"


def color(value: str | None, opacity: str | None = None) -> str | None:
    if value is None or value == "none":
        return None
    value = value.strip()
    if not value.startswith("#"):
        named = {"white": "#FFFFFF", "black": "#000000"}
        value = named.get(value.lower(), value)
    if len(value) == 4:  # #RGB
        value = "#" + "".join(c * 2 for c in value[1:])
    if opacity:
        opacity_str = opacity.strip()
        if opacity_str not in ("", "1"):
            alpha = max(0, min(255, round(float(opacity_str) * 255)))
            value = f"#{alpha:02X}{value[1:]}"
    return value.upper()


def rounded_rect(x: float, y: float, w: float, h: float, r: float) -> str:
    r = min(r, w / 2, h / 2)
    if r <= 0:
        return f"M{fmt(x)},{fmt(y)} H{fmt(x + w)} V{fmt(y + h)} H{fmt(x)} Z"
    return (
        f"M{fmt(x + r)},{fmt(y)} H{fmt(x + w - r)} A{fmt(r)},{fmt(r)} 0 0 1 {fmt(x + w)},{fmt(y + r)} "
        f"V{fmt(y + h - r)} A{fmt(r)},{fmt(r)} 0 0 1 {fmt(x + w - r)},{fmt(y + h)} "
        f"H{fmt(x + r)} A{fmt(r)},{fmt(r)} 0 0 1 {fmt(x)},{fmt(y + h - r)} "
        f"V{fmt(y + r)} A{fmt(r)},{fmt(r)} 0 0 1 {fmt(x + r)},{fmt(y)} Z"
    )


def circle(cx: float, cy: float, r: float) -> str:
    return (
        f"M{fmt(cx - r)},{fmt(cy)} A{fmt(r)},{fmt(r)} 0 1 0 {fmt(cx + r)},{fmt(cy)} "
        f"A{fmt(r)},{fmt(r)} 0 1 0 {fmt(cx - r)},{fmt(cy)} Z"
    )


_font_cache: dict[str, object] = {}


def get_font():
    if "font" not in _font_cache:
        from fontTools.ttLib import TTFont
        _font_cache["font"] = TTFont(str(FONT))
    return _font_cache["font"]


def text_outline(el: ET.Element) -> str:
    """Outlines of a <text> element (Latin labels such as PNG / HEIC), positioned like the SVG."""
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen

    font = get_font()
    glyph_set = font.getGlyphSet()
    cmap = font.getBestCmap()
    units = font["head"].unitsPerEm
    size = num(el.get("font-size"), 16)
    spacing = num(el.get("letter-spacing"))
    scale = size / units
    text = "".join(el.itertext())

    names = [cmap.get(ord(ch), ".notdef") for ch in text]
    advances = [glyph_set[n].width * scale for n in names]
    total = sum(advances) + spacing * max(0, len(text) - 1)
    x = num(el.get("x"))
    anchor = el.get("text-anchor", "start")
    if anchor == "middle":
        x -= total / 2
    elif anchor == "end":
        x -= total
    y = num(el.get("y"))

    parts = []
    for name, advance in zip(names, advances):
        pen = SVGPathPen(glyph_set, ntos=fmt)
        glyph_set[name].draw(TransformPen(pen, (scale, 0, 0, -scale, x, y)))
        commands = pen.getCommands()
        if commands:
            parts.append(commands)
        x += advance + spacing
    return " ".join(parts)


def drawing(el: ET.Element) -> str | None:
    tag = el.tag.replace(SVG_NS, "")
    if tag == "rect":
        geometry = rounded_rect(num(el.get("x")), num(el.get("y")), num(el.get("width")), num(el.get("height")),
                                num(el.get("rx"), num(el.get("ry"))))
    elif tag == "circle":
        geometry = circle(num(el.get("cx")), num(el.get("cy")), num(el.get("r")))
    elif tag == "line":
        geometry = f"M{fmt(num(el.get('x1')))},{fmt(num(el.get('y1')))} L{fmt(num(el.get('x2')))},{fmt(num(el.get('y2')))}"
    elif tag == "path":
        geometry = " ".join(el.get("d", "").split())
    elif tag == "text":
        geometry = text_outline(el)
    else:
        return None

    # SVG default fill is black; lines have no area, so only shapes that can be filled get a brush.
    fill = color(el.get("fill", "#000000" if tag != "line" else "none"), el.get("fill-opacity"))
    stroke = color(el.get("stroke"), el.get("stroke-opacity"))
    attrs = [f"Geometry={quoteattr(geometry)}"]
    if fill:
        attrs.insert(0, f'Brush="{fill}"')
    if not stroke:
        return f"        <GeometryDrawing {' '.join(attrs)} />"

    width = num(el.get("stroke-width"), 1)
    cap = {"round": "Round", "square": "Square"}.get(el.get("stroke-linecap", ""), "Flat")
    join = {"round": "Round", "bevel": "Bevel"}.get(el.get("stroke-linejoin", ""), "Miter")
    pen = f'<Pen Brush="{stroke}" Thickness="{fmt(width)}" LineCap="{cap}" LineJoin="{join}"'
    dash = el.get("stroke-dasharray")
    if dash:
        # Avalonia dash lengths are multiples of the pen thickness.
        dashes = ",".join(fmt(float(d) / width) for d in dash.replace(",", " ").split())
        pen += f'>\n                <Pen.DashStyle><DashStyle Dashes="{dashes}" /></Pen.DashStyle>\n            </Pen>'
    else:
        pen += " />"
    return (
        f"        <GeometryDrawing {' '.join(attrs)}>\n"
        f"            <GeometryDrawing.Pen>\n                {pen}\n            </GeometryDrawing.Pen>\n"
        f"        </GeometryDrawing>"
    )


def convert(svg: Path) -> str:
    root = ET.parse(svg).getroot()  # skipcq: PTC-W0017, BAN-B314
    view = (root.get("viewBox") or f"0 0 {root.get('width', '256')} {root.get('height', '256')}").split()
    w, h = float(view[2]), float(view[3])
    items = [d for el in root if (d := drawing(el))]
    key = f"Feature.{svg.stem}"
    # A transparent frame keeps the full canvas, so every icon scales the same way.
    frame = f'        <GeometryDrawing Brush="#00000000" Geometry="M0,0 H{fmt(w)} V{fmt(h)} H0 Z" />'
    body = "\n".join([frame, *items])
    return (
        f'<DrawingImage x:Key="{key}">\n'
        f"    <DrawingGroup>\n{body}\n    </DrawingGroup>\n"
        f"</DrawingImage>"
    )


def main() -> int:
    svgs = sorted(FEATURES.glob("*.svg"))
    if not svgs:
        print(f"No SVG files in {FEATURES}", file=sys.stderr)
        return 1
    blocks = "\n\n".join(convert(svg) for svg in svgs)
    content = (
        "<!-- Generated by scripts/generate_desktop_feature_icons.py from assets/features/*.svg. Do not edit;\n"
        "     change the SVG and run the script again. Keys: Feature.<command>. -->\n"
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
        + "\n".join("    " + line if line else line for line in blocks.splitlines())
        + "\n</ResourceDictionary>\n"
    )
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(content, encoding="utf-8", newline="\n")
    print(f"Wrote {len(svgs)} icons to {OUTPUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
