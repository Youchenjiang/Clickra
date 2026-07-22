"""Deterministic contracts for rendered layout-occupancy comparison."""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tests" / "PdfRegression"))

from layout_occupancy import compare_region_rows  # noqa: E402


def dense_rows(length: int = 700) -> np.ndarray:
    rows = np.zeros(length, dtype=bool)
    for start in range(10, length - 10, 12):
        rows[start : start + 5] = True
    return rows


def main() -> int:
    failures: list[str] = []
    source = dense_rows()

    normal_reflow = np.roll(source, 2)
    normal_issues, _ = compare_region_rows(source, normal_reflow, 1.0)
    if normal_issues:
        failures.append(f"small whole-column reflow must pass: {normal_issues}")

    large_hole = source.copy()
    large_hole[240:410] = False
    hole_issues, _ = compare_region_rows(source, large_hole, 1.0)
    if not any(issue["kind"] == "new_blank_band" for issue in hole_issues):
        failures.append("a newly empty 170-point band was not detected")

    early_end = source.copy()
    early_end[500:] = False
    tail_issues, _ = compare_region_rows(source, early_end, 1.0)
    if not any(issue["kind"] == "early_column_end" for issue in tail_issues):
        failures.append("a column ending 200 points early was not detected")

    sparse_source = np.zeros(700, dtype=bool)
    sparse_source[40:60] = True
    sparse_source[400:420] = True
    sparse_issues, _ = compare_region_rows(sparse_source, sparse_source.copy(), 1.0)
    if sparse_issues:
        failures.append(f"matching intentionally sparse layouts must pass: {sparse_issues}")

    for failure in failures:
        print(f"FAIL: {failure}")
    if failures:
        return 1
    print("PASS: layout occupancy heuristics")
    return 0


if __name__ == "__main__":
    sys.exit(main())
