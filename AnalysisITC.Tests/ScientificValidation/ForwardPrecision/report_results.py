#!/usr/bin/env python3
"""Create the concise report from recorded production predictions, without fitting."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

EPSILON = 2.220446049250313e-16


def hash_file(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def report(original_results, stoichiometry_results, trx, core_trx=None, baseline_trx=None):
    root = Path(__file__).resolve().parent
    old = root.parent/"PytcBookkeeping"
    extra = root.parent/"TwoSiteStoichiometry"
    precision_path = old/"precision-reference.json"
    precision = json.loads(precision_path.read_text())
    assert precision["reference_sha256"] == hash_file(old/"reference.json")
    assert precision["stoichiometry_reference_sha256"] == hash_file(extra/"reference.json")
    assert precision["generator_sha256"] == hash_file(old/"precision_reference.py")
    cases = json.loads((old/"reference.json").read_text())["cases"] + json.loads((extra/"reference.json").read_text())["cases"]
    predictions = {r["Id"]: r["ActualHeatsJoules"] for path in (original_results, stoichiometry_results)
                   for r in json.loads(path.read_text())["Results"]}
    references = precision["runs"][0]["curves"]
    confirmation = precision["runs"][1]["curves"]
    assert len(cases) == len(predictions) == len(references) == 52
    rows, results = [], []
    for case in cases:
        name = case["id"]
        expected, actual = references[name], predictions[name]
        assert len(expected) == len(actual) == len(case["heats_joules"])
        peak = max(map(abs, expected))
        ratios = [abs(a-e)/(512*EPSILON*(peak+abs(e))) for a, e in zip(actual, expected)]
        assert max(ratios) <= 1, f"Floating-point mismatch: {name} ({max(ratios):.6g})"
        assert all(abs(a-b) <= 512*EPSILON*(max(map(abs, confirmation[name]))+abs(b))
                   for a, b in zip(expected, confirmation[name])), f"Reference not converged: {name}"
        unequal = "counts" in case
        parameters = case if unequal else case["parameters"]
        model = "two-site" if unequal else case["model"]
        ns, logs, hs = (parameters[key] for key in ("n", "logka", "enthalpy"))
        rows.append(dict(test=name, model=model, n=";".join(f"{v:g}" for v in ns),
            cell_uM=case["cell_molar"]*1e6, syringe_uM=case["syringe_molar"]*1e6,
            cell_uL=case["cell_microliters"] if unequal else case["cell_liters"]*1e6,
            initial_ligand_uM=case["initial_ligand_molar"]*1e6,
            logKa=";".join(f"{v:.17g}" for v in logs),
            enthalpy_J_per_mol=";".join(f"{v:g}" for v in hs),
            competitor=json.dumps(case.get("competitor"), separators=(",", ":")),
            result="PASS"))
        results.append(dict(id=name, result="PASS", maximum_error_over_allowed=max(ratios),
            default_maximum_error_over_allowed=max(abs(a-e)/(512*EPSILON*(max(map(abs,case["heats_joules"]))+abs(e)))
                for a,e in zip(actual,case["heats_joules"])),
            expected_heats_joules=expected, actual_heats_joules=actual))
    with (root/"results.csv").open("w", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    payload = dict(criterion="abs(error_i) <= 512*epsilon*(peak_abs_heat + abs(reference_heat_i))",
        epsilon=EPSILON, precision_reference_sha256=hash_file(precision_path),
        prediction_sha256={str(p): hash_file(p) for p in (original_results, stoichiometry_results)}, results=results)
    (root/"comparisons.json").write_text(json.dumps(payload, indent=2)+"\n")
    counts = ET.parse(trx).find(".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters").attrib
    assert counts["failed"] == "0"
    verification = dict(model_tests=counts, trx_sha256=hash_file(trx),
        report_generator_sha256=hash_file(Path(__file__)), comparison_sha256=hash_file(root/"comparisons.json"))
    repository = root.parents[2]
    verification["production_source_sha256"] = {name: hash_file(repository/name) for name in (
        "AnalysisITC.Core/Analysis2/Models/OneSetOfSites.cs",
        "AnalysisITC.Core/Analysis2/Models/TwoSetsOfSites.cs",
        "AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs",
        "AnalysisITC.Core/Analysis2/Models/SequentialBindingSites.cs")}
    illustration = next(case for case in cases if case["id"] == "fractional-low-capacity")
    sampling = []
    for component in illustration["site_heats_joules"]:
        magnitude = list(map(abs, component))
        peak = max(magnitude)
        start = magnitude.index(peak)
        sampling.append(sum(.1*peak < heat < .9*peak for heat in magnitude[start:]))
    verification["two_site_illustration"] = dict(case=illustration["id"],
        uniform_injection_microliters=illustration["shots_microliters"][0],
        transition_points_90_to_10_percent=sampling,
        component_source="Full external coupled BindingPolynomial with the other site's enthalpy set to zero")
    for label, path in (("full_core_suite", core_trx), ("unrelated_lifecycle_recheck_with_original_tolerances", baseline_trx)):
        if path:
            document = ET.parse(path)
            verification[label] = dict(
                counts=document.find(".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters").attrib,
                trx_sha256=hash_file(path),
                failed_tests=[result.attrib["testName"] for result in document.findall(
                    ".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult") if result.attrib["outcome"] == "Failed"])
    (root/"verification.json").write_text(json.dumps(verification, indent=2)+"\n")

    from plot_forward_curves import plot_curves
    plot_curves(root, cases, references, predictions)
    intro = """# Forward-model precision validation

All 52 tests **PASS** at floating-point precision using prescribed parameters and externally generated integrated injection heats.

References use pinned pytc equations and finite injections. Both default and converged arithmetic were evaluated. The PASS criterion is `|errorᵢ| ≤ 512ε (peak |heat| + |reference heatᵢ|)`, with `ε = 2.220446049250313e−16`.

Converged references use `xtol = 2e−24 M` for binding polynomials and 80-digit arithmetic for the competitive analytical expression. A further check at `2e−27 M` and 100 digits confirms convergence. FT-ITC uses its production forward models, without fitting.

![Forward-model heat curves](forward-curves.png)

Lines: pytc. Circles: FT-ITC. Sequential curves show two, three and four steps. Residuals are FT-ITC minus pytc injection heats. Full parameters: [results.csv](results.csv). Numerical comparisons, including the default-reference audit: [comparisons.json](comparisons.json).

| Test | Model | N | Floating-point precision |
| --- | --- | --- | --- |
"""
    table = "\n".join(f"| {row['test']} | {row['model']} | {row['n'].replace(';', ', ') or '—'} | PASS |" for row in rows)
    (root/"README.md").write_text(intro+table+"\n")
    print(f"52/52 PASS; model suite: {counts['passed']} passed, {counts.get('notExecuted', '0')} skipped.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-results", type=Path, required=True)
    parser.add_argument("--stoichiometry-results", type=Path, required=True)
    parser.add_argument("--trx", type=Path, required=True)
    parser.add_argument("--core-trx", type=Path)
    parser.add_argument("--baseline-trx", type=Path)
    args = parser.parse_args()
    report(args.original_results.resolve(), args.stoichiometry_results.resolve(), args.trx.resolve(), args.core_trx, args.baseline_trx)
