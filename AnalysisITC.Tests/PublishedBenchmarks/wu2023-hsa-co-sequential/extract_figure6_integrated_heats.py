#!/usr/bin/env python3
"""Regenerate the Wu 2023 Figure 6 integrated-heat fixtures."""

from __future__ import annotations

import argparse
import hashlib
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image
from pypdf import PdfReader


EXPECTED_IMAGE_SHA256 = "353bd37595fce4f517a45c5045f2761f999f1fee19796523dc080cd40c00e508"
HERE = Path(__file__).resolve().parent
FIXTURES = {
    "H9A": HERE / "wu2023-h9a-hsa-co-figure6.DH",
    "H67A": HERE / "wu2023-h67a-hsa-co-figure6.DH",
}


def extract_figure(pdf_path: Path) -> np.ndarray:
    page = PdfReader(str(pdf_path)).pages[9]
    candidates = []
    for image in page.images:
        decoded = Image.open(BytesIO(image.data)).convert("RGB")
        if decoded.size == (886, 1119):
            candidates.append((image.data, decoded))
    if len(candidates) != 1:
        raise RuntimeError(f"Expected one 886x1119 Figure 6 image, found {len(candidates)}")

    encoded, image = candidates[0]
    digest = hashlib.sha256(encoded).hexdigest()
    if digest != EXPECTED_IMAGE_SHA256:
        raise RuntimeError(f"Unexpected Figure 6 SHA-256: {digest}")
    return np.asarray(image, dtype=int)


def ratios() -> list[float]:
    cell_volume_ul = 1431.4
    concentration_ratio = 2.0 / 0.05
    cumulative_ul = 0.0
    result = []
    for volume_ul in [2.0005] + [8.0] * 34:
        cumulative_ul += volume_ul
        d = cumulative_ul / cell_volume_ul
        result.append(concentration_ratio * d * (1.0 + d / 2.0))
    return result


def marker_rows(rgb: np.ndarray, label: str) -> list[int]:
    red, green, blue = (rgb[:, :, index] for index in range(3))
    if label == "H9A":
        score = np.minimum(green, blue) - red
        previous_y = 768.0 + 70.0
    else:
        score = np.minimum(red, blue) - green
        previous_y = 827.0 + 70.0

    rows = []
    for ratio in ratios()[1:]:
        expected_x = 177.0 + 697.0 * ratio / 9.0
        center_x = round(expected_x)
        lower_y = max(20, int(previous_y - 110))
        upper_y = min(999, int(previous_y + 25))
        local = score[lower_y : upper_y + 1, center_x - 5 : center_x + 6]
        ys, xs = np.nonzero(local > 35)
        if not len(ys):
            raise RuntimeError(f"No {label} marker pixels near molar ratio {ratio:.9f}")

        weights = local[ys, xs]
        absolute_y = ys + lower_y
        order = np.argsort(absolute_y)
        absolute_y = absolute_y[order]
        weights = weights[order]
        previous_y = float(absolute_y[np.searchsorted(np.cumsum(weights), weights.sum() / 2.0)])
        rows.append(int(previous_y))
    return rows


def fixture_text(rows: list[int]) -> str:
    header = ["35", "0,35,0,0,0", "25,0.05,2,1.4314,0", "0", "0", "2.0005,0"]
    heats = []
    for y in rows:
        normalized_kcal_per_mole = -(y - 19.0) / 196.0
        heats.append(f"8,{16.0 * normalized_kcal_per_mole:.12f}")
    return "\n".join(header + heats) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("pdf", type=Path)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--check", action="store_true")
    action.add_argument("--write", action="store_true")
    args = parser.parse_args()

    rgb = extract_figure(args.pdf)
    for label, path in FIXTURES.items():
        generated = fixture_text(marker_rows(rgb, label))
        if args.write:
            path.write_text(generated, encoding="utf-8")
            print(f"wrote {path}")
        elif path.read_text(encoding="utf-8") != generated:
            raise SystemExit(f"fixture differs: {path}")
        else:
            print(f"verified {path}")


if __name__ == "__main__":
    main()
