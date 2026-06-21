"""Opt-in end-to-end PDF translation test.

Identity mode exercises analysis, classification, masking, reconstruction and
save without network variability. Real mode additionally exercises the live
provider router and therefore is not part of the default offline gate.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import fitz

from pdf_test_common import ROOT, SOURCE_DIR, SOURCE_MAP, TMP_DIR


ANCHORS = ("TV_SHORT", "UNKNOWN_SOURCE", "VIDEO_GAME", "Jaccard")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--engine", choices=("identity", "real"), default="identity")
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args()

    cli = ROOT / "src" / "Clickra.CLI" / "bin" / "Debug" / "net10.0" / "Clickra.dll"
    source = SOURCE_DIR / SOURCE_MAP["final"]
    run_dir = TMP_DIR / f"e2e_{args.engine}"
    input_pdf = run_dir / source.name
    output_pdf = run_dir / f"{source.stem}_translated.pdf"

    failures: list[str] = []
    for path, label in ((cli, "compiled CLI"), (source, "source PDF")):
        if not path.is_file():
            failures.append(f"missing {label}: {path}")
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    if run_dir.exists():
        shutil.rmtree(run_dir)
    run_dir.mkdir(parents=True)
    shutil.copy2(source, input_pdf)

    env = os.environ.copy()
    if args.engine == "identity":
        env["CLICKRA_TRANSLATION_ENGINE"] = "identity"
    else:
        env.pop("CLICKRA_TRANSLATION_ENGINE", None)

    started = time.monotonic()
    try:
        completed = subprocess.run(
            ["dotnet", str(cli), "translate-pdf", "--no-ui", str(input_pdf)],
            cwd=ROOT,
            env=env,
            timeout=args.timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print(f"FAIL: {args.engine} E2E exceeded {args.timeout}s")
        return 1

    elapsed = time.monotonic() - started
    if completed.returncode:
        failures.append(f"CLI exited with {completed.returncode}")
    if not output_pdf.is_file():
        failures.append(f"translated PDF not created: {output_pdf}")

    if not failures:
        with fitz.open(source) as src_doc, fitz.open(output_pdf) as out_doc:
            if len(src_doc) != len(out_doc):
                failures.append(f"page count changed {len(src_doc)} -> {len(out_doc)}")

            output_text = "\n".join(page.get_text() for page in out_doc)
            for anchor in ANCHORS:
                if anchor not in output_text:
                    failures.append(f"bypassed appendix anchor missing: {anchor}")

            for page_no in (15, 16):
                page = out_doc[page_no - 1]
                pix = page.get_pixmap(matrix=fitz.Matrix(1, 1), colorspace=fitz.csGRAY)
                dark = sum(1 for value in pix.samples if value < 245)
                if dark / max(1, len(pix.samples)) < 0.01:
                    failures.append(f"p{page_no} rendered suspiciously blank")

            if args.engine == "real":
                body_text = "\n".join(out_doc[p - 1].get_text() for p in (1, 3, 7))
                if not any("\u4e00" <= char <= "\u9fff" for char in body_text):
                    failures.append("real translation produced no CJK text on sampled body pages")

    for failure in failures:
        print(f"FAIL: {failure}")
    if failures:
        return 1

    print(
        f"PASS: {args.engine} translation E2E completed in {elapsed:.1f}s; "
        f"output={output_pdf}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

