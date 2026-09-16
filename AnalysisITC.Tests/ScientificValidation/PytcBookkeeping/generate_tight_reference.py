#!/usr/bin/env python3
"""Generate tighter-tolerance native pytc curves for currently failed cases."""
import inspect
import json
import math
from pathlib import Path

import numpy as np

if not hasattr(inspect, "getargspec"):
    inspect.getargspec = inspect.getfullargspec
import pytc

ROOT = Path(__file__).resolve().parent
reference = json.loads((ROOT / "reference.json").read_text())
comparisons = json.loads((ROOT / "comparisons.json").read_text())
failed = {r["Id"] for r in comparisons["Results"] if not r["Passed"]}
output = {"xtol_molar": 2e-14, "cases": {}}

for case in reference["cases"]:
    if case["id"] not in failed:
        continue
    p = case["parameters"]
    constants = [10**value for value in p["logka"]]
    enthalpies = [value / 4.184 for value in p["enthalpy"]]
    protocol = dict(S_cell=case["cell_molar"], S_syringe=0.0,
                    T_cell=case["initial_ligand_molar"], T_syringe=case["syringe_molar"],
                    cell_volume=200.0,
                    shot_volumes=[v * 1e6 for v in case["injection_liters"]])
    if case["model"] == "two-site":
        ka, kb = constants
        ha, hb = enthalpies
        model = pytc.indiv_models.BindingPolynomial(num_sites=2, **protocol)
        values = dict(beta1=ka + kb, beta2=ka * kb,
                      dH1=(ka * ha + kb * hb) / (ka + kb), dH2=ha + hb,
                      fx_competent=1.0, dilution_heat=0.0, dilution_intercept=0.0)
    elif case["model"] == "sequential":
        model = pytc.indiv_models.BindingPolynomial(num_sites=len(constants), **protocol)
        values = {f"beta{i+1}": math.prod(constants[:i+1]) for i in range(len(constants))}
        values.update({f"dH{i+1}": sum(enthalpies[:i+1]) for i in range(len(constants))})
        values.update(fx_competent=1.0, dilution_heat=0.0, dilution_intercept=0.0)
    else:
        raise ValueError(case["model"])
    model.update_values(values)
    output["cases"][case["id"]] = {
        "heats_joules": (np.asarray(model.dQ) * 4.184e-6).tolist(),
    }

(ROOT / "tight-reference.json").write_text(json.dumps(output, indent=2) + "\n")
print(f"Wrote tighter native curves for {len(output['cases'])} failed cases.")
