#!/usr/bin/env python3
"""Fetch three pinned, publicly hosted PDFs for supplementary regression tests.

The historical *_translated.pdf baselines are intentionally NOT fabricated here.
"""
import argparse
import hashlib
import sys
from pathlib import Path
from http.client import HTTPSConnection
from urllib.parse import urljoin, urlsplit


SOURCES = {
    "2407": ("https://arxiv.org/pdf/2407.11279v1", "2407.11279v1_arxiv.pdf", "514f411e67fa8378d21f076b92363e83a5d2933bb9f3ab03941599cd766f9f35"),
    "pentest": ("https://shensecurity.com/assets/pdf/PentestAgent_AsiaCCS_camera_ready.pdf", "PentestAgent_AsiaCCS_camera_ready.pdf", "ec63c7e004da12f20517bea06c9ee13b5eae585b07572531cc5639227844e432"),
    "togll": ("https://arxiv.org/pdf/2405.03786v1", "TOGLL_arxiv_v1.pdf", "55854a3e45dac8699456ae5ef4d294b6dc771daec74702b3f963a2ebd3144f4c"),
}


ALLOWED_HOSTS = {"arxiv.org", "www.arxiv.org", "shensecurity.com"}
MAX_BYTES = 25_000_000


def download_pinned_pdf(url):
    """Allow HTTPS only, including at most three approved-host redirects."""
    for _ in range(4):
        parsed = urlsplit(url)
        if (parsed.scheme != "https" or parsed.hostname not in ALLOWED_HOSTS
                or parsed.port not in (None, 443) or parsed.username or parsed.password):
            raise ValueError("Unexpected PDF URL destination")
        connection = HTTPSConnection(parsed.hostname, port=443, timeout=45)
        try:
            path = parsed.path or "/"
            if parsed.query:
                path += "?" + parsed.query
            connection.request("GET", path, headers={"User-Agent": "Clickra-PDF-Regression/1.0"})
            response = connection.getresponse()
            if response.status in (301, 302, 303, 307, 308):
                location = response.getheader("Location")
                if not location:
                    raise ValueError("PDF redirect missing location")
                url = urljoin(url, location)
                continue
            if response.status != 200:
                raise ValueError(f"PDF HTTP response {response.status}")
            return response.read(MAX_BYTES + 1)
        finally:
            connection.close()
    raise ValueError("Too many PDF redirects")


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
    data = download_pinned_pdf(url)
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
