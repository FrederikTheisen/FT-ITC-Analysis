# pytc-style discrete bookkeeping

Validated on 15 September 2026. MicroCal remains the default; Dumas remains selectable and unchanged. This option follows pytc's finite-injection bookkeeping, not its complete numerical implementation or an empirically established improvement over MicroCal.

The [illustrated report](REPORT.md) puts all 29 native comparisons together, including twelve two-independent-site cases. It contains actual heat-curve overlays, signed-error plots, molar-ratio x-axes, per-case parameters and pass/diagnostic results. The shareable PDF is generated at `output/pdf/pytc-forward-validation.pdf` from the repository root. PNG figures are in `figures/`.

## Independent native reference

`generate_reference.py` calls unmodified pytc-fitter 1.1.5 at commit `d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac`. It verifies the checkout and installed Python model sources and records source, compiled-extension, generator and fixture hashes in `reference.json`. It does not load FT-ITC, implement replacement equilibrium equations, subdivide injections, add noise/background or fit parameters.

The 29 frozen `.DH` files contain native integrated injection heats. `native-trajectory.dat` is an additional serialization of native concentrations and heats for metadata-inference testing. There are no raw thermograms or baseline/integration steps. A separate regeneration reproduced all 31 frozen outputs (29 `.DH` files, the trajectory table and the reference manifest) byte-for-byte. All 14 original native case records and their heat files are unchanged.

- Eleven one-site cases cover matched exothermic/endothermic heats, a realistic c-value series (`c = 10, 100, 1000`) with both 1.5 µL and 5 µL shots, and an N scan (`N = 0.5, 1, 2`) at c = 100 using a 200 µM syringe. All injections are uniform within each case.
- Six required two-independent-site cases cover c₁ = 50, 500 and 2000 crossed with Kd₂/Kd₁ = 10 and 100. They use one site of each type, no initial ligand, a 20 µM cell and 300 µM syringe, and FT-ITC's actual `TwoSetsOfSites` model. The c₁ = 2000, Kd ratio 10 case is retained as a diagnostic because the realistic protocol exceeds the practical limit.
- Six diagnostic two-independent-site cases use the requested 30 µM cell, 500 µM syringe, no initial ligand and fixed 1.5 µL shots. They include the 50 pM/10 nM, 500 pM/100 nM and 5 nM/1 µM repeats plus the Kd₁ = 50 nM, Kd₂ = 50/500/5000 nM scan.
- Three competitive cases use no initial ligand, uniform 1.5 µL shots, an 80 µM cell and 500 µM syringe, with competitor concentrations of 0, 500 and 1000 µM.
- Three sequential diagnostics cover 2–4 steps with no initial ligand. The two-step ladder uses Kd = 10 nM and 1 µM; the three-step ladder uses 10 nM, 500 nM and 5 µM, with distinct enthalpies and a 10 µM cell / 150 µM syringe (15× concentration).
- Nonzero initial ligand is passed through native pytc's `T_cell` constructor argument. FT-ITC receives the same fixed initial condition as a segment start. This does **not** externally validate the tandem back-mixing tool.

Native concentrations are molar, volumes µL, and enthalpies cal/mol; native heats in microcalories are converted to joules using 4.184 J/cal. Cell-side N maps to `fx_competent`. Sequential cumulative association constants are products of step constants; bound-state enthalpies are sums of step enthalpies. Competitive parameters retain their existing meanings. Both native background parameters are zero: FT-ITC's offset convention remains unchanged and is tested separately.

The concentration law is the product of each `(1-v/V)` retention. Ordinary binding heat is `Qafter - (1-v/V)*Qbefore`. Source: [pytc's pinned models](https://github.com/harmslab/pytc/tree/d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac/pytc/indiv_models).

### Exact two-independent-site reference

pytc's native `BindingPolynomial` represents the independent-site polynomial `P = (1 + Ka*L)(1 + Kb*L)` exactly for `N1 = N2 = 1`. The parameter mapping is:

```text
beta1 = Ka + Kb
beta2 = Ka * Kb
dH1 = (Ka*Ha + Kb*Hb) / (Ka + Kb)
dH2 = Ha + Hb
fx_competent = 1
```

The singly occupied ensemble contains both alternatives, hence its affinity-weighted enthalpy. This is an exact reparameterization, not a proxy model or a sum of two independent one-site titrations. The external package solves the shared ligand mass balance and computes all finite-shot heats. FT-ITC is evaluated through `TwoSetsOfSites`, not its sequential model. A separate mapping test verifies the polynomial and enthalpy identities from independent-site occupancies.

### Reference-generation audit

The only locally authored model-specific reference logic is the input parameter mapping above. Concentrations are calculated by native `ITCModel._titrate_species`; two-site equilibria and finite-shot heats by native `BindingPolynomial.dQ` -> `bp_ext.dQ`. The generator freezes the returned `model.dQ` array. FT-ITC is used only by the C# comparison, and the report builder only plots recorded arrays.

A fresh build of the pinned, unmodified native C extension reproduced all 25 case records and integrated-heat files byte-for-byte, plus the native trajectory table. [native-audit.json](native-audit.json) records the source and fresh binary hashes. This is a provenance/replay check, not independent scientific review of the parameter mapping.

To repeat that audit, copy the pinned source's `pytc/` package into a fresh temporary directory and, from the pinned checkout, run `python setup.py build_ext --force --build-temp /absolute/fresh-directory/build --build-lib /absolute/fresh-directory`. Run `generate_reference.py` with `PYTHONPATH=/absolute/fresh-directory` and `--output /absolute/fresh-directory/reference`, then run:

```sh
python audit_native_reference.py --source /absolute/path/to/pytc --fresh-package /absolute/fresh-directory/pytc --replay /absolute/fresh-directory/reference
```

## Acceptance and numerical goal

The user-selected acceptance limit is **0.01% of peak injection heat**, i.e. maximum absolute error / maximum absolute native heat ≤ `1e-4`. Floating-point agreement is a **theoretical diagnostic goal**, not a release requirement. Its fixed diagnostic criterion is `abs(error_i) <= 512 * double_epsilon * (peak + abs(native_i))`; scaling by peak avoids unstable relative errors near zero heat.

All 17 required cases pass the practical limit. The seven diagnostics range from **0.0165% to 2.1759% of peak heat** and are not counted as practical passes. They cover the c₁ = 2000, ratio-10 two-site case, the three absolute-affinity repeats, and the realistic empty-cell sequential ladders. `comparisons.json` and the per-case JSON reports distinguish `Passed` from `RoundoffGoalMet` and retain both native and actual FT-ITC heat arrays. No variable-shot cases remain.

| Native model | Cases | Maximum error / peak across those cases | Practical limit | Roundoff goal |
|---|---:|---:|---|---|
| One-site | 9 | 3.31e-12 | Pass | Met |
| Two independent sites, one of each | 6 | 9.39e-3 | Pass | Not met |
| Two independent sites, diagnostics | 5 | 2.18e-2 | Diagnostic | Not met |
| Competitive | 3 | 7.82e-5 | Pass | Not met |
| Sequential, 2–4 steps | 3 | 2.90e-2 | Diagnostic | Not met |

FT-ITC's equilibrium solvers were not changed to imitate pytc's numerical rounding. In particular, native binding-polynomial root finding uses an absolute free-ligand tolerance of `2e-12 M`, while FT-ITC uses its existing scaled mass-balance tolerance. Matching the bookkeeping does not imply bit-identical equilibrium calculations. Fifteen cases do not meet the roundoff goal; those differences are not concealed or described as roundoff agreement by the practical acceptance result. The tight two-site and sequential diagnostics remain particularly sensitive to the root solver.

General fractional-stoichiometry two-independent-site binding and monomer–dimer dissociation are FT-ITC extensions, not native-pytc reference cases. `PytcInjectionHeatTests` checks them using independently chosen equilibrium states and mass/enthalpy balances, including incoming syringe dimer heat. It also checks stateless evaluation, excluded/zero shots, invalid-volume rejection and tandem compartment balances. Parameter recovery is not used as forward-model validation.

## Performance

Local Debug run, median milliseconds; timings are descriptive, not CI thresholds. Objective timings use five batches of 100 evaluations after warm-up. Fit timings use three LM solves after warm-up, with identical native observations, prescribed starting perturbations and a locked zero offset. Each method uses its own concentrations and heat convention. Imports and model preparation are outside the timers; differing optimizer paths can change fit times.

| Model / method | Objective, ms | Fit, ms |
|---|---:|---:|
| One-site / MicroCal | 0.0513 | 4.81 |
| One-site / Dumas | 0.0645 | 6.07 |
| One-site / pytc | 0.0518 | 6.22 |
| Sequential 2 / MicroCal | 0.723 | 91.1 |
| Sequential 2 / Dumas | 1.116 | 117.4 |
| Sequential 2 / pytc | 0.742 | 109.1 |

The full numbers are in `one-exothermic-performance.json` and `sequential-2-performance.json`. pytc uses two heat-content evaluations per nonzero shot, versus three for Dumas; this is not a guarantee of a particular fitting-speed improvement.

## Reproduction and application verification

Use a clean checkout of the pinned pytc revision and the generation dependencies in `../ExternalIntegratedHeats/requirements.txt`; install pytc from that checkout. Python 3.11+ needs only the generator's `inspect.getargspec` compatibility alias, which does not alter numerical calculations.

```sh
python generate_reference.py --pytc-source /absolute/path/to/pytc
```

From the FT-ITC repository root:

```sh
dotnet test AnalysisITC.Core.Tests --filter 'FullyQualifiedName~PytcInjectionHeatTests|FullyQualifiedName~PytcExternalReferenceTests'
FTITC_PYTC_RESULTS=AnalysisITC.Tests/ScientificValidation/PytcBookkeeping dotnet test AnalysisITC.Core.Tests --filter 'FullyQualifiedName~PytcInjectionHeatTests|FullyQualifiedName~PytcExternalReferenceTests' --logger 'trx;LogFileName=pytc-focused.trx' --results-directory /absolute/test-results
dotnet test AnalysisITC.Core.Tests
dotnet test AnalysisITC.Avalonia.Tests --filter 'FullyQualifiedName~PreferencesTests|FullyQualifiedName~ExperimentDetailsWindowTests' -- xUnit.MaxParallelThreads=1 xUnit.ParallelizeTestCollections=false
dotnet test AnalysisITC.Web.Tests --filter 'FullyQualifiedName~Viewer|FullyQualifiedName~Document|FullyQualifiedName~Ftxtc'
xcrun swift -module-cache-path /private/tmp/ftitc-pytc-swift-cache AnalysisITC.MacOS.Tests/PreferencesLayoutTests.swift
dotnet build AnalysisITC.Avalonia/AnalysisITC.Avalonia.csproj --configuration Debug --no-restore
msbuild AnalysisITC.MacOS/AnalysisITC.MacOS.csproj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:EnableCodeSigning=False /p:EnablePackageSigning=False /p:CreatePackage=False /m:1 /v:minimal
```

To regenerate the illustrated report, install `report-requirements.txt` in a reporting environment, then run from the repository root:

```sh
python AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_report.py --focused-trx /absolute/test-results/pytc-focused.trx --core-trx /absolute/test-results/core.trx
python -m unittest discover -s AnalysisITC.Tests/ScientificValidation/PytcBookkeeping -p 'test_generate_report.py'
```

`--core-trx` and `--focused-trx` are optional evidence inputs. Supply the current runs when reporting suite status. Generate `core.trx` with `dotnet test AnalysisITC.Core.Tests --logger 'trx;LogFileName=core.trx' --results-directory /absolute/test-results`. The builder writes `REPORT.md`, 15 PNG figures, `report-evidence.json` and `output/pdf/pytc-forward-validation.pdf`. It verifies reference hashes, case coverage, production-model identity and every plotted error/acceptance result before authoring the report. It does not solve equilibria or fabricate FT-ITC curves. Residual plots have explicitly labelled zoomed axes; the all-case overview shows the fixed 0.01% limit on a log scale.

Verification of this working tree:

- Expanded focused pytc suite: **53 passed**, including 24 native forward cases, the exact independent-site mapping check, and analytical extension tests. The c₁ = 2000/ratio-10 case, three absolute-affinity two-site cases and three sequential cases are explicitly expected diagnostic mismatches, not hidden passes.
- Report-evidence checks: **7 passed**, including rejection of stale inputs, altered curves, missing cases, incorrect model/pass labels and accurate counting of skipped tests.
- Focused scientific, bookkeeping lifecycle and native-format/schema regressions: **253 passed**, including retained Dumas coverage.
- Full core run after removing variable-shot protocols: **1,318 passed, 5 failed, 1 skipped**. All new pytc tests pass. The skipped test is the existing cross-convention higher-step parameter-recovery diagnostic, not a new pytc comparison.
- The five failures reproduce when the affected existing benchmark classes run alone: two Origin D369A MicroCal affinity comparisons (about 0.506–0.507% difference against a 0.5% bound), one published-pytc MicroCal/Nelder–Mead alternative-start recovery, and two Wu H67A sequential parameter-recovery comparisons (about 3.07% against 3%). Their data, solvers and acceptance criteria were not changed for this feature. These are fitting benchmarks, not failures of the new native-pytc forward comparisons.
- Avalonia preferences/details: **25 passed** with the headless runner restricted to one test thread. An unrestricted combined run encountered UI-thread ownership errors; the serial run passes without application changes for that harness issue.
- Web/viewer: **21 passed**. Core native-format tests also verify restored pytc model and bootstrap curves in the viewer.
- Native macOS preferences layout: **passed**, with non-fatal macOS XPC diagnostics.
- Both desktop Debug builds: **succeeded**. Native build emitted a non-fatal locale warning. No packaging, signing, release operations or version changes were performed.

The implementation reuses the existing experiment/model/bootstrap/validity provenance fields. New model schemas are 3 for ordinary pytc fits and 4 for sequential pytc fits; package/project schemas and all historical model schemas stay unchanged. Older fits are not migrated, and incompatible method/schema combinations are rejected. Dumas code, fixtures and UI availability are retained.
