"""Line count of every .cs under src/ (the Roslyn graph has no line counts); writes file_lines.json next to this script."""
import os, json
here = os.path.dirname(os.path.abspath(__file__))
src = os.path.join(here, "..", "..", "src")
out = {}
for dp, _, fs in os.walk(src):
    d = dp.replace("\\", "/")
    if "/obj" in d or "/bin" in d:
        continue
    for f in fs:
        if f.endswith(".cs"):
            p = os.path.join(dp, f)
            with open(p, "rb") as pf:
                out[os.path.relpath(p, src).replace("\\", "/")] = pf.read().count(b"\n") + 1
with open(os.path.join(here, "file_lines.json"), "w", encoding="utf-8", newline="\n") as fh:
    json.dump(out, fh, indent=0, sort_keys=True)
print(len(out), "files")
