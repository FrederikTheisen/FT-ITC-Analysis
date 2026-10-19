# Automatic assessment of fitted binding parameters

Status: Draft proposal (revised 2026-10-12). Parameter screening is not implemented or released. Decided: out-of-range parameters give No binding detected; no new reason field.

Extend the automatic binding assessment so that a binding model's improvement over the Null model is considered together with whether its parameters lie inside a physically plausible range. A good curve fit can still give a nonsensical interpretation, for example a very small binding ratio together with an enormous enthalpy. The goal is to catch some of this nonsense with a simple rule, not to judge every questionable fit.

## Scope and assumptions

- Each model parameter of interest has one allowed range. A parameter outside its range makes the automatic assessment No binding detected. There are no advisory/severe levels and no context-dependent checks.
- No reason, finding text, or warning is added for an out-of-range result. The assessment presentation does not have a reason field today, and this feature does not add one.
- Checked parameters: Kd, ΔH, and N (or α in syringe active-fraction mode). Derived values (ΔG, ΔS, −TΔS), ΔCp, Offset, and other model parameters are not checked.
- Fitted and fixed parameters are treated the same. A fixed value outside its range triggers the rule like a fitted one.
- Ranges are fixed screening limits, independent of the parameter-limit preference (Standard, Extended, No limit). They are chosen limits, not fundamental physical maxima, and their documentation says so.
- Plausible ranges lie slightly inside the Standard solver limits, so a Standard fit that hits a solver limit also falls outside the plausible range.
- Manual overrides remain authoritative, as today.
- No concentration-context or c-value check is part of this feature. c-values remain the existing diagnostic display.
- No new user settings, expected-stoichiometry inputs, automatic refits, or model selection. The proposed change to the existing N solver bound is a separate decision, still to be confirmed.

## Current behavior

`BindingAssessmentState.FromComparison` (`AnalysisITC.Core/DataClasses/BindingAssessmentState.cs`) uses **ΔAICc = AICc(Null) − AICc(binding)**. Rule `aicc-0-15-v1`:

| Saved comparison | Automatic outcome |
| --- | --- |
| ΔAICc ≤ 0 | No binding detected |
| 0 < ΔAICc < 15 | Inconclusive |
| ΔAICc ≥ 15 | Binding detected |
| Comparison unavailable or inconsistent | Not assessed |

The rule does not examine parameter values. The reader already reevaluates the assessment on load using the current rule, retaining manual overrides without refitting.

Solver limits (`ParameterSet.cs`), with factors ×20 (Extended) and ×2000 (No limit):

| Setting | N | ΔH | Kd (from log₁₀Ka −2 to 20) |
| --- | --- | --- | --- |
| Standard | 0.1–10 | ±1,000 kJ/mol | 10⁻²⁰ M to 100 M |
| Extended | 0.005–200 | ±20,000 kJ/mol | wider |
| No limit | 0.00005–20,000 | ±2,000,000 kJ/mol | wider |

Under Standard limits, ΔH = −50,000 kJ/mol and N = 0.001 are unreachable; such fits instead stop at a solver limit and only raise the separate parameter-boundary warning.

Only an effective No binding detected skips uncertainty estimation (when the existing preference is enabled), via `Solver2.IsAssessedNoBinding`. `BindingAssessmentInterpretation` and `ResultOutputPolicy` suppress binding output only for effective No binding detected; Inconclusive keeps parameters and uncertainty and adds a health warning.

## Proposed ranges

All comparisons are inclusive: a value equal to a range end is inside. Kd is checked as the physical value in M, not the optimizer's log₁₀Ka coordinate; ΔH in J/mol.

| Parameter | Plausible range | Notes |
| --- | --- | --- |
| Kd | 1 pM to 10 mM (10⁻¹² to 10⁻² M) | Kd above 10 mM is treated as implausible regardless of experimental design. This deliberately excludes very weak affinities that low-c experiments can sometimes estimate. |
| ΔH | −950 to +950 kJ/mol | Applies to both signs. Values are the fitted (observed) enthalpy, which can include buffer protonation contributions. Standard solver limit is ±1,000 kJ/mol. |
| N or α | 0.11 to 24 | One shared range for N and α. This proposal assumes the Standard solver limit of 0.1–25 below, which is separately pending confirmation. |

Separate proposal, pending confirmation: change the Standard solver limit for N from 0.1–10 to **0.1–25**. Extended and No limit would scale accordingly (0.005–500 and 0.00005–50,000). This changes fitting behavior independently of assessment; parameter screening itself does not require changing solver bounds.

## Model meanings

| Model | Checked parameters |
| --- | --- |
| One Set of Sites | Kd, ΔH, N (α in active-fraction mode). |
| Two Sets of Sites | Each site's Kd, ΔH, and N; the shared α in active-fraction mode. |
| Sequential Binding Sites | Each step's Kd and ΔH. Site count is model setup, not a parameter. |
| Competitive Binding | Fitted (intrinsic) ligand Kd, ΔH, N or α. Apparent Kd is derived and not checked. Competitor Kd/ΔH are supplied experiment inputs, not model parameters, and are not checked. |
| Dissociation | Kd = 1/Ka of the dimerization equilibrium and the association ΔH (per mol dimer). No N. |

In temperature-dependent joint fits, check each member's physical Kd and ΔH at its saved temperature. For independent fits, each member is checked against its own values. One out-of-range site or step affects the assessment of the whole fitted model. No site is discarded and nothing is refitted.

Non-finite or non-positive required values (e.g. Kd ≤ 0, non-finite ΔH) count as out of range.

## Proposed assessment rule

New rule ID, candidate `aicc-0-15-ranges-v1`. The ranges are constants of that rule version; changing a range requires a new rule ID.

| Comparison recommendation | All checked parameters in range | Any parameter out of range |
| --- | --- | --- |
| No binding detected | No binding detected | No binding detected |
| Inconclusive | Inconclusive | No binding detected |
| Binding detected | Binding detected | No binding detected |
| Not assessed | Not assessed | Not assessed |

Mapping out-of-range results to No binding detected reuses all existing behavior: uncertainty skipping (when the existing preference is enabled), output suppression, attempted-fit diagnostics, manual override, and the existing saved outcome set. No new outcome, reason field, or warning is added.

Accepted consequences:
- No binding detected now also covers "no plausible binding model," not only "the Null model is as good." The rule definition text in the app and manual must say so.
- A fit with a concentration error can show a clear isotherm with ΔAICc ≫ 15 and still be assessed No binding detected. The comparison table continues to show the ΔAICc; the user can override.

Manual overrides remain authoritative, as today. An override to Binding detected restores binding-output eligibility and prevents assessment-based uncertainty skipping when the fit is next run or updated. It does not generate uncertainty that was already skipped: the result remains stale until the user runs Update Result.

## Implementation outline

1. **Confirm ranges and solver scope.** Confirm the proposed ΔH and N/α ranges and separately decide whether to raise the Standard N upper limit to 25. If approved, change that limit in `ParameterSet.cs` and check tests and documents that assume 10. Do not change solver bounds merely as part of adding assessment checks.
2. **Range evaluator (Core).** A pure function taking the fit's physical central values (fitted and fixed) and returning whether every checked parameter is in range. Uses the original-data best fit only; bootstrap or profile results never change it.
3. **Combined assessment.** One calculation used by the solver, `AnalysisResult` construction/update, and the reader's existing reevaluation path, using saved fitted values on load. It must also replace comparison-only recalculations used for outcome display, including `NullModelComparisonPresentation.AutomaticOutcome`/`Conclusion(comparison)`. `Solver2.IsAssessedNoBinding` uses the combined outcome so that uncertainty is skipped.
4. **Persistence.** Keep the existing format and save the current rule ID. The parameter values are already saved, so no range-check metadata or format change is needed.
5. **Presentation.** No new presentation elements. Existing outcome displays in both desktop applications, reports, exports, interpretation evidence, and the web viewer must show the result's combined assessment, including its manual override, rather than recalculating from ΔAICc. Check `Documentation/ANALYSIS_REPORT_MAP.md` (A01, A06, E05/E08, P03) for entries that describe the outcome as ΔAICc-only.
6. **Rule definition text.** The existing definitions state the outcome purely in ΔAICc terms and describe No binding detected as "relative to the Null model": `NullModelComparisonPresentation.RuleExplanation`, `Resources/HelpTextResource.txt`, and `Documentation/UserManual/pages/08-results-advanced-analysis.md`. Add the range rule, the range values, and that they are chosen screening limits.

## Validation

Focused tests with independently specified expectations. These test assessment policy and parameter mapping; they are not forward-model validation.

- Each range end exactly, and values just inside and just outside, for Kd, ΔH (both signs), and N/α.
- Unit conversions: 30 mM = 0.03 M is out of range; −50,000 kJ/mol = −5×10⁷ J/mol is out of range; display units cannot change the outcome.
- Fixed parameters outside a range trigger the rule.
- Every model's parameter roles, all sequential steps, both sites, active-fraction mode, Competitive intrinsic vs apparent Kd, Dissociation convention, temperature-dependent members.
- Non-finite and non-positive values.
- The comparison-only rule table is unchanged for `aicc-0-15-v1`; the combined table for the new rule.
- Uncertainty gating and manual overrides for single, pooled, and independent fits. An override after uncertainty was skipped leaves the result stale until Update Result.
- Reopening an out-of-range fit applies the parameter checks through the existing reevaluation path, retaining manual overrides, fitted values, and stored uncertainty.
- An out-of-range fit with ΔAICc ≥ 15 shows No binding detected in both desktop applications, reports, exports, and the web viewer.

Run `AnalysisITC.Core.Tests`, the relevant Avalonia tests, and macOS build checks. ITC-090 contains separate known uncertainty/update failures; distinguish them from new regressions.

## Decisions before implementation

Decided: out-of-range → No binding detected; no reason field; Competitive and Dissociation treatment as above; Kd range as in the table.

Still to confirm: the proposed ΔH range, the N/α range, and the separate increase of the Standard N solver upper limit from 10 to 25. These proposals are not implementation approval.

Related existing work: ITC-002 (numerical bounds), ITC-014 (assessment policy), ITC-016/073 (member scope), ITC-050/051 (health and output), ITC-052/083 (uncertainty gating), ITC-090 (update integrity). These relationships do not authorize resolving adjacent tracker issues as part of this feature.
