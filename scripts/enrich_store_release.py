"""Attach a verified Microsoft Store package to an existing GitHub Release.

This helper is intentionally reconciliation-oriented and idempotent. It never
replaces an existing same-name asset with different bytes.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import subprocess


STORE_SECTION_MARKER = "<!-- clickra-store-package -->"
STORE_SECTION_HEADING = "### Microsoft Store package"


def required_environment_value(env: dict[str, str], name: str) -> str:
    value = next((value for key, value in env.items() if key.casefold() == name.casefold()), None)
    if not value:
        raise RuntimeError(
            f"Cannot construct an isolated Windows PowerShell module path; missing environment variable: {name}"
        )
    return value


def command_environment(args: list[str]) -> dict[str, str] | None:
    executable = pathlib.Path(args[0]).name.casefold()
    if os.name != "nt" or executable not in {"powershell", "powershell.exe"}:
        return None

    env = os.environ.copy()
    user_profile = required_environment_value(env, "USERPROFILE")
    program_files = required_environment_value(env, "ProgramFiles")
    system_root = required_environment_value(env, "SystemRoot")

    module_paths = [
        pathlib.Path(user_profile) / "Documents" / "WindowsPowerShell" / "Modules",
        pathlib.Path(program_files) / "WindowsPowerShell" / "Modules",
        pathlib.Path(system_root) / "System32" / "WindowsPowerShell" / "v1.0" / "Modules",
    ]
    env["PSModulePath"] = os.pathsep.join(str(path) for path in module_paths)
    return env


def run_command(args: list[str], *, capture: bool = True) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            args,
            check=True,
            text=True,
            capture_output=capture,
            env=command_environment(args),
        )
    except subprocess.CalledProcessError as error:
        stdout = (error.stdout or "").strip()
        stderr = (error.stderr or "").strip()
        details = "\n".join(part for part in (stdout, stderr) if part)
        if details:
            raise RuntimeError(
                f"Command failed with exit code {error.returncode}: {args[0]}\n{details}"
            ) from error
        raise RuntimeError(
            f"Command failed with exit code {error.returncode}: {args[0]}"
        ) from error


def parse_release_version(tag: str) -> str:
    if not tag.startswith("v") or len(tag) == 1:
        raise ValueError(f"Expected release tag in v<version> form, got '{tag}'.")
    version = tag[1:]
    parts = version.split(".")
    if len(parts) != 4 or any(not part.isdigit() for part in parts):
        raise ValueError(f"Expected four-part numeric release version, got '{version}'.")
    return version


DEFAULT_PRODUCT_ID = "9NGLBF6P1KLD"
DEFAULT_IDENTITY = "g1014308.Clickra"


def acquire_store_package(
    destination: pathlib.Path,
    *,
    version: str,
    product_id: str,
) -> dict[str, object]:
    evidence_path = destination.with_suffix(".acquisition.json")
    command = [
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(pathlib.Path(__file__).with_name("acquire_store_package.ps1")),
        "-ProductId",
        product_id,
        "-ExpectedIdentity",
        DEFAULT_IDENTITY,
        "-ExpectedVersion",
        version,
        "-OutputPath",
        str(destination),
        "-EvidencePath",
        str(evidence_path),
    ]
    run_command(command)
    if not evidence_path.is_file():
        raise RuntimeError("Store acquisition did not produce its JSON evidence file.")
    evidence = json.loads(evidence_path.read_text(encoding="utf-8-sig"))
    if evidence.get("ProductId") != product_id:
        raise RuntimeError("Store acquisition returned an unexpected ProductId.")
    if evidence.get("Identity") != DEFAULT_IDENTITY:
        raise RuntimeError("Store acquisition returned an unexpected package identity.")
    if evidence.get("Version") != version:
        raise RuntimeError("Store acquisition returned an unexpected package version.")
    evidence_path.unlink()
    return evidence


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_store_package(
    path: pathlib.Path,
    version: str,
    acquisition: dict[str, object],
) -> dict[str, object]:
    source_file_name = acquisition.get("SourceFileName")
    expected_sha1 = acquisition.get("SHA1")
    if not isinstance(source_file_name, str) or not source_file_name:
        raise RuntimeError("Store acquisition evidence is missing SourceFileName.")
    if not isinstance(expected_sha1, str) or not expected_sha1:
        raise RuntimeError("Store acquisition evidence is missing SHA1.")
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
        source_file_name,
        "-ExpectedSha1",
        expected_sha1,
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
        "It was acquired through Microsoft Store catalog and delivery endpoints and verified against the Store identity, "
        "version, package family, byte hashes, and Microsoft Marketplace signature before attachment.\n\n"
        f"SHA-256: `{sha256}`"
    )


def merge_release_body(body: str, section: str) -> tuple[str, bool]:
    if STORE_SECTION_MARKER in body:
        return body, False
    if not body:
        separator = ""
    elif body.endswith("\n"):
        separator = "\n"
    else:
        separator = "\n\n"
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
    parser.add_argument("--product-id", default=DEFAULT_PRODUCT_ID)
    parser.add_argument("--work-dir", default=".store-release-enrichment")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    version = parse_release_version(args.tag)
    asset_name = "Clickra-store.msix"
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
                    "productId": args.product_id,
                    "acquisition": "Microsoft Store catalog and delivery endpoints",
                    "assetName": asset_name,
                    "releaseUrl": release.get("url"),
                    "mutation": False,
                },
                indent=2,
            )
        )
        return 0

    acquisition = acquire_store_package(
        package_path,
        version=version,
        product_id=args.product_id,
    )
    verification = verify_store_package(package_path, version, acquisition)
    sha256 = sha256_file(package_path)
    acquisition_sha256 = acquisition.get("SHA256")
    if not isinstance(acquisition_sha256, str) or acquisition_sha256.lower() != sha256:
        raise RuntimeError("Acquisition SHA-256 does not match the downloaded package bytes.")
    if str(verification.get("SHA256", "")).lower() != sha256:
        raise RuntimeError("Verifier SHA-256 does not match the downloaded package bytes.")

    update_release(args.repo, args.tag, package_path, asset_name, sha256, release)
    print(json.dumps({"tag": args.tag, "asset": asset_name, "sha256": sha256, "status": "reconciled"}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
