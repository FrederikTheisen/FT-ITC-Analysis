# Derived-parameter uncertainty across all models

Status: Detailed implementation design proposed on 23 October 2026; scientific choices below remain proposals. Tracker: [ITC-117](ISSUES.md#itc-117---derived-parameter-uncertainty-across-all-models). No application changes are approved by this document.

The user requested detailed design first, with the explicit aim of finding bloat and overengineering. Prefer minimal profile machinery and identify unsupported cases. Implementation and validation have not started.

This feature supersedes the narrow CR-029 implementation proposal. Treat competitive apparent affinity as one example of a broader question: how should FT-ITC calculate and present uncertainty for every derived parameter, consistently across models and uncertainty methods?

## Agreed requirements

- Consider all derived parameters for all implemented models: One Set of Sites, Two Sets of Sites, Competitive Binding, Sequential Binding Sites, Dissociation, and Offset in its Null-model role. Establish explicitly where a model has no relevant derived quantities.
- Include bootstrap, leave-one-out, and profile-likelihood uncertainty. Profile-likelihood handling is part of the scientific design, not a fallback to decide during implementation.
- Preserve the original best-fit central values. Retained refits determine uncertainty, not replacement means or medians.
- Zero error means the error does not exist. Do not invent additional error tracking, availability flags, or display markers for this feature.
- Keep native macOS, Avalonia, the web viewer, reports, and exports consistent wherever they expose the same quantity and evaluation context.
- This is planning work. Do not implement a competitive-only patch, adopt the previous narrow fallback rules as settled policy, or change scientific behavior while registering the feature.

## Current evidence and adjacent work

- [CR-029](CODE_REVIEW_2026-10.md#cr-029--competitive-apparent-kd-omits-fitted-stoichiometry-uncertainty): the initial free-competitor calculation reads scalar N, so apparent Kd omits uncertainty from its N dependence. The same competition factor supplies apparent Ka and apparent enthalpy.
- A controlled Core probe used 10 µM initial cell and competitor concentrations, competitor Ka = 10^6 M^-1, and fixed intrinsic Kd = 10 nM. Retained N values of 0.9 and 1.1 produce apparent Kd values of 41.6227766 and 33.1662479 nM, while the primary result reports 37.0156212 nM with zero SD. The existing center-preserving distribution convention gives approximately 4.24520645 nM dispersion. This establishes the missing contribution; it does not choose a universal propagation method.
- In the bundled competitive project, current reported SDs are larger than dispersions calculated from the saved refits. Missing dependence and correlations do not imply that uncertainty is always underestimated. Probe source and output: `/private/tmp/cr029-investigation.xNEcg7/`.
- Shared thermodynamic solution helpers, linked global evaluation, and summary evaluation already implement different transformations and aggregation contexts. Inventory these before proposing a shared interface or replacing calculations.
- [CR-024](CODE_REVIEW_2026-10.md#cr-024--exported-and-displayed-summary-kd-uncertainties-disagree) was resolved with a selected summary convention: propagate summarized Gibbs SD and transform its interval endpoints. Read `SUMMARY_SEM.md` and preserve that agreement unless a specific change is discussed and approved. A per-fit derived distribution and a collection summary are distinct estimands.
- [CR-010](CODE_REVIEW_2026-10.md#cr-010--failed-profile-diagnostics-can-still-supply-numerical-uncertainty) records the local fix preventing failed owning profile runs from supplying numerical intervals. Retain that protection. [ITC-114](ITC-114_PROFILE_PRIMARY_RECOVERY_PLAN.md) concerns primary-fit recovery and remains separate.

## Detailed implementation proposal, 23 October 2026

### Recommended boundary

Implement retained-refit propagation in existing Core properties. Reuse the existing distribution summarizer, thermodynamic slots, linked evaluator, profile endpoint mapper and consumer routes. Do not add an independent N error term, another Monte Carlo pass, a covariance-matrix engine, a quantity registry, a dependency graph, a derived-result cache, new controls, or a new persistence representation.

For profiles, support transformations that genuinely depend on one fitted coordinate. General derived profiling is a separate extension, described below so its real cost can be reviewed. Existing linked profile approximations need an explicit policy decision; changing them is not a prerequisite for correcting retained-refit propagation.

### 1. Quantity and evidence inventory

Here `a = log10(Ka)` is the fitted affinity coordinate, `H = ΔH` is fitted enthalpy, `T` is the owning evaluation temperature in kelvin, and `R` is the gas constant. N is fitted stoichiometry in ordinary mode; the same stored coordinate is syringe activity α in correction mode. Fixed site counts are separate model options.

| Quantity | Scalar calculation and dependencies | Model applicability | Proposed treatment |
|---|---|---|---|
| Ka, Kd | `Ka = 10^a`; `Kd = 1/Ka` | Every binding step of One Set, Two Sets, Competitive, Sequential, and Dissociation | Transform each retained coordinate value. For profiles, map the owning coordinate endpoints. |
| ΔG | `G = -RT ln(Ka)` | Same steps | Scalar evaluation using that refit's temperature; one-coordinate profile transform for ordinary fits. Linked evaluation keeps its existing owning coordinates. |
| −TΔS, ΔS | `E = G-H`; `S = -E/T` | Same steps | Evaluate E from paired G/H values in each refit. At fixed owning T, scale the complete E estimate for S; do not rebuild it from value/SD. |
| Apparent Ka, Kd | `F = 1 + Kc*Cfree`; `Ka,app = Ka/F`; `Kd,app = F/Ka` | Competitive | Evaluate the final scalar from the same refit's N, cell concentration, competitor inputs and affinity. |
| Apparent ΔH | `Happ = H - Hc*(1-1/F)` | Competitive property API | Evaluate the final scalar per refit. Do not introduce a new report row. |
| Total ΔH | `ΣHstep` | Multiple enthalpy slots; a single slot reduces to its existing H | Sum paired slot values per refit. This changes uncertainty supplied to a newly run protonation analysis, not its analysis method. |
| c-value | Ordinary `N*Ccell/Kd`; Sequential `Ccell/Kd,step`; Competitive uses apparent Kd; correction mode uses fixed sites | Existing supported c-value models | Evaluate the final scalar per refit, preserving the initial tandem-segment concentration basis. |
| Syringe activity α | Existing fitted N-coordinate output | Models exposing correction mode | No additional derived uncertainty. Retain its fitted estimate. |
| Corrected guide N, H | `Nfixed/α`; `α*H` | Existing graph guides | Inventory only for this implementation. Both renderers use their centres; do not rewrite errors or add whiskers without a consumer that needs them. |
| Offset / Null role | Offset is a fitted coordinate | Offset | No binding-derived quantity to add. Prediction uncertainty remains on the existing model-refit path. |

Source homes: `Analysis2/Models/ThermodynamicSolution.cs:26-78`, `Models.cs:592-679`, `CompetitiveBinding.cs:560-600`, `Analysis2/AnalysisCValueCalculator.cs:48-144`, and `Analysis2/ThermodynamicParameterSlots.cs`. Paths in this inventory are under `AnalysisITC.Core` unless another project is named. Source locations are a 23 October snapshot, not a new API contract.

Every evaluation must use the supplied refit's own `Parameters`, `Data`, `ModelOptions`, temperature and correction mode. Reading the primary model's options or concentrations inside a refit projection defeats the calculation. Sampled concentration objects can still carry their original SD: read their scalar `.Value`; do not propagate that SD again after sampling.

### 2. Minimal retained-refit implementation

#### Shared helper in `SolutionInterface`, inside the existing `Models.cs`

Add at most one small helper with this shape:

```csharp
internal bool TrySummarizeDerivedRefits(
    Func<SolutionInterface, double> evaluateScalar,
    double primaryValue,
    out FloatWithError estimate);
```

Its job is narrowly defined:

1. Resolve whether the owning saved method uses retained bootstrap or leave-one-out refits. An owning profile method/run takes precedence over old snapshots. Ordinary global members can have `ErrorMethod.None`, so a strict member-only method check is incorrect. Use member method, captured member clone options, then captured parent clone options, with profile precedence; do not consult current application preferences. The existing resolution pattern in `BootstrapCorrelationAnalyzer.cs:361-380` is a reference, not a reason to generalize that analyzer.
2. Return false for a profile, method None or an unrecognized saved method. For bootstrap/leave-one-out with no retained rows, also return false and preserve the existing stored-coordinate/input-error fallback. That fallback is not claimed to contain joint evidence. Do not fall back from a profile to bootstrap samples.
3. Evaluate one scalar for each member refit already retained by the owning solver/storage path. Do not independently sample, unlock, refit, pair marginal distributions, or change membership.
4. Summarize the final scalar list with existing `SummarizeBootstrapDistribution(values, primaryValue)`. Do not pass `List<FloatWithError>` to its different, sampling-based constructor.
5. Return true with a complete `FloatWithError`, including its interval, when a nonempty refit set has been handled. The one/invalid-row policies below also return true, preventing accidental fallthrough to marginal quadrature. Callers must not rebuild it from value and SD.

The helper does not catch arbitrary exceptions. Expected numeric domain failures should be represented by the scalar calculation's existing finite/try checks. Unexpected programming failures keep the application's existing error handling.

The evidence gate and scalar projection should remain easy to read in the same class. Do not add a method-resolver service, strategy subclasses, or new status objects. Only extract a shared resolver if a second changed caller actually needs precisely the same semantics.

#### Existing distribution convention stays intact

`Math/NumberStructs.cs:97-119` uses the original best-fit centre `y0`, not the mean of the refits. For at least two values, the field named SD is `sqrt(Σ(yi-y0)^2/n)`. Bounds use the existing outward empirical percentile indices (`:175-188`). This implementation preserves that convention; it must not quietly substitute sample SD, recenter on the bootstrap mean, change percentiles, or apply jackknife scaling to leave-one-out values.

Two explicit edge policies are proposed for review:

- With exactly one retained value for a newly summarized derived quantity, return the original centre with zero error and collapsed bounds. The generic constructor currently gives one refit zero SD but bounds at that refit, which can differ from the primary centre. Keep the generic constructor unchanged; this would be a deliberate local feature policy. With no rows, preserve the existing fallback as specified above; changing historical no-evidence propagation is outside the initial refit correction.
- If any retained scalar is outside the quantity's domain or nonfinite, retain the primary centre with zero derived error for the whole quantity. Do not quietly discard that refit, use a subset, change the solver's success count, or substitute marginal quadrature. Do not add availability flags or warnings. Invalid primary centres keep their existing invalid/unavailable handling; never replace a primary centre with zero.

These policies are not already established by solver success: `ValidateBootstrapSolution` checks model type and parameter-key shape, not the finiteness of every derived quantity. A small explicit branch is necessary; a second refit-admission framework is not.

#### `ThermodynamicSolution.cs`

Keep the public/protected quantity properties and `ThermodynamicParameterSlots`. Add scalar calculations next to those existing helpers, reading coordinate `.Value` fields rather than uncertainty-bearing intermediate properties. Ordinary, unlinked getters obtain their original scalar centre, try direct refit summarization, then use their explicit profile rule or existing no-evidence fallback.

Preserve existing linked G/H/−TΔS entry points and their centre/interval shifting before applying this ordinary branch. Linked Kd already maps joint Gibbs refit curves in `ProfileMappedParameter`; reuse that route for linked affinity transformations rather than bypassing it with a second member summary. H itself is fitted, not a new derived transformation. New composites such as competitive apparent quantities can use scalar member snapshots without replacing the established linked evaluator.

For entropy, evaluate `Gscalar-Hscalar` before summarization. Summarizing G and H independently and then subtracting their `FloatWithError` objects still loses covariance. Ka/Kd must likewise summarize their transformed values, rather than transform an already summarized marginal SD. This can change their SD even without a covariance issue; it is an explicit nonlinear-transformation change to test.

Keep the existing central formula and arithmetic order where practical. An algebraic simplification such as replacing `ln(10^a)` by `a*ln(10)` is unnecessary to fix uncertainty and can change the last floating-point bits. Best-fit centres are acceptance criteria, not values to recompute from refit averages.

Do not place derived entries in `solution.Parameters`. That dictionary holds fitted-coordinate estimates and participates in bootstrap shape validation and sequential serialization checks. `ReportParameters`, `UISolutionParameters` and `DependenciesToReport` continue to call the existing properties.

#### `CompetitiveBinding.cs`

Extract the current competition-factor calculation into a scalar calculation on the supplied competitive solution. Reuse `TryCalculateInitialFreeCompetitorConcentration`; do not reimplement the equilibrium mathematics in the application.

The scalar must read that solution's total competitor, competitor Ka, cell concentration and ordinary N, or its fixed site count in syringe mode. Preserve the zero-competitor return of exactly 1 before reading unused competitor affinity or resolving equilibrium. Apparent H must also return intrinsic H immediately for zero total competitor, before reading competitor enthalpy; `NaN*0` is not an exact cancellation.

Compute the final target independently:

```text
Kdapp(refit) = factor(refit) / Ka(refit)
Kapp(refit)  = Ka(refit) / factor(refit)
Happ(refit)  = H(refit) - Hcompetitor(refit)*(1 - 1/factor(refit))
```

Then summarize that final list around a private scalar calculation of the original best-fit centre. A getter must not call itself to obtain its centre. Preserve the existing arithmetic order there: current apparent Kd is `1/(Ka/F)`, even though `F/Ka` is algebraically equivalent. Do not combine a summarized Ka with a summarized factor: those also covary. Do not add N's marginal error after this calculation. The same rule uses competitor input variation already captured in the refits exactly once, including when model options were sampled independently of the concentration checkbox. Do not change those sampling settings in this feature.

#### Total enthalpy and c-values

In `TotalEnthalpy`, use the existing slot/member ordering and sum scalar H values from the same refit. Keep the single-slot result's recorded uncertainty where there is no need for a new transformation. No changes to protonation sampling, salt regression, or Spolar–Record methods are proposed.

In `AnalysisCValueCalculator`, retain its existing model switch, quantity IDs, availability checks, concentration basis and fixed-site policy. Separate scalar per-solution calculation from estimate construction, then use the same refit helper for each existing c-value. Do not evaluate `Calculate` recursively over results containing uncertainty, and do not join separate lists by position. Primary or refit quantities must keep the same site/step identity.

The report currently exposes c-value error; interpretation exposes only its centre. Do not add interpretation fields or viewer rows merely because a new estimate can be calculated.

### 3. Local, global and linked ownership

Ordinary members, shared-global members and local leave-one-out all have member refit snapshots. `GlobalModelParameters.SetIndividualFromGlobal` in `ParameterSet.cs:387-406` already expands shared/linked coordinates into member values. Use those captured values for new composites at that member's temperature. No new global-to-local join or copied result graph is needed.

Global leave-one-out omits whole experiments. A member's retained list consequently contains only refits in which it participated. Preserve the existing membership and replicate indices; do not fabricate a value for an omitted member or relax a sharing constraint. ITC-118's bootstrap-unlocking dispatch question remains separate.

For arbitrary-temperature linked curves, retain `LinkedThermodynamicEvaluation.Build` and its scalar `uncertainty:false` path. It already evaluates joint global refits, respects participating members and uses the fixed reference temperature. It explicitly avoids `ReportParameters` because those getters call back into the evaluator. Do not route that scalar builder through a newly uncertainty-aware report getter and create recursion or nested summarization.

The linked member path already preserves the saved member centre while using the linked evaluator's error. Retain that centre/interval handling unless an independent test establishes a specific defect. Older records with member snapshots but no global coordinates must not acquire invented global covariance.

### 4. Profile implementation with minimal machinery

#### Reuse endpoint transformation

`ProfileCoordinateResult.Transform` already transforms both endpoints, orders decreasing transformations correctly, and derives the existing equivalent SD from the transformed interval. `ProfileMappedParameter` already resolves local, shared and some temperature-dependent owning coordinates. Extend these existing routes only where a concrete missing case requires it.

For a genuinely one-coordinate derived function `y=f(x)`, use the complete interval of the owning x and transform it. The function must be monotonic throughout that interval and finite in its domain. Other fitted nuisance parameters may vary during the profile; they are allowed only when the derived function itself does not depend on them.

Whether a coordinate is fixed comes from captured locks and global constraint ownership, not `SD == 0`, the number of completed intervals, or absence of a profile result. A failed profile is not evidence that a fitted coordinate is constant. A zero algebraic coefficient can remove a dependency exactly.

The following mappings require one remaining **owning** coordinate, not just one member-level parameter name. For linked global fits, fixed N does not make Ka(T) a single-coordinate function: Gref, Href and Cp can all influence it. At the exact reference temperature Tr, G/Ka/Kd depend only on Gref, H depends only on Href, and Cp cancels from the entropy contribution; entropy can still depend on both Gref and Href. Reuse these explicit cancellations where applicable. Other linked multi-owner cases retain the approximation described below; do not build a dependency graph to expand coverage.

| Derived quantity | Exact endpoint mapping supported without new fits | Cases not supplied by marginal intervals |
|---|---|---|
| Ka, Kd, ordinary ΔG | Existing owning affinity coordinate | Linked G(T) depending on several free owning coordinates |
| −TΔS and ΔS | Ordinary fit with either H fixed or affinity fixed; transform the other owning coordinate | Both H and affinity free |
| Apparent Ka/Kd | N fixed, or syringe mode with fixed site count: transform intrinsic affinity; if affinity fixed, transform N where the factor is monotonic | Both ordinary N and intrinsic affinity free |
| Apparent H | N/factor fixed: translate H; H fixed: transform N when monotonic; zero competitor or exact zero competitor H removes the correction | H and ordinary N both free and the correction remains active |
| Total H | One active enthalpy coordinate after combining exact coefficients/shared-coordinate repetitions | Sum involving two or more free owning coordinates |
| c-value | Fixed N and concentration: affinity transform; fixed affinity and concentration: N transform; check the competitive formula separately | Ordinary N and affinity both free, or a linked quantity with multiple active owners |
| Corrected guide N/H | No new interval implementation: centres are the current consumed output | No new guide uncertainty display is proposed |

External concentrations and competitor options are held at their saved primary values for these profile transformations. Their entered SDs are not likelihood constraints, and they are not sampled by profiling. These are conditional intervals. Do not independently draw those inputs or add their error afterwards. Including external-input uncertainty in profiles would require a separate statistical model and is outside this minimal design.

Keep `CanUseIntervals` and `HasCompleteInterval` checks. A completely failed owning run cannot supply numeric intervals; incomplete/censored coordinates do not become complete by taking a bound as an endpoint. Preserve the existing behavior for usable completed coordinates in partial/cancelled runs. This feature does not change CR-010 safeguards, the profile threshold/calibration, solver settings, or ITC-114 recovery.

#### Explicit decision about unsupported cases

For ordinary unlinked entropy/ΔS, competitive apparent affinity/enthalpy, total H and c-values, the proposed profile policy is the best-fit centre with zero error whenever the quantity has multiple free owning coordinates and no joint interval. This would deliberately replace their current marginally propagated errors in those cases; it is not implied merely by the instruction that zero means no error. The corresponding one-coordinate cases retain mapped intervals. Do not manufacture samples from independent marginal intervals or present quadrature as an exact derived profile.

There is an existing complication: `LinkedThermodynamicEvaluation` and `SummaryDependence.EvaluateWithoutReplicates` currently combine signed marginal profile widths in quadrature. Existing linked tests explicitly expect that approximation. The first retained-refit implementation should preserve it and state this limitation. It must not be relabelled as exact joint profiling.

Replacing that existing linked approximation with zero error is an additional scientific-policy decision for ITC-117 review. If chosen, alter the relevant linked/member entry points explicitly and update independent expectations; do not change generic `FloatWithError` arithmetic or all summary evaluation to impose it. The CR-024 summary-Kd transformation remains unchanged in either case.

Thus initial support is deliberately incomplete: joint bootstrap/leave-one-out evaluation and exact single-coordinate profile mappings are proposed; exact profiles for several free coordinates are not. Review must settle the existing linked-approximation policy before claiming a uniform all-model profile policy.

#### Why general derived profiling is not included

The correct target is `min Q(parameters)` subject to `derived(parameters)=target`, where Q is the original weighted or unweighted fitting objective. Each candidate must refit the nuisance coordinates under that constraint. Keep the original observation count, original primary fitted-coordinate count and the one-target profile calibration; removing a nuisance coordinate is not a reason to recalibrate the fit as a different model. Displayed RMSD remains separate from Q.

Some constraints have simple eliminations:

- For `E=G-H`, eliminate `H = -RT ln(10)*a-E`.
- For apparent Kd target d, eliminate `a = log10(F(N))-log10(d)`.
- For apparent enthalpy target h, eliminate `H = h + Hc*(1-1/F(N))`.

Those equations do not make this a one-line parameter lock. The relation must be reapplied at every objective/residual evaluation and final validation, preserve the original bounds on the eliminated parameter, and work through local/global Nelder–Mead and Levenberg–Marquardt callbacks. A fixed d does not fix intrinsic affinity when N is still varying. Penalty terms would introduce a new tolerance/statistical approximation.

The current estimator locks a fitted coordinate and calls the ordinary solver (`ProfileLikelihoodEstimator.EvaluateCandidate`). A derived-target relation would need a new solver integration point, target-specific bounds, additional fits, diagnostics and save/reopen design. A generic constraint compiler, automatic differentiation framework or new optimizer is disproportionate here. Defer this extension until exact multi-coordinate profile coverage is separately requested. The distinction between coordinate and prediction profiling is described by [Kreutz, Raue and Timmer (2012)](https://pmc.ncbi.nlm.nih.gov/articles/PMC3490710/).

### 5. Consumers and persistence

| Existing route | Consequence and necessary check |
|---|---|
| `UISolutionParameters` / `ReportParameters` | Both desktop result/list presentation, report parameter tables, figure text and per-fit CSV/TSV/clipboard already consume them. Verify the same estimate across routes; do not add per-client propagation. |
| `ViewerDocumentReader.BuildParameters` | Already reads Core report properties into viewer values/intervals. Preserve its document-local context and parameter shape. |
| `AnalysisCValueCalculator` | Separate calculation needs the change described above; its report uncertainty must agree with the scalar evidence. Interpretation currently stores only the centre. |
| Corrected graph guides | Native `CGGraph.cs:2435-2457` and Avalonia `IntegratedHeatsGraphControl.cs:609-625` read only `.Value`. No renderer or uncertainty-control work. |
| Collection summary / temperature bands | Continue through existing aggregate and linked evaluators. Summary Kd remains transformed summarized Gibbs with the selected spread/precision family; do not average member Kd or pool derived refits into a new summary estimand. |
| Model prediction bands | Already evaluate complete model refits. Parameter-reporting changes do not require new envelopes or caches. |
| Advanced analyses | New salt/protonation calculations can consume changed input uncertainty. Do not change their algorithms or recalculate completed saved outputs on load. |

Concrete verification homes include `AnalysisReportBuilder.cs:1464-1533,2473-2476`, `PublicationFigure.cs:1099-1117,1196,1236`, `AnalysisResultTableExporter.cs:512-539`, `ViewerDocumentReader.cs:1382-1418`, and `AnalysisResultParameterEvaluation.cs:109-129,163-189`. Core presentation callers are shared; desktop renderers remain their current implementations.

The FTXTC specification has been read for this design. Full native storage already captures sampled refit parameters, concentrations, model options, temperatures, segment data and indices, plus global coordinates and participating member IDs. There is no current native save option that simply omits bootstrap payloads. Report saves store references/settings rather than a second compact parameter table.

The wire name `reportedParameters` serializes `solution.Parameters`, not the lazily computed `solution.ReportParameters`. Restoring samples and invoking getters can therefore supply the proposed derived uncertainty without a schema addition. Do not add a second saved derived table or insert derived keys into the coordinate dictionary. Strict round-trip tests must prove estimates/intervals are the same before and after reopening, including varied input options and global leave-one-out membership. Existing intrinsic coordinate estimates remain saved as before.

One input-context limit needs explicit testing: ordinary `SolutionInterface.Temp` reads the attached experiment temperature, and the competitive primary factor reads its attached cell concentration. Captured refit inputs do not by themselves prove that a stale primary centre can be reconstructed after the experiment was edited. Preserve existing stale-fit eligibility guards; interpretation already avoids unsafe historical apparent-value projection. Do not fix this by using current global `DataManager` state or silently changing best-fit centres. If a required consumer lacks a saved primary input basis, record that concrete gap for a separate decision instead of introducing a general history/snapshot system here.

### 6. Independent validation design

Use public quantity/report behavior and saved evidence, not assertions about helper names. Add cases to existing test files rather than a general uncertainty test framework. First demonstrate the relevant failure on the unchanged production path, then rerun against the implementation. Tiny hand-constructed retained sets test propagation, not solver reliability or external forward-model validation.

| Case | Independent expected result |
|---|---|
| Entropy, correlated G/H | At primary `G=-25,000`, `H=-40,000 J/mol`, paired deviations `(G,H)=(+300,+1000),(-300,-1000)` give E deviations `-700,+700`: derived SD 700 J/mol, versus current marginal quadrature about 1044.03065. |
| Entropy, opposite variation | Paired deviations `(+300,-1000),(-300,+1000)` give derived SD 1300 J/mol. Error can grow as well as shrink. |
| Exact cancellation | Equal paired G/H deviations of ±1000 leave E unchanged: zero error and collapsed interval at the original centre. |
| Nonlinear affinity transform | Primary `a=0`; refits `a=±log10(2)` give Ka and Kd sets `{0.5,2}`. Keep centre 1, SD `sqrt(0.625)`, bounds `[0.5,2]`; never replace the centre by 1.25. |
| CR-029 | Cell and competitor 10 µM, competitor Ka `10^6 M^-1`, intrinsic Kd 10 nM; N `0.9,1.1` gives apparent Kd `41.6227766017,33.1662479036 nM`; original centre `37.0156211872 nM`, derived SD `4.24520645494 nM`. Derive expectations from competitor/site mass balance, without calling the production equilibrium helper to generate the expected numbers. |
| Compensating N/affinity | With the same N values, intrinsic Kd `8.893116752251174,11.160629714523045 nM` keeps apparent Kd at `37.0156211872 nM` to floating-point tolerance. No independent N error may be added. |
| Sampled inputs | Hold fitted coordinates fixed while varying saved competitor concentration/Ka or cell concentration. Evaluate each complete saved tuple and compare independently solved scalar targets. Replacing primary inputs must not replace refit inputs. |
| Total enthalpy | Two steps with equal opposite H deviations keep their sum constant. Same-direction deviations increase its error. |
| c-values | At fixed concentration, choose paired N/Kd with a constant ratio; derived c has zero spread. Cover first tandem segment and fixed-site correction mode separately. |
| Profile transforms | Complete `a` interval `[6,8]`, centre 7 maps Kd to `[10^-8,10^-6] M`, centre `10^-7 M`. Check decreasing endpoint order and equivalent SD separately. |
| Profile entropy | With H fixed, transform a complete affinity interval into E/ΔS; with affinity fixed, transform H. With both free, marginal intervals must not masquerade as an exact derived interval. |
| Exact profile cancellations | Zero competitor makes apparent affinity equal intrinsic affinity; zero competitor enthalpy makes apparent H equal intrinsic H. An unused failed coordinate must not invalidate an exact cancellation. |

For entropy fixtures, set fitted log-affinity from the independently specified G using `a=-G/(RT ln(10))`; do not obtain expected entropy by calling the production property. Values above were checked with a standalone arithmetic calculation on 23 October, not a Core solver or regression run.

Additional behavioral checks:

- Exercise every active step: single-site families, both independent site slots and Sequential counts 2–4. Offset remains a fitted-only control.
- Check zero/one refit and invalid-derived-refit policies explicitly, including zero competitor, very small positive values and finite primary centres.
- Cover local fits, independently fitted collections, shared coordinates, linked temperatures and global leave-one-out missing members. Preserve member centres, original locks and retained counts.
- Use a saved case where member `ErrorMethod.None` but parent clone metadata records bootstrap/leave-one-out. Profiles must never read stale retained samples, including failed profiles.
- Verify all supported monotonic transformations against independently calculated endpoints. For unsupported profiles, test the agreed zero/retained-approximation decision, not whatever fallback the implementation happens to return.
- Change current fitting/display preferences after save; stored evidence and uncertainty must not be resampled or refit. Test Update Result through its existing owning operation and saved settings.
- Verify report, per-fit export, clipboard, both desktop presentations and web viewer consume identical complete estimates for the same context. Keep model bands and collection summary conventions unchanged except for their explicitly changed per-fit inputs.
- Repeated reads should be deterministic. Inspect a large retained set through existing report/table creation to detect obvious repeated work; add no cache until actual cost warrants one.

Implementation validation must run the full `AnalysisITC.Core.Tests` suite plus relevant Avalonia/native/web checks. No new tests, builds, desktop interactions, packaging or release operations were run for this design review.

### 7. Implementation slices and bloat review gates

1. **Ordinary thermodynamics:** add the narrow refit helper/scalar evaluation, fix entropy and nonlinear affinity transforms, and demonstrate preserved centres with independent tests. No UI, solver, schema or generic numeric-operator changes.
2. **Competition:** reuse the existing equilibrium solver on each complete refit tuple; fix Kapp/Kdapp/dHapp properties. Cover the N-only failure and compensating covariance. No new output rows or sampling controls.
3. **Other consumed derived values:** apply the same rule to total H and c-values, trace newly run advanced-analysis inputs, and leave centre-only graph guides alone.
4. **Exact profile mappings:** extend the current mapper for explicitly listed single-owner cases. Decide the existing linked approximation separately. Do not add new optimization calls.
5. **Consumer/round-trip verification:** fix only demonstrated bypasses. Update concise scientific manual/help text and the relevant report-map entries after behavior is agreed and implemented; incidental internals do not belong in the manual.

Each slice should be reviewable without an architectural migration. Production changes should initially fit existing files. A new production type or persistent field must identify the concrete unsupported requirement that makes it necessary.

| Tempting addition | Why it is excluded |
|---|---|
| Independent N error in quadrature | Loses covariance and can double-count the influence already represented in refits. |
| New marginal Monte Carlo draws | Destroys the joint relationships already available and adds stochastic work. |
| Generic covariance/dependency framework | The model formulas and retained tuples already specify the needed dependence. |
| New derived estimate/status/cache objects | Existing `FloatWithError`, method evidence and presentation routes are sufficient for this proposal. |
| Global graph reconstruction for member quantities | Shared coordinates are already expanded into captured member refits. |
| New derived result storage/schema | Full evidence already exists; derived getters are not a new persisted result family. |
| Guide uncertainty controls/whiskers | Existing consumers draw centres only; no requested user-facing task needs them. |
| Generic derived-profile optimizer | Requires real solver/bounds/persistence work; not justified by the chosen minimal scope. |
| Rewriting summary-Kd or advanced-analysis methods | Changes a different estimand or method beyond this propagation design. |

The scientific basis for retaining correlations is the covariance term in the [NIST law of uncertainty propagation](https://www.nist.gov/pml/nist-technical-note-1297/nist-tn-1297-appendix-law-propagation-uncertainty). Here the retained fitted tuples provide that joint variation directly; no separate covariance estimate needs to be fitted or stored.

### Decisions still required before coding

The deliverable choice is settled: detailed design first, minimal extra profile machinery, and unsupported cases made explicit. These calculation choices remain proposals for review:

- Adopt direct retained-refit evaluation for the consumed quantities listed above, including nonlinear Ka/Kd SDs, while preserving their original centres and the existing collection-summary convention.
- Adopt the proposed single-refit/invalid-derived-refit handling, while retaining existing no-refit fallbacks, or choose another explicit policy; do not let an incidental constructor fallback decide it.
- Use zero error for newly unsupported multi-coordinate profile quantities; decide whether the existing linked marginal-width approximation stays for now or is explicitly replaced. Full exact derived profiling is deferred.

No solver, parameter, serialization, presentation or scientific behavior has been changed by this design. CR-029 remains unresolved until implementation and consumer validation are complete.
