"""Line count of every .cs under src/.

The Roslyn graph has no line counts; writes file_lines.json next to this
script.
"""
import json
import os


def count_lines() -> None:
    """Count lines of all C# source files under src/."""
    here = os.path.dirname(os.path.abspath(__file__))
    src = os.path.join(here, "..", "..", "src")
    out = {}
    for dir_path, _, file_names in os.walk(src):
        norm_dir = dir_path.replace("\\", "/")
        if "/obj" in norm_dir or "/bin" in norm_dir:
            continue
        for file_name in file_names:
            if file_name.endswith(".cs"):
                full_path = os.path.join(dir_path, file_name)
                rel_path = os.path.relpath(full_path, src).replace("\\", "/")
                with open(full_path, "rb") as pf:
                    out[rel_path] = pf.read().count(b"\n") + 1

    output_file = os.path.join(here, "file_lines.json")
    with open(output_file, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(out, fh, indent=0, sort_keys=True)
    print(len(out), "files")


if __name__ == "__main__":
    count_lines()
