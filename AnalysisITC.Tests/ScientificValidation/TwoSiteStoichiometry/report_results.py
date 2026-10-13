#!/usr/bin/env python3
"""Plot recorded comparisons only; no equilibrium or reference heat generation."""
import hashlib
import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = Path(__file__).resolve().parent


def main():
    reference = json.loads((ROOT/"reference.json").read_text())
    comparisons = json.loads((ROOT/"comparisons.json").read_text())
    diagnostic = json.loads((ROOT/"convergence-diagnostic.json").read_text())
    sha = hashlib.sha256((ROOT/"reference.json").read_bytes()).hexdigest()
    assert comparisons["ReferenceSha256"] == diagnostic["reference_sha256"] == sha
    cases = reference["cases"]
    results = {r["Id"]: r for r in comparisons["Results"]}
    assert set(results) == {c["id"] for c in cases}
    eps = 512*np.finfo(float).eps
    scores, diagnostic_scores = [], []
    rows = []
    intermediate_passes = 0
    fig, axes = plt.subplots(4, 4, figsize=(15, 12), constrained_layout=True)
    for case, ax in zip(cases, axes.flat):
        result = results[case["id"]]
        native = np.asarray(result["ExpectedHeatsJoules"])
        assert np.array_equal(native, case["heats_joules"])
        actual = np.asarray(result["ActualHeatsJoules"])
        assert len(actual) == len(native) == len(case["shots_microliters"])
        peak = max(abs(native))
        errors = abs(actual-native)
        score = max(errors/(eps*(peak+abs(native))))
        assert (score <= 1) == result["Passed"]
        assert np.isclose(max(errors)/peak, result["ErrorFractionOfPeak"], rtol=1e-14, atol=0)
        scores.append(score)
        for run in diagnostic["runs"]:
            curve = np.asarray(run["curves"][case["id"]])
            diagnostic_score = max(abs(actual-curve)/(eps*(max(abs(curve))+abs(curve))))
            if run["xtol_molar"] == 2e-18:
                intermediate_passes += diagnostic_score <= 1
            else:
                diagnostic_scores.append(diagnostic_score)
        tight = np.asarray(diagnostic["runs"][-1]["curves"][case["id"]])
        tight_error = max(abs(actual-tight))/max(abs(tight))
        ax.plot(range(1, len(native)+1), native*1e6, color="#245db1", label="Native pytc")
        ax.plot(range(1, len(actual)+1), actual*1e6, "--", color="#e98521", label="FT-ITC")
        ax.set_title(case["id"], fontsize=9)
        ax.set_xlabel("Injection", fontsize=8)
        ax.set_ylabel("Integrated heat (µJ)", fontsize=8)
        ax.tick_params(labelsize=8)
        ax.grid(alpha=.15)
        ns = ", ".join(f"{n:g}" for n in case["n"])
        kd = ", ".join(f"{k*1e6:g}" for k in case["kd_molar"])
        h = ", ".join(f"{v/1000:g}" for v in case["enthalpy"])
        rows.append(f"| {case['id']} | {ns} | {case['cell_molar']*1e6:g} / {case['syringe_molar']*1e6:g} | {kd} | {h} | {result['ErrorFractionOfPeak']*100:.6g} | {tight_error:.3g} |")
    axes.flat[-2].axis("off")
    axes.flat[-1].axis("off")
    handles, labels = axes.flat[0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="lower right", bbox_to_anchor=(.97, .07))
    fig.suptitle("Unequal-stoichiometry two-site integrated heats — no fitting\nOverlays alone cannot demonstrate floating-point agreement", fontsize=14)
    fig.savefig(ROOT/"heat-curves.png", dpi=160)
    plt.close(fig)
    fig, ax = plt.subplots(figsize=(12, 6), constrained_layout=True)
    positions = np.arange(len(cases))
    ax.scatter(positions, scores, label="Unmodified pytc, xtol = 2×10⁻¹² M", color="#245db1")
    ax.scatter(positions, diagnostic_scores, label="Modified-tolerance diagnostic, xtol = 2×10⁻²¹ M", color="#26805f")
    ax.axhline(1, color="#a63232", linestyle="--", label="Floating-point criterion")
    ax.set_yscale("log")
    ax.set_xticks(positions, [c["id"] for c in cases], rotation=50, ha="right", fontsize=9)
    ax.set_ylabel("Maximum error / allowed floating-point error")
    ax.set_title("Native discrepancies shrink to roundoff when only the external root tolerance changes")
    ax.grid(axis="y", alpha=.2)
    ax.legend(fontsize=9)
    fig.savefig(ROOT/"roundoff-comparison.png", dpi=170)
    plt.close(fig)
    native_passes = sum(s <= 1 for s in scores)
    tight_passes = sum(s <= 1 for s in diagnostic_scores)
    maximum_percent = max(r["ErrorFractionOfPeak"]*100 for r in results.values())
    md = ["# Unequal-stoichiometry two-site forward comparisons", "", "Run date: 11 October 2026", "",
        f"**Unmodified native pytc: {native_passes}/14 meet floating-point agreement.** Maximum error is {maximum_percent:.6g}% of peak integrated heat. All 14 are below the older 0.01% practical threshold; that threshold is not used to call these validation successes.", "",
        f"**Separate modified-tolerance diagnostic: {tight_passes}/14 meet the same floating-point criterion at xtol = 2e-21 M.** At 2e-18 M, {intermediate_passes}/14 meet it. Only the external C root solver's absolute tolerance changes. These results support agreement of the implemented mathematics, but do not turn the unmodified-pytc failures into passes.", "",
        "![Floating-point comparison](roundoff-comparison.png)", "",
        "## Parameters and outcomes", "",
        "N values are unequal in every case, and neither equals 1. Fractional N represents effective site capacity per nominal macromolecule. Values are selected for low, moderate and tight binding, with sufficient syringe ligand to traverse the capacities. Parameters are declared inputs; no solver estimates them. Concentrations and Kd are in µM; enthalpies are in kJ/mol.", "",
        "| Case | N₁, N₂ | Cell / syringe (µM) | Kd₁, Kd₂ (µM) | ΔH₁, ΔH₂ (kJ/mol) | Native max error (% peak) | Tight diagnostic max error / peak |",
        "|---|---|---|---|---|---|---|", *rows, "",
        "The usual protocol is a 200 µL cell, one 0.5 µL shot and 29 × 2 µL shots. Variable and preloaded cases use one 0.4 µL shot followed by six repeats of [1.2, 2.8, 1.6, 2.4, 2.0] µL. The preloaded case starts with 9 µM total ligand. The large-cell case uses 1400 µL, one 2 µL shot and 29 × 12 µL shots. Background heat is zero.", "",
        "The grid covers unequal fractional/integer capacities, both positive and negative enthalpies, opposing contributions, near cancellation, similar/equal affinities, a tight minority site, weak binding, variable shots, preloaded ligand, cell-volume scaling and reversed site order. It does not validate parameter recovery, tandem back-mixing or other dilution/heat conventions.", "",
        "![Integrated-heat curves](heat-curves.png)", "",
        "## Exact external mapping and provenance", "",
        "For N₁ = f·a and N₂ = f·b, where a and b are small integers, the native polynomial is P(L) = (1 + K₁L)^a (1 + K₂L)^b with fx_competent = f. Each coefficient βⱼ is the sum of affinity products over all j-occupied microscopic states; dHⱼ is their affinity-weighted enthalpy. This yields the same shared-ligand balance and heat content as two independent site classes. It is not a sum of separately titrated one-site curves. The C# tests verify polynomial, bound-ligand and enthalpy identities against closed-form independent-site expressions.", "",
        "[Pinned pytc source](https://github.com/harmslab/pytc/tree/d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac) performs all concentration updates, equilibria and finite-injection heats through BindingPolynomial.dQ → bp_ext.dQ. Locally authored code only maps inputs and serializes native arrays. Python 3.9.6, numpy 1.26.4, scipy 1.13.1 and pytc-fitter 1.1.5 were used; compatible build tools were setuptools 75.8.2 and wheel 0.45.1.", "",
        "The native manifest records generator, source, binary and .DH hashes. A second native generation reproduced the full manifest and all 14 .DH files byte-for-byte. comparisons.json retains every original native and actual production-model heat. convergence-diagnostic.json separately records the two tolerance patches, compiled binary hashes and all 28 modified-tolerance arrays. Native fixture heats are never replaced.", "",
        "## Reproduction", "", "```sh",
        "python generate_reference.py --pytc-source /absolute/pinned/pytc",
        "python convergence_diagnostic.py --pytc-source /absolute/pinned/pytc",
        "# From the repository root; use an absolute destination:",
        "FTITC_RUN_TWO_SITE_ROUNDOFF=1 FTITC_TWO_SITE_RESULTS=/absolute/repo/AnalysisITC.Tests/ScientificValidation/TwoSiteStoichiometry dotnet test AnalysisITC.Core.Tests --filter FullyQualifiedName~TwoSiteStoichiometryForwardTests",
        "python AnalysisITC.Tests/ScientificValidation/TwoSiteStoichiometry/report_results.py", "```", "",
        "The strict unmodified-pytc comparisons deliberately remain executable and fail when enabled. Ordinary runs skip those known mismatches, run all 14 mapping checks and all 14 tightened-tolerance checks, and separately verify/record the native outcomes. A green ordinary suite must not be presented as 14 unmodified-pytc forward-validation successes. Acceptance is abs(errorᵢ) ≤ 512·double_epsilon·(peak_abs_native_heat + abs(native_heatᵢ)); peak scaling keeps the criterion meaningful near zero heat.", ""]
    if (ROOT/"verification.json").exists():
        verification = json.loads((ROOT/"verification.json").read_text())
        md += ["## Recorded test execution", ""]
        for run in verification["runs"]:
            counts = run["counters"]
            skipped = int(counts["total"])-int(counts["executed"])
            md += [f"- {run['name']}: {counts['passed']} passed, {counts['failed']} failed, {skipped} skipped."]
        md += ["", "The 14 focused failures are the strict unmodified-pytc roundoff comparisons. The 63 focused passes include all 14 tightened-tolerance comparisons, all 14 exact mapping checks, native-outcome recording, and existing root/bookkeeping checks. Full Core skips the strict native comparison theory and the existing higher-step recovery diagnostic. verification.json records the TRX hashes and failed test names. No production model code changed.", ""]
    (ROOT/"DEFAULT_TOLERANCE_NOTES.md").write_text("\n".join(md))


if __name__ == "__main__":
    main()
