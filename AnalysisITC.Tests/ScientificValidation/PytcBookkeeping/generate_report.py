#!/usr/bin/env python3
"""Render recorded FT-ITC/native-pytc comparisons; never generate model predictions.

Requires numpy, matplotlib, reportlab and pypdf. Inputs are reference.json and
comparisons.json written by the native generator and C# tests, respectively.
"""
import argparse
from datetime import date
import hashlib
from html import escape
import json
from pathlib import Path
import textwrap
import xml.etree.ElementTree as ET

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D
import numpy as np
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Image, Table, TableStyle, PageBreak
from pypdf import PdfReader


BLUE = "#246A9B"
ORANGE = "#D97732"
TEAL = "#12766F"
INK = "#1E3042"
LIMIT = "#B74645"
TITLES = {
    "one-exothermic": "One-site / exothermic",
    "one-endothermic": "One-site / endothermic",
    "one-c10-small": "One-site / c=10, small injections",
    "one-c100-small": "One-site / c=100, small injections",
    "one-c1000-small": "One-site / c=1000, small injections",
    "one-c10-large": "One-site / c=10, large injections",
    "one-c100-large": "One-site / c=100, large injections",
    "one-c1000-large": "One-site / c=1000, large injections",
    "one-n05": "One-site / N=0.5",
    "one-n1": "One-site / N=1",
    "one-n2": "One-site / N=2",
    "two-c50-r10": "Two-site / c=50, Kd ratio 10",
    "two-c500-r10": "Two-site / c=500, Kd ratio 10",
    "two-c2000-r10": "Two-site / c=2000, Kd ratio 10",
    "two-c50-r100": "Two-site / c=50, Kd ratio 100",
    "two-c500-r100": "Two-site / c=500, Kd ratio 100",
    "two-c2000-r100": "Two-site / c=2000, Kd ratio 100",
    "two-realistic": "Tight binding",
    "two-realistic-500pM": "Moderate binding",
    "two-realistic-5nM": "Weaker binding",
    "two-tight-scale-2x": "Tight binding / 2× scale",
    "two-tight-scale-10x": "Tight binding / 10× scale",
    "two-tight-scale-100x": "Tight binding / 100× scale",
    "two-kd50-50": "Two-site / Kd₁=50 nM, Kd₂=50 nM",
    "two-kd50-500": "Two-site / Kd₁=50 nM, Kd₂=500 nM",
    "two-kd50-5000": "Two-site / Kd₁=50 nM, Kd₂=5 µM",
    "two-kd500-500": "Two-site / Kd₁=500 nM, Kd₂=500 nM",
    "two-kd500-5000": "Two-site / Kd₁=500 nM, Kd₂=5 µM",
    "two-kd500-50000": "Two-site / Kd₁=500 nM, Kd₂=50 µM",
    "competitive-0": "Competitive / no competitor",
    "competitive-500": "Competitive / 500 µM competitor",
    "competitive-1000": "Competitive / 1 mM competitor",
    "sequential-2": "Sequential / 2 steps",
    "sequential-2-segment-start": "Sequential / 2 steps, initial ligand",
    "sequential-3": "Sequential / 3 steps",
    "sequential-3-segment-start": "Sequential / 3 steps, initial ligand",
    "sequential-4": "Sequential / 4 steps",
    "sequential-2-low-affinity": "Sequential / 2 steps, lower affinity",
    "sequential-3-low-affinity": "Sequential / 3 steps, lower affinity",
    "sequential-4-low-affinity": "Sequential / 4 steps, lower affinity",
    "sequential-4-segment-start": "Sequential / 4 steps, initial ligand",
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_inputs(directory):
    reference = json.loads((directory/"reference.json").read_text())
    comparisons = json.loads((directory/"comparisons.json").read_text())
    if comparisons["ReferenceSha256"] != sha(directory/"reference.json"):
        raise ValueError("Comparisons belong to a different reference manifest. Rerun the C# tests.")
    if reference["generator_sha256"] != sha(directory/"generate_reference.py"):
        raise ValueError("Reference generator changed. Regenerate native fixtures before reporting.")
    cases = reference["cases"]
    results = {result["Id"]: result for result in comparisons["Results"]}
    if len(results) != len(comparisons["Results"]) or set(results) != {case["id"] for case in cases}:
        raise ValueError("Comparison case list differs from the frozen references.")
    limit = comparisons["PracticalErrorFractionOfPeak"]
    if limit != reference["acceptance"]["maximum_error_fraction_of_peak"]:
        raise ValueError("Inconsistent acceptance criteria.")
    models = {"one-site": "OneSetOfSites", "two-site": "TwoSetsOfSites",
              "competitive": "CompetitiveBinding", "sequential": "SequentialBindingSites"}
    for case in cases:
        result = results[case["id"]]
        if sha(directory/case["file"]) != case["sha256"] or result["ReferenceFileSha256"] != case["sha256"]:
            raise ValueError(f"Reference hash mismatch: {case['id']}")
        if result["FtItcModel"] != models[case["model"]]:
            raise ValueError(f"Wrong FT-ITC model: {case['id']}")
        expected = np.array(result["ExpectedHeatsJoules"])
        actual = np.array(result["ActualHeatsJoules"])
        if not np.array_equal(expected, case["heats_joules"]) or actual.shape != expected.shape:
            raise ValueError(f"Incomplete or stale plotted data: {case['id']}")
        if not np.isfinite(actual).all() or not np.isfinite(expected).all():
            raise ValueError(f"Nonfinite plotted data: {case['id']}")
        ratio = np.asarray(case["molar_ratio"], dtype=float)
        if ratio.shape != expected.shape or not np.isfinite(ratio).all() or np.any(np.diff(ratio) <= 0):
            raise ValueError(f"Invalid molar-ratio x-axis data: {case['id']}")
        peak = float(np.max(np.abs(expected)))
        maximum = float(np.max(np.abs(actual-expected)))
        fraction = maximum/peak
        if not np.isclose(fraction, result["ErrorFractionOfPeak"], rtol=1e-12, atol=0):
            raise ValueError(f"Recorded error disagrees with plotted data: {case['id']}")
        if bool(maximum <= limit*peak) != result["Passed"]:
            raise ValueError(f"Recorded acceptance disagrees with plotted data: {case['id']}")
        roundoff = comparisons["RoundoffMultiplier"]*comparisons["Epsilon"]*(peak+np.abs(expected))
        if bool(np.all(np.abs(actual-expected) <= roundoff)) != result["RoundoffGoalMet"]:
            raise ValueError(f"Recorded roundoff diagnosis disagrees with plotted data: {case['id']}")
    return reference, comparisons, results


def plot_overview(cases, results, limit, path):
    figure, axis = plt.subplots(figsize=(10.5, 8.2), layout="constrained")
    values = [100*results[c["id"]]["ErrorFractionOfPeak"] for c in cases]
    y = np.arange(len(cases))
    styles = {
        "one-site": ("#246A9B", "o", "One-site"),
        "two-site": ("#12766F", "s", "Two-site"),
        "competitive": ("#8A5A9B", "D", "Competitive"),
        "sequential": ("#B26A00", "^", "Sequential"),
    }
    for model, (color, marker, label) in styles.items():
        selected = [i for i, case in enumerate(cases) if case["model"] == model]
        axis.scatter([values[i] for i in selected], [y[i] for i in selected],
                     s=48, c=color, marker=marker, label=label, zorder=3)
    axis.set_yticks(y, [TITLES[c["id"]] for c in cases])
    axis.invert_yaxis()
    axis.set_xscale("log")
    axis.set_xlim(min(values)*.25, limit*100*12)
    axis.axvline(limit*100, color=LIMIT, linestyle="--", linewidth=1.5)
    axis.text(limit*100*1.3, -.8, "0.01%\nlimit", color=LIMIT, fontsize=10, va="bottom")
    axis.set_xlabel("Maximum absolute difference / peak native injection heat (%)\nLog scale; farther left means closer agreement")
    axis.grid(axis="x", alpha=.18)
    axis.set_title("All native-pytc forward comparisons", loc="left", fontsize=16, color=INK, pad=20)
    figure.legend(loc="outside lower center", ncol=4, frameon=False, fontsize=9)
    figure.savefig(path, dpi=190, facecolor="white")
    plt.close(figure)


def plot_pairs(cases, results, path):
    columns = len(cases)
    figure, axes = plt.subplots(2, columns, figsize=(5.25*columns, 6.1), sharex="col",
                               gridspec_kw={"height_ratios": [2, 1]}, layout="constrained")
    if columns == 1:
        axes = np.asarray(axes).reshape(2, 1)
    for col, case in enumerate(cases):
        result = results[case["id"]]
        expected = np.array(result["ExpectedHeatsJoules"])
        actual = np.array(result["ActualHeatsJoules"])
        ratio = np.asarray(case["molar_ratio"], dtype=float)
        heat, error = axes[:, col]
        heat.plot(ratio, expected*1e6, color=BLUE, linewidth=1.5, label="Native pytc")
        heat.plot(ratio, actual*1e6, color=ORANGE, linestyle="none", marker="o", markersize=3.1,
                  markerfacecolor="none", markeredgewidth=.8, label="FT-ITC")
        heat.axhline(0, color=INK, linewidth=.5, alpha=.4)
        # Keep the full protocol and model specification visible in each panel.
        # Wrapping is important for the three-panel figures, where an unwrapped
        # title would run into the neighboring axes.
        annotation = textwrap.fill(graph_parameter_text(case), width=62,
                                   break_long_words=False, break_on_hyphens=False)
        heat.set_title(TITLES[case["id"]] + "\n" + annotation,
                       loc="left", fontsize=7.5, color=INK, pad=10)
        heat.set_ylabel("Integrated injection heat (µJ)")
        heat.legend(frameon=False, fontsize=8, loc="best")
        signed = 100*(actual-expected)/result["PeakHeatJoules"]
        error.plot(ratio, signed, color=TEAL, marker=".", markersize=2.6, linewidth=.8)
        error.axhline(0, color=INK, linewidth=.5)
        error.set_ylabel("Error (% of peak)\nZoomed scale")
        error.set_xlabel("Nominal molar ratio (total titrant / initial cell macromolecule)")
        error.ticklabel_format(axis="y", style="sci", scilimits=(0, 0))
        status = "PASS" if result["Passed"] else "FAILED"
        error.set_title(f"Max |error| = {100*result['ErrorFractionOfPeak']:.4g}%   |   "
            f"{status} at 0.01%", fontsize=9, loc="left", color=TEAL if result["Passed"] else LIMIT)
        for axis in (heat, error):
            axis.grid(alpha=.13)
            axis.spines[["top", "right"]].set_visible(False)
    figure.savefig(path, dpi=190, facecolor="white")
    plt.close(figure)


def trx_summary(path):
    if not path:
        return None
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = root.find("t:ResultSummary/t:Counters", ns).attrib
    entries = root.findall("t:Results/t:UnitTestResult", ns)
    failures = [r.attrib["testName"] for r in entries if r.attrib["outcome"] == "Failed"]
    # VSTest can report notExecuted=0 in aggregate counters even when xUnit
    # emits NotExecuted results. Count those entries, not the misleading field.
    counts = dict(total=len(entries), passed=sum(r.attrib["outcome"] == "Passed" for r in entries),
                  failed=len(failures), skipped=sum(r.attrib["outcome"] == "NotExecuted" for r in entries))
    for key in ("total", "passed", "failed"):
        if counts[key] != int(counters[key]):
            raise ValueError(f"Inconsistent {key} test-run evidence: {path}")
    return {"source": path.name, "sha256": sha(path), "counters": counters, "result_counts": counts, "failed_tests": failures}


def parameter_text(case):
    p = case["parameters"]
    values = lambda sequence: ", ".join(f"{v:g}" for v in sequence)
    shots = np.array(case["injection_liters"])*1e6
    volume = f"{shots[0]:g}" if np.all(shots == shots[0]) else f"{min(shots):g}-{max(shots):g}"
    text = (f"{case['id']}: cell {case['cell_molar']*1e6:g} µM; syringe {case['syringe_molar']*1e3:g} mM; "
            f"initial ligand {case['initial_ligand_molar']*1e6:g} µM; {len(shots)} injections of {volume} µL. "
            f"log10 Ka = [{values(p['logka'])}]; enthalpies = [{values([h/1000 for h in p['enthalpy']])}] kJ/mol")
    if p["n"]:
        text += f"; N = [{values(p['n'])}]"
    if case.get("c_value") is not None:
        text += f"; c = {case['c_value']:g}"
    if case.get("kd_ratio") is not None:
        text += f"; Kd2/Kd1 = {case['kd_ratio']:g}"
    if case["model"] == "two-site":
        text += f"; Kd = [{values([1/(10**k)*1e9 for k in p['logka']])}] nM"
    if case["competitor"]:
        c = case["competitor"]
        text += f"; competitor {c['concentration']*1e6:g} µM, log10 Ka {c['logka']:g}, enthalpy {c['enthalpy']/1000:g} kJ/mol"
    return text+"."


def graph_parameter_text(case):
    p = case["parameters"]
    shots = np.asarray(case["injection_liters"], dtype=float)*1e6
    volume = f"{shots[0]:g}" if np.all(shots == shots[0]) else f"{min(shots):g}–{max(shots):g}"
    values = lambda sequence: ", ".join(f"{v:g}" for v in sequence)
    text = (f"cell {case['cell_molar']*1e6:g} µM; syringe {case['syringe_molar']*1e6:g} µM; "
            f"{len(shots)}×{volume} µL")
    if case["model"] == "one-site":
        text += f"; logKa {values(p['logka'])}; ΔH {values([h/1000 for h in p['enthalpy']])} kJ/mol"
    elif case["model"] == "two-site":
        kd = [1/(10**k)*1e9 for k in p["logka"]]
        text += f"; Kd {values(kd)} nM; ΔH {values([h/1000 for h in p['enthalpy']])} kJ/mol"
        if case.get("c_value") is not None:
            text += f"; c₁ {case['c_value']:g}"
        if case.get("kd_ratio") is not None:
            text += f"; Kd₂/Kd₁ {case['kd_ratio']:g}"
    elif case["model"] == "competitive":
        text += f"; target Kd {1/(10**p['logka'][0])*1e9:g} nM, ΔH {p['enthalpy'][0]/1000:g} kJ/mol"
        c = case["competitor"]
        text += f"; competitor {c['concentration']*1e6:g} µM, Kd {1/(10**c['logka'])*1e6:g} µM, ΔH {c['enthalpy']/1000:g} kJ/mol"
    elif case["model"] == "sequential":
        kd = values([1/(10**k)*1e9 for k in p["logka"]])
        text += f"; Kd {kd} nM; ΔH {values([h/1000 for h in p['enthalpy']])} kJ/mol"
    else:
        text += f"; logKa {values(p['logka'])}; ΔH {values([h/1000 for h in p['enthalpy']])} kJ/mol"
    return text


def generate(directory, pdf_output, report_date, core_trx, focused_trx):
    reference, comparisons, results = load_inputs(directory)
    cases = reference["cases"]
    limit = comparisons["PracticalErrorFractionOfPeak"]
    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9, "axes.labelsize": 9,
                         "axes.edgecolor": "#708090", "text.color": INK, "axes.labelcolor": INK})
    figures = directory/"figures"
    figures.mkdir(exist_ok=True)
    for model, _ in [("two-site", ""), ("one-site", ""), ("competitive", ""), ("sequential", "")]:
        for stale in figures.glob(f"{model}-*.png"):
            stale.unlink()
    plot_overview(cases, results, limit, figures/"forward-comparison-overview.png")
    groups = [("two-site", "Two independent sites"), ("one-site", "One-site binding"),
              ("competitive", "Competitive binding"), ("sequential", "Sequential binding")]
    sections = []
    for model, title in groups:
        selected = [case for case in cases if case["model"] == model]
        if model == "one-site":
            by_id = {case["id"]: case for case in selected}
            chunks = [[by_id["one-exothermic"], by_id["one-endothermic"]]]
            for c_value in (10, 100, 1000):
                chunks.append([case for case in selected if case.get("c_value") == c_value])
            chunks.append([by_id["one-n05"], by_id["one-n1"], by_id["one-n2"]])
        elif model == "sequential":
            by_id = {case["id"]: case for case in selected}
            chunks = [[by_id[f"sequential-{count}"] for count in (2, 3, 4)],
                      [by_id[f"sequential-{count}-low-affinity"] for count in (2, 3, 4)]]
        else:
            group_size = 3 if model in ("two-site", "competitive", "sequential") else 2
            chunks = [selected[index:index+group_size] for index in range(0, len(selected), group_size)]
        for index, pair in enumerate(chunks):
            image = figures/f"{model}-{index+1}.png"
            plot_pairs(pair, results, image)
            sections.append((title, pair, image))
    core = trx_summary(core_trx)
    focused = trx_summary(focused_trx)
    audit_path = directory/"native-audit.json"
    audit = json.loads(audit_path.read_text()) if audit_path.exists() else None
    if audit and (audit["reference_sha256"] != sha(directory/"reference.json")
                  or audit["source_commit"] != reference["source"]["commit"]):
        raise ValueError("Native-source audit belongs to a different reference. Rerun the audit.")
    required_cases = [c for c in cases if c.get("acceptance", "required") == "required"]
    failed_cases = [c for c in cases if not results[c["id"]]["Passed"]]
    passed = sum(results[c["id"]]["Passed"] for c in required_cases)
    worst = max(results.values(), key=lambda r: r["ErrorFractionOfPeak"])
    two = [results[c["id"]] for c in cases if c["model"] == "two-site"]
    worst_two = max(two, key=lambda r: r["ErrorFractionOfPeak"])
    required_two = [results[c["id"]] for c in cases
                    if c["model"] == "two-site" and c.get("acceptance", "required") == "required"]
    summary = (f"{passed}/{len(required_cases)} required native-pytc forward cases pass the 0.01% peak-heat limit. "
               f"{sum(r['Passed'] for r in required_two)}/{len(required_two)} required two-independent-site cases pass. "
               f"The largest required two-site difference is {100*max(required_two, key=lambda r: r['ErrorFractionOfPeak'])['ErrorFractionOfPeak']:.6g}% of peak heat.")
    if failed_cases:
        diagnostic = max((results[c["id"]] for c in failed_cases), key=lambda r: r["ErrorFractionOfPeak"])
        summary += (f" The {len(failed_cases)} failed cases include "
                    f"a largest discrepancy in {diagnostic['Id']} of "
                    f"{100*diagnostic['ErrorFractionOfPeak']:.6g}%; none meets the practical limit.")
    methods = (
        "These are forward calculations at prescribed parameters, not fitted curves. Native pytc-fitter 1.1.5 "
        "generates integrated finite-injection heats. FT-ITC imports the same protocol and evaluates its existing "
        "production model at the mapped parameters, using pytc-discrete bookkeeping. There is no noise, "
        "background, injection subdivision, raw thermogram integration or solver replacement. The curve figures use "
        "nominal molar ratio: total syringe titrant added (plus any initial cell titrant) divided by the "
        "initial cell macromolecule amount. All cells are 200 µL in the frozen protocol.")
    metric = (
        "The error is the largest absolute FT-ITC minus native-pytc injection-heat difference, divided by the "
        "largest absolute native heat in that dataset. Acceptance is at most 0.01% of peak heat. "
        "Using the dataset peak keeps the measure meaningful when individual heats approach or cross zero. "
        "")
    mapping = (
        "The two-site reference uses native BindingPolynomial with N1 = N2 = 1. "
        "For free ligand L, P = (1 + Ka L)(1 + Kb L), so beta1 = Ka + Kb and beta2 = Ka Kb. "
        "The native singly occupied ensemble enthalpy is (Ka Ha + Kb Hb)/(Ka + Kb); its doubly occupied "
        "enthalpy is Ha + Hb. This is an exact independent-site parameterization, not the earlier sequential "
        "test relabelled and not two one-site heats added together. FT-ITC evaluates TwoSetsOfSites, "
        "while all reference equilibria and injection heats are calculated by unmodified pytc.")
    limitations = (
        "The six passing external two-site cases validate one site of each type, not arbitrary independent fractional "
        "stoichiometries. The fractional-stoichiometry and monomer-dimer dissociation tests remain "
        "separate analytical FT-ITC extension tests; they are not native-pytc forward comparisons. "
        "Initial-ligand cases validate a prescribed segment start, not tandem inter-segment back-mixing. "
        "Agreement verifies the implementation for these cases, not empirical superiority of the mixing model. "
        "A forward test does not establish parameter identifiability or fitting robustness.")
    numerics = (
        "Different numerical solvers are retained. Native BindingPolynomial uses an absolute free-ligand "
        "Different numerical solvers are retained; the practical acceptance limit is 0.01% of peak injection heat. "
        "No tolerance was relaxed for these cases.")
    graphs = (
        "Blue lines are native integrated heats; orange open circles are FT-ITC predictions, one point per "
        "actual injection. The horizontal axis is nominal molar ratio (total titrant / initial cell macromolecule). "
        "Connecting lines are visual guides, not sub-injections. Lower panels show signed error "
        "as a percentage of the same dataset peak, with a zoomed vertical scale for each case. "
        "Use the overview and the printed maximum to compare with 0.01%; residual panel heights are not "
        "comparable across cases.")
    source_url = f"https://github.com/harmslab/pytc/tree/{reference['source']['commit']}/pytc/indiv_models"
    docs_url = "https://pytc.readthedocs.io/en/latest/indiv_models/binding-polynomial.html"
    audit_text = (
        f"A clean rebuild of pytc's native C extension reproduced all {audit['case_records_identical']} "
        "frozen case records and integrated-heat files byte-for-byte. The checkout was clean at the pinned "
        "revision and the loaded Python model sources matched it. The fresh extension hash and source hashes "
        "are recorded in native-audit.json. This confirms native-source replay; it does not independently "
        "prove the scientific parameter mapping.") if audit else "No native-source rebuild audit was supplied."
    boundaries = (
        "The locally authored reference-generation code selects protocols, maps parameters, converts units and "
        "writes files. The two-site mapping is shown above and checked against independent-site polynomial/enthalpy "
        "identities. Concentration updates come from native ITCModel._titrate_species. Equilibrium and shot heats "
        "come from BindingPolynomial.dQ and its bp_ext.dQ C extension. The returned native dQ array is frozen "
        "directly. FT-ITC generates only the comparison predictions via TwoSetsOfSites.Evaluate. The plotting "
        "script reads the two recorded arrays; it cannot generate a replacement model curve.")

    rows = [(c["id"], results[c["id"]]) for c in cases]
    md = ["# Native-pytc forward-model validation", "", f"Report date: {report_date}", "", summary, "",
          "## Scope and acceptance", "", methods, "", metric, "", "## Two-independent-site mapping", "", mapping, "",
          "## Reference-generation audit", "", boundaries, "", audit_text, "",
          "```python", "# Exact two-site input mapping; all equilibrium and injection calculations stay in pytc.",
          "model = pytc.indiv_models.BindingPolynomial(num_sites=2, **protocol)",
          "model.update_values(dict(beta1=ka+kb, beta2=ka*kb,",
          "                         dH1=(ka*ha+kb*hb)/(ka+kb), dH2=ha+hb,",
          "                         fx_competent=1.0, dilution_heat=0.0, dilution_intercept=0.0))",
          "heats = model.dQ", "```", "",
          "## Results", "", "![All-case error overview](figures/forward-comparison-overview.png)", "",
          "| Case | Injections | Maximum error (% of peak) | Result |",
          "|---|---:|---:|---|"]
    for case in cases:
        r = results[case["id"]]
        status = "PASS" if r["Passed"] else "FAILED"
        md.append(f"| {case['id']} | {len(case['injection_liters'])} | {100*r['ErrorFractionOfPeak']:.6g} | "
                  f"{status} |")
    md.extend(["", "## How to read the graphs", "", graphs, ""])
    for title, pair, image in sections:
        md.extend([f"### {title}: {' / '.join(c['id'] for c in pair)}", "",
                   f"![Heat overlays and signed errors]({image.relative_to(directory).as_posix()})", ""])
        md.extend(parameter_text(c)+"\n" for c in pair)
    md.extend(["## Limitations and numerical differences", "", limitations, "", numerics, "",
               "## Test-run evidence", ""])
    verification = []
    for name, run in [("Focused pytc suite", focused), ("Full shared-core suite", core)]:
        if run:
            c = run["result_counts"]
            line = f"{name}: {c['passed']} passed, {c['failed']} failed, {c['skipped']} skipped ({c['total']} total)."
            verification.append(line)
            md.append("- "+line)
    if core and core["failed_tests"]:
        verification.append("The full-suite failures below are reported separately from the native forward comparisons; "
                            "this report does not claim that the full core suite passed.")
        md.extend(["", verification[-1], ""]+["- "+name for name in core["failed_tests"]])
    md.extend(["", "## Provenance and reproduction", "", f"[Pinned native pytc source]({source_url}); "
               f"[binding-polynomial documentation]({docs_url}).", "",
               f"Reference SHA-256: `{sha(directory/'reference.json')}`", "",
               f"Comparison SHA-256: `{sha(directory/'comparisons.json')}`", "",
               "See README.md for generation/test commands. This report and all plots are regenerated from "
               "reference.json and the C#-exported comparisons.json; the report builder computes no equilibrium predictions.", ""])
    (directory/"REPORT.md").write_text("\n".join(md))

    styles = getSampleStyleSheet()
    styles.add(ParagraphStyle(name="TitleCustom", fontName="Helvetica-Bold", fontSize=25, leading=29,
                              textColor=colors.HexColor(INK), spaceAfter=14))
    styles.add(ParagraphStyle(name="Deck", fontSize=12, leading=17, textColor=colors.HexColor(TEAL), spaceAfter=15))
    styles.add(ParagraphStyle(name="BodyCustom", fontSize=10, leading=14, spaceAfter=10, alignment=TA_LEFT))
    styles.add(ParagraphStyle(name="SmallCustom", fontSize=8, leading=11, spaceAfter=8))
    styles.add(ParagraphStyle(name="TableCustom", fontSize=7.2, leading=9))
    for name in ("Heading1", "Heading2"):
        styles[name].textColor = colors.HexColor(INK)
    story = []
    def p(text, style="BodyCustom"):
        return Paragraph(escape(text), styles[style])
    def table(data, widths):
        result = Table([[p(str(cell), "TableCustom") for cell in row] for row in data], colWidths=widths, repeatRows=1)
        result.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#E8F0F5")),
                                    ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F5F8FA")]),
                                    ("VALIGN", (0, 0), (-1, -1), "TOP"),
                                    ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5)]))
        return result
    story.extend([p("Native-pytc\nforward-model validation", "TitleCustom"), p(f"FT-ITC Analysis | {report_date}", "SmallCustom"),
                  p(summary, "Deck"), p("What is being tested", "Heading2"), p(methods),
                  p("Acceptance", "Heading2"), p(metric), p("Two independent sites", "Heading2"), p(mapping),
                  p("Scope limits", "Heading2"), p(limitations), PageBreak()])
    story.extend([p("All-case comparison", "Heading1"), p("The dashed line is the fixed 0.01% acceptance limit. "
                  "Two-site cases are highlighted in teal. All errors use the dataset peak as their denominator."),
                  Image(str(figures/"forward-comparison-overview.png"), width=7.05*inch, height=7.05*8.2/10.5*inch),
                  PageBreak(), p("Results by case", "Heading1")])
    data = [["Native case", "Max error\n% of peak", "Result"]]
    data.extend([[case_id, f"{100*r['ErrorFractionOfPeak']:.6g}",
                  "PASS" if r["Passed"] else "FAILED"] for case_id, r in rows])
    story.extend([table(data, [260, 120, 100]), Spacer(1, 12), PageBreak()])
    for title, pair, image in sections:
        story.extend([p(title, "Heading1"), p(graphs, "SmallCustom"),
                      Image(str(image), width=7.05*inch, height=7.05*6.1/10.5*inch), Spacer(1, 13)])
        for case in pair:
            story.append(p(parameter_text(case), "SmallCustom"))
        story.append(PageBreak())
    story.extend([p("Reference-generation audit", "Heading1"), p(boundaries), p(audit_text),
                  p("Direct native calls", "Heading2"),
                  p("Reference: pytc.indiv_models.BindingPolynomial(num_sites=2, **protocol) -> "
                    "update_values(mapped_parameters) -> model.dQ -> native bp_ext.dQ."),
                  p("Comparison: import the frozen native protocol -> TwoSetsOfSites(data) -> "
                    "set the prescribed independent-site parameters -> model.Evaluate(injection, false)."),
                  p("No FT-ITC calculation is imported by the native generator. No custom equilibrium or "
                    "injection-heat solver was added to that generator. The inspect.getargspec alias only "
                    "adapts Python 3.11+ introspection and does not change numerical calculations."), PageBreak(),
                  p("Verification and provenance", "Heading1")])
    story.extend(p(line) for line in verification)
    if core:
        story.extend(p(name, "SmallCustom") for name in core["failed_tests"])
    story.extend([p("Reproducible evidence", "Heading2"),
                  p("The accompanying REPORT.md includes every graph and parameter set. reference.json contains native "
                    "protocols and heats; comparisons.json contains the actual FT-ITC arrays and per-case decisions. "
                    "The builder rejects mismatched hashes, missing cases and inconsistent metrics. README.md contains the commands."),
                  p(f"pytc-fitter 1.1.5 | commit {reference['source']['commit']}", "SmallCustom"),
                  Paragraph(f'<link href="{source_url}" color="{BLUE}">Pinned native source</link> | '
                            f'<link href="{docs_url}" color="{BLUE}">Binding-polynomial documentation</link>', styles["SmallCustom"]),
                  p(f"Reference SHA-256: {sha(directory/'reference.json')}", "SmallCustom"),
                  p(f"Comparisons SHA-256: {sha(directory/'comparisons.json')}", "SmallCustom")])
    pdf_output.parent.mkdir(parents=True, exist_ok=True)
    doc = SimpleDocTemplate(str(pdf_output), pagesize=(612, 792), rightMargin=45, leftMargin=45,
                            topMargin=42, bottomMargin=42, title="Native-pytc forward-model validation",
                            author="FT-ITC Analysis")
    def footer(canvas, document):
        canvas.setStrokeColor(colors.HexColor("#DCE4EA"))
        canvas.line(45, 32, 567, 32)
        canvas.setFont("Helvetica", 8)
        canvas.setFillColor(colors.HexColor("#596B7C"))
        canvas.drawString(45, 20, "FT-ITC | Native-pytc forward comparisons | No fitting")
        canvas.drawRightString(567, 20, str(document.page))
    doc.build(story, onFirstPage=footer, onLaterPages=footer)
    pages = PdfReader(pdf_output).pages
    if not all(page.extract_text().strip() for page in pages):
        raise ValueError("Empty PDF page detected.")
    evidence = dict(report_date=report_date, native_cases=len(cases), passed=passed,
                    reference_sha256=sha(directory/"reference.json"), comparisons_sha256=sha(directory/"comparisons.json"),
                    generator_sha256=sha(Path(__file__)), pdf_sha256=sha(pdf_output), pdf_pages=len(pages),
                    native_audit_sha256=sha(audit_path) if audit else None,
                    focused_test_run=focused, core_test_run=core)
    (directory/"report-evidence.json").write_text(json.dumps(evidence, indent=2)+"\n")
    print(summary)
    print(f"Wrote {len(pages)} PDF pages and {len(sections)+1} figures. All report metrics match the plotted arrays.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--pdf", type=Path, default=Path("output/pdf/pytc-forward-validation.pdf"))
    parser.add_argument("--date", default=date.today().isoformat())
    parser.add_argument("--core-trx", type=Path)
    parser.add_argument("--focused-trx", type=Path)
    args = parser.parse_args()
    generate(args.directory.resolve(), args.pdf.resolve(), args.date, args.core_trx, args.focused_trx)
