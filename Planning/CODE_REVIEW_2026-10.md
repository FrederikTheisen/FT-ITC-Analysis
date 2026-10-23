# Full-repository code-quality review — October 2026

Reviewed **51 units covering 828 unique maintained files and 268,216 inventory-time physical lines**, plus cross-cutting desktop, duplication and test-coverage reviews. Unit reviewers raised 83 formal candidates and cross-cutting reviewers raised 7. Independent verification classified those 90 candidates as **85 confirmed, 1 plausible and 4 rejected**. Two pairs sharing a root cause were merged, leaving **84 triage entries: 83 Confirmed and 1 Plausible; 10 High, 61 Medium and 13 Low**.

The five highest-priority findings are:

1. CR-001: changing selection during a fit can make the next fit run on the previous experiment.
2. CR-002: corrected-view spline dragging accumulates baseline displacement with every pointer event.
3. CR-003: copying a locked baseline can leave new settings beside old calculated heats.
4. CR-004: closing a decision dialog can select Save or Replace.
5. CR-005: associated-figure export can silently overwrite existing PDFs.

This is a staging review for maintainer triage, not an approved implementation backlog. Existing ITC issues were deduplicated before promotion; related entries identify adjacent scope rather than treating an already tracked defect as new. The two merges combine oversized report pagination failures in CR-023 and unchanged Details-field round trips in CR-037.

**Verification scope.** Confirmed means an independently reviewed source-and-caller path establishes the stated failure or concrete maintenance cost; it does not imply an interactive reproduction. CR-010 was initially Plausible; its subsequent controlled reproduction and fix are recorded below. The existing `AnalysisITC.MacOS.Tests/PreferencesLayoutTests.swift` harness passed after rerunning with a temporary writable Swift module cache; the initial invocation failed before tests because of cache permissions. The temporary cache was removed. During the original review, no .NET suites, interactive desktop scenarios, PDF renders, provider/service calls, deployments or packaging were run. No fixes, commits, or changes to `Planning/ISSUES.md` were made during that review; this staging file was its sole persistent output. The opening classification counts describe that original review; later resolutions are recorded in the entries.

**Review basis.** The working tree was reviewed on 16 October 2026. HEAD advanced through concurrent work from `06602ff3a235a13a7cd2f5559d49adf2bad214b8` at inventory to `9a2127b0af5d9eeb209a1c023c2c7e653e63a91b` at final verification, alongside unrelated uncommitted edits. Those changes were preserved. Counts describe the inventory, while finding locations were checked against the final working tree. This is not an immutable release qualification. Build outputs, vendored packages and generated files were excluded; scripts were inspected without executing packaging, signing, notarization, installer or release operations.

## Current-file recheck — 2026-10-19

**Planning convention:** if implementation is separately authorized, generally assign bounded work to gpt-6-luna agents under gpt-6-astra orchestration; escalate difficult scientific, persistence or concurrency decisions. This pass used parallel read-only investigation waves; only the orchestrator updates this artifact.

**Basis and validation:** all 84 CRs were rechecked against the files as they currently look, including callers, guards, test sources and tracker/plan decisions. Commit, push and staging state do not affect verdicts. Current source and test-source inspection is recorded as `validation=source`; **no tests, builds, interactive reproductions, service calls or release operations were executed in this pass**. Earlier validation records below remain historical evidence, not newly rerun results. Native macOS means `AnalysisITC.MacOS`; Avalonia is a separate consumer.

**Current counts:** **75 FIXED · 3 OPEN · 1 CLOSED · 2 DEFERRED · 1 SUPERSEDED · 2 REJECTED = 84**; PARTIAL and UNVERIFIED: 0. Counts include the maintainer's 22 October closure of CR-074 without a code change and the 23 October CR-066 tooling fix. FIXED means the original failure is addressed within the accepted scope, not that every platform workflow was interactively validated. CR-006 retains its accepted detection-only mitigation; CR-016 retains the accepted tandem restriction. CR-029/ITC-117 and CR-071/ITC-118 remain deferred, not fixed. The opening review counts and all original findings/resolutions are preserved.

| CR | Current status | CR | Current status |
|---|---|---|---|
| [CR-001](#cr-001--selecting-another-experiment-during-fitting-leaves-the-next-fit-on-the-old-data) | FIXED | [CR-043](#cr-043--native-automatic-injection-volume-is-capped-at-20-µl-only-in-its-primary-field) | FIXED |
| [CR-002](#cr-002--corrected-view-spline-dragging-accumulates-displacement-on-every-pointer-event) | FIXED | [CR-044](#cr-044--native-graph-construction-fails-when-every-injection-is-excluded) | FIXED |
| [CR-003](#cr-003--copying-a-locked-baseline-leaves-destinations-with-old-calculated-heats) | FIXED | [CR-045](#cr-045--unified-native-power-axes-include-experiments-with-no-thermogram) | FIXED |
| [CR-004](#cr-004--closing-an-avalonia-decision-dialog-selects-save-or-replace) | FIXED | [CR-046](#cr-046--native-residual-axes-can-exclude-their-own-uncertainty-bars) | REJECTED |
| [CR-005](#cr-005--associated-figure-export-silently-overwrites-existing-pdfs-on-avalonia) | FIXED | [CR-047](#cr-047--two-buffer-protonation-graphs-generate-nonfinite-native-uncertainty-bands) | FIXED |
| [CR-006](#cr-006--saved-thermodynamic-values-follow-edited-experiment-temperature) | FIXED | [CR-048](#cr-048--constant-fitted-values-produce-zero-span-native-dependence-axes) | FIXED |
| [CR-007](#cr-007--copy-processing-mutates-heat-only-imports-before-failing) | FIXED | [CR-049](#cr-049--native-energy-editors-change-scale-while-reusing-the-same-visible-text) | FIXED |
| [CR-008](#cr-008--a-partial-settings-write-can-prevent-avalonia-from-starting) | FIXED | [CR-050](#cr-050--native-results-replaces-saved-competitor-intervals-with-symmetric-bounds) | FIXED |
| [CR-009](#cr-009--restarting-during-registration-mail-retry-can-stall-the-outbox) | FIXED | [CR-051](#cr-051--native-integration-sliders-use-another-experiments-baseline-exclusion-policy) | FIXED |
| [CR-010](#cr-010--failed-profile-diagnostics-can-still-supply-numerical-uncertainty) | FIXED | [CR-052](#cr-052--changing-export-units-resets-native-correction-choices) | FIXED |
| [CR-011](#cr-011--two-site-fit-summaries-omit-requested-entropy-contributions) | FIXED | [CR-053](#cr-053--native-combined-data-export-offers-correction-options-it-cannot-honor) | FIXED |
| [CR-012](#cr-012--switching-to-sequential-can-leave-global-constraints-uneditable) | FIXED | [CR-054](#cr-054--native-tandem-creation-lets-validation-errors-escape-its-ui-action) | FIXED |
| [CR-013](#cr-013--min-volatility-baseline-fallback-creates-a-nan-spline-point) | FIXED | [CR-055](#cr-055--native-buffer-subtraction-inherits-a-filter-that-excludes-tandem-targets) | SUPERSEDED |
| [CR-014](#cr-014--integration-uncertainty-uses-different-samples-from-the-heat-integral) | FIXED | [CR-056](#cr-056--native-experiment-designer-has-no-sequential-site-count-editor) | FIXED |
| [CR-015](#cr-015--removing-leading-orphan-injections-breaks-positional-id-assumptions) | FIXED | [CR-057](#cr-057--native-details-commits-ph-input-that-its-editor-marks-invalid) | FIXED |
| [CR-016](#cr-016--tandem-reconstruction-changes-the-buffer-correction-used-during-scanning) | FIXED | [CR-058](#cr-058--opening-a-project-silently-appends-to-native-report-only-documents) | FIXED |
| [CR-017](#cr-017--the-viewer-strips-meaning-from-typed-experimental-conditions) | FIXED | [CR-059](#cr-059--public-quota-checks-race-with-durable-request-admission) | FIXED |
| [CR-018](#cr-018--quick-clipboard-exports-misalign-scientific-table-columns) | FIXED | [CR-060](#cr-060--public-accounting-omits-durable-attempts-without-a-final-request-summary) | FIXED |
| [CR-019](#cr-019--mixed-result-exports-use-one-label-for-stoichiometry-and-syringe-activity) | FIXED | [CR-061](#cr-061--concurrent-public-registry-writes-can-undo-a-successful-revocation) | FIXED |
| [CR-020](#cr-020--unavailable-salt-analysis-results-are-displayed-as-scientific-zeros) | FIXED | [CR-062](#cr-062--month-rollover-stops-unresolved-public-costs-from-blocking-new-work) | FIXED |
| [CR-021](#cr-021--missing-buffer-thermochemistry-is-treated-as-an-exact-zero-enthalpy) | FIXED | [CR-063](#cr-063--registry-health-reports-success-without-reading-the-registry) | FIXED |
| [CR-022](#cr-022--imported-comment-concentrations-misread-scientific-notation) | REJECTED | [CR-064](#cr-064--registration-tests-read-and-may-write-the-service-default-registry) | FIXED |
| [CR-023](#cr-023--oversized-report-blocks-lose-content-instead-of-continuing-on-another-page) | FIXED | [CR-065](#cr-065--deployment-activation-failures-bypass-the-existing-rollback-branch) | FIXED |
| [CR-024](#cr-024--exported-and-displayed-summary-kd-uncertainties-disagree) | FIXED | [CR-066](#cr-066--precision-reports-attach-current-source-hashes-to-potentially-older-executions) | FIXED |
| [CR-025](#cr-025--disabling-injection-tables-removes-schemas-still-used-by-processing-evidence) | FIXED | [CR-067](#cr-067--avalonia-labels-the-syringe-activity-input-as-stoichiometry) | FIXED |
| [CR-026](#cr-026--public-enrollment-notifies-native-ui-subscribers-from-a-worker-thread) | FIXED | [CR-068](#cr-068--native-salt-plots-replace-fitted-centers-with-fresh-random-sample-means) | FIXED |
| [CR-027](#cr-027--native-interpretation-hides-permitted-evidence-choices-inside-a-stale-parent) | FIXED | [CR-069](#cr-069--avalonia-competitive-designer-exposes-unidentified-internal-units-and-coordinates) | FIXED |
| [CR-028](#cr-028--saving-unrelated-settings-resets-the-live-fitting-configuration) | FIXED | [CR-070](#cr-070--several-export-actions-let-filesystem-failures-escape-the-ui-boundary) | FIXED |
| [CR-029](#cr-029--competitive-apparent-kd-omits-fitted-stoichiometry-uncertainty) | DEFERRED | [CR-071](#cr-071--bootstrap-unlocking-is-bypassed-when-every-shared-coordinate-is-locked) | DEFERRED |
| [CR-030](#cr-030--clearing-a-fit-leaves-its-cached-null-assessment-visible) | FIXED | [CR-072](#cr-072--independent-child-solvers-discard-explicit-iteration-and-tolerance-settings) | FIXED |
| [CR-031](#cr-031--avalonia-final-figure-pdfs-have-the-wrong-physical-dimensions) | FIXED | [CR-073](#cr-073--attempt-fix-repeats-the-incomplete-injection-warning-without-fixing-it) | FIXED |
| [CR-032](#cr-032--supporting-figure-size-controls-are-capped-by-captured-final-figure-dimensions) | FIXED | [CR-074](#cr-074--an-exactly-zero-heat-capacity-estimate-disappears-from-summaries) | CLOSED |
| [CR-033](#cr-033--corrected-view-spline-handles-use-raw-baseline-coordinates) | FIXED | [CR-075](#cr-075--bootstrap-progress-retains-every-permanent-status-entry-until-completion) | FIXED |
| [CR-034](#cr-034--a-report-build-can-overwrite-a-newer-explanation-choice) | FIXED | [CR-076](#cr-076--the-persistence-stability-test-cannot-detect-swapped-wire-identities) | OPEN |
| [CR-035](#cr-035--avalonia-immediately-overwrites-tandem-scan-failure-feedback) | FIXED | [CR-077](#cr-077--same-name-experiments-lose-fixed-parameter-entries-from-reports) | FIXED |
| [CR-036](#cr-036--changing-the-attribute-list-discards-pending-details-edits) | FIXED | [CR-078](#cr-078--a-native-preference-describes-project-reprocessing-instead-of-import-recalculation) | FIXED |
| [CR-037](#cr-037--applying-details-alters-scientific-values-and-dates-that-were-not-edited) | FIXED | [CR-079](#cr-079--copy-to-next-peak-is-enabled-before-its-hidden-zoom-requirement-is-met) | FIXED |
| [CR-038](#cr-038--avalonia-overrides-automatic-selection-of-a-newly-created-result) | FIXED | [CR-080](#cr-080--back-in-account-creation-enters-an-invalid-administration-workflow) | FIXED |
| [CR-039](#cr-039--avalonia-silently-drops-case-distinct-linux-import-paths) | FIXED | [CR-081](#cr-081--unknown-fallback-token-usage-is-reported-as-a-complete-subtotal) | OPEN |
| [CR-040](#cr-040--an-avalonia-fitting-test-exits-before-its-worker-and-admission-lease-finish) | FIXED | [CR-082](#cr-082--historical-tight-tolerance-diagnostics-do-not-establish-their-claimed-tolerance) | OPEN |
| [CR-041](#cr-041--preferences-tests-depend-on-the-live-interpretation-service) | FIXED | [CR-083](#cr-083--historical-reporting-tests-assert-retired-document-contents-and-filenames) | FIXED |
| [CR-042](#cr-042--native-copy-processing-buttons-have-no-connected-actions) | FIXED | [CR-084](#cr-084--correcting-a-tandem-import-option-requires-restarting-the-import) | FIXED |

The dated blocks beneath each finding carry current evidence and, for unresolved findings, bounded strategies or explicit decisions. Work order and consequential questions are at the end.

## CR-001 — Selecting another experiment during fitting leaves the next fit on the old data

- Priority: High
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:134`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:436`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:737`; `AnalysisITC.Core/Analysis2/AnalysisWorkspace.cs:363`
- Related: ITC-103, ITC-104
- Problem: Avalonia updates the selected experiment during fitting but skips rebuilding the ready analysis context; completion does not repair that context.
- Failure scenario: With Create analysis result off, fit experiment A, select ready experiment B while A is running, wait for completion, then press Run Fit. The existing ready context still owns A.
- Consequence: The application fits A again while B is selected and displayed. Fresh-copy preparation preserves the wrong experiment identity rather than preventing the mistake.
- Suggested direction: Rebuild deferred contexts after fitting and verify their experiment identity against the intended selection before solving.
- Resolution (2026-10-16): Avalonia records deferred context rebuilds and reconciles the latest selection at final fitting completion before releasing its fitting lease. Run Fit independently checks readiness, mode, model and experiment object identities, rebuilding from explicit view inputs when necessary. Selection remains available, selected comparison evidence is refreshed, and completion messages survive queued refreshes until a subsequent user action.
- Validation: All 65 focused workspace, selection-menu and progress checks pass; the full Avalonia suite passes with 498 passed and one opt-in skip. Regressions cover successful and cancelled fitting, latest-selection paths, unready/result/no selection, global membership/order, retained A outputs, overrides/locks and defensive programmatic starts. The standalone attached-MainWindow probe passes with 200 bootstrap iterations: the next fit produces B's solution while A's model, solution and prediction remain unchanged. The native managed parameter harness and a temporary AppKit selection probe also pass. Logs and xUnit results are in `/private/tmp/cr001-focused.*`, `/private/tmp/cr001-avalonia-full.*` and `/private/tmp/cr001-fixed-workflow.log`.
- Boundaries: Mouse-driven desktop verification remains unperformed because UI automation could not attach to the unbundled Avalonia window. The existing CR-038 automatic-result-selection defect remains unchanged; the setting tests verify Core's auto-open request. No Core, native application, scientific calculation or serialization changes are included.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:450`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:971`; validation=source; tests/UI=not-run. Deferred rebuilds reconcile latest selection before releasing fitting ownership; Run Fit checks actual model/mode/experiment identities. Selection-during-fit tests remain; CR-038 is separate.

## CR-002 — Corrected-view spline dragging accumulates displacement on every pointer event

- Priority: High
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:894`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:1226`; `AnalysisITC.Core/DataProcessing.cs:1736`
- Related: ITC-004, ITC-085
- Problem: Each corrected-view drag update adds cursor height to the already modified baseline, which is immediately rebuilt and reused by the next event.
- Failure scenario: With time dragging disabled, drag a sampled-time spline point above zero and continue horizontally at +0.1 µW. A 5 µW point becomes 5.1, then 5.2, then 5.3 µW on successive events.
- Consequence: The final baseline depends on event count and pointer path rather than the intended displacement. Release retains the altered point and recalculates heats; returning the cursor to zero does not undo the accumulated movement.
- Suggested direction: Calculate each update from the captured original power and a stable cursor displacement for the entire drag.
- Resolution (2026-10-16): Avalonia now uses the captured point power plus cursor displacement from the drag start in both raw and corrected views, preserving the initial click offset. Both cursor positions use the same graph transform. The current cursor-to-time calculation and time clamps, point locking, baseline rebuild and release processing remain unchanged; the unused `BaselinePowerAt` helper was removed. CR-033 is addressed separately below.
- Validation: Headless press/move/release events reproduced a 5.1 to 5.2 µW change at an identical cursor position before the fix; repeated events now retain exactly the first power and rebuilt baseline value. Thirteen new cases cover this regression, raw/corrected views, time dragging on/off, Joule/calorie power displays, off-center presses, upward/downward movement, return to the drag start, neighbor spacing and data-range clamps. All 22 graph tests pass; the full Avalonia suite passes with 511 passed and one existing opt-in skip. No interactive desktop verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:901`; validation=source; tests/UI=not-run. Power is original captured point plus stable cursor displacement; actual repeated-move regression source checks both point and rebuilt baseline.

## CR-003 — Copying a locked baseline leaves destinations with old calculated heats

- Priority: High
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/DataManager.cs:1106`; `AnalysisITC.Core/DataManager.cs:1116`; `AnalysisITC.Core/DataProcessing.cs:77`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingWorkspaceControl.cs:1000`
- Related: ITC-004, ITC-084
- Problem: Copy Processing transfers the source lock before deciding whether the destination needs processing, so a newly locked destination is skipped.
- Failure scenario: In Avalonia, process A and B with different baselines, lock A, select A and click Active while B is unlocked. B receives A's baseline/settings and lock, then bypasses recalculation.
- Consequence: B retains its old corrected trace, integration regions and heats beside the new baseline/settings, while the operation reports completion. An unprocessed target instead becomes locked without calculated heats.
- Suggested direction: Keep copied configuration separate from destination publication and lock state, and calculate coherent outputs before applying the intended final lock.
- Resolution (2026-10-16): Confirmed policy is to transfer a locked source's lock after successful destination recalculation. Core now unlocks each copied processor before attaching it, runs the existing baseline and integration sequence, and applies the captured source lock only after completed processing, including a successful Fit Peaks result when applicable. Failed or cancelled processing does not acquire the copied lock. Already locked destinations are untouched, including those without an interpolator. The general processor copy constructor is unchanged. The manual and in-app processing help describe the lock-transfer behavior. CR-007 and CR-042 remain separate.
- Validation: Before the fix, regression tests reproduced stale destination heats instead of the independently expected 12 J and missing integration on fresh destinations; the locked destination without an interpolator was also overwritten. Twelve new Core cases cover processed/unprocessed destinations, different prior baselines and integration regions, locked/unlocked sources, source preservation, Time and Fit Peaks paths, existing destination locks, baseline failure/cancellation, failed peak-fit rollback, and stopping a batch. All 21 processing queue cases pass. The full Release Core suite passes with 3,663 passed and one existing opt-in diagnostic skip; `git diff --check` passes. No interactive macOS or Avalonia verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataManager.cs:1101`; `AnalysisITC.Core/DataManager.cs:1157`; validation=source; tests/UI=not-run. Copies recalculate with unlocked processor before applying source lock; already locked destinations remain untouched. ProcessingCopyTests pins independent heats and failure/cancellation behavior.

## CR-004 — Closing an Avalonia decision dialog selects Save or Replace

- Priority: High
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/MainWindow.axaml.cs:2039`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:2083`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:2168`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:2255`
- Related: ITC-059, ITC-105
- Problem: The dialogs await nonnullable enum results whose zero values mean Save or Replace. Framework chrome-close returns default(TResult) when no explicit result was supplied. See [Avalonia 12.1.4 dialog result behavior](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.4/src/Avalonia.Controls/Window.cs).
- Failure scenario: With an idle dirty project already saved to a writable path, request Quit and dismiss the save prompt using its window close control.
- Consequence: The existing file is saved and the application shuts down if saving succeeds, despite no Save choice. Load Project dismissal likewise selects Replace, although later confirmations can limit that branch. No live chrome interaction was executed.
- Suggested direction: Treat an absent dialog result explicitly as Cancel and cover chrome-close behavior for both prompts.
- Resolution: Both prompts await nullable enum results and map a missing result to Cancel. Their Cancel buttons set `IsCancel = true`, so Escape also cancels. Enum ordering and explicit Save, Discard, Replace and Append results are unchanged.
- Regression verification: `DecisionDialogCancellationTests` exercises the real private `PromptAsync` methods with a shown owner. Before the fix, four no-result closures returned Save/Replace and four Escape cases timed out; all 12 explicit-button cases passed. After the fix, all 20 cases passed both in the isolated project and in a fresh process running the normal Avalonia test assembly. Coverage includes all three save-prompt reasons, Escape with an affirmative button focused, and every explicit button. These are headless result/keyboard tests, not a live operating-system chrome test.
- Suite validation: Follow-up testing of a frozen working-tree snapshot passed the full Avalonia suite twice with 511 passed, one opt-in visual QA skip, and no failures or runner errors, including all 20 CR-004 cases. Control runs excluding the new cases passed with 491 passed and one skip both with and without the four CR-004 production edits. The earlier interrupted run did not reproduce. Its missing-metadata test-case ID maps to `MainWindowSelectionDuringFitClearsStaleReadyContext`; the precise trigger of the original selection exception remains unconfirmed. VSTest socket access and Avalonia telemetry logging were sandbox limitations; verification used the executable xUnit runner and command-local telemetry opt-out. The subsequent help consolidation removes the obsolete resource reference. An isolated baseline plus that consolidation builds successfully; the full Avalonia suite passes with 545 passed and one opt-in skip, both Avalonia help tests pass with real Skia rendering, and the native help harness renders every topic and scientific subsection.
- Other value-type calls (audit only, unchanged): `ConfirmationDialogWindow.ConfirmProcessingClearAsync`, `ConfirmDeleteReportAsync`, `ConfirmAsync` and `ConfirmModal`; `AnalysisReportWindow.ConfirmReplacementAsync` and `ConfirmWarningsAsync`; `AttributeOperationsWindow.ShowAsync`; `ExportDataWindow.ConfigureAsync`; and `TraceabilityStartupWindow.ShowAsync` use `ShowDialog<bool>` and default to false. `AvaloniaFtxtcDuplicatePromptService.ChooseAction` uses `ShowDialog<FtxtcDuplicateAction>` and defaults to `SkipDuplicates`, already covered by its existing close/keyboard tests. These ten calls have cancellation-compatible defaults; the other value-type calls already request nullable results. Changes remain limited to the two CR-004 prompts.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/MainWindow.axaml.cs:2109`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:2193`; validation=source; tests/UI=not-run. Both prompts use nullable enum results mapped to Cancel and Escape-enabled cancel buttons. DecisionDialogCancellationTests covers close/Escape/explicit choices.

## CR-005 — Associated-figure export silently overwrites existing PDFs on Avalonia

- Priority: High
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1233`; `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1338`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:100`
- Related: ITC-061
- Problem: Associated-result export chooses a folder and writes planned names directly with File.Create, checking collisions only within the new batch.
- Failure scenario: Export an associated experiment named sample into a folder already containing sample.pdf, such as a deliberately retained previous figure.
- Consequence: The existing artifact is replaced without an overwrite decision. Native associated export asks, and Avalonia Active/All export uses non-overwriting output.
- Suggested direction: Check the complete plan against existing files and obtain a replacement decision or use the existing non-overwriting writer.
- Resolution: Avalonia associated export now materializes its complete target plan once and passes every path to the existing export confirmation service before creating the destination directory or writing any PDF. Existing files require the shared Overwrite/Cancel decision, matching native associated export. Cancellation, dialog closure and unavailable confirmation UI leave the batch untouched and emit no success status. Approved writes reuse exactly the confirmed paths, captured model choice and saved result-member solutions. Active/All export behavior is unchanged. The manual and in-app help describe confirmation and whole-batch cancellation. CR-070 remains separate.
- Validation: Before the guard, three new regression cases failed: a declined six-file batch exported all six PDFs, the approval callback was bypassed, and a declined export created its missing destination. All ten new export cases now pass, including a later-item collision, actual shared-dialog Cancel/close/Overwrite results, unavailable UI, collision-free output, sanitized/duplicate/case-variant/suffixed names, unchanged unrelated files, fixed confirmed paths and saved member-fit/model-selection evidence in PDF metadata. All 22 focused figure tests pass. The full Avalonia suite reports 564 passed, one existing opt-in skip and one failure in the unrelated, concurrently modified `AnalysisReportEnhancementTests.AppQuitAndMainWindowCloseHonorCancelForAnActiveEditor(quit: False)` (expected one prompt, observed zero); its two cases run alone reproduce that single failure without the export tests. Validation used the executable xUnit runner. Logs and results are `/private/tmp/cr005-before.*`, `/private/tmp/cr005-focused.*`, `/private/tmp/cr005-avalonia-full.*` and `/private/tmp/cr005-report-close-control.*`. No interactive operating-system desktop verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1266`; `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1282`; validation=source; tests/UI=not-run. Complete captured output plan is confirmed before writes; existing saved-member/model selection survives. CR-070's newer Active/All confirmation policy governs those paths too.

## CR-006 — Saved thermodynamic values follow edited experiment temperature

- Priority: High
- Status: Resolved (2026-10-16; detection-only fix).
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/Models/Models.cs:532`; `AnalysisITC.Core/Analysis2/Models/ThermodynamicSolution.cs:35`; `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:2641`
- Related: ITC-069, ITC-103
- Problem: Ordinary saved solutions derive their temperature from the mutable experiment rather than the temperature captured for the fit.
- Failure scenario: Fit at 25 °C, retain the result, then change the experiment to 35 °C in Details. Unchanged affinity and enthalpy now produce different ΔG, −TΔS and ΔS in direct solution/report consumers.
- Consequence: Saved scientific results can change without refitting and disagree with the already constructed temperature-dependence curves. Existing presentation caches may retain the old values, making the inconsistency dependent on the consumer.
- Suggested direction: Use captured fit temperature consistently for saved member thermodynamics, while preserving explicitly selected evaluation temperatures for combined results.
- Resolution (2026-10-16, detection only): The selected mitigation is option (b). New fit-input snapshots capture measured temperature in °C and the result's temperature-dependence state. Temperature edits produce a "temperature changed" reason. Any detected fit-input change fully invalidates a result with captured temperature dependence, even when its members were fitted individually; independent collections without temperature dependence retain partial invalidity. Optional native validity fields retain existing schema versions. Missing/null temperatures skip comparison without backfilling from the existing member-temperature map; missing dependence state uses the restored model's flag and stays missing on resave. The shared reason category, manual, help and report map are updated.
- Validation: Nine behavioral regression cases failed before the change. All 26 new Core cases pass, covering temperature edits/reversal/roundoff, zero and negative Celsius values, independent temperature-series changes to concentrations, inclusion, integration and heats, save/reopen with collapsed or expanded temperature spans, missing/null evidence, schema validation, and shared health/report badges. The full Release Core suite passes with 3,757 passed and one existing external-reference diagnostic skip. All six selected Avalonia status/report/help checks pass, including both new temperature cases. The native AppKit report harness passes the new status-title/reason checks and CoreGraphics PDF rendering for both partial and full invalidity. `git diff --check` passes. Evidence: `/private/tmp/cr006-before.log`, `/private/tmp/cr006-core-full.log`, `/private/tmp/cr006-tests/`, `/private/tmp/cr006-avalonia-focused.xml` and `/private/tmp/cr006-native.log`.
- Review (2026-10-16): No actionable regressions found. All 562 focused Core review checks pass, covering native/legacy compatibility, optional/null validity fields and resaving, import/reuse/copy paths, full invalidity for individually fitted temperature series, and surrounding binding-assessment/skipped-uncertainty behavior. The captured dependence flag takes precedence over the span rebuilt on load, including when edits collapse the series. Missing historical temperatures remain unknown without backfilling. Review evidence: `/private/tmp/cr006-review.log` and `/private/tmp/cr006-review/cr006-review.trx`. The review made no source edits and did not rerun interactive desktop checks.
- Boundary: Resolved for the agreed detection-only scope. `SolutionInterface.Temp`, thermodynamic calculations, evaluation-temperature selection, standalone-fit temperature persistence, and stale-result output eligibility remain unchanged. Derived values may still follow an edited temperature; the change identifies the result as stale rather than freezing those values. Result metadata already persists `memberTemperaturesKelvin` and restores `Model.Parameters.ExperimentTemperature`; when absent, temperature is derived from the loaded experiment. No interactive operating-system desktop verification, packaging or release was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataClasses/AnalysisResultValiditySnapshot.cs:74`; validation=source; tests/UI=not-run. Accepted detection-only mitigation captures temperature/dependence and flags changed inputs; freezing derived values remains outside scope. AnalysisResultTemperatureValidityTests covers edits, reopen and missing evidence.

## CR-007 — Copy Processing mutates heat-only imports before failing

- Priority: High
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataManager.cs:1077`; `AnalysisITC.Core/DataManager.cs:1106`; `AnalysisITC.Core/DataManager.cs:1121`; `AnalysisITC.Core/DataClasses/InjectionData.cs:268`
- Related: ITC-036, ITC-084, ITC-101
- Problem: Batch processing-copy targets can include integrated-heat imports with no raw trace, although the mutation path requires raw samples.
- Failure scenario: In Avalonia, load a processed raw experiment and a .dh experiment, select the raw experiment, and click New. The heat-only target receives the copied processor, loses its integrated flags, then reaches DataPoints.First() on an empty collection.
- Consequence: The target is left with invalidated processing/fit state before an exception escapes the async-void batch. Stored heat numbers are not explicitly erased; the resulting desktop exception presentation was not executed.
- Suggested direction: Validate thermogram availability before changing any destination, and make batch failures handled and observable to the caller.
- Resolution (2026-10-16): Copy to All/Active/New now filters targets by `HasThermogram`; the shared selection path rechecks after processing admission and before any processor replacement or invalidation. Status messages include the count skipped without a thermogram, including batches with no eligible targets and failed batches. Raw targets with a null processor are initialized safely; Copy to New's null-processor branch previously reached a null dereference in the batch. Unexpected preparation or processing/publication failures are caught and surfaced through the existing handled-error UI, with admission/ownership retained until error presentation returns and progress cleared in `finally`. CR-003's recalculation-before-lock behavior is retained. The manual and in-app help explain thermogram eligibility.
- Validation: All twelve new regression cases failed before the fix, observing escaped async-void exceptions safely through a test synchronization context. The twelve cases now pass, covering all four entry points, mixed raw/heat-only batches, preserved imported heats/integration flags/processing revision/fit validity, null/empty/single-sample traces, null processors, eligibility changes while queued, and injected preparation/publication errors with following queued work. All 33 focused copy/queue tests pass. Full Release Core suite: 3,674 passed, one existing opt-in diagnostic skip, and one failure in the untouched `AnalysisResultUpdaterTests.IndividualUpdateProgressRisesOnceWithoutGlobalFitStatus` progress-order assertion. Running that test's two cases alone, without the copy tests executing, produced two failing runs and one passing run; solver/progress code was not changed. Results: `/private/tmp/ft-itc-cr007-tests/`. `git diff --check` passes. No interactive macOS or Avalonia verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataManager.cs:1076`; `AnalysisITC.Core/DataManager.cs:1097`; validation=source; tests/UI=not-run. Thermogram targets are filtered before mutation and rechecked after admission; handled failures retain ownership and cleanup. Both desktop commands use the Core path.

## CR-008 — A partial settings write can prevent Avalonia from starting

- Priority: High
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Platform/AvaloniaJsonSettingsStore.cs:101`; `AnalysisITC.Avalonia/Platform/AvaloniaJsonSettingsStore.cs:114`; `AnalysisITC.Avalonia/Platform/AvaloniaPlatformBootstrapper.cs:12`; `AnalysisITC.Avalonia/App.axaml.cs:37`
- Related: ITC-059, ITC-105
- Problem: Settings overwrite the sole JSON file directly, and startup deserializes it without malformed-file recovery.
- Failure scenario: An interruption or I/O failure after truncation leaves partial JSON. The next startup constructs the store before the main window and project-recovery flow.
- Consequence: Every startup fails until the file is repaired or removed externally. Missing-file defaults do not protect this state; interrupted-write frequency was not measured.
- Suggested direction: Use atomic replacement with recoverable prior settings and an explicit startup policy for invalid configuration.
- Resolution (2026-10-16): Avalonia serializes settings to a unique temporary file in the same directory, flushes it to disk, then atomically replaces `settings.json`, keeping the previous valid file as `settings.json.bak`. Startup prefers the primary file, falls back to a valid backup when the primary is missing or malformed, and uses defaults when neither is valid; recovery is logged. The immediate startup preference-migration save preserves the recovered backup instead of overwriting it with the malformed primary. Save failures remain observable, and temporary-file cleanup does not mask them.
- Validation: The new store regressions reproduced malformed-file failures before the fix. All 17 focused Avalonia checks pass, including 14 new cases covering invalid/empty/truncated JSON, backup recovery and preservation, failed replacement with unchanged saved settings and successful retry, typed settings/unknown-key round trips, and headless settings initialization plus main-window creation with and without a backup. The Avalonia test project builds with no errors; existing warnings remain. Evidence is in `/private/tmp/cr008-before.*`, `/private/tmp/cr008-focused.*` and `/private/tmp/cr008-build.log`.
- Boundaries: No full-suite or interactive desktop run was performed; Windows and Linux filesystem behavior was not exercised. Builds disabled Avalonia's build-telemetry task to avoid its sandbox-denied log path. Non-JSON filesystem errors still propagate. No native macOS settings, Core, scientific calculations, project formats or manual changes are included.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Platform/AvaloniaJsonSettingsStore.cs:106`; `AnalysisITC.Avalonia/Platform/AvaloniaJsonSettingsStore.cs:139`; validation=source; tests/UI=not-run. Flushed temporary writes replace primary with backup; invalid/missing primary recovers without destroying valid backup. Store tests cover recovery and failed replacement/retry; cross-OS runtime remains historical limitation.

## CR-009 — Restarting during registration-mail retry can stall the outbox

- Priority: High
- Confidence: Confirmed
- Category: Web
- Status: Fixed locally on 16 October 2026
- Location: `AnalysisITC.Web/SelfRegistrationStore.cs:59`; `AnalysisITC.Web/RegistrationDeliveryOutbox.cs:205`; `AnalysisITC.Web/RegistrationMailSender.cs:23`; `AnalysisITC.Web/Program.cs:225`
- Related: None
- Problem: Startup reconciliation marks registry-backed activating accounts active, while successful access-code delivery requires the account still to be activating before its outbox update can commit.
- Failure scenario: Activation provisions a credential, email delivery fails temporarily, and the service restarts before retry. Reconciliation changes the account state; a later successful email response cannot mark the pending message sent.
- Consequence: Successful or deduplicated retries leave the oldest message eligible without advancing its retry time, potentially starving later mail. A later provider failure may eventually mark it failed; no deployed incident was observed.
- Suggested direction: Make reconciliation compatible with pending delivery and make successful completion idempotent for an already-active matching registration.
- Resolution: Access-code `MarkSent` accepts the matching account in either `activating` or `active` state, preserving an existing activation timestamp and committing the delivery as sent. This also allows already-reconciled pending deliveries to complete without changing startup reconciliation.
- Validation: The new store-level `AccessCodeRetryAfterReconciliationCompletesAndAdvancesQueue` test failed before the fix (`pending` instead of `sent`) and passes afterward. It covers provisioning, retry scheduling, reopening the stores, reconciliation, stable credential/idempotency key, successful completion, repeated completion, preserved activation timestamp, and advancing to the next registration. All 21 registration tests pass. The full Web suite had 404 passes and one timeout in `CompletedMalformedMultipartPreservesItsExistingError`; both cases of that upload test passed when rerun in isolation. No live mail delivery or deployment was tested.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/RegistrationDeliveryOutbox.cs:267`; `AnalysisITC.Web.Tests/RegistrationSecurityTests.cs:114`; validation=source; tests/UI=not-run. Matching active/activating accounts complete delivery idempotently without changing activation time. Restart/retry path has regression coverage; ITC-120 configuration retry remains separate.

## CR-010 — Failed profile diagnostics can still supply numerical uncertainty

- Priority: High
- Confidence: Confirmed by a controlled two-coordinate reproduction; no ordinary real-data case demonstrated.
- Category: Scientific integrity
- Status: Fixed locally on 16 October 2026.
- Location: `AnalysisITC.Core/Analysis2/ProfileLikelihoodResult.cs:208`; `AnalysisITC.Core/Analysis2/Models/Models.cs:749`; `AnalysisITC.Core/Presentation/LinkedThermodynamicEvaluation.cs:34`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs:645`; `AnalysisITC.Core/Analysis2/ProfileLikelihoodEstimator.cs:140`; `AnalysisITC.Core/Analysis2/AnalysisResultUpdater.cs:129`
- Related: ITC-090, ITC-094, ITC-099
- Problem: Numerical consumers accept completed profile coordinates without checking whether the owning run ended in CompleteFailure, although the solver refuses to install uncertainty from that outcome.
- Failure scenario: A multi-parameter profile completes an affinity interval before a different coordinate finds a better primary minimum. Staging retains the completed interval while marking the run failed; the getters can still turn that diagnostic interval into Kd or linked thermodynamic uncertainty.
- Consequence: Outputs can present numerical uncertainty from a run declared unavailable. A deterministic analytic model with integrated observations reproduces a completed affinity interval followed by another coordinate's `PrimaryMinimumImproved`, with `CompleteFailure` still supplying numerical Kd uncertainty, in both weighted and unweighted runs. This demonstrates the production estimator/reporting path with a constructed model, not occurrence in an ordinary experimental workflow.
- Suggested direction: Use one shared eligibility rule for numerical profile reporting. Preserve failed-run diagnostics, and verify the mixed-coordinate sequence before promoting this candidate to confirmed.
- Resolution: Core `ProfileLikelihoodRunResult.CanUseIntervals` now supplies the existing solver eligibility rule to local/shared Kd mapping, linked thermodynamic propagation, historical interpretation projection, numerical interpretation fields, temperature-coordinate application, direct-profile labeling, profile summary eligibility and Update Result interval counts. Completely failed runs retain raw coordinates, endpoints, warnings and diagnostic counts but cannot supply numerical uncertainty or make an update usable. Completed, partial and cancelled-run interval handling is preserved. Reported primary values, fitting equations, search behavior and project schema are unchanged; ITC-114 recovery is not implemented or modified.
- Validation: The initial regression set reproduced eight failures with three unaffected cases passing. All 20 final `FailedProfileUncertaintyTests` cases pass, covering the actual mixed-coordinate estimator sequence with weighted/unweighted integrated observations, local/shared affinity and Gibbs mapping, linked thermodynamics, matched/historical interpretation evidence, save/reopen and viewer restoration, and failed-run summary/update eligibility. All 595 focused Core tests pass; the full Core suite passes with 3,728 passed and one existing external-reference skip. The 119 selected Avalonia result/report/interpretation checks pass. Native macOS managed compilation and `UncertaintyPresentationTests`, `UpdateResultStatusTests`, and `AnalysisReportReferencesTests` pass. No interactive ordinary-data reproduction, deployment, packaging or release was performed. Logs and TRX evidence: `/private/tmp/cr010-validation/` (`before`, `focused-final`, `core-final`, `avalonia`, and `native`).

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Analysis2/ProfileLikelihoodResult.cs:208`; `AnalysisITC.Core/Analysis2/Models/Models.cs:748`; `AnalysisITC.Core/Presentation/LinkedThermodynamicEvaluation.cs:35`; validation=source; tests/UI=not-run. Owning-run CanUseIntervals gates numerical consumers, installation, summaries and interpretation; failed coordinates remain diagnostics. FailedProfileUncertaintyTests covers the controlled mixed-coordinate route. ITC-114's older tracker boundary is stale; primary recovery remains separate.

## CR-011 — Two-site fit summaries omit requested entropy contributions

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Analysis2/Models/TwoSetsOfSites.cs:482`; `AnalysisITC.Core/Presentation/AnalysisParameterSummaryPresentation.cs:107`; `AnalysisITC.Core/Presentation/PublicationFigure.cs:1194`
- Related: ITC-030
- Problem: The two-site solution formatter never handles the Entropy display flag for either site, although the saved quantities exist.
- Failure scenario: Fit a two-site model and inspect its Analysis summary or enable Derived parameters in a figure annotation.
- Consequence: Both −TΔS values are omitted on both desktops. Report parameter tables retain them; no numerical calculation or persisted value is wrong.
- Suggested direction: Handle both existing site-specific quantities in the shared solution formatter and verify the actual two-site display path.
- Resolution (2026-10-17): The shared two-site formatter now includes each site's existing −TΔS value when Entropy is requested, between that site's enthalpy and Gibbs energy. Both desktop inspectors, analysis graph boxes and publication figure annotations receive the rows through their existing shared presentation paths. Calculations, uncertainty propagation, serialization and report parameter tables are unchanged.
- Validation: All eight new `TwoSiteEntropyPresentationTests` cases pass, covering both site labels and independently expected values, row order, entropy-only/derived/disabled display flags, kJ/mol and kcal/mol, and standard-deviation/hidden uncertainty formatting through the inspector, graph text and publication annotation paths. Before the fix, six cases failed on missing entropy rows while both entropy-disabled cases passed. The full Core suite completed successfully (exit 0). All 50 selected Avalonia workspace/publication checks pass. Native macOS managed compilation and `AnalysisNullGraphTests` / `AnalysisParameterDraftTests` pass after rerunning an initial runtime dependency-loading failure. No interactive desktop verification, packaging or release was performed. Focused and desktop validation logs: `/private/tmp/cr011-before.log`, `/private/tmp/cr011-focused.log`, `/private/tmp/cr011-avalonia.log`, `/private/tmp/cr011-native.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Analysis2/Models/TwoSetsOfSites.cs:497`; validation=source; tests/UI=not-run. Both site entropy contributions follow the Entropy flag through shared inspector/graph/publication formatting; TwoSiteEntropyPresentationTests uses independent values.

## CR-012 — Switching to Sequential can leave global constraints uneditable

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Analysis2/AnalysisWorkspace.cs:66`; `AnalysisITC.Core/Analysis2/AnalysisBuilder.cs:216`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:583`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:603`
- Related: ITC-078, ITC-082, ITC-086
- Problem: Model changes retain old global constraints, but a rejected new context removes the controls needed to repair them.
- Failure scenario: Fit two experiments with One Site and Affinity1 shared, then select Sequential. Affinity1 remains shared while Affinity2 has no constraint; the intentional family-consistency check rejects this combination.
- Consequence: Fitting becomes unavailable and the constraint controls disappear on both desktops. Native launch-time normalization occurs after the failing rebuild, so the user must restore defaults or switch back to remove the constraint.
- Suggested direction: Reconcile incompatible constraints during model transitions, or preserve a way to repair them when context validation fails.
- Resolution (2026-10-17): Actual model changes reset global constraints to Independent in shared Core, including changes made in Single mode before returning to Multiple. Native macOS also clears cached constraint drafts so revisiting a model cannot restore prior sharing choices; parameter and model-option drafts remain intact. Ordinary rebuilds, mode switches and selecting the current model retain constraints. Sequential's consistency validation remains unchanged.
- Validation: The original failure was reproduced through Avalonia's actual controls in a headless window without running a fit. All 50 focused Core checks and 48 Avalonia workspace checks pass, including model changes in Single and Multiple modes and model round trips. The native AppKit parameter-inspector harness passes with actual model-change actions, available Sequential constraint controls, cached-draft reset, pending sharing edits, same-model selection and retained parameter/option drafts. The full Core suite passes: 3,825 passed, one skipped. Logs: `/private/tmp/cr012-core-focused.log`, `/private/tmp/cr012-core-full.log`, `/private/tmp/cr012-avalonia-focused.log` and `/private/tmp/cr012-native.log`. Manual retention wording and the existing in-app global-fitting guidance now state the reset behavior. No mouse-driven desktop verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Analysis2/AnalysisWorkspace.cs:66`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:167`; validation=source; tests/UI=not-run. Actual model changes clear sharing constraints; native cached constraint drafts reset while parameter/option drafts remain. Existing model-transition tests verify Sequential controls remain repairable.

## CR-013 — Min-volatility baseline fallback creates a NaN spline point

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataClasses/DataPoint.cs:48`; `AnalysisITC.Core/DataProcessing.cs:1604`; `AnalysisITC.Core/DataProcessing.cs:1648`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingWorkspaceControl.cs:474`
- Related: ITC-058, ITC-101
- Problem: Volatility-weighted averaging divides zero by zero for one sample, but automatic spline construction explicitly permits a singleton fallback.
- Failure scenario: In Avalonia, use an unlocked spline, exclude integrated points, select Min volatility, and extend an integration endpoint to its full 180-second delay on 1-second data. The candidate window [174,175] is excluded and falls back to sample 174 alone.
- Consequence: A NaN spline point enters baseline construction and prevents valid baseline subtraction/integration. Sequential editing and finite input data are sufficient; the default Mean mode avoids the problem.
- Suggested direction: Define singleton averaging as that sample's power and check the complete automatic-window fallback against an independently finite thermogram.
- Resolution (2026-10-17): Shared Core volatility-weighted averaging now returns the sample's power for a singleton. Multi-sample weighting, empty-input behavior, and automatic fallback sample selection are unchanged, including fallback to a raw sample inside an excluded integration region. No UI, persistence, or manual changes.
- Reproduction: Five of seven new Core cases failed before the fix: positive, negative, and zero singleton powers returned NaN, as did the automatic fallback point for both Linear and Smooth splines. The processing fixture uses finite 1-second data, a 40-second initial delay, a 180-second injection delay, excluded integrated points, and a sequential endpoint extension from 60 to 180 seconds. Its final candidate window [214,215] falls back to raw sample 214. Multi-sample weighting and empty-input checks already passed.
- Validation: All 61 focused Core checks pass, covering the seven new cases plus spline conversion, integration, and baseline autocorrelation. Both spline algorithms complete baseline construction with the new fallback point, finite baselines and corrected traces, and the independently expected 10-microjoule integrated pulse. The full Core suite passes (3,840 passed, one existing opt-in skip, zero failures). The final focused rerun additionally verifies baseline completion and the installed fallback point to rule out retaining the earlier baseline. Logs: `/private/tmp/cr013-before.log`, `/private/tmp/cr013-focused.log`, `/private/tmp/cr013-full.log`; corresponding `cr013-*.trx` files are in `AnalysisITC.Core.Tests/TestResults`. Tests ran against the current dirty working tree; unrelated changes were preserved. No interactive desktop verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataClasses/DataPoint.cs:48`; validation=source; tests/UI=not-run. Singleton volatility averaging returns the sample power; automatic-window tests cover finite fallback/integration while retaining multi-sample and empty-input behavior.

## CR-014 — Integration uncertainty uses different samples from the heat integral

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/DataClasses/InjectionData.cs:305`; `AnalysisITC.Core/DataClasses/InjectionData.cs:480`; `AnalysisITC.Core/Math/Statistics.cs:12`; `AnalysisITC.Core.Tests/BaselineAutoCorrelationTests.cs:80`
- Related: None
- Problem: Heat integration uses right-endpoint powers and actual overlap durations, while the integration-noise calculation counts strictly interior samples with equal full-step weights.
- Failure scenario: A permitted two-interval region contributes two power samples to heat but contains only one strict-interior timestamp, causing zero SD despite nonzero baseline noise. Four aligned intervals use four heat samples but three in the AR(1) variance.
- Consequence: Processing uncertainties are wrong; zero values disable desktop weighted fitting, while nonzero underestimated values remain eligible and affect relative weights. The existing autocorrelation test codifies the sample-count mismatch.
- Suggested direction: Propagate integration noise using the actual contributing samples and overlap weights, with independent aligned and fractional-boundary covariance expectations. Keep separate baseline/cross-covariance policy decisions outside this finding.
- Resolution (2026-10-16): Integration collects each positive interval's contributing duration while retaining float subtraction and float power multiplication before double heat accumulation. The uncertainty calculation uses those durations in a linear-time weighted AR(1) covariance sum, including right endpoints and single contributing intervals. Empty integration and insufficient baseline data retain zero SD. The baseline-noise estimator, correlation estimate, separate baseline contribution, heat-direction threshold, public API and persistence are unchanged. The manual and matching in-app help replace the integration effective-count equation with the weighted covariance expression.
- Validation: Six new integration cases reproduced the uncertainty defect before the fix; the new pre-change heat bit-pattern regression already passed. All 45 focused integration, autocorrelation and real-data tandem cases pass after the fix, followed by the full Core suite (3,808 passed, one existing opt-in skip, zero failures). Independent covariance expansion covers positive/negative/zero correlation, existing correlation limits, fractional and irregular durations, and small scales. Existing direction assertions are unchanged; positive and negative tiny short-window peaks now correctly become Unknown with finite nonzero SD. Evidence: `/private/tmp/cr014-before.log`, `/private/tmp/cr014-focused.log`, `/private/tmp/cr014-full-core.log`, and the corresponding `AnalysisITC.Core.Tests/TestResults/cr014-*.trx` files.
- Boundaries: Core calculation and tests plus documentation only; no desktop UI, serialization, migration, automatic reprocessing, packaging, version, commit or push changes. Existing project loads retain saved heats and SDs. Validation uses the current dirty working tree with unrelated changes preserved. No interactive desktop verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataClasses/InjectionData.cs:306`; `AnalysisITC.Core/DataClasses/InjectionData.cs:491`; `AnalysisITC.Core.Tests/BaselineAutoCorrelationTests.cs:71`; validation=source; tests/UI=not-run. Uncertainty receives actual positive overlap durations from heat integration; independent covariance expansion covers weighted AR(1), with baseline contribution and saved heats retained.

## CR-015 — Removing leading orphan injections breaks positional ID assumptions

- Priority: Medium
- Status: Resolved (2026-10-19), together with [ITC-115](ISSUES.md#itc-115---leading-orphan-injection-cleanup-breaks-indexing-and-concentration-history) and CR-073.
- Confidence: Confirmed
- Category: Correctness
- Original failure: Removing a recorded leading injection shifted ID-based prediction lookup and erased its contribution to the retained shots' concentration history. The original production-path reproduction used a deliberately truncated trace; ordinary-file prevalence was not established.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataReaders/DataValidator.cs:99`; `AnalysisITC.Core/DataReaders/MicroCalITC200Reader.cs:297`; validation=source and executed tests. Raw imports reject recorded starts outside the trace before optional warnings, without deleting or renumbering recorded shots. Exact endpoints are accepted; no tolerance was introduced. Saved projects can still be kept unchanged. Only trailing planned MicroCal shots without stream markers are omitted, before bookkeeping and tandem-segment creation; a missing marker before a later shot rejects the file.
- Verification: 44 focused Core import tests pass, including independent concentration expectations and reader→validation→processing→objective checks, the truncated-leading-trace regression, ordinary stopped-run data, batch continuation, saved-project reopening, and complete/concatenated controls. Avalonia preference/dialog tests: 31 passed. Native validation and tandem-import modal checks and preferences storyboard layout: passed. Full Core suite: 4,447 passed, 1 skipped (4,448 total); the final focused import set also passed after adding the last missing-marker cases.
- Boundary: Missing-heat fitting support and ITC-107 planned-count metadata remain outside this fix. The obsolete broad deletion preference was retired in both desktops.

## CR-016 — Tandem reconstruction changes the buffer correction used during scanning

- Priority: Medium
- Status: Resolved (2026-10-17) by a temporary restriction on supported workflows.
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/TandemMixingScan.cs:759`; `AnalysisITC.Core/TandemConcatenationTool.cs:222`; `AnalysisITC.Core/DataClasses/BufferSubtraction.cs:330`
- Related: ITC-060, ITC-062, ITC-067
- Problem: The scan uses each source's corrected heats, but the concatenated experiment retains only the first source's correction attributes and applies them using cumulative injection IDs.
- Failure scenario: Merge two 20-injection segments using a blank whose injection 2 heat is 2 µJ and final heat is 5 µJ. A second-segment raw heat of 10 µJ is scanned as 8 µJ, but its merged number 22 clamps to blank injection 20 and becomes 5 µJ.
- Consequence: The produced corrected observations differ from those used to select automatic back-mixing. Later segments' different blank references or correction methods are also lost; a changed optimum was not measured.
- Resolution (2026-10-17): Core rejects new or changed buffer subtraction on tandem targets, including loaded-reference, stored-reference, and attribute-copy paths. Copy-attributes actions reject the complete batch with a handled warning before changing any target. All tandem merge modes and scan entry points reject source experiments with a buffer-subtraction attribute, even when its reference is missing. Both desktop apps exclude tandem subtraction targets, prevent adding or editing the correction in Details, and disable tandem creation with a shared explanation when a selected source has buffer subtraction. Corrections are never silently discarded. Unchanged saved tandem corrections remain readable and removable; this compatibility handling does not enable new corrections. The manual and in-app help describe both restrictions.
- Validation (2026-10-17): The full Core suite passes with 3,921 passed and one existing opt-in skip, including 57 new restriction and compatibility cases. Coverage includes every merge/scan entry point, first and later corrected sources, loaded/stored/missing references, all eight copy-attributes actions rejecting before mutation, allowed ordinary correction and metadata copies, and strict native-project round trips retaining removable saved tandem corrections. All 76 focused Avalonia restriction, merger, buffer-preview, Details, and selection-menu cases pass. Native managed compilation and the restriction, tandem-creation, Details-draft (18 cases), and Details-value (36 cases) harnesses pass. Desktop validation used automated control/action harnesses; an installed-app manual walkthrough was not performed.
- Follow-up: Supporting buffer subtraction for tandem experiments remains deferred until segment-specific reference sources, methods, and injection mappings have a scientific design and validation.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataClasses/BufferSubtractionPolicy.cs:8`; `AnalysisITC.Core/TandemConcatenationTool.cs:183`; validation=source; tests/UI=not-run. Accepted restriction blocks new tandem corrections and corrected merge sources in Core and both desktops; unchanged saved corrections remain readable/removable. Segment-specific support remains deferred.

## CR-017 — The viewer strips meaning from typed experimental conditions

- Status: Resolved (2026-10-18).
- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Core/Viewer/ViewerDocumentReader.cs:1247`; `AnalysisITC.Web/wwwroot/app.js:1307`; `AnalysisITC.Core/Analysis2/ExperimentAttribute.cs:342`
- Related: ITC-027, ITC-042, ITC-089
- Problem: Viewer metadata selects one raw attribute field instead of formatting the typed condition, losing identities, units, pH and species locations.
- Failure scenario: Open a project containing 20 mM HEPES at pH 7.4 and 150 mM NaCl in the viewer. It displays Buffer: 0.02 and Salt: 0.15.
- Consequence: Readers cannot reliably identify or interpret the conditions. Native serialization retains the evidence; this is presentation loss rather than corruption of the project file.
- Suggested direction: Provide typed viewer presentation with document-local reference context, avoiding desktop formatters that implicitly resolve references through global DataManager state.
- Resolution: A viewer-specific Core formatter handles the seven available experiment attributes, preserving chemical identities, units, pH, species locations, repeated conditions, custom labels, and explicit missing-reference states. Reference names resolve only within the uploaded document. Competitor metadata identifies captures saved on the experiment and shares the desktop Kd/ΔH composition; it never substitutes newly evaluated source values or claims that these captures belong to a particular saved fit. Numeric output uses explicit invariant G6 formatting with saved SD/95% bounds, independent of server precision settings. Existing desktop formatting and the label/value web contract are unchanged; no JavaScript or serialization change was needed. Added the confirmed viewer-manual note about experiment captures.
- Validation: The pre-fix in-memory `.ftxtc` regression failed with `0.02` instead of `20 mM Hepes pH 7.4`. Focused Core tests: 29 passed; full Core suite: 3,938 passed, one existing skip; viewer upload tests: 21 passed. Tests cover phosphate/salt markup, ionic strength from `ParameterValue`, units, zero/blank/unavailable rules, stored reference snapshots, distinct source versus captured values, matching-ID decoys in both `DataManager.Data` and `DataManager.Results`, consecutive document isolation, and invariant output under da-DK/non-default precision. Desktop competitor output retains its current-culture SD-only presentation. Live loopback browser QA confirmed wrapped long names, both species locations, Unicode, and literal HTML-like annotations without markup interpretation.
- Review follow-up (2026-10-19): Confirmed a reader-reachable legacy regression using constructed `.ftitc` experiment attributes: `NumberOfSites1/2` are declared Int but canonically store their decimal values in `DoubleValue`. Six cases failed before the correction. The viewer now reads `DoubleValue` for these two known storage exceptions, retaining invariant G6 formatting; other legacy fallback behavior is unchanged. The six reader-level regressions cover both keys, fractions, and decimal zero despite a conflicting integer field, and pass under da-DK. No occurrence was found in the two checked-in `.ftitc` fixtures; historical occurrence remains unconfirmed. Focused viewer checks: 35 passed; full Core suite: 4,312 passed, one existing skip. No scientific-method, serialization, desktop, or JavaScript changes.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Viewer/ViewerDocumentReader.cs:1240`; `AnalysisITC.Core/Viewer/ViewerAttributePresentation.cs:19`; validation=source; tests/UI=not-run. Typed document-local formatting retains identities, units, pH, captured inputs and missing states; legacy decimal site-count exceptions are covered without using global desktop data.

## CR-018 — Quick clipboard exports misalign scientific table columns

- Status: Resolved (2026-10-17).
- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataExport/Exporter.cs:666`; `AnalysisITC.Core/DataExport/Exporter.cs:725`; `AnalysisITC.Core/DataExport/Exporter.cs:776`; `AnalysisITC.MacOS/PopOptionViewControllers/BindingAnalysisViewController.cs:283`
- Related: ITC-015, ITC-017, ITC-094
- Problem: The quick-copy path declares value/SD pairs but splits display strings on ± and does not quote CSV fields.
- Failure scenario: Copy a binding result with fixed N, missing uncertainty or uncertainty display set to None. One field appears under a two-field header; confidence intervals or comma-containing names can add fields instead.
- Consequence: Subsequent values and assessments appear under incorrect spreadsheet headings. Both desktops' result-details actions reach this path, although native Results also offers a newer safe exporter.
- Suggested direction: Build explicit fixed-width scientific cells and quote every CSV field, preferably reusing the structured result-table exporter.

- Resolution (2026-10-17): Both desktops' quick-copy actions now use the structured result-table exporter with experiment rows, TSV and separate scientific columns. The uncertainty display preference determines value-only, SD, CI or SD + CI columns; Automatic retains the exporter's SD + CI behavior. Quick copy no longer appends a combined summary row; that remains available in the configurable exporter. Existing explicit energy, concentration and temperature arguments are retained, and per-member assessment/output policy is shared with the exporter. Scientific cells are built independently of formatted display strings, with delimiter-aware escaping and invariant decimal points. The manual and shared help describe the revised quick-copy output. CR-019 remains separate.
- Validation: All 12 clipboard regression cases passed, covering every uncertainty preference, fixed/missing values, mixed parameter availability, delimiter-aware CSV/TSV parsing, embedded tabs/quotes/newlines/±, decimal-comma culture, and explicit units/Kelvin. The broader 90-test Core selection passed before the final missing-uncertainty case was added; that case passed in the final 12-test run and full suite. All seven Avalonia selection/menu checks passed, including menu and details-button copy output. Native managed compilation and the result-details action passed with real macOS pasteboard readback. Full Core suite: 3,855 passed, one skipped, four failed: `ThermodynamicSummaryConsistencyTests.ThermodynamicSummariesAgreeAcrossCacheDisplayReportAndExport` (unavailable uncertainty), two `ProjectLoadingProgressTests` cases (interactive rendering and cancellation), and `StatusBarManagerTests.SuccessfulFileMessageStopsProgressWithoutSuppressingDocumentStatus`. These failures were left outside CR-018; no baseline comparison was performed. Evidence: `/private/tmp/cr018-clipboard-final.log`, `/private/tmp/cr018-core-focused-final.log`, `/private/tmp/cr018-core-full.log`, `/private/tmp/cr018-avalonia-final.log`, `/private/tmp/cr018-native-final.log`. External spreadsheet-application pasting was not exercised; table structure was verified by parsing and clipboard readback. No packaging or release steps.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataExport/Exporter.cs:489`; `AnalysisITC.Core.Tests/ResultClipboardTests.cs:20`; validation=source; tests/UI=not-run. Both clients' quick copy uses structured TSV/separate columns with uncertainty-style-specific widths; escaping/invariant-number tests and native pasteboard source coverage remain.

## CR-019 — Mixed result exports use one label for stoichiometry and syringe activity

- Status: Resolved (2026-10-18).
- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:694`; `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:744`; `AnalysisITC.Core/Analysis2/Models/OneSetOfSites.cs:61`
- Related: ITC-030, ITC-051, ITC-082
- Problem: A shared Nvalue1 column takes its N or α label from the first result, although later rows can use the coordinate for the other scientific quantity.
- Failure scenario: Select ordinary One-Site and syringe-corrected One-Site results together, then export Summary or All Rows.
- Consequence: Some values are labeled as stoichiometry when they represent syringe active fraction, or vice versa, with no per-row option field distinguishing them. Homogeneous exports remain correctly labeled.
- Suggested direction: Preserve per-result parameter meaning through distinct columns or explicit semantic metadata.

- Resolution (2026-10-18): The shared Core table exporter distinguishes fitted stoichiometry (`N`/`N1`, `N2`), syringe activity (`α`), and saved fixed stoichiometry. First-site N headers are numbered whenever an exportable Two-Site result contributes. Columns follow the first-site N → α → fixed-N order; the second-site N/fixed-N group precedes Kd2 regardless of selection order. Corrected rows leave fitted-N cells blank. Every corrected export, including quick copy, includes scalar saved fixed inputs (`_value` only in Separate columns), formatted as invariant G5 numbers; missing saved options remain blank. Suppressed binding rows leave α and fixed inputs blank, and suppressed corrected results cannot introduce their columns. Ordinary-only output remains unchanged. The export manual links to the existing syringe-correction explanation. PDF reports are unchanged, so `ANALYSIS_REPORT_MAP.md` requires no update.
- Validation: Before repository edits, the full Core baseline passed (3,940 passed, one skipped, zero failures); none of CR-018's four recorded failures reproduced. The new regression selection failed 247 of 248 cases against the unchanged exporter, with the ordinary-output baseline check passing. All 248 CR-019 cases now pass, covering the six approved mixed selections in both orders, both row modes, CSV/TSV, both error layouts, every uncertainty style, homogeneous corrected exports, suppression, missing saved inputs, invariant G5 values, and single-result quick copy. SHA-256 baselines confirm unchanged ordinary One-Site, Two-Site, Competitive and mixed ordinary tables across layouts, plus ordinary quick copy (platform line endings normalized for portability). The post-change full Core run reported 4,220 passed, one skipped and three failures in concurrently edited tests: `ReportInconclusiveReferencesSurviveAppSentenceDeduplication` (unsupported assessment override) and both `SaltAnalysisTests.ReopenPreservesSavedComponentsWhenNewEligibilityFails` cases (fixture reopening failed in model initialization). These were absent from the clean baseline and did not involve the exporter. Their source files were updated independently during the run; the final focused run against the normal Core test project passed all 308 selected tests, including those three cases and all export/clipboard regressions. The full suite was not rerun after those concurrent test updates. All 14 relevant Avalonia headless menu/export-policy/unit-selector checks passed. Native managed compilation and the result-details Copy action passed with real macOS pasteboard readback; no interactive desktop workflow or spreadsheet pasting was exercised. Evidence: `/private/tmp/cr019-baseline-tests.log`, `/private/tmp/cr019-red.log`, `/private/tmp/cr019-focused.log`, `/private/tmp/cr019-core-full.log`, `/private/tmp/cr019-final-focused.log`, `/private/tmp/cr019-avalonia.log`, `/private/tmp/cr019-native.log`. No packaging, version changes or release steps were used for implementation validation.
- Commit boundary: CR-018 clipboard integration and CR-020 salt-analysis changes remain separate. This commit tests the configured exporter against the committed base; the working-tree integration tests additionally cover CR-018 quick copy and its value-only layout.
- Commit-snapshot validation (2026-10-19): On an isolated committed-base snapshot containing only CR-019, all 248 configured-export regressions pass and the full Core suite passes (4,001 passed, one skipped, zero failures). Ordinary table baselines were captured before applying CR-019 to that base. This commit retains the base's automatic concentration units and uncertainty-layout behavior; CR-018-dependent quick-copy integration checks remain in the working tree. Evidence: `/private/tmp/cr019-commit-baseline.log`, `/private/tmp/cr019-commit-focused.log`, `/private/tmp/cr019-commit-core-full.log`, `/private/tmp/cr019-commit-results/cr019-commit-full.trx`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:217`; validation=source; tests/UI=not-run. Per-saved-solution semantic columns distinguish fitted N, activity and fixed stoichiometry; configured export and quick copy share the path. Mixed-selection and suppression regressions remain.

## CR-020 — Unavailable salt-analysis results are displayed as scientific zeros

- Status: Resolved (2026-10-18).
- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ElectrostaticsAnalysis.cs:70`; `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs:1334`; `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs:1095`
- Related: ITC-036, ITC-051
- Problem: Salt analysis can mark itself Calculated with unavailable components, while both desktop output views test only that overall flag. Formatting loses the FloatWithError missing-value marker.
- Failure scenario: Analyze a one-site result with two valid affinities at 50 and 150 mM NaCl. The eligibility checks pass, but the component calculations require more data and return unavailable values.
- Consequence: Desktop outputs show zero Kd0 and zero counter-ion result rather than unavailable. Reports already guard component availability; partially available results have the same desktop risk.
- Suggested direction: Use shared per-component availability in both desktop views and align run eligibility with the calculations' data requirements.

- Resolution (2026-10-18): Core eligibility now greedily selects three usable ionic strengths, each strictly more than half the configured minimum span above the last accepted value. The span and buffer-inclusion policy are captured when the result is constructed/updated/reopened, and run admission uses that stored eligibility. Counter-ion calculation requires three distinct positive activities and retains replicates. Both desktops, reports, viewer DTOs and interpretation evidence use shared per-component availability; failed/missing fits cannot become formatted zeros, and partial async runs retain their valid component. Desktop Kd0 labels and preference labels/tooltips are aligned, with distinct wrapped eligibility reasons. Strict restoration bypasses only the condition/spacing requirement, retaining model/salt-metadata checks; saved values remain in report/viewer/interpretation/save consumers while desktop Salt and coupled columns remain hidden when ineligible. The general missing-marker formatting defect is deferred as ITC-122.
- Validation: Final full Core suite: 4,229 passed, one existing external-reference diagnostic skipped. Final focused salt/report/interpretation checks: 212 passed; 39 focused Avalonia salt, availability and preference checks passed. Native salt presentation, eight salt graph cases and Protonation availability/layout checks passed. Full Web suite: 445 passed. All 19 browser JavaScript checks passed, covering preserved complete/partial outputs despite failed current spacing, missing values and valid zero. Native unified-help checks also passed. Documentation/report map and ITC-051's conditional ionic-strength retention were updated. No schema, versions, release metadata, packaging or commits changed.

- Reopen follow-up (2026-10-19): Completed electrostatics analyses now retain their buffer-inclusion policy alongside the coefficients, uncertainties, and completion metadata. Desktop, report, and viewer ionic-strength observations use that completed policy; result columns and future runs retain their independently captured preferences. Failed/cancelled replacement runs roll back both fit and policy. The optional current-schema field `includeBufferInIonicStrengthCalc` is required for restoring a complete saved attempt, including a present `false`. Missing policy or required component estimates quietly omit only the optional electrostatics object in both read modes; null components remain valid partial results. Model, salt-metadata, malformed-value, and schema checks remain. No versions or migrations added.

- Reopen validation (2026-10-19): Full Core suite: 4,299 passed, one existing external-reference diagnostic skipped, zero failures. All 69 focused salt/persistence/schema/interpretation checks, 44 focused Avalonia checks, native salt presentation and 10 salt graph cases, five native linear-envelope cases, protonation availability/layout, unified help, and all 19 browser JavaScript checks passed. Tests cover both saved/run policy directions, unchanged saved estimates/uncertainties/curves, successful replacement, failure/cancellation rollback, complete/partial attempts (including false), quiet incomplete-object omission in both read modes, independent parent/Spolar Record/protonation preservation, re-save omission and recalculation. Full Web suite: 444 passed, one unrelated failure in `CompleteStoredPackagePassesStrictLocalContractWithHistoricalEvidence(compact: true)` (expected HTTP 413, received 503); the same failure was reproduced with the pre-change Core library in an isolated temporary test copy. Evidence: `/private/tmp/cr020-reopen-core-full.log`, `/private/tmp/cr020-reopen-core-focused-final.log`, `/private/tmp/cr020-reopen-avalonia-final.log`, `/private/tmp/cr020-reopen-native.log`, `/private/tmp/cr020-reopen-js.log`, `/private/tmp/cr020-reopen-web.log`, and `/private/tmp/cr020-reopen-web-baseline.log`. No packaging, versions, release metadata, commits, or pushes changed.

- Independent commit validation (2026-10-19): The isolated staged candidate based on `9d4ec0c8` passes the full Core suite (4,105 passed, one existing external-reference diagnostic skipped, zero failures), all 12 focused Avalonia salt tests, both native saved-policy graph cases, native salt output/restore presentation checks, all five native linear-envelope cases, and both browser salt tests. All 47 working-tree salt tests also pass with the uncommitted CR-018 clipboard implementation. The commit preserves HEAD's clipboard format and changes its ionic-strength calculation to the existing captured-policy helper; regression tests recognize both CSV/TSV header forms and independently check both buffer-policy directions, separation from the completed plotting policy, and preference recapture on update. The CR-018 rewrite and CR-068 point-grouping changes remain outside this commit. The known unrelated Web admission/accounting failure is not changed or re-tested here. Evidence: `/private/tmp/cr020-final/core-candidate-full.log`, `/private/tmp/cr020-final/core-working-tree-salt.log`, `/private/tmp/cr020-final/avalonia.log`, `/private/tmp/cr020-final/native.log`, and `/private/tmp/cr020-final/js.log`. No packaging, versions, release metadata, or pushes changed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Analysis2/AdvancedAnalysis/ElectrostaticsAnalysis.cs:28`; `AnalysisITC.Core/DataExport/FTXTCFormat.cs:2007`; validation=source; tests/UI=not-run. Shared component gates protect desktop/report/viewer/interpretation output; saved plotting policy is retained separately from run eligibility. Incomplete optional restoration follows accepted policy; ITC-122 remains separate.

## CR-021 — Missing buffer thermochemistry is treated as an exact zero enthalpy

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Status: Fixed locally on 16 October 2026.
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs:38`; `AnalysisITC.Core/DataClasses/Buffers.cs:538`; `AnalysisITC.Core/DataClasses/AnalysisResult.cs:523`; `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ProtonationAnalysis.cs:22`
- Related: ITC-003, ITC-027
- Problem: Buffer definitions lacking thermochemical data receive a zero enthalpy curve, and protonation analysis accepts them based on buffer identity alone.
- Failure scenario: Use MES and the selectable Succinate buffer for two experiments at 25 °C. MES supplies 14,800 J/mol, while Succinate supplies an unsupported zero; differing measured binding enthalpies then produce a finite protonation regression.
- Consequence: Protonation and intrinsic binding enthalpy can be reported from invented thermochemical evidence. The current proton-sign convention is not challenged by this finding.
- Suggested direction: Represent unavailable thermochemistry explicitly and require usable coordinates, keeping genuine zero enthalpies distinct from missing data.
- Resolution: Missing buffer curves and JSON registry coefficients remain unavailable; explicitly supplied zero values remain usable. Checked lookups reject unknown buffers and nonfinite values. Shared Core eligibility requires usable thermochemistry for every member and at least two distinct finite buffer-enthalpy coordinates, without filtering the collection. Both desktop apps omit the unavailable Protonation view and show the same wrapped reason, naming affected buffers and experiments. Figure metadata omit missing enthalpies, tables/exports retain their checked availability, and enthalpy sorting places unavailable values after known values. All buffers remain selectable. Manual, in-app help and report-map eligibility are aligned.
- Validation: All 35 focused Core buffer/scientific-reference/advanced-analysis checks and 12 Avalonia availability/correlation checks pass; the final two Avalonia availability cases also pass after updating the image encoder call. The full Core suite completed with 3,776 passed, one existing external-reference skip, and two failures in `FittingProgressOrderingTests.PassResetBetweenASolverUpdatesNotificationsDiscardsBothOfThem`; both failing cases pass in isolation. Native managed macOS availability and saved-structuring checks pass, including narrow-panel wrapping with long experiment names. Rendered availability explanations from both desktop apps were inspected. Evidence: `/private/tmp/cr021-focused-core.log`, `/private/tmp/cr021-full-core.log`, `/private/tmp/cr021-progress-isolated.log`, `/private/tmp/cr021-avalonia.log`, `/private/tmp/cr021-avalonia-final.log`, `/private/tmp/cr021-native.log`, `/private/tmp/cr021-native-visual.log`, `/private/tmp/cr021-qa/`, and `AnalysisITC.Core.Tests/TestResults/cr021-full-core.trx`.
- Boundaries: No new thermochemical values, scientific constants, sign conventions, subset selection, project-format migration, special historical-result handling, packaging, version, commit or push changes. Validation used the current dirty working tree; unrelated edits were preserved.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataClasses/Buffers.cs:121`; `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ProtonationAnalysisAvailability.cs:13`; validation=source; tests/UI=not-run. Missing thermochemistry fails checked lookup while explicit zero remains valid; all members and distinct coordinates are required without subset filtering. Desktop/output consumers and independent BufferThermochemistryTests agree.

## CR-022 — Imported comment concentrations misread scientific notation

- Priority: Medium
- Status: REJECTED — closed by maintainer decision (2026-10-19).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/UnitClasses/ConcentrationUnit.cs:180`; `AnalysisITC.Core/DataReaders/DataReaders.cs:504`; `AnalysisITC.Core/DataReaders/DataReaders.cs:548`
- Related: ITC-027, ITC-089
- Problem: Comment extraction accepts a decimal prefix and an unknown unit token rather than matching the complete concentration.
- Failure scenario: Import a raw file containing NaCl 1e-1 M in its comment. Extraction reads 1 with unit e, falls back to mM, and stores 0.001 M instead of 0.1 M. The before-name pattern can match the exponent tail instead.
- Consequence: Salt or buffer metadata can be wrong by orders of magnitude or negative, affecting ionic strength, advanced analysis and reported conditions. The separate interactive exponent parser does not protect this path.
- Suggested direction: Parse complete numeric/unit tokens including exponents and boundaries, rejecting partial matches and unknown unit suffixes.

- Recheck: **2026-10-19; OPEN**; evidence=`AnalysisITC.Core/UnitClasses/ConcentrationUnit.cs:180`; `AnalysisITC.Core/DataReaders/DataReaders.cs:569`; validation=source; tests/UI=not-run. Decimal-prefix regexes and unknown-unit→mM fallback remain; buffer caller also ignores parse failure. Ordinary supported raw-comment import.
- Target: full comment token → correct molar metadata.
- Strategy: extend both name-before/name-after patterns for signed exponents and complete numeric/unit boundaries; reuse invariant decimal-comma parsing and explicit unit map; default to mM only when unit is absent. Make buffer extraction honor failure.
- Verify: NaCl 1e-1 M and 1e-1 M NaCl → 0.1 M; micro aliases, mol/L, omitted units, malformed exponents and unknown suffixes; reader-level buffer/salt checks.
- Questions: for a named buffer with absent/invalid concentration, omit it or retain identity with an unavailable concentration? Preserve existing no-concentration behavior until decided.

- Resolution (2026-10-19, maintainer decision): **REJECTED; closed.** Writing scientific notation in imported concentration comments is user error, outside the supported input convention. The reported parsing behavior does not require a product fix under that policy. The preceding recheck, proposed strategy and questions are retained as history and withdrawn from active work. No parser, test or documentation change is required by this closure.

## CR-023 — Oversized report blocks lose content instead of continuing on another page

- Priority: Medium
- Confidence: Confirmed
- Status: Implemented (2026-10-18); focused tests and both renderer visual checks passed. Full-suite limitations below.
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:471`; `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:479`; `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:625`; `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1401`
- Related: ITC-021, ITC-022, ITC-040
- Problem: Notices are treated as indivisible fragments whose height is clamped to the page. A single oversized key-value row is also forced onto the page without allocating its full height.
- Failure scenario: A 24-experiment series retaining three warning categories per member produces at least 72 warning lines, exceeding the default 738.85-point printable height. Separately, a Traceability Mode report with 70 results puts all identifiers into one oversized value.
- Consequence: Both renderers clip the warning inventory's tail or draw identifiers through the footer/beyond the page, without continuation. Individual chapters or saved identifiers retain the underlying evidence; no PDF was generated during verification.
- Suggested direction: Provide continuation for content taller than a page, or represent long identifier lists as independently pageable rows. Never treat an oversized row as fitted while allocating only its heading.

- Implementation: Warning diagnostics retain in-memory category, member index/label, method and result context through chapter copying. P03 groups that captured inventory using method-specific sentences, all-member denominators and ordered/ranged report references. R03 consumes structured shared health reasons; app text/deduplication stays unchanged (same-name inconclusive app lines are tracked as ITC-121). Trace-only P05 replaces P04's oversized identifier value with independent rows. Notices continue at wrapped lines; key-value rows use shared prewrapped layouts and exceptionally tall rows use line slices, including continuation within row zero. Both renderers draw fragment notice titles and match measured geometry.
- Validation: Final focused Core report/assessment checks: 310 passed, including exact-height page boundaries. Full Core run: 4,221 passed, one skipped, two failures in `ProjectLoadingProgressTests` (interactive loading-status delivery/cancellation; no report layout/builder path). All 77 Avalonia report checks passed; both desktop renderer checks include drawn continuation-title comparisons. Native `AnalysisReportReferencesTests` as invoked by its standard script stops in the existing tandem-buffer-subtraction picker fixture; a temporary copy excluding only its two picker calls passed all remaining checks, including the new continuation fixture. Production picker behavior and the original fixture were not changed.
- Rendered evidence: `/private/tmp/cr023-report-qa/` contains preview PNGs and PDFs from both renderers. Each eight-page continuation PDF retains all 150 warning lines, 150 metadata lines and 70 identifiers exactly once by extraction. Every PDF page was visually inspected, alongside ordinary report output and wrapped source metadata; continuation titles, last lines, subsequent blocks and footer clearance were checked. Report map P03/R03/P04/P05/H03 and brief denominator guidance were updated.

- Review follow-up (2026-10-19): Centralized notice insets, fonts, heading advance, body bounds and line height in Core `AnalysisReportNoticeLayout`; both renderers use its explicit line advance. Each renderer's stress test now compares every notice fragment with an otherwise identical fragment missing its last line, proving that the final line changes rendered pixels. Restored the existing R03 order for single/pooled assessments (fit warnings before inconclusive) and documented the full R03 ordering. Added near-one-line boundary cases and a guard against emitting a zero-line fragment; the reported rounding symptom was not reproduced with the original constants. P03/R03 eligibility stays as agreed; the user chose to keep report pages unchanged for the count difference, so no explanatory sentence was added. Validation: all 404 focused Core report/assessment checks and the full Core suite passed (4,278 passed, one skipped). All 77 Avalonia report checks passed. Native report checks, including every notice fragment's final-line pixels, passed after supplying the existing Mono runtime dependencies and excluding only the same two conflicting tandem-picker calls in a temporary harness; the original harness still stops in that picker fixture. Regenerated eight-page PDFs from both renderers were extracted and visually inspected, with every warning, metadata line and identifier retained. Current evidence is in `/private/tmp/cr023-review-qa-20261019/`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:509`; `AnalysisITC.Core/Presentation/AnalysisReportNoticeLayout.cs:1`; validation=source; tests/UI=not-run. Shared wrapped/sliced fragments reserve actual heights; both renderers use common geometry and last-line pixel regressions. Concurrent CR-077 picker-fixture correction supersedes earlier harness blockage.

## CR-024 — Exported and displayed summary Kd uncertainties disagree

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:122`; `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:424`; `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:377`
- Related: ITC-051
- Problem: The displayed summary derives Kd uncertainty from summarized Gibbs energy, while export transforms each linked bootstrap replicate before calculating dispersion.
- Failure scenario: Export a thermodynamically linked result with differing bootstrap Gibbs energies. At the same evaluation temperature the two paths preserve the same best-fit Kd, but generally calculate different SDs.
- Consequence: CSV/TSV uncertainty disagrees with Results and report summaries for the same result and temperature. The existing linked-uncertainty test pins the export calculation without comparing the display.
- Suggested direction: Route both consumers through one summary calculation, preserving the selected Gibbs-based convention in Planning/SUMMARY_SEM.md:31.
- Resolution (2026-10-17): Summary Kd now always propagates the summarized Gibbs SD and transforms its interval endpoints. Desktop Results/report rows consume the same Core summary evaluator as CSV/TSV and configured summary-table copying; the web viewer follows that convention too. Fitted centers, individual-fit uncertainty, and saved data are unchanged. The broader thermodynamic-summary review found shared numerical paths for ΔH, −TΔS, ΔG, and ΔCp; cross-consumer regression coverage now includes those quantities, local and linked summaries, multiple slots, profile uncertainty, unavailable uncertainty, and cached/uncached evaluation. Manual/help and report-map descriptions document the convention.
- Validation: Final full Core suite passed (3,859 passed, 1 skipped, 0 failed), including all 53 focused uncertainty/summary tests. The initial run’s locale-dependent display/report assertion was corrected to compare under the report’s invariant decimal convention. All 58 focused Avalonia result/report tests and 17 browser evaluation tests passed. Native macOS managed compilation, report references/PDF rendering, and five report figure pairs passed using explicit installed System.Buffers/MathNet.Numerics dependency paths for Mono. These are automated checks; no interactive desktop walkthrough or live web deployment was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:122`; `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:467`; `AnalysisITC.Web/wwwroot/app.js:952`; validation=source; tests/UI=not-run. Results/report/export/viewer follow summarized Gibbs uncertainty with transformed Kd bounds, preserving the settled SUMMARY_SEM convention and original best-fit centers.

## CR-025 — Disabling injection tables removes schemas still used by processing evidence

- Priority: Medium
- Status: Fixed and validated locally (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs:469`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs:515`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs:347`
- Related: ITC-034
- Problem: Injection-table selection removes baseline schemas as well, even when detailed processing remains enabled and its positional tables still reference those schemas.
- Failure scenario: An Advanced or Administrator user retains detailed processing but disables injection tables for a spline or segmented experiment.
- Consequence: The compact interpretation request contains baseline arrays without their declared column definitions. Canonical evidence remains intact; an incorrect generated interpretation was not demonstrated.
- Resolution (2026-10-18): Both desktop dialogs now select cumulative Results, Injection data, Processing details and Thermograms levels through a shared Core policy. Full canonical capture remains local; model projection normalizes the effective option flags and filters current injection/Null points, historical fit snapshots, processing evidence and traces consistently in inline, supporting and shared layouts. Only schemas used by retained tables are emitted. IncludedOnly remains a transmission filter, and selected-level omissions are recorded in generation provenance. Canonical and model boundaries compose trace and No-binding restrictions; ITC-123 records the former overwrite defect. The enum is not serialized, and unpublished v2 development snapshots are intentionally allowed to become stale. Manual/help, relay contract, report map and export README describe the selected transmission level and fuller local archive.
- Validation: Final full Core VSTest suite passed: 4,241 passed, one existing external-reference diagnostic skip, zero failed. Handwritten content/column expectations cover all four levels, source-sharing rewrites, the old processing-on/injections-off combination, historical evidence, No-binding clauses, IncludedOnly, real pre-change v2 canonical/project fixtures and generation provenance. All 81 focused Avalonia interpretation/report tests passed. Native managed compilation, the real AppKit evidence-selection/action harness and the Swift interpretation layout check passed. The separate native AnalysisReportReferencesTests stops in its existing tandem buffer-subtraction picker setup, before report rendering; that unrelated fixture was preserved.

- Review follow-up (2026-10-19): Results payloads now disclose when saved Null comparison points were omitted, including generation provenance. Missing or empty point arrays add no omission, and repeated projection preserves a single disclosure. The No-binding scientific guidance is unchanged. The privacy paragraph now explains the selected transmission level and the full local evidence archive.
- Follow-up validation (2026-10-19): Four regression cases failed before the disclosure fix; all 22 focused Core cases now pass. Full Release Core VSTest: 4,306 passed, one existing external-reference diagnostic skip, zero failed. All 81 focused Avalonia interpretation/report VSTest cases, the native InterpretationEvidenceLevelTests AppKit harness and the Swift interpretation layout check passed on the live checkout. No interactive desktop or live service validation was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs:137`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs:470`; validation=source; tests/UI=not-run. Canonical capture is full; cumulative projection keeps referenced schemas and records actual omissions. No-binding and trace restrictions compose, including saved Null-point omission disclosure.

## CR-026 — Public enrollment notifies native UI subscribers from a worker thread

- Priority: Medium
- Status: Fixed locally on 16 October 2026.
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Interpretation/FtItcInterpretationClient.cs:132`; `AnalysisITC.Core/Properties/AppSettings.cs:248`; `AnalysisITC.MacOS/ViewControllers/SideBarViewController.cs:291`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:261`
- Related: None
- Problem: After asynchronous public enrollment with ConfigureAwait(false), settings persistence synchronously invokes native subscribers that access AppKit without dispatch.
- Failure scenario: Open Generate Interpretation with no saved access code while the native main window is open. The enrollment response resumes on a worker and triggers sidebar and initialized Analysis controls.
- Consequence: The source establishes off-main-thread AppKit access. The report window's own dispatch does not protect the other subscribers; an exact native crash was not reproduced.
- Suggested direction: Dispatch UI-affecting settings notifications through the established main-thread service, or separate credential persistence from those notifications.
- Resolution: All `AppSettings` notification paths now use the existing platform main-thread dispatcher, including public enrollment's ordinary save, standalone settings application, access verification and account persistence. Successful saves deliver `SettingsApplied` followed by `SettingsDidUpdate` in one dispatched callback after synchronization; failed persistence delivers neither. Public event signatures, enrollment transport and credential reuse are unchanged.
- Validation: All 14 new Core settings cases failed before the combined CR-026/CR-028 change and now pass. Coverage includes a deliberately asynchronous enrollment response, queued notifications, order/counts, persisted credential reuse and failed synchronization. The full Release Core suite passes with 3,822 passed and one existing external-reference diagnostic skip. All 65 focused Avalonia settings/workspace/help cases pass. The native managed AppKit settings harness exercises background enrollment, verification and account updates with initialized sidebar and Analysis subscribers and verifies main-thread event delivery; native parameter-draft and help harnesses also pass. Evidence: `/private/tmp/cr026-cr028-before.log`, `/private/tmp/cr026-cr028-tests/`, `/private/tmp/cr026-cr028-avalonia-focused.xml` and `/private/tmp/cr026-cr028-native.log`. No live service requests or installed-application interactive session were used.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Properties/AppSettings.cs:248`; `AnalysisITC.Core/Properties/AppSettings.cs:565`; validation=source; tests/UI=not-run. Successful persistence precedes one main-thread callback delivering Applied then DidUpdate; enrollment and native subscriber test sources cover asynchronous delivery/failure.

## CR-027 — Native interpretation hides permitted evidence choices inside a stale parent

- Priority: Medium
- Status: Fixed and validated locally (2026-10-18).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1502`; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1697`; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1852`
- Related: ITC-034, ITC-066
- Problem: The optional-evidence parent is hidden according to initial thermogram eligibility and is not recomputed with current capabilities.
- Failure scenario: Open the native dialog with verified Standard access. Injection tables are permitted and their checkbox is individually unhidden, but its parent remains hidden.
- Consequence: The user cannot change an inclusion choice still consumed by generation. Capability refresh also retains stale thermogram eligibility; access guards prevent claiming unauthorized generation.
- Resolution (2026-10-18): Replaced both checkbox groups with the cumulative Evidence level selector. Dialog-owned requested state is separate from the access-limited effective state; cached capabilities, refreshed responses and failures recompute permitted items, selected level, parent/heading visibility and enabled state. Recovery restores the requested level unless the user changed it. Thermograms resets once on opening and retains an explicit opt-in through capability refresh within that opening. Standard native access now exposes the selector, and opening/refreshing does not save a downgrade.
- Validation: All 81 focused Avalonia interpretation/report VSTest cases passed. Native managed compilation and the AppKit harness passed none-to-Standard-to-Advanced visibility/restoration, failure/recovery, fresh Thermograms opt-in, explicit user changes, IncludedOnly preservation, unchanged saved report state and retained Generate/Save package/Cancel actions. The Swift layout check passed. Shared selection tests also passed in the full Core suite (4,241 passed, one skipped, zero failed).

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Interpretation/AnalysisInterpretationEvidencePolicy.cs:91`; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1931`; validation=source; tests/UI=not-run. Requested/effective evidence choices remain separate; capability refresh updates parent visibility, selection and enabled state on both desktops without saving downgrades; Thermograms opt-in resets on opening.

## CR-028 — Saving unrelated settings resets the live fitting configuration

- Priority: Medium
- Status: Fixed locally on 16 October 2026.
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Properties/AppSettings.cs:248`; `AnalysisITC.Core/Properties/AppSettings.cs:554`; `AnalysisITC.Core/DataExport/ProjectWriter.cs:156`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:287`
- Related: None
- Problem: AppSettings.Save also reapplies fitting preference defaults to the live analysis controller.
- Failure scenario: In native macOS, fit with nondefault uncertainty settings, use Save As, then leave and reopen Analysis. Saving LastDocumentPath invokes Save; view reentry reloads the reset options.
- Consequence: The next fit uses preference defaults instead of the preceding analysis configuration. Completed fits are unchanged; an immediate fit without leaving the view can retain the visible choices.
- Suggested direction: Separate persistence of unrelated settings from deliberate application of fitting defaults.
- Resolution: General settings application and persistence preserve the live inspector's solver, uncertainty method, bootstrap count, concentration-uncertainty, weighting and parameter-unlocking choices. This includes Preferences Apply when fitting defaults change. Settings loading explicitly initializes those options through the existing `ResetToPreferenceDefaults` method; the existing Analysis restore-defaults action also loads the saved defaults and clears parameter unlocking. Display/processing application remains active. The manual and in-app help explain the chosen startup/reset policy; optimizer tolerance, maximum iterations, parameter-limit policy and project formats are unchanged.
- Validation: The Core regressions cover ordinary saves, both document-path setters, export preferences, direct settings application, unchanged/changed Preferences Apply, saved-default loading and explicit reset. Both desktop harnesses perform real Save As operations, reopen/resynchronize Analysis controls, launch subsequent fits and verify their retained optimizer, uncertainty method, iteration count and weighting. They also check the existing reset flow; native display application remains verified. Full Core, focused Avalonia and native results are recorded under CR-026 above.
- Separate observation: Native reset with a preference count of 500 displayed 499 despite a live option of 500. This pre-existing logarithmic-slider roundoff is tracked without a fix as ITC-116 in `Planning/ISSUES.md`, with evidence in `/private/tmp/cr026-cr028-native-rounding.log`. The final native reset case uses the exactly represented count 1,000. Session fitting choices are not newly persisted across restarts. No packaging, signing, release, commits or pushes were performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Properties/AppSettings.cs:559`; `AnalysisITC.Core/Analysis2/Solver2.cs:56`; validation=source; tests/UI=not-run. Ordinary saves/Preferences Apply retain live fitting choices; only settings load and explicit reset restore defaults. Native slider rounding remains separate ITC-116.

## CR-029 — Competitive apparent Kd omits fitted stoichiometry uncertainty

- Priority: Medium
- Status: Deferred to feature planning under ITC-117 (2026-10-17); not fixed.
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:564`; `AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:586`; `AnalysisITC.Core/Analysis2/Models/ThermodynamicSolution.cs:50`
- Related: ITC-117
- Problem: The finite-depletion calculation reads N.Value, making free competitor a scalar and discarding N uncertainty before calculating apparent Kd.
- Failure scenario: Use ordinary stoichiometry mode with nonzero competitor, exact competitor inputs, locked intrinsic affinity and bootstrap-varying N. At 10 µM cell and competitor concentrations and competitor Ka = 10^6 M⁻¹, N values 0.9 and 1.1 produce different competition factors, yet reported apparent Kd has zero SD.
- Consequence: A derived affinity that varies across the retained fitted samples is presented without estimated uncertainty. Zero competitor and syringe-correction mode avoid this particular dependence.
- Suggested direction: Include the N dependence and relevant joint uncertainty in apparent-Kd propagation, using retained samples or an explicit propagation rule while preserving the original best-fit center.
- Feature follow-up: [ITC-117 — Derived-parameter uncertainty across all models](ITC-117_DERIVED_PARAMETER_UNCERTAINTY_PLAN.md) supersedes the narrow competitive-only proposal. It must consider all derived parameters across all models and explicitly design how profile-likelihood errors apply to derived quantities. Registration does not select a propagation/fallback policy or resolve this finding.

- Recheck: **2026-10-19; DEFERRED**; evidence=`AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:571`; validation=source; tests/UI=not-run. ITC-117 owns the broader all-model derived-uncertainty design; N.Value still discards this dependence.
- Depends: [ITC-117](ITC-117_DERIVED_PARAMETER_UNCERTAINTY_PLAN.md). Existing questions: joint retained-refit propagation; monotonic versus multi-coordinate profile treatment; external-input uncertainty, incomplete evidence and persistence. Preserve original best-fit centers, zero-error semantics and CR-024's Gibbs-based summary convention. No isolated competitive patch.

## CR-030 — Clearing a fit leaves its cached Null assessment visible

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:349`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:230`; `AnalysisITC.Core/DataClasses/ExperimentData.cs:811`
- Related: ITC-029, ITC-030, ITC-084
- Problem: Both desktops refresh the fitted-parameter summary after solution removal without clearing their separate Null-comparison cache.
- Failure scenario: Select an experiment with a saved Null comparison and invoke Clear Solution while keeping it selected.
- Consequence: Old Null RMSD, ΔAICc, conclusion and optional prediction can remain beside the cleared fit. Selection away/back or refitting repairs the display; saved scientific state is not corrupted.
- Suggested direction: Synchronize Null evidence on every owned solution change using the same UI-dispatch and selection protections as the fit summary.
- Resolution (2026-10-18): Avalonia's shared solution, processing, and point-inclusion refresh now rebuilds its analysis context, then refreshes only the fit summary while fitting or refreshes selected-experiment evidence otherwise, before fitting graph axes. Its existing fit-completion evidence refresh remains in place. macOS now handles owned solution notifications inline on the main thread or dispatches them there from workers, checks that the sender is both the subscribed summary experiment and current selection, synchronizes Null evidence through the existing fitting guard, and refreshes the fit summary on the main thread.
- Validation: Seeded saved-comparison regressions failed before the fix on both platforms: Avalonia retained its graph Null comparison after `RemoveModel` for both main-thread and worker notifications, and the native controller retained its cached Null comparison with the original handler. After the fix, Avalonia `AnalysisWorkspaceControlTests` passed 51/51, including selected/restored evidence, global-parent member removal, replacement with and without comparison, prediction preference/data restoration, worker suppression while fitting, and shared processing/inclusion refresh; `IntegratedHeatsGraphControlTests` passed 22/22. Native `AnalysisNullEvidenceTests` and `AnalysisNullGraphTests` passed. The native fixture verifies rendered values and cleared states, main-thread and worker removal, same-selection replacement, global-parent member removal, fitting suppression, and delayed callback selection ownership.
- Boundaries: No Core scientific behavior, serialization, documentation/help, or saved state changed. The shared Avalonia callback intentionally covers solution, processing, and inclusion notifications. The separate macOS `OnAnalysisFinished` active-solver evidence assignment and its cross-selection behavior were not changed or runtime-tested; it remains outside this fix.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:347`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:347`; validation=source; tests/UI=not-run. Owned solution changes refresh Null evidence with selection/dispatch/fitting guards. Original cleared-fit cache failure addressed; unrelated native solver-completion cross-selection remains outside accepted scope.

## CR-031 — Avalonia Final Figure PDFs have the wrong physical dimensions

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/PublicationFigure.cs:64`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:104`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:579`
- Related: ITC-012
- Problem: The PDF writer treats preview layout units based on 113.5 units per inch as 72-point PDF units, without a conversion.
- Failure scenario: Directly export a Final Figure whose plot width is set to 6 cm. The width becomes approximately 268.11 PDF points, corresponding to 9.4583 cm.
- Consequence: Exported physical dimensions are enlarged by 113.5/72. Printer fit-to-page behavior is separate and may change eventual printed size; no PDF was generated during this review.
- Suggested direction: Convert both page dimensions and drawing coordinates consistently from the preview scale to PDF points.
- Resolution (2026-10-18): Final Figure PDF output uniformly scales both page dimensions and the drawing canvas by `(72 / 2.54) / document.Options.PointsPerCentimeter`, matching the native macOS writer's fixed `CGGraph.PPcm` conversion at the default density. Preview sizing, drawing/layout rules, fit-only half-height, metadata, selection, filenames, overwrite handling, and stream ownership are retained. The print payload's recorded PDF size is corrected for consistency; no backend reads that property. Avalonia's base-font wording and the shared Core line-width tooltip no longer claim PDF point units, and the manual now identifies plot-area dimensions and removes the line-width point claim. `Resources/HelpTextResource.txt` has no corresponding claim and is unchanged by this fix.
- Validation: Before the fix, 12 of 13 new checks failed; all 13 now pass. Both file and stream exports give a 6 cm plot width of 170.078740 pt, with composite heights of 283.464567 pt for complete/thermogram-only figures and 141.732283 pt for fit-only figures. Geometry tolerance is 0.01 pt; page-box tolerance is 1 pt for Skia rounding. A test-only reader inflates page content with `ZLibStream` and measures transformed frame paths through concatenated and saved/restored graphics states, accepting rectangles or equivalent line paths. One additional case covers a nondefault density reachable only internally. Metadata, vector output, stream retention, and bitmap pixels/dimensions also pass. All 85 focused checks pass; the full Avalonia suite passes with 708 passed and one existing skip. An initial full run captured a transient CR-040 forced-failure probe from concurrent work; a fresh build after its removal passed. The full Core suite passes with 4,241 passed and one existing skip; results are in `/private/tmp/cr031-core-results/cr031-core.trx`. `git diff --check` passes.
- Visual evidence: Original and corrected Binding, Null, None, and Null print PDFs were rasterized with pdfplumber/PDFium to equal 1000 px widths and inspected for proportions, annotations, labels, and clipping. The 6.5 cm fixture's plot width changes from 290.452760 to 184.251964 pt; its preview PNG pixels remain identical for all three model selections. Measured base-font, axis-title, and parameter-box text sizes are 8.88106, 9.51542, and 7.61233 pt, respectively. Comparison images and measurements are in `/private/tmp/cr031-visual/`; test evidence is in `/private/tmp/cr031-baseline-tests.xml`, `/private/tmp/cr031-fixed-dimensions.xml`, `/private/tmp/cr031-focused.xml`, and `/private/tmp/cr031-avalonia-final.xml`.
- Printing and resolution: Linux CUPS composition with configured A4 media and Portrait/Landscape preserves the corrected vector PDF byte-for-byte; Save as PDF still writes `payload.Pdf` directly (source verification). macOS retains PDFKit scale-to-fit; Windows retains bitmap fit-to-page. The unchanged print-bitmap width calculation uses preview width divided by 72 times 300, bounded to 1200–6000 px, corresponding to about 473 ppi at corrected physical size before those limits. It was intentionally not reduced to 300 ppi.
- Boundaries: Core changes are limited to the shared tooltip. Avalonia's tooltip is exercised in a headless control test; both native tooltip assignments were verified against the shared constant. No native macOS rendering, Supporting Figure, Analysis Report, scientific calculation, project serialization, or printer settings were changed. Physical printing, native PDFKit print output, Windows printing, and the Linux Save as PDF dialog were not runtime-tested.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:74`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:127`; validation=source; tests/UI=not-run. Page bounds and canvas scale together to 72-point PDF units; file/stream dimension tests use independent geometry. Native renderer and print-backend limits retained.

## CR-032 — Supporting Figure size controls are capped by captured Final Figure dimensions

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/Drawing/SkiaFigureCanvasRenderer.cs:80`; `AnalysisITC.Avalonia/Misc/Tools/SupportingFigureCanvasWindow.cs:499`; `AnalysisITC.MacOS/Drawing/CoreGraphicsFigureCanvasRenderer.cs:122`
- Related: None
- Problem: Avalonia takes the smaller of the Supporting Figure dimensions and its captured Final Figure dimensions, contrary to the documented size-control precedence.
- Failure scenario: Open Supporting Figure with a 6 × 10 cm appearance snapshot and request a valid 10 × 12 cm plot.
- Consequence: Preview and export use 6 × 10 cm; increasing the controls cannot produce the requested size. The actual-size readout shows the capped value, so it is not itself misleading.
- Suggested direction: Use the validated explicit canvas dimensions, as the native renderer does.
- Resolution: Avalonia's canvas renderer now takes plot width and height directly from the validated Supporting Figure options, matching macOS. Captured Final Figure dimensions no longer cap preview or PDF output. Report overview dimensions remain controlled by the report builder, which supplies matching canvas and figure dimensions. The manual already states that Supporting Figure size controls take precedence.
- Validation: Before the fix, four of six focused regression cases failed: larger width, larger height, both larger, and restored window dimensions. All 69 focused Avalonia canvas, window, font, and report-rendering checks now pass, including per-cell geometry, multipage bitmap rendering, and exported PDF page sizes. Evidence: `/private/tmp/cr032-before.xml` and `/private/tmp/cr032-focused.xml`. No Core or native macOS implementation changes.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Drawing/SkiaFigureCanvasRenderer.cs:81`; `AnalysisITC.MacOS/Drawing/CoreGraphicsFigureCanvasRenderer.cs:122`; validation=source; tests/UI=not-run. Both canvas renderers take validated explicit Supporting Figure dimensions; captured Final Figure size no longer caps Avalonia geometry.

## CR-033 — Corrected-view spline handles use raw baseline coordinates

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:654`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:710`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:1033`
- Related: ITC-004
- Problem: Spline anchors move to zero in corrected view, but slope handles are drawn and hit-tested around raw point power.
- Failure scenario: Enable Smooth handles and Corrected data on a trace with a baseline offset larger than the corrected plot range and a modest local slope.
- Consequence: Handles become detached from their point or disappear outside the clipped plot, where they cannot be dragged. Raw view is a workaround; this is distinct from drag accumulation.
- Suggested direction: Use the same raw/corrected coordinate basis for anchor drawing, handle drawing and handle hit-testing.
- Resolution (2026-10-16): Avalonia drawing and hit-testing now share one handle-endpoint calculation using the anchor's displayed height: point power in raw view and zero in corrected view. The existing slope offsets, horizontal handle length, clipping, visibility, drag sensitivity, locks and release processing are retained. The CR-002 drag-reference fix is preserved; no Core, native macOS or serialization changes are included.
- Validation: Twenty-four new headless cases inspect recorded drawing geometry and exercise actual press/move/release events for both handles, raw/corrected views, zero/positive/negative locked slopes and Joule/calorie power displays. Before the fix, all 12 corrected-view cases failed while raw-view cases and existing graph tests passed. All 46 graph cases now pass, including connector alignment, slope direction/magnitude, repeated-event stability, unchanged point power/time and one completion on release. The full Avalonia suite, run with `--no-build` after a concurrent PDB-file lock blocked rebuilding, finished with 547 passed, one existing opt-in skip and one unrelated failure: the concurrently added `AvaloniaJsonSettingsStoreTests.FailedReplacementLeavesSavedSettingsIntactAndCanBeRetried` expects `IOException` but receives `UnauthorizedAccessException` for a directory at the backup path on macOS. That failure also reproduces in isolation. No interactive desktop verification was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:710`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:1041`; validation=source; tests/UI=not-run. Drawing/hit-testing share endpoints around displayed raw/corrected anchor; control-event regressions cover slopes, units and release.

## CR-034 — A report build can overwrite a newer explanation choice

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs:448`; `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs:1031`; `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs:1331`
- Related: ITC-102
- Problem: Expanded explanations remains enabled during a background build. Toggling it marks the preview stale, but completion installs the older options and resets the cache to current.
- Failure scenario: Start Update Preview on a report large enough to interact with during construction, then change Expanded explanations before it finishes.
- Consequence: The checkbox and saved settings disagree with the ready preview and exported PDF; weighted-fit explanations can be missing despite the option being checked. Timing was not reproduced interactively.
- Suggested direction: Disable the control during construction and preserve invalidation through completion using the captured options or build revision.

- Resolution (2026-10-19): Added Expanded explanations to Avalonia's existing `SetBusy` policy. It is disabled while busy and restored only when results are selected, matching native macOS. The checkbox value, `CurrentOptions` capture, `MarkStale` invalidation and stale-export rebuild remain unchanged; no build revision framework or public API was added.
- Validation (2026-10-19): Both new headless regressions fail on the original code and pass with the one-line fix. A test-local `OneSetOfSites` subclass gates background evaluation, establishing the disabled in-flight state and restoration after completion. Later option changes clear the cached document/layout and rebuild on export; weighted-fit explanation presence matches the option in the report, layout and exported PDF. PDF text extraction confirms the heading and body are present with explanations enabled and absent when disabled; both corresponding rendered pages were inspected. The no-selected-results busy-state regression passes. The app/test build succeeds with 21 existing compiler/analyzer and NuGet audit warnings; the full Avalonia suite passes (738 passed, two skipped: opt-in Project Information visual QA and a case-distinct import check on the case-insensitive temporary filesystem). Isolated build outputs required linking the existing raw-import fixture directory into the temporary test root. No interactive desktop verification was performed.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs:1526`; `AnalysisITC.Avalonia.Tests/AnalysisReportEnhancementTests.cs:228`; validation=source,headless,PDF; tests/UI=two regressions and full Avalonia suite passed; PDF text/render checked; interactive desktop not run.
- Target: report build → preview/PDF matching the selected explanation option.
- Strategy: include expandedExplanationsCheck in the existing SetBusy enable/restore policy; retain CurrentOptions capture, MarkStale and stale-export rebuild. This closes the ordinary editable-control race without a new build-state framework.
- Verify: hold construction in flight, confirm user input is disabled, release and restore eligibility; change option afterward → stale preview and rebuilt PDF with expected explanation content.
- Questions: none.
- Depends: report map expanded-explanation contract; native busy policy is comparison only.

## CR-035 — Avalonia immediately overwrites tandem-scan failure feedback

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Tools/TandemMergerWindow.cs:360`; `AnalysisITC.Avalonia/Misc/Tools/TandemMergerWindow.cs:295`; `AnalysisITC.Core/TandemMixingScan.cs:575`
- Related: None
- Problem: The catch displays the error, but finally refreshes controls and synchronously replaces it with ordinary selection status.
- Failure scenario: Select two ordinary raw experiments with unintegrated included injections and create using Auto back-mixing. UI eligibility permits them, but the scanner rejects missing integration.
- Consequence: No merged experiment appears and the explanation is replaced by the selection count. Source experiments are not modified by this demonstrated failure.
- Suggested direction: Preserve failure feedback while restoring controls, or use the existing handled-error presentation.
- Resolution: Avalonia tandem creation now calls `AppEventHandler.DisplayHandledException(ex)`, matching native macOS error routing. The existing alert preserves the Core explanation while the merger restores controls and releases its processing lease. Eligibility, Core wording and scientific behavior, public APIs, MainWindow, AppEventHandler, and native code are unchanged.
- Modality: Native `NSAlert.RunModal` blocks the whole app. The Avalonia alert is owned by MainWindow and is a sibling of the merger, so the merger remains enabled while the alert is open. This follows existing Avalonia export-tool behavior and is unchanged by this fix; error routing is aligned, not application modality. The alert supplies the failure feedback; main-window status text is not part of this change.
- Validation: One new headless `[AvaloniaTheory]` exercises One-site and Model-free selections through the actual async creation method, awaited through reflection, with the merger opened using `ShowDialog<bool?>(main)`. Both cases reach the same pre-fit `ValidateAdaptiveSources` integration rejection; this does not validate fitting or criterion-specific scientific behavior. Both failed before the fix at the missing-error-dialog assertion, excluding the merger from MainWindow's owned-window count. Both now verify one persistent alert containing the Core sentence, an open merger with `Created == false`, restored Create/Cancel/list controls, unchanged source instances and order, no tandem result, included unintegrated source injections, and idle processing. Dismissal followed by retry produces one new alert. Fixture setup uses a document-restore scope when constructing MainWindow to suppress unrelated automatic baseline initialization of the minimal raw experiments.
- Validation results: All 16 focused merger and app-message cases pass. The full Release Avalonia suite reports 710 passed, one existing opt-in skip, and one unrelated failure in `StartupFileActivationCoordinatorTests.DefaultPolicyPreservesCaseDistinctPathsExceptOnWindows`; that path-policy failure also reproduces alone without running merger tests. The Release build succeeds and the focused diff passes `git diff --check`. Validation used automated headless controls/dialogs, not an interactive desktop walkthrough. Logs: `/private/tmp/cr035-before-regression.log`, `/private/tmp/cr035-after-focused.log`, `/private/tmp/cr035-avalonia-full.log`, and `/private/tmp/cr035-unrelated-path-policy.log`. No commit, packaging, or release was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Misc/Tools/TandemMergerWindow.cs:360`; `AnalysisITC.MacOS/ViewControllers/ExperimentMergerViewController.cs:357`; validation=source; tests/UI=not-run. Both desktop creation actions catch validation/processing failures, show the handled error, and restore controls in finally. Test sources cover async failures and retry; native/Avalonia alert modality remains intentionally different.

## CR-036 — Changing the attribute list discards pending Details edits

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs:258`; `AnalysisITC.Avalonia/Misc/Details/ExperimentAttributeEditorControl.cs:183`; `AnalysisITC.MacOS/PopOptionViewControllers/ExperimentDetailsPopoverController.cs:582`
- Related: CR-037
- Problem: Avalonia reconstructs every attribute editor from underlying copies before committing drafts held in controls.
- Failure scenario: Change buffer concentration from 20 to 50 mM, then add an attribute, remove another row or change another row's key. Reconstruction restores the buffer to 20 mM.
- Consequence: Ordinary multi-attribute editing silently loses pending input. Some selectors commit immediately, so the finding does not apply to every control.
- Suggested direction: Preserve draft values independently of editor reconstruction, or retain unaffected controls.
- Resolution: Avalonia synchronizes attribute rows by object identity, retaining surviving editors and their pending input. Available-key menus refresh with selection callbacks suppressed, so only a deliberate type change rebuilds that row's value controls. Both desktops retain unrelated editors when PBS/TBS replaces buffer and salt attributes, preserving the existing preset composition. Structural changes do not parse or apply drafts; incomplete and invalid input remains available for correction. Explicit removal or type/preset replacement discards only the affected rows, without confirmation. Apply validation, cancellation, and CR-037 precision/uncertainty behavior remain intact. No Core scientific or persistence behavior changed.
- Validation: Before the fix, 12 of 13 new Avalonia cases failed, and the corrected native fixture reproduced six failures confined to PBS/TBS; native ordinary list changes already passed. All 13 new Avalonia cases and 18 native AppKit cases now pass, covering numeric/text/selection drafts, deliberate replacement/removal, presets, key availability, invalid input, cancellation, empty lists and untouched precision. Avalonia also covers checkbox drafts. All 34 Avalonia and 36 native CR-037 regressions pass. The full Release Avalonia suite passes with 656 passed and one existing opt-in skip. Both desktop projects compile, and `git diff --check` passes. Native tests drive real controls/actions with sheet dismissal overridden; invalid native drafts exercise the same ApplyOption validator directly to avoid opening error alerts. No interactive desktop verification, packaging, signing or release was performed. Logs: `/private/tmp/cr036-before-avalonia.log`, `/private/tmp/cr036-before-native.log`, `/private/tmp/cr036-after-avalonia-focused.log`, `/private/tmp/cr036-after-native.log` and `/private/tmp/cr036-avalonia-full.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs:258`; `AnalysisITC.MacOS/PopOptionViewControllers/ExperimentDetailsPopoverController.cs:582`; validation=source; tests/UI=not-run. Attribute editors survive by object identity; presets replace only intended rows. Both DetailsDraftPreservationTests cover invalid/incomplete drafts, structural edits and cancellation.

## CR-037 — Applying Details alters scientific values and dates that were not edited

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:384`; `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:755`; `AnalysisITC.Avalonia/Misc/Details/ExperimentAttributeEditorControl.cs:429`; `AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs:380`
- Related: ITC-055, ITC-080
- Problem: Both desktops reparse rounded attribute displays on Apply without preserving untouched values. Avalonia also reparses editable short dates.
- Failure scenario: Apply a comment-only native edit to buffer pH 7.46: its F1 display becomes stored 7.5. G6 competitor concentrations similarly lose digits on both platforms. An untouched Avalonia filesystem date with seconds becomes minute-rounded and UserModified.
- Consequence: Unrelated edits change scientific conditions, can invalidate fits, and can falsify timestamp provenance. Main concentration/temperature fields already preserve unchanged originals; these attribute/date paths do not.
- Suggested direction: Preserve original values and provenance unless their corresponding controls were actually edited.
- Resolution: Both desktop attribute editors capture the original numeric values and displayed text, then parse only edited components. Untouched values retain their complete uncertainty, including SD, asymmetric intervals and missing-value state; editing one component preserves the other at full precision. The main cell/syringe concentration fields also retain their complete uncertainty when untouched. Avalonia parses dates only when their text changes, preserving ticks, DateTime kind and provenance on unrelated edits. Native date handling already preserves these and is covered by regression tests. Explicit date edits retain the existing editable/trusted-date policy. CR-036 remains separate.
- Validation: The initial regressions reproduced 22 Avalonia failures and 14 native failures before the fix. All 34 final Avalonia regression cases and all 36 native AppKit checks pass, covering untouched and comment-only Apply, pH, concentration/SD edits, asymmetric intervals, missing values, reverted text after an Apply attempt, invalid input, unchanged processing/heats, date precision/kind/provenance and deliberate date edits. The full Release Avalonia suite passes with 612 passed and one existing opt-in skip. Both desktop projects compile, and `git diff --check` passes. Native tests exercise the real Details controller and controls with sheet dismissal overridden in the harness; no interactive desktop verification, packaging or release was performed. Logs: `/private/tmp/cr037-before-avalonia.log`, `/private/tmp/cr037-before-native.log`, `/private/tmp/cr037-avalonia-full.log` and `/private/tmp/cr037-after-native.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Misc/Details/ExperimentAttributeEditorControl.cs:596`; `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:822`; validation=source; tests/UI=not-run. Changed-only parsing preserves full original values/intervals and untouched date provenance; both DetailsValuePreservationTests cover comment-only Apply and deliberate edits.

## CR-038 — Avalonia overrides automatic selection of a newly created result

- Priority: Medium
- Status: Resolved (2026-10-19).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/MainWindow.axaml.cs:1068`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:1129`; `AnalysisITC.Core/DataManager.cs:577`
- Related: None
- Problem: The sidebar refresh restores its previous local selection and writes it back over an explicit Core result selection.
- Failure scenario: Finish a fit with Create analysis result and Auto-open new result enabled. Core selects the new result, but the queued Avalonia refresh restores the preceding experiment.
- Consequence: The requested Results workspace does not open automatically; the result remains available for manual selection. Native macOS follows the Core selection.
- Suggested direction: Honor explicit Core selection changes and retain the old row only when no new selection was requested.

- Resolution (2026-10-19): `RefreshDataList` reads the current Core selected index when the queued refresh executes, replacing the previous local-row and first-row fallbacks. The existing selection-event guard protects list replacement, and the existing selection handler opens the Results workspace. Later user selection and CR-001's fit-context reconciliation remain intact; Core and native macOS behavior are unchanged.
- Validation: The strengthened actual-fit test reproduced the auto-open failure before the fix; three additional Core-selection cases also failed (result, another experiment and no selection). After the fix, 99 relevant Avalonia checks pass with one platform-specific import test skipped on macOS. Attached headless MainWindow checks cover all four create-result/auto-open combinations, selected result identity and visible Results workspace after dispatcher drain, later user selection, mixed list order, reordering, selected/unselected removal, empty/no selection and repeated refreshes without extra selection notifications. Existing CR-001 workspace, selection-menu and progress regressions pass, along with import, macOS workflow-header and result-update-dialog checks. Build succeeds. Logs and xUnit results: `/private/tmp/cr038-build-green.log`, `/private/tmp/cr038-red.log`, `/private/tmp/cr038-red.xml`, `/private/tmp/cr038-focused.log`, `/private/tmp/cr038-focused.xml`. Interactive desktop verification was not performed.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/MainWindow.axaml.cs:1059`; `AnalysisITC.Avalonia.Tests/AnalysisWorkspaceControlTests.cs:1330`; `AnalysisITC.Avalonia.Tests/ResultSelectionNavigationTests.cs:26`; validation=source+tests; tests/UI=99 passed, 1 platform skip (attached headless windows); interactive UI=not-run.
- Target: Auto-open new result → selected new result and Results workspace.
- Strategy: reflect Core's current selected index during list replacement, keeping isRestoringDataListSelection and avoiding stale local writeback. Preserve user selection after later interaction, mixed list order and disabled-auto-open behavior.
- Verify: attached MainWindow + actual result creation, dispatcher drained, setting on/off; removal/reorder/no-selection and repeated refresh cases.
- Questions: none.
- Depends: preserve CR-001's selection-during-fit reconciliation.

## CR-039 — Avalonia silently drops case-distinct Linux import paths

- Priority: Medium
- Status: Resolved (2026-10-18); Linux desktop checks not performed.
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/MainWindow.axaml.cs:846`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:983`; `AnalysisITC.Avalonia/StartupFileActivationCoordinator.cs:81`
- Related: ITC-028, ITC-077
- Problem: Path deduplication uses case-insensitive comparison on every filesystem.
- Failure scenario: On case-sensitive Linux, select distinct run.itc and Run.itc together in the supported multi-file picker or drop them together.
- Consequence: Only the first reaches the reader, so one selected experiment is silently omitted. Separate imports can succeed because the pending batch is cleared.
- Suggested direction: Use filesystem-appropriate path identity consistently across picker, drop and startup activation paths.
- Resolution: `StartupFileActivationCoordinator` is now the import-batch deduplication point after its existing full-path normalization, using `OrdinalIgnoreCase` on Windows and `Ordinal` on Linux and macOS. Both drop handlers and `OpenQueuedPathsAsync` retain their filtering but no longer deduplicate independently. Picker, drop, startup activation, and Reload Last all feed the coordinator; `LastDocumentPaths()` and its ordinal `Distinct()` are unchanged. First-seen order, sequential reads, startup readiness, pending-request merging, and reimport after a completed batch are preserved. The internal constructor accepts a comparer override for tests; no public API or Core reader changes.
- Deliberate macOS change: Ordinal comparison matches native `AnalysisITC.MacOS/Platform/MacDataReader.cs` import behavior, rather than the default APFS volume's case-insensitive identity. Differently cased aliases of one file now import twice on default macOS volumes. Picker, drop, and Finder activation supply on-disk names, so this arises through command-line arguments or Reload Last paths saved from them. No volume probing, symlink resolution, hard-link handling, or ITC-077 duplicate-ID policy changes.
- Validation: The new default-policy regression failed before the fix because only the first case-distinct path reached the reader. All 11 coordinator cases now pass, including both comparer overrides, the host default, exact and normalized relative/absolute duplicates, first-seen order, merged requests/activations, later reimports, sequential reads, and startup confirmation/readiness. The raw `.itc` filesystem integration test explicitly skipped on the development Mac: after creating `run.itc`, `Run.itc` already existed on its case-insensitive APFS volume; no second fixture was overwritten. It also skips when the platform import policy is case-insensitive. The Debug Avalonia app and test project build successfully; the full Avalonia suite reports 719 passed, zero failures, and two skips (this filesystem test and the existing opt-in Project Information visual check), including startup, menu, and import-prompt regressions. `git diff --check` passes for the affected files. Logs: `/private/tmp/cr039-red-test.log`, `/private/tmp/cr039-build.log`, `/private/tmp/cr039-focused-tests.log`, and `/private/tmp/cr039-full-tests.log`. Independent review on 2026-10-19 ran the raw `.itc` integration test on a temporary case-sensitive APFS volume, where it passed, and reported the full Avalonia suite at 728 passed, zero failed, and two skipped.
- Not performed: No Linux desktop is available in this session. Linux picker, window-drop, experiment-list-drop, startup-activation, and Reload Last entry-point checks were not performed. No interactive macOS walkthrough, packaging, signing, release, commit, or push was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/StartupFileActivationCoordinator.cs:11`; `AnalysisITC.Avalonia/StartupFileActivationCoordinator.cs:91`; `AnalysisITC.Avalonia.Tests/ImportPathTests.cs:20`; validation=source; tests/UI=not-run. Full-path normalization uses case-insensitive identity only on Windows. Case-distinct Linux inputs reach the reader in the focused test source. Native queued URL import adds no case-insensitive deduplication. Linux interactive validation remains unperformed.

## CR-040 — An Avalonia fitting test exits before its worker and admission lease finish

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Avalonia.Tests/AnalysisWorkspaceControlTests.cs:1104`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:833`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:898`
- Related: ITC-007, ITC-098, ITC-101
- Problem: The test requests Stop, closes its window and clears shared state without joining the real asynchronous fit.
- Failure scenario: The worker remains scheduled after teardown, while the next serialized test starts and requests calculation admission.
- Consequence: The next test can observe busy admission or receive late callbacks from the previous fixture. Serialization does not join asynchronous work; no flaky run was reproduced.
- Suggested direction: Await the owned fit's completion in teardown, including assertion-failure paths, before clearing data and restoring global settings.
- Resolution: The preflight test is now asynchronous. Its `finally` block requests Stop and awaits the existing completion/admission helper before closing its window and clearing shared data. It also restores the fitting-options snapshot after completion, alongside the parameter-limit setting. Application behavior is unchanged.
- Validation: A temporary probe reproduced busy admission immediately after the original test returned, then passed after the fix. With an assertion deliberately failed immediately after `RunFit`, the probe also confirmed completion, restored settings and successful admission for a subsequent calculation. The probes and injected failure were removed before the final Release build and full Avalonia suite: 708 passed, one existing skip, zero failures. Evidence: `/private/tmp/cr040-validation/` (`before.log`, `after.log`, `assertion-failure.log`, `final-build.log`, `avalonia-full.log` and `avalonia-full.xml`). `git diff --check` passes. Application code and manual/help are unchanged. No commits or pushes.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia.Tests/AnalysisWorkspaceControlTests.cs:1953`; `AnalysisITC.Avalonia.Tests/AnalysisWorkspaceControlTests.cs:1983`; validation=source; tests/UI=not-run. Finally requests Stop and awaits fitting/admission completion before teardown; outer cleanup restores options even on failed assertions.

## CR-041 — Preferences tests depend on the live interpretation service

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Avalonia.Tests/PreferencesTests.cs:466`; `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:925`; `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:953`; `AnalysisITC.Core/Interpretation/FtItcInterpretationClient.cs:124`
- Related: ITC-007, ITC-065
- Problem: Production Preferences constructors create real HTTP clients; substituting the settings store leaves this transport active in tests.
- Failure scenario: A test opens Preferences with synthetic cached credentials and expects Verified (cached). A timely live response changes that state; blank-code windows can instead enroll a public installation.
- Consequence: Tests depend on service response timing and can update shared settings after their initiating fixture ends. Some account-refresh cancellation guards exist, but public refresh lacks equivalent lifetime control. No request was made during review.
- Suggested direction: Inject controlled transport/client behavior and cancel or join every refresh during cleanup.
- Resolution: Preferences uses controlled test transports and owns verification, account refresh and public enrollment tasks. Closing or reloading suppresses stale UI/settings updates on both desktops; an enrollment already dispatched completes and Core saves its issued code. Controlled cancellation/race tests and the full Core suite cover the persistence exception.
- Validation: Final Linux deployment candidate: 442/442 Web tests; Core: 3,751 passed, one existing skip; Avalonia Preferences: 27/27. Native harness/layout checks passed; native Preferences response races were reviewed and compiled but not exercised in an interactive AppKit window. Operational evidence is in `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:38`; `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:983`; validation=source; tests/UI=not-run. Preferences tests use controlled transport; owned cancellable/keyed refreshes suppress stale updates on both clients. Dispatched enrollment still persists issued code as agreed; native race-test coverage remains limited.

## CR-042 — Native Copy Processing buttons have no connected actions

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Main.storyboard:5602`; `AnalysisITC.MacOS/Main.storyboard:5610`; `AnalysisITC.MacOS/Main.storyboard:5618`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1207`
- Related: None
- Problem: The visible All, Active and New buttons have no action, binding, outlet or programmatic handler. Existing connections belong to detached older controls.
- Failure scenario: Select a processed thermogram in native macOS and click any button under Copy processing to.
- Consequence: No processing is copied and no explanatory feedback appears. Existing tests construct outlets directly and do not exercise these storyboard buttons.
- Suggested direction: Connect the visible controls to their intended copy scopes and admission policy, and verify their actual storyboard dispatch.
- Resolution (2026-10-16): The visible All, Active and New buttons now have controller outlets and distinct storyboard actions. All invokes the existing all-experiments operation; Active retains included-experiment scope, and New retains no-baseline scope. One helper checks for a selected thermogram with a baseline, acquires and enters processing admission, and invokes the appropriate Core operation. Locked sources remain eligible. The buttons participate in calculation busy state and refresh when selecting an experiment, a saved result, no selection or an empty project. Short shared Core tooltips replace the incorrect locking text, with the matching Active/New text used in Avalonia. The manual and in-app help explain the scopes. Core copying, thermogram filtering and lock-transfer policies are unchanged.
- Validation: A new harness compiles the actual storyboard into temporary build output and loads its Process controller, verifies that outlets reference the visible archived buttons, and clicks them through AppKit. All 16 cases pass: three scopes with locked/unlocked sources, both existing menu scopes, no selection/empty project/result/heat-only/no-baseline sources, busy-state rejection with and without selection loss, tooltip restoration, and empty-target feedback. Mixed destinations verify inclusion filtering, locked and heat-only preservation, source preservation, and an independently expected 12 J heat after recalculation. Existing native slider, initialization/admission, graph-drag, clear-processing and unified-help harnesses pass. Full Release Core suite: 3,757 passed and one existing opt-in skip. Focused Avalonia processing suite: 11 passed. One additional native harness, `UpdateResultStatusTests`, fails before reaching its copy checks because its unrelated existing reflection call supplies six arguments to the current seven-parameter `PublishProgress` method; those files were not changed for CR-042. Logs/results: `/private/tmp/ft-itc-cr042-tests/`. `git diff --check` passes. No interactive application verification, packaging or signing was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Main.storyboard:5602`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1221`; validation=source; tests/UI=not-run. Visible archived All/Active/New controls have actions/outlets and scoped admission; actual-storyboard test source checks dispatch and eligibility.

## CR-043 — Native automatic injection volume is capped at 20 µL only in its primary field

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:400`; `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:409`; `AnalysisITC.MacOS/Main.storyboard:1297`
- Related: ITC-078, ITC-082
- Problem: The automatic volume field is clamped by the manual stepper maximum, while simulation uses the unclamped automatic volume.
- Failure scenario: Select VP-ITC, ten injections, a small first injection and automatic volume. The 310 µL syringe gives 34.3 µL regular injections, but the disabled primary field displays 20 µL.
- Consequence: The primary volume field conflicts substantially with the simulated schedule; turning Auto off also changes the volume to 20 µL. The schedule summary correctly shows 34.3 µL, so the effective value is not completely hidden.
- Suggested direction: Display the effective automatic volume without the manual ceiling and align permitted manual ranges with supported instruments and protocols.
- Resolution: The automatic value is assigned directly to the native field without manual-range validation. Manual input retains the 0.1 µL minimum and has no upper ceiling, matching Avalonia as requested. The native stepper's archived and runtime maxima no longer impose the former 20 µL ceiling. Turning Auto off retains the displayed automatic volume. Simulation calculations are unchanged.
- Validation: The new regression first reproduced VP-ITC's 34.3 µL schedule with a primary display of 20 µL. The complete native `ExperimentDesignerTests` harness now passes, including seven independently expected automatic schedules across all five supported instruments, small-first on/off, two-injection schedules, Auto-to-manual retention, manual field/stepper values above 20 µL and syringe capacity, and the 0.1 µL manual minimum. The harness also compiles and loads the actual Designer storyboard and dispatches its instrument, Auto, stepper and field actions. Existing parameter retention, energy-edit, control rebinding and fit-locking regressions pass. Native managed compilation, storyboard compilation, shell syntax and `git diff --check` pass. No shared scientific calculations changed; no interactive application verification, packaging or signing was performed. Logs: `/private/tmp/cr043-before-native.log` and `/private/tmp/cr043-final-native.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:131`; `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:412`; `AnalysisITC.MacOS.Tests/ExperimentDesignerTests.cs:92`; validation=source; tests/UI=not-run. Automatic volume populates field/stepper directly; no 20 µL ceiling remains, and manual input retains the 0.1 µL floor. Focused test sources cover automatic-to-manual retention and larger volumes.

## CR-044 — Native graph construction fails when every injection is excluded

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/CGGraph.cs:2188`; `AnalysisITC.MacOS/GraphViews/AnalysisGraphView.cs:172`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:127`
- Related: ITC-106, ITC-108
- Problem: Included-only enthalpy scaling calls Min/Max on each experiment before guarding empty observation sets, during construction before display options are applied.
- Failure scenario: Exclude every injection while idle, then leave and return to Analyze or reselect the experiment.
- Consequence: An empty-sequence exception interrupts graph construction. Final Figure and selected-fit graphs share the constructor; application termination was not demonstrated.
- Suggested direction: Handle empty eligible observations before aggregation, preserving excluded-point scaling policy and any available prediction range.
- Resolution: Native `DataFittingGraph.GetMinMaxEnthalpy` now aggregates eligible observations and existing fit/enabled Null predictions before taking finite minima and maxima. Included-only scaling remains literal: excluded observations do not become a fallback. Zero still anchors the range; an empty usable set yields `[0, 0]` for the existing axis buffer, giving finite nondegenerate limits. Unified enthalpy membership, solution resolution, offset handling, manual limits, property setters, native residual scaling (including CR-046), Core publication figures and Avalonia are unchanged. No shared helper was added.
- Validation: The new native `AllExcludedFitGraphTests` fixture first reproduced the constructor's empty-sequence exception before the fix. All eight scenario groups now pass, covering excluded-only graphs with/without a fit and bitmap drawing, independently expected observation/fit/Null bounds, hidden and nonfinite Null predictions, empty/nonfinite observation sets, mixed and entirely excluded unified groups, selected-experiment membership boundaries, the existing empty-`IncludedData` branch, Traceability Mode with real Final Figure option order, actual Analyze initialization/reinitialization and selected-fit setup, unchanged neutral residual limits, and manual Final Figure limits after drawing. All seven targeted native fixtures pass: the new fixture, `AnalysisNullGraphTests`, `AnalysisNullEvidenceTests`, `InjectionEditingTests`, `DissociationGraphTests`, `UnifiedPowerAxisTests` and `FinalFigureModelSelectionTests`. Native managed compilation, shell syntax and focused `git diff --check` pass. No interactive application verification, packaging or signing was performed. Logs: `/private/tmp/cr044-before-native.log`, `/private/tmp/cr044-after-native.log` and `/private/tmp/cr044-related-native.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Drawing/CGGraph.cs:2194`; validation=source; tests/UI=not-run. Eligible finite observations/fit/Null predictions aggregate before extrema; empty set has finite zero-buffer fallback. AllExcludedFitGraphTests covers actual graph entry paths.

## CR-045 — Unified native power axes include experiments with no thermogram

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/CGGraph.cs:939`; `AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs:279`; `AnalysisITC.MacOS/PopOptionViewControllers/FinalFigureOptions/FinalFigureOptionsController.cs:374`
- Related: ITC-012, ITC-036
- Problem: Unified power scaling aggregates every active experiment without filtering empty raw/corrected trace collections.
- Failure scenario: Load an integrated-heats file alongside a processed raw experiment, select the raw experiment and enable unified power axes in Final Figure.
- Consequence: The heat-only member causes an empty-sequence exception. The selected-experiment thermogram guard does not protect other members; the option is session-local and default-off.
- Suggested direction: Build the common power domain only from usable thermogram samples and define an explicit empty-domain fallback.
- Resolution (2026-10-18): Native power-axis bounds and heat-direction classification now use only thermogram-bearing experiments with nonempty traces in the selected raw/corrected mode. An explicitly empty corrected trace is skipped; the existing null-corrected fallback to raw samples is retained. An empty shared domain falls back to the selected experiment's usable trace, otherwise retaining the initialized/current axis. Switching raw/corrected display recalculates shared bounds when enabled; local scaling, zero anchoring, buffering, units, and manual bounds retain their existing behavior. No Core, Avalonia, persistence, or manual/help changes.
- Validation: Eight of nine new native behavioral checks failed before the fix; all nine now pass. The fixture regression strictly reopens `SolutionIdentityTracingTest/processed.ftxtc`, imports `BufferSubtraction/hepes-01.DH` through the real reader with a test unit prompt, and verifies both active members, shared figure construction, bitmap preview drawing, and native PDF rendering. Independent numeric expectations cover exothermal/endothermal/mixed domains, ignored heat-only directions, raw/corrected switching, local scaling, empty corrected traces, null-corrected raw fallback, selected-trace fallback, no available samples, and manual-bound precedence. Inactive heat-only members and heat-only selection are also covered. Existing `FinalFigureModelSelectionTests` and all seven `DissociationGraphTests` checks pass. Managed native compilation only; no packaging/signing targets. Logs: `/private/tmp/cr045-before.log` and `/private/tmp/cr045-after.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Drawing/CGGraph.cs:946`; validation=source; tests/UI=not-run. Unified power bounds filter usable thermograms; selected trace/current bounds are explicit fallbacks. Raw/corrected/no-sample tests preserve manual axes.

## CR-046 — Native residual axes can exclude their own uncertainty bars

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/CGGraph.cs:2631`; `AnalysisITC.MacOS/Drawing/CGGraph.cs:2699`; `AnalysisITC.Core/Presentation/PublicationFigure.cs:1069`
- Related: ITC-096
- Problem: Autoscaling selects the smaller absolute uncertainty endpoint, although drawing uses both endpoints.
- Failure scenario: The largest residual is 100 J/mol with SD 100 J/mol and other points do not enlarge the domain. Scaling pads the chosen 100 to ±150 while a bar endpoint reaches 200.
- Consequence: The plot domain omits part of displayed uncertainty. Padding can conceal some cases but cannot ensure containment; no rendered artifact was inspected.
- Suggested direction: Include the largest absolute lower/upper endpoint when calculating residual bounds under the existing visibility policy.
- Resolution (2026-10-18): Autoscaling the residual axis from residual values while allowing uncertainty bars to extend beyond the domain is intentional. No code change.

- Recheck: **2026-10-19; REJECTED**; evidence=`AnalysisITC.MacOS/Drawing/CGGraph.cs:2633`; `AnalysisITC.MacOS/Drawing/CGGraph.cs:2701`; validation=source; tests/UI=not-run. The 18 October decision explicitly accepts residual-based autoscaling with bars allowed outside the domain. The original claimed defect conflicts with intended policy; no corrective change occurred. Endpoint-inclusive publication scaling is separate.

## CR-047 — Two-buffer protonation graphs generate nonfinite native uncertainty bands

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:165`; `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:306`; `AnalysisITC.Core/DataClasses/AnalysisResult.cs:548`
- Related: ITC-094
- Problem: Native line-band preparation divides by n−2, although protonation analysis permits two distinct buffer observations.
- Failure scenario: Run protonation analysis on two one-site experiments with distinct buffer ionization enthalpies and display its graph. Positive observation SD produces infinite bounds; zero SD produces NaN.
- Consequence: Nonfinite uncertainty coordinates enter the native path, while central fitted values remain intact. The exact CoreGraphics response or a crash was not established.
- Suggested direction: Use shared finite envelope preparation with an explicit two-observation policy, retaining the native renderer and distinguishing unavailable uncertainty from invalid arithmetic.
- Resolution: Native linear graphs now prepare one shared Core `FitEnvelopeBuilder` envelope with 300 intervals, draw its original-fit centers, and join the finite `HasBand` samples into one closed polygon, matching Avalonia and report semantics. Zero-width or unusable intervals supply no band vertices; mapped nonfinite fit coordinates are excluded from native paths. Two-buffer eligibility, observation error bars, stored estimates/intervals, completed-analysis metadata, and the CoreGraphics renderer are unchanged. `ResultGraphView.cs` and the help resource were left untouched. The manual adds only the approved platform-neutral explanation of the interval-endpoint envelope and its correlation limitation.
- Intentional counter-ion change: Counter-ion requires at least three distinct activities, so its previous native band was finite. That band used observation SDs with `t = 1`; its integer `1 / n` term evaluated to zero. Replacing it with combinations of the slope's and intercept's 95% interval endpoints changes the displayed band in existing counter-ion projects. This is an intentional presentation change, not a counter-ion crash fix, and does not introduce a prediction interval or joint confidence band.
- Fail-first evidence: Added `LinearDependenceEnvelopeTests` before editing production code. Both graphs are created through `ResultGraphView.Setup(...)` and rendered into CoreGraphics bitmaps; a recording subclass copies each production graph's data, fit, labels, scale factors and axis bounds, without adding a setup seam. Before: **0 passed, 5 failed**. Each two-buffer case recorded 24 fit coordinates, including 22 nonfinite band coordinates (Infinity with positive SD; NaN with zero SD). The three-observation literal/unusable-bound cases and three-activity counter-ion case had finite old geometry but failed the new envelope expectations. After: **5 passed, 0 failed**. Positive-SD protonation and counter-ion record 903 finite fit coordinates; zero-width/unusable bounds record 301 finite center coordinates and no band coordinates. Evidence: `/private/tmp/cr047/native-before.log`, `/private/tmp/cr047/native-after.log`.
- Numerical checks: With `ReferenceT = 0`, slope `0.3 [0.2, 0.4]`, intercept `1000 [500, 1500]`, and the production x-axis set to `[0, 30000]`, `x = 10000` is an exact sample. The native harness verifies mapped center/lower/upper values of `4000`, `2500`, `5500`. The counter-ion case uses three distinct activities and verifies `-12`, `-12.8`, `-11.2` at `ln(activity) = -4`, including identical repeated geometry and unchanged stored results. Three Avalonia/report integration cases verify the same corner policy, negative coordinates, unit scaling, and zero-width omission; no duplicate Core envelope tests were added.
- Visual review: Inspected matched 900 × 650 production-native bitmaps in `/private/tmp/cr047/before/` and `/private/tmp/cr047/after/`: `two-buffer-positive-sd.png`, `two-buffer-zero-sd.png`, and `counter-ion-three-activities.png`. Fixtures, axis bounds, dimensions and styling are identical before/after. Positive-SD protonation gains finite shading; zero-SD protonation retains the central line and points without shading; counter-ion visibly changes from its old finite observation-SD band to the wider interval-endpoint envelope, with the line and observations retained.
- Validation: 214 focused Core/report checks and all 41 focused Avalonia graph/report checks passed. Native managed compilation, the five new regression cases, eight existing salt-plot cases, protonation availability/layout, Null graph and five report figure-pair checks passed. Full Core suite: **4,241 passed, one existing skip, zero failures**. Logs: `/private/tmp/cr047/core-focused.log`, `/private/tmp/cr047/core-full.log`, `/private/tmp/cr047/avalonia-focused.log`, and `/private/tmp/cr047/native-after.log`. Report map X05/X06 was checked; report behavior and structure are unchanged. No packaging, version, commit or push changes.
- Commit verification (2026-10-19): The isolated CR-047 snapshot passes all five native regression cases and all three Avalonia/report integration cases. The new Avalonia fixture is self-contained; unrelated pending Core API-call adaptations remain outside this commit. Evidence: `/private/tmp/cr047-commit-mkk33gcr/native-isolated.log` and `/private/tmp/cr047-commit-mkk33gcr/avalonia-isolated.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:165`; `AnalysisITC.Core/Presentation/FitEnvelope.cs:131`; validation=source; tests/UI=not-run. Native and Avalonia/report paths use finite interval-corner envelopes; no two-observation divide-by-zero remains. Intentional counter-ion band change and native renderer preserved.

## CR-048 — Constant fitted values produce zero-span native dependence axes

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/GraphAxis.cs:147`; `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:42`; `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:120`
- Related: ITC-096
- Problem: The static axis factory leaves equal bounds unchanged, and subsequent scaling divides by the zero span.
- Failure scenario: Create a valid salt-series result whose affinity is fixed or shared at the same value and open affinity versus salt.
- Consequence: Tick calculations and mapped point coordinates become nonfinite for a valid constant dependence. Uncertainty does not enlarge this domain because extrema use central values.
- Suggested direction: Give the axis factory a finite, scale-appropriate degenerate-range policy consistent with the guarded axis setter.

- Resolution: The native buffered-axis factory and setter share one range calculation. Exactly equal bounds use `max(1, |value|)` as the padding basis, multiplied by the requested buffer fraction. The factory initializes ticks only after expanding the range and records the constant-range buffer without reapplying it. Ordinary unequal factory ranges retain their historical buffer rescaling; ordinary setter ranges and tick settings are unchanged. No changes to fitted estimates, intervals, Avalonia, serialization, public APIs, manual or help text.
- Reproduction: Before the production change, all 10 constant factory cases failed finite-range checks; the setter's large-value cases also collapsed, while nonzero constant cases exposed its fixed padding. Production `ResultGraphView.Setup` salt fixtures with identical affinities failed with and without uncertainty, as did direct constant-X/Y data. Evidence: `/private/tmp/cr048-before.log`. That initial run also exposed an incorrect new ordinary-range test assumption about historical factory rescaling; the test was corrected to preserve the existing behavior rather than changing production behavior.
- Validation: `ConstantAxisTests` covers 24 cases: factory/setter zero, positive, negative, tiny and large constants at default/custom buffer fractions; recorded factory buffer; ordinary ranges and tick-count preservation; constant-X/Y rendering; and production constant-affinity salt rendering with/without uncertainty, explicitly positive SD and enclosing intervals, finite mapped centers/error endpoints and unchanged saved estimates/intervals. After review, the harness sets `en-US` culture to match the application. The requested ConstantAxisTests, SaltDependenceGraphTests, UnifiedPowerAxisTests, AnalysisNullGraphTests, DissociationGraphTests and AllExcludedFitGraphTests all pass (`/private/tmp/cr048-finish.log`). The earlier five linear-envelope cases passed before concurrent electrostatics API changes; the reported unrelated `counter-ion-three-activities` RestoreResult argument-count failure is outside CR-048 and was not rerun or fixed in this pass. The staged four-file slice was applied to a detached HEAD worktree; Core restore/build and all 24 ConstantAxisTests passed there (`/private/tmp/cr048-isolated.log`). The temporary worktree used the existing NuGet package cache to satisfy native HintPaths; no project files were changed. The new test is registered in the native harness. No shared Core production, manual or help changes.
- Visual review and chosen limitation: Regenerated and inspected all three white-background native bitmaps under `en-US` in `/private/tmp/cr048/after-enus/`; tick labels are distinct numeric values. The explicitly chosen padding basis of at least 1 is applied in stored axis coordinates, as in Avalonia's approach; consequently micromolar Kd values stored in molar units receive broad axes and error bars collapse below one pixel: the SD and zero-SD renders are byte-identical. Subsequent Buffer assignment on a constant-range axis retains existing UpdateAutoScale semantics (finite and centred, not re-padded to `max(1, |value|)` times the new buffer). This fix does not introduce unit-aware minimum padding or change existing native tick-label formatting.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Drawing/GraphAxis.cs:147`; `AnalysisITC.MacOS/Drawing/GraphAxis.cs:175`; validation=source; tests/UI=not-run. Factory/setter expand equal ranges by agreed max(1,abs(value)) basis; ordinary ranges retain behavior. Broad axes/subpixel errors for small molar values remain accepted.

## CR-049 — Native energy editors change scale while reusing the same visible text

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs:518`; `AnalysisITC.MacOS/CustomViews/OptionAdjustmentView.cs:738`; `AnalysisITC.MacOS/CustomViews/ParameterValueAdjustmentView.cs:50`
- Related: ITC-055, ITC-078, ITC-082
- Problem: Native editors resolve parsing units from the already mutated stored value, while their label and text retain the original unit.
- Failure scenario: A manual competitor enthalpy displays −40 kJ/mol. Enter −0.05, producing −50 J/mol, then edit its SD. The next parse resolves J/mol and turns the unchanged central text into −0.05 J/mol under the kJ/mol label.
- Consequence: An ordinary edit changes a scientific input by a factor of 1000. Designer energy controls share the defect, and automatic simulation can parse an option twice during one edit.
- Suggested direction: Keep the displayed unit stable as editor state. Any unit transition should update label and text together while preserving the internal value.

- Resolution: Analysis option drafts capture the displayed energy unit and retain it with value/SD text across context merges and control rebuilds. Designer parameter and option controls capture their unit before populating labels, text and sliders; repeated application during automatic simulation uses that same unit. Fresh editors can choose a new scale while formatting their text and labels together. The read-only Results display helper retains its existing behavior.
- Validation: The new native `EnergyEditorUnitTests` fails against the original implementation at the reported SD edit (−0.05 J/mol instead of −50 J/mol), and passes with the fix. Focused native runs of `EnergyEditorUnitTests`, `ExperimentDesignerTests`, and `AnalysisParameterDraftTests` pass, covering Joule/calorie families, zero, scale crossings, SD edits, draft merges/rebuilds, fresh-editor rescaling, sliders, repeated option application, and automatic simulation on/off. Avalonia's 25 `DesignerCompetitorOptionTests` pass without changes. The final native run used temporary dependency copies on `MONO_PATH` after transient missing assemblies in shared build outputs; no packaging or signing was run.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs:424`; `AnalysisITC.MacOS/CustomViews/OptionAdjustmentView.cs:737`; `AnalysisITC.MacOS/CustomViews/ParameterValueAdjustmentView.cs:63`; validation=source; tests/UI=not-run. Native drafts/editors retain displayed energy units through repeated parsing/rebuilds; EnergyEditorUnitTests covers scale crossings, SD edits, sliders and automatic simulation.

## CR-050 — Native Results replaces saved competitor intervals with symmetric bounds

- Priority: Medium
- Status: Resolved (2026-10-18).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs:2173`; `AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs:456`; `AnalysisITC.Core/Math/NumberStructs.cs:60`
- Related: ITC-042, ITC-044, ITC-055, ITC-094
- Problem: The native Model page reconstructs model-option values from value and SD, discarding their saved interval endpoints and asymmetry.
- Failure scenario: Fit competitive data using competitor properties from a one-site result with asymmetric uncertainty, then view Results → Model using CI or Automatic uncertainty display.
- Consequence: macOS shows different bounds or a different uncertainty style from the saved value and Avalonia. The stored scientific evidence remains intact.
- Suggested direction: Format the complete transformed FloatWithError using shared presentation instead of reconstructing it from two scalars.

- Resolution: Core `AnalysisResultModelOptionPresentation` formats saved model options for both desktop Results views. Affinity-to-Kd and unit conversions retain the complete `FloatWithError`, including interval endpoints; Automatic selects its style after transformation. Numeric options follow the selected uncertainty, precision, concentration-unit and energy-family preferences. Source indicators and per-experiment saved values retain their existing roles. Native editable value/SD fields, fitting and persistence are unchanged.
- Validation: Before-fix native checks reproduced Kd `1.0 [0.5, 1.5] µM` instead of `1.0 [0.1, 10.0] µM`, and reconstructed enthalpy bounds; Avalonia checks reproduced its fixed-precision Kd formatting. All 32 new Core regressions pass, covering independent conversion expectations, all five styles, units/precision, zero and symmetric uncertainty, source indicators and saved-value preservation. Full Core suite: 4,273 passed, one existing skip. The 40 focused Avalonia competitor/results/designer tests pass. Native `CompetitiveResultModelOptionPresentationTests`, `EnergyEditorUnitTests` and `AnalysisParameterDraftTests` pass. Real Skia and AppKit renders were inspected at inspector width. Evidence: `/private/tmp/cr050-core-focused.log`, `/private/tmp/cr050-core-full.log`, `/private/tmp/cr050-after-avalonia.log`, `/private/tmp/cr050-after-native.log`, and `/private/tmp/cr050-qa/`. No packaging, signing, commit or push was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Presentation/AnalysisResultModelOptionPresentation.cs:17`; `AnalysisITC.Core/Math/NumberStructs.cs:312`; validation=source; tests/UI=not-run. Both Results views format complete transformed saved intervals; reciprocal conversion reverses endpoints. Asymmetric Kd/enthalpy presentation tests preserve values.

## CR-051 — Native integration sliders use another experiment's baseline-exclusion policy

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1242`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:968`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1019`
- Related: ITC-085
- Problem: Slider processing decisions use a cached DiscardIntegratedPoints flag that updates on data changes but not on selection changes.
- Failure scenario: Process A with integration-region exclusion enabled, change the default to disabled, and import B. Reselect A and shorten an integration window with a slider; the stale false flag skips A's required baseline refit.
- Consequence: Heats can use a baseline fitted to the previous integration regions. Graph-marker dragging reads the actual selected processor policy, so equivalent edits can produce different outcomes.
- Suggested direction: Read the selected processor's policy at commit time or rebind the cache whenever selection changes.
- Resolution (2026-10-16): Removed the mutable controller cache. Both slider commit paths and drag completion read the selected processor's `DiscardIntegratedPoints` setting directly. Existing initial-display and `UpdateUI` paths synchronize both exclusion checkboxes; the existing checkbox action toggles the processor's actual setting, refreshes the UI and requests one processing pass. No production methods or abstractions were added. ITC-085's silent previews, single release processing and experiment ownership checks are retained.
- Validation: The new native selection regression failed before the fix because reselection retained the other experiment's checkbox states. All eight cases now pass, covering both exclusion policies, start/end sliders and keyboard/native slider-cell tracking. Each checks the heat against an independent arithmetic-mean baseline expectation, clicks both native exclusion checkboxes, requires one processing pass per click and verifies that the other experiment's policy, heat and interpolation count remain unchanged. `ProcessingSliderTests`, `ProcessingGraphDragTests`, `ProcessingInitializationTests` and `ProcessingClearTests` pass, including managed compilation and existing local Y242A fixtures. Obsolete reflection writes to the removed cache were deleted from three test helpers. `git diff --check` passes. Evidence: `/private/tmp/cr051-fix.ehUoV0/red.log`, `/private/tmp/cr051-fix.ehUoV0/green.log`.
- Boundaries: Validation uses native controls/actions and existing test harnesses; no full installed-application session was performed. No Core/Avalonia production code, scientific equations, serialization, manual/help, packaging, signing, commits or pushes were changed or run for this fix.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:969`; validation=source; tests/UI=not-run. Slider completion uses selected processor exclusion policy with drag ownership; checkbox synchronization and independent heat regressions remain.

## CR-052 — Changing export units resets native correction choices

- Priority: Medium
- Status: Resolved (2026-10-19), together with CR-053.
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:110`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:215`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:238`
- Related: None
- Problem: Native export refreshes reload correction checkboxes from settings before pending checkbox edits have been saved.
- Failure scenario: For a fitted experiment, choose Integrated Peaks, uncheck Offset-corrected peaks, then change the molar energy unit. Refresh restores the previously enabled correction.
- Consequence: Export can subtract an offset after the user deliberately chose uncorrected peaks. The reset is visible and can be repaired before confirmation.
- Suggested direction: Retain draft correction choices across unrelated refreshes, or save them when the controls change.

- Resolution: Correction checkboxes initialize once and their native actions update the sheet's existing settings. Refresh preserves those drafts around `SetData()` while updating visibility and eligibility, including temporary ineligible scopes. Empty-data rejection restores the drafts for retry; successful confirmation retains existing eligibility normalization. Application preferences still persist only after a folder is selected.
- Validation: All six native AppKit scenarios pass, covering checked/unchecked drafts, unit/format/scope actions, rejected and accepted confirmation, eligibility normalization, independent uncorrected peak/fit expectations, and Cancel. The original controller fails five cases; its numerical regression exports -12.25 instead of the independent -12 kJ/mol expectation after the offset-off choice and unit change. The seven focused Core molar-energy tests and six Avalonia dialog checks pass. The final rebuilt full Core suite passes with 4,359 passed, one existing external-reference diagnostic skip, and zero failures; its TRX explicitly records both new Combined Data and folder-cancellation cases as passed.
- Boundaries: Native validation compiled the changed export controller with a temporary committed `AnalysisReportViewController.cs` in both original/fixed builds because the unrelated working-tree report picker fails compilation (`NSStringAttributes` versus `CTStringAttributes`). No live report source was changed. These are automated AppKit checks, not an installed-application walkthrough. The opening-scope initialization defect was subsequently resolved in the separately approved ITC-126 follow-up: all eight native harness cases pass, including the six CR-052/053 cases and the new opening/empty-scope cases; evidence is in `/private/tmp/itc126/{red,green}/test-run.log` and the ITC-126 tracker entry. No packaging, signing, version changes, commits or pushes.
- Evidence: `/private/tmp/cr052-053/{green,red}/{native-build.log,test-compile.log,test-run.log}`, `/private/tmp/cr052-053/molar-energy-retry.log`, `/private/tmp/cr052-053/tests.log`, `/private/tmp/cr052-053/layout.log`, `/private/tmp/cr052-053/core-final.log`, and `/private/tmp/cr052-053/results/core-final.trx`.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:141`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:201`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:249`; validation=source+native-actions+numerical-regressions. Correction drafts survive unrelated refreshes and rejected confirmation; successful confirmation applies existing eligibility rules.

## CR-053 — Native Combined Data export offers correction options it cannot honor

- Priority: Medium
- Status: Resolved (2026-10-19), together with CR-052.
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:206`; `AnalysisITC.Core/DataExport/Exporter.cs:440`; `AnalysisITC.Avalonia/Misc/Dialogs/ExportDataWindow.cs:162`
- Related: None
- Problem: The native sheet exposes correction checkboxes for Combined Data even though that format intentionally includes both traces and uncorrected peaks.
- Failure scenario: Export Combined Data with Offset-corrected peaks checked for an experiment with nonzero fitted offset. The peak and fit columns retain the offset.
- Consequence: Output contradicts the selected option; the baseline checkbox likewise cannot choose the included trace columns. Existing tests explicitly require the current export contents.
- Suggested direction: Expose only format-supported controls and explain Combined Data's fixed contents, preserving its intentional numerical export policy.

- Resolution: Native Combined Data hides both correction checkboxes, shows the existing no-additional-options message, and retains its independently controlled molar-energy selector. Baseline correction is offered only for Thermogram Data; offset correction only for Integrated Peaks and ITCsim. Both desktops use the shared format description explaining raw/baseline-corrected traces and peaks/fits without fitted-offset correction; Avalonia's duplicate explanation was removed. The manual and shared export help describe the same fixed contents. Exporter calculations, headers, public APIs, project serialization and preference persistence are unchanged.
- Validation: The native scenarios and six Avalonia checks verify supported controls and Combined Data description layout. All seven focused Core molar-energy tests pass; distinct raw/corrected samples and independent peak/fit/residual expectations verify identical Combined Data output across all eight baseline/offset/fitted-peak flag combinations. Full Core: 4,359 passed, one existing skip, zero failures. Native original/fixed comparison, build limitation and evidence paths are recorded under CR-052.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:216`; `AnalysisITC.Core/DataExport/ExportType.cs:44`; `AnalysisITC.Avalonia/Misc/Dialogs/ExportDataWindow.cs:145`; validation=source+native-actions+avalonia-headless+numerical-regressions. Only supported correction choices are shown; Combined Data retains its fixed numerical policy and molar-energy selector.

## CR-054 — Native tandem creation lets validation errors escape its UI action

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/ExperimentMergerViewController.cs:262`; `AnalysisITC.MacOS/ViewControllers/ExperimentMergerViewController.cs:289`; `AnalysisITC.Core/TandemMixingScan.cs:572`
- Related: ITC-098
- Problem: The async-void creation action has finally cleanup but no exception handler around its awaited scan.
- Failure scenario: Choose two admitted raw sources with unintegrated included injections and request automatic back-mixing. Core validation throws during the scan.
- Consequence: The exception bypasses the application's handled-error route after controls and admission are restored. Actual process termination was not established.
- Suggested direction: Present validation/creation failures through the handled-error path while retaining operation ownership through that presentation.
- Resolution (2026-10-16): The native creation action catches exceptions from scanning, concatenation and awaited processing through `AppEventHandler.DisplayHandledException`. Its activity lease, entered ownership and creating flag remain active during error presentation; the existing `finally` restores controls before the action releases admission. Failure keeps the merger open with its selections/settings for retry. Source eligibility, scan validation, scientific calculations and successful publication/notification/dismissal are unchanged.
- Validation: The new `TandemCreationTests` native harness reproduced eight escaped exceptions before the fix: two attempts for each automatic criterion with included unintegrated injections, and two attempts each for simple/fixed creation with an injected awaited processing-publication failure. All eight now produce exactly one handled error without escaping, retain the current operation and disabled creation controls through a nested Cocoa event loop, keep unrelated processing queued until presentation returns, preserve source data/document dirty state/drafts, and allow retry. Both successful simple/fixed cases process and publish the tandem, retain ordered source provenance, request the completion notification and dismiss the real sheet. Native managed compilation and `ProcessingInitializationTests` pass. The combined runner's `UpdateResultStatusTests` stops at an unrelated stale reflection helper supplying six arguments to the current seven-parameter `PublishProgress`; a temporary copy supplying the missing optional null argument passes the full status harness, and the repository copy is unchanged. `git diff --check` passes. Evidence: `/private/tmp/cr054-native-red.log`, `/private/tmp/cr054-native-green.log`, `/private/tmp/cr054-status-compat/validation.log`.
- Boundaries: The harness uses real native actions/sheets and Cocoa continuations, intercepting the handled-message callback to exercise its nested event loop. It does not establish actual process termination in the original application or test a full installed application. No Core/Avalonia production changes, manual/help changes, packaging, signing or pushes are included.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/ViewControllers/ExperimentMergerViewController.cs:349`; `AnalysisITC.MacOS.Tests/TandemCreationTests.cs:71`; validation=source; tests/UI=not-run. Native creation catches scan, concatenation, and awaited processing failures and restores controls in finally. Tests cover ordinary validation and constructed processing-publication failure with retry. Shared implementation with CR-035.

## CR-055 — Native Buffer Subtraction inherits a filter that excludes tandem targets

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/BufferSubtractionTool.cs:44`; `AnalysisITC.MacOS/ListViews/ExperimentMergerListViewCell.cs:88`; `AnalysisITC.Core/DataClasses/ExperimentData.cs:325`
- Related: ITC-026, ITC-060, ITC-062, ITC-067, ITC-081
- Problem: The subtraction tool reuses the merger-source list, whose unconditional filter removes tandem experiments.
- Failure scenario: Create a tandem sample and tandem blank, then open Buffer Subtraction. The unrestricted reference list can show the blank, but the sample is absent from targets.
- Consequence: The native batch/preview workflow cannot perform this supported correction. Native Details and Avalonia still provide paths to apply it.
- Suggested direction: Separate merger-source eligibility from buffer-subtraction target eligibility.

- Recheck: **2026-10-19; SUPERSEDED**; evidence=`AnalysisITC.Core/DataClasses/BufferSubtractionPolicy.cs:8`; `AnalysisITC.MacOS/ViewControllers/BufferSubtractionTool.cs:149`; validation=source; tests/UI=not-run. CR-016's accepted restriction supersedes the original supported-tandem-correction premise. Both desktop tools now intentionally exclude tandem targets; do not split list eligibility to re-enable forbidden corrections.

## CR-056 — Native Experiment Designer has no sequential-site-count editor

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/CustomViews/OptionAdjustmentView.cs:124`; `AnalysisITC.MacOS/CustomViews/OptionAdjustmentView.cs:667`; `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:527`
- Related: ITC-078, ITC-082
- Problem: The selectable Sequential model exposes SequentialSiteCount, but its native designer option view has neither editor nor apply handling for it.
- Failure scenario: Select Sequential Binding Sites in the native Designer. The option row is empty and remains at the default two sites.
- Consequence: Users cannot design the supported three- or four-step experiments there. Native analysis has a separate working editor, and Avalonia Designer supports these choices.
- Suggested direction: Provide a bounded site-count editor through the existing designer option and parameter-table rebuilding flow.

- Resolution: **2026-10-19; FIXED**. Native Designer now provides a popup populated from `SequentialSiteCount.EnumOptions`; selection applies `IntValue` and raises the existing change event. Avalonia Designer reuses its analysis selector instead of accepting an unrestricted integer. Both use the shared Core tooltip and a sentence-case label. The existing controller/state paths retain edits, rebuild parameter rows, and preserve automatic/manual simulation behavior; defaults and window isolation are unchanged.
- Validation: native `ExperimentDesignerTests` passed, including actual AppKit 2→3→4→2 actions with automatic simulation on/off, retained affinity and hidden fourth-step enthalpy, setup/model rebuilds, finite simulations, solver option transfer, window isolation and fitting locks. Avalonia Designer/competitor tests passed 52/52; focused Core Designer/Sequential tests passed 108/108. Visual QA confirmed the native option row and the Avalonia Designer selector fit and display correctly. Full Core suite: 4,329 passed, one skipped, two failures in `ProjectLoadingProgressTests.CancellationDuringItemUpdateClearsLoadingText` and `InteractiveReadLetsUiRenderEachItemBeforeLoadingCompletes` (project-loading cancellation/progress assertions; unrelated to this UI change).
- Documentation: existing model-option guidance and Sequential model description remain sufficient; no additional prose, scientific calculations, serialization, or settings changes.

## CR-057 — Native Details commits pH input that its editor marks invalid

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:699`; `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:755`; `AnalysisITC.MacOS/PopOptionViewControllers/ExperimentDetailsPopoverController.cs:649`
- Related: ITC-003, ITC-027
- Problem: Native pH validation only colors the field; Apply commits the numeric draft without enforcing that chosen input policy.
- Failure scenario: Change a buffer's pH from 7.4 to 74 while leaving concentration valid, then press Apply despite the red field.
- Consequence: The typo becomes experiment metadata and the remembered pH default, and enters buffer-inclusive ionic-strength calculations. The concern is inconsistent application validation, not a universal physical 0–14 bound.
- Suggested direction: Validate before staging attributes using an explicit shared input policy across both desktops.

- Resolution: **2026-10-19; FIXED**. The confirmed application entry policy is finite pH from 0 through 14, inclusive; blank input is rejected. Shared Core `ExperimentAttributeInputPolicy` supplies the rule and message to both Details editors and native field coloring. Native Buffer Apply validates pH and concentration before assigning draft values, preserves unchanged full-precision inputs, and remembers pH only after successful application (including the complete form when staging). Avalonia rejects nonfinite input and preserves saved zero; new buffer rows receive 7.4 when their key is selected. Scientific calculations, loading, concentration uncertainty, and detached staging retain their existing behavior.
- Validation: native managed compilation and both focused Details harnesses pass (52 value/pH cases and 18 draft cases). The focused Avalonia Details, identifiers, precision and draft tests pass 77/77. Cases exercise 74, out-of-range values, nonnumeric/nonfinite text, blank/whitespace, invalid-field coloring, correction/retry, 0/14 Apply/reopen, new-buffer defaults, native direct application, and remembered pH after an earlier valid buffer or later form rejection. Rejected input preserves live attributes, pending text, comments, processing revision and measured heats. Full Core suite: 4,340 passed, one skipped, zero failures, including all nine new policy cases. Both desktop app/test builds pass; no interactive desktop verification was performed.
- Documentation: one entry-range sentence added to the existing Details manual and in-app help.

## CR-058 — Opening a project silently appends to native report-only documents

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Platform/MacDataReader.cs:176`; `AnalysisITC.Core/DataReaders/DataReaders.cs:153`; `AnalysisITC.Core/DataReaders/FTXTCReader.cs:151`
- Related: ITC-032, ITC-059, ITC-105
- Problem: The native Replace/Append/Cancel prompt checks source items but ignores retained reports.
- Failure scenario: Create a report, remove its source items individually, then open another project. The old report remains while new content is appended and the opened file's path becomes the save destination.
- Consequence: A subsequent Save includes both projects' reports without an Append choice. The combined document is dirty, and Clear All remains available; immediate loss or a falsely clean document is not claimed.
- Suggested direction: Include retained reports in the native document-content predicate used for project-open decisions.

- Recheck: **2026-10-19; FIXED**. Native project-open content detection now includes retained reports, matching Avalonia's content predicate. Reports remain independently retained under ITC-032; the existing Replace/Append/Cancel prompt and `CloseAllDataAsync` save handling are reused. Raw imports, empty-document opening, Core readers, serialization and read-only `.ftitc` support are unchanged.
- Validation: `bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh ProjectOpenReportOnlyTests` passed. The production native open queue and real AppKit alerts cover Cancel, Replace/Cancel at the dirty-save prompt, Replace/Clear All, Replace/Save and Append from a report-only state created by removing the final experiment. Checks cover report identity/text, save destination, dirty state, document identity, unchanged original files when appropriate, and strict saved/reopened report sets. Empty-document project open and a real raw `.itc` import also passed. The same regression failed before the predicate change with "Expected native project-open/save prompt was skipped". The harness substitutes only recent-document registration to avoid the system recent-items service.
- Avalonia comparison: `HasDocumentContent` already includes reports; no Avalonia change required. Focused Project Information and decision-dialog tests passed (38 passed, one opt-in visual QA skipped).

## CR-059 — Public quota checks race with durable request admission

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/Program.cs:692`; `AnalysisITC.Web/PublicQuotaService.cs:16`; `AnalysisITC.Web/InterpretationQuotaService.cs:45`; `AnalysisITC.Web/InterpretationUsageStore.cs:32`
- Related: None
- Problem: Public quota and active-execution checks run before admission, while the admission transaction skips the relevant checks for Public's null monetary-limit path.
- Failure scenario: With four of five installation requests used, two requests with distinct correlation IDs both pass the public check before either admission commits. Both are then admitted and dispatch provider work.
- Consequence: Public request and concurrency limits can be exceeded; global quota decisions also lack atomic revalidation. This differs from intentionally allowing one admitted request to finish above a recorded cost limit.
- Suggested direction: Include public installation/global quota evaluation and installation ownership in durable admission, preserving duplicate claims and the policy against predicted-cost reservation.
- Resolution: Typed public admission policy runs in the immediate SQLite admission transaction, checking duplicates, migration blockers, maintenance, installation activity, lifetime unresolved costs, installation limits and global recorded limits. Existing response codes are retained; no predicted-cost reservation was introduced.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/InterpretationUsageStore.cs:31`; `AnalysisITC.Web/Program.cs:759`; validation=source; tests/UI=not-run. Duplicate, active-execution, unresolved-liability and quota decisions occur in immediate transaction before durable admission/provider dispatch; two-host race tests cover one-provider-call outcome.

## CR-060 — Public accounting omits durable attempts without a final request summary

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/InterpretationUsageStore.cs:174`; `AnalysisITC.Web/InterpretationUsageStore.Reporting.cs:45`; `AnalysisITC.Web/Program.cs:866`; `AnalysisITC.Web/InterpretationUsageStore.Administration.cs:56`
- Related: None
- Problem: Public usage requires an access-tier field sourced only from the optional final summary; admission does not persist that classification with execution identity.
- Failure scenario: A public attempt records a provider receipt, then the process crashes or summary finalization fails. Other installations' global checks omit it immediately; stopped-execution recovery later removes the originating installation's active guard without restoring classification.
- Consequence: Public counts, recorded spend and unresolved liabilities undercount durable provider activity. The authoritative receipt and operator/account-history queries retain the charge.
- Suggested direction: Persist quota classification with durable admission identity and calculate public usage independently of final-summary availability.
- Resolution: Schema 7 records access classification and request-count eligibility with durable execution identity before provider dispatch. Final summaries do not establish or overwrite them. Repeatable startup backfill handles old-writer rows; public counts include admitted/finalized/recovered executions and exclude rejections. Unknown historical classifications are reported as non-public.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/InterpretationUsageStore.cs:346`; `AnalysisITC.Web/InterpretationUsageStore.Reporting.cs:7`; validation=source; tests/UI=not-run. Execution identity durably captures access classification/count eligibility before dispatch; reporting reads identity/attempts independently of summary, with migration handling.

## CR-061 — Concurrent public-registry writes can undo a successful revocation

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/PublicAccessRegistry.cs:49`; `AnalysisITC.Web/PublicAccessRegistry.cs:61`; `AnalysisITC.Web/InterpretationAdminCommands.cs:31`; `AnalysisITC.Web/Program.cs:657`
- Related: ITC-057
- Problem: Whole-file registry mutations use only a process-local semaphore, while the supported administration command runs separately.
- Failure scenario: The server reads the registry while touching identity A; the CLI revokes B and writes; the server then replaces the file with its earlier snapshot.
- Consequence: A successfully revoked credential can become accepted again, even when the concurrent request belongs to another installation. The required interleaving is narrow and its deployment frequency is unknown.
- Suggested direction: Serialize mutations across processes or use transactional storage, covering the real service/CLI writer arrangement.
- Resolution: Public create/touch/revoke mutations use a checked Unix flock on a persistent sibling inode with a five-second bound. Create/revoke fail without writes on timeout; touch logs and proceeds without writing only on timeout, while a discovered revocation denies access. Linux separate-process service/admin tests passed, including termination and lock permissions.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. Separate-process Linux service/admin lock rehearsal also passed. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/PublicAccessRegistry.cs:30`; `AnalysisITC.Web/PublicAccessRegistry.cs:100`; validation=source; tests/UI=not-run. Create/revoke/touch lock the persistent sibling inode across read-modify-write and use atomic replacement; touch timeout skips writing, reread revocation denies. Separate-process test source covers lock boundaries.

## CR-062 — Month rollover stops unresolved public costs from blocking new work

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/InterpretationUsageStore.cs:174`; `AnalysisITC.Web/PublicQuotaService.cs:13`; `AnalysisITC.Web/InterpretationQuotaService.cs:48`
- Related: None
- Problem: Public usage applies the current-month cutoff to unresolved-cost counts as well as monthly spending, without an independent unresolved hold.
- Failure scenario: A public request finalizes late on 31 October with unknown provider cost. On 1 November its unknown count disappears from the public snapshot and a fresh request can pass if its rolling allowance permits.
- Consequence: New work is allowed without settlement or waiver of the previous liability, contrary to the documented cross-month rule. This occurs even when the public summary was successfully stored.
- Suggested direction: Limit the date filter to periodic spending totals and query unresolved liabilities over their full lifetime.
- Resolution: Unresolved public liabilities are queried over the installation lifetime independently of monthly spending and rolling request counts. Settlement/waiver releases the affected installation; another installation can continue against recorded global spending. Month-boundary, missing-summary, migrated-liability and waiver tests cover the rule.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/InterpretationUsageStore.cs:208`; `AnalysisITC.Web.Tests/InterpretationUsageStoreDurabilityTests.cs:120`; validation=source; tests/UI=not-run. Lifetime unresolved-liability query is separate from monthly spend/rolling counts and gates admission; rollover/settlement/waiver source coverage remains.

## CR-063 — Registry health reports success without reading the registry

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/HealthChecks.cs:50`; `AnalysisITC.Web/GenerationPresetRegistry.cs:43`; `AnalysisITC.Web/DailyStatusEmail.cs:82`
- Related: None
- Problem: The interpretation registry check unconditionally reports readable configuration inside a try block that performs no registry read.
- Failure scenario: An existing generation-preset registry becomes malformed or inaccessible while interpretation/provider configuration is otherwise healthy. Options and generation requests fail when reading it.
- Consequence: Health output and the daily email can report the Interpretation group healthy and omit the outage cause. Other storage probes inspect different files; no actual deployment failure was observed.
- Suggested direction: Validate registries through the request-handling read policy and report safe structured failures, retaining intentional defaults for an absent preset file.
- Resolution: Health reads and validates presets, public access, static operator codes and registered operator codes through their actual read paths even while generation is paused. Missing-file defaults are retained. Structured health/daily-email checks and the 20-case registry matrix cover valid, absent, malformed, invalid and unreadable files.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/HealthChecks.cs:70`; `AnalysisITC.Web.Tests/PausedRegistryHealthMatrixTests.cs:11`; validation=source; tests/UI=not-run. Health reads every applicable registry via actual request read policy even while paused; 20-case source matrix includes missing/default, malformed and inaccessible files.

## CR-064 — Registration tests read and may write the service-default registry

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Web.Tests/RegistrationSecurityTests.cs:674`; `AnalysisITC.Web/InterpretationOptions.cs:40`; `AnalysisITC.Web/Program.cs:225`; `AnalysisITC.Web/RegistrationMailSender.cs:28`
- Related: None
- Problem: The fixture redirects several files but omits Registration.OperatorRegistryPath and retains the real delivery worker.
- Failure scenario: Enabled startup reads the ambient registered-account registry. A malformed/unreadable file can fail the test; if the activation fixture lasts until a worker tick and the default directory is writable, credential provisioning writes there before missing mail configuration is detected.
- Consequence: Tests depend on external machine state and can conditionally leave persistent synthetic credentials outside fixture cleanup. No ambient registry was inspected or modified during review.
- Suggested direction: Redirect the distinct registration registry into the fixture and explicitly control the worker and transport in endpoint tests.
- Resolution: Every Web endpoint fixture redirects interpretation/registration persistence into temporary storage and removes the RegistrationDeliveryWorker, whose retention pass otherwise runs even when registration is disabled. Delivery tests explicitly drive controlled transports. The final full Linux candidate suite verifies behavior without ambient service-store access.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web.Tests/WebTestHostIsolation.cs:11`; validation=source; tests/UI=not-run. All eight found endpoint host fixtures isolate both registry paths and other persistence, removing delivery worker; controlled tests explicitly drive delivery.

## CR-065 — Deployment activation failures bypass the existing rollback branch

- Priority: Medium
- Status: Resolved (2026-10-17); implemented and deployed to MIST.
- Confidence: Confirmed
- Category: Correctness
- Location: `scripts/publish-web-app.sh:42`; `scripts/publish-web-app.sh:50`; `scripts/publish-web-app.sh:106`; `Documentation/Interpretation/MIST_DEPLOYMENT.md:31`
- Related: None
- Problem: Stop, rename and start commands run under set -e outside recovery handling; only failures reached through the later health check enter rollback.
- Failure scenario: The old release is moved aside and the next rename fails, or systemctl start returns failure. Bash exits before the health-check recovery branch.
- Consequence: The service remains stopped with no active release directory or a failed replacement, requiring manual recovery. No deployment was executed.
- Suggested direction: Handle activation-stage failures explicitly and recover only to an executable compatible with the complete current accounting ledger; keep generation paused until compatibility and health are verified, without restoring an old database.
- Resolution: The publish script accepts an isolated source tree and separate staging/activation. A detached host runner records activation phases and has one idempotent recovery entry point, preserving the complete live ledger. Consistent backups, executable/dependency hashes, an actual candidate/previous/candidate ledger round trip, fault-injected phase tests and live preflight recovery verified restoration of prior availability.
- Validation: Final exact Linux deployment candidate passed 442/442 Web tests. Seven deployment-runner test methods cover injected activation/recovery phase failures; the ledger-copy round trip and live preflight recovery passed. See `Documentation/Interpretation/ROLLOUT_2026-10-17_RELIABILITY.md` for operational evidence.

- Recheck: **2026-10-19; FIXED**; evidence=`scripts/publish-web-app.sh:38`; `scripts/activate-web-release.py:182`; `scripts/tests/test_activate_web_release.py:88`; validation=source; tests/UI=not-run. Activation phases route failures through idempotent recovery with generation paused and complete ledger preserved; fault-test source covers activation/recovery boundaries. No deployment executed.

## CR-066 — Precision reports attach current source hashes to potentially older executions

- Priority: Medium
- Status: Resolved (2026-10-23).
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Tests/ScientificValidation/ForwardPrecision/report_results.py:27`; `AnalysisITC.Tests/ScientificValidation/ForwardPrecision/report_results.py:66`; `AnalysisITC.Core.Tests/PytcExternalReferenceTests.cs:357`
- Related: None
- Problem: Reporting hashes the current model sources without binding them to the execution that produced the supplied prediction arrays and test results.
- Failure scenario: Edit a model, then run only the documented report command using an earlier passing prediction directory and TRX.
- Consequence: The generated evidence associates 52 passing recorded predictions with hashes of code that did not produce them. Numerical/reference checks remain valid; no existing archive was shown to be wrong.
- Suggested direction: Capture source/build identity when predictions execute and preserve or verify it during reporting, distinguishing report-time checkout identity when useful.

- Recheck: **2026-10-19; OPEN**; evidence=`AnalysisITC.Tests/ScientificValidation/ForwardPrecision/report_results.py:62`; `AnalysisITC.Core.Tests/PytcExternalReferenceTests.cs:54`; validation=source; tests/UI=not-run. Report hashes current sources beside independently supplied prediction/TRX files; no execution identity joins them. Tooling misuse is reachable; no existing archive proved wrong.
- Target: precision report → identity of the code that produced predictions.
- Earlier strategy (superseded by the decision below): scoped runner captures build-source hashes and loaded Core assembly hash, gives prediction files/test output a run ID, then finalizes manifest with reference/prediction/TRX hashes. Reporter requires matching IDs, expected cases and successful results; label any current checkout hashes separately. Verify old predictions + changed source/build or unrelated passing TRX are rejected; matching run succeeds.
- Questions: none. Preserve external integrated-heat floating-point acceptance; no solver/refitting claim or general provenance framework.

- Plan decision (2026-10-19): **One command builds the current repository, runs the tests, then generates the report from that run.** Plan only; implementation remains pending.
- Strategy: add one runner under `ScientificValidation/ForwardPrecision`; use a fresh run directory, build `AnalysisITC.Core.Tests`, then run `PytcExternalReferenceTests` and `TwoSiteStoichiometryForwardTests` against that build. Set their existing prediction-output variables and TRX destination to that directory. Only after successful tests and complete 52-case output, invoke `report_results.py` with those exact files. Capture source/build identity for this build, abort if relevant sources change during the command, and report the captured identity. Document this command as the supported workflow; no caller-supplied old results or separate run-ID framework required.
- Verify: one invocation produces fresh predictions, test results and report; stale files elsewhere are unused; build/test failure, missing cases or concurrent source changes produce no new success report. Preserve pinned external references and the current precision criterion.

- Resolution (2026-10-23): `ForwardPrecision/run_validation.py` is the single supported build→test→report command. It rebuilds the Core test project in Release into a fresh run directory, executes the two existing forward-test classes against that assembly, overrides prediction-output variables and reports only those new outputs. Source hashes captured before building and built Core/test assembly hashes are recorded in the existing verification JSON; unchanged inputs and assemblies are checked after build, tests and rendering. The report is published only after all checks succeed. The reporter no longer offers saved-result CLI arguments or hashes the current model sources itself. Missing/duplicate cases, incomplete test runs and numerical failures are rejected. Reproduction instructions now use the single command; existing archived evidence remains unchanged. No separate manifest, run-ID system or application change was added.
- Validation: A controlled pre-fix reproduction accepted recorded predictions and attached a deliberately altered source hash. All 14 Python workflow checks now pass, covering fresh output/environment isolation, build/test failure, absent/duplicate/missing cases, numerical failure, empty/aborted test runs, changed source during build/tests/rendering, replaced assemblies and refusal of the old report-only CLI. The final real command built successfully, passed all 131 selected .NET tests with no skips/failures, and generated all 52 floating-point-precision comparisons plus CSV/JSON/README and PDF/PNG figures. The generated plot was visually inspected. Final evidence: `/private/tmp/cr066-final-portable/run-kin20mbw/`; logs: `/private/tmp/cr066-final-portable.log` and `/private/tmp/cr066-workflow-tests.log`. Validation ran on macOS/.NET 10; Windows/Linux execution was not performed. Application code, C# tests, external reference data and scientific tolerances are unchanged.
- Recheck: **2026-10-23; FIXED**; evidence=`AnalysisITC.Tests/ScientificValidation/ForwardPrecision/run_validation.py`; `AnalysisITC.Tests/ScientificValidation/ForwardPrecision/test_run_validation.py`; validation=real build/test/report and controlled failure-path tests. This supersedes the earlier OPEN recheck and pending plan above.

## CR-067 — Avalonia labels the syringe activity input as stoichiometry

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisParameterRowBuilder.cs:385`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisParameterRowBuilder.cs:467`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:681`
- Related: ITC-030
- Problem: Editable rows receive no model-option context, so Nvalue1 always appears as N-value/N even when its meaning changes to syringe activity α.
- Failure scenario: Enable Use Syringe Correction for One Site, then edit or lock the first parameter. The separate Stoichiometry option controls cell-side sites, but the row still describes stoichiometry.
- Consequence: The user can configure the wrong scientific quantity. Native editing and Core output metadata already recognize the alternative meaning; this differs from mixed-result export labeling.
- Suggested direction: Supply option-sensitive editable parameter metadata through shared Core presentation.
- Resolution: Both desktops now use shared Core parameter labels, symbols and scientific tooltips. With syringe correction enabled, the first parameter is shown as Correction factor / α; otherwise it remains N-value / N. This covers the Analysis inspector, experiment designer, saved locked-parameter rows and associated global-constraint labels for One Set of Sites, Two Sets of Sites and Competitive Binding. The Avalonia designer refreshes parameter presentation when the option changes even if the parameter keys do not change. Saved result rows use the result's own model options. Entered values, locks, equations, parameter keys and export naming are unchanged. The manual and in-app help identify where to enter α and the fixed stoichiometry.
- Validation: Five new Core metadata cases pass. All 97 related Avalonia checks pass, including 15 new cases exercising correction on/off transitions, single/global Analysis, single/global saved locked rows, all three models, tooltips and retained values/locks. The new native AppKit harness covers the same model meanings, designer option events, parameter drafts, saved single/global locked rows and global-constraint labels; it and the existing parameter-draft and designer harnesses pass. Native runtime package paths were supplied from Core's resolved dependencies to avoid concurrent builds removing copied dependency DLLs. Full Core suite: 3,908 passed, one skipped. Evidence: `/private/tmp/cr067-core-full-final.log`, `/private/tmp/cr067-core-focused.log`, `/private/tmp/cr067-avalonia-final.log`, `/private/tmp/cr067-native-final.log`. No packaging or release targets were run.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Presentation/AnalysisParameterLabelPresentation.cs:24`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisParameterRowBuilder.cs:403`; `AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs:1241`; validation=source; tests/UI=not-run. Shared Core presentation selects Correction factor/α versus N-value/N from the option. Both desktop editable/constraint labels consume it and have option-sensitive test sources.

## CR-068 — Native salt plots replace fitted centers with fresh random sample means

- Priority: Medium
- Status: Resolved (2026-10-17).
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.MacOS/GraphViews/ResultGraphView.cs:205`; `AnalysisITC.MacOS/GraphViews/ResultGraphView.cs:227`; `AnalysisITC.Core/Math/NumberStructs.cs:122`
- Related: None
- Problem: Counter-ion and Debye–Hückel graph setup resamples each salt group and adopts the sample mean, including groups with one fitted observation.
- Failure scenario: Open a salt-series result with nonzero affinity uncertainty and one experiment per salt condition, then switch graph modes away and back.
- Consequence: Plotted centers and intervals change without refitting and can differ from the estimates used by the stored advanced fit. Saved results remain intact, but visual comparisons and screenshots are unstable.
- Suggested direction: Preserve singleton values and intervals directly and define a deterministic grouping policy for repeated conditions.
- Resolution: Native counter-ion-release and Debye–Hückel graphs now plot one point per experiment, matching Avalonia. Counter-ion release uses the directly transformed member estimate and interval already supplied by Core; Debye–Hückel applies the existing log10 transformation to each member independently. Neither graph groups repeated conditions or resamples uncertainty. Stored advanced fits are unchanged. The manual and in-app help explain what each point and logarithmic interval represent.
- Validation: All eight new native AppKit regression cases failed before the change and pass afterward. They cover both modes, distinct and repeated salt conditions, asymmetric intervals and zero uncertainty, independent expectations from known log10-affinity inputs, four graph rebuilds with intervening mode switches, and unchanged member estimates and advanced-fit metadata. Registered in the default native harness. The existing native help, null-graph and seven dissociation-graph checks also pass; `git diff --check` passes. Evidence: `/private/tmp/cr068-native-before.log` and `/private/tmp/cr068-native-verified.log`. The harness uses installed System.Buffers and Xamarin facade directories through MONO_PATH. No interactive full-application workflow, packaging or release targets were run.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/GraphViews/ResultGraphView.cs:200`; `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ElectrostaticsAnalysis.cs:75`; validation=source; tests/UI=not-run. Counter-ion/Debye-Hückel graphs use each experiment's estimate/interval without grouping or random sampling; repeated rebuild tests retain original centers and saved fit.

## CR-069 — Avalonia Competitive Designer exposes unidentified internal units and coordinates

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:351`; `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:365`; `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:451`; `AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:97`
- Related: ITC-078, ITC-079, ITC-082
- Problem: Competitor option fields show and accept raw molarity, J/mol and log10 Ka without identifying those units or coordinates.
- Failure scenario: Enter ligand affinity 1 expecting the ordinary editor's Kd = 1 µM convention. Designer stores log10 Ka = 1, equivalent to Kd = 0.1 M; entering −40 expecting kJ/mol similarly becomes −40 J/mol.
- Consequence: Plausible entries simulate a substantially different experiment, undermining the planning workflow. Labels and tooltips provide no compensating explanation.
- Suggested direction: Reuse scientific conversions and explicit unit/coordinate labels for Designer model options.
- Resolution: The three competitor fields now reuse the ordinary Avalonia model-option editor's conversions in a single-value layout. Total concentration uses the preferred concentration unit; affinity is explicitly labeled **Kd** in that unit; enthalpy uses the resolved Joules/Calories display unit per mole. Each field keeps its displayed unit until rebuilt. Entries commit on Enter or loss of focus, reject negative concentrations, nonpositive Kd, nonfinite values and conversion overflow before changing the simulation, and preserve full stored precision when rounded text is untouched. Setup/model changes and the fit factory retain the converted options. Input tooltips reuse Core descriptions and identify units; the shared concentration description no longer implies an attribute source in the Designer. Native Designer conversions already follow this convention. The manual and in-app help include an entry example.
- Validation: The initial 16 regression cases reproduced 13 failures before the fix. All 25 final regressions pass, covering µM/nM/mM, Kd coordinates, kJ/kcal/J/cal, decimal-comma input, invalid entries, setup/model switching, preference changes, precision preservation, fit-factory inputs and unchanged ordinary-editor uncertainty conversion. The full Release Avalonia suite passes with 637 passed and one existing opt-in visual QA skip; the full Release Core suite passes with 3,808 passed and one existing external-reference diagnostic skip. `git diff --check` passes. Evidence: `/private/tmp/cr069-before.log`, `/private/tmp/cr069-full.log`, `/private/tmp/cr069-full.xml`, `/private/tmp/cr069-after-build.log`, `/private/tmp/cr069-core-full.log` and `/private/tmp/cr069-core-results/cr069-core.trx`. Avalonia used its executable xUnit runner; Core used VSTest with local socket access after the sandbox blocked that connection. No interactive desktop verification, packaging or release was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:349`; `AnalysisITC.Avalonia/Workspace/Analysis/ModelOptionRowBuilder.cs:324`; validation=source; tests/UI=not-run. Designer uses explicit concentration/Kd/molar-energy conversions and validates before applying; untouched text retains precision. DesignerCompetitorOptionTests covers units, invalid inputs and ordinary editor parity.

## CR-070 — Several export actions let filesystem failures escape the UI boundary

- Priority: Medium
- Status: Resolved (2026-10-16).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Tools/AnalysisResultExporterWindow.cs:71`; `AnalysisITC.Avalonia/Misc/Tools/SupportingFigureCanvasWindow.cs:244`; `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1233`; `AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs:520`
- Related: None
- Problem: Avalonia table, supporting-figure and selected-figure actions, plus associated-figure loops on both desktops, lack handled-error boundaries around output writes.
- Failure scenario: An accepted export destination becomes disconnected, full or unwritable. WriteAllTextAsync or File.Create throws through the UI action.
- Consequence: An ordinary output failure becomes an unhandled UI exception, and batches can stop after partial completion. Other export paths handle equivalent errors; actual process termination was not established.
- Suggested direction: Handle failures at the complete export-action boundary and make any partial batch completion clear.
- Resolution: Core now supplies protected per-file writes, batch completion/replacement counts, shared failure messages, and figure filename planning. Figure, result-table, Supporting Figure, and numerical export actions on both desktops handle preparation and output failures, stop at the first failed file, and report one handled error without a success message. Each output is finished in a sibling temporary file before being committed; only previously existing, approved destinations can be replaced. New destinations and approved destinations that disappear use a no-overwrite move, preserving a file that appears after confirmation. A failed batch that already replaced files explicitly identifies the mix of old and new exports and explains which planned outputs were not written. Active/All figure batches now share associated export's confirm-before-replace policy; suffixes resolve only within-batch name collisions, with natural names reserved first. Both desktops use project/date subfolders for Active/All and result-name subfolders for associated exports. Saved member-fit references, captured model choices, scientific values, numerical formats, and each desktop's renderer are retained. The manual and shared help describe the changed destination and overwrite behavior.
- Validation: A probe against pre-change Avalonia binaries reproduced the original associated-export failure: the first PDF was written, then the second destination raised an escaping `UnauthorizedAccessException`. Final Core suite: 3,793 passed, one existing skip, zero failures. Final Avalonia suite: 578 passed, one existing opt-in skip, zero failures or runner errors. Regressions cover complete action boundaries, declined confirmations and Avalonia dialog closure, repeated replacement without extra suffix files, protected previous files, late unapproved destinations, approved destinations disappearing, partial/mixed batches, and write/flush/disposal/replacement/cleanup failures. Focused native `FinalFigureModelSelectionTests`, `ProtectedNativeExportTests`, and `SupportingFigureCanvasTests` pass with real CoreGraphics output and filesystem writes; native alert configuration and handling of Cancel, Overwrite and unexpected responses are tested through injected modal results. Live native modal interaction could not be verified: both the new timer-driven check and the existing `FtxtcDuplicatePromptTests` control timed out with `com.apple.hiservices-xpcservice` errors, including outside the sandbox. Logs/results are `/private/tmp/cr070-baseline-probe/reproduction.log`, `/private/tmp/cr070-core-final.log`, `/private/tmp/cr070-avalonia-final.log`, `/private/tmp/cr070-native-focused.log`, and `/private/tmp/cr070-validation/`. No packaging, signing, or release scripts were run.

- Commit isolation (2026-10-16): An isolated HEAD snapshot with only the CR-070 patch builds and passes all 57 focused Core checks, all 41 focused Avalonia checks, and the three named native harnesses. The full-suite totals above describe the shared working tree, which also contained unrelated pending changes. Isolated logs/results are `/private/tmp/cr070-commit-idx8ygac/`. Live native modal interaction retains the limitation recorded above.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataExport/ExportBatchWriter.cs:34`; `AnalysisITC.Core/DataExport/ExportBatchWriter.cs:107`; validation=source; tests/UI=not-run. UI export actions use protected temporary writes, approved replacements, no-overwrite late destinations and partial-batch handled errors; each desktop retains its renderer.

## CR-071 — Bootstrap unlocking is bypassed when every shared coordinate is locked

- Priority: Medium
- Status: Moved to ITC-118 and deferred by user (2026-10-17); not fixed and not for solving now.
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/ParameterSet.cs:282`; `AnalysisITC.Core/Analysis2/Solver2.cs:1410`; `AnalysisITC.Core/Analysis2/Models/Models.cs:472`; `AnalysisITC.Core.Tests/GlobalUncertaintyCloneTests.cs:18`
- Related: ITC-118, ITC-052, ITC-083, ITC-090, ITC-094
- Problem: When primary fitting requires only individual solves, bootstrap uses member clones that retain IsGloballyDetermined. Unlock clears IsLocked but still does not admit shared coordinates to fitting.
- Failure scenario: Fit eligible multiple experiments with shared locked Offset or N, remaining coordinates local, residual bootstrap and Unlock parameters enabled.
- Consequence: Shared coordinates remain fixed in all replicas despite the recorded setting. Primary estimates are unchanged; uncertainty magnitude or bias was not measured. Existing tests exercise the correct global helper directly, bypassing real dispatch.
- Tracker handoff: [ITC-118 — Bootstrap unlocking of locked shared parameters needs a solver policy](ISSUES.md#itc-118---bootstrap-unlocking-of-locked-shared-parameters-needs-a-solver-policy) now holds the runtime reproduction, test gap, design alternatives, performance concerns, and open assessment/uncertainty decisions. A subsequent local Core probe confirmed zero bootstrap spread for locked shared Offset and N through the actual Analyze route. Neither switching only bootstrap dispatch nor running the whole solver globally has been selected as the implementation policy. Investigation and discussion are deferred; no solver fix was made.

- Recheck: **2026-10-19; DEFERRED**; evidence=`AnalysisITC.Core/Analysis2/Solver2.cs:1515`; `AnalysisITC.Core/Analysis2/Models/Models.cs:470`; `AnalysisITC.Core/Analysis2/ParameterSet.cs:28`; validation=source; tests/UI=not-run. Independent dispatch and globally-determined exclusion remain; the recorded actual-route reproduction still applies. Explicit user deferral retained.
- Depends: [ITC-118](ISSUES.md#itc-118---bootstrap-unlocking-of-locked-shared-parameters-needs-a-solver-policy). Existing questions only: common versus member-specific unlocked coordinates; primary/uncertainty dispatch; participating members and no-binding skipping; replicate retention/failure/cancellation and presentation. Neither a bootstrap-only route change nor always-global fitting has been selected. Do not reopen this work.

## CR-072 — Independent child solvers discard explicit iteration and tolerance settings

- Priority: Low
- Confidence: Confirmed
- Category: Design and maintainability
- Location: `AnalysisITC.Core/Analysis2/Solver2.cs:1577`; `AnalysisITC.Core.Tests/ProfileLikelihoodWorkflowTests.cs:167`
- Related: None
- Problem: Independent global fitting copied several parent settings into child solvers but omitted MaxOptimizerIterations and SolverToleranceModifier.
- Failure scenario: The existing `IndependentGlobalSolverProfilesEachMemberAndAggregatesSides` workflow test sets the parent maximum to 90 optimizer iterations. Before the fix, children use the application default of 20,000, so their recorded profile candidate limit is 6,666 instead of 30. The test previously checked profile results without checking this limit.
- Consequence: The test's explicit fitting budget is ignored by independent children. Ordinary desktop parents and children normally share application defaults; no desktop symptom has been established.
- Suggested direction: Pass effective parent solver settings into independent children and assert the effective profile limit in the existing workflow test.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Analysis2/Solver2.cs:1577`; `AnalysisITC.Core.Tests/ProfileLikelihoodWorkflowTests.cs:214`; validation=regression. Both weighted/unweighted cases failed before the fix (expected 30, actual 6,666) and passed afterward (2/2) using an in-process xUnit runner; UI not run.
- Resolution: Added the two missing parent-to-child assignments in the existing construction path. The existing derived bootstrap/profile caps and uncertainty tolerance policy are unchanged. The focused regression asserts each child's recorded profile candidate limit.
- Validation: `AnalysisITC.Core.Tests`: 4,365 passed, 1 failed, 1 skipped (4,367 total), using in-process xUnit because standard VSTest cannot open its sandbox socket. The sole failure, `FitInformationCriteriaTests.OverviewTableShowsMemberAiccAndAicFallback` (`FitInformationCriteriaTests.cs:291`), also fails in isolation: it expects a report overview information-criteria column and does not run the modified independent-fit path. Left outside this fix.
- Target: explicit parent settings → identical effective child limits.
- Strategy: copy MaxOptimizerIterations and SolverToleranceModifier when constructing independent children, before primary fitting and deferred uncertainty; reuse existing derived bootstrap/profile caps. Preserve algorithm, locks, assessments and admission.
- Verify: extend the existing weighted/unweighted workflow test to assert that every child's recorded profile candidate limit is 30 for the parent's explicit 90-iteration budget; run `AnalysisITC.Core.Tests`.
- Questions: none; no solver-policy redesign.

## CR-073 — Attempt Fix repeats the incomplete-injection warning without fixing it

- Priority: Low
- Confidence: Confirmed
- Category: Correctness
- Related: CR-015 / ITC-115; ITC-107 remains metadata-only.
- Original failure: The near-end warning offered a repair that retained positive-time injection markers. An injection at 150 s with a trace ending at 155 s therefore repeated the warning after Attempt Fix; Keep or Discard still allowed the user to exit.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/DataReaders/DataValidator.cs:128`; `AnalysisITC.Core.Tests/MicroCalImportPolicyTests.cs:154`; validation=source and executed tests. The unchanged near-end predicate now offers only Keep/Discard and explains incomplete baseline/integration coverage. Neither this warning nor the separate out-of-range warning offers injection deletion.
- Verification: The 150–155 s scenario passes through production import: Keep retains the recorded shot, Discard skips the experiment, and neither offers a repair. Included in 44 passing focused Core import regressions. Avalonia preference/dialog tests: 31 passed. Actual native modal buttons/actions and the preferences storyboard layout pass. Full Core suite: 4,447 passed, 1 skipped (4,448 total); the final focused import set also passed after adding the last missing-marker cases.
- Resolution: Recorded injections outside the trace reject new raw imports; saved-project Keep preserves their state. Never-reached trailing planned MicroCal shots are automatically omitted by stream-marker evidence before bookkeeping, with no confirmation or deletion setting.

## CR-074 — An exactly zero heat-capacity estimate disappears from summaries

- Priority: Low
- Status: Closed — no change (maintainer decision, 2026-10-22).
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:359`; `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:381`; `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:380`
- Related: None
- Problem: Summary row presence depends on a nonzero slope rather than whether ΔCp is a modeled quantity with uncertainty.
- Failure scenario: A temperature-dependent enthalpy result has an exactly zero ΔCp center and positive uncertainty. The evaluator returns it, but the summary builder removes its row; numerical export can retain it.
- Consequence: Results and reports omit the estimate and interval. Exact-zero uncertain fits were not demonstrated in ordinary data, so practical frequency is limited; constant SameForAll enthalpy is intentionally a different case.
- Suggested direction: Determine row presence from the model constraint and availability, allowing a valid zero estimate without adding ΔCp to constant-enthalpy models.

- Recheck: **2026-10-22; CLOSED** by maintainer decision; no new tests or runtime verification.
- Resolution: No change. The maintainer considers an exactly zero fitted ΔCp outside naturally occurring results and does not want this case addressed. Existing summary and export behavior is retained; no implementation or further verification is planned.

## CR-075 — Bootstrap progress retains every permanent status entry until completion

- Priority: Low
- Confidence: Confirmed
- Category: Design and maintainability
- Location: `AnalysisITC.Core/StatusBarManager.cs:407`; `AnalysisITC.Core/Analysis2/Solver2.cs:1238`; `AnalysisITC.Avalonia/AnalysisProgressCoordinator.cs:146`; `AnalysisITC.MacOS/Platform/MacCalculationProgress.cs:39`
- Related: ITC-098, ITC-109
- Problem: Each delivered bootstrap update appends a permanent priority-1 status; replacement removes only lower-priority entries.
- Failure scenario: Run the offered 10,000-bootstrap setting. At least 10,000 entries accumulate, requiring at least 49,995,000 preceding-entry predicate checks across RemoveAll calls.
- Consequence: The UI thread performs avoidable allocation and scanning even when phase text is unchanged. Completion clears the entries, so this is bounded overhead, not a persistent leak; measurable responsiveness loss was not demonstrated.
- Suggested direction: Coalesce the operation's permanent status while preserving the intentional timed-message queue and progress counters.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/StatusBarManager.cs:414`; `AnalysisITC.Core.Tests/StatusBarManagerTests.cs:44`; `AnalysisITC.Avalonia.Tests/AnalysisProgressCoordinatorTests.cs:48`; `AnalysisITC.MacOS.Tests/UpdateResultStatusTests.cs:159`; validation=fail-first-and-regression. Before the fix, both 10,000-update Core cases retained all permanent messages; Avalonia retained 10,001 entries including the initial fitting status, and the native 10,000-update storage check also failed. Both desktop paths now retain one permanent entry and clear it on completion. Real 10,000-refit execution and responsiveness measurement were not performed.
- Resolution: `SetStatusMessage` removes existing permanent messages at equal priority only when the incoming message is permanent, alongside its existing lower-priority removal. Timed statuses, queued statuses, locking, notifications and expiry retain their existing behavior. Solver ordering/counter/fraction retention, operation ownership and both desktop coordinators are unchanged; no public API or operation-ID layer was added.
- Working-tree validation: All 45 focused Core status/progress-ordering cases and all 19 Avalonia coordinator cases pass, including permanent priorities, timed expiry, queue FIFO/priority/pause/clear, decreasing reports, reversed dispatch, member transitions, obsolete operations, Stop and completion. The native `UpdateResultStatusTests` harness passes its 10,000 synthetic bootstrap reports and existing late/lower-progress, Stop, stale-operation, success/cancellation/failure and Update Result checks. Evidence: `/tmp/cr075-core-before.log`, `/tmp/cr075-core-focused.log`, `/tmp/cr075-avalonia-before.log`, `/tmp/cr075-avalonia-after.log`, `/tmp/cr075-native-before.log`, `/tmp/cr075-native-after.log`. No manual/help changes are needed for this internal storage correction.
- Isolated commit validation: A temporary HEAD snapshot with only the CR-075 patch passes all 19 Core status cases, all 14 Avalonia coordinator cases and the native `UpdateResultStatusTests` harness. The desktop storage regressions do not depend on the separate uncommitted progress-indicator presentation changes. Evidence: `/tmp/cr075-commit-8mnsb85u/core-tests.log`, `/tmp/cr075-commit-8mnsb85u/avalonia-tests.log`, `/tmp/cr075-commit-8mnsb85u/native-tests.log`.
- Full Core working-tree validation: `AnalysisITC.Core.Tests` passed with 4,455 passed, 0 failed and 1 existing external-reference diagnostic skipped (4,456 total). Evidence: `/tmp/cr075-core-full.log` and `/tmp/cr075-test-results/cr075-full-core.trx`.
- Target: progress updates → bounded status storage.
- Strategy: in StatusBarManager.SetStatusMessage, an incoming permanent message replaces existing permanent messages of equal priority as well as the already-removed lower priorities; retain timed-message/queue behavior. Older equal-priority permanent messages cannot reappear under the current expiry/clear rules; no operation-ID layer needed.
- Verify: 10,000 updates leave bounded storage and the latest text; timed expiry still reveals the prior permanent status, with queue priority/pause/clear and Stop/completion checks on both desktop coordinators.
- Questions: none.

## CR-076 — The persistence stability test cannot detect swapped wire identities

- Priority: Low
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Core.Tests/FTXTCFormatTests.cs:2261`; `AnalysisITC.Core/DataExport/FTXTCPersistenceRegistry.cs:137`; `AnalysisITC.Core/DataExport/FTXTCPersistenceRegistry.cs:193`
- Related: None
- Problem: The registry stability test checks a bijection through the same map in both directions, without independently pinning every stable identity.
- Failure scenario: A maintenance change swaps the literal Histidine and Imidazole IDs. Round-trip assertions still pass, but saved histidine values would deserialize as the other buffer.
- Consequence: The named stability test cannot guard this backward-compatibility contract and leaves registry changes dependent on manual auditing. No current wrong mapping or released-user artifact is alleged; some other IDs already have literal assertions.
- Suggested direction: Add independent literal identity expectations alongside the current coverage and inverse-map checks.

- Recheck: **2026-10-19; OPEN**; evidence=`AnalysisITC.Core.Tests/FTXTCFormatTests.cs:2261`; `AnalysisITC.Core/DataExport/FTXTCPersistenceRegistry.cs:137`; validation=source; tests/UI=not-run. Bijection through the same map cannot detect swapped stable identities. Test-contract gap; no current wrong mapping found.
- Target: changed stable wire identity → failing compatibility regression.
- Strategy: add independently reviewed literal enum→wire expectations for every registry family, including buffers; retain coverage/uniqueness/inverse checks and representative serialized-attribute checks. Derive expectations from format specification, reviewed identity history and buffer catalogue, never generate expected IDs using the map under test. Verify Histidine/Imidazole swap would fail both directions; existing files retain their identities.
- Questions: none.
- Depends: FTXTC_FORMAT.md; no production mapping/schema change and .ftitc remains read-only.

## CR-077 — Same-name experiments lose fixed-parameter entries from reports

- Status: Resolved (2026-10-19).
- Priority: Low
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1516`; `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1539`
- Related: ITC-048, ITC-051
- Scope correction: The original different-local-fixed-values scenario is not exposed by multi-experiment setup: a parameter must be shared before it can be fixed. Single-experiment setup does expose local locks.
- Problem: A11 scanned member projections of shared fixed parameters and deduplicated them by display label before also listing the shared coordinates. This made a redundant inventory depend on experiment names.
- Resolution: Multi-experiment A11 now lists each locked shared coordinate once as `Shared <Parameter>`, directly from the shared parameter table. Single-experiment fixed rows remain. Removed display-label deduplication and the helper's unused single-solution argument; formatting, units, uncertainty omission, report-purpose policy and persistence are unchanged. Updated A11 in the report map; no new member-identity or fallback handling was added.
- Initial validation: Four regression cases failed before the fix on redundant member rows. All six new Standard/Diagnostic cases and the retained single-experiment fixed-value test pass, covering same-name members, renaming, shared coordinates, affinity/unit formatting, uncertainty omission, unlocked coordinates and absent A11. Full Release Core suite: 4,331 passed, one existing opt-in external-reference skip. Release Avalonia report checks: 77 passed. Native figure-pair checks and all nine existing renderer checks pass. The full native report-reference harness stops in its unrelated picker fixture because it configures buffer subtraction on a tandem experiment, now forbidden by the working-tree policy; the nine renderer checks were invoked separately through a temporary runner. `git diff --check` passes. Evidence: `/private/tmp/cr077-before-tests.log`, `/private/tmp/cr077-core-full.log`, `/private/tmp/cr077-core-results/cr077-core.trx`, `/private/tmp/cr077-avalonia-report-tests.log`, `/private/tmp/cr077-macos-report-tests.log`, `/private/tmp/cr077-macos-figure-tests.log` and `/private/tmp/cr077-native-renderer-checks.log`. No packaging, release or interactive desktop verification was performed.
- Fixture follow-up (2026-10-19): Replaced the unsupported tandem correction setup with a real two-segment tandem plus a separate corrected ordinary member in the same result. The source retains both buffer-reference and tandem-source roles. The Avalonia picker and PDF fixtures now use actual concatenation and assert tandem identity, two segments, no tandem correction, combined appendix roles and missing-source metadata. The complete native report-reference and tandem-restriction harnesses pass; all 83 Avalonia report/restriction cases and 75 Core restriction/provenance cases pass. Production restrictions remain unchanged. Evidence: `/private/tmp/picker-fixture-native-tests.log`, `/private/tmp/picker-fixture-avalonia-tests.log` and `/private/tmp/picker-fixture-core-tests.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1518`; `AnalysisITC.Core.Tests/AnalysisReportBuilderTests.cs:234`; validation=source; tests/UI=not-run. A11 lists each locked shared coordinate once, retaining single-fit locks. Same-name/rename tests remain; current picker fixtures use actual tandem plus separately corrected ordinary member.

## CR-078 — A native preference describes project reprocessing instead of import recalculation

- Priority: Low
- Status: Resolved (2026-10-19).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/Preferences.storyboard:1104`; `AnalysisITC.MacOS/ViewControllers/SettingsViews/MacPreferencesPaneControllers.cs:554`; `AnalysisITC.Core/DataReaders/IntegratedHeatReader.cs:306`
- Related: ITC-036
- Problem: Reprocess integrated heats when loading a project actually supplies the default concentration/ratio-recalculation choice for integrated-table import.
- Failure scenario: Change the checkbox expecting reopened projects to be reprocessed. It instead changes the initial option for subsequent .dat/.aff imports.
- Consequence: The label describes the wrong operation and scientific inputs. The correctly labeled per-import dialog limits the impact; no saved project heats are rewritten by this preference.
- Suggested direction: Describe the import concentration/ratio recalculation default explicitly, matching the dialog and manual.

- Resolution (2026-10-19): Both desktop controls now read **Recalculate concentrations on integrated-heat import** and use the shared Core `IntegratedHeatImportPresentation.PreferenceTooltip`: "Initial .dat/.aff import choice for recalculating concentrations and ratios." The manual row uses the new label and retains its explanation; existing in-app import help now identifies the preference and per-import override. Control identifiers, stored key/default, calculations and serialization are unchanged.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Preferences.storyboard:1096`; `AnalysisITC.MacOS/ViewControllers/SettingsViews/MacPreferencesPaneControllers.cs:601`; `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:98`; `AnalysisITC.Core/Presentation/IntegratedHeatImportPresentation.cs:5`; validation=source, builds, tests and rendered preference panes.
- Validation: Full Core suite: 4,455 passed, one skipped; focused preference/import tests: 47 passed; Avalonia preference tests: 27 passed. Avalonia builds and native managed compilation pass; native help rendering and the existing Swift preference-layout harness pass. Temporary harnesses exercised the actual Avalonia window and native storyboard/controller with an isolated in-memory settings store: both checkbox values save and restore, labels fit, and tooltips match Core. In both runtimes, `.dat` and `.aff` prompt-service checks receive each preference value, accept the opposite per-import choice, and leave the preference unchanged. Strict reopening of the three-experiment JORS `.ftxtc` fixture under both values retains identical raw/corrected heats, concentrations and ratios. Both rendered Processing panes were inspected. Interactive import dialogs and full application restarts were not manually exercised; no new repository tests, packaging or signing operations were added or run.
- Target: preference wording → correct .dat/.aff import expectation.
- Strategy: label both desktop controls 'Recalculate concentrations on integrated-heat import'; share a short Core tooltip identifying concentrations/ratios and initial import choice. Preserve stored key/default and per-import override; reuse the manual's existing explanation.
- Verify: each app saves/restores the option; .dat/.aff dialog initializes accordingly, while .ftxtc reopening retains saved heats/concentrations.
- Questions: none; no calculation, serialization or general reprocessing change.

## CR-079 — Copy to Next Peak is enabled before its hidden zoom requirement is met

- Priority: Low
- Status: Resolved (2026-10-19).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:487`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1141`; `AnalysisITC.MacOS/GraphViews/DataProcessingGraphView.cs:634`
- Related: None
- Problem: The native button enables for a selected nonfinal injection, but its handler additionally requires that injection to be zoomed.
- Failure scenario: In All Peaks view, single-click a nonfinal injection and press Copy to Next Peak. The enabled action returns without copying or advancing.
- Consequence: The documented selection-based workflow silently does nothing. Focusing the injection first is a workaround; Avalonia supports ordinary selection.
- Suggested direction: Align the enabled state and action prerequisites, preferably supporting the documented selected-injection workflow.
- Resolution (2026-10-19): The native button and Space shortcut copy from a selected nonfinal injection at any zoom level, then retain the existing advance-and-focus behavior. Both handlers reject the final injection rather than copying onto itself. The manual already describes the selection-based workflow.
- Validation: All 24 archived native processing-copy cases pass, including button and Space dispatch from All Peaks and focused views, both start-boundary preferences, unchanged source boundaries, advancing/focusing, final-injection and missing-selection no-ops. Native processing-slider and integration-drag harnesses also pass. The new All Peaks cases reproduced the no-op before the fix. Log: `/private/tmp/cr079-after.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1146`; `AnalysisITC.MacOS.Tests/ProcessingCopyButtonsTests.cs:271`; validation=source; tests/UI=not-run. Button/Space copy from selected nonfinal injection at any zoom, advance/focus, and reject missing/final selections; matches Avalonia.

## CR-080 — Back in account creation enters an invalid administration workflow

- Priority: Low
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/InteractiveAdminTool.cs:314`; `AnalysisITC.Web/InteractiveAdminTool.cs:929`; `AnalysisITC.Web/OperatorCodeRegistry.cs:55`
- Related: None
- Problem: Create Account handles a null tier selection but fails to handle the explicit back result.
- Failure scenario: Choose Back in the access-level menu, continue through expiry and confirm the proposal. The proposal displays Public, but creation receives back as its tier.
- Consequence: Validation throws an uncaught exception in the administration console. No account is created; the separately running web service is unaffected. Escape/Backspace and declining confirmation avoid the path.
- Suggested direction: Treat explicit Back as cancellation consistently with Change Tier and test it separately from keyboard dismissal.

- Resolution (2026-10-19): Create Account now returns immediately for either null or the existing `back` tier-menu result, matching Change Tier. Existing pause suppression returns directly to the account menu without expiry options, creation confirmation, or registry mutation. Registry tier validation remains unchanged; no service, schema, provider, or cancellation-message changes.
- Validation: Nine new workflow cases cover scripted and keyboard explicit Back, Escape and Backspace separately, byte-for-byte preservation of an existing registry, no follow-on expiry/confirmation/pause, and a valid Advanced account creation after each scripted cancellation. Against the original guard, all three explicit-Back cases failed while the six Escape/Backspace cases passed. After the fix, all 78 `OperatorAndUsageTests` passed, including a final rebuild after the baseline comparison. Full Web suite: 468 passed, one unrelated failure in `CompleteStoredPackagePassesStrictLocalContractWithHistoricalEvidence(compact: True)` (expected HTTP 413, actual 503); the same endpoint failure reproduces with the original guard (its noncompact case passes). Logs: `/private/tmp/cr080-before-clean.log`, `/private/tmp/cr080-focused-after.log`, `/private/tmp/cr080-focused-final.log`, `/private/tmp/cr080-web-suite.log`, `/private/tmp/cr080-unrelated-original-guard.log`.
- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Web/InteractiveAdminTool.cs:320`; `AnalysisITC.Web.Tests/OperatorAndUsageTests.cs:166`; validation=source and automated workflows; manual terminal UI=not-run.

## CR-081 — Unknown fallback token usage is reported as a complete subtotal

- Priority: Low
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/InterpretationUsageStore.cs:217`; `AnalysisITC.Web/Program.cs:904`; `AnalysisITC.Web/InteractiveAdminTool.cs:636`
- Related: None
- Problem: SQL SUM ignores unknown attempt usage and returns the known subtotal, which is stored and displayed as ordinary execution token totals.
- Failure scenario: A retrieval attempt records 1,000 input and one output token, then its permitted fallback times out without usage. The execution displays 1,000/1 although the fallback contribution is unknown.
- Consequence: Administrative token figures appear complete while understating usage. Cost remains unresolved and attempt receipts remain available, so this root does not itself break quota protection.
- Suggested direction: Represent token completeness separately from known subtotals and preserve that distinction through storage and administrative output.

- Recheck: **2026-10-19; OPEN**; evidence=`AnalysisITC.Web/InterpretationUsageStore.cs:250`; `AnalysisITC.Web/Program.cs:913`; `AnalysisITC.Web/InteractiveAdminTool.cs:630`; validation=source; tests/UI=not-run. SUM drops null attempt counters; final summary/admin output presents a known subtotal as complete after a fallback without usage. Cost liabilities remain protected.
- Target: incomplete fallback usage → explicitly partial totals.
- Strategy: derive per-counter completeness from attempt_starts LEFT JOIN nullable receipt counters; preserve known subtotals and mark summary-only historical evidence unknown. Share this aggregation with final request projection, execution_usage, account/detail/CLI summaries and CSV; no new durable flag needed. Verify known 1,000/1 then missing fallback → partial 1,000/1 everywhere; complete, omitted-counter and legacy cases retain correct meaning.
- Questions: confirm partial-subtotal display (recommended) versus unavailable aggregates. Never infer omitted counters as zero; costs, liabilities and admission stay unchanged.

## CR-082 — Historical tight-tolerance diagnostics do not establish their claimed tolerance

- Priority: Low
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_tight_reference.py:12`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_tight_reference.py:18`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_report.py:184`
- Related: None
- Problem: The optional historical generator labels its output with xtol_molar = 2e-14 without setting or verifying the installed pytc solver tolerance.
- Failure scenario: Run it with the documented ordinary pinned pytc environment, whose binding-polynomial default is 2e-12.
- Consequence: Default-arithmetic curves are labeled as tight-tolerance diagnostics. This affects the optional historical workflow, not the current 52-case precision acceptance suite.
- Suggested direction: Construct and verify the claimed external tolerance or reject environments where it cannot be established.

- Recheck: **2026-10-19; OPEN**; evidence=`AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_tight_reference.py:18`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/precision_reference.py:92`; validation=source; tests/UI=not-run. Historical generator labels 2e-14 without setting/verifying external root tolerance. Current 52-case precision workflow is separate and verifies its patch.
- Target: optional tight diagnostic → demonstrated external tolerance.
- Strategy: reuse the existing external-generator setup/verification approach where compatible, explicitly configure 2e-14 in the pinned pytc source/build and verify the invoked root solver; record effective tolerance and external identity, fail before emitting mislabeled evidence if unavailable. Do not substitute local equations. Verify ordinary 2e-12 environment is rejected or rebuilt; instrument effective argument and compare tighter-run convergence.
- Questions: retain and repair this optional workflow, or retire its tight diagnostic? Current precision acceptance stays unchanged.

## CR-083 — Historical reporting tests assert retired document contents and filenames

- Priority: Low
- Status: Resolved (2026-10-19).
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/test_generate_report.py:56`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/test_generate_report.py:145`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_report.py:579`
- Related: None
- Problem: Tests expect old practical-limit tables in README and newly generated content in REPORT, although those files now redirect to precision results and generation writes DEFAULT_TOLERANCE_REPORT.
- Failure scenario: Run the historical documented unittest command. The README assertion fails; the lifecycle test reads the unchanged copied redirect instead of the newly generated report.
- Consequence: Intended current artifact layout causes false regression failures in historical reporting maintenance. This was established from source, not a suite execution.
- Suggested direction: Target retained historical documents and the actual generated filename while keeping the current precision redirects intact.
- Resolution: The frozen-count/percentage/benchmark assertions now read `DEFAULT_TOLERANCE_NOTES.md`, and the generation lifecycle test reads `DEFAULT_TOLERANCE_REPORT.md`. The lifecycle test removes the copied historical report before generation to require fresh output and verifies that both precision redirects (`README.md` and `REPORT.md`) remain byte-for-byte unchanged. The generator, scientific fixtures and current precision documents are unchanged.
- Validation: The documented unittest discovery command reproduced both stale-document failures (14 passed, two failed) before the change; all 16 reporting tests pass afterward using the pinned `report-requirements.txt` dependencies in a temporary Python 3.12 environment. The lifecycle test generates and checks Markdown, a 22-page PDF and report evidence in a temporary copy, with existing plot placeholders retained. `git diff --check` passes for the changed files. Logs: `/private/tmp/cr083-before.log` and `/private/tmp/cr083-after.log`.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/test_generate_report.py:56`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/test_generate_report.py:145`; validation=source; tests/UI=not-run. Historical tests target DEFAULT_TOLERANCE_NOTES/REPORT, require fresh generated Markdown and preserve both precision redirects; generator filename matches.

## CR-084 — Correcting a tandem-import option requires restarting the import

- Priority: Low
- Status: Resolved (2026-10-19).
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/Platform/MacTandemImportPromptService.cs:64`; `AnalysisITC.Core/DataReaders/MicroCalITC200Reader.cs:163`; `AnalysisITC.Core/DataReaders/DataReaders.cs:336`
- Related: ITC-026, ITC-079
- Problem: Native tandem import dismisses its options dialog before validating compensation inputs.
- Failure scenario: Import a valid concatenated MicroCal file, enter dead volume 0 or mixing percentage 200, then choose back-mixing compensation.
- Consequence: A handled per-file error skips the current import; correcting the typo requires reopening the file and reentering choices. Remaining files continue, with no crash or persisted-data loss established.
- Suggested direction: Keep the dialog and entered choices available until validation succeeds, consistent with Avalonia.
- Resolution: The native compensation button validates before ending the modal dialog. Invalid input displays a wrapping error in the same dialog and selects the field to correct; both entered values and the overflow choice remain available. Valid compensation input completes the current import, and choosing MicroCal remains available after a validation error.
- Validation: A native modal regression reproduced the original `FormatException` with dead volume 0 before the fix. All seven final native scenarios pass, covering repeated correction of dead volume 0 and mixing fraction 200, retained drafts and defaults, switching to MicroCal after an error, 0%/100% boundaries, decimal-comma and invariant input, and a real three-segment MicroCal import completed with corrected compensation settings. The managed native build and `git diff --check` pass. Evidence: `/private/tmp/cr084-before.log` and `/private/tmp/cr084-final.log`. No packaging or release was performed.

- Recheck: **2026-10-19; FIXED**; evidence=`AnalysisITC.MacOS/Platform/MacTandemImportPromptService.cs:81`; `AnalysisITC.MacOS.Tests/TandemImportPromptTests.cs:45`; validation=source; tests/UI=not-run. Compensation validates before modal completion; invalid drafts remain editable and MicroCal remains available. Source coverage includes correction of the same three-segment import.

## Review coverage

Inventory used gpt-6-luna with medium reasoning. Scientific Core, persistence, processing, concurrency-heavy units and scientific-validation tooling used gpt-6-astra with high reasoning; UI, ordinary Web and test units used gpt-6.1-sol with high reasoning. Five separate cross-cutting reviews covered all nine desktop feature pairs, repeated logic and tests versus high-risk Core. Nineteen independent gpt-6-astra high-reasoning verification batches checked candidates against surrounding code, callers, guards, intended behavior, planning records and existing ITC scope.

All units below were reviewed. Paths are representative entry points into each assigned feature area; line counts cover the full unit, include comments and blank lines, and are not counts of changed code. Main storyboard ranges reflect the inventory before concurrent edits shortened that file.

| Unit | Area | Representative paths | Lines | Reviewer |
|---|---|---|---:|---|
| U01 | Core models and global constraints | `AnalysisITC.Core/Analysis2/Models/Models.cs`; `AnalysisITC.Core/Analysis2/GlobalModel.cs`; `AnalysisITC.Core/Analysis2/ParameterSet.cs` | 5,329 | gpt-6-astra high |
| U02 | Core solvers and uncertainty | `AnalysisITC.Core/Analysis2/Solver2.cs`; `AnalysisITC.Core/Analysis2/SolverUtilities.cs`; `AnalysisITC.Core/Analysis2/ProfileLikelihoodEstimator.cs` | 6,017 | gpt-6-astra high |
| U03 | Core analysis setup and update lifecycle | `AnalysisITC.Core/Analysis2/ModelFactory.cs`; `AnalysisITC.Core/Analysis2/AnalysisBuilder.cs`; `AnalysisITC.Core/Analysis2/AnalysisResultUpdater.cs` | 3,741 | gpt-6-astra high |
| U04 | Core processing and experiment state | `AnalysisITC.Core/DataProcessing.cs`; `AnalysisITC.Core/DataClasses/ExperimentData.cs`; `AnalysisITC.Core/DataClasses/CorrectedPeakEndEstimator.cs` | 5,670 | gpt-6-astra high |
| U05 | Core tandem and instrument imports | `AnalysisITC.Core/DataReaders/ProcessedTandemCsvReader.cs`; `AnalysisITC.Core/TandemMixingScan.cs`; `AnalysisITC.Core/DataReaders/NanoItcReader.cs` | 5,161 | gpt-6-astra high |
| U06 | Core FTXTC persistence and viewer restoration | `AnalysisITC.Core/DataExport/FTXTCFormat.cs`; `AnalysisITC.Core/DataReaders/FTXTCReader.cs`; `AnalysisITC.Core/Viewer/ViewerDocumentReader.cs` | 6,819 | gpt-6-astra high |
| U07 | Core legacy import and numerical export | `AnalysisITC.Core/DataReaders/FTITCReader.cs`; `AnalysisITC.Core/DataReaders/OriginProjectReader.cs`; `AnalysisITC.Core/DataExport/Exporter.cs` | 6,121 | gpt-6-astra high |
| U08 | Core scientific math and advanced analyses | `AnalysisITC.Core/Analysis2/BootstrapCorrelationAnalyzer.cs`; `AnalysisITC.Core/DataClasses/Buffers.cs`; `AnalysisITC.Core/Math/NumberStructs.cs` | 4,917 | gpt-6-astra high |
| U09 | Core report composition and layout | `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`; `AnalysisITC.Core/Presentation/AnalysisReport.cs`; `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs` | 5,940 | gpt-6.1-sol high |
| U10 | Core presentation figures and result policy | `AnalysisITC.Core/Presentation/PublicationFigure.cs`; `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs`; `AnalysisITC.Core/Presentation/NullModelComparisonPresentation.cs` | 6,300 | gpt-6.1-sol high |
| U11 | Core interpretation transport and evidence | `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs`; `AnalysisITC.Core/Interpretation/FtItcInterpretationClient.cs`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs` | 4,727 | gpt-6.1-sol high |
| U12 | Core state concurrency settings and result domain (1/2) | `AnalysisITC.Core/DataManager.cs`; `AnalysisITC.Core/DataClasses/AnalysisResult.cs`; `AnalysisITC.Core/DataClasses/AnalysisResultValiditySnapshot.cs` | 4,112 | gpt-6-astra high |
| U13 | Core state concurrency settings and result domain (2/2) | `AnalysisITC.Core/Properties/AppSettings.cs`; `AnalysisITC.Core/StatusBarManager.cs`; `AnalysisITC.Core/Properties/AppVersion.cs` | 3,950 | gpt-6-astra high |
| U14 | Core tests persistence import export (1/2) | `AnalysisITC.Core.Tests/FTXTCFormatTests.cs`; `AnalysisITC.Core.Tests/FtxtcImportResolverTests.cs`; `AnalysisITC.Core.Tests/FitInformationCriteriaTests.cs` | 6,919 | gpt-6.1-sol high |
| U15 | Core tests persistence import export (2/2) | `AnalysisITC.Core.Tests/SequentialPersistenceTests.cs`; `AnalysisITC.Core.Tests/ProjectWriterTests.cs`; `AnalysisITC.Core.Tests/ProjectInformationTests.cs` | 6,397 | gpt-6.1-sol high |
| U16 | Core tests processing and tandem | `AnalysisITC.Core.Tests/PeakFittingTests.cs`; `AnalysisITC.Core.Tests/InjectionDisplacementCalculatorTests.cs`; `AnalysisITC.Core.Tests/SplineConversionTests.cs` | 7,140 | gpt-6.1-sol high |
| U17 | Core tests report presentation interpretation (1/2) | `AnalysisITC.Core.Tests/AnalysisReportBuilderTests.cs`; `AnalysisITC.Core.Tests/AnalysisInterpretationTests.cs`; `AnalysisITC.Core.Tests/AnalysisReportEnhancementTests.cs` | 6,958 | gpt-6.1-sol high |
| U18 | Core tests report presentation interpretation (2/2) | `AnalysisITC.Core.Tests/ClassifiedNullFigureTests.cs`; `AnalysisITC.Core.Tests/AnalysisResultUpdaterTests.cs`; `AnalysisITC.Core.Tests/ClassifiedOutputPolicyTests.cs` | 5,352 | gpt-6.1-sol high |
| U19 | Core tests fitting scientific and lifecycle (1/4) | `AnalysisITC.Core.Tests/AnalysisWorkspaceSolutionRetentionTests.cs`; `AnalysisITC.Core.Tests/CompactModelInputWriterTests.cs`; `AnalysisITC.Core.Tests/BootstrapCorrelationAnalyzerTests.cs` | 6,542 | gpt-6.1-sol high |
| U20 | Core tests fitting scientific and lifecycle (2/4) | `AnalysisITC.Core.Tests/FileTypeFixtureTests.cs`; `AnalysisITC.Core.Tests/GaussianLikelihoodTests.cs`; `AnalysisITC.Core.Tests/LinkedThermodynamicUncertaintyTests.cs` | 6,629 | gpt-6.1-sol high |
| U21 | Core tests fitting scientific and lifecycle (3/4) | `AnalysisITC.Core.Tests/ProfileLikelihoodMappingTests.cs`; `AnalysisITC.Core.Tests/ParameterBoundaryContactTests.cs`; `AnalysisITC.Core.Tests/ProfileLikelihoodEstimatorTests.cs` | 6,690 | gpt-6.1-sol high |
| U22 | Core tests fitting scientific and lifecycle (4/4) | `AnalysisITC.Core.Tests/ResidualBootstrapTests.cs`; `AnalysisITC.Core.Tests/ProfileLikelihoodWorkflowTests.cs`; `AnalysisITC.Core.Tests/PublishedElifeTwoSiteSourceDataTests.cs` | 6,111 | gpt-6.1-sol high |
| U23 | Avalonia fitting and results workspaces | `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs`; `AnalysisITC.Avalonia/Workspace/Analysis/IntegratedHeatsGraphControl.cs`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs` | 7,740 | gpt-6.1-sol high |
| U24 | Avalonia processing figures and rendering (1/2) | `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs`; `AnalysisITC.Avalonia/Drawing/SkiaAnalysisReportRenderer.cs`; `AnalysisITC.Avalonia/Drawing/SkiaPublicationFontResolver.cs` | 4,542 | gpt-6.1-sol high |
| U25 | Avalonia processing figures and rendering (2/2) | `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs`; `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingWorkspaceControl.cs` | 4,187 | gpt-6.1-sol high |
| U26 | Avalonia reports and scientific tools | `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs`; `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs`; `AnalysisITC.Avalonia/Misc/Tools/SupportingFigureCanvasWindow.cs` | 5,076 | gpt-6.1-sol high |
| U27 | Avalonia preferences details dialogs and support | `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs`; `AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs`; `AnalysisITC.Avalonia/Misc/Details/ExperimentAttributeEditorControl.cs` | 6,330 | gpt-6.1-sol high |
| U28 | Avalonia application platform and controls (1/2) | `AnalysisITC.Avalonia/MainWindow.axaml.cs`; `AnalysisITC.Avalonia/Misc/Menus/AppMenuController.cs`; `AnalysisITC.Avalonia/MainWindow.axaml` | 4,643 | gpt-6.1-sol high |
| U29 | Avalonia application platform and controls (2/2) | `AnalysisITC.Avalonia/ThermogramGraphControl.cs`; `AnalysisITC.Avalonia/Workspace/WorkspaceControlBuilder.cs`; `AnalysisITC.Avalonia/Platform/AvaloniaImportPromptService.cs` | 4,292 | gpt-6.1-sol high |
| U30 | Avalonia tests workspace and science | `AnalysisITC.Avalonia.Tests/AnalysisWorkspaceControlTests.cs`; `AnalysisITC.Avalonia.Tests/IntegratedHeatsGraphControlTests.cs`; `AnalysisITC.Avalonia.Tests/ExperimentDesignerRetentionTests.cs` | 5,576 | gpt-6.1-sol high |
| U31 | Avalonia tests presentation and infrastructure (1/2) | `AnalysisITC.Avalonia.Tests/AnalysisReportEnhancementTests.cs`; `AnalysisITC.Avalonia.Tests/AnalysisReportRenderingTests.cs`; `AnalysisITC.Avalonia.Tests/BindingAssessmentPresentationTests.cs` | 4,869 | gpt-6.1-sol high |
| U32 | Avalonia tests presentation and infrastructure (2/2) | `AnalysisITC.Avalonia.Tests/ProjectInformationTests.cs`; `AnalysisITC.Avalonia.Tests/PublicationFontRenderingTests.cs`; `AnalysisITC.Avalonia.Tests/PreferencesTests.cs` | 4,181 | gpt-6.1-sol high |
| U33 | macOS storyboard UI wiring (1/3) | `AnalysisITC.MacOS/Main.storyboard:1-5801` | 5,801 | gpt-6.1-sol high |
| U34 | macOS storyboard UI wiring (2/3) | `AnalysisITC.MacOS/Main.storyboard:5802-11601` | 5,800 | gpt-6.1-sol high |
| U35 | macOS storyboard UI wiring (3/3) | `AnalysisITC.MacOS/Preferences.storyboard` | 3,135 | gpt-6.1-sol high |
| U36 | macOS drawing backends (1/2) | `AnalysisITC.MacOS/Drawing/CGGraph.cs`; `AnalysisITC.MacOS/Drawing/CoreGraphicsAnalysisReportRenderer.cs`; `AnalysisITC.MacOS/Drawing/BufferSubtractionGraph.cs` | 4,215 | gpt-6.1-sol high |
| U37 | macOS drawing backends (2/2) | `AnalysisITC.MacOS/Drawing/CoreGraphicsFigureCanvasRenderer.cs`; `AnalysisITC.MacOS/Drawing/FinalFigure.cs`; `AnalysisITC.MacOS/Drawing/GraphAxis.cs` | 3,672 | gpt-6.1-sol high |
| U38 | macOS analysis and result controllers (1/2) | `AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs`; `AnalysisITC.MacOS/PopOptionViewControllers/FinalFigureOptions/FinalFigureOptionsController.cs`; `AnalysisITC.MacOS/PopOptionViewControllers/ExperimentDetailsPopoverController.cs` | 4,285 | gpt-6.1-sol high |
| U39 | macOS analysis and result controllers (2/2) | `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs` | 3,941 | gpt-6.1-sol high |
| U40 | macOS processing graphs and figure controls | `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs`; `AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs`; `AnalysisITC.MacOS/GraphViews/CorrelationGraphControl.cs` | 5,453 | gpt-6.1-sol high |
| U41 | macOS reports and export controllers | `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs`; `AnalysisITC.MacOS/ViewControllers/SupportingFigureCanvasWindowController.cs`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs` | 4,660 | gpt-6.1-sol high |
| U42 | macOS tool preferences and detail controllers | `AnalysisITC.MacOS/ViewControllers/SettingsViews/MacPreferencesPaneControllers.cs`; `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs`; `AnalysisITC.MacOS/ViewControllers/ProjectInformationWindowController.cs` | 3,102 | gpt-6.1-sol high |
| U43 | macOS custom controls and lists | `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs`; `AnalysisITC.MacOS/CustomViews/OptionAdjustmentView.cs`; `AnalysisITC.MacOS/CustomViews/ParameterValueAdjustmentView.cs` | 5,851 | gpt-6.1-sol high |
| U44 | macOS application platform and remaining UI (1/2) | `AnalysisITC.MacOS/AppDelegate.cs`; `AnalysisITC.MacOS/AnalysisITC.MacOS.csproj`; `AnalysisITC.MacOS/HelpDocumentView.cs` | 4,675 | gpt-6.1-sol high |
| U45 | macOS application platform and remaining UI (2/2) | `AnalysisITC.MacOS/ViewControllers/MainWindowController.cs`; `AnalysisITC.MacOS/ViewControllers/MainViews/ViewController.cs`; `AnalysisITC.MacOS/Platform/MacIdentifierEditor.cs` | 4,475 | gpt-6.1-sol high |
| U46 | macOS test harnesses | `AnalysisITC.MacOS.Tests/AnalysisReportReferencesTests.cs`; `AnalysisITC.MacOS.Tests/ProjectInformationTests.cs`; `AnalysisITC.MacOS.Tests/ProcessingInitializationTests.cs` | 6,356 | gpt-6.1-sol high |
| U47 | Web interpretation service and authorization | `AnalysisITC.Web/OpenAIInterpretationProvider.cs`; `AnalysisITC.Web/DailyStatusEmail.cs`; `AnalysisITC.Web/RegistrationDeliveryOutbox.cs` | 5,257 | gpt-6-astra high |
| U48 | Web application upload health and front end | `AnalysisITC.Web/wwwroot/app.js`; `AnalysisITC.Web/InteractiveAdminTool.cs`; `AnalysisITC.Web/Program.cs` | 5,156 | gpt-6.1-sol high |
| U49 | Web tests (1/2) | `AnalysisITC.Web.Tests/InterpretationEndpointTests.cs`; `AnalysisITC.Web.Tests/OperatorAndUsageTests.cs`; `AnalysisITC.Web.Tests/OpenAIInterpretationProviderTests.cs` | 5,543 | gpt-6.1-sol high |
| U50 | Web tests (2/2) | `AnalysisITC.Web.Tests/RegistrationSecurityTests.cs`; `AnalysisITC.Web.Tests/ViewerUploadTests.cs`; `AnalysisITC.Web.Tests/ViewerUploadAdmissionTests.cs` | 3,028 | gpt-6.1-sol high |
| U51 | Scientific validation tools and build scripts | `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_report.py`; `tools/TandemMixingReport/PdfReport.cs`; `tools/TandemMixingReport/ReportData.cs` | 4,833 | gpt-6-astra high |

### Desktop comparison coverage

Paths are relative to `AnalysisITC.MacOS/` and `AnalysisITC.Avalonia/`; follow actual referenced classes when a feature spans multiple files.

| Feature | macOS | Avalonia |
|---|---|---|
| Main document, menus and startup | `ViewControllers/MainWindowController.cs; ViewControllers/MainViews/ViewController.cs; ViewControllers/SideBarViewController.cs; AppDelegate.cs` | `MainWindow.axaml.cs; MainWindow.axaml; Misc/Menus/AppMenuController.cs; StartupFileActivationCoordinator.cs` |
| Processing | `ViewControllers/MainViews/DataProcessingViewControllet.cs; GraphViews/DataProcessingGraphView.cs; CustomViews/IntegrationRangeSlider.cs` | `Workspace/Processing/ProcessingWorkspaceControl.cs; Workspace/Processing/ProcessingGraphControl.cs` |
| Fitting | `ViewControllers/MainViews/DataAnalysisViewController.cs; CustomViews/AnalysisInspectorViews.cs; PopOptionViewControllers/BindingAnalysisViewController.cs` | `Workspace/Analysis/AnalysisWorkspaceControl.cs; Workspace/Analysis/AnalysisParameterRowBuilder.cs; Workspace/Analysis/ModelOptionRowBuilder.cs` |
| Results and advanced analyses | `ViewControllers/MainViews/AnalysisResultTabViewController.cs; GraphViews/ResultGraphView.cs` | `Workspace/Results/AnalysisResultWorkspaceControl.cs; Workspace/Results/ResultDependenceGraphControl.cs; Workspace/Results/ResultCorrelationGraphControl.cs; Workspace/Results/ResultParameterGraphControl.cs` |
| Reports and result export | `ViewControllers/AnalysisReportViewController.cs; ViewControllers/AnalysisResultExporterViewController.cs; ViewControllers/ExportSheetViewController.cs` | `Misc/Tools/AnalysisReportWindow.cs; Misc/Tools/AnalysisResultExporterWindow.cs; Misc/Dialogs/ExportDataWindow.cs` |
| Designer, tandem and buffer | `ViewControllers/ExperimentDesignerViewController2.cs; ViewControllers/ExperimentMergerViewController.cs; ViewControllers/BufferSubtractionTool.cs` | `Misc/Tools/ExperimentDesignerWindow.cs; Misc/Tools/TandemMergerWindow.cs; Misc/Tools/BufferSubtractionWindow.cs; Misc/Tools/BufferSubtractionGraphControl.cs` |
| Preferences and project details | `ViewControllers/SettingsViews/; ViewControllers/ProjectInformationWindowController.cs; PopOptionViewControllers/ExperimentDetailsPopoverController.cs` | `Misc/Preferences/; Misc/Details/; Misc/Dialogs/ProjectInformationWindow.cs` |
| Final and supporting figures | `ViewControllers/MainViews/FinalFigureViewController.cs; ViewControllers/SupportingFigureCanvasWindowController.cs; Drawing/; GraphViews/` | `Workspace/FinalFigure/FinalFigureWorkspaceControl.cs; Misc/Tools/SupportingFigureCanvasWindow.cs; Drawing/; Printing/` |
| Platform prompts, settings and progress | `Platform/; ViewControllers/StatusBarViewController.cs` | `Platform/; MainWindow.axaml.cs` |

The repeated-code pass scanned 454 maintained production C# files and manually checked nine duplicate groups. It yielded no additional formal candidates: duplication was retained only when another entry established a concrete behavioral or maintenance cost. The test-versus-code pass distinguished external forward-model validation from ordinary regression and parameter-recovery tests; locally constructed regression data were not treated as an invalid external reference claim.

## Evidence belonging to existing ITC entries

These observations add evidence to existing issues and are not additional CR candidates or part of the 90-candidate verdict count:

- **Adds evidence to ITC-032:** report-only autosave recovery returns before restoring reports when `AddData` receives an empty experiment/result collection (`AnalysisITC.Core/DataReaders/DataReaders.cs:54`, `AnalysisITC.Core/DataReaders/DataReaders.cs:266`). ITC-032 already promises report-only autosave preservation. This is separate from CR-058, which concerns the native normal-open append/replace decision.
- **Adds evidence to ITC-093:** Avalonia rejects an invalid newly typed parameter inside the row callback but does not propagate that draft-invalid state to Run Fit (`AnalysisITC.Avalonia/Workspace/Analysis/AnalysisParameterRowBuilder.cs:83`, `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:722`). The fit can use the previous accepted value while the editor still shows the rejected draft. ITC-093 already includes manual validation of newly typed values.
- **Adds evidence to ITC-098:** drag-leave/drop clears global status during other owned work (`AnalysisITC.Avalonia/MainWindow.axaml.cs:951`, `AnalysisITC.Avalonia/MainWindow.axaml.cs:959`; corresponding native behavior in `AnalysisITC.MacOS/AppWindow.cs:97`). Operation-owned status behavior is already in ITC-098's scope.
- The known live-input mutation, progress-presentation and queued-processing lifetime concerns remain with **ITC-104, ITC-109 and ITC-110**. The review did not promote the already tracked constructed same-ID replacement scenario as a newly demonstrated ordinary workflow.

## Appendix — Rejected candidates

These are the four formal candidates rejected during independent verification:

- **Support report start time may reflect class initialization:** Exact static-initialization timing was not observed; the remaining plausible diagnostic timestamp mismatch has low impact and fails the retention threshold.
- **Avalonia automatic volume differs from its rounded field:** The actual schedule explicitly shows the full effective volume and simulation/bookkeeping agree. The remaining sub-0.1 µL display mismatch and Auto-to-manual intent do not establish a consequential defect.
- **A redundant storyboard outlet crashes native tool loading:** The correct outlet is present too, and no specific AppKit/Xamarin failure mechanism was established. The passing Preferences harness with omitted outlets undermines the generic KVC-crash inference.
- **Nonfinite-candidate tests do not require exact optimum recovery:** The assertions match the declared invalid-trial nonabort/finiteness purpose. Precise convergence is separate recovery coverage; no current solver defect or misleading forward-validation claim was established.

## Recheck synthesis — 2026-10-19

**Historical reconciled totals (19 October):** FIXED 61; OPEN 18; DEFERRED 2; SUPERSEDED 1; REJECTED 2; PARTIAL 0; UNVERIFIED 0. All 84 IDs had a dated verdict; all 18 then-open entries had a strategy or an explicit decision gate. Current counts above include later fixes and the maintainer's 22 October closure of CR-074. Validation this pass is source/test-source inspection only. The initial 19 October recheck had 19 OPEN and 1 REJECTED; the subsequent maintainer closure of CR-022 changes only those two counts.

**Reconciliation and retained scope:**

- CR-022 is REJECTED and closed by the maintainer's 19 October decision: scientific notation in imported concentration comments is user error. Its earlier strategy and questions are historical, with no parser work planned.
- CR-006 is fixed for the agreed stale-input detection scope; derived values can still follow edited temperature. Freezing those values and changing output eligibility are not implied by this verdict. CR-016 is fixed through the agreed restriction, with segment-specific tandem correction still deferred.
- CR-055 is SUPERSEDED by CR-016's restriction: its former supported-correction premise no longer applies. CR-046 is REJECTED as a defect under the explicitly accepted residual-axis policy; the clipping behavior was not changed.
- CR-010's numerical-consumer fix is present. ITC-114's tracker boundary still says that issue is deferred; that wording is stale. The recovery plan already separates the two concerns. Primary-fit recovery remains its own feature.
- CR-005's earlier “Active/All unchanged” note describes that fix; CR-070 supplies the later protected-write/confirmation behavior. CR-035/054 share tandem-action error handling. Keep those shared implementations together without reopening resolved scope.
- Current report-picker fixtures use a real tandem plus a separate corrected ordinary member. The concurrent CR-077 fixture-follow-up note and its validation history were preserved; earlier fixture failures remain historical. A maintained-file hash comparison found only those two report-test files and this review changing during investigation; the changed fixture sources were rechecked before synthesis.

**Recommended work order** (each group remains bounded; questions gate only the dependent work):

1. **Import and document integrity:** CR-015/073 and ITC-115 are resolved under the recorded-versus-never-reached injection policy above. CR-057's entry rule is decided and implemented; see its resolution above. CR-058's report-only open prompt is implemented and verified through the native open queue and dialogs; see its resolution above.
2. **Ordinary interaction:** CR-034 report busy control; CR-056 Designer option. Reuse existing state/confirmation flows. CR-052/053 were resolved together in the native export sheet on 2026-10-19; CR-078 preference wording and CR-080 admin Back were also resolved on 2026-10-19.
3. **Scientific presentation and execution settings:** CR-074 was closed without change by the maintainer on 22 October; no work remains for it. CR-075 status replacement is resolved through the shared manager, with both desktop coordinators verified.
4. **Usage completeness:** CR-081 shares one aggregation across final request output, execution/account detail, admin/CLI and CSV; settle partial-subtotal presentation first. Existing cost/liability and admission protections stay intact.
5. **Validation evidence:** CR-076 literal persistence identities; CR-082 repair or retire the optional historical tight diagnostic. CR-066's single build→test→report command is resolved. External forward tests remain integrated-heat, floating-point-precision checks of mathematics, not solver recovery.

CR-029 stays with [ITC-117's all-model uncertainty plan](ITC-117_DERIVED_PARAMETER_UNCERTAINTY_PLAN.md); CR-071 stays with [deferred ITC-118](ISSUES.md#itc-118---bootstrap-unlocking-of-locked-shared-parameters-needs-a-solver-policy). Neither enters this work order. Scientific implementation must retain original-data best-fit centers, the weighted-objective/unweighted-RMSD distinction, zero error meaning no error, and independent expected results. Shared outputs must trace both desktops and relevant report/export/viewer paths, retaining each desktop renderer. Serialization work follows [FTXTC_FORMAT.md](../Documentation/Schemas/FTXTC/FTXTC_FORMAT.md), preserving .ftxtc compatibility and read-only .ftitc support.

**Most consequential maintainer questions:**

- **CR-015/073:** decided and implemented on 19 October: reject raw imports with recorded starts outside the trace, accept exact endpoints without tolerance, automatically omit only never-reached trailing planned shots, and retain Keep/Discard for the near-end warning. Saved projects remain openable without injection deletion; see ITC-115.
- **CR-057:** decided and implemented on 19 October: finite inclusive 0–14 entry in both Details editors, with blank input rejected. This is an application entry rule; see the resolution and validation above.
- **CR-081:** show known token subtotals explicitly marked partial (recommended), or suppress incomplete aggregates? CR-082 separately needs a retain/repair versus retire decision for its optional historical workflow.

No source, tests, other documentation or tracker entries were edited by this recheck. No strategies were implemented.
