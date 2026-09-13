#!/usr/bin/env python3
"""Freeze unmodified pytc predictions as integrated injection heats.

No FT-ITC assembly, model, baseline, or integration routine is called here.
The only numerical transformations are documented parameter/unit mappings
and summation of upstream subshots in the explicit refinement comparisons.
"""

import argparse
import hashlib
import importlib.metadata
import inspect
import json
import math
from pathlib import Path
import platform
import subprocess


PYTC_COMMIT = "d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac"
CALORIE_JOULES = 4.184


def generate(source, output):
    revision = subprocess.check_output(
        ["git", "-C", str(source), "rev-parse", "HEAD"], text=True
    ).strip()
    if revision != PYTC_COMMIT:
        raise RuntimeError(f"Expected pytc {PYTC_COMMIT}; found {revision}")
    subprocess.run(["git", "-C", str(source), "diff", "--exit-code", "HEAD"], check=True)

    # Python 3.11 removed getargspec. pytc only reads .args and .defaults;
    # getfullargspec supplies the same fields. This changes no model arithmetic.
    if not hasattr(inspect, "getargspec"):
        inspect.getargspec = inspect.getfullargspec

    import numpy as np
    import pytc

    for relative in (
        "indiv_models/base.py", "indiv_models/single_site.py",
        "indiv_models/single_site_competitor.py", "indiv_models/binding_polynomial.py",
    ):
        installed = Path(pytc.__file__).parent / relative
        if installed.read_bytes() != (source / "pytc" / relative).read_bytes():
            raise RuntimeError(f"Installed pytc differs from pinned source: {relative}")

    cases = [
        dict(id="one-site-exothermic", model="OneSetOfSites", cell=20e-6, syringe=500e-6,
             count=40, n=[1.1], logka=[6.2], enthalpy=[-32000.0]),
        dict(id="one-site-endothermic", model="OneSetOfSites", cell=40e-6, syringe=2e-3,
             count=40, n=[0.85], logka=[5.2], enthalpy=[24000.0]),
        dict(id="two-independent-sites", model="TwoSetsOfSites", cell=20e-6, syringe=2e-3,
             count=60, n=[1.0, 1.0], logka=[6.4, 5.0], enthalpy=[-25000.0, 15000.0]),
        dict(id="competitive", model="CompetitiveBinding", cell=20e-6, syringe=500e-6,
             count=40, n=[1.15], logka=[7.0], enthalpy=[-18000.0],
             competitor=dict(concentration_molar=30e-6, logka=6.2, enthalpy_j_per_mol=-8000.0)),
    ]
    for steps in (2, 3, 4):
        cases.append(dict(id=f"sequential-{steps}", model="SequentialBindingSites",
                          cell=20e-6, syringe=2e-3, count=60,
                          n=[], logka=[6.4, 5.7, 5.0, 4.3][:steps],
                          enthalpy=[-30000.0, 20000.0, -18000.0, 14000.0][:steps]))

    # Preserve the original protocol and its results. For the two higher-step
    # models, additionally test convergence of the external injection
    # discretization: sum 10 or 100 unmodified upstream subshot heats into
    # each original 1 uL injection. Concentrations, total delivered volume,
    # thermodynamic parameters and acceptance thresholds are unchanged.
    for original in tuple(cases):
        if original["id"] in ("sequential-3", "sequential-4"):
            for subdivisions in (10, 100):
                cases.append(dict(original, id=f"{original['id']}-subshots-{subdivisions}",
                                  subdivisions=subdivisions))

    output.mkdir(parents=True, exist_ok=True)
    references = []
    for case in cases:
        subdivisions = case.get("subdivisions", 1)
        protocol = dict(S_cell=case["cell"], T_syringe=case["syringe"],
                        cell_volume=200.0,
                        shot_volumes=[1.0 / subdivisions] * (case["count"] * subdivisions))
        constants = [10.0 ** value for value in case["logka"]]
        enthalpies = [value / CALORIE_JOULES for value in case["enthalpy"]]
        if case["model"] == "OneSetOfSites":
            model = pytc.indiv_models.SingleSite(**protocol)
            parameters = dict(K=constants[0], dH=enthalpies[0], fx_competent=case["n"][0])
        elif case["model"] == "CompetitiveBinding":
            competitor = case["competitor"]
            model = pytc.indiv_models.SingleSiteCompetitor(
                C_cell=competitor["concentration_molar"], **protocol)
            parameters = dict(K=constants[0], dH=enthalpies[0], fx_competent=case["n"][0],
                              Kcompetitor=10.0 ** competitor["logka"],
                              dHcompetitor=competitor["enthalpy_j_per_mol"] / CALORIE_JOULES)
        else:
            model = pytc.indiv_models.BindingPolynomial(num_sites=len(constants), **protocol)
            if case["model"] == "TwoSetsOfSites":
                # Two independent sites, one of each type per macromolecule.
                # P=(1+K1*l)(1+K2*l). The singly occupied state is a
                # population-weighted mixture; the doubly occupied heat is H1+H2.
                parameters = dict(beta1=sum(constants), beta2=math.prod(constants),
                                  dH1=sum(k*h for k, h in zip(constants, enthalpies)) / sum(constants),
                                  dH2=sum(enthalpies), fx_competent=1.0)
            else:
                # pytc uses cumulative Adair constants and state enthalpies;
                # FT-ITC uses step constants and step enthalpies.
                parameters = {f"beta{i+1}": math.prod(constants[:i+1]) for i in range(len(constants))}
                parameters.update({f"dH{i+1}": sum(enthalpies[:i+1]) for i in range(len(constants))})
                parameters["fx_competent"] = 1.0
        parameters.update(dilution_heat=0.0, dilution_intercept=0.0)
        model.update_values(parameters)
        # Volumes are uL and dH is cal/mol, so pytc.dQ is microcalories.
        upstream_heats_microcal = np.asarray(model.dQ, dtype=float)
        heats_microcal = upstream_heats_microcal.reshape(case["count"], subdivisions).sum(axis=1)
        if len(heats_microcal) != case["count"] or not np.isfinite(heats_microcal).all():
            raise RuntimeError(f"Non-finite/incomplete upstream prediction: {case['id']}")
        lines = ["10", f"0,{case['count']},0,0,0",
                 f"25,{case['cell'] * 1000:.17g},{case['syringe'] * 1000:.17g},0.2,0",
                 "0", "0"]
        lines.extend(f"1,{heat:.17g}" for heat in heats_microcal)
        filename = case["id"] + ".DH"
        data = ("\n".join(lines) + "\n").encode("utf-8")
        (output / filename).write_bytes(data)
        references.append(dict(
            id=case["id"], model=case["model"], upstream_model=type(model).__name__,
            file=filename, sha256=hashlib.sha256(data).hexdigest(),
            temperature_celsius=25.0, cell_liters=200e-6,
            cell_molar=case["cell"], syringe_molar=case["syringe"],
            injection_liters=[1e-6] * case["count"],
            heats_joules=(heats_microcal * CALORIE_JOULES * 1e-6).tolist(),
            upstream_parameters=parameters,
            upstream_subshots_per_injection=subdivisions,
            expected=dict(n=case["n"], logka=case["logka"], enthalpy_j_per_mol=case["enthalpy"],
                          offset_j_per_mol=0.0),
            competitor=case.get("competitor"),
        ))

    source_paths = ["pytc/indiv_models/base.py", "pytc/indiv_models/single_site.py",
                    "pytc/indiv_models/single_site_competitor.py", "pytc/indiv_models/binding_polynomial.py",
                    "src/_bp_ext.c", "src/binding_polynomial.c", "src/binding_polynomial.h", "LICENSE"]
    manifest = dict(
        schema_version=1,
        generator_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        source=dict(name="pytc-fitter", version=importlib.metadata.version("pytc-fitter"),
                    repository="https://github.com/harmslab/pytc", commit=revision,
                    source_sha256={p: hashlib.sha256((source / p).read_bytes()).hexdigest() for p in source_paths}),
        environment=dict(python=platform.python_version(),
                         packages={p: importlib.metadata.version(p) for p in ("numpy", "scipy")}),
        generation=dict(noise="none", heat_units="J", file_heat_units="microcalories",
                        concentration_convention="pytc product of (1 - injection_volume / cell_volume)",
                        heat_convention="pytc V * post-injection active cell concentration * change in mean bound-state enthalpy",
                        subshot_aggregation="For explicit 10/100-subshot companion cases, sum native pytc subshot heats into each original 1 uL injection. No other heat correction."),
        acceptance=dict(forward_max_error_fraction_of_peak_heat=0.02,
                        fitted_n_relative=0.02, fitted_ka_relative=0.02,
                        fitted_enthalpy_relative=0.02, fitted_offset_absolute_j_per_mol=50.0),
        cases=references,
    )
    (output / "reference.json").write_text(json.dumps(manifest, indent=2, allow_nan=False) + "\n")
    print(f"Generated {len(references)} integrated-heat fixtures from unmodified pytc {revision}.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pytc-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    generate(args.pytc_source.resolve(), args.output.resolve())
