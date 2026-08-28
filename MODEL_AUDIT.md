# FT-ITC Analysis mathematical and numerical audit

Audit date: 2026-08-28  
Audited core revision: `3f44ffe28f1bc280f68d14b54c947b7af5fd0fdb` (`Update analysis workflows and supporting files`)  
Method: six isolated first-round reviews, independent high-precision oracle work, and a second verifier/rebutter/original-finder round  
Scope: scientific calculations, fitting, uncertainty estimation, data transformations, persistence, and reporting. The audit itself made no application-source change; post-audit implementation notes below describe corrections subsequently made in the working tree.

## 1. Executive verdict

### Decision

The audited revision contains **five High-severity defects** capable of producing materially incorrect scientific results on reachable paths. No finding was classified Stop-ship because each High finding is scoped to a particular model, import format, or uncertainty/persistence workflow rather than the ordinary one-site workflow as a whole.

The High findings are:

1. `.aff`/`.dat` integrated-heat imports assume a fixed relative `DH`/`NDH` scale and can infer syringe concentration exactly \(10^6\) too small for same-scale energy numerators.
2. the two-independent-site solver has a hard 1 mM free-ligand ceiling and substitutes a non-root, producing mass loss and even wrong-sign heats;
3. the competitive-binding trigonometric cubic is catastrophically ill-conditioned at valid high affinities, can violate site conservation, and can depend on an irrelevant competitor affinity when competitor concentration is zero;
4. bootstrap/LOO rows are counted as successful before a different, limit-dependent filter is applied, allowing reported counts and actual global/member distributions to disagree; and
5. FTITC reload re-filters saved uncertainty rows under current parameter limits, so uncertainty can change across save/reload.

### What is presently trustworthy

Within the tested ranges and documented assumptions, the following paths have strong support:

- ordinary one-set-of-sites predictions and incremental heat accounting;
- active one-site syringe-concentration correction, including a single application of the correction factor;
- sequential-binding state enumeration, occupancies, and stepwise heat accumulation for the tested site counts and ranges;
- two-set predictions **only while the physical free-ligand root remains below 1 mM** and the root residual is well conditioned;
- competitive-binding predictions in moderate, well-conditioned regimes, but not near the allowed high-affinity extremes;
- ordinary, non-tandem dissociation plug-replacement heat accounting under its implemented \(2M\rightleftharpoons D\) convention;
- parameter/vector mapping, locks and bounds for ordinary local primary fits;
- unweighted SSE, error-weighted \(\sum_i(r_i/\sigma_i)^2\), and the deliberate separation between a weighted fit objective and the displayed unweighted local RMSD;
- preservation of the original-data primary best fit as the central reported estimate;
- raw MicroCal, TA, and NanoITC unit conversions exercised by fixtures;
- FTITC/FTXTC primary-fit values, exclusions, references, and ordinary interior bootstrap rows in the existing round-trip tests; and
- same-stock tandem reconstruction and matched-protocol blank subtraction.

“Trustworthy” here means independently derived or exercised without a material discrepancy; it is not a proof over all inputs.

### Outputs requiring caution

Do not use the following outputs for scientific inference without correction or an explicit validation against an independent calculation:

- concentrations, injection masses, normalized heats, or fits originating from the audited `.aff`/`.dat` reader when its `DH`/`NDH` relative-unit assumption does not match the file;
- two-set-of-sites fits that reach or may reach free ligand above 1 mM;
- competitive fits at high affinity/concentration ratios, especially displacement-free limiting cases;
- restored legacy syringe-uncertainty, isomerization, or two-competing-sites models;
- FTITC-restored bootstrap distributions, global/member bootstrap counts, boundary-heavy uncertainty distributions, derived-parameter errors that depend on covariance, concentration-uncertainty results, global unlock-bootstrap results, or two-site site-specific intervals without label alignment;
- LOO values if interpreted as jackknife standard errors or 95% confidence intervals;
- global values labelled “RMSD” (the implementation sums member RMSDs);
- “Fast” fits selected by Restore Defaults when a coarse optimum could matter;
- tandem merges with changed stocks, blank subtraction with mismatched protocols, or dissociation across a tandem segment boundary;
- the competitive-model “prebound” input label and its displayed apparent \(K_d\); and
- thermodynamic interpretation of the global constant-\(\Delta G\) temperature rule as a coupled van’t Hoff/\(\Delta C_p\) state-function model.

### Overall confidence

Overall audit confidence is **High**. Confidence is Very high for the five High findings because the production call paths were identified, the expected calculations were derived, and independent reproductions were obtained. Confidence is lower for scientific coverage outside the oracle grid, for hidden legacy file variants, and for empirical interval coverage because a large repeated-coverage campaign was not feasible. The audit found material errors; it does not claim exhaustive proof of correctness elsewhere.

## 2. Audited revision, working tree, environment, and tests

### Revision and working-tree safety

The audit began at:

```text
HEAD 3f44ffe28f1bc280f68d14b54c947b7af5fd0fdb
3f44ffe Update analysis workflows and supporting files
```

The initial working tree contained four pre-existing user changes:

```text
 M AnalysisITC.Web.Tests/ViewerUploadTests.cs
 M AnalysisITC.Web/wwwroot/app.js
 M AnalysisITC.Web/wwwroot/index.html
 M AnalysisITC.Web/wwwroot/styles.css
```

Those changes were preserved. During the audit a concurrent/shared-worktree event committed exactly those four changes as `ea993097a472380ae97d8c187847f7cf33692286` (`web app updates`). Five manual pages then changed outside this audit and were committed as `450f974069fa120760d1f369cd336856a4a031e9` (`manual source updates`), which was `HEAD` at final report validation. The combined `3f44ffe..450f974` diff affects only those four web files and five manual pages; `git diff --quiet 3f44ffe..450f974 -- AnalysisITC.Core` returned zero, so the audited Core/model tree is unchanged. All source and documentation claims in this report remain anchored to `3f44ffe`; later manual edits were excluded. A transient loss of the original four worktree edits was recovered byte-for-byte from Codex capture ref `refs/codex/turn-diffs/captures/1787867521098/6c5250f7-4efd-42cb-8cdd-80bd5622fa31/base` and verified with a zero `git diff --quiet` result before the later commit. No audit agent intentionally changed repository source.

### Environment

```text
OS:       macOS 15.7.4, Darwin 24.6.0, arm64
Runtime:  .NET SDK 10.0.302; .NET runtime 10.0.10
Locale/time zone observed by audit: Europe/Copenhagen
```

Independent C# and Python/mpmath probes were created only under fresh `/tmp` directories and referenced already-built production assemblies where possible.

### Existing test results

| Suite / command | Result | Interpretation |
|---|---:|---|
| `dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj -c Debug` | first run 384 passed / 6 failed; rerun of built DLL 386 / 4 | Genuine, order-sensitive Core failures; not a platform failure. |
| `dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj -c Release` | 383 passed / 7 failed | Same recovery/optimizer-sensitive families plus local/global offset tolerance failures. |
| `dotnet test AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj -c Debug` | 88 / 88 passed | UI/state coverage passed. |
| same Avalonia command, Release | 88 / 88 passed | Passed. |
| `dotnet test AnalysisITC.Web.Tests/AnalysisITC.Web.Tests.csproj` from solution-relative temporary artifact layout, Debug and Release | 22 / 22 each | Passed. An earlier 22/22-invalid run was a temporary solution-root discovery error and is not counted as a product failure. |
| legacy Xamarin.Mac build | failed: missing `Xamarin.Mac.CSharp.targets` | Platform/toolchain limitation, not mathematical evidence. |
| targeted reader/unit/processing tests | 69 / 69 passed | Existing fixtures pass but omit several decisive value assertions. |

The Core failures included:

- sequential synthetic recovery: Levenberg–Marquardt log-affinity error `0.003310742679` exceeded a `0.003` tolerance; a Release Nelder–Mead case erred by `0.0992267`;
- a one-site published-recovery affinity discrepancy of about `0.513%`;
- two published two-site enthalpy-1 discrepancies of about `37.17%` and `36.24%`; and
- in Release, local/global LM offset recovery `1996.5257` versus expected `2000` (balanced settings recovered about `1999.9988`).

The varying failure set across repeat/configuration indicates shared static `AppSettings`/test-order sensitivity in addition to loose-optimizer behavior. These failures are real regression signals, but they do not by themselves prove a forward-model formula error; the independent oracle supplies that distinction.

## 3. End-to-end calculation map

```text
raw thermogram or integrated-heat file
  -> DataReaders / IntegratedHeatReader / vendor-specific reader
     (parse metadata; convert power, heat, volume, concentration, temperature)
  -> ExperimentData + InjectionData
     (concentration evolution under selected dilution law; inclusion/exclusion)
  -> thermogram processing
     (baseline -> integration boundaries -> absolute injection heat -> heat error)
  -> optional transformations
     (blank/buffer subtraction; tandem concatenation/back-mixing)
  -> AnalysisWorkspace / AnalysisBuilder
     (model selection; model attributes; local/shared/temperature constraints)
  -> Model.Evaluate(injection)
     (equilibrium populations -> total cell reaction heat -> incremental overflow-corrected heat)
  -> residual vector
     observed absolute heat - predicted absolute heat
  -> objective
     local SSE or sum of squared standardized residuals;
     global objective is the sum of member objectives
  -> solver
     Nelder-Mead or Levenberg-Marquardt; transformed affinity coordinates, locks, bounds
  -> primary ModelSolution / GlobalModelSolution
     fitted coordinates -> Kd, DeltaG, -TDeltaS, apparent quantities, residuals, local RMSD
  -> optional uncertainty refits
     centered standardized residual bootstrap; concentration perturbation; LOO
  -> uncertainty summaries
     marginal RMS displacement, percentile endpoints, correlations, prediction bands
  -> display and persistence
     analysis UI / tables / publication figures / CSV and vendor exports / FTITC / FTXTC / web viewer
```

Key call-path anchors at `3f44ffe`:

- readers and concentration evolution: `AnalysisITC.Core/DataReaders/DataReaders.cs:78-184,247-281,403-465` and `IntegratedHeatReader.cs`;
- integration and error: `AnalysisITC.Core/DataClasses/InjectionData.cs:283-302,388-454`;
- subtraction and tandem: `ExperimentData.cs:264-381`, `BufferSubtraction.cs`, and `TandemConcatenationTool.cs`;
- common cell heat/incremental overflow correction: `AnalysisITC.Core/Analysis2/Models/Models.cs:97-125`;
- model construction/reachability: `AnalysisBuilder.cs:389-400`, `ModelFactory.cs:336-350`, and `FTXTCPersistenceRegistry.cs:18-28,171-195`;
- objective/vector/global propagation: `Models.cs`, `GlobalModel.cs`, `GlobalModel.Loss.cs`, `Solver2.cs`, and `SolverUtilities.cs`;
- bootstrap/LOO: `Solver2.cs`, `BootstrapModelSnapshot.cs`, and `BootstrapCorrelationAnalyzer.cs`;
- display/export/persistence: `AnalysisResult.cs`, `Exporter.cs`, `FTITCReader.cs`, `FTXTCReader.cs`, and corresponding writer/registry code.

Observed normalized/molar heat is a display quantity derived from absolute injection heat divided by injected moles. Forward models predict absolute heat. This separation is correct when `InjectionMass` is correct, but at the audited revision it amplified a mismatched `.aff`/`.dat` relative-unit convention into both normalized observations and model inputs.

## 4. Model and subsystem inventory

| Model/subsystem | Reaction or role | Reachability at audited revision | Primary implementation | Existing independent-enough tests / important gaps |
|---|---|---|---|---|
| One Set of Sites | \(P+nL\rightleftharpoons PL_n\), identical independent sites represented by total site concentration | Selectable in active UI; saved/viewed/exported | `Analysis2/Models/OneSetOfSites.cs` | Synthetic/recovery and published examples exist. Oracle covered 150 active cases; no broad literature-oracle grid existed before this audit. |
| One Set with syringe uncertainty | one-set model plus multiplicative syringe factor \(\alpha\) and fixed stoichiometry | Hidden from new-model list; restorable through legacy/persistence registry | `OneSetOfSitesSyringeUncertaintyModel.cs` | Active modern syringe correction in common models was checked; this legacy class has no direct coordinate/persistence oracle and is defective (F-006). |
| Two Sets of Sites | two independent site classes with additive bound populations/heats | Selectable; saved/viewed/exported | `TwoSetsOfSites.cs` | Symmetry tests exist, but no root-residual oracle above 1 mM and no bootstrap label-alignment test. Defects F-002 and F-016. |
| Sequential Binding Sites | stepwise \(P L_{j-1}+L\rightleftharpoons PL_j\) binding polynomial | Selectable; saved/viewed/exported | `SequentialBindingSites.cs` | Existing recovery tests plus 240 high-precision cases; active forward model matched to sub-fJ scale. Test tolerances/settings are unstable. |
| Competitive Binding | target A and competitor B mutually exclusive on the same sites | Selectable; saved/viewed/exported | `CompetitiveBinding.cs` | Moderate cases exist. No conservation/high-affinity/irrelevant-parameter limit test; F-003, F-017, and F-018. |
| Two Competing Sites | legacy two-population competitive model | Hidden from new-model list; restorable through FTXTC | `TwoCompetingSites.cs` | No direct oracle or migration test; affinity coordinate is inconsistent (F-006). |
| Dissociation | \(2M\rightleftharpoons D\), dilution-driven dimer change | Selectable; saved/viewed/exported | `Dissociation.cs` | Ordinary equilibrium algebra checked in 200 high-precision cases. No tandem-boundary test; F-019. |
| One-Site Isomerization | intended coupled cis/trans population plus binding | Hidden from new-model list; restorable through FTXTC | `OneSiteIsomerization.cs` | No active coupled-model test; evaluation ignores the isomer population (F-007). |
| Global constraints | local, shared, or temperature-dependent coordinates across member experiments | Selectable workflow | `GlobalModel.cs`, `GlobalConstraintSemantics.cs`, `AnalysisBuilder.cs` | Vector sharing/propagation tests exist. Missing pooled-RMSD, global-unlock, shared-stock covariance, and coupled thermodynamic-law tests. |
| Residual bootstrap | centered standardized residual resampling and refitting | User-selectable | `Solver2.cs`, model snapshot/correlation classes | Primary preservation and several round trips tested. Boundary/filter/count, label alignment, limited convergence, and derived covariance are inadequately tested. |
| LOO | delete one injection for a single fit; delete one experiment for a global fit | User-selectable | `Solver2.cs`, `GlobalModel.cs` | No jackknife semantics/scale tests; zero-loop configuration untested. |
| Processing/readers | import, baseline, quadrature, errors, concentrations | Always reachable by relevant file/workflow | `DataReaders`, `ExperimentData`, `InjectionData` | Many fixture shape/count tests; decisive unit, irregular-time, locale, and mismatched-protocol assertions absent. |

## 5. Model-by-model audit matrix

“Yes*” means correct only under the limitation stated in the same row. Oracle coverage counts refer to the independent audit harness, not repository tests.

| Model | Independent derivation completed | Implementation matches derivation | Units correct | Dilution/overflow correct | Limiting cases correct | Numerical stability | Identifiability concerns | Independent oracle coverage | Existing test adequacy | Confidence |
|---|---|---|---|---|---|---|---|---|---|---|
| One Set | Yes | Yes | Yes | Yes under documented symmetric active-cell correction | Yes | Strong over tested grid | \(N,\Delta H,K_a\), concentration, and offset can correlate | 150 randomized/log-grid cases; max heat discrepancy about `0.124 pJ` | Moderate; recovery settings/order need hardening | Very high |
| One Set + active syringe correction | Yes | Yes for the modern common-model \(\alpha\) path | Yes | Yes | Yes | Strong in 120 cases | \(\alpha\) can be degenerate with \(N\), \(\Delta H\), concentration | 120 cases; max discrepancy about `0.004 pJ` | Weak for identifiability and persistence | High |
| Legacy syringe-uncertainty class | Yes | **No**: log coordinate evaluated as linear \(K\) | Internally inconsistent affinity semantics | Same heat form otherwise | Not reliable after restore/refit | Not meaningfully valid until migration | Severe coordinate/stoichiometry degeneracy | Call-path probes, not full grid | Inadequate | Very high |
| Two Sets | Yes | Yes only when a physical root is actually found below 1 mM | Yes | Yes* | Algebraic reductions and label-swap prediction pass below cap | **Fail** above hard bracket; arbitrary fallback | Strong site-label and near-identical-site nonidentifiability | Grid/random oracle including reproduced wrong-sign cases | Inadequate root/conservation and label-summary tests | Very high |
| Sequential | Yes | Yes | Yes | Yes | Yes for tested zero/weak/strong cases | Strong for audited site counts/ranges | Adjacent steps can be highly correlated; microscopic/macroscopic interpretation must remain clear | 240 cases; max error about `0.00044 fJ` | Moderate; optimizer recovery is setting-sensitive | Very high |
| Competitive | Yes | Algebraically yes in well-conditioned regime; numerically no at high affinity | Yes for intrinsic fit; displayed apparent \(K_d\) uses total competitor approximation | Common heat correction yes | Zero-competitor reduction can fail numerically depending on irrelevant \(K_B\) | **Fail** at high configured affinities | Target/competitor affinity and enthalpy can be correlated | 300 random cases; 133 exceeded `1 pJ` and `1e-7` tolerance; exact public failure reproduced | Inadequate conservation/limit tests | Very high |
| Two Competing Sites | Yes at reaction-inventory level | **No reliable modern coordinate semantics** | Affinity reporting inconsistent | Not independently cleared | Not adequately verified | Unverified | Severe multi-population nonidentifiability | Minimal legacy call-path checks | Inadequate | High for defect; Moderate for untested remainder |
| Dissociation | Yes | Yes for ordinary plug replacement; no at tandem boundary | Yes under enthalpy-per-mole-dimer convention | Ordinary yes; tandem pre-state no | Low/high concentration limits pass | Strong in 200 ordinary cases | \(K\), \(\Delta H\), and concentration range may correlate | 200 cases; max discrepancy about `0.088 fJ` | Ordinary moderate; tandem absent | High |
| One-Site Isomerization | Yes | **No**: coupled routines/population are disconnected from active `Evaluate` | Reported quantities are semantically misleading | One-site apparent curve only | Intended isomer limits not exercised | Not assessable as named model | Population and intrinsic/apparent constants are nonidentifiable in current curve | Direct invariance/path inspection | Inadequate | Very high |
| Global temperature constraints | Yes | Yes for documented constant-\(\Delta G\) phenomenology | Kelvin/sign/unit transformations correct | Delegated to member model | Code matches stated rule | Stable | Independent \(\Delta G\), \(\Delta H\), \(\Delta C_p\) are not one coupled state function | Analytic temperature comparisons | Tests check implementation, not physical interpretation | Very high |

## 6. Confirmed defects, ordered by severity

### F-001 — `.aff`/`.dat` inference assumes `DH/NDH` is numerically in µmol

- **Classification:** Data-handling defect
- **Severity:** High
- **Confidence:** Very high
- **One-sentence claim:** the audited reader treats the numeric quotient `DH/NDH` as µmol by dividing it by numeric µL; that is correct for µcal/(cal/mol) and the application's J/scaled-`NDH` export, but same-scale cal/(cal/mol) inputs are inferred exactly \(10^6\) too low.
- **Mathematical expectation:** the bytes do not identify the relative `DH`/`NDH` scale, so concentration must come from the `Xt`/`Mt` dilution trajectory or explicit metadata rather than an unconditional heat quotient.
- **Actual implementation behaviour at the audited revision:** `IntegratedHeatReader.cs:422-460` divides `r.DH/r.NDH` by `r.InjV_uL`. This embeds a µmol/µL convention; replacing it unconditionally with `InjV_L` would fix same-scale energy inputs but break the two working conventions.
- **Scientific impact:** affected files have wrong injection mass, ratios, normalized heat, concentration evolution, and fitted parameters. In the supplied `CURVE-2.aff`, `DH=1.12e-6 cal`, `NDH=27.9 cal/mol`, and `INJV=10 µL` imply about `4.0143e-3 M` from that row, while the audited reader stores about `4.0143e-9 M`. The independently encoded `Xt`/`Mt` trajectory indicates approximately `4.00e-3 M`.
- **Correction implemented in the working tree:** infer cell volume and syringe concentration from cumulative `Mt`/`Xt` under the active dilution model and prompt when metadata is unresolved. A complete `DH` column supplies absolute heat; when `DH` is absent or incomplete, a complete `NDH` column supplies molar heat and is converted to absolute heat using the resolved injection mass. The selected unit applies only to the chosen heat column. Real fixtures cover J, µcal, and cal `DH` inputs, with separate normalized-heat coverage.
- **Verifier/rebutter follow-up:** comparison with `230908_PRLRlong_W392A_run1.dat` and `CURVE-1.aff` disproved the original categorical claim; the factor-of-million defect is confirmed only for incompatible relative-unit conventions such as `CURVE-2.aff`.
- **Final status:** **Confirmed but scope corrected; resolved in the working tree.**

### F-002 — two-set free-ligand solver substitutes a non-root above 1 mM

- **Classification:** Numerical-stability defect
- **Severity:** High
- **Confidence:** Very high
- **One-sentence claim:** `TwoSetsOfSites` restricts the physical free-ligand root to `[0,1e-3] M` and, on bracket failure, returns a clamped `0.05*L_total` value that is not a mass-balance root.
- **Mathematical expectation:** the nonnegative free ligand must solve the two-class ligand balance and lies in a physical bracket such as `[0,L_total]`; its residual must be near zero.
- **Actual implementation behaviour:** `TwoSetsOfSites.cs:108-124,144-198` applies the fixed ceiling and silent fallback. One audit case lost about 91.75% of ligand mass and gave production `+110.397822 µJ` versus the 90-digit oracle `-13.471959 µJ`.
- **Scientific impact:** ordinary millimolar weak-binding/high-stock conditions can produce wrong-sign heats and fundamentally wrong fitted affinities/enthalpies.
- **Suggested correction:** dynamically bracket `[0,L_total]`, use a monotone physical solver, validate conservation/residual, and fail explicitly instead of guessing.
- **Verifier conclusion:** independently reproduced a physical root `1.080100984 mM` versus production `55 µM`, with heat sign reversal.
- **Rebutter conclusion:** found the cubic algebra sound but no convention justifying the hard ceiling or fallback.
- **Final consensus status:** **Confirmed** as High; failure is numerical/root-selection rather than the equilibrium derivation.

### F-003 — competitive cubic loses conservation at high affinity

- **Classification:** Numerical-stability defect
- **Severity:** High
- **Confidence:** Very high
- **One-sentence claim:** the algebraic competitive-binding cubic is evaluated with a cancellation-prone trigonometric formula that can yield nonphysical site fractions and catastrophic heat errors at valid configured affinities.
- **Mathematical expectation:** nonnegative fractions must satisfy \(x_P+x_{PA}+x_{PB}=1\), and when \(B_0=0\) the result must be independent of \(K_B\) and equal the stable one-site solution.
- **Actual implementation behaviour:** `CompetitiveBinding.cs:84-165` clamps only one root and does not restore conservation. For `P0=10 µM`, `A0=100 µM`, `K_A=10^12 M^-1`, `B0=0`, computed occupancy can be `1.5807786454`; changing the physically irrelevant `log10(K_B)` changes heat sharply. A public `Evaluate` case produced `-1.095379549e-5 J` versus one-site/oracle about `-6.0820624e-12 J`.
- **Scientific impact:** high-affinity displacement or nominal no-competitor analyses can report heat many orders of magnitude wrong and fit false parameters.
- **Suggested correction:** do not change either fitting optimizer or the model's public evaluation/heat-accounting path. Retain the present analytic cubic as a fast candidate, validate the candidate against the physical balances, and use a small allocation-free safeguarded Newton/bisection fallback only when the candidate is invalid. Special-case exact one-ligand limits before evaluating the irrelevant ligand's affinity.
- **Verifier conclusion:** confirmed conservation sums near `0.20` at `K_A=K_B=10^15`; a benign-\(K_B\) sweep initially made the zero-competitor limit appear correct.
- **Rebutter conclusion:** challenged the broad “all \(B_0=0\) cases fail” claim; exact-source probes nevertheless reproduced failure when the irrelevant \(K_B\) made the cubic ill-conditioned.
- **Final consensus status:** **Confirmed** as High with narrowed wording: the formula is algebraically correct in well-conditioned regimes; the numerical evaluation is not.

#### F-003 remediation constraints

This correction should be narrowly scoped to the private competitive-equilibrium state calculation. It should **not** replace or modify `Solver2`, the Nelder–Mead or Levenberg–Marquardt implementations, parameter coordinates or bounds, `CompetitiveBinding.Evaluate`, `DeltaHeatFromHeatContent`, the overflow correction, persistence, or reporting. Those surrounding paths are not the cause of F-003, and changing them would make a numerical repair unnecessarily risky. The existing interpretation of prebound-ligand concentration during dilution should also remain unchanged here; its naming and apparent-\(K_d\) issues are separate findings F-017 and F-018.

The repair should not instantiate or call another model such as `OneSetOfSites`. The limiting calculation belongs in a pure private helper that accepts scalar totals and affinities and returns one physical competitive state. This avoids changing model lifecycle, offsets, concentration correction, or mutable fitting state.

#### Recommended fast and robust equilibrium resolver

Use the current trigonometric cubic only as a **candidate generator**, rather than trusting and clamping its output. In the dimensionless variables already used by `CompetitiveBinding.cs`, let

\[
r_A=A_t/S_t,\quad r_B=B_t/S_t,\quad
c_A=K_AS_t,\quad c_B=K_BS_t,
\]

where \(S_t=nP_t\), and let \(x\) be the free-site fraction. For any candidate \(x\), calculate

\[
x_{PA}=r_A\frac{c_Ax}{1+c_Ax},\qquad
x_{PB}=r_B\frac{c_Bx}{1+c_Bx},
\]

and the site-balance residual

\[
F(x)=x+x_{PA}+x_{PB}-1.
\]

For nonnegative physical inputs, \(F(0)=-1\), \(F(1)\ge0\), and

\[
F'(x)=1+\frac{r_Ac_A}{(1+c_Ax)^2}
        +\frac{r_Bc_B}{(1+c_Bx)^2}>0.
\]

There is therefore exactly one physical root in `[0,1]`; no general polynomial root selection is required for the fallback.

The resolver should use the following order:

1. Handle exact limits first. If `B0 == 0`, evaluate the stable one-ligand quadratic for A without reading or exponentiating \(K_B\) and set \(x_{PB}=0\). Apply the symmetric rule when `A0 == 0`; if both totals are zero, return the all-free state. Use a rationalized quadratic expression so the one-ligand branch does not introduce its own subtractive cancellation.
2. In the two-ligand case, run the existing cubic expression to obtain a fast candidate. Do not independently clamp \(x\), \(x_{PA}\), or \(x_{PB}\) into physical ranges, because that can hide rather than repair lost mass.
3. Accept the candidate only if every value is finite, all fractions are nonnegative within roundoff, each bound fraction does not exceed its ligand total, and both the site-conservation residual and ligand/equilibrium residuals satisfy a scale-aware tolerance. Tiny roundoff excursions may be normalized only after these checks pass.
4. If validation fails, solve \(F(x)=0\) on `[0,1]` with a safeguarded Newton iteration. Maintain a sign-changing bracket on every step; accept a Newton step only when it is finite and strictly inside the bracket, otherwise bisect. Seed it from a finite cubic candidate or from the first Newton step at zero. This normally converges in a few iterations and has a deterministic bisection bound.
5. Cap the fallback (for example, at 64 iterations), then accept only a state that passes the same conservation checks. If no validated state can be produced, report a controlled invalid model evaluation through the existing convergence/error path. Never substitute a guessed or merely clamped population and continue a scientific fit.

The saturation term should be evaluated in a form that cannot overflow for allowed inputs (for example, branch when `c*x` is non-finite and use its limiting value). The helper should be scalar, allocation-free, and free of mutable static or per-model caches so that bootstrap and global fits remain deterministic and thread-safe. A generic root-finder object or delegate should not be constructed in this hot path.

This two-tier design limits behavioral change: ordinary well-conditioned states continue to use the current analytic result plus a few arithmetic validation checks; exact no-competitor states become both faster and independent of irrelevant options; only states that the current implementation cannot certify pay for iteration. Replacing the cubic unconditionally with a general-purpose solver is not required for F-003.

#### F-003 verification and performance gates

The correction should be accepted only with tests that cover all of the following:

- exact `B0 == 0` equivalence to a high-precision one-site oracle across the full configured \(K_A\) range, while sweeping \(K_B\) and \(\Delta H_B\) to prove irrelevance;
- the symmetric target-free limit, the both-ligands-zero limit, and continuity as either ligand total approaches zero;
- finite, nonnegative fractions with \(x+x_{PA}+x_{PB}=1\), ligand mass conservation, and equilibrium residuals across log-spaced totals, stoichiometries, and affinities including the parameter bounds;
- the published catastrophic case and the `K_A=K_B=10^15` verifier case, compared with a high-precision monotone oracle;
- unchanged heat predictions in well-conditioned legacy cases to a tight floating-point tolerance, including syringe-correction mode and the common pre/post-injection heat calculation; and
- deterministic results under repeated, bootstrap, and global evaluation.

Performance should be measured in a Release build before and after the patch at two levels: a no-allocation microbenchmark of the private state resolver (well-conditioned fast path, exact one-ligand path, and forced fallback), and representative full competitive fits using both supported fitting algorithms. Record fallback iteration counts as test diagnostics. A suitable merge gate is zero steady-state allocation, typical fallback convergence within about eight iterations, the hard iteration cap never reached on the oracle grid, and no more than a 10% median regression for a representative well-conditioned full fit. Wall-clock thresholds should remain in benchmark/CI performance jobs rather than ordinary unit tests to avoid flaky correctness tests.

### F-004 — bootstrap/LOO success counts and retained samples can disagree

- **Classification:** Statistical-method defect
- **Severity:** High
- **Confidence:** Very high
- **One-sentence claim:** error-estimation fits are counted as successes before a separate 1%-inside-bound filter removes rows, and global/member distributions do not apply one common retained replicate set.
- **Mathematical expectation:** a reported successful replicate population must be defined once using the actual fit limits and used consistently for counts, marginal summaries, correlations, global temperature summaries, and persistence.
- **Actual implementation behaviour:** `Solver2.cs:624-656,927-959` increments success before `Models.cs:551-602` filters. The filter reconstructs default/current limits rather than the limits used by Extended/No-limit fits; global rows remain raw while members filter independently (`GlobalModel.cs:404-435`). One retained row can yield zero SD and a point CI without warning.
- **Scientific impact:** reported success counts, local/global CIs, correlations, temperature-dependent errors, and post-reload row populations can silently refer to different samples, generally truncating boundary-heavy tails.
- **Suggested correction:** attach a replicate ID/status/actual bounds, validate once before counting, require a defensible minimum retained population, and propagate one indexed retained set everywhere.
- **Verifier conclusion:** independently confirmed count-before-filter and the global/member split, including default-limit reconstruction.
- **Rebutter conclusion:** found no later count correction; identified further save/reload intersection changes.
- **Final consensus status:** **Confirmed** as High.

### F-005 — FTITC reload can change saved uncertainty

- **Classification:** Data-handling defect
- **Severity:** High
- **Confidence:** Very high
- **One-sentence claim:** FTITC restoration sends saved bootstrap rows through the current session’s limit-dependent filter, so identical saved results can reload with different row counts and uncertainty.
- **Mathematical expectation:** persisted uncertainty rows and their replicate identities must round-trip without re-evaluation under unrelated current fitting preferences.
- **Actual implementation behaviour:** `FTITCReader.cs:883-899` routes saved snapshot/legacy/parameter rows through `SetBootstrapSolutions`; `Models.cs:563-571` explicitly provides `RestoreBootstrapSolutions` to avoid re-filtering, and `FTXTCReader.cs:377-395` correctly uses it.
- **Scientific impact:** opening a file under Standard rather than Extended/No-limit policy can remove rows, change intervals, and change global/member pairing even though the underlying file is unchanged.
- **Suggested correction:** restore FTITC rows verbatim with persisted replicate IDs/status, using the FTXTC restoration contract; never reapply session limits during deserialization.
- **Verifier conclusion:** independently traced all FTITC bootstrap representations to the re-filtering path.
- **Rebutter conclusion:** found existing round-trip tests use interior rows and therefore miss the defect.
- **Final consensus status:** **Confirmed** as High; discovered during the adversarial false-negative search.

### F-006 — persistence-only model affinities mix log and linear coordinates

- **Classification:** Confirmed defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** three restorable legacy models initialize/use an affinity coordinate as `log10(Ka)` but evaluate and report that raw value as linear (K_a).
- **Mathematical expectation:** a persisted affinity coordinate must have one versioned convention; a log coordinate (a) must enter evaluation as (10^a\,M^{-1}).
- **Actual implementation behaviour:** `OneSetOfSitesSyringeUncertaintyModel.cs`, `OneSiteIsomerization.cs`, and `TwoCompetingSites.cs` call `GuessLogAffinity` while active evaluation/reporting treats the value linearly; `FTXTCPersistenceRegistry.cs:171-195` can restore all three. Commit `18d5dbe` changed the guesses without a corresponding evaluator migration.
- **Scientific impact:** a coordinate value `6` can mean \(10^6\,M^{-1}\) in persistence/editor semantics but be evaluated as `6 M^-1`, a \(166{,}667\times\) Kd discrepancy.
- **Suggested correction:** introduce format/model-version-aware migration, then use a single modern log convention; do not blindly exponentiate genuinely old linear files.
- **Verifier conclusion:** independently confirmed both code mismatch and hidden persistence reachability.
- **Rebutter conclusion:** scope is limited because these models are not selectable for new analyses; old linear values require careful migration.
- **Final consensus status:** **Confirmed**, scoped to legacy-restored models.

### F-007 — restored isomerization model does not implement its named mechanism

- **Classification:** Confirmed defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** `OneSiteIsomerization.Evaluate` uses an ordinary apparent one-site curve and does not use the cis population, coupled-isomer routines, or isomerization heat.
- **Mathematical expectation:** the named coupled model must normalize both isomer populations, solve coupled binding/isomer equilibria, and include binding and redistribution enthalpies without double counting.
- **Actual implementation behaviour:** `OneSiteIsomerization.cs:42-126` contains disconnected/dead routines while the active path at `:128-156` is one-site; changing the population leaves every predicted heat unchanged while derived intrinsic quantities change.
- **Scientific impact:** restored results may look model-specific while their curve is insensitive to the defining population parameter.
- **Suggested correction:** complete and independently test the coupled model or reject restoration/evaluation as unsupported.
- **Verifier conclusion:** confirmed the population is created/reported but absent from active heat evaluation.
- **Rebutter conclusion:** persistence-only reachability reduces overall exposure but does not make affected outputs valid.
- **Final consensus status:** **Confirmed**, scope-limited.

### F-008 — integration excludes the exact end-boundary sample

- **Classification:** Data-handling defect
- **Severity:** Low
- **Confidence:** Very high
- **One-sentence claim:** right-point integration matches the acquisition convention, but the strict end predicate excludes a sample whose trailing averaging interval belongs to the integration window.
- **Mathematical expectation:** each recorded power is the average over a trailing acquisition/filter interval ending at its timestamp, so heat is accumulated as `P_i*(t_i-t_previous)` over `(integrationStart,integrationEnd]`.
- **Actual implementation behaviour at the audited revision:** `InjectionData.cs:283-302` uses `dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime`, excluding a sample exactly at the end boundary while otherwise applying the intended right-point sum with actual timestamp differences.
- **Scientific impact:** ordinarily negligible because a valid integration window ends only after the recorded baseline-corrected signal has returned to approximately zero; it matters only when the selected end cuts through a nonzero trailing-average sample.
- **Correction implemented in the working tree:** make the end comparison inclusive and document the trailing-average/right-endpoint convention; no trapezoidal interpolation or public interface change is required.
- **Verifier conclusion:** confirmed the strict end exclusion and the one-line inclusive-boundary correction.
- **Rebutter conclusion:** the original trapezoidal criticism was rejected after establishing the trailing-average acquisition semantics; zero-valued boundary samples strongly mitigate the remaining issue.
- **Final consensus status:** **Confirmed** as Low and limited to the end-boundary convention; resolved in the working tree.

### F-009 — irregular-time injection uncertainty uses a mean timestep

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** processing-derived heat uncertainty uses one experiment-wide mean timestep and sample count rather than the actual quadrature weights/timestamp lags.
- **Mathematical expectation:** for weights \(w_i\), integrated-power variance is \(\sum_{ij}w_iw_j\operatorname{Cov}(P_i,P_j)\), with covariance based on actual time lag.
- **Actual implementation behaviour:** `InjectionData.cs:439-453` and `ExperimentData.cs:81-88` substitute a mean `TimeStep`. With independent unit noise and interval weights `1 s` and `8 s`, the correct signal SD is `8.062`; a containing trace with mean `10/3 s` makes the code term `4.714` before baseline contribution.
- **Scientific impact:** weighted fits can over- or underweight injections with missing/nonuniform timestamps.
- **Suggested correction:** reuse actual integration weights and a time-aware covariance model, or reject irregular series.
- **Verifier conclusion:** confirmed dimensional/statistical mismatch and a numerical example.
- **Rebutter conclusion:** uniform instrument sampling is the dominant mitigating case.
- **Final consensus status:** **Confirmed**, scope-limited to nonuniform timestamps.

### F-010 — LOO spread is not jackknife uncertainty

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** delete-one estimates are summarized with bootstrap RMS-from-primary and raw percentiles, yet some result surfaces present them as SD/95% CI rather than sensitivity diagnostics.
- **Mathematical expectation:** jackknife SE requires the delete-one mean and `(n-1)/n` scaling (or pseudovalues); otherwise the output must be explicitly labelled delete-one influence/spread.
- **Actual implementation behaviour:** `Solver2.cs:659-727,962-1019` passes LOO rows to `Models.cs:551-560` and `NumberStructs.cs:82-100,158-178`, with no jackknife centering/scaling. For a sample-mean problem it understates jackknife SE by `sqrt(n-1)`.
- **Scientific impact:** users can substantially understate uncertainty if they treat the displayed LOO SD/CI as a sampling interval.
- **Suggested correction:** implement jackknife estimates or relabel and segregate LOO as robustness/influence analysis.
- **Verifier conclusion:** independently derived and confirmed the missing jackknife operations.
- **Rebutter conclusion:** bundled help/manual describe robustness and document RMS-from-primary, reducing severity; the misleading SD/CI surfaces remain.
- **Final consensus status:** **Confirmed** as a statistical interpretation/reporting defect, Medium rather than High.

### F-011 — derived thermodynamic uncertainty discards covariance

- **Classification:** Scientific limitation
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** derived values such as \(-T\Delta S=\Delta G-\Delta H\) combine marginal uncertainties in quadrature instead of summarizing the paired replicate-derived distribution.
- **Mathematical expectation:** \(\operatorname{Var}(G-H)=\operatorname{Var}G+\operatorname{Var}H-2\operatorname{Cov}(G,H)\), and nonlinear derived intervals should be computed per retained replicate.
- **Actual implementation behaviour:** model solution classes first summarize each coordinate, then `NumberStructs.cs:238-275` applies covariance-free `FloatWithError` arithmetic. Perfectly correlated equal changes should yield zero SD for the difference but production reports `sqrt(2)*sigma`.
- **Scientific impact:** entropy-related and other derived uncertainty can be severely over- or understated.
- **Suggested correction:** compute each derived quantity row-wise using paired replicate IDs and report empirical intervals.
- **Verifier conclusion:** independently confirmed the covariance term is absent.
- **Rebutter conclusion:** audited manual `06-fitting-models.md:211-213` explicitly discloses generic propagation; this is a documented method limitation, not a hidden formula mismatch.
- **Final consensus status:** **Confirmed limitation**, not reclassified as an implementation bug.

### F-012 — concentration “SD” is used as a uniform half-width

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** cell/syringe concentration fields called SD are sampled uniformly within `±SD`, giving an actual SD only `SD/sqrt(3)`.
- **Mathematical expectation:** an entered standard deviation \(s\) must parameterize a distribution whose SD is \(s\), subject to an explicit positive-concentration model.
- **Actual implementation behaviour:** `ExperimentData.cs:545-566` uses `1 + Uniform(-1,1)*FractionSD`; the UI calls the values concentration SD. A declared 10% SD produces a realized `5.778%` SD and bounds `±10%`.
- **Scientific impact:** the concentration component of reported parameter uncertainty is systematically understated by 42.3% in SD terms.
- **Suggested correction:** use normal/lognormal sampling with the entered SD, or relabel as uniform half-range and transform accordingly.
- **Verifier conclusion:** a 20,000-draw probe reproduced the expected `s/sqrt(3)` distribution.
- **Rebutter conclusion:** found inconsistent normal sampling for another model-option uncertainty, not a compensating convention.
- **Final consensus status:** **Confirmed** as Medium.

### F-013 — global shared locks are not unlocked for uncertainty refits

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** `UnlockBootstrapParameters=true` unlocks local clone coordinates but leaves global/shared coordinates locked in bootstrap and LOO clones.
- **Mathematical expectation:** the documented option must allow every fitted locked coordinate, including a shared global coordinate, to vary in error-estimation refits while member coordinates remain globally linked.
- **Actual implementation behaviour:** local copying at `Models.cs:289-297` clears locks; `GlobalModel.cs:152-155,181-184` copies global lock state. Direct probe: requested `true`, primary/global clone remained `locked=True`, `fitted=False`, while a local clone unlocked.
- **Scientific impact:** dependent global/member uncertainty can be understated by conditioning on fixed shared values contrary to the user’s selection.
- **Suggested correction:** clear copied global locks under the option and test both bootstrap and global LOO.
- **Verifier conclusion:** independently reproduced the clone state through the production assembly.
- **Rebutter conclusion:** no UI timing compensation exists; scope requires global fit + locked shared parameter + error estimation + unlock.
- **Final consensus status:** **Confirmed** as Medium because primary estimates are unaffected and exposure is conjunctive.

### F-014 — concentration-enabled LOO can schedule zero refits

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** concentration-enabled LOO uses integer division of requested iterations by included injections, so a supported request can create zero models.
- **Mathematical expectation:** every omitted injection must receive at least one refit, or an insufficient budget must be rejected explicitly.
- **Actual implementation behaviour:** `Solver2.cs:668-679` computes `BootstrapIterations / includedInjectionCount`; 10 iterations with 20 injections gives zero loops and a `NotRun` outcome.
- **Scientific impact:** the user can request LOO uncertainty and receive none without the configuration being rejected up front.
- **Suggested correction:** require at least one per omission and distribute remainder, or enforce `B>=n` in validation.
- **Verifier conclusion:** confirmed the reachable 10/20 configuration.
- **Rebutter conclusion:** no minimum is enforced by the fit UI.
- **Final consensus status:** **Confirmed** as Medium.

### F-015 — limit-terminated uncertainty refits are accepted without a quality gate

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** bootstrap/LOO refits receive one-third of the primary budget and all iteration-, evaluation-, or time-limit terminations are counted as usable unless failed/cancelled.
- **Mathematical expectation:** uncertainty draws should be converged to a documented objective/parameter-stability criterion, or nonconverged best-so-far rows must be separately flagged and excluded/retried under a defensible policy.
- **Actual implementation behaviour:** `Solver2.cs` applies reduced budgets; `SolverUtilities.cs:219-237` defines usable as not failed/stopped, with no distance/stability/boundary gate.
- **Scientific impact:** incomplete optimizations can broaden, truncate, or skew parameter distributions while appearing as ordinary successes.
- **Suggested correction:** report termination categories, retry with expanded budget, and retain only rows passing objective/parameter stability checks.
- **Verifier conclusion:** confirmed code path and lack of quality threshold.
- **Rebutter conclusion:** the policy is deliberate for performance and limited fits can be useful; intent does not establish statistical equivalence to converged draws.
- **Final consensus status:** **Confirmed** as a Medium robustness defect; empirical frequency remains unmeasured.

### F-016 — two-site uncertainty summaries do not align label-swapped refits

- **Classification:** Statistical-method defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** the two-site likelihood is invariant to swapping complete site tuples, but bootstrap summaries aggregate fixed site labels without canonicalizing equivalent refits.
- **Mathematical expectation:** each replicate must be assigned to the primary site labels by a complete tuple permutation before marginal CIs/correlations; the known symmetry is not a second biological result.
- **Actual implementation behaviour:** `TwoSetsOfSites.cs:237-253` directly collects site-1/site-2 coordinates. A primary `(logK1,logK2)=(8,5)` plus equivalent refits `(8,5)` and `(5,8)` gives site-1 SD `2.121` log units although aligned uncertainty is zero.
- **Scientific impact:** noisy/weakly identified fits can show artificial bimodality, inflated site-specific intervals, and corrupted correlations.
- **Suggested correction:** minimum-cost complete-tuple alignment to the primary (with deterministic tie-breaking) before summaries, correlations, and persistence.
- **Verifier conclusion:** independently confirmed exact symmetry and absence of canonicalization.
- **Rebutter conclusion:** initialization at the primary labels reduces switching frequency but cannot eliminate the symmetry; actual field frequency was not measured.
- **Final consensus status:** **Confirmed** as Medium, rather than High, because the implementation defect is certain but exposure frequency was not quantified.

### F-017 — competitive input label asks for the wrong physical quantity

- **Classification:** Documentation or labelling defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** the UI/manual describe competitor concentration as already bound/prebound ligand, while the equilibrium equation consumes total analytical competitor concentration.
- **Mathematical expectation:** competitive mass balance needs \(B_{total}=B_{free}+PB\), not the initial concentration of bound complex alone.
- **Actual implementation behaviour:** `ExperimentAttribute.cs:53-58` and manual text say bound/prebound; `CompetitiveBinding.cs:92-128` uses `rB=B0/(nP0)` as total competitor.
- **Scientific impact:** following the label can supply the wrong input and bias intrinsic target affinity/enthalpy.
- **Suggested correction:** label “total pre-equilibrated competitor concentration in the cell (free + bound)” and explain equilibration.
- **Verifier conclusion:** independently confirmed the semantic mismatch and quantified material depletion.
- **Rebutter conclusion:** the mass-balance algebra itself is correct when total competitor is supplied.
- **Final consensus status:** **Confirmed** as Medium because user input can alter the fit.

### F-018 — competitive apparent Kd uses total rather than free competitor

- **Classification:** Confirmed defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** the displayed apparent affinity uses \(K_{d,A}(1+K_BB_{total})\) where the conventional competitive factor requires free competitor at the stated equilibrium.
- **Mathematical expectation:** \(K_{d,app}=K_{d,A}(1+K_BB_{free})\), unless explicitly labelled a total≈free/excess approximation.
- **Actual implementation behaviour:** `CompetitiveBinding.cs:177-207,231-268` inserts the option’s total competitor. At `P_total=B_total=10 µM`, `K_B=10^6 M^-1`, exact `B_free=2.70156 µM`; production overstates apparent Kd by `2.97x`.
- **Scientific impact:** the displayed derived quantity can be materially false under finite depletion; intrinsic fitted heat-model parameters are unaffected.
- **Suggested correction:** solve initial `B_free` from the same mass balance or label the value as an excess-competitor approximation.
- **Verifier conclusion:** independently reproduced the finite-depletion discrepancy (up to `31.16x` in a stronger example).
- **Rebutter conclusion:** scope is display-derived only and the approximation is valid when competitor is in overwhelming excess.
- **Final consensus status:** **Confirmed** as Medium.

### F-019 — dissociation model ignores tandem segment pre-state

- **Classification:** Confirmed defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** the dissociation model uses the previous injection’s post-state at every `i>0` and bypasses the segment-aware pre-state used by other models.
- **Mathematical expectation:** the first injection after tandem reload must begin from stored/back-mixed segment initial concentrations.
- **Actual implementation behaviour:** `Dissociation.cs:58-77` does not use `Data.Segments`/`GetReferencePreStateConcentrations` (`Models.cs:97-124`).
- **Scientific impact:** the first heat after a tandem boundary can use the wrong dimer population, compounded if stocks differ.
- **Suggested correction:** use segment-aware pre-state concentrations while preserving explicit syringe-dimer inflow.
- **Verifier conclusion:** confirmed the bypass and boundary mismatch.
- **Rebutter conclusion:** ordinary non-tandem plug-replacement accounting is defensible and was withdrawn from the claim.
- **Final consensus status:** **Confirmed**, limited to tandem transitions.

### F-020 — global “RMSD” is a sum of member RMSDs

- **Classification:** Confirmed defect
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** `GlobalModel.Loss` adds each member RMSD and the UI labels the sum RMSD, although a pooled RMSD is `sqrt(total SSE/total n)`.
- **Mathematical expectation:** one global RMSD must weight all included residuals by count and remain invariant when an identical dataset is duplicated.
- **Actual implementation behaviour:** `GlobalModel.cs:119-129`, `Models.cs:251-265`, and `Solver2.cs:776-780`; two equal-size member RMSDs 1 and 3 report 4 rather than `sqrt(5)=2.236`.
- **Scientific impact:** global fit quality scales with member count and cannot be compared correctly across workspaces; fitted parameters remain correct because optimization sums SSE.
- **Suggested correction:** compute pooled RMSD, or label the current value “sum of member RMSDs.”
- **Verifier conclusion:** independently reproduced the formula and UI label.
- **Rebutter conclusion:** confirmed the optimizer objective is sound, limiting impact to diagnostics/reporting.
- **Final consensus status:** **Confirmed** as Medium.

### F-021 — Restore Defaults silently selects the coarse “Fast” tolerance

- **Classification:** Numerical-stability defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** startup initializes Balanced, but Reset/Restore Defaults selects Fast, which can report convergence on noiseless data with materially inaccurate parameters.
- **Mathematical expectation:** a factory/default reset should reproduce the safe startup default or clearly warn that it selects a coarse approximation.
- **Actual implementation behaviour:** `AppSettings.cs:79-80,293-303` and preference state map reset tolerance to Fast. A sequential NM probe from the same start reported Fast log-affinities `6.39923/5.32420` versus truths `6.3/5.2` (`+25.6%/+33.1%` in Ka), while Balanced recovered `6.29857/5.19822`.
- **Scientific impact:** users restoring defaults can receive materially biased parameters with a “Converged” status.
- **Suggested correction:** make reset/startup both Balanced or Strict, document numeric tolerances, and distinguish coarse convergence from precision convergence.
- **Verifier conclusion:** independent one-site and sequential probes confirmed accuracy loss, strongest for NM.
- **Rebutter conclusion:** Fast is an explicit speed/accuracy preset and LM was less affected; the defect is the inconsistent factory-reset choice, not all optimizer use.
- **Final consensus status:** **Confirmed** as Medium.

### F-022 — comma-delimited legacy exports use current-culture numbers

- **Classification:** Data-handling defect
- **Severity:** Low
- **Confidence:** High
- **One-sentence claim:** MicroCal/PyTC writers join fields with commas while numeric `ToString()` is culture-sensitive.
- **Mathematical expectation:** machine-readable interchange numbers must use invariant culture independent of UI locale.
- **Actual implementation behaviour:** `Exporter.cs:248-350`; under `da-DK`, `1.25` becomes `1,25` and creates extra columns.
- **Scientific impact:** direct Core use or a front end that does not force `en-US` can emit malformed files.
- **Suggested correction:** pass `InvariantCulture` for every machine-readable numeric field.
- **Verifier conclusion:** confirmed by construction and a Core harness.
- **Rebutter conclusion:** shipped Avalonia/macOS front ends force `en-US`, substantially mitigating normal GUI exposure.
- **Final consensus status:** **Confirmed**, downgraded to Low.

### F-023 — advertised molar integrated-reader mode divided by 1000

- **Classification:** Data-handling defect
- **Severity:** Low
- **Confidence:** Very high
- **One-sentence claim:** `IntegratedHeatReader.ReadFile(..., concentrationsAreMilliMolar:false)` does not honor raw-molar inputs for stored concentrations.
- **Mathematical expectation:** when the flag is false, M input must remain M.
- **Actual implementation behaviour at the audited revision:** `IntegratedHeatReader.cs:20-26,87-101,137-171` computes `concScale` but unconditionally divides cell and injection `Mt/Xt` values by 1000; the scale cancels in the only helper receiving it.
- **Scientific impact:** public/API-only callers using raw-M mode store concentrations 1000 times too small; active application calls default mM mode.
- **Correction implemented in the working tree:** `concScale` is applied consistently to the initial and injection `Mt/Xt` states and to trajectory-derived syringe concentration; paired M/mM fixtures assert identical internal values.
- **Verifier conclusion:** independently confirmed the unused/cancelled scale.
- **Rebutter conclusion:** no current GUI caller passes false, so exposure is latent.
- **Final status:** **Confirmed as Low at the audited revision; resolved in the working tree.**

### F-024 — PEAQ power comment contradicts the likely encoded unit

- **Classification:** Documentation or labelling defect
- **Severity:** Low
- **Confidence:** High
- **One-sentence claim:** the PEAQ reader comment says DP is µcal/s while fixture magnitudes and conversion support cal/s encoding of values numerically around a few `e-6`.
- **Mathematical expectation:** encoded and physical units must be stated unambiguously next to conversion.
- **Actual implementation behaviour:** `PEAQReader.cs:119-140` converts with calories; fixture `5.18682e-6 cal/s` is about `5.19 µcal/s`, a plausible ITC power.
- **Scientific impact:** maintainers could “fix” correct code by a factor of (10^6); current numeric behavior is likely correct.
- **Suggested correction:** state “stored in cal/s (typically a few ×10^-6 cal/s)” and validate against a vendor schema.
- **Verifier conclusion:** fixture scale supports production conversion.
- **Rebutter conclusion:** successfully rebutted the initial factor-(10^6) code-defect suspicion.
- **Final consensus status:** **Confirmed comment defect; numeric-conversion allegation is a false alarm.**

## 7. Probable or unresolved defects

### P-001 — tandem merge lacks a same-stock precondition

- **Classification:** Probable defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** tandem concatenation silently applies the first experiment’s cell/syringe concentrations to every segment without validating that later stocks match.
- **Mathematical expectation:** either all segments must be proven to share stock/composition/conditions, or per-segment stock metadata must drive injected mass and back-mixing.
- **Actual implementation behaviour:** `TandemConcatenationTool.cs:157-209,275-346` copies first-source concentrations and uses one `Cs/M0`; validation checks format, volume, and missing data, not stock concentration.
- **Scientific impact:** selecting 100 µM then 200 µM stock models segment 2 at 100 µM, halving injected moles.
- **Suggested correction:** enforce equality within tolerance or represent per-segment stocks.
- **Verifier conclusion:** confirmed the call path and missing validation.
- **Rebutter conclusion:** same-stock reloads are the intended/common protocol and existing same-stock recurrence is correct.
- **Final consensus status:** **Probable**, because changed-stock support/intent is not explicit enough to call the matched-protocol algorithm wrong.

### P-002 — blank/buffer subtraction lacks protocol compatibility checks

- **Classification:** Probable defect
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** matched/linear/exponential blank subtraction uses injection ordinal and absolute heat without validating injection amount, composition, cell volume, or schedule.
- **Mathematical expectation:** absolute blank heat is transferable only under matched protocols; otherwise subtraction must be normalized/scaled under an explicit model.
- **Actual implementation behaviour:** `BufferSubtraction.cs:164-178,181-321` and `InjectionData.cs:357-385` check inclusion/integration but not physical compatibility; an out-of-range target can reuse the first/last reference value.
- **Scientific impact:** a 2 µL target can subtract the absolute heat of a 10 µL blank, biasing all downstream fit parameters.
- **Suggested correction:** validate protocol metadata or regress heat per injected amount with documented assumptions.
- **Verifier conclusion:** independently traced the ordinal mapping and permissive UI.
- **Rebutter conclusion:** real-data tests and ordinary use employ matched 56-injection protocols, under which the operation is defensible.
- **Final consensus status:** **Probable**, scoped to mismatched protocols.

### P-003 — comprehensive empirical interval coverage remains unresolved

- **Classification:** Test gap
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** no sufficiently large simulation study establishes nominal coverage for bootstrap/LOO intervals across nonlinear, bounded, global, and heteroscedastic cases.
- **Mathematical expectation:** documented interval semantics should be checked by repeated synthetic experiments with known parameters and predeclared coverage tolerances.
- **Actual implementation behaviour:** existing tests check mechanics/round trips and the audit used deterministic probes, not a large coverage campaign.
- **Scientific impact:** additional undercoverage/overcoverage may remain after the confirmed implementation defects are fixed.
- **Suggested correction:** add reproducible coverage suites by model/regime, reporting Monte Carlo uncertainty.
- **Verifier conclusion:** all statistical reviewers found the coverage gap.
- **Rebutter conclusion:** small simulations cannot decisively establish coverage and should not be overinterpreted.
- **Final consensus status:** **Unresolved test gap**, not a confirmed interval failure beyond F-004/F-010–F-016.

## 8. Statistical, weighting, bootstrap, and LOO audit

### Reconstructed implemented procedure

| Component | Implemented behavior | Audit conclusion |
|---|---|---|
| Primary estimate | Original-data best fit remains the reported central value. | **Intentional design; correct and preserved.** No bootstrap mean/median replacement was found. |
| Unweighted objective | Sum of squared absolute-heat residuals. | Correct. |
| Weighted objective | \(\sum_i (r_i/\sigma_i)^2\); invalid/nonpositive sigma receives a safe fallback. | Correct use of standard deviation, applied once. |
| Displayed local loss | Unweighted `1e6*sqrt(SSE/n)` even for weighted fits. | Intentional and consistently useful for cross-fit comparison. Global aggregation alone is wrong (F-020). |
| Residual bootstrap | Residuals are centered and standardized; resampling is within each member experiment; synthetic residuals are rescaled by target sigma; the original weighting is reapplied. | Sound basic heteroscedastic residual-bootstrap construction. |
| Bootstrap starting point | Each replicate starts from the primary solution rather than the preceding replicate. | Correct; avoids a random walk/stale solution chain. |
| Exclusions | Excluded injections remain excluded in generated/refitted data. | Correct. |
| Concentration perturbation | Independent cell/syringe multiplicative uniform perturbations per experiment. | SD semantics wrong (F-012); shared-stock covariance unsupported (L-002). |
| Failed/cancelled fits | Failed/cancelled rows are excluded. Limit-terminated rows are accepted. | F-015. |
| Boundary filtering | Rows within 1% of reconstructed default bounds are removed after success counting. | F-004; invalidates count/sample consistency. |
| Marginal “SD” | RMS displacement from the primary, denominator \(B\), not sample SD around the bootstrap mean. | Explicitly documented and coherent as displacement, but must not be confused with conventional sample SD. |
| Marginal CI | Empirical 2.5/97.5 percentiles of retained fitted coordinates; primary remains the displayed center. | Coherent percentile endpoints, subject to row filtering and label alignment. |
| Derived errors | Arithmetic on marginal `FloatWithError` values. | Covariance loss, F-011. |
| Correlations | Pearson correlations on matched successful bootstrap rows; minimum population 30. | Correct for the retained matched residual-bootstrap population, provided F-004 is fixed. |
| Prediction bands | Predictions from actual replicate models, not parameter-wise linearization. | Correct. The offset-corrected graph uses a fixed primary-offset coordinate; see FA-005. |
| Single-fit LOO | Exactly one included injection omitted at a time; pre-excluded injections stay excluded. | Deletion mechanics correct; aggregation semantics F-010 and zero-loop F-014. |
| Global LOO | Exactly one member experiment omitted at a time. | Coherent sensitivity analysis, not a jackknife CI without correct scaling/labels. |
| Randomness | Streams are intentionally non-replicable from the UI; no saved seed contract. | Acceptable design, but a deterministic test hook is needed. |
| Persistence | Primary values preserved; FTXTC uses a restoration path; FTITC re-filters. | F-005. |

The most consequential statistical failure is not the deliberate primary-central rule, denominator \(B\), or unweighted displayed local RMSD. It is **sample identity and semantics**: which rows count, which rows are retained by each member, whether the same rows survive reload, and whether derived/site-specific quantities use the correct paired/canonical representation.

### Global and member weighting

The global optimizer concatenates every member’s residual contribution and sums member objectives. For unweighted fitting, this is pooled SSE: datasets with more included injections receive proportionally more contribution, as ordinary observation-level least squares implies. For weighted fitting, each valid point contributes its standardized square. No accidental averaging-by-member was found in the actual fit objective. The incorrect sum-of-member-RMSDs is assigned only after fitting for display/status (F-020).

### Identifiability versus implementation error

Large correlations in \(N,\Delta H,K_a\), two-site label permutation, weakly separated sequential steps, and competitive parameter tradeoffs are intrinsic scientific limitations. They become implementation defects only where the program reports an artificial coordinate-dependent summary—most clearly F-016—or fails to propagate the actual paired distribution (F-011).

## 9. Data handling, units, processing, subtraction, serialization, and export

### Dimensional trace

| Quantity | Internal convention / transformation | Conclusion |
|---|---|---|
| Power | Reader-specific input converted to W (J/s). | MicroCal/TA/Nano fixture scales are coherent; PEAQ comment needs correction (F-024). |
| Absolute injection heat | Right-point integral of trailing-average, baseline-corrected power, stored in J. | Unit and quadrature convention are correct; the audited revision excludes the exact end-boundary sample (F-008). |
| Display heat | Absolute heat converted to µJ or divided by injected moles for molar heat. | Correct when `InjectionMass` is correct. |
| Enthalpy | Internal J/mol; UI/export may display kJ/mol or kcal/mol. | Active models consistent. |
| Volume | Internal L; source metadata may be µL or mL. | At the audited revision, integrated inference embedded a relative-unit convention (F-001); the working-tree reader now derives volume from `Mt`. |
| Concentration | Internal mol/L; source inputs commonly mM. | Audited `.aff/.dat` issues F-001/F-023 are corrected in the working tree through trajectory inference and consistent scaling. |
| Temperature | Internal absolute K in thermodynamic formulae; readers convert Celsius where specified. | Correct. |
| Affinity | Active fitted coordinate is generally \(\log_{10}K_a\); evaluator uses \(10^a\); Kd is \(1/K_a\). | Correct in active models; legacy exceptions F-006. |
| Free energy | \(\Delta G=-RT\ln K_a\) with 1 M standard-state convention implicit in the dimensionless log. | Correct units/sign in active models. |
| Entropy-related display | \(-T\Delta S=\Delta G-\Delta H\). | Central value correct; uncertainty limitation F-011. |

### Sign and injection conventions

The active models compute total cell reaction heat before/after each injection, then use the repository’s symmetric MicroCal-style overflow correction

\[
q_i=Q_i-Q_{i-1}+\frac{v_i}{2V_0}(Q_i+Q_{i-1})
\]

plus an injection offset scaled by the repository convention. This is an explicit active-cell approximation, not the exact exponential/plug solution for every physical instrument. Within that convention, one-set, two-set below the cap, sequential, and competitive in well-conditioned regimes agree with the independent derivation. The dissociation model uses an explicit plug-replacement reaction-extent balance, also defensible for ordinary injections.

Raw-reader sign conversions, first-injection inclusion state, injection-volume storage, FTITC/FTXTC primary values, exclusions, and reference links were not found to double-normalize or reverse heat. F-001 demonstrates why dimensional value assertions are necessary; the working-tree tests now assert heat, concentration, volume, and injection mass for the real fixtures.

### Processing and transformations

- Baseline and integration state flow correctly into `InjectionData`; F-008 is limited to the exact end boundary, while F-009 affects uncertainty weighting for irregular data.
- Buffer subtraction preserves sign algebraically and avoids nested/self references, but P-002 shows the physical protocol precondition is not enforced.
- Same-stock tandem back-mixing and numbering pass existing tests; P-001 and F-019 cover changed-stock and dissociation-boundary gaps.
- Generic CSV uses invariant formatting; legacy MicroCal/PyTC writers do not (F-022).
- FTITC/FTXTC round trips preserve ordinary central estimates and interior rows; FTITC uncertainty restoration is not session-independent (F-005).
- No evidence was found that derived parameters are deserialized as new independent fitted parameters in the active FTXTC path.

## 10. Global fitting and temperature dependence

### Parameter topology and objective

The audit traced local, shared, and temperature-dependent coordinates from `AnalysisBuilder` into one optimizer vector and back to member models. Shared coordinates are not duplicated as independent optimizer variables; locked/local/global status and solved values propagate correctly for primary fits. The global objective is the sum of member SSE/weighted SSE contributions. No stale primary solution overwrite, accidental derived-coordinate fitting, or member-order corruption was reproduced on the ordinary paths.

Confirmed global-specific problems are F-004 (replicate populations), F-005 (FTITC reload), F-013 (shared unlock), and F-020 (reported RMSD). Shared-stock covariance is L-002.

### L-001 — constant-ΔG temperature law is phenomenological, not van’t Hoff coupled

- **Classification:** Scientific limitation
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** the documented temperature constraint uses one constant \(\Delta G\) for affinity while independently varying enthalpy with \(\Delta C_p\), so the reported trio is not a thermodynamically integrated state-function model.
- **Mathematical expectation:** a coupled Kirchhoff/van’t Hoff model links \(\ln K_a(T)\), \(\Delta H_{ref}\), and \(\Delta C_p\); equivalently \(d\ln K/dT=\Delta H/(RT^2)\).
- **Actual implementation behaviour:** `GlobalConstraintSemantics.cs:105-147` uses \(\Delta H(T)=\Delta H_{ref}+\Delta C_p(T-T_{ref})\) and \(K_a(T)=\exp[-\Delta G/(RT)]\) with a constant shared \(\Delta G\). Audited manual `07-multiple-experiments.md:67-73` states exactly this rule.
- **Scientific impact:** users may overinterpret \(\Delta G,\Delta H,\Delta C_p\) as a single self-consistent thermodynamic state function. For one test parameter set, production and an integrated relation differed by factors about `0.620` at 283.15 K and `2.059` at 313.15 K.
- **Suggested correction:** label the existing option “constant-ΔG phenomenological constraint,” warn against coupled interpretation, and offer a separate integrated van’t Hoff/\(\Delta C_p\) model.
- **Verifier conclusion:** confirmed the analytic difference.
- **Rebutter conclusion:** code, tests, and documentation agree; it is not an implementation mismatch.
- **Final consensus status:** **Intentional documented behavior with a Medium scientific limitation**, not a defect in the stated rule.

Reference temperature and absolute-temperature handling are correct. There was no evidence of Celsius entering \(RT\), sign inversion of \(\Delta G\), or accidental duplication of a temperature-dependent coordinate.

## 11. Scientific limitations and identifiability warnings

### L-002 — shared-stock concentration covariance cannot be represented

- **Classification:** Scientific limitation
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** global concentration uncertainty always draws independent member factors because the data model has no stock/preparation identity or covariance.
- **Mathematical expectation:** experiments prepared from one stock need a common calibration component plus independent preparation components; independently prepared experiments should remain independent.
- **Actual implementation behaviour:** `GlobalModel.cs:136-145` calls `ExperimentData.cs:545-566` separately for each member.
- **Scientific impact:** a truly common stock error can incorrectly average down by roughly `sqrt(m)` in a shared-parameter fit.
- **Suggested correction:** optional stock-group IDs and hierarchical/common-plus-independent perturbations.
- **Verifier conclusion:** confirmed independent draws and missing metadata.
- **Rebutter conclusion:** universal correlation would also be wrong; independence is correct for separate preparations.
- **Final consensus status:** **Scientific limitation**, conditional on shared stocks.

### L-003 — active-cell dilution correction is a documented approximation

- **Classification:** Scientific limitation
- **Severity:** Medium
- **Confidence:** High
- **One-sentence claim:** the common incremental-heat correction is a symmetric active-cell approximation rather than an exact universal flow/mixing law.
- **Mathematical expectation:** exact concentration/heat evolution depends on instrument-specific overflow, mixing, injection profile, and active volume.
- **Actual implementation behaviour:** `Models.cs:97-125` applies the same symmetric correction to active binding models while reader concentration evolution offers named MicroCal/exponential laws.
- **Scientific impact:** systematic heat/concentration bias may remain for imperfect mixing, continuous injection, or miscalibrated active volume even when algebra is correct.
- **Suggested correction:** document the assumed instrument law, expose calibrated active volume/mixing alternatives, and validate against Tellinghuisen/Dumas regimes.
- **Verifier conclusion:** derivation matches the implemented convention.
- **Rebutter conclusion:** no evidence of a coding mismatch under that convention.
- **Final consensus status:** **Limitation**, not a confirmed formula defect.

### L-004 — intrinsic nonlinear identifiability limits

- **Classification:** Scientific limitation
- **Severity:** Medium
- **Confidence:** Very high
- **One-sentence claim:** several models can fit nearly equivalent heat curves with strongly correlated or permutation-equivalent parameters even when the forward formula is correct.
- **Mathematical expectation:** identifiability depends on the c-value, concentration calibration, saturation range, noise, competing-ligand depletion, and separation of step/site affinities.
- **Actual implementation behaviour:** the optimizer exposes \(N,\Delta H,\log K_a\), offsets, correction factors, multiple site tuples, and competitor parameters without a universal identifiability diagnostic.
- **Scientific impact:** narrow numerical convergence does not imply a unique biological interpretation; two-site and sequential/competitive decompositions can be weakly determined.
- **Suggested correction:** profile likelihoods/loss maps, multi-start fits, condition/Jacobian diagnostics, and explicit symmetry-aware summaries.
- **Verifier conclusion:** oracle loss-surface and recovery work observed expected degeneracies, not equation errors.
- **Rebutter conclusion:** this is intrinsic to the data/model and cannot be repaired by relabelling an optimizer status alone.
- **Final consensus status:** **Scientific limitation**; report alongside, not as, a coding defect.

The two-site model’s complete site permutation is intentionally one physical result. F-016 is specifically the program’s failure to respect that fact in uncertainty summaries. The legacy isomerization/two-competitor problems are implementation failures, not merely weak identifiability.

## 12. Important false alarms investigated and resolved

### FA-001 — primary estimates are not replaced by bootstrap means

- **Classification:** Intentional design
- **Severity:** Low
- **Confidence:** Very high
- **One-sentence claim:** retaining the original-data optimum as the displayed value is intentional and correctly implemented.
- **Mathematical expectation:** under the stated convention, resamples estimate dispersion/intervals and must not overwrite the primary model.
- **Actual implementation behaviour:** primary solution remains central; bootstrap/LOO models are stored separately.
- **Scientific impact:** none; recommending an unconditional bootstrap-mean replacement would contradict the required design.
- **Suggested correction:** none; retain a regression test.
- **Verifier conclusion:** confirmed through solution flow and persistence.
- **Rebutter conclusion:** no stale/failed replicate overwrite path was found.
- **Final consensus status:** **Resolved as intentional design.**

### FA-002 — weighted fitting with unweighted displayed local RMSD

- **Classification:** Intentional design
- **Severity:** Low
- **Confidence:** Very high
- **One-sentence claim:** minimizing standardized residuals while displaying unweighted local RMSD is deliberate and internally consistent.
- **Mathematical expectation:** weighted objective and a comparable unweighted diagnostic may coexist if clearly separated.
- **Actual implementation behaviour:** objective applies sigma once; local `Loss()` remains unweighted.
- **Scientific impact:** none in local fits; global display aggregation is separately defective (F-020).
- **Suggested correction:** preserve the separation and label objective/diagnostic distinctly.
- **Verifier conclusion:** explicit vector calculations matched code.
- **Rebutter conclusion:** no sigma-squared-twice or variance/SD confusion was found.
- **Final consensus status:** **False alarm / intentional design.**

### FA-003 — active one-site, syringe correction, and sequential forward formulae

- **Classification:** False alarm
- **Severity:** Low
- **Confidence:** Very high
- **One-sentence claim:** no material forward-equation discrepancy was found for active one-site, modern syringe correction, or sequential models over the audited grid.
- **Mathematical expectation:** mass/action balances, state probabilities, and incremental heat must match independent high-precision solutions.
- **Actual implementation behaviour:** maximum discrepancies were about `0.124 pJ` (one-site), `0.004 pJ` (syringe correction), and `0.00044 fJ` (sequential).
- **Scientific impact:** tested forward predictions are reliable; optimizer/identifiability limitations remain separate.
- **Suggested correction:** convert the oracle cases into permanent tests.
- **Verifier conclusion:** direct, binding-polynomial, and numerical-root strategies agreed.
- **Rebutter conclusion:** no hidden unit/sign/factor-of-two error was found.
- **Final consensus status:** **Resolved as correct within tested ranges.**

### FA-004 — ordinary dissociation reaction-extent heat

- **Classification:** False alarm
- **Severity:** Low
- **Confidence:** High
- **One-sentence claim:** the ordinary dissociation formula’s explicit plug-replacement balance is defensible and its difference from the common binding heat correction is not itself an error.
- **Mathematical expectation:** under plug replacement, reaction heat is \(H\{V D_{post}-[(V-v)D_{pre}+vD_{syr}]\}\), with \(H\) per mole dimer.
- **Actual implementation behaviour:** `Dissociation.cs:64-75` implements that expression.
- **Scientific impact:** ordinary non-tandem predictions need not be rejected on this basis.
- **Suggested correction:** document the convention; fix only tandem pre-state F-019.
- **Verifier conclusion:** equilibrium/factor-of-two checks passed.
- **Rebutter conclusion:** successfully established the physical plug interpretation.
- **Final consensus status:** **Resolved as correct under the stated flow assumption.**

### FA-005 — primary-offset subtraction in prediction bands

- **Classification:** False alarm
- **Severity:** Low
- **Confidence:** High
- **One-sentence claim:** subtracting the primary offset from every replicate prediction is a coherent fixed-coordinate confidence band, not a proven numerical error.
- **Mathematical expectation:** observations plotted as `y-primaryOffset` should be compared with replicate predictions transformed by the same fixed subtraction, retaining replicate offset uncertainty.
- **Actual implementation behaviour:** `Models.cs:137-165` and graph/publication code use this coordinate.
- **Scientific impact:** the band represents total fitted prediction uncertainty on the primary-offset-corrected axis, not a conditional binding-only curve.
- **Suggested correction:** document the estimand and test it; offer a separate conditional band if desired.
- **Verifier conclusion:** initially proposed subtracting each replicate’s offset.
- **Rebutter conclusion:** demonstrated that would answer a different question and would not match the plotted data coordinate.
- **Final consensus status:** **Resolved as defensible; documentation/test ambiguity only.**

### FA-006 — PEAQ factor-of-million conversion allegation

- **Classification:** False alarm
- **Severity:** Low
- **Confidence:** High
- **One-sentence claim:** fixture magnitudes support treating encoded PEAQ DP values as cal/s, so the production numerical conversion is likely correct.
- **Mathematical expectation:** `5.18682e-6 cal/s` is `5.18682 µcal/s`, a plausible ITC power.
- **Actual implementation behaviour:** code converts from calories while an adjacent comment says µcal/s.
- **Scientific impact:** no demonstrated numeric error; comment risk is F-024.
- **Suggested correction:** correct/verify the comment, not the multiplier absent vendor evidence.
- **Verifier conclusion:** scale analysis supports code.
- **Rebutter conclusion:** successfully falsified the original factor-(10^6) code claim.
- **Final consensus status:** **Resolved as false alarm for calculation.**

Other investigated non-defects: affinity/Kd/\(\Delta G\) signs and units in active models; Celsius-to-Kelvin use; active syringe \(\alpha\) applied once; excluded injections remaining excluded; label-swapped two-site **predictions** remaining invariant; global optimizer vector deduplication; primary failed/cancelled solutions not replacing results; FTXTC ordinary central-value round trips; and same-stock tandem recurrence.

## 13. Missing tests, prioritized

### Priority 0 — required before relying on affected outputs

1. **Implemented in the working tree:** `.aff`/`.dat` real-fixture assertions for inferred syringe concentration, injection mass, concentration trajectory, heat unit, and row completeness across J, µcal, and cal inputs.
2. Two-set physical-root/conservation tests above 1 mM, with residual and sign-oracle checks; prohibit non-root fallback.
3. Competitive conservation and limiting-case tests at `logKa=12–20`, including `B0=0` invariance to every competitor parameter.
4. Bootstrap retained-row identity tests covering Standard/Extended/No-limit bounds, global/member alignment, reported counts, one/zero retained rows, correlations, and temperature summaries.
5. FTITC save/reload tests where saved rows lie near Standard bounds but were generated under Extended/No-limit policy; assert byte-level semantic equality of replicate IDs and summaries.

### Priority 1 — before the next release

6. Trailing-average right-point integration tests covering exact boundaries, irregular timestamps, zero endpoints, and both heat signs.
7. Irregular-grid integration-error tests against explicit covariance matrix multiplication.
8. Versioned persistence tests for all three hidden legacy models, including old-linear and modern-log affinity provenance; reject ambiguous files.
9. Isomerization population-sensitivity/limiting-case tests—or a test that unsupported restore is refused.
10. Concentration-perturbation distribution tests checking empirical mean/SD/positivity and deterministic seeded draws.
11. LOO tests separating influence/spread from jackknife SE, plus `B<n` validation.
12. Global unlock tests for bootstrap and LOO, including a locked shared coordinate and dependent member CIs.
13. Limit-termination tests that distinguish converged, retained-with-warning, retried, and rejected error-estimation rows.
14. Two-site replicate label-alignment tests using exact swapped tuples, near ties, shared-N mode, correlations, and round trips.
15. Pooled global RMSD tests with equal/unequal member sizes and duplicate datasets.
16. Startup versus Restore Defaults equality and noiseless recovery gates for NM and LM at every tolerance preset.
17. Competitive input-label/UI tests and free-versus-total apparent-Kd oracle cases.
18. Tandem changed-stock rejection/per-segment reconstruction, including dissociation at a segment boundary.
19. Blank-subtraction mismatch tests for injection volume/count/composition and out-of-range ordinal behavior.

### Priority 2 — longer-term scientific validation

20. Repeated coverage studies for one-site, two-site, sequential, competitive, dissociation, and global fits across c-values, noise, heteroscedasticity, bounds, concentration calibration, and multi-start conditions.
21. Profile likelihood/loss-surface and Jacobian-conditioning tests for identifiability; symmetry-aware two-site reporting.
22. Cross-reader unit-property tests: physically equivalent datasets encoded in every supported format must yield identical `ExperimentData` and model fits.
23. Culture matrix tests (`en-US`, `da-DK`, `de-DE`) for every parser/exporter in direct Core and front-end contexts.
24. Independent reference tests for active-volume/mixing laws against calibrated analytic or numerical flow models.
25. Vendor-schema-backed PEAQ assertions for power, injection metadata, and sign.
26. Temperature-series synthetic fits comparing the existing constant-ΔG rule with a separately implemented integrated van’t Hoff/ΔCp rule.

## 14. Recommended remediation sequence

### Immediate

1. **Implemented in the working tree:** derive `.aff`/`.dat` concentration from `Xt`/`Mt`, prompt for unresolved metadata, retain the plausibility guard, and add real-fixture regression coverage. Previously imported affected experiments still require re-import or manual correction.
2. Replace the two-site and competitive analytic evaluation paths with physical bracketed solvers plus conservation checks. Do not merely widen the two-site constant or clamp competitive fractions.
3. Make bootstrap sample admission a single indexed operation using actual fit bounds; align global/member rows and correct displayed success counts.
4. Switch FTITC deserialization to the non-filtering restoration contract and preserve replicate IDs/status.
5. Disable or explicitly mark the three legacy models unsupported until version-aware affinity migration and isomerization implementation are available.
6. Add the Priority-0 regression tests before considering these fixes complete.

### Before the next release

1. Include the exact terminal sample in right-point peak integration, document the trailing-average convention, and make uncertainty use the same actual interval weights.
2. Correct concentration SD semantics, global unlock behavior, zero-loop LOO validation, and limit-terminated replicate policy.
3. Canonicalize two-site replicate labels and recompute derived thermodynamic values from paired retained rows.
4. Fix pooled global RMSD and make Restore Defaults match the safe startup setting.
5. Relabel total competitor input; compute/qualify apparent Kd; add tandem/blank protocol validation and dissociation segment awareness.
6. Use invariant formatting in all exporters and honor raw-M integrated-reader mode.
7. Add a migration/recalculation notice for projects produced by affected versions; silently changing old fitted values is unsafe.

### Longer-term validation

1. Maintain the independent high-precision oracle as a repository test asset with seeded grids and property checks.
2. Introduce explicit model/version/parameter-coordinate metadata in persistence formats.
3. Offer an integrated van’t Hoff/ΔCp global model beside the existing phenomenological constant-ΔG constraint.
4. Add stock/preparation groups and correlated uncertainty components.
5. Add profile likelihoods, multi-start diagnostics, symmetry-aware parameter reporting, and empirical coverage dashboards.
6. Calibrate active-volume/mixing laws and quadrature/error models against primary literature and instrument reference data.

Fixes should be reviewed as a separate change set. This audit intentionally makes no production-source modification.

## 15. Proposed regression-test specifications

The tolerances below compare absolute heat in joules unless stated otherwise and should use an implementation independent of the production formula.

| ID | Test specification | Required assertion |
|---|---|---|
| R-001 | Load repository `CURVE-2.aff`, selecting cal: first `DH=1.12e-6 cal`, `NDH=27.9 cal/mol`, `INJV=10 µL`. | Trajectory-derived syringe concentration near `4.00e-3 M`, first injection moles near `4.00e-8 mol`, and first heat `4.68608e-6 J`; never nanomolar concentration or `1.12e-3 J`. |
| R-002 | Two sets: `P=10 µM`, `Ltotal=1.1 mM`, `K1=1e6`, `K2=1e5 M^-1`, `N1=N2=1`. | Free ligand `1.080100984 mM` within high-precision tolerance; mass residual < `1e-12 M`; no fallback. |
| R-003 | Production injection pair corresponding to the original two-site reproduction (`V=200 µL`, `v=2 µL`, `ΔH1=-40 kJ/mol`, `ΔH2=+20 kJ/mol`). | Heat near `-13.471958831 µJ`, not `+110.397821998 µJ`; conservation at pre/post states. |
| R-004 | Competitive public case: `P0=10 µM`, `CsA=1 mM`, `V=200 µL`, `v=2 µL`, `N=1`, `ΔHA=-40 kJ/mol`, `KA=KB=1e12`, `B0=0`. | Competitive heat matches stable one-site/oracle near `-6.0820624e-12 J`; site fractions nonnegative and sum to 1. |
| R-005 | Same R-004, sweep irrelevant `logKB=0,3,...,18` and competitor enthalpy. | With `B0=0`, prediction invariant to both competitor parameters to `1e-12` relative/absolute appropriate tolerance. |
| R-006 | Random competitive grid over `logK=-2..20`, concentrations `1e-9..1e-1 M`. | All species finite/nonnegative; site and mass residuals < `1e-10` scaled; agrees with 80+ digit monotone root. |
| R-007 | Integrate `P(t)=t` at `(0,0),(5,5),(10,10)` over `[0,10]`. | Exactly/near `50 J`; endpoints included/interpolated. |
| R-008 | Integrate a linear function on irregular grid with bounds between samples; include a constant function. | Piecewise-linear analytic integral to machine tolerance; no terminal-interval loss. |
| R-009 | Known covariance matrix and irregular interval weights. | Reported heat variance equals `w^T C w`, including baseline term under documented convention. |
| R-010 | Two bootstrap rows: one interior, one near a bound; vary Standard/Extended policies and lock state. | Count, retained IDs, member/global rows, correlations, and summaries all use the same explicit policy/rows. |
| R-011 | Save FTITC under Extended bounds with a valid row outside Standard interior; reload under Standard. | Same row count/IDs/values/CIs before and after; no session re-filtering. |
| R-012 | Versioned files for each legacy model with known `Ka=1e6 M^-1`. | Old-linear and modern-log provenance both evaluate/report `Ka=1e6`; ambiguous version rejected, not guessed. |
| R-013 | Change isomer population from 0.1 to 0.9 under a coupled reference case. | Named isomerization model changes predictions as reference dictates, or application refuses unsupported evaluation. |
| R-014 | Seeded 100,000 concentration draws with declared fractional SD 0.1. | Empirical SD `0.1±Monte Carlo tolerance` under documented distribution; concentrations positive. |
| R-015 | Sample-mean LOO analytic case for several `n`. | If labelled jackknife, SE uses `(n-1)/n` rule; otherwise UI/report says influence/spread and never “95% CI.” |
| R-016 | 20 included injections, requested budget 10, concentration uncertainty on. | Configuration rejected or every omitted injection has at least one refit; never silent zero total. |
| R-017 | Global fit with a locked shared affinity and `UnlockBootstrapParameters=true`. | Shared clone coordinate unlocked and fitted while member links remain global. |
| R-018 | Force iteration-limit termination for a bootstrap refit. | Row status exposed; row retried/rejected or passes explicit stability gate; not an indistinguishable success. |
| R-019 | Primary two-site tuple `(8,5)` plus replicate rows `(8,5)` and exact swap `(5,8)` with full tuple values. | After canonicalization, site-specific SD is zero in the exact case; prediction invariant before/after; IDs survive round trip. |
| R-020 | Two global members with residual RMSDs 1 and 3 µJ, equal then unequal counts. | Displayed global RMSD equals `sqrt(totalSSE/totalN)`; duplicating identical members leaves RMSD unchanged. |
| R-021 | Fresh startup then Restore Defaults; run noiseless sequential NM case used by audit. | Same selected tolerance at startup/reset; default recovery meets predeclared affinity/heat/offset tolerance or clearly reports coarse convergence. |
| R-022 | Competitive `Ptotal=Btotal=10 µM`, `KB=1e6`, total competitor option. | UI says total competitor; computed initial `Bfree=2.70156 µM`; displayed apparent Kd uses/labels the selected convention. |
| R-023 | Tandem sources with 100 and 200 µM stocks; then same-stock control. | Mismatch rejected or per-segment injected moles correct; same-stock result unchanged. |
| R-024 | Dissociation first injection in a later tandem segment with stored initial active concentrations different from previous segment end. | Heat uses stored segment pre-state and matches independent extent balance. |
| R-025 | Blank target/reference with 2 versus 10 µL schedules and unequal lengths. | Operation rejected or explicitly amount-normalized; no silent ordinal/clamped absolute subtraction. |
| R-026 | Export MicroCal/PyTC under `en-US`, `da-DK`, `de-DE`. | Identical parseable field counts/numeric values in all cultures. |
| R-027 | Same integrated trajectory expressed once in mM mode and once in M mode. | **Implemented in the working tree:** identical internal cell/syringe concentrations, cell volume, and injection trajectories. |
| R-028 | PEAQ vendor-schema reference with known watts/µcal/s and sign. | Parsed power and integrated heat match physical reference; comment and code unit agree. |

For stochastic tests, save the seed and the independent expected distribution. For nonlinear root tests, assert physical residuals in addition to heat agreement; a second implementation can reproduce the same wrong root if only output values are compared.

## 16. Appendix

### A. Independent model equations and conventions

#### Common injection heat

Let \(Q_i\) be total reaction heat content of active volume \(V_0\) after injection \(i\), and \(v_i\) the injection volume. The active binding models use

\[
q_i=Q_i-Q_{i-1}+\frac{v_i}{2V_0}(Q_i+Q_{i-1})+q_{offset,i}.
\]

The oracle used the same stated overflow convention when comparing the equilibrium model itself, thereby separating equation/root errors from alternative flow models.

#### One set of independent sites

With total site concentration \(S_t=nP_t\), total ligand \(L_t\), and \(K_a\), free ligand \(L\) and bound sites \(B\) obey

\[
B=S_t\frac{K_aL}{1+K_aL},\qquad L_t=L+B.
\]

Equivalently,

\[
B=\frac{S_t+L_t+K_a^{-1}-
\sqrt{(S_t+L_t+K_a^{-1})^2-4S_tL_t}}{2}.
\]

Cell heat content is \(Q=VB\Delta H\). The modern syringe factor replaces the intended syringe concentration by \(\alpha C_s\) once in concentration evolution/injected amount; it must not be independently multiplied into heat again.

#### Two independent site classes

For classes \(j=1,2\),

\[
B_j=n_jP_t\frac{K_jL}{1+K_jL},\qquad
L_t=L+B_1+B_2,
\]

\[
Q=V(B_1\Delta H_1+B_2\Delta H_2).
\]

The physical scalar residual is monotone in \(L\ge0\); `[0,L_t]` is a natural bracket. Swapping complete tuples \((n_1,K_1,H_1)\leftrightarrow(n_2,K_2,H_2)\) leaves predictions invariant.

#### Sequential binding

For states \(PL_j\), step constants \(K_j\), and free ligand \(L\), define

\[
\beta_0=1,\quad \beta_j=\prod_{k=1}^{j}K_k,\quad
Z=\sum_{j=0}^{m}\beta_jL^j,\quad p_j=\frac{\beta_jL^j}{Z}.
\]

Total bound ligand per macromolecule is \(\sum_j jp_j\), which closes the ligand balance. State \(j\) has cumulative enthalpy \(\sum_{k=1}^{j}\Delta H_k\); therefore

\[
Q=VP_t\sum_{j=0}^{m}p_j\sum_{k=1}^{j}\Delta H_k.
\]

The audit translated the repository’s step-constant convention consistently; no extra combinatorial factor should be inserted unless constants are redefined as microscopic site constants.

#### Competitive binding

Let total sites be \(S_t=nP_t\), target A and competitor B bind mutually exclusively, and \(p\) be free site concentration. With free ligands \(a,b\),

\[
[PA]=K_Apa,\quad [PB]=K_Bpb,\quad
S_t=p+[PA]+[PB],
\]

\[
A_t=a+[PA],\qquad B_t=b+[PB].
\]

These balances reduce to a monotone scalar equation in \(p\in[0,S_t]\) (or another free species) and give fractions that must be nonnegative and sum to one. Heat content is

\[
Q=V([PA]\Delta H_A+[PB]\Delta H_B).
\]

When \(B_t=0\), every result is independent of \(K_B,\Delta H_B\) and exactly reduces to one-site A binding. This invariant exposes F-003.

#### Dimer dissociation

For \(2M\rightleftharpoons D\), under association convention \(K=[D]/[M]^2\) and total monomer equivalents \(C_t=[M]+2[D]\), solve

\[
2K[M]^2+[M]-C_t=0,\qquad [D]=K[M]^2.
\]

With enthalpy per mole dimer and plug replacement, ordinary injection reaction extent is

\[
\Delta n_D=V[D]_{post}-\{(V-v)[D]_{pre}+v[D]_{syr}\},
\qquad q=\Delta H_D\Delta n_D.
\]

The tandem defect is selection of `[D]pre`, not this ordinary extent equation.

#### Thermodynamic transforms

With (K_a) expressed relative to the 1 M standard state,

\[
K_d=1/K_a,\qquad \Delta G=-RT\ln K_a,\qquad
-T\Delta S=\Delta G-\Delta H.
\]

The existing global rule is

\[
\Delta H(T)=\Delta H_{ref}+\Delta C_p(T-T_{ref}),\qquad
K_a(T)=\exp[-\Delta G/(RT)],
\]

with independent constant \(\Delta G\). A thermodynamically coupled alternative would integrate

\[
\frac{d\ln K_a}{dT}=\frac{\Delta H(T)}{RT^2}
\]

using the same \(\Delta H_{ref}\) and \(\Delta C_p\).

### B. Independent numerical-oracle method

The oracle was written independently in Python with `mpmath` at 90 decimal digits. It did not translate the production cubic/root routines. It combined:

1. direct mass-action plus conservation root solution;
2. analytic quadratic/binding-polynomial evaluation where appropriate; and
3. independent numerical minimization/root solution, with conservation and probability residual checks.

Production values were obtained through small C# harnesses referencing the built `AnalysisITC.Core` assembly. Probes and generated data stayed under `/tmp`; no application file was edited.

The principal randomized grid used seed `0xF717C` and covered:

- `log10(Ka)` from `-2` to `20`;
- concentrations from `1e-9` to `1e-1 M`;
- injection fractions `v/V` from `1e-4` to `0.1`;
- enthalpy magnitudes `1e3` to `1e5 J/mol`, both signs;
- weak, intermediate, saturating, boundary, exact-zero, and symmetric cases; and
- label swaps, zero enthalpy/concentration, competitor removal, equal sites, multiple injections, and cell displacement.

At least 1,361 production/oracle cases were compared. Notable clean maxima were approximately `0.124 pJ` for one-site (150 cases), `0.004 pJ` for the active syringe correction (120), `0.00044 fJ` for sequential (240), and `0.088 fJ` for ordinary dissociation (200). Two-set cases below the artificial cap agreed; cases above it failed as F-002. Competitive moderate cases agreed, but 133 of 300 random cases exceeded both `1 pJ` and `1e-7` discrepancy thresholds in ill-conditioned regimes.

The audit also ran noiseless parameter-recovery probes under both optimizers/tolerance settings. These distinguish forward-model agreement from early optimizer termination; agreement between NM and LM alone was never treated as an oracle.

### C. Literature mapping

| Source | Repository mapping and audit use |
|---|---|
| [Wiseman et al., 1989, *Analytical Biochemistry* 179, 131–137, DOI 10.1016/0003-2697(89)90213-3](https://doi.org/10.1016/0003-2697(89)90213-3) | Data-analysis section’s single-class binding is mapped to total site concentration `n*P`, association constant in M⁻¹, absolute cell heat, and finite injections. Used as a check on the one-site quadratic/c-value convention, not as the sole oracle. |
| [Sigurskjold, 2000, *Analytical Biochemistry* 277, 260–266, DOI 10.1006/abio.1999.4402](https://pubmed.ncbi.nlm.nih.gov/10625516/) | Competitive/displacement analysis section mapped to mutually exclusive target/competitor mass balances. Total analytical competitor is distinguished from free and initially bound competitor; F-017/F-018 follow that mapping. |
| [Tellinghuisen, 2004, *Analytical Biochemistry*, DOI 10.1016/j.ab.2004.05.061](https://pubmed.ncbi.nlm.nih.gov/15450820/) | Active cell-volume calibration/error discussion used to classify the common overflow law as an instrument assumption and to avoid treating every non-exact flow model as a code bug. |
| [Tellinghuisen, 2007, *Journal of Physical Chemistry B*, DOI 10.1021/jp074515p](https://pubmed.ncbi.nlm.nih.gov/17850136/) | Cell mixing, dilution, calibration, and injection-volume sections mapped to the repository’s concentration-evolution and active-volume conventions. |
| [Tellinghuisen & Chodera, 2011, *Analytical Biochemistry*, DOI 10.1016/j.ab.2011.03.024](https://pubmed.ncbi.nlm.nih.gov/21443854/) | Concentration/baseline systematic-error discussion used in assessing concentration uncertainty and why independent random error cannot substitute for shared calibration error. |
| [Keller et al., 2012, *Analytical Chemistry*, DOI 10.1021/ac3007522](https://pubmed.ncbi.nlm.nih.gov/22530732/) | Thermogram integration/peak-shape analysis used to frame F-008/F-009: baseline selection and uncertainty remain central even when the acquisition convention determines the right-point area rule. |
| [Brautigam et al., 2016, *Nature Protocols* 11, 882–894, DOI 10.1038/nprot.2016.044](https://pmc.ncbi.nlm.nih.gov/articles/PMC7466939/) | Global-analysis workflow and parameter-sharing discussion mapped to local/shared member fits and concentration/systematic-error cautions. |
| [Freiburger et al., global ITC framework, equations 7, 8, and 16](https://pmc.ncbi.nlm.nih.gov/articles/PMC5179259/) | Equations 7/8 were translated into common thermodynamic temperature conventions; equation 16 informed the mapping of global objectives/uncertainty without assuming identical software conventions. |
| [Dumas, 2022, *European Biophysics Journal*, DOI 10.1007/s00249-021-01588-4](https://pubmed.ncbi.nlm.nih.gov/34999938/) | Dilution and imperfect-mixing treatment used to classify active-cell evolution as a scientific model assumption and motivate longer-term validation. |

Where an exact paper equation was not needed or not unambiguously accessible in the audited environment, the table cites the relevant section rather than inventing an equation number. Every comparison was translated to the same association/dissociation, standard-state, heat-per-event, concentration, and cell-overflow conventions before judgment.

### D. Representative commands and output

```text
git status --short
git rev-parse HEAD
git log -1 --oneline

dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj -c Debug
dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj -c Release
dotnet test AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj -c Debug
dotnet test AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj -c Release
dotnet test AnalysisITC.Web.Tests/AnalysisITC.Web.Tests.csproj -c Debug
dotnet test AnalysisITC.Web.Tests/AnalysisITC.Web.Tests.csproj -c Release
```

Representative decisive probe output:

```text
TwoSets production:  +110.397821998 uJ
TwoSets oracle:       -13.471958831 uJ

Competitive production: -1.0953795490050976e-05 J
OneSet production:       -6.082062398203927e-12 J
90-digit oracle:         -6.082062418176375e-12 J

GLOBAL_UNLOCK requested=True
shared_locked_before=True
shared_locked_clone=True
local_N_locked_clone=False

Sequential NM Fast:     logK1=6.399226695, logK2=5.324196276, loss=0.100136726 uJ
Sequential NM Balanced: logK1=6.298570641, logK2=5.198218935, loss=0.00188015 uJ
Truth:                  logK1=6.300000000, logK2=5.200000000
```

### E. Residual risk and audit boundary

The audit independently validated the active one-site, modern syringe-correction, sequential, ordinary dissociation, moderate competitive, and below-cap two-site forward paths over the stated grids. It traced all discovered model subclasses and persistence reachability. It did **not** obtain every historical vendor file schema, run a large empirical coverage campaign, validate against instrument vendor output as a primary oracle, build the legacy Xamarin.Mac target in a compatible environment, or prove floating-point correctness outside the stated ranges. Hidden file variants, highly pathological parameter combinations, concurrency/order effects, and untested UI/export surfaces remain residual risks.

The decisive conclusion is therefore: **material mathematical, numerical, statistical, and data-handling defects exist, but they are identifiable and scoped. Ordinary one-site and several active forward-model paths are supportable; the affected import/model/uncertainty paths should not be used for scientific conclusions until corrected and regression-tested.**
