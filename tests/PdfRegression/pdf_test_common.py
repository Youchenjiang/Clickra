"""Shared paths and fixtures for Clickra PDF translation tests."""
from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TEST_PDFS = ROOT / "test_pdfs"
SOURCE_DIR = TEST_PDFS / "source"
TRANSLATED_DIR = TEST_PDFS / "translated"
DIAGNOSTIC_DIR = TEST_PDFS / "diagnostic"
TMP_DIR = ROOT / "tmp" / "pdfs"

SOURCE_MAP = {
    "2407": "2407.11279v1_clean.pdf",
    "pentest": "PentestAgent_Agent Pentest.pdf",
    "togll": "TOGLL_Oracle Generation.pdf",
    "final": "114423046_final_project.pdf",
}

TRANSLATED_MAP = {
    key: f"{Path(name).stem}_translated.pdf"
    for key, name in SOURCE_MAP.items()
}


def require_file(path: Path, failures: list[str], label: str) -> bool:
    if path.is_file():
        return True
    failures.append(f"{label}: missing file: {path}")
    return False

