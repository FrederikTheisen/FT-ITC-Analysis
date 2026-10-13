#!/usr/bin/env python3
"""Separate modified-tolerance pytc diagnostic; never replaces native references."""
import argparse
import hashlib
import inspect
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def evaluate(reference_path):
    if not hasattr(inspect, "getargspec"):
        inspect.getargspec = inspect.getfullargspec
    import numpy as np
    import pytc
    from pytc.indiv_models import bp_ext
    reference = json.loads(reference_path.read_text())
    curves = {}
    for case in reference["cases"]:
        model = pytc.indiv_models.BindingPolynomial(num_sites=sum(case["counts"]),
            S_cell=case["cell_molar"], T_cell=case["initial_ligand_molar"],
            T_syringe=case["syringe_molar"], cell_volume=case["cell_microliters"],
            shot_volumes=case["shots_microliters"])
        model.update_values(case["upstream_parameters"])
        curves[case["id"]] = (np.asarray(model.dQ)*4.184e-6).tolist()
    return dict(compiled_extension_sha256=hashlib.sha256(Path(bp_ext.__file__).read_bytes()).hexdigest(), curves=curves)


def run(source, directory):
    reference = directory/"reference.json"
    manifest = json.loads(reference.read_text())
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != manifest["source"]["commit"]:
        raise ValueError("Wrong pinned revision")
    subprocess.run(["git", "-C", str(source), "diff", "--exit-code", "HEAD"], check=True)
    for name, expected in manifest["source"]["source_sha256"].items():
        if hashlib.sha256((source/name).read_bytes()).hexdigest() != expected:
            raise ValueError(f"Source changed: {name}")
    runs = []
    for tolerance in ("2e-18", "2e-21"):
        with tempfile.TemporaryDirectory(prefix="ftitc-two-site-convergence-") as temporary:
            checkout = Path(temporary)/"source"
            shutil.copytree(source, checkout, ignore=shutil.ignore_patterns(".git", "build", "*.egg-info", "*.so"))
            path = checkout/"src/_bp_ext.c"
            original = path.read_text()
            if original.count("double xtol = 2e-12;") != 1:
                raise ValueError("Unexpected tolerance source")
            path.write_text(original.replace("double xtol = 2e-12;", f"double xtol = {tolerance};"))
            subprocess.run([sys.executable, "setup.py", "build_ext", "--inplace", "--force"], cwd=checkout, check=True, stdout=subprocess.DEVNULL)
            environment = dict(os.environ, PYTHONPATH=str(checkout))
            payload = subprocess.check_output([sys.executable, str(Path(__file__).resolve()), "--evaluate", str(reference)],
                                              cwd=temporary, env=environment, text=True)
            runs.append(dict(xtol_molar=float(tolerance), patch=f"double xtol = 2e-12; -> double xtol = {tolerance};",
                             patched_source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(), **json.loads(payload)))
    output = dict(kind="Modified native-pytc tolerance experiment; not unmodified external validation",
                  reference_sha256=hashlib.sha256(reference.read_bytes()).hexdigest(),
                  generator_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), runs=runs)
    (directory/"convergence-diagnostic.json").write_text(json.dumps(output, indent=2)+"\n")
    print("Wrote separate convergence diagnostics at 2e-18 and 2e-21 M.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--evaluate", type=Path)
    parser.add_argument("--pytc-source", type=Path)
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    if args.evaluate:
        print(json.dumps(evaluate(args.evaluate.resolve())))
    else:
        run(args.pytc_source.resolve(), args.directory.resolve())
