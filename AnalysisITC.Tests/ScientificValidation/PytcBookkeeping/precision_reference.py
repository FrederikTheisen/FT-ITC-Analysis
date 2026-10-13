#!/usr/bin/env python3
"""Evaluate pinned upstream models with converged arithmetic; equations unchanged."""
import argparse
import hashlib
import importlib
import importlib.metadata
import inspect
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def evaluate(reference, stoichiometry_reference, digits):
    if not hasattr(inspect, "getargspec"):
        inspect.getargspec = inspect.getfullargspec
    import numpy as np
    import mpmath as mp
    import pytc
    mp.mp.dps = digits

    class Arithmetic:
        # Object arrays execute the upstream vector expressions in mpmath.
        # No binding equation or concentration update is implemented here.
        def array(self, values):
            return np.array([mp.mpf(v) for v in values], dtype=object)
        def zeros(self, count):
            return np.array([mp.mpf(0)] * count, dtype=object)
        def prod(self, values):
            return mp.fprod(values)
        def sqrt(self, values):
            return np.vectorize(mp.sqrt, otypes=[object])(values)
        def arccos(self, values):
            return np.vectorize(mp.acos, otypes=[object])(values)
        def cos(self, values):
            return np.vectorize(mp.cos, otypes=[object])(values)

    curves = {}
    cases = json.loads(reference.read_text())["cases"] + json.loads(stoichiometry_reference.read_text())["cases"]
    for case in cases:
        unequal = "counts" in case
        protocol = dict(S_cell=case["cell_molar"], T_cell=case["initial_ligand_molar"],
                        T_syringe=case["syringe_molar"],
                        cell_volume=case["cell_microliters"] if unequal else case["cell_liters"]*1e6,
                        shot_volumes=case["shots_microliters"] if unequal else [v*1e6 for v in case["injection_liters"]])
        if case.get("model") == "competitive":
            base = importlib.import_module("pytc.indiv_models.base")
            competitor = importlib.import_module("pytc.indiv_models.single_site_competitor")
            saved = base.np, competitor.np
            base.np = competitor.np = Arithmetic()
            try:
                protocol = {k: [mp.mpf(v) for v in val] if isinstance(val, list) else mp.mpf(val)
                            for k, val in protocol.items()}
                model = pytc.indiv_models.SingleSiteCompetitor(
                    C_cell=mp.mpf(case["competitor"]["concentration"]), **protocol)
                model.update_values({k: mp.mpf(v) for k, v in case["upstream_parameters"].items()})
                curves[case["id"]] = [float(v*mp.mpf(4.184e-6)) for v in model.dQ]
            finally:
                base.np, competitor.np = saved
        else:
            if case.get("model") == "one-site":
                model = pytc.indiv_models.SingleSite(**protocol)
            else:
                model = pytc.indiv_models.BindingPolynomial(
                    num_sites=sum(case["counts"]) if unequal else len(case["parameters"]["logka"]), **protocol)
            model.update_values(case["upstream_parameters"])
            curves[case["id"]] = (np.asarray(model.dQ)*4.184e-6).tolist()
    return curves


def generate(source, directory):
    reference = directory/"reference.json"
    stoichiometry_reference = directory.parent/"TwoSiteStoichiometry/reference.json"
    manifest = json.loads(reference.read_text())
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != manifest["source"]["commit"]:
        raise ValueError("Wrong upstream revision")
    subprocess.run(["git", "-C", str(source), "diff", "--exit-code", "HEAD"], check=True)
    for name, expected in manifest["source"]["source_sha256"].items():
        if hashlib.sha256((source/name).read_bytes()).hexdigest() != expected:
            raise ValueError(f"Source changed: {name}")
    runs = []
    for tolerance, digits in (("2e-24", 80), ("2e-27", 100)):
        with tempfile.TemporaryDirectory(prefix="ftitc-precision-") as temporary:
            checkout = Path(temporary)/"source"
            shutil.copytree(source, checkout, ignore=shutil.ignore_patterns(".git", "build", "*.egg-info", "*.so"))
            path = checkout/"src/_bp_ext.c"
            original = path.read_text()
            if original.count("double xtol = 2e-12;") != 1:
                raise ValueError("Unexpected tolerance source")
            path.write_text(original.replace("double xtol = 2e-12;", f"double xtol = {tolerance};"))
            subprocess.run([sys.executable, "setup.py", "build_ext", "--inplace", "--force"], cwd=checkout,
                           check=True, stdout=subprocess.DEVNULL)
            payload = subprocess.check_output([sys.executable, str(Path(__file__).resolve()),
                "--evaluate", str(reference), "--stoichiometry-reference", str(stoichiometry_reference), "--digits", str(digits)], cwd=temporary,
                env=dict(os.environ, PYTHONPATH=str(checkout)), text=True)
            runs.append(dict(xtol_molar=float(tolerance), competitive_decimal_digits=digits,
                patch=f"double xtol = 2e-12; -> double xtol = {tolerance};",
                patched_source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                compiled_extension_sha256=hashlib.sha256(next((checkout/"pytc/indiv_models").glob("bp_ext*.so")).read_bytes()).hexdigest(),
                curves=json.loads(payload)))
    output = dict(reference_sha256=hashlib.sha256(reference.read_bytes()).hexdigest(),
        stoichiometry_reference_sha256=hashlib.sha256(stoichiometry_reference.read_bytes()).hexdigest(),
        generator_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        environment=dict(python=sys.version, packages={p: importlib.metadata.version(p) for p in ("numpy", "scipy", "mpmath", "pytc-fitter")}),
        protocol="Pinned upstream finite injections and model expressions; no fitting or subdivision.",
        arithmetic="BindingPolynomial: only xtol changed; relative tolerance unchanged. Competitive: unchanged upstream expressions and protocol evaluated with mpmath object arrays.",
        runs=runs)
    (directory/"precision-reference.json").write_text(json.dumps(output, indent=2)+"\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evaluate", type=Path)
    parser.add_argument("--digits", type=int, default=80)
    parser.add_argument("--stoichiometry-reference", type=Path)
    parser.add_argument("--pytc-source", type=Path)
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    if args.evaluate:
        print(json.dumps(evaluate(args.evaluate.resolve(), args.stoichiometry_reference.resolve(), args.digits)))
    else:
        generate(args.pytc_source.resolve(), args.directory.resolve())
