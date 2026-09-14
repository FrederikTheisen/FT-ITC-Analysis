#!/usr/bin/env python3
"""Export tables and static figures from protocol-conformance test results."""

import argparse
import csv
import json
import math
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter


ORDER = [
    "one-site-exothermic", "two-independent-sites", "competitive-binding",
    "sequential-2", "sequential-3", "sequential-4", "dissociation",
]
LABELS = {
    "one-site-exothermic": "One set of sites",
    "two-independent-sites": "Two independent site sets",
    "competitive-binding": "Competitive binding",
    "sequential-2": "Sequential, 2 steps",
    "sequential-3": "Sequential, 3 steps",
    "sequential-4": "Sequential, 4 steps",
    "dissociation": "Dimer dissociation",
}


def read_results(directory: Path):
    results = []
    for identifier in ORDER:
        path = directory / f"{identifier}.json"
        if not path.exists():
            raise FileNotFoundError(f"Missing test result: {path}")
        results.append(json.loads(path.read_text(encoding="utf-8")))
    return results


def write_tables(results, output: Path):
    headers = ["FT-ITC model", "External equilibrium source", "Injections", "Maximum absolute error (J)", "Maximum relative error"]
    rows = [[LABELS[result["id"]], result["generator"], str(result["injection_count"]),
             f"{result['maximum_error_joules']:.3e}", f"{result['maximum_relative_error']:.3e}"]
            for result in results]
    markdown = ["# Protocol-conformance forward-model results", "",
                "Each reference uses the FT-ITC MicroCal injection protocol. Errors are FT-ITC prediction minus independently generated integrated heat.", "",
                "| " + " | ".join(headers) + " |",
                "|" + "|".join(["---"] * len(headers)) + "|"]
    markdown.extend("| " + " | ".join(row) + " |" for row in rows)
    markdown.append("")
    (output / "protocol-conformance-results.md").write_text("\n".join(markdown), encoding="utf-8")
    with (output / "protocol-conformance-results.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(headers)
        writer.writerows(rows)
    with (output / "protocol-conformance-injection-data.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["case", "model", "injection", "external_heat_joules", "ftitc_predicted_joules", "residual_joules"])
        for result in results:
            for row in result["rows"]:
                writer.writerow([result["id"], LABELS[result["id"]], row["injection"], row["observed_joules"],
                                 row["ftitc_predicted_joules"], row["residual_joules"]])


def save_all_formats(figure, output: Path, name: str):
    for extension in ("png", "svg", "pdf"):
        figure.savefig(output / f"{name}.{extension}", dpi=300 if extension == "png" else None,
                       bbox_inches="tight")


def make_curves_figure(results, output: Path):
    figure, axes = plt.subplots(4, 2, figsize=(10, 13), constrained_layout=True)
    axes = axes.ravel()
    for axis, result in zip(axes, results):
        rows = result["rows"]
        injection = [row["injection"] for row in rows]
        external = [row["observed_joules"] * 1e6 for row in rows]
        predicted = [row["ftitc_predicted_joules"] * 1e6 for row in rows]
        axis.plot(injection, external, color="#0072B2", marker="o", markersize=2.7,
                  markerfacecolor="white", linewidth=1.2, label="External reference")
        axis.plot(injection, predicted, color="#D55E00", linestyle="--", linewidth=1.2,
                  label="FT-ITC prediction")
        axis.set_title(LABELS[result["id"]], fontsize=10)
        axis.set_xlabel("Injection number")
        axis.set_ylabel("Integrated heat (µJ)")
        axis.grid(alpha=0.22, linewidth=0.6)
    axes[-1].axis("off")
    handles, labels = axes[0].get_legend_handles_labels()
    figure.legend(handles, labels, loc="lower center", ncol=2, frameon=False)
    figure.suptitle("Protocol-conformant forward heat predictions", fontsize=14)
    save_all_formats(figure, output, "protocol-conformance-curves")
    plt.close(figure)


def make_error_figure(results, output: Path):
    labels = [LABELS[result["id"]] for result in results]
    errors = [result["maximum_relative_error"] for result in results]
    figure, axis = plt.subplots(figsize=(8.5, 4.8), constrained_layout=True)
    y = list(range(len(results)))
    bars = axis.barh(y, errors, color="#009E73", height=0.62)
    axis.set_yticks(y, labels)
    axis.set_xscale("log")
    axis.set_xlabel("Maximum absolute error / largest heat")
    axis.set_title("Maximum forward-model discrepancy")
    axis.grid(axis="x", alpha=0.25, linewidth=0.7)
    axis.invert_yaxis()
    axis.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"10$^{{{int(round(math.log10(value)))}}}$"))
    for bar, error in zip(bars, errors):
        axis.text(error * 1.25, bar.get_y() + bar.get_height() / 2, f"{error:.2e}", va="center", fontsize=9)
    axis.set_xlim(min(errors) / 3, max(errors) * 20)
    save_all_formats(figure, output, "protocol-conformance-errors")
    plt.close(figure)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    results = read_results(args.results.resolve())
    args.output.mkdir(parents=True, exist_ok=True)
    write_tables(results, args.output)
    make_curves_figure(results, args.output)
    make_error_figure(results, args.output)
    print(f"Exported table and figures for {len(results)} protocol-conformance cases to {args.output}")


if __name__ == "__main__":
    main()
