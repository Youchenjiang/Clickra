"""Resolve Clickra Microsoft Store package candidates without downloading them.

The resolver is discovery-only. Its response is not trusted as proof of package
identity or signature validity; later phases must verify downloaded bytes.
"""

from __future__ import annotations

import argparse
import http.cookiejar
import html.parser
import json
import re
import sys
import urllib.parse
import urllib.request
from dataclasses import asdict, dataclass


RESOLVER_URL = "https://store.rg-adguard.net/api/GetFiles"
DEFAULT_PRODUCT_ID = "9NGLBF6P1KLD"
DEFAULT_IDENTITY = "g1014308.Clickra"
DEFAULT_ARCHITECTURE = "neutral"
MICROSOFT_CDN_SUFFIX = ".delivery.mp.microsoft.com"
PACKAGE_EXTENSIONS = (".msix", ".msixbundle", ".appx", ".appxbundle")


@dataclass(frozen=True)
class Candidate:
    name: str
    url: str
    host: str
    https_transport: bool
    allowed_store_transport: bool
    extension: str
    reported_sha1: str | None
    reported_size: str | None
    identity: str | None
    version: str | None
    architecture: str | None
    microsoft_cdn: bool
    exact_identity: bool
    exact_version: bool
    intended_type: bool
    intended_architecture: bool
    selected: bool


class LinkParser(html.parser.HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self._href: str | None = None
        self._text: list[str] = []
        self.links: list[tuple[str, str]] = []
        self._in_row = False
        self._in_cell = False
        self._row_cells: list[str] = []
        self._cell_text: list[str] = []
        self._row_href: str | None = None
        self.rows: list[tuple[str, str, str | None, str | None]] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag.lower() == "tr":
            self._in_row = True
            self._row_cells = []
            self._row_href = None
        elif tag.lower() == "td" and self._in_row:
            self._in_cell = True
            self._cell_text = []
        if tag.lower() != "a":
            return
        self._href = dict(attrs).get("href")
        if self._in_row:
            self._row_href = self._href
        self._text = []

    def handle_data(self, data: str) -> None:
        if self._href is not None:
            self._text.append(data)
        if self._in_cell:
            self._cell_text.append(data)

    def handle_endtag(self, tag: str) -> None:
        lowered = tag.lower()
        if lowered == "td" and self._in_cell:
            self._row_cells.append("".join(self._cell_text).strip())
            self._in_cell = False
            self._cell_text = []
        if lowered == "tr" and self._in_row:
            if self._row_href and self._row_cells:
                name = self._row_cells[0]
                sha1 = self._row_cells[2] if len(self._row_cells) > 2 else None
                size = self._row_cells[3] if len(self._row_cells) > 3 else None
                self.rows.append((self._row_href, name, sha1, size))
            self._in_row = False
            self._row_cells = []
            self._row_href = None
        if lowered != "a" or self._href is None:
            return
        self.links.append((self._href, "".join(self._text).strip()))
        self._href = None
        self._text = []


def is_microsoft_cdn_host(host: str) -> bool:
    normalized = host.lower().rstrip(".")
    return normalized.endswith(MICROSOFT_CDN_SUFFIX)


def parse_package_name(name: str) -> tuple[str | None, str | None, str | None, str]:
    lower = name.lower()
    extension = next((ext for ext in PACKAGE_EXTENSIONS if lower.endswith(ext)), "")
    if not extension:
        return None, None, None, ""
    stem = name[: -len(extension)]
    match = re.match(
        r"^(?P<identity>.+)_(?P<version>\d+\.\d+\.\d+\.\d+)_(?P<arch>[^_]+)__[^_]+$",
        stem,
    )
    if not match:
        return None, None, None, extension
    return (
        match.group("identity"),
        match.group("version"),
        match.group("arch"),
        extension,
    )


def parse_candidates(
    html: str,
    *,
    identity: str,
    version: str,
    architecture: str,
) -> list[Candidate]:
    parser = LinkParser()
    parser.feed(html)
    metadata = {url: (name, sha1, size) for url, name, sha1, size in parser.rows}
    candidates: list[Candidate] = []
    for url, name in parser.links:
        parsed_url = urllib.parse.urlparse(url)
        if parsed_url.scheme.lower() not in ("http", "https") or not parsed_url.hostname:
            continue
        parsed_identity, parsed_version, parsed_architecture, extension = parse_package_name(name)
        if not extension:
            continue
        microsoft_cdn = is_microsoft_cdn_host(parsed_url.hostname)
        https_transport = parsed_url.scheme.lower() == "https"
        allowed_store_transport = microsoft_cdn and parsed_url.scheme.lower() in ("http", "https")
        exact_identity = parsed_identity == identity
        exact_version = parsed_version == version
        intended_type = extension in (".msix", ".msixbundle")
        intended_architecture = parsed_architecture == architecture
        selected = all(
            (
                allowed_store_transport,
                microsoft_cdn,
                exact_identity,
                exact_version,
                intended_type,
                intended_architecture,
            )
        )
        candidates.append(
            Candidate(
                name=name,
                url=url,
                host=parsed_url.hostname,
                https_transport=https_transport,
                allowed_store_transport=allowed_store_transport,
                extension=extension,
                reported_sha1=(metadata.get(url) or (None, None, None))[1],
                reported_size=(metadata.get(url) or (None, None, None))[2],
                identity=parsed_identity,
                version=parsed_version,
                architecture=parsed_architecture,
                microsoft_cdn=microsoft_cdn,
                exact_identity=exact_identity,
                exact_version=exact_version,
                intended_type=intended_type,
                intended_architecture=intended_architecture,
                selected=selected,
            )
        )
    return candidates


def query_resolver(product_id: str, timeout: int) -> str:
    cookie_jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookie_jar))
    common_headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36",
        "Referer": "https://store.rg-adguard.net/",
    }
    seed_request = urllib.request.Request(
        "https://store.rg-adguard.net/",
        headers=common_headers,
        method="GET",
    )
    with opener.open(seed_request, timeout=timeout) as response:  # nosec B310 - fixed HTTPS endpoint
        if response.status != 200:
            raise RuntimeError(f"Resolver preflight returned HTTP {response.status}.")
        response.read()

    form = urllib.parse.urlencode(
        {"type": "ProductId", "url": product_id, "ring": "Retail", "lang": "en-US"}
    ).encode("ascii")
    request = urllib.request.Request(
        RESOLVER_URL,
        data=form,
        headers={
            **common_headers,
            "Content-Type": "application/x-www-form-urlencoded",
            "Origin": "https://store.rg-adguard.net",
        },
        method="POST",
    )
    with opener.open(request, timeout=timeout) as response:  # nosec B310 - fixed HTTPS endpoint
        if response.status != 200:
            raise RuntimeError(f"Resolver returned HTTP {response.status}.")
        html = response.read().decode("utf-8", errors="strict")
    if "delivery.mp.microsoft.com" not in html:
        raise RuntimeError("Resolver response did not contain Microsoft Store CDN links.")
    return html


def run_self_test() -> None:
    fixture = """
    <table>
      <tr><td><a href="http://tlu.dl.delivery.mp.microsoft.com/a">g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.msix</a></td><td>2099-01-01</td><td>ABCDEF</td><td>15 MB</td></tr>
      <tr><td><a href="https://dl.delivery.mp.microsoft.com/b">g1014308.Clickra_3.12.0.0_neutral__mgcm3zc7fc0ty.msix</a></td></tr>
      <tr><td><a href="https://evil.example/c">g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.msix</a></td></tr>
      <tr><td><a href="https://dl.delivery.mp.microsoft.com/d">Microsoft.VCLibs_14.0.0.0_x64__8wekyb3d8bbwe.appx</a></td></tr>
    </table>
    """
    candidates = parse_candidates(
        fixture,
        identity=DEFAULT_IDENTITY,
        version="3.11.0.0",
        architecture=DEFAULT_ARCHITECTURE,
    )
    selected = [candidate for candidate in candidates if candidate.selected]
    assert len(candidates) == 4
    assert len(selected) == 1
    assert selected[0].name.endswith(".msix")
    assert selected[0].microsoft_cdn
    assert selected[0].allowed_store_transport
    assert not selected[0].https_transport
    assert selected[0].reported_sha1 == "ABCDEF"
    assert selected[0].reported_size == "15 MB"
    assert not candidates[1].exact_version
    assert not candidates[2].microsoft_cdn
    assert not candidates[3].exact_identity


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-id", default=DEFAULT_PRODUCT_ID)
    parser.add_argument("--identity", default=DEFAULT_IDENTITY)
    parser.add_argument(
        "--version",
        required="--self-test" not in sys.argv,
        help="Exact Store package version to match, for example 3.11.0.0.",
    )
    parser.add_argument("--architecture", default=DEFAULT_ARCHITECTURE)
    parser.add_argument("--timeout", type=int, default=90)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        run_self_test()
        print("PASS: Store resolver parser self-test")
        return 0

    html = query_resolver(args.product_id, args.timeout)
    candidates = parse_candidates(
        html,
        identity=args.identity,
        version=args.version,
        architecture=args.architecture,
    )
    selected = [candidate for candidate in candidates if candidate.selected]
    result = {
        "resolver": RESOLVER_URL,
        "productId": args.product_id,
        "ring": "Retail",
        "expected": {
            "identity": args.identity,
            "version": args.version,
            "architecture": args.architecture,
            "packageTypes": [".msix", ".msixbundle"],
            "microsoftCdnSuffix": MICROSOFT_CDN_SUFFIX,
        },
        "candidateCount": len(candidates),
        "selectedCount": len(selected),
        "candidates": [asdict(candidate) for candidate in candidates],
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if len(selected) != 1:
        print(
            f"ERROR: expected exactly one fail-closed Store candidate, found {len(selected)}.",
            file=sys.stderr,
        )
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
