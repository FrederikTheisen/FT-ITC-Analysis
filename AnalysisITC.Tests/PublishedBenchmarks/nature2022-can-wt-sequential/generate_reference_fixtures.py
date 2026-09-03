#!/usr/bin/env python3
"""Generate fixed-parameter Can WT predictions with the pinned upstream model."""

import argparse
import contextlib
import hashlib
import io
import math
from pathlib import Path
import runpy
import sys

import numpy


COMMIT = "49569a7f62d01b67f213b28d7c820818831c5b5c"
EXPECTED_SHA256 = {
    "fit_itc_model.py": "5fd16d46cdadd971354a00087788c2644bead51fc34ce3d3f2c6f01fa57ea15c",
    "can_wt_preq1_1_peaq_data": "e043fd50fb17228ceb7c9a1fffbaf70d0a2f65de0534e3b0bcb4bf115eb97518",
    "can_wt_preq1_2_peaq_data": "715e6ad4279716e021eb696400e017c7d2e64561c122bcf21af78c466e131d13",
    "can_wt_preq1_3_peaq_data": "2f78ed7e455d87c769ffef72c0206d87ed54d3589b74485bd685436701c46c00",
    "can_wt_preq1_123_log10_16_fit_0.875": "c05af2f5c36b244ca60e50fdefc5c9470ae6eef051a9728a990dabc4fad28c8f",
}

# [offset 1..3, eta, DH_A1, DH_B1, DH_B2, KD_A1, KD_A2, KD_B2].
# These are fit.x at full binary64 precision after regenerating the named
# upstream global result with NumPy 2.0.2 and SciPy 1.13.1.
FIT = numpy.array([
    0.34611151995817996,
    -0.033651140984190059,
    -0.56611565226946092,
    1.0065750263310016,
    -36.903317860618174,
    -29.819465518667954,
    -40.307923920441787,
    1.4762055628369972,
    0.18211877274971924,
    0.27879105348594285,
])

LIGAND_UM = (60.0, 60.0, 55.0)
RECEPTOR_UM = 3.0
CELL_VOLUME_UL = 1420.6
TEMPERATURE_C = 37.0


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_upstream(root):
    paths = {
        "fit_itc_model.py": root / "fit_itc_model.py",
        **{
            f"can_wt_preq1_{index}_peaq_data":
                root / "2021_preq1_riboswitch_data" / "itc_data" /
                f"can_wt_preq1_{index}_peaq_data"
            for index in (1, 2, 3)
        },
        "can_wt_preq1_123_log10_16_fit_0.875":
            root / "2021_preq1_riboswitch_data" / "fits" /
            "can_wt_preq1_123_log10_16_fit_0.875",
    }
    for name, path in paths.items():
        actual = sha256(path)
        if actual != EXPECTED_SHA256[name]:
            raise RuntimeError(f"{name}: expected SHA-256 {EXPECTED_SHA256[name]}, got {actual}")
    return paths


def load_pinned_implementation(root, paths):
    old_argv = sys.argv
    data = root / "2021_preq1_riboswitch_data" / "itc_data"
    sys.argv = [
        str(paths["fit_itc_model.py"]), "-n", "2", "-s", "1",
        "-d", repr(math.log(10.0)), "-e", "16",
        "-l", "60.0,60.0,55.0", "-r", "3.0", "-v", "1420.6",
        "-p", repr(10.0 ** 0.875),
        *(str(data / f"can_wt_preq1_{index}_peaq_data") for index in (1, 2, 3)),
    ]
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            state = runpy.run_path(str(paths["fit_itc_model.py"]), run_name="__main__")
    finally:
        sys.argv = old_argv

    # Optimizer recovery is not fixture truth. This check only detects an
    # incompatible numerical environment before using the fixed FIT vector.
    if not numpy.allclose(state["fit"].x, FIT, rtol=0.0, atol=5e-12):
        raise RuntimeError("Regenerated upstream fit does not match the pinned full-precision vector")
    return state["TwoSites"]


def write_dh(path, volumes_ul, predicted_kcal_per_mol, ligand_um, eta):
    heat_ucal = predicted_kcal_per_mol * ligand_um * volumes_ul * 1e-3
    lines = [
        str(len(volumes_ul)),
        f"0,{len(volumes_ul)},0,0,0",
        f"{TEMPERATURE_C:.17g},{RECEPTOR_UM * eta / 1000:.17g},"
        f"{ligand_um / 1000:.17g},{CELL_VOLUME_UL / 1000:.17g},0",
        "0",
        "0",
        *(f"{volume:.17g},{heat:.17g}" for volume, heat in zip(volumes_ul, heat_ucal)),
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("upstream_root", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()

    root = args.upstream_root.resolve()
    paths = verify_upstream(root)
    two_sites = load_pinned_implementation(root, paths)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)

    eta = FIT[3]
    microscopic = FIT[4:]
    for experiment, ligand_um in enumerate(LIGAND_UM, start=1):
        source = numpy.loadtxt(paths[f"can_wt_preq1_{experiment}_peaq_data"])
        volumes_ul = source[:, 0]
        fractional_volume = numpy.cumsum(volumes_ul) / CELL_VOLUME_UL
        receptor = RECEPTOR_UM * numpy.exp(-fractional_volume)
        ligand = ligand_um * (1.0 - numpy.exp(-fractional_volume))
        model = two_sites(0, RECEPTOR_UM, ligand_um, receptor, ligand)
        parameters = numpy.concatenate(([FIT[experiment - 1], eta], microscopic))
        predicted = model(parameters, numpy.zeros(volumes_ul.size))
        write_dh(
            output / f"can-wt-preq1-{experiment}-reference-predicted.dh",
            volumes_ul, predicted, ligand_um, eta)


if __name__ == "__main__":
    main()
