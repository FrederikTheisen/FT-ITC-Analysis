#!/usr/bin/env python3
"""Build, test and report the current forward models in a fresh run directory."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

from report_results import hash_file, report

REPOSITORY = Path(__file__).resolve().parents[3]
FILTER = "FullyQualifiedName~PytcExternalReferenceTests|FullyQualifiedName~TwoSiteStoichiometryForwardTests"
MODEL_SOURCES = [f"AnalysisITC.Core/Analysis2/Models/{name}.cs" for name in (
    "OneSetOfSites", "TwoSetsOfSites", "CompetitiveBinding", "SequentialBindingSites")]


def source_hashes(repository):
    paths = set()
    for directory in ("AnalysisITC.Core", "AnalysisITC.Core.Tests"):
        paths.update(path for path in (repository/directory).rglob("*")
                     if path.suffix in (".cs", ".csproj", ".props", ".targets")
                     and not {"bin", "obj"}.intersection(part.lower() for part in path.relative_to(repository).parts))
    for pattern in ("Directory.Build.*", "Directory.Packages.*", "global.json", "[Nn]u[Gg]et.[Cc]onfig"):
        paths.update(repository.glob(pattern))
    scientific = repository/"AnalysisITC.Tests/ScientificValidation"
    for directory in ("ForwardPrecision", "PytcBookkeeping", "TwoSiteStoichiometry"):
        paths.update(path for path in (scientific/directory).iterdir()
                     if path.suffix.lower() in (".json", ".py", ".dh"))
    return {path.relative_to(repository).as_posix(): hash_file(path) for path in sorted(paths)}


def validate(output_root, repository=REPOSITORY):
    output_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output_root))
    print(f"Validation run: {run}", flush=True)
    sources = source_hashes(repository)
    build = run/"build"
    project = repository/"AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj"
    subprocess.run(["dotnet", "build", str(project), "--configuration", "Release",
                    "--no-restore", "--no-incremental", "--output", str(build)],
                   cwd=repository, check=True)
    assemblies = {name: hash_file(build/name) for name in (
        "AnalysisITC.Core.dll", "AnalysisITC.Core.Tests.dll")}

    def check_unchanged():
        if source_hashes(repository) != sources:
            raise RuntimeError("Build inputs changed during validation; rerun the command.")
        if any(hash_file(build/name) != digest for name, digest in assemblies.items()):
            raise RuntimeError("Built assemblies changed during validation; rerun the command.")

    check_unchanged()
    env = dict(os.environ, FTITC_PYTC_RESULTS=str(run/"original"),
               FTITC_TWO_SITE_RESULTS=str(run/"stoichiometry"))
    subprocess.run(["dotnet", "test", str(build/"AnalysisITC.Core.Tests.dll"),
                    "--filter", FILTER, "--logger", "trx;LogFileName=forward.trx",
                    "--results-directory", str(run)], cwd=repository, env=env, check=True)
    check_unchanged()
    identity = dict(
        production_source_sha256={name: sources[name] for name in MODEL_SOURCES},
        build=dict(configuration="Release", assemblies_sha256=assemblies,
                   inputs_sha256=hashlib.sha256(json.dumps(sources, sort_keys=True).encode()).hexdigest()))
    # Publish the report only after numerical checks, rendering and the final input check succeed.
    with tempfile.TemporaryDirectory(prefix="report-", dir=run) as staging:
        report(run/"original/comparisons.json", run/"stoichiometry/comparisons.json",
               run/"forward.trx", Path(staging), identity)
        check_unchanged()
        Path(staging).rename(run/"report")
    print(f"Report: {run/'report/README.md'}", flush=True)
    return run


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-root", type=Path, default=REPOSITORY/"test-results/forward-precision",
                        help="Parent directory for a new run (existing results are never reused).")
    args = parser.parse_args()
    validate(args.output_root.resolve())
