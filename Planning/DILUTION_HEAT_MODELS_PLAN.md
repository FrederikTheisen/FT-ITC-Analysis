# Linear and Exponential dilution-heat models — plan

Status: planning (scientific plan revised 2026-10-10; consolidated 2026-10-13). Linear and Exponential are not implemented. This file is the single source for this feature's scope, decisions, implementation stages, remaining work, and audit history; record decisions here rather than in separate issue or tool-specific plans.

The former ITC-075 correction plan and tracker entry have been consolidated here. Their completed corrections are recorded under [Completed foundation](#completed-foundation); their remaining compatibility work belongs to the implementation stages below. Removing the duplicate records does not mark the new models or remaining infrastructure as implemented.

Keep concerns about the planned implementation in this document. `Planning/ISSUES.md` records issues with the current program, not defects that might arise after these models are implemented.

Implementation should use **gpt-6-luna agents**, with the shared Core contract completed before parallel desktop integration.

Add both families to every existing binding model and to the automatic Null comparison. Keep manual selection and Offset as the default. Preserve the existing working-tree changes.

## 1. Decisions

### Equations

For injection \(i\):

- \(x_i\): saved `ActualTitrantConcentration`, the post-injection total titrant concentration in M.
- \(m_i\): injected titrant amount, the existing `InjectionMass`.
- \(h(x)\): model-estimated dilution heat per injected mole.
- Background injection heat: \(q_{\mathrm{bg},i}=m_i h(x_i)\).

| Family | Molar background law | Parameters and stored units |
|---|---|---|
| Offset | \(h(x)=a\) | \(a\): J/mol |
| Linear | \(h(x)=a+bx\) | \(a\): J/mol (intercept at \(x=0\)); \(b\): J/mol/M |
| Exponential | \(h(x)=q_\infty+A e^{-x/\lambda}\) | \(q_\infty\) (plateau), \(A\): J/mol; \(\lambda>0\): M |

Use this **identical additive background** in both hypotheses. Binding plus background keeps the existing binding-heat calculation and adds \(q_{\mathrm{bg},i}\). The Null predicts only \(q_{\mathrm{bg},i}\).

This is an empirical endpoint convention: no midpoint evaluation, reference injection volume, or extra finite-injection averaging. Concentration bookkeeping and binding-heat bookkeeping remain separate. A fitted background does not establish that mixing, diffusion, or concentration errors have been corrected.

### Product choices

- Support One-Set, Two-Sets, Sequential, Competitive, and Dissociation. No family gates per binding model. Dissociation with Exponential is not expected to be well identified; this is accepted.
- One **Dilution heat model** selector: Offset, Linear, Exponential. Exponential is not visibly marked as experimental; it may be hidden later if it does not work well.
- One result uses the same Null model identity for all members; independently fitted members may have different parameter values.
- Only the constant term is editable/fixable, using the existing Offset parameter and label: Linear intercept \(a\), Exponential plateau \(q_\infty\). Family-specific tooltip/help text explains what the constant means; no family-specific labels. Slope, amplitude, and decay scale are fitted automatically and appear in results, reports, and exports.
- The existing global sharing control applies to the **entire background**: local or same for all. Sharing assumes the same molar curve \(h(x)\) across experiments; injected-mole normalization or use of the same titrant alone does not establish this. No temperature-dependent background sharing. Condition checks for sharing are out of scope (ITC-086).
- Preserve assessment thresholds and manual overrides.
- Defer matched-control constraints, automatic family selection, and a new component-overlay control. Buffer subtraction remains a separate processing step.

### Null comparison constraints (agreed, intentional Offset change)

New Null fits use the same heat limits, fixed constant, and sharing structure as the binding fit. Other coefficients are refitted separately. Consequently, refitting an old analysis can change its comparison; loading an old analysis must not.

- Heat limits are modelling constraints intended to prevent essentially non-physical fitted backgrounds. A lower residual objective outside these limits is not a reason to relax them. Use the same constraints in both hypotheses, and state in the comparison details when the Null background is fixed or touches a heat limit.
- Use the existing Offset limit expansion for Standard/Expanded/No limits in both hypotheses. "No limits" currently means a large finite range. The Offset-specific `UnreachableOffsetLimits` helper is not the limit policy for the new comparisons.
- This partially resolves ITC-018 (matching background sharing only).

### Heat limits

- The active Offset heat limits (Standard/Expanded/No limits) apply to the **predicted molar background at the lowest and highest included concentrations**. Linear and Exponential are monotonic, so this covers the whole included range. Shared backgrounds use the union of included concentrations. Snapshot the effective numerical limits.
- For Linear and Exponential, the stored constant (intercept or plateau) is an extrapolation and is **not** checked against the Offset limits. The initial-value limit check (`InitialParameterLimitViolationDetector`) must skip it for these families.
- A fixed constant is used as entered. For background-constraint feasibility, block a fit with a specific reason only if no slope (Linear), or no amplitude and decay scale within the fixed λ interval (Exponential), can bring both endpoint predictions inside the limits. An infeasible starting λ alone is not grounds for rejecting the fit.
- Do not clip predictions after evaluation, and do not constrain every coefficient separately to the Offset range.

### Decay-scale bounds

Use the fixed inclusive interval **1 nM ≤ λ ≤ 1 M** (`1e-9` to `1` mol/L), agreed on 2026-10-13. These hardcoded modelling limits allow margin around the plausible ITC concentration scales below. Do not derive bounds from each experiment's concentration steps or final concentration, or change them with the heat-limit mode.

- λ has units of cell titrant concentration (M), matching \(x\). Plausible injected amounts inform its range through cell volume and accumulation over the injection series; neither injected moles nor syringe concentration alone specifies the relevant cell concentration scale.
- Planning examples supplied by the user include injection volumes around 1 µL, syringe concentrations around 10–1000 µM, and cell volumes around 200–1500 µL, but also potentially much higher syringe concentrations such as 1 M salt. The small-injection estimate \(C_{\mathrm{syringe}}v/V\) gives initial concentration changes of roughly 0.0067–5 µM for the first range, or 0.67–5 mM for the 1 M example. Consider both concentration increments and the accumulated experimental range when choosing bounds. These are approximate design examples, not strict input-validation limits or an exhaustive operating range.
- The 1 nM lower bound allows margin below the smallest example concentration increment; the 1 M upper bound allows margin for accumulated high-concentration experiments. Broad fixed bounds do not ensure visible curvature in every individual experiment. They are practical modelling constraints, not guarantees of parameter identification or floating-point safety.
- Use the same fixed interval for local and shared backgrounds, both hypotheses, and bootstrap/leave-one-out/profile refits. Exclusions and resampling do not redefine the interval.
- The intended Exponential background is a smooth curve across the experiment. Amplitude and plateau may be poorly determined even when that curve is adequately fitted; report available uncertainty and limitations without treating that alone as fit failure.
- Fewer than two distinct included concentrations: the new family fails with a reason. Do not silently substitute Offset.
- Save the effective bounds with the fit and retain them when restoring saved results.

### Starting values

- Constant term, Null fits: mean of included molar heats \(q_i/m_i\), as a simple initial estimate. The optimizer determines the best fit under the selected weighting and constraints; this starting value need not be the least-squares solution.
- Constant term, binding fits: keep the existing `GuessOffset` (late-injection estimate); early injections contain binding heat. Late heats also approximate the Exponential plateau.
- User-entered constants are kept and used as starts.
- Slope and amplitude: 0 when feasible. Decay scale: the middle of the actual included concentration range, \(\lambda_0=(x_{\min}+x_{\max})/2\), brought inside the fixed λ interval when necessary. Use the union of included concentrations for a shared background. If these defaults violate the heat limits, construct a feasible start from the endpoint constraints while preserving the user-entered constant; for Exponential this may require adjusting λ within the fixed interval. This feasibility calculation does not require a coefficient pre-fit or grid search.
- Begin with these simple starts and compatible saved values. Do not require a coefficient pre-fit, a λ grid search, or multiple starts merely to obtain an accurate initial guess. Add initialization machinery only if representative optimizer tests show that it is needed.

### Profile-likelihood intervals (agreed 2026-10-13)

When profile likelihood is selected for an eligible fit, profile **all free physical background parameters**:

- Offset: constant \(a\).
- Linear: intercept \(a\) and slope \(b\).
- Exponential: plateau \(q_\infty\), amplitude \(A\), and decay scale \(\lambda\).

Constrain the named physical quantity through the shared coordinate mapping, even though the optimizer uses endpoint values and log λ. Do not substitute profiles of optimizer coordinates or combine their marginal intervals into an interval for a physical parameter.

For each profile, refit all other free binding and background parameters under the same fit constraints. Binding-parameter profiles likewise refit the full free background. Profile shared parameters once against the complete global objective; profile local parameters for each member while retaining the global refit where applicable.

Fixed constants are reported as fixed, without an estimated interval. If a confidence threshold is not reached before a bound, retain the bound-limited/unavailable-side diagnostic rather than treating the bound as a measured confidence endpoint. Report unresolved identification explicitly, including unidentified λ at zero amplitude.

Preserve the existing no-binding uncertainty skip and the decision not to estimate uncertainty for automatic comparison-only Null fits. This decision changes which physical quantities receive profiles, not uncertainty eligibility or the reported original-data optimum.

## 2. Shared Core implementation

**Model composition and interfaces**

Introduce a dilution-heat family type distinct from concentration `DilutionMethod`, with shared parameter descriptors, molar evaluation, injection-heat evaluation, and fit constraints. Add an explicit background-evaluation API to `Model`; keep the existing evaluation flag as a compatibility adapter meaning "include the entire background".

Integrate through `AnalysisBuilder`, `ModelFactory`, model options, session state, synthetic clones, and all five binding implementations. Register Linear and Exponential Null model identities with `IsNullModel = true`; keep Offset's identity and parameter key.

Use the existing Offset key for the constant term and append stable keys for slope, amplitude, and decay scale. Keep hidden fitted parameters separate from the editable setup parameters. Preserve user-entered constants and per-family setup state across ordinary rebuilds.

`BufferSubtraction` offers numerical patterns, but its injection-number fits, control-data handling, uncertainty approximation, and failure fallback are not reused as the scientific model.

`TandemMixingScan` stays Offset-only.

**Fitting and constraints**

- Use stable, scaled optimizer coordinates. For free backgrounds, optimize bounded values at the concentration-range endpoints; for Exponential, also optimize log decay scale. Convert to the physical parameters for evaluation and reporting.
- Settle a shared Core mapping contract before desktop integration: physical parameter keys always denote intercept/plateau, slope, amplitude, and λ. Do not store endpoint values or log λ under those keys. The mapping must cover conversion in both directions, bounds, step sizes, fixed values, both optimizers, global sharing, and uncertainty refits. Persist and report physical quantities with the actual meaning of their constraints.
- Enforce the fixed constant through the coordinate mapping.
- Use the simple starting values above with the existing optimizers. Test convergence over representative physical experiments before deciding whether an additional deterministic start or decay-scale search is necessary.
- Reuse the constraint mapping in both optimizers, bootstrap, leave-one-out, and profile likelihood. Uncertainty refits start from the original-data optimum and retain the existing iteration budgets and cancellation handling; additional starts require evidence from robustness tests.
- Require finite physical parameters as well as finite predictions. Handle exponential underflow, large-scale cancellation, and conversion overflow explicitly.
- Report boundary contact, numerical rank loss, and unresolved profile intervals as fit-health limitations, including applicable diagnostics in Null comparison details. At zero amplitude, λ is unidentified even if optimization succeeds. Do not silently reduce the parameter count, change family, or treat poor identification as absence of binding.

**Comparison and uncertainty**

Replace Offset-specific construction in `NullModelComparisonCalculator` with family-aware construction. Fit each hypothesis separately using identical observations, weights, background constraints, and sharing.

Count every free parameter, including automatically fitted background coefficients; count shared coordinates once and fixed coordinates zero times. Keep the estimated residual-variance parameter and likelihood conventions:

\[
\Delta\mathrm{AICc}=\mathrm{AICc}(\text{background})-\mathrm{AICc}(\text{binding + background}).
\]

Preserve independent member assessments, pooled diagnostics, and mixed outcomes.

The comparison asks whether adding binding improves the explanation under the selected background family and restrictions. Matching those restrictions does not establish that they are appropriate for every experiment.

Joint uncertainty includes all fitted background parameters and their covariance with binding parameters. Report the original-data optimum, never a bootstrap average. Keep the no-binding uncertainty skip (ITC-052). Automatic comparison-only Null fits continue to report uncertainty as **not estimated**.

Generalize corrected observations, predictions, exports, and confidence bands to injection-specific background subtraction. Keep the corrected-band convention: subtract the primary fitted background from total bootstrap predictions, retaining joint covariance. This translates the total-fit uncertainty band; it is not an uncertainty interval for the isolated binding component. Preserve that distinction in results and documentation.

## 3. Persistence and saved evidence

Follow `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md`.

- Preserve historical Offset model schemas and schema-1 Null comparisons. Missing background selection means Offset only for historical records.
- Add a composition-aware binding model schema for non-Offset backgrounds, with explicit family, endpoint convention, and heat-bookkeeping identity. Older readers must reject unsupported fits rather than read them as Offset (same mechanism as the Dumas/Discrete heat-method schemas).
- Add schema-2 Null comparison evidence with actual model identity, complete parameters, constraints, effective bounds (heat limits and decay-scale bounds), diagnostics, and captured fit inputs. Keep package-level versioning unchanged where independently versioned records suffice.
- Capture real injection amounts, volumes, post-injection concentrations, inclusion flags, observed heats, predictions, and required experiment context. Restore from captured evidence without invented concentrations or current preferences.
- Carry the family and parameters through bootstrap matrices, profile records, global replicate state, validity snapshots, import/copy remapping, and Save Selected.
- Preserve saved assessments and scores without refitting. Keep strict/recovery behavior and primary results when optional comparison evidence is damaged.
- Update desktop and Web viewer readers, registries, schemas, and compatibility tests together. `.ftitc` remains read-only.

No application-version, release-note, citation, commit, or push changes.

## 4. Desktop presentation, reports, and exports

Keep macOS and Avalonia aligned with their own renderers.

- Add the selector and whole-background sharing control using shared Core labels and tooltips.
- Keep the Offset editing control and label; family-specific tooltip text explains intercept/plateau.
- Generalize existing correction and parameter-display controls to the whole background. Preserve their saved setting keys.
- Show binding model and dilution family separately. Distinguish the background fitted within the binding fit from the independently fitted Null.
- Report all background parameters with correct units and available uncertainty through Core formatters. Slope and decay-scale units differ from molar-energy units.
- Keep uncorrected heats for Null figures and existing assessment-based output policies. Propagate correction through live graphs, publication figures, previews, printing, clipboard, and numerical exports.
- Include family, coordinate convention, effective bounds, sharing, and identification limitations in fit details.

Update `Documentation/ANALYSIS_REPORT_MAP.md`, particularly A01/A03, A06, A10/A11, E04b/E05, and figure/injection-table descriptions. Reconcile entries affected by this implementation; the R05/E01 unavailable-label discrepancy was corrected on 2026-10-13.

Update fitting/results manual pages and help/science resources only with what users need to choose a family or interpret results: equations and endpoint convention, units, bound expansion, distinction from buffer subtraction, and possible compensation between binding and background.

## 5. Implementation order

0. **Neutral refactor:** introduce the family abstraction with Offset only and the existing Null behavior. Existing Offset fits, comparisons, saved files, and reports must be unchanged at floating-point precision.
1. **Null constraint change:** apply limits, fixed constant, and sharing to the Offset Null, with comparison-detail flags. Separate commit/step so intentional changes can be told apart from refactoring regressions.
2. Linear and Exponential equations and fitting.
3. Comparison and uncertainty.
4. Persistence.
5. Desktop, report, export, and Web viewer integration; manual and help.

Reinspect the working tree before each stage. Run the full `AnalysisITC.Core.Tests` suite, relevant Avalonia and Web tests, native macOS graph/UI checks, and desktop builds. No packaging or release scripts.

## 6. Acceptance criteria

| Test category | Required evidence |
|---|---|
| **Neutral refactor** | Stage 0 leaves existing Offset predictions, fits, comparisons, assessments, saved files, and reports unchanged. |
| **Equation checks** | Independently calculated, high-precision expectations for all three laws, both amplitude signs, zero slope/amplitude, unequal volumes, unit conversions, exclusions, and tandem concentration states, at fixed parameters, agreeing at floating-point precision. |
| **Composition checks** | For every binding model and bookkeeping method, total heat equals independently checked binding heat plus the specified background. Zero slope/amplitude reproduce Offset predictions. |
| **Fitting robustness** | Weighted/unweighted fits, both optimizers, simple starts, fixed constants, local/shared backgrounds, all limit settings, flat data, short series, decay scale at both fixed bounds, cancellation, and invalid inputs. Test the stated physical instrument ranges and the chosen margin. On representative cases with an admissible Offset fit, verify that the richer fits recover an objective no worse than that feasible constant solution within numerical tolerance. Compare background-only solutions against independent constrained least-squares/profile calculations. Use failures to decide whether extra initialization machinery is needed. |
| **Model discrimination** | Independently calculated AICc and parameter counts, including fixed constants and sharing. Insufficient sample size, failed hypotheses, and mixed independent outcomes. Do not require richer backgrounds always to reduce binding evidence. |
| **Parameter uncertainty** | Synthetic recovery and repeated-data coverage checks, labeled separately. Joint resampling; profiles of every free physical background parameter through the coordinate mapping; fixed-constant exclusion; local/shared refits of the other free parameters; boundary, unidentified-parameter and open-interval reporting; original-fit central estimates; and uncertainty round trips. |
| **Compatibility and presentation** | Historical files keep saved predictions and assessments; new files survive strict reopening, Save Selected, import/copy, and recovery. Both desktops and the viewer show matching families, values, corrections, and unavailable states. |

Include a targeted start-feasibility case: heat limits ±1 kJ/mol, fixed plateau +2 kJ/mol, and concentrations 10–110 µM require λ ≥ \(100/\ln(3)\) ≈ 91.02 µM. The midpoint start of 60 µM is infeasible, but 100 µM permits a feasible amplitude. When the allowed λ interval includes such values, initialization must find a feasible start without rejecting the fixed plateau. This checks the interaction with a fixed plateau and heat limits, not the accuracy of the ordinary λ starting guess.

Unequal-volume equation checks verify the selected convention. Experimental sensitivity checks assess whether that convention adequately describes unequal-volume data; equation agreement alone does not establish this.

Re-run the supplied experimental cases under the endpoint convention and active limit settings. Record differences from the exploratory midpoint/unrestricted fits, including P13 parameter sensitivity, ambiguous Y242A curves, persistent cobalt evidence, and the CrsL counterexample. These are sensitivity and regression cases, not a calibrated classification benchmark.

**External validation remains an evidence gap.** Core consistency checks and independently calculated equation tests must not be called external forward-model validation. That claim requires an external generator covering the exact model and finite-injection protocol, evaluated on integrated heats at floating-point precision.

## 7. Out of scope

- Experiment Designer.
- Full AI interpretation support remains deferred. Before that route accepts new-family results, either export the family and physical quantities with correct units, parameter meanings, and constraint meanings, or explicitly exclude unsupported result evidence. Omitting only extra coefficients is insufficient if the receiving system cannot tell which background was fitted. The existing exporter would otherwise assign slope and decay scale fallback unit `"1"` and could mistake optimizer-coordinate bounds for physical-parameter bounds. This is a prospective integration requirement, not a current-program issue; do not log it in the issues tracker. MIST must accept the evidence schema before a client sending it is released; deployed acceptance of the current schema 2.2 remains unverified. Any future schema change retains that deployment dependency; see the [relay contract](../Documentation/Interpretation/MIST_RELAY_CONTRACT.md).
- Sharing condition checks (ITC-086).
- Standalone Null-selection workflows, matched controls, automatic family selection, threshold calibration.

Record implementation progress and completion in this document. On completion, update ITC-018 in `Planning/ISSUES.md` for the partial resolution of matching background sharing; broader global Null-model design remains that issue's responsibility. ITC-008's unmatched-member label fallback and ITC-025's pooled-report diagnostic decision remain separate current-program issues.

## 8. Open items

- AI interpretation route: choose correct evidence support or explicit exclusion before enabling new-family results on that route; full interpretation support can remain a later feature.

## Completed foundation

The Null-role corrections formerly tracked as ITC-075 were implemented on 2026-10-08 and audited on 2026-10-13. They are the baseline for this feature, not evidence that alternative models are implemented. Preserve these contracts while carrying out the staged changes above:

- **Terminology and presentation:** Null is the no-binding role; Offset remains the current model and parameter name. Use “model”, not “flavor”. Shared model fields show the actual saved model name, or `Unknown`, with status and scope separate. Standalone figures use `Null (Offset)` or `Null (model unknown)`. Reports, tables and clipboard output use named parameters from matching saved solutions and appropriate units; scalar `member.Offset` fallback requires confirmed Offset evidence. Manual/help and current scientific guidance distinguish the role from the implementation; older archived guidance was retained.
- **Identity and legacy evidence:** Generic comparisons have no implicit model identity; construction and restoration assign it explicitly. `Model.IsNullModel` excludes recursive comparisons. The current schema-1 writer preserves missing identity as readable status-only evidence with reason “Saved Null model identity was missing.”, rather than silently losing the comparison on reopen. The Offset-only wire shape and restoration remain the compatibility baseline until the planned schema and restoration changes.
- **Saved-solution figures:** Resolve exactly one saved Null solution matching the experiment, registered model identity and injection IDs. Missing, mismatched or ambiguous solutions show the role-and-model unavailable label. Draw the saved solution without refitting, scalar reconstruction, binding-parameter substitution or mutation of saved/live state. Null figures ignore model-background correction, use observed-minus-Null residuals, and retain processing-derived error bars. Preserve binding-model names when the binding fit is displayed.
- **AI evidence:** Schema 2.2 carries actual identity and named parameters on independent-member and Null-member records in both full and compact input; schemas 2.0 and 2.1 remain accepted. Values, units and constraints come from the Null solution, with uncertainty only when computed, including after native restoration. The compatibility Offset field is guarded by identity. This does not complete the new-family AI integration described above.
- **Existing coverage:** Presentation/export/AI contracts, Null-role exclusion, pooled and independent save/reopen/save/reopen, missing identity, and saved varying predictions are covered. Figure tests include identity/injection mismatches, ambiguous solutions, ignored correction and unchanged binding labels. Test-only varying models establish presentation contracts, not external forward-model validation.

Audit history:

- **2026-10-08, revision 3:** Core 2,312 passed / one existing skip; Web 347 passed; 72 relevant Avalonia tests passed in isolated class runs; native managed/CoreGraphics and AppKit layout checks passed. Standard and diagnostic Skia report pages were rendered and inspected. The full Avalonia run encountered UI-thread ownership failures and stalled before the isolated checks.
- **2026-10-08, model-field context correction:** Core 2,357 passed / one existing skip; 53 relevant Avalonia tests passed in isolated class runs; native managed/CoreGraphics and AppKit layout checks passed.
- **2026-10-13, audit:** No runtime regression found in the audited corrections. Core 3,396 passed / one existing skip; Web 347 passed; 86 selected Avalonia tests passed; native graph and figure-selection checks passed. The earlier inventory had been static only; these are subsequent runtime results.
- **2026-10-13, maintenance:** Removed three obsolete Final Figure source-string assertions while preserving the Swift layout and other checks; aligned report-map R05 with E01's unavailable label. `AnalysisNullInspectorLayout.swift`, native `AnalysisNullGraphTests`, native `FinalFigureModelSelectionTests`, and the three-file `git diff --check` passed. Core/Web/Avalonia audit runs were reused for this test/documentation-only change. Logs: `/tmp/itc075-maintenance-layout.log` and `/tmp/itc075-maintenance-native.log`. No packaging, signing, deployment, commit or push was part of this work.
