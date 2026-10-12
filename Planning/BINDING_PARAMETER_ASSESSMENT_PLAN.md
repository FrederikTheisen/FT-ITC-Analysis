# Automatic assessment of fitted binding parameters

Status: Draft proposal (revised 2026-10-12). Planning only; the assessment consequence and the exact range values remain open decisions.

Extend the automatic binding assessment so that a binding model's improvement over the Null model is considered together with whether its parameters lie inside a physically plausible range. A good curve fit can still give a nonsensical interpretation, for example a very small binding ratio together with an enormous enthalpy. The goal is to catch some of this nonsense with a simple rule, not to judge every questionable fit.

## Scope and assumptions

- Each model parameter of interest has one allowed range. A parameter outside its range gives a worse automatic assessment. There are no advisory/severe levels and no context-dependent checks.
- Checked parameters: Kd, ΔH, and N (or α in syringe active-fraction mode). Derived values (ΔG, ΔS, −TΔS), ΔCp, Offset, and other model parameters are not checked.
- Fitted and fixed parameters are treated the same. A fixed value outside its range triggers the rule like a fitted one.
- Ranges are fixed screening limits, independent of the parameter-limit preference (Standard, Extended, No limit). They are chosen limits, not fundamental physical maxima, and their documentation says so.
- Plausible ranges lie slightly inside the Standard solver limits, so a Standard fit that hits a solver limit also falls outside the plausible range.
- Manual overrides remain authoritative, as today.
- Historical results keep their recorded assessments until explicitly updated.
- No concentration-context or c-value check is part of this feature. c-values remain the existing diagnostic display.
- No new user settings, expected-stoichiometry inputs, solver constraints, automatic refits, or model selection.

## Current behavior

`BindingAssessmentState.FromComparison` (`AnalysisITC.Core/DataClasses/BindingAssessmentState.cs`) uses **ΔAICc = AICc(Null) − AICc(binding)**. Rule `aicc-0-15-v1`:

| Saved comparison | Automatic outcome |
| --- | --- |
| ΔAICc ≤ 0 | No binding detected |
| 0 < ΔAICc < 15 | Inconclusive |
| ΔAICc ≥ 15 | Binding detected |
| Comparison unavailable or inconsistent | Not assessed |

The rule does not examine parameter values.

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
| ΔH | −800 to +800 kJ/mol (candidate) | Applies to both signs. Values are the fitted (observed) enthalpy, which can include buffer protonation contributions. |
| N or α | 0.15 to 20 (candidate) | One shared range for N and α. |

Change the Standard solver limit for N from 0.1–10 to **0.1–25**. Extended and No limit scale accordingly (0.005–500 and 0.00005–50,000). This is a solver-bound change affecting all new fits and is listed as its own implementation step.

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

New rule ID, candidate `aicc-0-15-ranges-v1`. The ranges are constants of that rule version; changing a range requires a new rule ID. `aicc-0-15-v1` is preserved for historical results.

| Comparison recommendation | All checked parameters in range | Any parameter out of range |
| --- | --- | --- |
| No binding detected | No binding detected | No binding detected |
| Inconclusive | Inconclusive | **Open decision** |
| Binding detected | Binding detected | **Open decision** |
| Not assessed | Not assessed | Not assessed |

### Open decision: the out-of-range outcome

**Option A — No binding detected.** Reuses all existing behavior: uncertainty skipping, output suppression, attempted-fit diagnostics, manual override, and the existing saved outcome set.
- The label claims more than the evidence. A common cause of tiny N is a concentration error; such a fit can show a clear isotherm with ΔAICc ≫ 15 and still be labelled No binding detected. The reason must be shown alongside the comparison.
- It broadens the meaning of No binding detected from "the Null model is as good" to also "no plausible binding model."

**Option B — a distinct outcome (or Inconclusive plus a skip flag).** Keeps the meanings separate, but extends the saved outcome set, the uncertainty gate, output policy, the web viewer, and every consumer of the outcome.

Either way, the visible reason names the parameter, its value, and the range, for example: “The fitted enthalpy, −50,000 kJ/mol, is outside the plausible range of ±800 kJ/mol; the binding interpretation is not accepted.” It must not state that the molecules cannot bind.

Manual overrides remain authoritative. An override to Binding detected restores today's behavior for that result, including uncertainty estimation, but keeps the out-of-range reason visible.

## Implementation outline

1. **Solver limit.** Change the Standard N upper limit to 25 in `ParameterSet.cs`. Check tests and documents that assume 10.
2. **Range evaluator (Core).** A pure function taking the fit's physical central values (fitted and fixed) and returning out-of-range findings: parameter, site/step/member, value, range. Uses the original-data best fit only; bootstrap or profile results never change it.
3. **Combined assessment.** One calculation used by the solver, `AnalysisResult` construction/update, and the reader. It must also replace comparison-only recalculations used for outcome display, including `NullModelComparisonPresentation.AutomaticOutcome`/`Conclusion(comparison)`. Under Option A, `Solver2.IsAssessedNoBinding` uses the combined outcome so that uncertainty is skipped.
4. **Persistence.** Save the new rule ID. Because the ranges are constants of the rule and the parameter values are already saved, the reader recalculates findings rather than storing them. Validation must be chosen by the saved rule ID; the current check in `FTXTCReader.cs` only validates when the saved ID equals `CurrentRuleId`, which would silently stop validating `aicc-0-15-v1` files once the constant changes. Older app versions accept unfamiliar rule IDs; confirm what they show and what a re-save from an older version preserves. Read `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md` before any format change.
5. **Presentation.** Both desktop applications show the effective outcome, the comparison recommendation, and the out-of-range reasons through shared Core text, formatted with `FloatWithError`/`Energy`. Carry the reasons through report previews, PDF, table and clipboard exports, interpretation evidence, and the web viewer, which must display the saved combined outcome. Update `Documentation/ANALYSIS_REPORT_MAP.md` (A01, A06, E05/E08, P03) when changing report output.
6. **Documentation.** Update `Documentation/UserManual/pages/08-results-advanced-analysis.md`, `Resources/HelpTextResource.txt`, and `Resources/ScienceHelpResource.txt` with the ranges, that they are chosen screening limits, and the resulting assessment.

## Validation

Focused tests with independently specified expectations. These test assessment policy and parameter mapping; they are not forward-model validation.

- Each range end exactly, and values just inside and just outside, for Kd, ΔH (both signs), and N/α.
- Unit conversions: 30 mM = 0.03 M is out of range; −50,000 kJ/mol = −5×10⁷ J/mol is out of range; display units cannot change a finding.
- Fixed parameters outside a range trigger the rule.
- Every model's parameter roles, all sequential steps, both sites, active-fraction mode, Competitive intrinsic vs apparent Kd, Dissociation convention, temperature-dependent members.
- Non-finite and non-positive values.
- The comparison-only rule table is unchanged for `aicc-0-15-v1`; the combined table for the new rule.
- Uncertainty gating and manual overrides for single, pooled, and independent fits.
- Round trip of the new rule ID; historical `aicc-0-15-v1` files still validate and keep their outcomes.
- Matching reasons in both desktop applications, reports, exports, and the web viewer.

Run `AnalysisITC.Core.Tests`, the relevant Avalonia tests, and macOS build checks. ITC-090 contains separate known uncertainty/update failures; distinguish them from new regressions.

## Decisions before implementation

1. The out-of-range outcome (Option A or B).
2. Final ΔH and N/α range values.
3. Confirmation of the Competitive and Dissociation parameter treatment above.

Related existing work: ITC-002 (numerical bounds), ITC-014 (assessment policy), ITC-016/073 (member scope), ITC-050/051 (health and output), ITC-052/083 (uncertainty gating), ITC-090 (update integrity). These relationships do not authorize resolving adjacent tracker issues as part of this feature.
