# Derived-parameter uncertainty from retained refits

Implemented for residual bootstrap and leave-one-out on 6 November 2026. See [ITC-117](ISSUES.md#itc-117---derived-parameter-uncertainty-across-all-models).

## Problem

Before ITC-117, these derived values used `FloatWithError` arithmetic on the fitted values' uncertainties. That arithmetic treats fitted values as independent and loses their correlation. The derived values are Kd, ΔG, −TΔS, ΔS, competitive apparent Kd/Ka/ΔH, total ΔH and c-values. Two examples follow.

- −TΔS and ΔS come from ΔG − ΔH in quadrature, so they can be too large or too small. Example: SDs of 0.3 and 1.0 kJ/mol with correlation +0.8 give a true SD of about 0.78 kJ/mol, while quadrature gives 1.04.
- Competitive apparent Kd omits N's uncertainty entirely, because the competition factor reads N's plain value ([CR-029](CODE_REVIEW_2026-10.md#cr-029--competitive-apparent-kd-omits-fitted-stoichiometry-uncertainty)).

Residual bootstrap and leave-one-out already keep every retained refit. Computing each derived value inside every refit and summarizing those values around the best fit includes the correlations directly. No covariance estimate is needed.

## Scope

- **In scope:** residual bootstrap and leave-one-out, i.e. the methods with retained refits.
- **Profile likelihood is unchanged.** A profile run stores one interval per fitted value, with only that value's endpoint (`ProfileSideResult.Endpoint`) and nothing about how the other values moved. Profile results therefore keep today's arithmetic: one-input quantities are converted exactly, several-input quantities are combined as if independent. Profile mode's own defects are tracked separately in [ITC-228](ISSUES.md#itc-228---failed-input-profiles-still-give-derived-values-an-interval) and [ITC-229](ISSUES.md#itc-229---profile-mode-apparent-values-omit-n-and-linked-affinity-uncertainty).
- **Done elsewhere:**
  - [ITC-217](ISSUES.md#itc-217---a-single-retained-refit-gives-an-interval-without-an-sd): a single retained refit gives the best fit with zero SD and collapsed bounds.
  - [ITC-226](ISSUES.md#itc-226---entered-input-sds-were-added-to-apparent-values-and-c-values): entered input SDs are no longer added through arithmetic.
- **Unchanged:** fitted coordinates, ΔG, ΔH, the collection summary convention (CR-024/SUMMARY_SEM), prediction bands, advanced-analysis methods, and the linked global evaluation path.

## What the code already provides

- **Each retained refit is a complete solution.** `BootstrapSolutions` entries are full `ModelSolution` objects of the same model type (`ValidateBootstrapSolution`, `Models.cs`). Each has:
  - its own value-only `Parameters` (SD 0);
  - its own `Data`, with sampled concentrations, temperature and segments;
  - its own `ModelOptions`, with sampled values;
  - an empty `BootstrapSolutions` list.

  This holds for in-session refits (`GetSynthClone`, `SetSynthModelParameters`) and for refits restored from `.ftxtc` (`BootstrapModelSnapshot.Restore` stores concentrations, temperature, options, parameters and segments per refit). Calling an existing getter on a refit (`refit.Kd`, `refit.TdS`, `refit.Kdapp`) therefore gives that refit's derived value. The arithmetic runs on zero errors, and there is no recursion.
- **Fitted values** get their uncertainty once, when refits are set: `ComputeErrorsFromBootstrapSolutions` → `BootstrapEstimate` → `SummarizeBootstrapDistribution` (`ThermodynamicSolution.cs`, `Models.cs`). The result has the best-fit centre, the RMS spread about that centre, and the outward percentile interval (`FloatWithError.FromDistributionInPlace`, `GetConfidenceInterval`). Derived values reuse this summarizer and convention.
- **Thermodynamically linked global fits** already take ΔH, ΔG, −TΔS and Kd from the replicates (`LinkedThermodynamicParameter`, `ProfileMappedParameter`, `LinkedThermodynamicEvaluation`). Only apparent values, c-values and total ΔH still use arithmetic there.
- **Global members** receive member refits whose shared and linked coordinates are already expanded to member values (`GlobalSolution.SetBootstrapSolutions`, `ParameterSet.SetIndividualFromGlobal`). In global leave-one-out, a member has only the refits it took part in.
- **ΔG** is linear in log K, so the pool would reproduce today's SD and interval exactly. It needs no change.

## Decisions

1. **Mechanism:** evaluate the existing getter on each retained refit and take its value. No formula is duplicated.
2. **Kd** uses the pool. Its SD becomes the actual spread of the refits' Kd values; its interval was already exact.
3. **Unusable derived value:** if any retained refit yields a non-finite or unavailable derived value, including a known domain failure such as an unresolved competitor equilibrium, keep the best-fit value and omit uncertainty for that quantity (zero SD and collapsed bounds). Keep the refit pool and the existing refit admission rules.
4. **Storage:** compute the refit-based estimates once and store the full `FloatWithError`:
   - when error estimation finishes (`SetBootstrapSolutions`). Clear stored derived estimates when the pool is cleared.
   - during normal `.ftxtc` loading, after the reader has restored the saved parameter estimates and profile status, plus any global parent and replicates.

   Files keep only fitted values, so no file-format change is needed. Getters return the stored estimate when present, and otherwise today's arithmetic. Edits to experiment inputs use the existing stale-result handling.
5. **Profile likelihood** keeps today's arithmetic. Nothing is stored for a solution, or a member of a parent, that has a profile run.
6. **ΔG** stays on arithmetic (identical result).

## Design

### Evidence rule

Refit-based estimates are stored only when the solution has retained refits and neither the solution nor its parent has a profile run. The rule checks the evidence itself, not method fields: pooled global members carry `ErrorMethod = None`, and legacy files don't restore `ErrorMethod`. The existing linked evaluator already decides this way.

### Computation

One helper on `SolutionInterface` (`Models.cs`) runs at the two storage points. For each stored quantity:

1. Take the centre from today's arithmetic estimate, so best-fit values are unchanged.
2. Evaluate the quantity's getter on every retained refit and take its value.
3. If any value is unusable, store the centre with zero error. Otherwise store `SummarizeBootstrapDistribution(values, centre)`.

Results go into a small per-solution store, not into `Parameters`. `Parameters` holds fitted coordinates and is validated and serialized.

### Quantities

| Quantity | Getter | Notes |
|---|---|---|
| −TΔS | `EntropyContributionFor` (`ThermodynamicSolution.cs`) | Fallback branch; linked fits keep the evaluator. |
| ΔS | `EntropyFor` | Reads the stored −TΔS and divides by the same temperature, so it follows exactly. |
| Kd | `DissociationConstantFor` | Fallback branch only; linked and profile branches unchanged. |
| Apparent Kd, Ka, ΔH | `Kdapp`, `Kapp`, `dHapp` (`CompetitiveBinding.cs`) | Each refit's own N, sampled competitor values and concentrations come along automatically. This fixes CR-029 for refit methods. |
| Total ΔH | `TotalEnthalpy` (`Models.cs`) | Sum of the same refit's step enthalpies. |
| c-values | `AnalysisCValueCalculator.Calculate` | Computed per refit and matched by quantity id, not position. |

These cover every active site or step of One Set of Sites, Two Sets of Sites, Sequential Binding Sites, Competitive Binding and Dissociation, through the existing per-slot getters. Offset has no derived quantity.

### Global fits

- Member refits arrive with shared and linked coordinates already expanded, so member derived values need no global join.
- In global leave-one-out, each member uses only the refits it took part in.

## Consequences

- Reopening a bootstrap or leave-one-out project changes the displayed uncertainty of −TΔS, ΔS, Kd (SD only), the apparent values, total ΔH and c-values. Best-fit values do not change.
- Saved advanced-analysis outputs keep the values they were computed with; rerunning the analysis updates them.
- Approved interpretations become out of date through their existing fingerprint.

## Implementation steps

1. The helper, both storage points and the evidence rule, applied to −TΔS/ΔS and Kd.
2. Competitive apparent values.
3. Total ΔH and c-values.
4. Round-trip and consumer checks: report, per-fit export and clipboard, both desktop applications, and the web viewer must show the same estimates.
5. Manual and in-app help text, after the behaviour is in place.

## Validation

Add cases to the existing test files. Derive expected values independently of the code under test.

| Case | Expected result |
|---|---|
| −TΔS, correlated | Paired deviations (G, H) = (+300, +1000) and (−300, −1000) J/mol: SD 700, versus 1044.03 from quadrature. |
| −TΔS, anti-correlated | (+300, −1000) and (−300, +1000): SD 1300. |
| −TΔS, cancelling | Equal G and H deviations of ±1000: zero error, interval collapsed on the centre. |
| Kd | Refits at log K = ±log10 2 around 0: centre 1, SD √0.625, bounds [0.5, 2]. The centre is never replaced by the mean 1.25. |
| CR-029 | 10 µM cell and competitor, Kc = 10⁶ M⁻¹, intrinsic Kd 10 nM, refits with N = 0.9 and 1.1: apparent Kd 41.6227766017 and 33.1662479036 nM around 37.0156211872 nM, SD 4.24520645494 nM. Expected values come from the competitor/site mass balance, not the production equilibrium helper. |
| Compensating N and affinity | Same N values with intrinsic Kd 8.893116752251174 and 11.160629714523045 nM: apparent Kd stays at 37.0156211872 nM, zero spread. |
| Total ΔH | Two steps with equal and opposite deviations: zero spread. Same-direction deviations increase the spread. |
| c-values | Constant N/Kd ratio at fixed concentration: zero spread. Cover the first tandem segment and syringe-correction mode. |
| Unusable derived value | With multiple retained refits, one yields an unusable value for one quantity. Its best-fit value stays the same and its uncertainty is omitted. Other quantities still use the full pool. |
| Profile result | Unchanged from today's arithmetic. |
| Save and reopen | Identical estimates for a standalone fit and for global leave-one-out with members missing from some refits. |

Then run the full `AnalysisITC.Core.Tests` suite, and spot-check that both desktop applications show the same values as the report.

## Excluded

- **A separate N term in quadrature:** refits already contain N's effect, so this would count it twice.
- **New random draws or a covariance engine:** the retained refits already carry the joint variation.
- **New saved data or a file-format change:** derived values are recomputed from the saved refits.
- **Profile-mode changes:** these are ITC-228 and ITC-229.
- **Exact profiling of derived values:** this needs constrained refits and a new solver integration point, and is deferred.
