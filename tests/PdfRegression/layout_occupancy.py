"""Rendered source/translation occupancy comparison for PDF layout regressions.

The comparison intentionally ignores glyph identity. It renders both pages and
compares where visible ink exists inside each academic-paper column, which can
detect large holes without treating every translated character as a pixel diff.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass
from pathlib import Path

import fitz
import numpy as np
from PIL import Image


@dataclass(frozen=True)
class OccupancyThresholds:
    minimum_new_gap_points: float = 40.0
    minimum_source_occupancy_in_gap: float = 0.16
    minimum_large_gap_points: float = 24.0
    maximum_added_large_gap_points: float = 72.0
    maximum_large_gap_growth_ratio: float = 1.30
    minimum_source_occupied_ratio: float = 0.12
    maximum_occupied_ratio_retention: float = 0.62
    minimum_occupied_ratio_drop: float = 0.07
    minimum_tail_gap_points: float = 65.0
    maximum_added_tail_gap_points: float = 45.0


DEFAULT_THRESHOLDS = OccupancyThresholds()


def _dilate_rows(rows: np.ndarray, radius: int = 2) -> np.ndarray:
    if radius <= 0 or not rows.any():
        return rows.copy()
    kernel = np.ones(radius * 2 + 1, dtype=np.int16)
    return np.convolve(rows.astype(np.int16), kernel, mode="same") > 0


def _blank_runs(rows: np.ndarray) -> list[tuple[int, int]]:
    runs: list[tuple[int, int]] = []
    start: int | None = None
    for index, occupied in enumerate(rows):
        if not occupied and start is None:
            start = index
        elif occupied and start is not None:
            runs.append((start, index))
            start = None
    if start is not None:
        runs.append((start, len(rows)))
    return runs


def _detect_new_blank_bands(
    source: np.ndarray,
    output_runs: list[tuple[int, int]],
    points_per_row: float,
    thresholds: OccupancyThresholds,
) -> list[dict]:
    issues = []
    for start, end in output_runs:
        length_points = (end - start) * points_per_row
        if length_points < thresholds.minimum_new_gap_points:
            continue
        source_occupancy = float(source[start:end].mean()) if end > start else 0.0
        if source_occupancy >= thresholds.minimum_source_occupancy_in_gap:
            issues.append(
                {
                    "kind": "new_blank_band",
                    "start_points": round(start * points_per_row, 1),
                    "end_points": round(end * points_per_row, 1),
                    "length_points": round(length_points, 1),
                    "source_occupied_ratio": round(source_occupancy, 3),
                }
            )
    return issues



def _large_gap_total(
    runs: list[tuple[int, int]],
    points_per_row: float,
    thresholds: OccupancyThresholds,
) -> float:
    return sum(
        (end - start) * points_per_row
        for start, end in runs
        if (end - start) * points_per_row >= thresholds.minimum_large_gap_points
    )


def _detect_excess_blank_space(
    source_large_gap: float,
    output_large_gap: float,
    thresholds: OccupancyThresholds,
) -> list[dict]:
    added_large_gap = output_large_gap - source_large_gap
    if (
        added_large_gap > thresholds.maximum_added_large_gap_points
        and output_large_gap
        > max(
            thresholds.minimum_large_gap_points,
            source_large_gap * thresholds.maximum_large_gap_growth_ratio,
        )
    ):
        return [
            {
                "kind": "excess_blank_space",
                "source_large_gap_points": round(source_large_gap, 1),
                "output_large_gap_points": round(output_large_gap, 1),
                "added_points": round(added_large_gap, 1),
            }
        ]
    return []


def compare_region_rows(
    source_rows: np.ndarray,
    output_rows: np.ndarray,
    points_per_row: float,
    thresholds: OccupancyThresholds = DEFAULT_THRESHOLDS,
) -> tuple[list[dict], dict]:
    """Compare two boolean row-occupancy profiles for one page column."""
    if source_rows.shape != output_rows.shape:
        raise ValueError("Source and output occupancy rows must have equal shapes.")

    source = _dilate_rows(source_rows)
    output = _dilate_rows(output_rows)
    source_runs = _blank_runs(source)
    output_runs = _blank_runs(output)

    source_large_gap = _large_gap_total(source_runs, points_per_row, thresholds)
    output_large_gap = _large_gap_total(output_runs, points_per_row, thresholds)

    issues = _detect_new_blank_bands(source, output_runs, points_per_row, thresholds)
    issues.extend(_detect_excess_blank_space(source_large_gap, output_large_gap, thresholds))

    source_ratio = float(source.mean())
    output_ratio = float(output.mean())
    if (
        source_ratio >= thresholds.minimum_source_occupied_ratio
        and source_ratio - output_ratio > thresholds.minimum_occupied_ratio_drop
        and output_ratio < source_ratio * thresholds.maximum_occupied_ratio_retention
    ):
        issues.append(
            {
                "kind": "occupancy_drop",
                "source_occupied_ratio": round(source_ratio, 3),
                "output_occupied_ratio": round(output_ratio, 3),
            }
        )

    def tail_gap(rows: np.ndarray) -> float:
        occupied = np.flatnonzero(rows)
        if len(occupied) == 0:
            return len(rows) * points_per_row
        return (len(rows) - int(occupied[-1]) - 1) * points_per_row

    source_tail = tail_gap(source)
    output_tail = tail_gap(output)
    if (
        output_tail >= thresholds.minimum_tail_gap_points
        and output_tail - source_tail > thresholds.maximum_added_tail_gap_points
    ):
        issues.append(
            {
                "kind": "early_column_end",
                "source_tail_points": round(source_tail, 1),
                "output_tail_points": round(output_tail, 1),
                "added_points": round(output_tail - source_tail, 1),
            }
        )

    metrics = {
        "source_occupied_ratio": round(source_ratio, 3),
        "output_occupied_ratio": round(output_ratio, 3),
        "source_large_gap_points": round(source_large_gap, 1),
        "output_large_gap_points": round(output_large_gap, 1),
        "source_tail_points": round(source_tail, 1),
        "output_tail_points": round(output_tail, 1),
    }
    return issues, metrics


def _render_gray(page: fitz.Page, target_width: int = 720) -> np.ndarray:
    scale = target_width / max(1.0, page.rect.width)
    pixmap = page.get_pixmap(
        matrix=fitz.Matrix(scale, scale),
        colorspace=fitz.csGRAY,
        alpha=False,
    )
    return np.frombuffer(pixmap.samples, dtype=np.uint8).reshape(pixmap.height, pixmap.width)


def _resize_like(image: np.ndarray, shape: tuple[int, int]) -> np.ndarray:
    if image.shape == shape:
        return image
    resized = Image.fromarray(image, mode="L").resize(
        (shape[1], shape[0]),
        Image.Resampling.BILINEAR,
    )
    return np.asarray(resized)


def _column_rows(gray: np.ndarray, x0: float, x1: float, y0: float, y1: float) -> np.ndarray:
    height, width = gray.shape
    left = max(0, min(width - 1, round(width * x0)))
    right = max(left + 1, min(width, round(width * x1)))
    top = max(0, min(height - 1, round(height * y0)))
    bottom = max(top + 1, min(height, round(height * y1)))
    crop = gray[top:bottom, left:right]
    row_ink_ratio = (crop < 245).mean(axis=1)
    return row_ink_ratio >= 0.0025


def compare_pdf_layout_occupancy(
    source_path: Path,
    output_path: Path,
    thresholds: OccupancyThresholds = DEFAULT_THRESHOLDS,
) -> dict:
    report = {
        "source": str(source_path),
        "translated": str(output_path),
        "thresholds": asdict(thresholds),
        "pages": [],
        "failures": [],
    }
    with fitz.open(source_path) as source, fitz.open(output_path) as output:
        if len(source) != len(output):
            report["failures"].append(
                f"page count changed: source={len(source)} translated={len(output)}"
            )
            return report

        columns = (("left", 0.05, 0.49), ("right", 0.51, 0.95))
        for page_index, (source_page, output_page) in enumerate(zip(source, output), start=1):
            source_gray = _render_gray(source_page)
            output_gray = _resize_like(_render_gray(output_page), source_gray.shape)
            points_per_row = source_page.rect.height / source_gray.shape[0]
            page_result = {"page": page_index, "columns": []}

            for name, x0, x1 in columns:
                source_rows = _column_rows(source_gray, x0, x1, 0.05, 0.94)
                output_rows = _column_rows(output_gray, x0, x1, 0.05, 0.94)
                issues, metrics = compare_region_rows(
                    source_rows,
                    output_rows,
                    points_per_row,
                    thresholds,
                )
                column_result = {"name": name, **metrics, "issues": issues}
                page_result["columns"].append(column_result)
                for issue in issues:
                    report["failures"].append(
                        f"p{page_index} {name}: {issue['kind']} {issue}"
                    )

            report["pages"].append(page_result)
    return report
