#!/usr/bin/env python3
"""Generate an analytic raw ITC reference using Python's standard library only.

This program does not import, execute, or copy a prediction from FT-ITC.
The binding reference solves ligand conservation by bisection, independently
of the application's closed-form one-site heat calculation. See README.md.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
CELL_M = 40e-6
SYRINGE_M = 500e-6
CELL_L = 200e-6
INJECTION_L = 2e-6
COUNT = 32
N = 1.1
KA = 10 ** 6.2
DH_J_PER_MOL = -32000.0
OFFSET_J_PER_MOL = 350.0
J_PER_CAL = 4.184


def bound_sites(cell_m, total_ligand_m):
    """Solve L_total = L_free + N*M_total*Ka*L_free/(1+Ka*L_free)."""
    low, high = 0.0, total_ligand_m
    for _ in range(120):
        ligand = (low + high) / 2.0
        bound = N * cell_m * KA * ligand / (1.0 + KA * ligand)
        if ligand + bound > total_ligand_m:
            high = ligand
        else:
            low = ligand
    ligand = (low + high) / 2.0
    return N * cell_m * KA * ligand / (1.0 + KA * ligand)


def generate():
    rows = []
    previous_bound = 0.0
    for i in range(COUNT):
        # Declared MicroCal untruncated cumulative-volume balance; no production call.
        ratio = (i + 1) * INJECTION_L / CELL_L
        cell = CELL_M * (1.0 - ratio / 2.0) / (1.0 + ratio / 2.0)
        ligand = SYRINGE_M * ratio / (1.0 + ratio / 2.0)
        bound = bound_sites(cell, ligand)
        # Binding heat = enthalpy * (change of bound moles in cell + bound
        # moles displaced). Displaced concentration uses the endpoint mean.
        reacted_mol = CELL_L * (bound - previous_bound) + INJECTION_L * (bound + previous_bound) / 2.0
        heat = DH_J_PER_MOL * reacted_mol + OFFSET_J_PER_MOL * SYRINGE_M * INJECTION_L
        rows.append(dict(id=i, time_seconds=40 + i * 80, volume_liters=INJECTION_L,
                         cell_molar=cell, ligand_molar=ligand,
                         implementation_ligand_molar=SYRINGE_M * ratio * (1.0 - ratio / 2.0),
                         heat_joules=heat))
        previous_bound = bound

    lines = ["$ITC", f"$ {COUNT}", "$NOT", "$ 25", "$ 40", "$ 750", "$ 10", "$ 1", "$ADCGainCode: 0", "$False,True,True"]
    lines += ["$ 2, 2, 80, 1"] * COUNT
    lines += ["# 0", "# 0.5", "# 0.04", "# 0.2", "?Analytic synthetic reference; not experimental measurements", "@0"]
    by_time = {row["time_seconds"]: row for row in rows}
    last_time = rows[-1]["time_seconds"] + 80
    for t in range(last_time + 1):
        if t in by_time:
            row = by_time[t]
            lines.append(f"@{row['id'] + 1},2,2,{t}")
        # Linear drift is defined independently of the baseline fitter.
        power = 3e-6 + 1e-9 * t
        for row in rows:
            age = t - row["time_seconds"]
            if 0 <= age <= 20:
                # Unit-area triangle: height 0.1/s, width 20 s. Its continuous
                # integral and 1 s right-endpoint sum are both exactly one.
                power += row["heat_joules"] * max(0.0, 1.0 - abs(age - 10) / 10.0) / 10.0
        lines.append(f"{t},{power / (J_PER_CAL * 1e-6):.12g},25")
    raw = ("\n".join(lines) + "\n").encode("utf-8")
    (ROOT / "analytic-one-site.itc").write_bytes(raw)
    reference = dict(description="Independently generated analytic synthetic thermogram; not laboratory data",
                     license="MIT (repository license)", raw_sha256=hashlib.sha256(raw).hexdigest(),
                     cell_molar=CELL_M, syringe_molar=SYRINGE_M, cell_liters=CELL_L,
                     dilution="MicroCal", baseline_watts=dict(intercept=3e-6, slope_per_second=1e-9),
                     integration_start_delay_seconds=0, integration_end_offset_seconds=22,
                     excluded_injection_ids=[0], expected_fit=dict(n=N, log10_ka=6.2, ka_per_molar=KA,
                     enthalpy_joules_per_mole=DH_J_PER_MOL, offset_joules_per_mole=OFFSET_J_PER_MOL),
                     implementation_fit_regression=dict(n=1.0993303, log10_ka=6.206575,
                     enthalpy_joules_per_mole=-32008.74, offset_joules_per_mole=346.10),
                     injections=rows)
    (ROOT / "reference.json").write_text(json.dumps(reference, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    generate()
