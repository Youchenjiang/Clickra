#!/usr/bin/env python3
"""Opt-in download of published PDF candidates, never assumed baseline-equivalent."""
import argparse
import hashlib
from pathlib import Path
from urllib.request import Request, urlopen


SOURCES = {
    "2407": ("https://arxiv.org/pdf/2407.11279v1", "2407.11279v1_arxiv.pdf"),
    "pentest": ("https://shensecurity.com/assets/pdf/PentestAgent_AsiaCCS_camera_ready.pdf", "PentestAgent_AsiaCCS_camera_ready.pdf"),
    "togll": ("https://arxiv.org/pdf/2405.03786v1", "TOGLL_arxiv_v1.pdf"),
}


def fetch(name, destination):
    url, filename = SOURCES[name]
    target = destination / filename
    if target.exists():
        raise FileExistsError(f"Refusing to overwrite existing candidate: {target}")
    request = Request(url, headers={"User-Agent": "ClickraFixtureResearch/1.0 (small opt-in test download)"})
    with urlopen(request, timeout=35) as response:
        if response.geturl().split("/")[2] not in {"arxiv.org", "shensecurity.com", "www.arxiv.org"}:
            raise ValueError("Unexpected redirect host")
        data = response.read(25_000_001)
    if len(data) > 25_000_000 or not data.startswith(b"%PDF-"):
        raise ValueError("Download is too large or is not a PDF")
    destination.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    print(f"{name}: {target}; sha256={hashlib.sha256(data).hexdigest()}")
    print("Candidate only: do not rename as an existing regression baseline without comparison.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("names", nargs="+", choices=sorted(SOURCES))
    parser.add_argument("--output", type=Path, default=Path("test_pdfs") / "downloaded_candidates")
    args = parser.parse_args()
    for name in args.names:
        fetch(name, args.output)


if __name__ == "__main__":
    main()
