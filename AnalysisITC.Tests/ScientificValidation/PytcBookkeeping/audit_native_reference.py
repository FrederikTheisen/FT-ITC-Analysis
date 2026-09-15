#!/usr/bin/env python3
"""Compare a freshly source-built native pytc run with the frozen references.

This audits provenance and replay only. It contains no equilibrium, concentration
or injection-heat equations and does not replace the separate parameter-mapping check.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(source, fresh_package, replay, reference):
    original = json.loads((reference/"reference.json").read_text())
    regenerated = json.loads((replay/"reference.json").read_text())
    commit = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    clean = not subprocess.check_output(["git", "-C", str(source), "status", "--porcelain"], text=True).strip()
    if not clean or commit != original["source"]["commit"]:
        raise ValueError("The audited checkout is not clean at the pinned revision.")
    if original["generator_sha256"] != sha(reference/"generate_reference.py"):
        raise ValueError("The frozen generator differs from the current generator.")
    for key in ("generator_sha256", "protocol", "units", "acceptance", "environment", "cases"):
        if original[key] != regenerated[key]:
            raise ValueError(f"Fresh native replay differs in {key}.")
    for path, digest in original["source"]["source_sha256"].items():
        if sha(source/path) != digest or regenerated["source"]["source_sha256"][path] != digest:
            raise ValueError(f"Native source changed: {path}")
    for name in ("base.py", "single_site.py", "single_site_competitor.py", "binding_polynomial.py"):
        if (fresh_package/"indiv_models"/name).read_bytes() != (source/"pytc/indiv_models"/name).read_bytes():
            raise ValueError(f"Fresh native Python module changed: {name}")
    extensions = list((fresh_package/"indiv_models").glob("bp_ext*.so"))
    if len(extensions) != 1 or sha(extensions[0]) != regenerated["source"]["compiled_extension_sha256"]:
        raise ValueError("Replay did not record the fresh native extension.")
    files = [case["file"] for case in original["cases"]]+["native-trajectory.dat"]
    for name in files:
        if (reference/name).read_bytes() != (replay/name).read_bytes():
            raise ValueError(f"Native heat/trajectory output changed: {name}")
    result = dict(source_commit=commit, source_checkout_clean=clean,
                  reference_sha256=sha(reference/"reference.json"), generator_sha256=original["generator_sha256"],
                  audited_package_path=str(fresh_package),
                  fresh_extension_sha256=sha(extensions[0]),
                  frozen_extension_sha256=original["source"]["compiled_extension_sha256"],
                  case_records_identical=len(original["cases"]),
                  integrated_heat_files_byte_identical=len(original["cases"]),
                  additional_trajectory_file_byte_identical=True,
                  source_files_verified=original["source"]["source_sha256"],
                  scope="Native-source replay/provenance audit, not an independent review of the parameter mapping.")
    (reference/"native-audit.json").write_text(json.dumps(result, indent=2)+"\n")
    print(f"Clean native-source rebuild reproduces all {len(original['cases'])} case records and integrated-heat files exactly.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--fresh-package", required=True, type=Path)
    parser.add_argument("--replay", required=True, type=Path)
    parser.add_argument("--reference", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    audit(args.source.resolve(), args.fresh_package.resolve(), args.replay.resolve(), args.reference.resolve())
