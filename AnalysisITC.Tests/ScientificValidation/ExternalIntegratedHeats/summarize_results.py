#!/usr/bin/env python3
"""Summarize every external heat comparison, including failed recovery runs."""

import argparse
import hashlib
import json
import math
from pathlib import Path


def summarize(results, output):
    reference_path = Path(__file__).resolve().parent / "reference.json"
    reference = json.loads(reference_path.read_text())
    acceptance = reference["acceptance"]
    records = [json.loads(path.read_text()) for path in sorted(results.glob("*.json"))]
    summary = []
    for case in reference["cases"]:
        rows = [row for row in records if row["id"] == case["id"]]
        forward = [row for row in rows if row["kind"] == "forward"]
        fits = [row for row in rows if row["kind"] == "fit"]
        identities = {(row["algorithm"], row["startDirection"]) for row in fits}
        expected_identities = {(solver, start) for solver in ("LevenbergMarquardt", "NelderMead") for start in (-1, 1)}
        if len(forward) != 1 or len(fits) != 4 or identities != expected_identities:
            raise RuntimeError(f"Incomplete/duplicated comparison for {case['id']}; run with protocol diagnostics enabled.")

        def within(value, limit):
            return isinstance(value, (int, float)) and math.isfinite(value) and value <= limit

        def maximum(values):
            values = list(values)
            if not all(isinstance(value, (int, float)) and math.isfinite(value) for value in values):
                return None
            return max(values, default=0.0)

        def passes(row):
            return (row["success"]
                    and all(within(x, acceptance["fitted_n_relative"]) for x in row["nErrors"])
                    and all(within(x, acceptance["fitted_ka_relative"]) for x in row["kaErrors"])
                    and all(within(x, acceptance["fitted_enthalpy_relative"]) for x in row["hErrors"])
                    and isinstance(row["offset"], (int, float))
                    and within(abs(row["offset"]), acceptance["fitted_offset_absolute_j_per_mol"]))

        for row in fits:
            row["meets_parameter_recovery_acceptance"] = passes(row)
        summary.append(dict(
            id=case["id"], upstream_subshots_per_injection=case["upstream_subshots_per_injection"],
            forward_error_fraction=forward[0]["fractionalError"],
            forward_pass=within(forward[0]["fractionalError"], acceptance["forward_max_error_fraction_of_peak_heat"]),
            fit_passes=sum(passes(row) for row in fits), fit_count=len(fits),
            maximum_n_error_fraction=maximum(x for row in fits for x in row["nErrors"]),
            maximum_ka_error_fraction=maximum(x for row in fits for x in row["kaErrors"]),
            maximum_enthalpy_error_fraction=maximum(x for row in fits for x in row["hErrors"]),
            maximum_absolute_offset_j_per_mol=maximum(abs(row["offset"]) if isinstance(row["offset"], (int, float)) else row["offset"] for row in fits),
            forward=forward[0], fits=fits,
        ))
    if len(records) != 5 * len(reference["cases"]):
        raise RuntimeError("Unexpected extra comparison records in results directory.")
    report = dict(
        reference_sha256=hashlib.sha256(reference_path.read_bytes()).hexdigest(),
        acceptance=acceptance, forward_passes=sum(row["forward_pass"] for row in summary),
        forward_count=len(summary), fit_passes=sum(row["fit_passes"] for row in summary),
        fit_count=sum(row["fit_count"] for row in summary), cases=summary,
    )
    output.mkdir(parents=True, exist_ok=True)
    (output / "results.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    lines = ["| Dataset | Pytc subshots per injection | Maximum heat discrepancy | Maximum Ka error | Maximum ΔH error | Fits within acceptance |",
             "| --- | ---: | ---: | ---: | ---: | ---: |"]
    def percentage(value):
        return f"{100*value:.5f}%" if isinstance(value, (int, float)) and math.isfinite(value) else "nonfinite"

    for row in summary:
        lines.append(f"| {row['id']} | {row['upstream_subshots_per_injection']} | {percentage(row['forward_error_fraction'])} | "
                     f"{percentage(row['maximum_ka_error_fraction'])} | {percentage(row['maximum_enthalpy_error_fraction'])} | "
                     f"{row['fit_passes']}/{row['fit_count']} |")
    (output / "results-table.md").write_text("\n".join(lines) + "\n")
    print(f"Forward comparisons: {report['forward_passes']}/{report['forward_count']}; "
          f"parameter recovery: {report['fit_passes']}/{report['fit_count']}.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    summarize(args.results, args.output)
