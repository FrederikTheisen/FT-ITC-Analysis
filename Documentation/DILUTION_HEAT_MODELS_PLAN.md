# Linear and Exponential dilution-heat models — plan

Status: planning (revised 2026-10-10 after review). No application changes have been made for this plan. This file is shared between tools; record decisions here rather than in tool-specific plans.

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

This is an empirical endpoint convention: no midpoint evaluation, reference injection volume, or extra finite-injection averaging. Concentration bookkeeping and binding-heat bookkeeping remain separate.

### Product choices

- Support One-Set, Two-Sets, Sequential, Competitive, and Dissociation. No family gates per binding model. Dissociation with Exponential is not expected to be well identified; this is accepted.
- One **Dilution heat model** selector: Offset, Linear, Exponential. Exponential is not visibly marked as experimental; it may be hidden later if it does not work well.
- Only the constant term is editable/fixable, using the existing Offset parameter and label: Linear intercept \(a\), Exponential plateau \(q_\infty\). Family-specific tooltip/help text explains what the constant means; no family-specific labels. Slope, amplitude, and decay scale are fitted automatically and appear in results, reports, and exports.
- The existing global sharing control applies to the **entire background**: local or same for all. No temperature-dependent background sharing. Condition checks for sharing are out of scope (ITC-086).
- Preserve assessment thresholds and manual overrides.
- Defer matched-control constraints, automatic family selection, and a new component-overlay control. Buffer subtraction remains a separate processing step.

### Null comparison constraints (agreed, intentional Offset change)

New Null fits use the same heat limits, fixed constant, and sharing structure as the binding fit. Other coefficients are refitted separately. Consequently, refitting an old analysis can change its comparison; loading an old analysis must not.

- Heat limits are not symmetric in effect: the binding model can partly absorb an out-of-range background through ΔH; the Null cannot. Do not gate the assessment on this, but state in the comparison details when the Null background is fixed or touches a heat limit.
- Under the "No limits" setting, the Null keeps the current unreachable bounds (`UnreachableOffsetLimits`), because the solvers need finite bounds.
- This partially resolves ITC-018 (matching background sharing only).

### Heat limits

- The active Offset heat limits (Standard/Expanded/No limits) apply to the **predicted molar background at the lowest and highest included concentrations**. Linear and Exponential are monotonic, so this covers the whole included range. Shared backgrounds use the union of included concentrations. Snapshot the effective numerical limits.
- For Linear and Exponential, the stored constant (intercept or plateau) is an extrapolation and is **not** checked against the Offset limits. The initial-value limit check (`InitialParameterLimitViolationDetector`) must skip it for these families.
- A fixed constant is used as entered. A fit is blocked, with a specific reason, only if no slope or amplitude can bring the endpoint predictions inside the limits.
- Do not clip predictions after evaluation, and do not constrain every coefficient separately to the Offset range.

### Decay-scale bounds

Bounds come from each experiment's included data, not fixed multiples of the span:

- Lower bound: the smallest step between consecutive included concentrations. A faster decay changes only one point and cannot be resolved. Physical scale for reference: ≥1 µL of ≥50 µM into ~200 µL gives steps of about 0.25 µM. This bound also limits \(e^{x_{\mathrm{first}}/\lambda}\) to roughly \(e^{\text{number of steps}}\), so the amplitude cannot overflow.
- Upper bound: 10 × the final included concentration. At this scale the curve cannot be distinguished from a straight line; contact is reported in those terms.
- Fewer than two distinct included concentrations: the new family fails with a reason. Do not silently substitute Offset.
- Save the effective bounds with the fit.

### Starting values

- Constant term, Null fits: mean of included molar heats \(q_i/m_i\) (the least-squares Offset).
- Constant term, binding fits: keep the existing `GuessOffset` (late-injection estimate); early injections contain binding heat. Late heats also approximate the Exponential plateau.
- User-entered constants are kept and used as starts.
- Slope and amplitude: 0. Decay scale: half the final included concentration.
- This flat start is the first of the deterministic starts. With \(A=0\), λ has no effect on the fit, so the λ search (below) is still needed as additional starts, particularly for Levenberg–Marquardt.

## 2. Shared Core implementation

**Model composition and interfaces**

Introduce a dilution-heat family type distinct from concentration `DilutionMethod`, with shared parameter descriptors, molar evaluation, injection-heat evaluation, and fit constraints. Add an explicit background-evaluation API to `Model`; keep the existing evaluation flag as a compatibility adapter meaning "include the entire background".

Integrate through `AnalysisBuilder`, `ModelFactory`, model options, session state, synthetic clones, and all five binding implementations. Register Linear and Exponential Null model identities with `IsNullModel = true`; keep Offset's identity and parameter key.

Use the existing Offset key for the constant term and append stable keys for slope, amplitude, and decay scale. Keep hidden fitted parameters separate from the editable setup parameters. Preserve user-entered constants and per-family setup state across ordinary rebuilds.

`BufferSubtraction` offers numerical patterns, but its injection-number fits, control-data handling, uncertainty approximation, and failure fallback are not reused as the scientific model.

`TandemMixingScan` stays Offset-only.

**Fitting and constraints**

- Use stable, scaled optimizer coordinates. For free backgrounds, optimize bounded values at the concentration-range endpoints; for Exponential, also optimize log decay scale. Convert to the physical parameters for evaluation and reporting.
- Enforce the fixed constant through the coordinate mapping.
- Initialize Linear by constrained weighted/unweighted least squares. Initialize Exponential with a logarithmic decay-scale search over the bounds above, solving the conditional constrained coefficients at each λ, then local refinement. Seed binding fits from compatible saved values, the flat start, and residual-based background estimates.
- Use deterministic multiple starts for new-family fits and keep the best admissible objective. Reuse the constraint mapping in both optimizers, bootstrap, leave-one-out, and profile likelihood.
- Require finite physical parameters as well as finite predictions. Handle exponential underflow, large-scale cancellation, and conversion overflow explicitly.
- Report boundary contact, numerical rank loss, and unresolved profile intervals as fit-health limitations. Do not silently reduce the parameter count, change family, or treat poor identification as absence of binding.

**Comparison and uncertainty**

Replace Offset-specific construction in `NullModelComparisonCalculator` with family-aware construction. Fit each hypothesis separately using identical observations, weights, background constraints, and sharing.

Count every free parameter, including automatically fitted background coefficients; count shared coordinates once and fixed coordinates zero times. Keep the estimated residual-variance parameter and likelihood conventions:

\[
\Delta\mathrm{AICc}=\mathrm{AICc}(\text{background})-\mathrm{AICc}(\text{binding + background}).
\]

Preserve independent member assessments, pooled diagnostics, and mixed outcomes.

Joint uncertainty includes all fitted background parameters and their covariance with binding parameters. Report the original-data optimum, never a bootstrap average. Keep the no-binding uncertainty skip (ITC-052). Automatic comparison-only Null fits continue to report uncertainty as **not estimated**.

Generalize corrected observations, predictions, exports, and confidence bands to injection-specific background subtraction. Keep the corrected-band convention: subtract the primary fitted background from total bootstrap predictions, retaining joint covariance.

## 3. Persistence and saved evidence

Follow `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md`.

- Preserve historical Offset model schemas and schema-1 Null comparisons. Missing background selection means Offset only for historical records.
- Add a composition-aware binding model schema for non-Offset backgrounds, with explicit family, endpoint convention, and heat-bookkeeping identity. Older readers must reject unsupported fits rather than read them as Offset (same mechanism as the Dumas/Discrete heat-method schemas).
- Add schema-2 Null comparison evidence with actual model identity, complete parameters, constraints, effective bounds (heat limits and decay-scale bounds), diagnostics, and captured fit inputs. Keep package-level versioning unchanged where independently versioned records suffice.
- Capture real injection amounts, volumes, post-injection concentrations, inclusion flags, observed heats, predictions, and required experiment context. Restore from captured evidence without invented concentrations or current preferences (ITC-075 N12).
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

Update `Documentation/ANALYSIS_REPORT_MAP.md`, particularly A01/A03, A06, A10/A11, E04b/E05, and figure/injection-table descriptions.

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
| **Fitting robustness** | Weighted/unweighted fits, both optimizers, multiple starts, fixed constants, local/shared backgrounds, all limit settings, flat data, short series, decay scale at both bounds, cancellation, and invalid inputs. Under identical constraints, the best Linear/Exponential objective is never worse than the Offset objective. Compare background-only solutions against independent constrained least-squares/profile calculations. |
| **Model discrimination** | Independently calculated AICc and parameter counts, including fixed constants and sharing. Insufficient sample size, failed hypotheses, and mixed independent outcomes. Do not require richer backgrounds always to reduce binding evidence. |
| **Parameter uncertainty** | Synthetic recovery and repeated-data coverage checks, labeled separately. Joint resampling, parameter/profile mappings, boundary and open-interval reporting, original-fit central estimates, and uncertainty round trips. |
| **Compatibility and presentation** | Historical files keep saved predictions and assessments; new files survive strict reopening, Save Selected, import/copy, and recovery. Both desktops and the viewer show matching families, values, corrections, and unavailable states. |

Re-run the supplied experimental cases under the endpoint convention and active limit settings. Record differences from the exploratory midpoint/unrestricted fits, including P13 parameter sensitivity, ambiguous Y242A curves, persistent cobalt evidence, and the CrsL counterexample. These are sensitivity and regression cases, not a calibrated classification benchmark.

**External validation remains an evidence gap.** Core consistency checks and independently calculated equation tests must not be called external forward-model validation. That claim requires an external generator covering the exact model and finite-injection protocol, evaluated on integrated heats at floating-point precision.

## 7. Out of scope

- Experiment Designer.
- AI interpretation evidence (ITC-087). Until resolved, new background parameters reach the AI package with fallback unit `"1"` for slope and decay scale.
- Sharing condition checks (ITC-086).
- Standalone Null-selection workflows, matched controls, automatic family selection, threshold calibration.

On completion, update ITC-075 (deferred list) and ITC-018 (partial resolution) in `Documentation/ISSUES.md`.

## 8. Open items

- Bootstrap/leave-one-out runtime: proposed that refits start from the original-data optimum plus the flat start, with the full λ search only for original-data fits. Needs a stated start budget and cancellation checks.
- Profile likelihood: decide which background quantities get profiles. λ is an optimizer coordinate; intercept/plateau, slope, and amplitude are derived from endpoint coordinates and need constrained refits.
- Report map: limit wording reconciliation to entries touched by this change, or also fix the existing inconsistent unavailable-figure labels?
