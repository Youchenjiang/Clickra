#!/usr/bin/env python3
"""Fetch three pinned, publicly hosted PDFs for supplementary regression tests.

The historical *_translated.pdf baselines are intentionally NOT fabricated here.
"""
import argparse
import hashlib
import sys
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import Request, urlopen


SOURCES = {
    "2407": ("https://arxiv.org/pdf/2407.11279v1", "2407.11279v1_arxiv.pdf", "514f411e67fa8378d21f076b92363e83a5d2933bb9f3ab03941599cd766f9f35"),
    "pentest": ("https://shensecurity.com/assets/pdf/PentestAgent_AsiaCCS_camera_ready.pdf", "PentestAgent_AsiaCCS_camera_ready.pdf", "ec63c7e004da12f20517bea06c9ee13b5eae585b07572531cc5639227844e432"),
    "togll": ("https://arxiv.org/pdf/2405.03786v1", "TOGLL_arxiv_v1.pdf", "55854a3e45dac8699456ae5ef4d294b6dc771daec74702b3f963a2ebd3144f4c"),
}


def verify(data, expected):
    if not data.startswith(b"%PDF-"):
        raise ValueError("Response is not a PDF")
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected:
        raise ValueError(f"PDF SHA-256 mismatch: got {actual}; expected {expected}")


def fetch(name, destination):
    url, filename, digest = SOURCES[name]
    target = destination / filename
    if target.exists():
        verify(target.read_bytes(), digest)
        print(f"PASS {name}: existing verified fixture {target}")
        return target
    request = Request(url, headers={"User-Agent": "Clickra-PDF-Regression/1.0"})
    with urlopen(request, timeout=45) as response:
        redirect = urlsplit(response.geturl())
        if redirect.scheme != "https" or redirect.hostname not in {"arxiv.org", "www.arxiv.org", "shensecurity.com"}:
            raise ValueError("Unexpected redirect destination")
        data = response.read(25_000_001)
    if len(data) > 25_000_000:
        raise ValueError("PDF exceeds the 25 MB limit")
    verify(data, digest)
    destination.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    print(f"PASS {name}: verified {target} (sha256={digest})")
    return target


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("names", nargs="*", choices=sorted(SOURCES), default=list(SOURCES))
    parser.add_argument("--output", type=Path, default=Path("test_pdfs") / "downloaded_candidates")
    args = parser.parse_args()
    for name in args.names or SOURCES:
        try:
            fetch(name, args.output)
        except (OSError, ValueError) as exc:
            print(f"FAIL {name}: {exc}", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
