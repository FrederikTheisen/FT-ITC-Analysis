# Primary fit recovery during profile likelihood

Status: Feature plan registered on 16 October 2026. Implementation has not been requested. Tracker: [ITC-114](ISSUES.md#itc-114---recover-the-primary-fit-when-profiling-finds-a-better-candidate).

When profile likelihood discovers a parameter combination that fits the original data better than the primary fit, automatically restart the primary fit from that combination and calculate the profiles again. Allow up to two recovery restarts for each fit. If a verified improved primary fit is obtained but profiling subsequently fails, keep that improved fit and report its profile uncertainty as unavailable.

This feature follows the discussion of [CR-010](CODE_REVIEW_2026-10.md#cr-010--failed-profile-diagnostics-can-still-supply-numerical-uncertainty). CR-010 concerns numerical consumers accepting intervals from a failed run; its investigation and fix remain separate. Registering or implementing recovery does not resolve that finding.

## Decisions and scope

The user selected these behaviors on 16 October 2026:

- Recovery is automatic, with a clear status message.
- Each fit being profiled may restart its primary optimization at most twice.
- A verified improved primary fit is retained if subsequent profiling fails, with profile uncertainty unavailable.

The implementation scope is shared Core behavior for a single experiment, each independently fitted member, and a global fit with shared or linked coordinates. Run Fit and Update Result use the same recovery policy. Both desktop apps present the same progress and outcome. An independent member has its own two-restart allowance; one shared global fit has one allowance for the complete group.

The following are planning assumptions, rather than additional user decisions:

- No preference, new dialog, or manual recovery command is added. The two-restart limit is a shared Core policy.
- Recovery keeps the original optimizer, primary-fit iteration limit and tolerance, model, observations, weighting, bounds, locks, concentrations, and constraints. The temporary profile lock is removed; user locks remain.
- Cancellation preserves existing retention behavior, including the existing saved Analysis Result during Update Result. Keeping an improved fit after a non-cancelled profile failure does not authorize publication after cancellation.
- An ordinary final partial profile may retain its usable intervals under the existing policy. A pass that discovers a better primary candidate is invalidated as a whole. A final complete failure has no usable numerical profile uncertainty.
- A change in the effective binding assessment still follows the existing uncertainty-skip setting and manual overrides. Recovery does not force profiling of a fit that should now be skipped.

Confirm these assumptions when implementation is commissioned if the requested scope has changed.

## Scientific behavior

A profile fixes one fitted parameter at a trial value and refits the other free parameters. A trial combination can improve on the primary fit if the original optimization stopped short or reached a poorer local minimum. Such an improvement undermines the reference used for that pass's confidence intervals. A recovered fit can improve the estimate, but it does not establish a global optimum or guarantee identifiable parameters.

The reported values remain a fit to the original integrated heats. There is no resampling, averaging of profile candidates, concentration perturbation, or change to the forward model. The fitted objective and the displayed unweighted RMSD remain distinct.

Keep the existing improvement test. In the current estimator, `S` is the residual sum of squares, standardized by the observation SDs when weighting is enabled; `n` is the included observation count. An improvement is detected when `n × (ln(S_candidate) − ln(S_primary)) < −1e-4`. This is a chosen numerical tolerance in the profile objective difference, not a percentage improvement or a tolerance on a physical parameter. Recompute both scores with the same evaluator, observations, units, and weighting; do not compare a profile score with a display RMSD or an objective in a different scale. Preserve the existing handling of a zero-score candidate and a non-positive profile baseline rather than inventing a positive floor.

## Current code and integration points

| Area | Relevant behavior and required connection |
| --- | --- |
| [ProfileLikelihoodEstimator.cs](../AnalysisITC.Core/Analysis2/ProfileLikelihoodEstimator.cs) | Expansion and refinement can return `PrimaryMinimumImproved`. Candidate evaluation captures the complete local or global parameter values internally, but the improvement return does not carry them back. Coordinate searches run in parallel and retain completed diagnostic results. |
| [ProfileLikelihoodResult.cs](../AnalysisITC.Core/Analysis2/ProfileLikelihoodResult.cs) | Defines profile outcomes, coordinates, and progress. Keep the recovery candidate and retry control transient rather than adding raw candidate vectors to saved profile evidence. |
| [Solver2.cs](../AnalysisITC.Core/Analysis2/Solver2.cs) | `Solve` creates primary convergence before uncertainty estimation. Local and global `ProfileLikelihood` install run metadata; independent collections can defer uncertainty until after assessment. Recovery must return the final primary solution and convergence to every outer caller. |
| [AnalysisResultUpdater.cs](../AnalysisITC.Core/Analysis2/AnalysisResultUpdater.cs) | Profile updates are staged and ordinarily reject complete profile failure. The selected recovery behavior requires a narrow exception for a verified improved primary fit, while preserving cancellation and document-revision checks. |
| [FittingStatusPresentation.cs](../AnalysisITC.Core/Presentation/FittingStatusPresentation.cs) and [UncertaintyEstimationPresentation.cs](../AnalysisITC.Core/Presentation/UncertaintyEstimationPresentation.cs) | Existing shared operation, phase, counters, completion, and retention messages should carry recovery progress and outcomes to both desktops. |
| [SolverUtilities.cs](../AnalysisITC.Core/Analysis2/SolverUtilities.cs) | The returned convergence, objective, termination, diagnostics, and timings must describe the accepted fit and complete analysis. Existing convergence summaries can record recovery without a schema change. |

## Recovery sequence

1. Start with the successful primary fit and run the existing profile estimator.
2. On a genuine improvement, capture an immutable, coherent snapshot of the entire candidate parameter combination, its score, and the triggering coordinate. Preserve shared parameters and member-local values together. Do not construct a starting point by merging values from different candidates.
3. End the invalidated profile pass and join its workers before starting recovery. No worker may mutate the accepted source fit or publish late progress into the next pass. Select the recovery candidate by stable coordinate order, not worker arrival time: earlier coordinates must be resolved before a later trigger wins. The first improving coordinate in that order supplies the candidate. This deliberately favors reproducibility over choosing whichever improvement finishes first.
4. If the restart allowance is exhausted, stop recovery. Otherwise consume one restart, construct a detached primary model from the same fit inputs, apply the candidate values, and restore the original free/fixed parameter configuration.
5. Run the original primary optimizer with uncertainty estimation disabled inside that solve. Use primary-fit settings, not the looser nuisance-refit budget. This solve must not recursively start another profile run, create a nested Analysis Result, or emit an independent user-facing completion.
6. Accept only a normally converged, finite, feasible primary fit whose independently reevaluated score improves on the currently accepted primary by the existing improvement criterion. Reject a failed, limit-terminated, invalid, or non-improving recovery fit. Do not substitute the raw constrained trial candidate when the ordinary primary solve fails.
7. Rebuild the primary solution and its fit-dependent state, recalculate the binding comparison, preserve manual assessment overrides, and reevaluate uncertainty eligibility. If profiling remains eligible, start every coordinate again against the recovered baseline, with fresh counters and no intervals inherited from the invalidated pass.
8. Repeat only if this new pass finds another improvement. There are at most two recovery primary solves and three profile passes per owning fit. A failed recovery attempt consumes an attempt and ends recovery; it does not cause an unbounded retry loop.
9. Publish the final accepted primary fit together with the final eligible profile evidence and outcome, using the existing operation's finalization path.

Stable coordinate selection requires changing the current early-stop coordination: the shared improvement flag must not skip unresolved earlier coordinates. Preserve bounded searches and parallel execution where compatible, but do not let a thread-scheduling race choose the next primary starting point. Cover both expansion and refinement discoveries.

## Outcomes and result replacement

| Outcome | Required result |
| --- | --- |
| No improvement discovered | Existing fit and profile behavior. No recovery-only change to ordinary profile replacement. |
| Recovery succeeds and the final profile completes | Report the recovered primary estimates with intervals from that final pass. |
| Recovery succeeds and the final profile is ordinarily partial | Retain only that pass's usable intervals with its partial outcome. Do not merge intervals across baselines. |
| At least one recovery succeeds, then a later recovery fails, the allowance is exhausted, or profiling completely fails | Keep the best accepted recovered primary fit. Profile uncertainty is unavailable; explain the reason. A still-better constrained candidate is diagnostic only. |
| The first recovery fails before any improved primary is accepted | Do not promote the trial candidate. Retain the established primary and existing failure/Update Result retention behavior. |
| Reassessment makes uncertainty intentionally skipped | Keep the recovered fit and existing `SkippedNoBinding` explanation. |
| The user cancels or the document changes before publication | Follow existing cancellation and document-identity/revision retention rules. Do not commit an intermediate recovery result. |

For the new recovered-fit failure path, the published solution must have no numerical intervals from invalidated or completely failed passes, including values already copied into parameter uncertainties. Preserve useful attempt diagnostics as text or transient records; do not attach abandoned interval collections as usable final profile evidence. Unavailable uncertainty must not appear as zero uncertainty or fall back to uncertainty from a previous primary fit.

This requirement belongs to construction of the new recovery result. The general CR-010 consumer-eligibility fix, including unrelated failed or restored profile runs, remains outside this feature. Review the two changes together before implementation is accepted; recovery must never be treated as a guarantee that the CR-010 path cannot occur.

During Update Result, carry explicit transient evidence that recovery accepted a better primary fit. Only this condition permits a non-cancelled recovered-fit replacement despite subsequent complete profile failure. Do not broadly change the existing rule for every failed profile update, infer recovery from a message string, or compare an updated-data objective with the old saved result's objective. Improvement is established within the current run against its own primary fit on the same data.

## Consistent fit state and assessments

Replacing model parameters alone is insufficient. The final model, reported estimates, primary convergence, residual statistics, parameter-boundary contacts, derived thermodynamics, and comparison evidence must all belong to the accepted recovered fit. Carry the new convergence back through `Solve`, member convergence lists, global aggregation, and Update Result; do not return the original fit's convergence beside the recovered solution.

With uncertainty skipping enabled, a binding comparison may already exist before profiling begins. Recompute it after a primary recovery. The deferred independent workflow also captures the comparison before estimating member uncertainties and assigns it back later; rebuild that evidence after changed members rather than reattaching the captured comparison. Preserve member identity and manual overrides through these rebuilds. Do not reuse stale no-binding decisions to suppress a recovered member's output.

An independent member restarts only its own fit and profiles. Keep unaffected members' accepted estimates and intervals, then rebuild result-level statistics and comparisons from the complete member set. A shared global recovery refits the entire original group, preserving temperature relationships, reference temperature, parameter mappings, and local/shared locks. Never recombine a subset because one member has unavailable uncertainty.

Keep the work inside the current processing admission and detached solving lifecycle. Use the ordinary publication and invalidation paths once at completion. Cancellation, stale documents, preparation failures, and rejected updates must preserve live attachments, saved evidence, dirty state, and previous results as required by ITC-090/ITC-099.

## Desktop feedback and saved evidence

Define shared text in Core. During a recovery primary solve, show a message such as “Refitting after a better fit was found” with “Restart 1 of 2”. When profiling starts again, say that uncertainty estimation has restarted and reset the counter for that pass. Independent fits retain their member position/name; shared global fits describe the whole group. Stop remains available through every stage.

Use the existing phase and operation model, extending its transient recovery context only as needed. Reset progress explicitly between passes and ignore queued events from an abandoned pass even though the outer operation ID is unchanged. Do not redesign progress indicators or expand ITC-109. Whole-analysis elapsed time includes all attempts; distinguish primary and profile work without double-counting it. Attempted solver-call diagnostics may accumulate, but final endpoint counts describe the final pass only.

Completion distinguishes “Fit improved; profile uncertainty estimated” from “Fit improved; profile uncertainty unavailable”. Update Result must describe an accepted improved fit as an update rather than saying the previous result was kept. Cancellation and genuine retention keep their existing messages. Persist a short recovery summary through existing convergence diagnostics so the retained improvement and unavailable uncertainty remain understandable after reopening; transient progress text is not saved evidence.

No new project schema, setting, enum wire token, or stored candidate history is planned. Follow [the FTXTC specification](../Documentation/Schemas/FTXTC/FTXTC_FORMAT.md), preserve existing profile metadata/outcome conventions, and verify round trips of recovered fits and unavailable uncertainty. If existing fields cannot express the agreed outcome accurately, bring back a specific persistence proposal before expanding this scope. The Web viewer should render the saved final estimates and outcome through existing fields; it does not acquire fitting controls.

## Implementation stages

1. **Candidate handoff and reproducible discovery.** Add the transient complete-candidate return path, stable selection across parallel coordinates, and tests for expansion/refinement and source immutability. Keep current interval mathematics and thresholds unchanged.
2. **Primary recovery coordinator.** Implement the two-restart limit, detached full primary solves, acceptance checks, restored locks/constraints, final-pass isolation, reassessment, and explicit final solution/convergence return. Cover local, independent, and shared global fitting.
3. **Publication and Update Result.** Apply the narrow verified-recovery replacement rule, rebuild derived/comparison evidence, clear invalidated uncertainty, and preserve cancellation/document checks. Reconcile the result boundary with the separately handled CR-010 fix.
4. **Presentation and persistence verification.** Wire shared progress/completion to both desktops, retain concise saved diagnostics, and verify final evidence in inspectors, exports, reports, the Web viewer, and save/reopen paths.
5. **Documentation and review.** Update the user guidance described below, run the required suites and desktop checks, and record actual validation before marking ITC-114 complete.

## Validation and acceptance

Tests must establish numerical expectations independently of the implementation. Use analytic objectives with known optima and independently computed score differences for controlled recovery cases; constructing the expected values through the new recovery coordinator is insufficient. These are optimization and lifecycle tests, not external forward-model validation.

- Reproduce one coordinate completing an interval before another discovers an improvement. Prove every old-baseline interval is excluded after recovery and the final intervals use the final baseline. Test discoveries during expansion and refinement.
- Verify the complete candidate vector is used; the profile's temporary lock is released; original fixed values, bounds, constraints, input observations, concentrations, temperatures, and inclusion flags remain unchanged. Cover both supported optimizers and weighted/unweighted fitting.
- Cover no recovery, one successful restart, two successful restarts, a third improvement, failed/non-improving/limit-terminated restarts, non-finite candidates, improvement below tolerance, and an exact zero-score candidate. A zero recovered score must retain the fit without inventing a valid positive profile baseline.
- Force different worker completion orders, including a later coordinate discovering first. Verify the same selected candidate, no mixed parameter snapshots, no duplicate recovery solves, no source mutation, and no late progress overwriting the next pass.
- Verify independent members have separate allowances and unaffected evidence is retained; shared fits have one group allowance. Include shared affinity and thermodynamically linked/temperature-dependent mappings and preserved reference temperature.
- Check returned primary convergence, saved model values, reported estimates, RMSD, objective, derived values, boundary warnings, automatic comparisons, and manual overrides all describe the recovered fit. Exercise comparison-before-profiling and deferred independent assessment paths.
- Verify the new improved-fit/failed-profile outcome through fresh fitting and Update Result, including clearing prior/bootstrap/abandoned uncertainty and showing unavailable uncertainty in numerical consumers. Verify ordinary complete/partial/failed profile replacement remains unchanged when recovery has not accepted an improved primary.
- Cancel during discovery, a recovery solve, reassessment, and restarted profiling. Change the document before publication. Verify existing result/attachments/evidence are retained, no success message is emitted, and processing admission is released correctly.
- Round-trip recovered fits with complete, partial, unavailable, and skipped profile uncertainty. Confirm saved diagnostics remain accurate and older files still open without synthesizing a recovery history.
- Check both desktops' restart messages, member context, counter resets, Stop, completion, retention, and final parameter displays. Verify saved values/outcomes in existing export/report/viewer paths without changing their layout.

Run `AnalysisITC.Core.Tests` for shared changes, the relevant Avalonia fitting/results/update tests, and native managed/UI checks including `UpdateResultStatusTests` and uncertainty presentation. Use focused recovery tests while developing, then run the complete Core suite. No packaging, signing, release scripts, version changes, commits, or pushes are part of this plan.

## User documentation

When implemented, add a short explanation to [fitting and models](../Documentation/UserManual/pages/06-fitting-models.md) and [shared in-app help](../Resources/HelpTextResource.txt): profiling can refine the primary fit, the reported values are the final fit to the original data, and intervals are calculated against that fit. Explain the unavailable-uncertainty outcome where needed to interpret a result. Omit worker scheduling, candidate storage, and incidental status behavior from the manual.

Review [the analysis report map](../Documentation/ANALYSIS_REPORT_MAP.md), especially A10/E08 uncertainty and affected fit/comparison diagnostics, when implementing the outcome presentation. Update the map if the displayed content changes; no new report layout is proposed here.

## Deferred work

- CR-010's general failed-profile numerical eligibility problem and its reproduction.
- General multistart/global optimization, automatic optimizer switching, and recovery for bootstrap or leave-one-out refits.
- Changes to equations, likelihood calibration, confidence level, profile stepping, parameter bounds, observation inclusion, or concentration uncertainty.
- New user settings, a separate recovery tool, new project schema fields, and a stored archive of abandoned profiles/candidate vectors.

The three user-selected behaviors above are settled for this plan. Other assumptions are explicit so implementation can be reviewed without silently widening the feature.
