#!/usr/bin/env python3
"""Plot recorded forward heats and their signed residuals without fitting."""
import json
from pathlib import Path


def plot_curves(root, cases, references, predictions):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.ticker import ScalarFormatter
    import numpy as np

    by_id = {case["id"]: case for case in cases}
    panels = [("One site", ["one-c100-small"]),
              ("Two independent sites", ["fractional-low-capacity"]),
              ("Competitive binding", ["competitive-500"]),
              ("Sequential binding", ["sequential-2", "sequential-3", "sequential-4"])]
    colors = ["#2563a5", "#d27b26", "#55834a"]
    fig = plt.figure(figsize=(7.0, 6.5), layout="constrained")
    groups = fig.subfigures(2, 2, wspace=.04, hspace=.04)
    for group, (title, names) in zip(groups.flat, panels):
        ax, residual_ax = group.subplots(2, 1, sharex=True,
                                        gridspec_kw={"height_ratios": [3, 1]})
        largest_residual = 0
        for index, name in enumerate(names):
            case = by_id[name]
            if "counts" in case:
                x = (case["initial_ligand_molar"]*case["cell_microliters"] +
                     np.cumsum(case["shots_microliters"])*case["syringe_molar"])/(case["cell_molar"]*case["cell_microliters"])
            else:
                x = case["molar_ratio"]
            expected = np.array(references[name])
            actual = np.array(predictions[name])
            # Subtract in joules before converting to the plotted heat units.
            residual = (actual-expected)*1e6
            largest_residual = max(largest_residual, max(abs(residual)))
            label = f"{len(case['parameters']['logka'])} steps" if len(names)>1 else None
            ax.plot(x, expected*1e6, color=colors[index], linewidth=1.1, label=label)
            ax.plot(x, actual*1e6, "o", color=colors[index],
                    markersize=1.8 if "counts" in case else 2.5,
                    markerfacecolor="none", markeredgewidth=.7)
            residual_ax.plot(x, residual, "o", color=colors[index],
                             markersize=1.8, markeredgewidth=0)
        ax.set_title(title, fontsize=10)
        ax.set_ylabel("Injection heat (µJ)", fontsize=8)
        residual_ax.set_xlabel("Total ligand / initial macromolecule", fontsize=8)
        residual_ax.set_ylabel("Residual (µJ)", fontsize=8)
        residual_ax.axhline(0, color="#888888", linewidth=.5, zorder=0)
        residual_ax.set_ylim(-1.2*largest_residual, 1.2*largest_residual)
        formatter = ScalarFormatter(useMathText=True)
        formatter.set_powerlimits((0, 0))
        formatter.set_useOffset(False)
        residual_ax.yaxis.set_major_formatter(formatter)
        residual_ax.yaxis.get_offset_text().set_fontsize(8)
        residual_ax.locator_params(axis="y", nbins=3)
        for axis in (ax, residual_ax):
            axis.tick_params(labelsize=8)
            axis.spines[["top", "right"]].set_visible(False)
        if len(names)>1:
            ax.legend(frameon=False, fontsize=7)
    fig.savefig(root/"forward-curves.png", dpi=300)
    fig.savefig(root/"forward-curves.pdf")
    plt.close(fig)


if __name__ == "__main__":
    root = Path(__file__).resolve().parent
    cases = json.loads((root.parent/"PytcBookkeeping/reference.json").read_text())["cases"]
    cases += json.loads((root.parent/"TwoSiteStoichiometry/reference.json").read_text())["cases"]
    results = json.loads((root/"comparisons.json").read_text())["results"]
    assert len(results) == 52 and all(result["result"] == "PASS" for result in results)
    plot_curves(root, cases,
                {result["id"]: result["expected_heats_joules"] for result in results},
                {result["id"]: result["actual_heats_joules"] for result in results})
