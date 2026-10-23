# Reproduction

From the repository root, after normal .NET dependency restore, run:

```sh
python3 AnalysisITC.Tests/ScientificValidation/ForwardPrecision/run_validation.py
```

Use the repository's .NET SDK and Python 3 with numpy and matplotlib installed
for the existing figure renderer. Plotting dependency versions are listed in
`../PytcBookkeeping/report-requirements.txt`.

The command builds `AnalysisITC.Core.Tests` in Release configuration into a new
directory, runs `PytcExternalReferenceTests` and `TwoSiteStoichiometryForwardTests`
against that assembly, then generates the report from that run's predictions and
TRX. Existing prediction-output environment variables are overridden. The
external references and floating-point acceptance criterion are unchanged.
The 52-case comparison uses prescribed parameters without fitting; the command
reuses the checked-in external references.

Each run is saved under `test-results/forward-precision/run-*/`. The command
prints its report path. Use `--output-root /path/to/results` to select another
parent directory; a fresh run is always created. The run retains its build,
prediction files, TRX and `report/` (README, CSV, JSON and PDF/PNG figures).
The checked-in historical reports are left unchanged.

The existing `verification.json` records the source hashes captured before
building, a build-input fingerprint and hashes of the built Core/test assemblies.
Changed inputs or assemblies, build/test failure, missing/duplicate cases or
failed numerical checks prevent publication of `report/`. The standalone
`report_results.py` command no longer accepts saved-result arguments: rerun the
single command to produce current validation evidence.

To run the workflow's failure-path tests without building .NET:

```sh
python3 -m unittest discover -s AnalysisITC.Tests/ScientificValidation/ForwardPrecision -p test_run_validation.py -v
```

## Regenerating the external references

The native `.DH` fixtures and default pytc outputs remain in `PytcBookkeeping`
and `TwoSiteStoichiometry`. The precision generator executes the same pinned
external models and full finite-injection protocol. Its only C change is
absolute root tolerance; competitive expressions run unchanged with mpmath
arithmetic. Source, generator and compiled-extension hashes are recorded in
`precision-reference.json`.

For reference regeneration, use a clean pytc checkout at
`d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac`, Python 3.9.6, numpy 1.26.4,
scipy 1.13.1, mpmath 1.3.0 and the pytc 1.1.5 dependencies:

```sh
python AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/precision_reference.py --pytc-source /path/to/pinned-pytc
```

After regeneration, run `run_validation.py` to rebuild, test and report against
those references. All 52 comparisons and the reference convergence check must
pass the unchanged floating-point criterion.
