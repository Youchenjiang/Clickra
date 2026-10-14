"""Opt-in real-pipeline identity translation smoke for three pinned public PDFs.

Requires the Clickra CLI, but never calls an external translation provider.
Does not replace human-approved translated-PDF layout baselines.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pymupdf

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from fetch_public_pdf_fixtures import SOURCES, fetch  # noqa: E402


def check_output(source: Path, translated: Path) -> None:
    if not translated.is_file():
        raise ValueError(f"translation output missing: {translated}")
    with pymupdf.open(source) as original, pymupdf.open(translated) as result:
        if len(original) != len(result):
            raise ValueError(f"page count changed: {len(original)} -> {len(result)}")
        for page_no, (before, after) in enumerate(zip(original, result), start=1):
            if before.get_text().strip() and not after.get_text().strip():
                raise ValueError(f"page {page_no} lost all extractable text")
            if after.rect.is_empty:
                raise ValueError(f"page {page_no} has an empty page rectangle")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--timeout", type=int, default=240)
    parser.add_argument("--cli", type=Path, help="Compiled Clickra.dll; otherwise run using dotnet run")
    args = parser.parse_args()
    if shutil.which("dotnet") is None:
        raise RuntimeError(".NET SDK is unavailable; public PDF translation E2E not executed")
    project = ROOT / "src" / "Clickra.CLI" / "Clickra.csproj"
    if not args.cli and not project.is_file():
        raise RuntimeError(f"Clickra CLI project unavailable: {project}")
    inputs = ROOT / "test_pdfs" / "downloaded_candidates"
    output_dir = ROOT / "tmp" / "pdfs" / "public_identity"
    output_dir.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    environment["CLICKRA_TRANSLATION_ENGINE"] = "identity"
    for key in SOURCES:
        source = fetch(key, inputs)
        target = output_dir / f"{source.stem}_translated.pdf"
        if target.exists():
            target.unlink()  # Never accept a stale result from a previous run.
        command = (["dotnet", str(args.cli)] if args.cli else
                   ["dotnet", "run", "--project", str(project), "--"])
        command += ["translate-pdf", "--no-ui", "--out-dir", str(output_dir), str(source)]
        result = subprocess.run(command, cwd=ROOT, env=environment,
                                timeout=args.timeout, check=False)
        if result.returncode:
            raise RuntimeError(f"{key}: CLI translation failed with exit {result.returncode}")
        check_output(source, target)
        print(f"PASS {key}: full PDF identity translation pipeline and page integrity")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
