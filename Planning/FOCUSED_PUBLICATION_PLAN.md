# Focused publication correctness plan

**Status:** Planning only, 21 October 2026. No implementation, tests, builds, deployment, packaging, commits or pushes are authorized by this document. The user requested this plan and explicitly requires testing on every affected platform when implementation is later authorized.

**Scope:** Seven issues only: ITC-166, ITC-169, ITC-179, ITC-180, ITC-187, ITC-188 and ITC-194. ITC-182 receives a bounded investigation, not an automatic implementation slot. Keep the individual records in [ISSUES.md](ISSUES.md); this plan does not change their statuses.

**Execution guidance:** If delegated execution is useful after authorization, use scoped Luna agents with one owner per shared file. The numerical and presentation packages both touch shared Core code; do not run conflicting edits in parallel.

## Baseline and working assumptions

- The planning snapshot is HEAD `3afb0178` plus the current dirty working tree. Source, tests, documentation and the tracker are changing through parallel work. Refresh the eight issue records and relevant diffs before execution; preserve unrelated edits and avoid implementing an already completed correction twice.
- ITC-166 is already marked resolved locally, with malformed concentration checks present in the reader and test source. Its remaining task here is verification of that work. ITC-169 has pending processing-invalidation changes and an untracked focused test file; inspect and validate that candidate before proposing another implementation. Neither observation is a new test pass or implementation by this planning task.
- Both native macOS and Avalonia remain supported. Shared changes require tests on macOS, Windows and Linux, plus the affected report/export/viewer consumers. Testing one desktop app or cross-compiling is insufficient evidence for the others. Missing platform access remains an explicit validation gap.
- Preserve original-data best-fit centers, complete asymmetric intervals, and zero error as no error. Keep the weighted objective separate from unweighted RMSD. Preserve Null/Offset terminology, dilution bookkeeping, model equations and existing output eligibility.
- Prefer existing shared Core policies and formatting. Preserve `.ftxtc` compatibility and legacy `.ftitc` reading; add no format version, migration or random-seed persistence as an incidental correction. Read the format specification before any serialization edit becomes necessary.
- ITC-194 requires agreement on the public sign/name convention before its implementation. That decision does not block the other packages. This plan does not approve a convention by silence.

## Work packages and order

| Order | Package | Scope | Completion requirement |
| --- | --- | --- | --- |
| 1 | Buffer-reference processing invalidation | ITC-169 only | Corrected-heat changes reach the target processing lifecycle exactly once; prediction caches and attached/saved solutions are invalidated; matching saved fits remain valid after save/reopen. |
| 2 | Preserve uncertainty on recomputation | ITC-179, ITC-180 | Intended percentile endpoints are stable; unchanged saved inputs reproduce their summary uncertainty without changing scientific meaning. |
| 3 | Report the same scientific quantity everywhere | ITC-188, ITC-194 | Fixed affinities match their column units; protonation signs and enthalpy names follow the agreed convention in every affected consumer. |
| One fixed investigation | Solver fallback check | ITC-182 | Record a demonstrated consequence, no demonstrated consequence within the bound, or an inconclusive infrastructure failure. No automatic solver change. |

For each issue, first inspect the latest candidate and establish the observable failure with independent expectations. Reuse existing regression coverage where it already demonstrates the defect. A resolved candidate needs verification, not a recreated failure or a second fix.

### 1. Buffer-reference processing invalidation

**Scope decision (maintainer, 2026-10-21):** ITC-169 only. ITC-166 is resolved and committed in `3afb0178`; do not re-verify it. ITC-187 is closed as “Reviewed, no change”; leave `SameDouble` and every validity tolerance unchanged.

**ITC-169 — buffer-reference processing invalidation**

- Target: `ExperimentData` subtraction apply/change/clear/reference-refresh paths and their existing processing lifecycle. Review the existing uncommitted `ChangeBufferSubtraction` / `RestoreBufferSubtraction` candidate and `BufferSubtractionProcessingInvalidationTests`; fix only gaps demonstrated by a failing test with independent expectations.
- Acceptance: a real net change to corrected heat, SD or subtraction settings updates the target revision, invalidates its prediction cache and affected attached/saved solutions, and sends the existing target processing notification without duplicate publication. Raw heats are preserved. An unchanged refresh does not generate a false processing change. Reopening an unchanged saved correction preserves the validity of its matching saved fit.
- Evidence: use a fitted target and independently specified target/reference heats. Exercise apply, change reference/method, reference heat/include change and clear; verify corrected heats, revision, prediction cache, attached solutions, notifications exactly once, and saved-result validity after save/reopen. Keep the existing `below-snapshot-tolerance` case and its snapshot-valid assertion unchanged: exact in-session invalidation and tolerant on-demand saved-result comparison intentionally differ.
- Verification scope for this package: run `AnalysisITC.Core.Tests`; build and check native macOS and Avalonia on macOS where runnable. Do not attempt Windows, Linux or the Web viewer. Record untested coverage in ITC-169. Use existing tests/harnesses only; no commits or pushes.

### 2. Preserve uncertainty on recomputation

**ITC-179 — percentile index rounding**

- Target: `FloatWithError.GetConfidenceInterval` and its existing numeric-distribution entry points. Preserve the intended discrete percentile convention; do not introduce interpolation, another confidence level or a new statistical method.
- Acceptance: the lower index no longer advances because 2.5% is represented slightly high. For ordered values `0..199`, expected limits are 4 and 195; for `0..999`, 24 and 975. These are order-statistic expectations, not copied floating expressions. The supplied original-data center is unchanged.
- Evidence: 100, 200, 1000, 2000, 5000 and 10000 samples, neighboring counts around one affected size, and existing empty/single-sample cases. Cover constructor and in-place distribution paths; retain complete saved bounds through display transformations.

**ITC-180 — deterministic saved-summary uncertainty**

- Target: the no-temperature-dependence summary in `GlobalModel`, the list-of-`FloatWithError` propagation it uses, and `LinearFitWithError.GetXAxisIntersect` used by Spolar–Record. Do not replace bootstrap sampling, concentration perturbation or all application random streams.
- Approach: reuse the complete saved quantity for a single member when no aggregation is required. For remaining derived summaries, use a stable operation-local propagation stream or an existing deterministic calculation that preserves the current quantity. If Monte Carlo is retained, keep its distribution, draw count, weights and denominator convention; never depend on wall time, a process-randomized hash or mutable global RNG state. Do not replace combined spread with SEM or add a summary-family feature.
- Acceptance: repeated evaluation and save/reopen of identical member inputs reproduce center, SD and both bounds. The reported center remains the current original-data estimate/defined summary of those estimates. Individual fitted values, saved uncertainty, zero-error inputs and ordinary temperature-dependent output retain their established meanings.
- Evidence: single member with an asymmetric interval, multiple same-temperature members with unequal uncertainties, shared-parameter controls, and repeated nondegenerate x-axis intersection. Compare two evaluations and two independent reloads on each affected runtime, then compare the same saved fixtures across platforms using stated numerical tolerances. Retain existing independent numerical checks for center and spread; repeatability alone is not scientific correctness. Do not expand into a near-zero-slope policy redesign.

### 3. Report the same scientific quantity everywhere

**ITC-188 — fixed affinity overview units**

- Target: A06 fixed-value formatting in `AnalysisReportBuilder` and the overview's existing resolved affinity-unit policy. Format the fixed cell in its column unit using the shared formatter; retain `(fixed)`, no uncertainty for fixed parameters, current column layout and assessment suppression.
- Acceptance/evidence: a fixed 50 nM value under a µM header displays `0.05 (fixed)`, not `50 (fixed)`. Check the reverse magnitude combination, free/fixed rows and selected unit overrides. Verify numeric content and visible preview/PDF in both existing renderers; compare equivalent table/clipboard output where it shares the unit policy.
- Consult [ANALYSIS_REPORT_MAP.md](../Documentation/ANALYSIS_REPORT_MAP.md), A06. Clarify that unit contract if needed; do not repair unrelated map discrepancies or report formatting in this package.

**ITC-194 — protonation sign and buffer enthalpy naming**

- Decision before implementation: define the public signed proton quantity, what a positive value means, and whether the buffer x-coordinate is named ionization/deprotonation enthalpy or protonation enthalpy. Confirm the meaning of the actual buffer data from its source. Lay out the `+slope` and `−slope` alternatives using that same physical x-coordinate; do not change tabulated values just to make labels agree.
- Prefer a solution that preserves the existing canonical regression/storage and applies the agreed public convention through shared Core presentation. A decision requiring stored-value reinterpretation or migration must return for explicit scope agreement rather than silently enlarging this plan.
- Acceptance: native macOS, Avalonia, report tables/plots, viewer, interpretation evidence, exports and save/reopen describe the same signed quantity. Negating an uncertain value must also transform/reorder its complete bounds. The fitted line, intrinsic binding enthalpy, original-data centers and buffer thermochemistry remain correct under the chosen definition.
- Evidence: an independently specified two-point example: buffer x values 10 and 30 kJ/mol and observed y values −40 and −30 kJ/mol imply intercept −45 kJ/mol and raw slope +0.5. Both public sign alternatives must explain this same line; add a negative-slope counterpart and an asymmetric interval. Check each consumer against the agreed expectation, not against another consumer's output.
- Update only the user-facing sign/meaning explanations that help interpretation in the manual, shared help and report map A06/X06. Preserve the existing format and serialized keys if their meaning is unchanged; a label change is not permission for a schema migration. Counter-ion analysis, protonation eligibility and new buffer methods remain excluded.

## ITC-182: one bounded solver check

**Limit:** one investigation session, at most 60 minutes of investigator time including setup/analysis, using the fixed matrix below. Run the same resulting check on the affected platforms during validation. No dataset search, repeated random-seed search, optimizer tuning or broad solver benchmark project.

1. Read the local/global `ApplyBestFittedParameters` paths and distinguish the usual comparison from the separate inward-start path. A predicate-only demonstration of the 1e-12 floor is supporting evidence, not proof of a consequential production failure.
2. Select one existing ordinary integrated-heat One-Set-Of-Sites fixture with known physical inputs, then make a two-member pooled version from the same fixture. Fix the fixture, starting parameters and a supported low iteration budget before running; do not change them to hunt a failure.
3. Run local/pooled × Nelder–Mead/Levenberg–Marquardt × unweighted/weighted control: eight configurations per platform. Capture actual initial/candidate/accepted parameters and independently calculate the unweighted sum of squared heat residuals in J² from the integrated heats and predictions. Use the selected weighted objective for its control; RMSD is reported separately.
4. **Promote for a decision only** if the real acceptance path returns a materially worse finite candidate instead of the available start, the difference exceeds numerical roundoff, and the absolute floor explains the choice. Report the numeric before/after evidence and smallest proposed correction. Implementation still needs separate authorization.
5. If the fixed matrix does not demonstrate that consequence within the bound, record “not demonstrated in the bounded check” and defer ITC-182 from this publication pass. If infrastructure prevents the check, record “inconclusive” and the exact missing validation; do not call it a pass or infer a solver defect. Stop the investigation in either case.

## Validation when implementation is authorized

Use one small reusable set of fixtures: valid/malformed integrated input, a fitted target with a buffer reference, a mixed fixed/free affinity overview, an uncertain same-temperature summary and the signed protonation example. Add only the focused order-statistic cases and solver matrix described above.

| Platform/consumer | Required evidence |
| --- | --- |
| Avalonia on macOS, Windows and Linux | Relevant automated import, result/summary, buffer and report checks on each OS; one ordinary workflow pass on each: import → process/integrate → fit → save/reopen → report/export. Use supported shipping runtimes/architectures and record the actual environment; cross-compilation does not count as execution. |
| Native macOS | Native build and affected managed/control/render harnesses; the same ordinary workflow, with buffer invalidation, fixed-unit A06 and signed protonation outputs checked in the native renderer. Record the tested architecture and any missing shipping-architecture coverage. |
| Shared Core | Run the focused regressions, then `AnalysisITC.Core.Tests` once on each affected OS/runtime. Cover the native app's consumer runtime through its native harnesses as well. Scientific expectations must be independent of the changed implementation. |
| Web viewer and interpretation evidence | Relevant `AnalysisITC.Web.Tests`, existing JavaScript checks and a browser inspection of affected summary/protonation output on supported client platforms. Validate the existing server runtime (Linux if that remains the published host). Use the same signed/saved fixtures; no deployment, mail, quota or registration testing is added. |

Run the existing relevant scientific validation once against the final candidate; do not create new model coverage or reopen method studies. External forward validation must retain its independently generated integrated-heat inputs and floating-point-precision criterion. Keep optimizer recovery checks separate.

Record per issue: the reproduced failure or inspected existing fix, independent expected result, final result, test command/environment, and any platform gap. Final checks are run once per required environment; rerun only after a relevant change, failure or unresolved concern. Existing unrelated failures are recorded separately and are not authorization to fix another issue. A required missing platform result remains incomplete validation.

## Scope fence and stopping rule

- No other issue joins this work. Exclude general import cleanup, broad numerical/processing refactors, summary-SEM and derived-method expansion, new scientific methods, web hardening/scaling, UI polish, manuscript edits, release metadata and distribution work. Avoid neighboring fixes even when a touched file contains them.
- Do not merge unrelated concurrent changes into this package. If a scoped correction needs a larger prerequisite, describe the precise dependency and return for a scope decision; do not absorb a new project automatically.
- Finish this publication correctness pass when the seven acceptance criteria and affected-platform checks are satisfied, ITC-194's convention is agreed and aligned, and ITC-182 has its bounded disposition. A demonstrated ITC-182 failure needs its separate decision; an inconclusive check remains disclosed missing evidence.
- Stop adding work after that pass. A newly discovered problem can be presented as a proposed exception only with evidence of wrong scientific output, data loss or failure of an advertised ordinary workflow. It requires an explicit scope decision; it does not enter automatically because an audit lists it.
- Completion means this bounded correctness plan is complete. It does not assert that signing, packaging, hosted deployment, manuscript submission or publication itself has been performed. The remainder of the tracker stays outside this plan.

## Planning record

Only this plan is authored by this task. Planning evidence is current source/diff/test-source inspection; no builds, tests or runtime probes were run. Pre-existing implementation and documentation changes belong to their existing work and are preserved. No issue status is changed by this plan.
