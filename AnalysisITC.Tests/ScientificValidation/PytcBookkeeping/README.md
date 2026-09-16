# pytc-style discrete bookkeeping

Report refreshed on 16 September 2026. MicroCal remains the default; Dumas remains selectable and unchanged. This option follows pytc's finite-injection bookkeeping, not its complete numerical implementation or an empirically established improvement over MicroCal.

The [illustrated report](REPORT.md) puts all **38 native comparisons** together, including **18 two-independent-site cases**. It contains actual heat-curve overlays, signed-error plots, molar-ratio x-axes, per-case parameters and practical pass/fail results. A separate exploratory concentration-sensitivity section includes 15 native-pytc curves and an early-injection zoom. The shareable PDF is generated at `output/pdf/pytc-forward-validation.pdf` from the repository root. PNG figures are in `figures/`.

## Independent native reference

`generate_reference.py` calls unmodified pytc-fitter 1.1.5 at commit `d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac`. It verifies the checkout and installed Python model sources and records source, compiled-extension, generator and fixture hashes in `reference.json`. It does not load FT-ITC, implement replacement equilibrium equations, subdivide injections, add noise/background or fit parameters.

The 38 `.DH` files listed in `reference.json` contain native integrated injection heats. `native-trajectory.dat` is an additional serialization of native concentrations and heats for metadata-inference testing. There are no raw thermograms or baseline/integration steps. A separate regeneration reproduced all 38 case records and all 39 data files (38 `.DH` files plus the trajectory table) identically. Older fixture files also remain in this directory; only cases listed in the current manifest count toward this report.

- Eleven one-site cases cover matched exothermic/endothermic heats; a c-value series (`c = Ka * N * cell concentration = 10, 100, 1000`) with both 1.5 µL and 5 µL shots; and an N scan (`N = 0.5, 1, 2`) at fixed Ka = 1e7 M⁻¹, 10 µM cell and 200 µM syringe. The N scan therefore has c = 50, 100 and 200, respectively.
- Six two-independent-site cases cover c₁ = 50, 500 and 2000 crossed with Kd₂/Kd₁ = 10 and 100. They use a 20 µM cell and 300 µM syringe. Five meet the practical limit; c₁ = 2000, Kd ratio 10 is a diagnostic mismatch.
- Twelve further two-independent-site cases comprise three affinity pairs (50 pM/10 nM, 500 pM/100 nM and 5 nM/1 µM) at 30 µM cell / 500 µM syringe; 2×, 10× and 100× joint concentration/Kd scalings of the tightest pair; a Kd₁ = 50 nM, Kd₂ = 50/500/5000 nM scan; and its 10× joint concentration/Kd scaling. All use 25 injections of 1.5 µL. All 18 two-site cases use N1 = N2 = 1 and FT-ITC's actual `TwoSetsOfSites` model.
- Three competitive cases use no initial ligand, uniform 1.5 µL shots, an 80 µM cell and 500 µM syringe, with competitor concentrations of 0, 500 and 1000 µM.
- Six sequential cases cover 2-4 steps at 10 µM cell / 150 µM syringe with 60 injections of 1 µL. The base Kd ladders are [10, 1000], [10, 100, 1000] and [10, 100, 1000, 10000] nM, plus a 10×-Kd lower-affinity repeat of each. The three base ladders fail the practical limit; the three lower-affinity repeats pass.

All current cases have a 200 µL cell, zero initial ligand and uniform injections within each experiment. This grid does **not** externally validate variable injection volumes, nonzero initial ligand or tandem back-mixing. Separate analytical/lifecycle tests do not replace that external coverage.

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

A fresh build of the pinned, unmodified native C extension reproduced all 38 case records and integrated-heat files identically, plus the native trajectory table. [native-audit.json](native-audit.json) records the source and fresh binary hashes. This is a provenance/replay check, not independent scientific review of the parameter mapping.

To repeat that audit, copy the pinned source's `pytc/` package into a fresh temporary directory and, from the pinned checkout, run `python setup.py build_ext --force --build-temp /absolute/fresh-directory/build --build-lib /absolute/fresh-directory`. Run `generate_reference.py` with `PYTHONPATH=/absolute/fresh-directory` and `--output /absolute/fresh-directory/reference`, then run:

```sh
python audit_native_reference.py --source /absolute/path/to/pytc --fresh-package /absolute/fresh-directory/pytc --replay /absolute/fresh-directory/reference
```

## Acceptance and numerical goal

The user-selected acceptance limit is **0.01% of peak injection heat**, i.e. maximum absolute error / maximum absolute native heat ≤ `1e-4`. Floating-point agreement is a **theoretical diagnostic goal**, not a release requirement. Its fixed diagnostic criterion is `abs(error_i) <= 512 * double_epsilon * (peak + abs(native_i))`; scaling by peak avoids unstable relative errors near zero heat.

**28/38 cases meet the practical limit; 10/38 do not.** The manifest labels 24 cases `required` and 14 `diagnostic`: all 24 required cases and four diagnostic cases meet the limit. A diagnostic label is not a pass/fail result. The ten mismatches range from **0.0101037% to 2.1759% of peak heat**. `comparisons.json` and the per-case JSON reports distinguish `Passed` from `RoundoffGoalMet` and retain both native and actual FT-ITC heat arrays.

| Native model | Cases | Maximum error (% of peak) | Within 0.01% | Roundoff goal met |
|---|---:|---:|---:|---:|
| One-site | 11 | 3.30603e-12 | 11/11 | 11/11 |
| Two independent sites, one of each | 18 | 2.1759 | 11/18 | 0/18 |
| Competitive | 3 | 0.000120503 | 3/3 | 0/3 |
| Sequential, 2-4 steps | 6 | 0.0364929 | 3/6 | 0/6 |

FT-ITC's equilibrium solvers were not changed to imitate pytc's numerical rounding. In particular, native binding-polynomial root finding uses an absolute free-ligand tolerance of `2e-12 M`, while FT-ITC retains its existing solvers. Matching the bookkeeping does not imply bit-identical equilibrium calculations. **11/38 cases meet the roundoff goal; 27/38 do not.** The 0.01% practical criterion is unchanged. The C# suite currently expects ten mismatches, so a green suite is not evidence that all 38 curves agree; the per-case results above are the scientific outcome.

General fractional-stoichiometry two-independent-site binding and monomer–dimer dissociation are FT-ITC extensions, not native-pytc reference cases. `PytcInjectionHeatTests` checks them using independently chosen equilibrium states and mass/enthalpy balances, including incoming syringe dimer heat. It also checks stateless evaluation, excluded/zero shots, invalid-volume rejection and tandem compartment balances. Parameter recovery is not used as forward-model validation.

## Exploratory concentration sensitivity

`solver_sensitivity.py` calls the same pinned, unmodified native `BindingPolynomial` for 15 initial cell concentrations from 27 to 33 µM around `two-realistic` (30 µM). The 500 µM syringe, 200 µL cell, 25 × 1.5 µL shots, 50 pM/10 nM Kd pair and -20/-50 kJ/mol enthalpies remain fixed. No solver settings change and no random noise is added. `solver-sensitivity.json` records the arrays, source/generator/input hashes and the existing FT-ITC base curve. Regeneration reproduced the original 15 arrays exactly.

The report includes the complete curves and an unsmoothed zoom of the first six injections. All x coordinates use the **30 µM base denominator**, not each curve's perturbed concentration; this aligns identical shots. FT-ITC is shown at the base concentration only.

The saturation shifts are expected physical consequences of changing concentration. The jagged early-shot behavior is consistent with numerical sensitivity, but this sweep alone does not isolate its cause, measure experimental noise or establish which solver is correct. There are no stochastic replicates, matched FT-ITC concentration sweep or independently converged reference. These 15 curves are a separate diagnostic, **not 15 additional forward-validation cases**, and do not change any pass/fail result.

Optional tightened-tolerance curves in `tight-reference.json` are a separate numerical experiment using modified pytc solver settings. They must not be interpreted as unmodified-pytc references or substituted into the acceptance calculations.

## Performance

Local Debug run, median milliseconds; timings are descriptive, not CI thresholds. Objective timings use five batches of 100 evaluations after warm-up. Fit timings use three LM solves after warm-up, with identical native observations, prescribed starting perturbations and a locked zero offset. Each method uses its own concentrations and heat convention. Imports and model preparation are outside the timers; differing optimizer paths can change fit times.

| Model / method | Objective, ms | Fit, ms |
|---|---:|---:|
| One-site / MicroCal | 0.033795 | 3.9447 |
| One-site / Dumas | 0.043491 | 5.4143 |
| One-site / pytc | 0.035194 | 3.72 |
| Sequential 2 / MicroCal | 0.727085 | 86.7886 |
| Sequential 2 / Dumas | 1.1034 | 127.769 |
| Sequential 2 / pytc | 0.756885 | 90.9587 |

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

After regenerating native references or C# comparisons, regenerate the exploratory sweep using the same unmodified pytc environment:

```sh
python AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/solver_sensitivity.py --pytc-source /absolute/path/to/pytc
```

To regenerate the illustrated report, install `report-requirements.txt` in a reporting environment, then run from the repository root:

```sh
python AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_report.py --focused-trx /absolute/test-results/pytc-focused.trx --core-trx /absolute/test-results/core.trx
python -m unittest discover -s AnalysisITC.Tests/ScientificValidation/PytcBookkeeping -p 'test_generate_report.py'
```

`--core-trx` and `--focused-trx` are optional evidence inputs. Supply current runs when reporting current suite status. Generate `core.trx` with `dotnet test AnalysisITC.Core.Tests --logger 'trx;LogFileName=core.trx' --results-directory /absolute/test-results`. The builder writes `REPORT.md`, 16 PNG figures (15 comparison figures plus the concentration-sensitivity figure), `report-evidence.json` and `output/pdf/pytc-forward-validation.pdf`. It verifies reference hashes, case coverage, production-model identity, every plotted error/acceptance result and the sensitivity sweep's provenance/base curve. It does not solve equilibria or fabricate FT-ITC curves. It overwrites only its exact output filenames, without deleting other figures. Residual plots have explicitly labelled zoomed axes; the overview includes every recorded discrepancy and the fixed 0.01% limit on a log scale.

Current focused evidence (16 September 2026):

- Expanded focused pytc suite: **67 passed**, including 38 native forward cases, the exact independent-site mapping check, and analytical extension tests. Ten cases are explicitly expected mismatches. The scientific result remains **28/38 within 0.01%**, not 38/38.
- Report-evidence checks: **16 passed**, covering stale inputs, altered/incomplete curves, missing cases, incorrect model/pass labels, skipped-test counts, unclipped overview limits, README numbers, sensitivity inclusion and preservation of separately authored figures.
- Native reference replay reproduced all 38 case records and 39 data files; the separate 15-curve concentration sweep was also reproduced unchanged.

Earlier implementation verification, retained for context (not rerun for this report-only update, and not a claim about full-suite coverage of the latest 38-case grid):

- Focused scientific, bookkeeping lifecycle and native-format/schema regressions: **253 passed**, including retained Dumas coverage.
- Recorded full core run on 15 September: **1,318 passed, 5 failed, 1 skipped**. The skipped test is the existing cross-convention higher-step parameter-recovery diagnostic, not a native-pytc comparison. This run predates the final case-grid expansion.
- The five failures reproduce when the affected existing benchmark classes run alone: two Origin D369A MicroCal affinity comparisons (about 0.506–0.507% difference against a 0.5% bound), one published-pytc MicroCal/Nelder–Mead alternative-start recovery, and two Wu H67A sequential parameter-recovery comparisons (about 3.07% against 3%). Their data, solvers and acceptance criteria were not changed for this feature. These are fitting benchmarks, not failures of the new native-pytc forward comparisons.
- Avalonia preferences/details: **25 passed** with the headless runner restricted to one test thread. An unrestricted combined run encountered UI-thread ownership errors; the serial run passes without application changes for that harness issue.
- Web/viewer: **21 passed**. Core native-format tests also verify restored pytc model and bootstrap curves in the viewer.
- Native macOS preferences layout: **passed**, with non-fatal macOS XPC diagnostics.
- Both desktop Debug builds: **succeeded**. Native build emitted a non-fatal locale warning. No packaging, signing, release operations or version changes were performed.

The implementation reuses the existing experiment/model/bootstrap/validity provenance fields. New model schemas are 3 for ordinary pytc fits and 4 for sequential pytc fits; package/project schemas and all historical model schemas stay unchanged. Older fits are not migrated, and incompatible method/schema combinations are rejected. Dumas code, fixtures and UI availability are retained.
