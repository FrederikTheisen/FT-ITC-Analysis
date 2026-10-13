#!/usr/bin/env python3
"""Freeze unequal-stoichiometry heats from pinned, unmodified native pytc.

Only exact input reparameterization and output serialization live here.
The external package performs all concentrations, equilibria and finite shots.
"""
import argparse
import hashlib
import importlib.metadata
import inspect
import itertools
import json
import math
from pathlib import Path
import platform
import subprocess

COMMIT = "d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac"


def polynomial_parameters(counts, factor, constants, enthalpies):
    # Enumerate microscopic states to obtain each occupancy ensemble's
    # coefficient and affinity-weighted enthalpy. No equilibrium is solved here.
    sites = [i for i, count in enumerate(counts) for _ in range(count)]
    result = dict(fx_competent=factor, dilution_heat=0.0, dilution_intercept=0.0)
    for occupied in range(1, len(sites) + 1):
        states = list(itertools.combinations(range(len(sites)), occupied))
        weights = [math.prod(constants[sites[i]] for i in state) for state in states]
        result[f"beta{occupied}"] = sum(weights)
        result[f"dH{occupied}"] = sum(w * sum(enthalpies[sites[i]] for i in state)
                                        for w, state in zip(weights, states)) / sum(weights)
    return result


def cases():
    def case(name, counts, factor, cell, syringe, kd, h, volume=200, initial=0, shots=None):
        return dict(id=name, counts=counts, factor=factor, n=[factor*c for c in counts],
                    cell_molar=cell*1e-6, syringe_molar=syringe*1e-6,
                    kd_molar=[k*1e-6 for k in kd], enthalpy=h, cell_microliters=volume,
                    initial_ligand_molar=initial*1e-6,
                    shots_microliters=shots or [0.5]+[2.0]*29)
    variable = [0.4]+[1.2, 2.8, 1.6, 2.4, 2.0]*6
    return [
        case("fractional-low-capacity", [1, 2], .7, 12, 260, [.35, 8.5], [-28500, -15700]),
        case("fractional-high-capacity", [2, 3], .6, 24, 700, [.8, 14], [-34000, -19000]),
        case("unequal-integer-capacity", [2, 3], 1, 16, 680, [1.3, 18], [-22500, -41000]),
        case("small-fractional-capacity", [3, 2], .15, 35, 350, [.55, 6.7], [-31200, -16400]),
        case("opposing-enthalpies", [2, 3], .45, 18, 480, [.6, 5.2], [-37000, 18500]),
        case("both-endothermic", [2, 1], .8, 28, 650, [1.1, 9.3], [21000, 33500]),
        case("similar-affinities", [1, 2], .55, 21, 420, [2.4, 2.7], [-26300, -17300]),
        case("equal-affinities-cancellation", [1, 2], .7, 19, 430, [1.7, 1.7], [-30000, 14000]),
        case("tight-minority-site", [1, 2], .35, 9, 180, [.0025, 1.8], [-42000, -20500]),
        case("weak-binding", [2, 1], .65, 45, 1600, [22, 95], [-19500, -33000]),
        case("variable-shots", [2, 3], .6, 24, 700, [.8, 14], [-34000, -19000], shots=variable),
        case("preloaded-ligand", [1, 2], .7, 12, 260, [.35, 8.5], [-28500, -15700], initial=9, shots=variable),
        case("large-cell", [2, 1], .8, 17, 400, [.7, 11], [-29800, 12300], volume=1400,
             shots=[2]+[12]*29),
        case("reversed-site-order", [3, 2], .6, 24, 700, [14, .8], [-19000, -34000]),
    ]


def generate(source, output):
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != COMMIT:
        raise ValueError(f"Expected {COMMIT}, found {revision}")
    subprocess.run(["git", "-C", str(source), "diff", "--exit-code", "HEAD"], check=True)
    if not hasattr(inspect, "getargspec"):
        inspect.getargspec = inspect.getfullargspec
    import numpy as np
    import pytc
    from pytc.indiv_models import bp_ext
    modules = ["base.py", "binding_polynomial.py"]
    for name in modules:
        if (Path(pytc.__file__).parent/"indiv_models"/name).read_bytes() != (source/"pytc/indiv_models"/name).read_bytes():
            raise ValueError(f"Installed source differs: {name}")
    output.mkdir(parents=True, exist_ok=True)
    records = []
    for case in cases():
        constants = [1/k for k in case["kd_molar"]]
        native = polynomial_parameters(case["counts"], case["factor"], constants,
                                       [h/4.184 for h in case["enthalpy"]])
        model = pytc.indiv_models.BindingPolynomial(num_sites=sum(case["counts"]),
            S_cell=case["cell_molar"], T_cell=case["initial_ligand_molar"],
            T_syringe=case["syringe_molar"], cell_volume=case["cell_microliters"],
            shot_volumes=case["shots_microliters"])
        model.update_values(native)
        heats = np.asarray(model.dQ)
        if len(heats) != len(case["shots_microliters"]) or not np.isfinite(heats).all():
            raise ValueError(f"Invalid native curve: {case['id']}")
        lines = ["10", f"0,{len(heats)},0,0,0",
                 f"25,{case['cell_molar']*1000:.17g},{case['syringe_molar']*1000:.17g},{case['cell_microliters']/1000:.17g},0", "0", "0"]
        lines += [f"{v:.17g},{q:.17g}" for v, q in zip(case["shots_microliters"], heats)]
        payload = ("\n".join(lines)+"\n").encode()
        filename = case["id"]+".DH"
        (output/filename).write_bytes(payload)
        records.append(dict(case, file=filename, sha256=hashlib.sha256(payload).hexdigest(),
            upstream_model="BindingPolynomial", upstream_parameters=native,
            logka=[math.log10(k) for k in constants], heats_joules=(heats*4.184e-6).tolist(),
            cell_concentrations_molar=model._S_conc.tolist(),
            titrant_concentrations_molar=model._T_conc.tolist()))
    paths = [f"pytc/indiv_models/{n}" for n in modules]+["src/_bp_ext.c", "src/binding_polynomial.h", "LICENSE"]
    manifest = dict(source=dict(repository="https://github.com/harmslab/pytc", commit=revision,
        source_sha256={p: hashlib.sha256((source/p).read_bytes()).hexdigest() for p in paths},
        compiled_extension_sha256=hashlib.sha256(Path(bp_ext.__file__).read_bytes()).hexdigest()),
        generator_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        environment=dict(python=platform.python_version(), packages={p: importlib.metadata.version(p)
            for p in ["pytc-fitter", "numpy", "scipy"]}),
        protocol="Unmodified native pytc concentrations and finite-shot heats; zero background, no fitting or subdivision.",
        acceptance="abs(error_i) <= 512*double_epsilon*(peak_abs_native_heat + abs(native_heat_i))",
        cases=records)
    (output/"reference.json").write_text(json.dumps(manifest, indent=2)+"\n")
    print(f"Wrote {len(records)} native unequal-stoichiometry two-site curves.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--pytc-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    generate(args.pytc_source.resolve(), args.output.resolve())
