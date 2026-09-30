"""Attach a verified Microsoft Store package to an existing GitHub Release.

This helper is intentionally reconciliation-oriented and idempotent. It never
replaces an existing same-name asset with different bytes.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import subprocess
import sys
import urllib.parse
import urllib.request

import resolve_store_package


STORE_SECTION_MARKER = "<!-- clickra-store-package -->"
STORE_SECTION_HEADING = "### Microsoft Store package"


def run_command(args: list[str], *, capture: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        check=True,
        text=True,
        capture_output=capture,
    )


def parse_release_version(tag: str) -> str:
    if not tag.startswith("v") or len(tag) == 1:
        raise ValueError(f"Expected release tag in v<version> form, got '{tag}'.")
    version = tag[1:]
    parts = version.split(".")
    if len(parts) != 4 or any(not part.isdigit() for part in parts):
        raise ValueError(f"Expected four-part numeric release version, got '{version}'.")
    return version


def select_exact_candidate(version: str, product_id: str) -> resolve_store_package.Candidate:
    response_html = resolve_store_package.query_resolver(product_id, timeout=90)
    candidates = resolve_store_package.parse_candidates(
        response_html,
        identity=resolve_store_package.DEFAULT_IDENTITY,
        version=version,
        architecture=resolve_store_package.DEFAULT_ARCHITECTURE,
    )
    selected = [candidate for candidate in candidates if candidate.selected]
    if len(selected) != 1:
        raise RuntimeError(f"Expected exactly one verified resolver candidate, found {len(selected)}.")
    candidate = selected[0]
    if not candidate.reported_sha1:
        raise RuntimeError("Resolver candidate is missing the published SHA-1 metadata.")
    return candidate


def download_candidate(candidate: resolve_store_package.Candidate, destination: pathlib.Path) -> None:
    request = urllib.request.Request(candidate.url, headers={"User-Agent": "Mozilla/5.0 Clickra-Store-Enrichment/1.0"})
    destination.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(request, timeout=120) as response, destination.open("wb") as output:  # nosec B310 - URL is resolver-filtered to the documented Microsoft delivery domain.
        final_url = urllib.parse.urlparse(response.geturl())
        if not final_url.hostname or not resolve_store_package.is_microsoft_cdn_host(final_url.hostname):
            raise RuntimeError(f"Store download redirected outside the Microsoft delivery domain: {response.geturl()}")
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            output.write(chunk)


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_store_package(path: pathlib.Path, version: str, candidate: resolve_store_package.Candidate) -> dict[str, object]:
    command = [
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(pathlib.Path(__file__).with_name("verify_store_msix.ps1")),
        "-MsixPath",
        str(path),
        "-ExpectedVersion",
        version,
        "-SourceFileName",
        candidate.name,
        "-ExpectedSha1",
        candidate.reported_sha1 or "",
    ]
    result = run_command(command)
    return json.loads(result.stdout)


def get_release(repo: str, tag: str) -> dict[str, object]:
    result = run_command(
        [
            "gh",
            "release",
            "view",
            tag,
            "--repo",
            repo,
            "--json",
            "tagName,body,assets,url",
        ]
    )
    return json.loads(result.stdout)


def find_asset(release: dict[str, object], name: str) -> dict[str, object] | None:
    assets = release.get("assets")
    if not isinstance(assets, list):
        return None
    return next((asset for asset in assets if isinstance(asset, dict) and asset.get("name") == name), None)


def normalize_digest(value: object) -> str | None:
    if not isinstance(value, str) or not value:
        return None
    return value.removeprefix("sha256:").lower()


def ensure_asset_state(release: dict[str, object], asset_name: str, sha256: str) -> str:
    existing = find_asset(release, asset_name)
    if existing is None:
        return "upload"
    existing_digest = normalize_digest(existing.get("digest"))
    if existing_digest == sha256.lower():
        return "present"
    raise RuntimeError(
        f"GitHub Release already contains '{asset_name}' with a different or unavailable digest; refusing replacement."
    )


def build_store_section(asset_name: str, sha256: str) -> str:
    return (
        f"{STORE_SECTION_MARKER}\n"
        f"{STORE_SECTION_HEADING}\n"
        f"The Microsoft Store-published package for this release is attached as `{asset_name}`. "
        "It was resolved from the Retail Store channel and verified against the Store identity, version, package family, "
        "resolver hash metadata, and Microsoft Marketplace signature before attachment.\n\n"
        f"SHA-256: `{sha256}`"
    )


def merge_release_body(body: str, section: str) -> tuple[str, bool]:
    if STORE_SECTION_MARKER in body:
        return body, False
    separator = "\n\n" if body and not body.endswith("\n") else "\n" if body else ""
    return f"{body}{separator}{section}\n", True


def update_release(repo: str, tag: str, package_path: pathlib.Path, asset_name: str, sha256: str, release: dict[str, object]) -> None:
    asset_state = ensure_asset_state(release, asset_name, sha256)
    if asset_state == "upload":
        run_command(
            [
                "gh",
                "release",
                "upload",
                tag,
                str(package_path),
                "--repo",
                repo,
            ],
            capture=False,
        )

    body = release.get("body")
    if not isinstance(body, str):
        body = ""
    new_body, changed = merge_release_body(body, build_store_section(asset_name, sha256))
    if changed:
        notes_path = package_path.parent / "release-notes.md"
        notes_path.write_text(new_body, encoding="utf-8")
        run_command(
            ["gh", "release", "edit", tag, "--repo", repo, "--notes-file", str(notes_path)],
            capture=False,
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--product-id", default=resolve_store_package.DEFAULT_PRODUCT_ID)
    parser.add_argument("--work-dir", default=".store-release-enrichment")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    version = parse_release_version(args.tag)
    candidate = select_exact_candidate(version, args.product_id)
    extension = candidate.extension
    asset_name = f"Clickra-store{extension}"
    work_dir = pathlib.Path(args.work_dir).resolve()
    package_path = work_dir / asset_name

    release = get_release(args.repo, args.tag)
    if release.get("tagName") != args.tag:
        raise RuntimeError(f"Release lookup returned unexpected tag: {release.get('tagName')}")

    if args.dry_run:
        print(
            json.dumps(
                {
                    "tag": args.tag,
                    "version": version,
                    "candidate": candidate.name,
                    "assetName": asset_name,
                    "releaseUrl": release.get("url"),
                    "mutation": False,
                },
                indent=2,
            )
        )
        return 0

    download_candidate(candidate, package_path)
    verification = verify_store_package(package_path, version, candidate)
    sha256 = sha256_file(package_path)
    if str(verification.get("SHA256", "")).lower() != sha256:
        raise RuntimeError("Verifier SHA-256 does not match the downloaded package bytes.")

    update_release(args.repo, args.tag, package_path, asset_name, sha256, release)
    print(json.dumps({"tag": args.tag, "asset": asset_name, "sha256": sha256, "status": "reconciled"}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
