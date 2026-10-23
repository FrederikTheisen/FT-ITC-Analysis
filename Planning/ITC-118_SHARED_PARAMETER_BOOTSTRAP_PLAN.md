# ITC-118 — Shared-parameter bootstrap unlocking

Execution guidance: If gpt-6-astra coordinates implementation, generally use gpt-6-luna agents for scoped execution, following repository instructions. This document authorizes no implementation or agent execution.

Status: Focused implementation plan, 23 October 2026. The user approved the scientific policies below and requested this plan only. Tracker: [ITC-118](ISSUES.md#itc-118---bootstrap-unlocking-of-locked-shared-parameters-needs-a-solver-policy); origin: [CR-071](CODE_REVIEW_2026-10.md#cr-071--bootstrap-unlocking-is-bypassed-when-every-shared-coordinate-is-locked).

## Agreed behavior

- Unlocking removes parameter locks while preserving sharing and other model constraints. A shared N or Offset remains one coordinate within each replicate and can vary between replicates.
- Every joint bootstrap refit retains every experiment in the original analysis, including experiments assessed as no binding.
- Skip the whole joint bootstrap for no binding only when all effective member assessments are **No binding detected**. Honor manual overrides. Any Binding detected, Inconclusive or Not assessed member prevents that group skip.
- Retain existing joint replicate admission, partial-result and cancellation behavior. A failed member cannot be salvaged as an independent piece of a joint replicate.
- Report the original-data primary best-fit values. Bootstrap distributions supply uncertainty and do not replace those values with their means or medians.

Additional behaviors confirmed by the user on 23 October 2026: the existing skip setting remains authoritative (disabled means no no-binding skip), the optimizer budget stays unchanged, and already-global primary fits retain their existing assessment policy. These are agreed constraints, not outstanding assumptions.

## Boundary

Fix the transition from separate primary fits with locked shared coordinates to joint residual-bootstrap refits with those coordinates unlocked. Reuse the existing deferred-uncertainty branch and global bootstrap routine. Keep the primary routing predicates, fitting objective, residual generation, weighting, concentration/model-option sampling, bounds, tolerances, and original observations unchanged.

No routing framework, scheduler, strategy classes, new solver type, duplicate bootstrap loop, new preference, persisted routing flag, schema change, or always-global primary fitting. Leave leave-one-out, profile likelihood, ordinary independent bootstraps and already-global bootstrap dispatch unchanged. ITC-117 derived-parameter propagation is separate. No report layout, assessment threshold, packaging, release, version, commit or push work belongs to this plan.

## Implementation

### 1. Select the existing parent uncertainty pass

Work primarily in [Solver2.cs](../AnalysisITC.Core/Analysis2/Solver2.cs), inside `GlobalSolver.Analyze`'s existing individual-fit branch.

1. After the existing error-estimation policy is applied, identify the affected case: multiple experiments, residual bootstrap, effective parameter unlocking enabled, and a locked coordinate in the global parameter table. Inspect actual shared coordinates rather than constraint dictionary entries alone. Keep this as a small condition; do not change `RequiresGlobalFitting` or `ShouldFitIndividually`.
2. Extend the existing `deferEstimation` decision to include that case even when no-binding skipping is disabled. Each child runs the same primary solve with uncertainty deferred; it still records the method and requested bootstrap count.
3. Assemble the existing `GlobalSolution` from those primary solutions with `reconstructBootstrap: false`, calculate the existing member assessments, and retain the comparison. Use the aggregate primary `CanRunErrorEstimation` gate: a failed or cancelled primary member prevents the complete joint pass; never remove it or fall back to a subset. Preserve existing eligibility of limit-terminated primary fits.
4. When eligible, apply the all-members no-binding rule above, subject to the existing skip setting. Mark the root and members skipped together, or call the parent's existing `EstimateUncertainty` once. Do not enter the member uncertainty loop for this case.
5. Return that same assembled primary result. Do not call the parent `Solve` on the original data, reconstruct joint samples from independent member lists, or reaggregate member uncertainty counters afterward.

The existing `GlobalSolver.BoostrapResiduals`, `GlobalModel.GenerateSyntheticModel`/`CopyUncertaintyFitTopologyTo`, and `GlobalSolution.SetBootstrapSolutions` already perform joint cloning, refitting, indexed admission, and sample distribution. Reuse them without changing their scientific policy or creating another execution path inside the bootstrap loop.

### 2. Populate existing uncertainty records

After the joint pass, apply its uncertainty outcome to each original member convergence record using existing `ApplyErrorEstimationResult`, request-capture and skip operations. Carry requested, retained, failed and limit-terminated counts, elapsed uncertainty time, and cancellation state. Keep each member's primary termination, objective, RMSD, fit time and boundary evidence intact; do not replace its convergence with a copy of the root.

The root records B requested joint refits, not B times the number of experiments. Each member refers to the same B joint attempts. Retain the root's recorded elapsed uncertainty time rather than summing that shared time across members.

Preserve existing cancellation checks and replicate admission in the reused routine. Update Result must continue to retain the existing saved result after cancellation or unusable uncertainty. Its existing validator should accept a usable partial joint replacement once member records are populated; do not weaken validation or change replacement policy to accommodate missing metadata.

### 3. Adjust only consumers that need uncertainty scope

- **Live progress (`Solver2.cs`):** use joint scope during this bootstrap phase so it does not display the last primary member as the active experiment. Keep primary fitting and assessment scope unchanged and reuse the existing operation, phase and progress delivery.
- **Completion counts (`UncertaintyEstimationPresentation.cs`):** count complete joint refits once. For 19 retained of 20 requested joint refits, show 19 of 20, not 38 of 40 for two members. Preserve the independent-member count path for genuinely independent ensembles.
- **Correlations (`BootstrapCorrelationAnalyzer.cs`):** for retained joint ensembles, use the existing global selection path: shared coordinates plus the selected member's local coordinates. Avoid the independent-primary shortcut for those ensembles. Retain current availability, minimum-refit and numerical correlation policies.

Distinguish saved joint samples by their existing retained global-coordinate evidence, not merely the historical Unlock parameters flag. Old independent common-index reconstructions lack that global table and must not be relabelled as successful joint unlocking. If completion and correlation need the same evidence check, share one small computed query on `GlobalSolution`; no new stored state or general topology service. Empty ensembles continue to report their existing skipped, unavailable or cancelled outcomes.

Both desktops consume these shared paths. No desktop-specific routing is expected. Keep `AnalysisResultUpdater`, clone helpers and convergence classes unchanged unless a focused regression establishes a missing integration requirement.

## Validation

Extend existing Core workflow coverage, with small fixtures and expectations independent of the dispatch implementation. The temporary investigation caller is supporting evidence, not a new test framework.

| Coverage | Required observation |
| --- | --- |
| Actual `GlobalSolver.Analyze`, shared locked N and Offset, weighted and unweighted | Fail before the fix: no variation despite unlocking. Afterward: nonzero variation on controlled nonzero-noise data, one shared value within each refit, complete experiment membership, stable replicate indices, and unchanged primary model values and reported centers. Compare primary estimates with the same run configuration without uncertainty. |
| Route controls | Unlock disabled keeps shared values fixed; local-only unlocking retains individual dispatch; the affected route still becomes joint with no-binding skipping disabled. Existing single and already-global clone/workflow checks continue to pass. |
| Assessment gate | All effective members no binding skips together when skipping is enabled; a mixed group runs with every experiment. Exercise manual overrides in both directions, Inconclusive/Not assessed, and skipping disabled. Primary failure/cancellation never produces a subset joint pass. |
| Admission and Update Result | Existing complete, partial, limit-terminated and cancelled replicate rules remain unchanged. A usable partial result is replaceable; cancellation retains the saved result and live attachments. Root/member outcomes agree and root counters are not multiplied. Reuse existing controlled failure/cancellation fixtures. |
| Shared presentation and correlation | Partial completion counts joint attempts once; live bootstrap scope is joint. With at least 30 complete refits, selected-member correlation includes the shared coordinate once and uses joint counts. Old independent reconstructed ensembles keep their current classification. |
| `.ftxtc` save/reopen | Preserve primary locks/centers, unlocked shared refit values, full membership, indices, counts and outcomes using existing `globalReplicates` and member snapshots. Reopening performs no fitting; the shared presentation/correlation selection remains correct. |

Run focused regressions first, then the full `AnalysisITC.Core.Tests` suite. Verify the affected progress, counts and result/correlation views in native macOS and Avalonia using their existing tests and a focused smoke check. Do not broaden validation into a benchmark project or external forward-model claim. Record representative joint-run time and retained counts with the current budget; a convergence problem warrants a separate decision, not automatic budget scaling.

## Documentation and completion

During implementation, add a short explanation beside parameter unlocking in [the fitting manual](../Documentation/UserManual/pages/06-fitting-models.md#unlock-parameters-during-error-estimation) and the relevant section of [HelpTextResource.txt](../Resources/HelpTextResource.txt): sharing remains enforced, primary values stay fixed, and joint refits include the complete experiment set. Explain the group skip rule where uncertainty skipping is already documented. Omit routing mechanics and new controls.

Read [FTXTC_FORMAT.md](../Documentation/Schemas/FTXTC/FTXTC_FORMAT.md) before persistence work. Use the existing representation; a schema or report-content change requires returning to scope review. Preserve `.ftxtc` compatibility and read-only `.ftitc` support.

Completion requires production-route fail-before/pass-after evidence, the Core suite and affected desktop checks, the round trip above, and a focused diff confined to this behavior. Then reconcile ITC-118 and CR-071 with actual validation. Until implementation and validation, CR-071 remains unresolved.

Evidence available when this plan was written: the 23 October investigation reproduced four production-route failures, demonstrated reuse of the existing joint routine with 20 retained refits in each weighted/unweighted N/Offset case, and checked mixed membership, metadata, count presentation and seven existing clone tests. Temporary caller and results: `/private/tmp/itc118-investigation/`; build log: `/private/tmp/itc118-build.log`. These are prior investigation results, not execution of this plan. Temporary files are not required to implement the permanent regressions.
