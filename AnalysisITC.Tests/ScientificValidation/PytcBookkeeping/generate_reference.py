#!/usr/bin/env python3
"""Freeze native finite-shot pytc heats; no FT-ITC or replacement model code.

Only parameter/unit mappings and text serialization are performed locally.
There is no noise, background, shot subdivision, fitting or reference solver.
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

COMMIT = "d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac"
J_PER_CAL = 4.184


def generate(source, output):
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != COMMIT:
        raise RuntimeError(f"Expected {COMMIT}, found {revision}")
    subprocess.run(["git", "-C", str(source), "diff", "--exit-code", "HEAD"], check=True)
    # Python 3.11 compatibility only; the model reads .args and .defaults.
    if not hasattr(inspect, "getargspec"):
        inspect.getargspec = inspect.getfullargspec
    import numpy as np
    import pytc
    from pytc.indiv_models import bp_ext

    modules = ["base.py", "single_site.py", "single_site_competitor.py", "binding_polynomial.py"]
    for name in modules:
        if (Path(pytc.__file__).parent / "indiv_models" / name).read_bytes() != (source / "pytc/indiv_models" / name).read_bytes():
            raise RuntimeError(f"Installed pytc differs from the pinned source: {name}")

    cases = [
        dict(id="one-exothermic", model="one-site", cell=20e-6, syringe=250e-6, shots=[1.0]*40,
             n=[1.1], logka=[6.2], enthalpy=[-32000.0]),
        dict(id="one-endothermic", model="one-site", cell=20e-6, syringe=250e-6, shots=[1.0]*40,
             n=[1.1], logka=[6.2], enthalpy=[32000.0]),
        # Standard ITC c-value series: c = Ka * N * M_cell. These use
        # 10 uM cell, N=1, 100 uM syringe (10x cell): Kd = 1 uM, 100 nM, 10 nM.
        dict(id="one-c10-small", model="one-site", cell=10e-6, syringe=100e-6,
             shots=[1.5]*25, n=[1.0], c_value=10.0, logka=[6.0], enthalpy=[-25000.0]),
        dict(id="one-c100-small", model="one-site", cell=10e-6, syringe=100e-6,
             shots=[1.5]*25, n=[1.0], c_value=100.0, logka=[7.0], enthalpy=[-25000.0]),
        dict(id="one-c1000-small", model="one-site", cell=10e-6, syringe=100e-6,
             shots=[1.5]*25, n=[1.0], c_value=1000.0, logka=[8.0], enthalpy=[-25000.0]),
        dict(id="one-c10-large", model="one-site", cell=10e-6, syringe=100e-6,
             shots=[5.0]*12, n=[1.0], c_value=10.0, logka=[6.0], enthalpy=[-25000.0]),
        dict(id="one-c100-large", model="one-site", cell=10e-6, syringe=100e-6,
             shots=[5.0]*12, n=[1.0], c_value=100.0, logka=[7.0], enthalpy=[-25000.0]),
        dict(id="one-c1000-large", model="one-site", cell=10e-6, syringe=100e-6,
             shots=[5.0]*12, n=[1.0], c_value=1000.0, logka=[8.0], enthalpy=[-25000.0]),
        # Stoichiometry scan at c=100, with twice the standard syringe
        # concentration so that each N value remains a realistic titration.
        dict(id="one-n05", model="one-site", cell=10e-6, syringe=200e-6,
             shots=[1.5]*25, n=[0.5], logka=[7.0], enthalpy=[-25000.0]),
        dict(id="one-n1", model="one-site", cell=10e-6, syringe=200e-6,
             shots=[1.5]*25, n=[1.0], logka=[7.0], enthalpy=[-25000.0]),
        dict(id="one-n2", model="one-site", cell=10e-6, syringe=200e-6,
             shots=[1.5]*25, n=[2.0], logka=[7.0], enthalpy=[-25000.0]),
        # Realistic independent-site series: c1 = Ka1*N1*[cell] is 50, 500
        # or 2000, with Kd2/Kd1 = 10 or 100. N1=N2=1 and no initial ligand.
        dict(id="two-c50-r10", model="two-site", cell=20e-6, syringe=300e-6,
             shots=[1.5]*40, n=[1.0, 1.0], c_value=50.0, kd_ratio=10.0,
             logka=[math.log10(1/(400e-9)), math.log10(1/(4e-6))], enthalpy=[-25000.0, 14000.0]),
        dict(id="two-c500-r10", model="two-site", cell=20e-6, syringe=300e-6,
             shots=[1.5]*40, n=[1.0, 1.0], c_value=500.0, kd_ratio=10.0,
             logka=[math.log10(1/(40e-9)), math.log10(1/(400e-9))], enthalpy=[-25000.0, 14000.0]),
        dict(id="two-c2000-r10", model="two-site", cell=20e-6, syringe=300e-6,
             shots=[1.5]*40, n=[1.0, 1.0], c_value=2000.0, kd_ratio=10.0,
             logka=[8.0, 7.0], enthalpy=[-25000.0, 14000.0], acceptance="diagnostic"),
        dict(id="two-c50-r100", model="two-site", cell=20e-6, syringe=300e-6,
             shots=[1.5]*40, n=[1.0, 1.0], c_value=50.0, kd_ratio=100.0,
             logka=[math.log10(1/(400e-9)), math.log10(1/(40e-6))], enthalpy=[-25000.0, 14000.0]),
        dict(id="two-c500-r100", model="two-site", cell=20e-6, syringe=300e-6,
             shots=[1.5]*40, n=[1.0, 1.0], c_value=500.0, kd_ratio=100.0,
             logka=[math.log10(1/(40e-9)), math.log10(1/(4e-6))], enthalpy=[-25000.0, 14000.0]),
        dict(id="two-c2000-r100", model="two-site", cell=20e-6, syringe=300e-6,
             shots=[1.5]*40, n=[1.0, 1.0], c_value=2000.0, kd_ratio=100.0,
             logka=[8.0, 6.0], enthalpy=[-25000.0, 14000.0]),
        dict(id="two-realistic", model="two-site", cell=30e-6, syringe=500e-6,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(50e-12)), 8.0],
             enthalpy=[-20000.0, -50000.0], acceptance="diagnostic"),
        # Affinity-scaled repeats keep the protocol fixed while testing whether
        # the tight two-site discrepancy is tied to the absolute Kd values.
        dict(id="two-realistic-500pM", model="two-site", cell=30e-6, syringe=500e-6,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(500e-12)), 7.0],
             enthalpy=[-20000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-realistic-5nM", model="two-site", cell=30e-6, syringe=500e-6,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(5e-9)), 6.0],
             enthalpy=[-20000.0, -50000.0], acceptance="diagnostic"),
        # Tight-binding case scaled in both Kd and concentrations.
        dict(id="two-tight-scale-2x", model="two-site", cell=60e-6, syringe=1e-3,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(100e-12)), math.log10(1/(20e-9))],
             enthalpy=[-20000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-tight-scale-10x", model="two-site", cell=300e-6, syringe=5e-3,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(500e-12)), math.log10(1/(100e-9))],
             enthalpy=[-20000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-tight-scale-100x", model="two-site", cell=3e-3, syringe=50e-3,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(5e-9)), math.log10(1/(1e-6))],
             enthalpy=[-20000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-kd50-50", model="two-site", cell=30e-6, syringe=500e-6,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(50e-9)), math.log10(1/(50e-9))],
             enthalpy=[-25000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-kd50-500", model="two-site", cell=30e-6, syringe=500e-6,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(50e-9)), math.log10(1/(500e-9))],
             enthalpy=[-25000.0, -50000.0]),
        dict(id="two-kd50-5000", model="two-site", cell=30e-6, syringe=500e-6,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(50e-9)), math.log10(1/(5000e-9))],
             enthalpy=[-25000.0, -50000.0]),
        # Tenfold concentration/affinity scale of the preceding Kd2 scan.
        dict(id="two-kd500-500", model="two-site", cell=300e-6, syringe=5e-3,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(500e-9)), math.log10(1/(500e-9))],
             enthalpy=[-25000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-kd500-5000", model="two-site", cell=300e-6, syringe=5e-3,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(500e-9)), math.log10(1/(5000e-9))],
             enthalpy=[-25000.0, -50000.0], acceptance="diagnostic"),
        dict(id="two-kd500-50000", model="two-site", cell=300e-6, syringe=5e-3,
             shots=[1.5]*25, n=[1.0, 1.0], logka=[math.log10(1/(500e-9)), math.log10(1/(50000e-9))],
             enthalpy=[-25000.0, -50000.0], acceptance="diagnostic"),
    ]
    # Realistic competitive-binding series: 100 uM cell, 500 uM syringe,
    # target Kd = 5 nM / dH = -60 kJ/mol, competitor Kd = 1 uM /
    # dH = -30 kJ/mol, with no initial ligand.
    for concentration, label in ((0.0, "0"), (500e-6, "500"), (1e-3, "1000")):
        cases.append(dict(id=f"competitive-{label}", model="competitive",
                          cell=80e-6, syringe=500e-6, shots=[1.5]*40,
                          n=[1.0], logka=[math.log10(1/(5e-9))], enthalpy=[-60000.0],
                          competitor=dict(concentration=concentration, logka=6.0, enthalpy=-30000.0)))
    sequential_parameters = {
        # Realistic affinity ladders expressed as rounded Kd values.
        2: ([8.0, 6.0], [-25000.0, 12000.0]),
        3: ([8.0, 7.0, 6.0],
            [-25000.0, 14000.0, -9000.0]),
        4: ([8.0, 7.0, 6.0, 5.0], [-25000.0, 14000.0, -9000.0, 6000.0]),
    }
    for count in (2, 3, 4):
        for initial in (0.0,):
            logka, enthalpy = sequential_parameters[count]
            cases.append(dict(id=f"sequential-{count}" + ("-segment-start" if initial else ""),
                              model="sequential", cell=10e-6, initial_ligand=initial, syringe=150e-6,
                              shots=[1.0]*60, n=[],
                              logka=logka, enthalpy=enthalpy,
                              acceptance="diagnostic" if not initial else "required"))
        low_logka = {2: [7.0, 5.0], 3: [7.0, 6.0, 5.0], 4: [7.0, 6.0, 5.0, 4.0]}[count]
        cases.append(dict(id=f"sequential-{count}-low-affinity", model="sequential",
                          cell=10e-6, initial_ligand=0.0, syringe=150e-6,
                          shots=[1.0]*60, n=[], logka=low_logka,
                          enthalpy=enthalpy, acceptance="required"))

    output.mkdir(parents=True, exist_ok=True)
    references = []
    for case in cases:
        protocol = dict(S_cell=case["cell"], T_cell=case.get("initial_ligand", 0.0),
                        T_syringe=case["syringe"], cell_volume=200.0, shot_volumes=case["shots"])
        constants = [10**value for value in case["logka"]]
        enthalpies = [value/J_PER_CAL for value in case["enthalpy"]]
        if case["model"] == "one-site":
            model = pytc.indiv_models.SingleSite(**protocol)
            params = dict(K=constants[0], dH=enthalpies[0], fx_competent=case["n"][0])
        elif case["model"] == "competitive":
            competitor = case["competitor"]
            model = pytc.indiv_models.SingleSiteCompetitor(C_cell=competitor["concentration"], **protocol)
            params = dict(K=constants[0], dH=enthalpies[0], fx_competent=case["n"][0],
                          Kcompetitor=10**competitor["logka"], dHcompetitor=competitor["enthalpy"]/J_PER_CAL)
        elif case["model"] == "two-site":
            # Exact independent-site parameter mapping, not a replacement model:
            # P = (1 + Ka*L)(1 + Kb*L). The singly occupied ensemble has
            # Ka/(Ka+Kb) and Kb/(Ka+Kb) fractions; the doubly occupied
            # state's enthalpy is Ha+Hb. All equilibrium and shot calculations
            # below are performed by the unmodified native BindingPolynomial.
            if case["n"] != [1.0, 1.0]:
                raise ValueError("This mapping validates one site of each type only.")
            ka, kb = constants
            ha, hb = enthalpies
            model = pytc.indiv_models.BindingPolynomial(num_sites=2, **protocol)
            params = dict(beta1=ka+kb, beta2=ka*kb,
                          dH1=(ka*ha+kb*hb)/(ka+kb), dH2=ha+hb, fx_competent=1.0)
        elif case["model"] == "sequential":
            model = pytc.indiv_models.BindingPolynomial(num_sites=len(constants), **protocol)
            params = {f"beta{i+1}": math.prod(constants[:i+1]) for i in range(len(constants))}
            params.update({f"dH{i+1}": sum(enthalpies[:i+1]) for i in range(len(constants))})
            params["fx_competent"] = 1.0
        else:
            raise ValueError(f"Unsupported reference model: {case['model']}")
        params.update(dilution_heat=0.0, dilution_intercept=0.0)
        model.update_values(params)
        heats = np.asarray(model.dQ, dtype=float)
        if len(heats) != len(case["shots"]) or not np.isfinite(heats).all():
            raise RuntimeError(f"Incomplete/nonfinite native output: {case['id']}")
        lines = ["10", f"0,{len(heats)},0,0,0", f"25,{case['cell']*1000:.17g},{case['syringe']*1000:.17g},0.2,0", "0", "0"]
        lines.extend(f"{volume:.17g},{heat:.17g}" for volume, heat in zip(case["shots"], heats))
        payload = ("\n".join(lines)+"\n").encode()
        filename = case["id"]+".DH"
        (output/filename).write_bytes(payload)
        references.append(dict(id=case["id"], model=case["model"], upstream_model=type(model).__name__,
                               file=filename, sha256=hashlib.sha256(payload).hexdigest(),
                               cell_liters=200e-6, cell_molar=case["cell"], syringe_molar=case["syringe"],
                               initial_ligand_molar=protocol["T_cell"],
                               injection_liters=[v*1e-6 for v in case["shots"]],
                               cell_concentrations_molar=model._S_conc.tolist(),
                               titrant_concentrations_molar=model._T_conc.tolist(),
                               molar_ratio=((case.get("initial_ligand", 0.0) * 200e-6
                                             + np.cumsum(np.asarray(case["shots"]) * 1e-6 * case["syringe"])
                                            ) / (case["cell"] * 200e-6)).tolist(),
                               heats_joules=(heats*J_PER_CAL*1e-6).tolist(), upstream_parameters=params,
                               parameters=dict(n=case["n"], logka=case["logka"], enthalpy=case["enthalpy"], offset=0),
                               c_value=case.get("c_value"),
                               kd_ratio=case.get("kd_ratio"),
                               acceptance=case.get("acceptance", "required"),
                               competitor=case.get("competitor")))
        if case["id"] == "one-c10-small":
            # A second native-output serialization exercises trajectory-based metadata inference.
            table = ["DH;INJV;Xt;Mt;Xmt"]
            table.extend(f"{heat:.17g};{v:.17g};{l*1000:.17g};{s*1000:.17g};0"
                         for heat, v, l, s in zip(heats, case["shots"], model._T_conc[:-1], model._S_conc[:-1]))
            table.append(f";;{model._T_conc[-1]*1000:.17g};{model._S_conc[-1]*1000:.17g};0")
            table_payload = ("\n".join(table)+"\n").encode()
            (output/"native-trajectory.dat").write_bytes(table_payload)
            references[-1]["trajectory_file"] = "native-trajectory.dat"
            references[-1]["trajectory_sha256"] = hashlib.sha256(table_payload).hexdigest()

    paths = [f"pytc/indiv_models/{name}" for name in modules] + ["src/_bp_ext.c", "src/binding_polynomial.c", "src/binding_polynomial.h", "LICENSE"]
    manifest = dict(schema_version=1, generator_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    source=dict(name="pytc-fitter", version=importlib.metadata.version("pytc-fitter"),
                                repository="https://github.com/harmslab/pytc", commit=revision,
                                source_sha256={p: hashlib.sha256((source/p).read_bytes()).hexdigest() for p in paths},
                                compiled_extension_sha256=hashlib.sha256(Path(bp_ext.__file__).read_bytes()).hexdigest()),
                    environment=dict(python=platform.python_version(), packages={p: importlib.metadata.version(p) for p in ("numpy", "scipy")}),
                    protocol="Unmodified native finite shots; no subdivision, noise, background or fitting.",
                    units="Concentrations M; native volumes uL and enthalpies cal/mol; native dQ microcalories converted to J.",
                    acceptance=dict(maximum_error_fraction_of_peak=0.0001,
                                    rule="User-selected practical limit: 0.01% of peak injection heat.",
                                    epsilon=2.220446049250313e-16, roundoff_multiplier=512,
                                    diagnostic_goal="abs(actual-expected) <= 512*epsilon*(peak_abs_heat + abs(expected)); theoretical goal, not a release requirement"),
                    cases=references)
    (output/"reference.json").write_text(json.dumps(manifest, indent=2, allow_nan=False)+"\n")
    print(f"Generated {len(references)} unmodified native-pytc finite-injection cases.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pytc-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    generate(args.pytc_source.resolve(), args.output.resolve())
