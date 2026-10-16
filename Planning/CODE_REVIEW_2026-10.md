# Full-repository code-quality review — October 2026

Reviewed **51 units covering 828 unique maintained files and 268,216 inventory-time physical lines**, plus cross-cutting desktop, duplication and test-coverage reviews. Unit reviewers raised 83 formal candidates and cross-cutting reviewers raised 7. Independent verification classified those 90 candidates as **85 confirmed, 1 plausible and 4 rejected**. Two pairs sharing a root cause were merged, leaving **84 triage entries: 83 Confirmed and 1 Plausible; 10 High, 61 Medium and 13 Low**.

The five highest-priority findings are:

1. CR-001: changing selection during a fit can make the next fit run on the previous experiment.
2. CR-002: corrected-view spline dragging accumulates baseline displacement with every pointer event.
3. CR-003: copying a locked baseline can leave new settings beside old calculated heats.
4. CR-004: closing a decision dialog can select Save or Replace.
5. CR-005: associated-figure export can silently overwrite existing PDFs.

This is a staging review for maintainer triage, not an approved implementation backlog. Existing ITC issues were deduplicated before promotion; related entries identify adjacent scope rather than treating an already tracked defect as new. The two merges combine oversized report pagination failures in CR-023 and unchanged Details-field round trips in CR-037.

**Verification scope.** Confirmed means an independently reviewed source-and-caller path establishes the stated failure or concrete maintenance cost; it does not imply an interactive reproduction. CR-010 remains Plausible because the complete multi-coordinate profile outcome was not reproduced. The existing `AnalysisITC.MacOS.Tests/PreferencesLayoutTests.swift` harness passed after rerunning with a temporary writable Swift module cache; the initial invocation failed before tests because of cache permissions. The temporary cache was removed. No .NET suites, interactive desktop scenarios, PDF renders, provider/service calls, deployments or packaging were run. No fixes, commits, or changes to `Planning/ISSUES.md` were made; this staging file is the sole persistent review output.

**Review basis.** The working tree was reviewed on 16 October 2026. HEAD advanced through concurrent work from `06602ff3a235a13a7cd2f5559d49adf2bad214b8` at inventory to `9a2127b0af5d9eeb209a1c023c2c7e653e63a91b` at final verification, alongside unrelated uncommitted edits. Those changes were preserved. Counts describe the inventory, while finding locations were checked against the final working tree. This is not an immutable release qualification. Build outputs, vendored packages and generated files were excluded; scripts were inspected without executing packaging, signing, notarization, installer or release operations.

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

## CR-002 — Corrected-view spline dragging accumulates displacement on every pointer event

- Priority: High
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:894`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:1226`; `AnalysisITC.Core/DataProcessing.cs:1736`
- Related: ITC-004, ITC-085
- Problem: Each corrected-view drag update adds cursor height to the already modified baseline, which is immediately rebuilt and reused by the next event.
- Failure scenario: With time dragging disabled, drag a sampled-time spline point above zero and continue horizontally at +0.1 µW. A 5 µW point becomes 5.1, then 5.2, then 5.3 µW on successive events.
- Consequence: The final baseline depends on event count and pointer path rather than the intended displacement. Release retains the altered point and recalculates heats; returning the cursor to zero does not undo the accumulated movement.
- Suggested direction: Calculate each update from the captured original power and a stable cursor displacement for the entire drag.

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
- Suite validation: Follow-up testing of a frozen working-tree snapshot passed the full Avalonia suite twice with 511 passed, one opt-in visual QA skip, and no failures or runner errors, including all 20 CR-004 cases. Control runs excluding the new cases passed with 491 passed and one skip both with and without the four CR-004 production edits. The earlier interrupted run did not reproduce. Its missing-metadata test-case ID maps to `MainWindowSelectionDuringFitClearsStaleReadyContext`; the precise trigger of the original selection exception remains unconfirmed. VSTest socket access and Avalonia telemetry logging were sandbox limitations; verification used the executable xUnit runner and command-local telemetry opt-out. An isolated HEAD-plus-CR-004 build is blocked by the pre-existing Avalonia project reference to `Resources/ScienceHelpResource.txt`, which is absent from HEAD; that unrelated help consolidation is not included in this commit.
- Other value-type calls (audit only, unchanged): `ConfirmationDialogWindow.ConfirmProcessingClearAsync`, `ConfirmDeleteReportAsync`, `ConfirmAsync` and `ConfirmModal`; `AnalysisReportWindow.ConfirmReplacementAsync` and `ConfirmWarningsAsync`; `AttributeOperationsWindow.ShowAsync`; `ExportDataWindow.ConfigureAsync`; and `TraceabilityStartupWindow.ShowAsync` use `ShowDialog<bool>` and default to false. `AvaloniaFtxtcDuplicatePromptService.ChooseAction` uses `ShowDialog<FtxtcDuplicateAction>` and defaults to `SkipDuplicates`, already covered by its existing close/keyboard tests. These ten calls have cancellation-compatible defaults; the other value-type calls already request nullable results. Changes remain limited to the two CR-004 prompts.

## CR-005 — Associated-figure export silently overwrites existing PDFs on Avalonia

- Priority: High
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1233`; `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1338`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:100`
- Related: ITC-061
- Problem: Associated-result export chooses a folder and writes planned names directly with File.Create, checking collisions only within the new batch.
- Failure scenario: Export an associated experiment named sample into a folder already containing sample.pdf, such as a deliberately retained previous figure.
- Consequence: The existing artifact is replaced without an overwrite decision. Native associated export asks, and Avalonia Active/All export uses non-overwriting output.
- Suggested direction: Check the complete plan against existing files and obtain a replacement decision or use the existing non-overwriting writer.

## CR-006 — Saved thermodynamic values follow edited experiment temperature

- Priority: High
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/Models/Models.cs:532`; `AnalysisITC.Core/Analysis2/Models/ThermodynamicSolution.cs:35`; `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:2641`
- Related: ITC-069, ITC-103
- Problem: Ordinary saved solutions derive their temperature from the mutable experiment rather than the temperature captured for the fit.
- Failure scenario: Fit at 25 °C, retain the result, then change the experiment to 35 °C in Details. Unchanged affinity and enthalpy now produce different ΔG, −TΔS and ΔS in direct solution/report consumers.
- Consequence: Saved scientific results can change without refitting and disagree with the already constructed temperature-dependence curves. Existing presentation caches may retain the old values, making the inconsistency dependent on the consumer.
- Suggested direction: Use captured fit temperature consistently for saved member thermodynamics, while preserving explicitly selected evaluation temperatures for combined results.

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

## CR-010 — Failed profile diagnostics can still supply numerical uncertainty

- Priority: High
- Confidence: Plausible
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/Models/Models.cs:749`; `AnalysisITC.Core/Presentation/LinkedThermodynamicEvaluation.cs:34`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs:645`; `AnalysisITC.Core/Analysis2/ProfileLikelihoodEstimator.cs:321`
- Related: ITC-090, ITC-094, ITC-099
- Problem: Numerical consumers accept completed profile coordinates without checking whether the owning run ended in CompleteFailure, although the solver refuses to install uncertainty from that outcome.
- Failure scenario: A multi-parameter profile completes an affinity interval before a different coordinate finds a better primary minimum. Staging retains the completed interval while marking the run failed; the getters can still turn that diagnostic interval into Kd or linked thermodynamic uncertainty.
- Consequence: Outputs can present numerical uncertainty from a run declared unavailable. The consumer bypass and producer retention are verified statically, but this combined outcome was not reproduced with a real multi-coordinate dataset.
- Suggested direction: Use one shared eligibility rule for numerical profile reporting. Preserve failed-run diagnostics, and verify the mixed-coordinate sequence before promoting this candidate to confirmed.

## CR-011 — Two-site fit summaries omit requested entropy contributions

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Analysis2/Models/TwoSetsOfSites.cs:482`; `AnalysisITC.Core/Presentation/AnalysisParameterSummaryPresentation.cs:107`; `AnalysisITC.Core/Presentation/PublicationFigure.cs:1194`
- Related: ITC-030
- Problem: The two-site solution formatter never handles the Entropy display flag for either site, although the saved quantities exist.
- Failure scenario: Fit a two-site model and inspect its Analysis summary or enable Derived parameters in a figure annotation.
- Consequence: Both −TΔS values are omitted on both desktops. Report parameter tables retain them; no numerical calculation or persisted value is wrong.
- Suggested direction: Handle both existing site-specific quantities in the shared solution formatter and verify the actual two-site display path.

## CR-012 — Switching to Sequential can leave global constraints uneditable

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Analysis2/AnalysisWorkspace.cs:66`; `AnalysisITC.Core/Analysis2/AnalysisBuilder.cs:216`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:583`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:603`
- Related: ITC-078, ITC-082, ITC-086
- Problem: Model changes retain old global constraints, but a rejected new context removes the controls needed to repair them.
- Failure scenario: Fit two experiments with One Site and Affinity1 shared, then select Sequential. Affinity1 remains shared while Affinity2 has no constraint; the intentional family-consistency check rejects this combination.
- Consequence: Fitting becomes unavailable and the constraint controls disappear on both desktops. Native launch-time normalization occurs after the failing rebuild, so the user must restore defaults or switch back to remove the constraint.
- Suggested direction: Reconcile incompatible constraints during model transitions, or preserve a way to repair them when context validation fails.

## CR-013 — Min-volatility baseline fallback creates a NaN spline point

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataClasses/DataPoint.cs:48`; `AnalysisITC.Core/DataProcessing.cs:1604`; `AnalysisITC.Core/DataProcessing.cs:1648`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingWorkspaceControl.cs:474`
- Related: ITC-058, ITC-101
- Problem: Volatility-weighted averaging divides zero by zero for one sample, but automatic spline construction explicitly permits a singleton fallback.
- Failure scenario: In Avalonia, use an unlocked spline, exclude integrated points, select Min volatility, and extend an integration endpoint to its full 180-second delay on 1-second data. The candidate window [174,175] is excluded and falls back to sample 174 alone.
- Consequence: A NaN spline point enters baseline construction and prevents valid baseline subtraction/integration. Sequential editing and finite input data are sufficient; the default Mean mode avoids the problem.
- Suggested direction: Define singleton averaging as that sample's power and check the complete automatic-window fallback against an independently finite thermogram.

## CR-014 — Integration uncertainty uses different samples from the heat integral

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/DataClasses/InjectionData.cs:305`; `AnalysisITC.Core/DataClasses/InjectionData.cs:480`; `AnalysisITC.Core/Math/Statistics.cs:12`; `AnalysisITC.Core.Tests/BaselineAutoCorrelationTests.cs:80`
- Related: None
- Problem: Heat integration uses right-endpoint powers and actual overlap durations, while the integration-noise calculation counts strictly interior samples with equal full-step weights.
- Failure scenario: A permitted two-interval region contributes two power samples to heat but contains only one strict-interior timestamp, causing zero SD despite nonzero baseline noise. Four aligned intervals use four heat samples but three in the AR(1) variance.
- Consequence: Processing uncertainties are wrong; zero values disable desktop weighted fitting, while nonzero underestimated values remain eligible and affect relative weights. The existing autocorrelation test codifies the sample-count mismatch.
- Suggested direction: Propagate integration noise using the actual contributing samples and overlap weights, with independent aligned and fractional-boundary covariance expectations. Keep separate baseline/cross-covariance policy decisions outside this finding.

## CR-015 — Removing leading orphan injections breaks positional ID assumptions

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataReaders/DataValidator.cs:379`; `AnalysisITC.Core/Analysis2/Models/Models.cs:343`; `AnalysisITC.Core/Analysis2/Models/OneSetOfSites.cs:44`; `AnalysisITC.Core/DataClasses/InjectionData.cs:432`
- Related: ITC-107
- Problem: Import cleanup retains injection IDs after removing leading entries, while fitting and error estimation use those IDs as list positions.
- Failure scenario: Import a raw file whose first injection marker precedes the thermogram. Cleanup removes ID 0, leaving IDs 1…N−1 at positions 0…N−2; no downstream reader renumbers them.
- Consequence: Binding predictions address the next injection and eventually index beyond the final entry; error estimation also chooses the wrong preceding injection. Processing/fitting may handle the resulting exception; a published wrong fit or application crash was not demonstrated.
- Suggested direction: Establish one consistent indexing contract after cleanup and validate the real reader-to-processing-to-fit path, rather than only asserting retained IDs.

## CR-016 — Tandem reconstruction changes the buffer correction used during scanning

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/TandemMixingScan.cs:759`; `AnalysisITC.Core/TandemConcatenationTool.cs:222`; `AnalysisITC.Core/DataClasses/BufferSubtraction.cs:330`
- Related: ITC-060, ITC-062, ITC-067
- Problem: The scan uses each source's corrected heats, but the concatenated experiment retains only the first source's correction attributes and applies them using cumulative injection IDs.
- Failure scenario: Merge two 20-injection segments using a blank whose injection 2 heat is 2 µJ and final heat is 5 µJ. A second-segment raw heat of 10 µJ is scanned as 8 µJ, but its merged number 22 clamps to blank injection 20 and becomes 5 µJ.
- Consequence: The produced corrected observations differ from those used to select automatic back-mixing. Later segments' different blank references or correction methods are also lost; a changed optimum was not measured.
- Suggested direction: Preserve segment-specific correction sources, methods and local injection mappings, or explicitly constrain supported combinations. Check agreement between scanned and produced corrected observations.

## CR-017 — The viewer strips meaning from typed experimental conditions

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Core/Viewer/ViewerDocumentReader.cs:1247`; `AnalysisITC.Web/wwwroot/app.js:1307`; `AnalysisITC.Core/Analysis2/ExperimentAttribute.cs:342`
- Related: ITC-027, ITC-042, ITC-089
- Problem: Viewer metadata selects one raw attribute field instead of formatting the typed condition, losing identities, units, pH and species locations.
- Failure scenario: Open a project containing 20 mM HEPES at pH 7.4 and 150 mM NaCl in the viewer. It displays Buffer: 0.02 and Salt: 0.15.
- Consequence: Readers cannot reliably identify or interpret the conditions. Native serialization retains the evidence; this is presentation loss rather than corruption of the project file.
- Suggested direction: Provide typed viewer presentation with document-local reference context, avoiding desktop formatters that implicitly resolve references through global DataManager state.

## CR-018 — Quick clipboard exports misalign scientific table columns

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataExport/Exporter.cs:666`; `AnalysisITC.Core/DataExport/Exporter.cs:725`; `AnalysisITC.Core/DataExport/Exporter.cs:776`; `AnalysisITC.MacOS/PopOptionViewControllers/BindingAnalysisViewController.cs:283`
- Related: ITC-015, ITC-017, ITC-094
- Problem: The quick-copy path declares value/SD pairs but splits display strings on ± and does not quote CSV fields.
- Failure scenario: Copy a binding result with fixed N, missing uncertainty or uncertainty display set to None. One field appears under a two-field header; confidence intervals or comma-containing names can add fields instead.
- Consequence: Subsequent values and assessments appear under incorrect spreadsheet headings. Both desktops' result-details actions reach this path, although native Results also offers a newer safe exporter.
- Suggested direction: Build explicit fixed-width scientific cells and quote every CSV field, preferably reusing the structured result-table exporter.

## CR-019 — Mixed result exports use one label for stoichiometry and syringe activity

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:694`; `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:744`; `AnalysisITC.Core/Analysis2/Models/OneSetOfSites.cs:61`
- Related: ITC-030, ITC-051, ITC-082
- Problem: A shared Nvalue1 column takes its N or α label from the first result, although later rows can use the coordinate for the other scientific quantity.
- Failure scenario: Select ordinary One-Site and syringe-corrected One-Site results together, then export Summary or All Rows.
- Consequence: Some values are labeled as stoichiometry when they represent syringe active fraction, or vice versa, with no per-row option field distinguishing them. Homogeneous exports remain correctly labeled.
- Suggested direction: Preserve per-result parameter meaning through distinct columns or explicit semantic metadata.

## CR-020 — Unavailable salt-analysis results are displayed as scientific zeros

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ElectrostaticsAnalysis.cs:70`; `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs:1334`; `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs:1095`
- Related: ITC-036, ITC-051
- Problem: Salt analysis can mark itself Calculated with unavailable components, while both desktop output views test only that overall flag. Formatting loses the FloatWithError missing-value marker.
- Failure scenario: Analyze a one-site result with two valid affinities at 50 and 150 mM NaCl. The eligibility checks pass, but the component calculations require more data and return unavailable values.
- Consequence: Desktop outputs show zero Kd0 and zero counter-ion result rather than unavailable. Reports already guard component availability; partially available results have the same desktop risk.
- Suggested direction: Use shared per-component availability in both desktop views and align run eligibility with the calculations' data requirements.

## CR-021 — Missing buffer thermochemistry is treated as an exact zero enthalpy

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs:38`; `AnalysisITC.Core/DataClasses/Buffers.cs:538`; `AnalysisITC.Core/DataClasses/AnalysisResult.cs:523`; `AnalysisITC.Core/Analysis2/AdvancedAnalysis/ProtonationAnalysis.cs:22`
- Related: ITC-003, ITC-027
- Problem: Buffer definitions lacking thermochemical data receive a zero enthalpy curve, and protonation analysis accepts them based on buffer identity alone.
- Failure scenario: Use MES and the selectable Succinate buffer for two experiments at 25 °C. MES supplies 14,800 J/mol, while Succinate supplies an unsupported zero; differing measured binding enthalpies then produce a finite protonation regression.
- Consequence: Protonation and intrinsic binding enthalpy can be reported from invented thermochemical evidence. The current proton-sign convention is not challenged by this finding.
- Suggested direction: Represent unavailable thermochemistry explicitly and require usable coordinates, keeping genuine zero enthalpies distinct from missing data.

## CR-022 — Imported comment concentrations misread scientific notation

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/UnitClasses/ConcentrationUnit.cs:180`; `AnalysisITC.Core/DataReaders/DataReaders.cs:504`; `AnalysisITC.Core/DataReaders/DataReaders.cs:548`
- Related: ITC-027, ITC-089
- Problem: Comment extraction accepts a decimal prefix and an unknown unit token rather than matching the complete concentration.
- Failure scenario: Import a raw file containing NaCl 1e-1 M in its comment. Extraction reads 1 with unit e, falls back to mM, and stores 0.001 M instead of 0.1 M. The before-name pattern can match the exponent tail instead.
- Consequence: Salt or buffer metadata can be wrong by orders of magnitude or negative, affecting ionic strength, advanced analysis and reported conditions. The separate interactive exponent parser does not protect this path.
- Suggested direction: Parse complete numeric/unit tokens including exponents and boundaries, rejecting partial matches and unknown unit suffixes.

## CR-023 — Oversized report blocks lose content instead of continuing on another page

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:471`; `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:479`; `AnalysisITC.Core/Presentation/AnalysisReportLayout.cs:625`; `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1401`
- Related: ITC-021, ITC-022, ITC-040
- Problem: Notices are treated as indivisible fragments whose height is clamped to the page. A single oversized key-value row is also forced onto the page without allocating its full height.
- Failure scenario: A 24-experiment series retaining three warning categories per member produces at least 72 warning lines, exceeding the default 738.85-point printable height. Separately, a Traceability Mode report with 70 results puts all identifiers into one oversized value.
- Consequence: Both renderers clip the warning inventory's tail or draw identifiers through the footer/beyond the page, without continuation. Individual chapters or saved identifiers retain the underlying evidence; no PDF was generated during verification.
- Suggested direction: Provide continuation for content taller than a page, or represent long identifier lists as independently pageable rows. Never treat an oversized row as fitted while allocating only its heading.

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

## CR-025 — Disabling injection tables removes schemas still used by processing evidence

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs:469`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationModelInputWriter.cs:515`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs:347`
- Related: ITC-034
- Problem: Injection-table selection removes baseline schemas as well, even when detailed processing remains enabled and its positional tables still reference those schemas.
- Failure scenario: An Advanced or Administrator user retains detailed processing but disables injection tables for a spline or segmented experiment.
- Consequence: The compact interpretation request contains baseline arrays without their declared column definitions. Canonical evidence remains intact; an incorrect generated interpretation was not demonstrated.
- Suggested direction: Retain every schema referenced by remaining processing tables and test the independent injection/processing option combination.

## CR-026 — Public enrollment notifies native UI subscribers from a worker thread

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Interpretation/FtItcInterpretationClient.cs:132`; `AnalysisITC.Core/Properties/AppSettings.cs:248`; `AnalysisITC.MacOS/ViewControllers/SideBarViewController.cs:291`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:261`
- Related: None
- Problem: After asynchronous public enrollment with ConfigureAwait(false), settings persistence synchronously invokes native subscribers that access AppKit without dispatch.
- Failure scenario: Open Generate Interpretation with no saved access code while the native main window is open. The enrollment response resumes on a worker and triggers sidebar and initialized Analysis controls.
- Consequence: The source establishes off-main-thread AppKit access. The report window's own dispatch does not protect the other subscribers; an exact native crash was not reproduced.
- Suggested direction: Dispatch UI-affecting settings notifications through the established main-thread service, or separate credential persistence from those notifications.

## CR-027 — Native interpretation hides permitted evidence choices inside a stale parent

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1502`; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1697`; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs:1852`
- Related: ITC-034, ITC-066
- Problem: The optional-evidence parent is hidden according to initial thermogram eligibility and is not recomputed with current capabilities.
- Failure scenario: Open the native dialog with verified Standard access. Injection tables are permitted and their checkbox is individually unhidden, but its parent remains hidden.
- Consequence: The user cannot change an inclusion choice still consumed by generation. Capability refresh also retains stale thermogram eligibility; access guards prevent claiming unauthorized generation.
- Suggested direction: Update every inclusion control, selected state, parent and heading from the same capability response, including consistent clearing after failure.

## CR-028 — Saving unrelated settings resets the live fitting configuration

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Properties/AppSettings.cs:248`; `AnalysisITC.Core/Properties/AppSettings.cs:554`; `AnalysisITC.Core/DataExport/ProjectWriter.cs:156`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:287`
- Related: None
- Problem: AppSettings.Save also reapplies fitting preference defaults to the live analysis controller.
- Failure scenario: In native macOS, fit with nondefault uncertainty settings, use Save As, then leave and reopen Analysis. Saving LastDocumentPath invokes Save; view reentry reloads the reset options.
- Consequence: The next fit uses preference defaults instead of the preceding analysis configuration. Completed fits are unchanged; an immediate fit without leaving the view can retain the visible choices.
- Suggested direction: Separate persistence of unrelated settings from deliberate application of fitting defaults.

## CR-029 — Competitive apparent Kd omits fitted stoichiometry uncertainty

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:564`; `AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:586`; `AnalysisITC.Core/Analysis2/Models/ThermodynamicSolution.cs:50`
- Related: None
- Problem: The finite-depletion calculation reads N.Value, making free competitor a scalar and discarding N uncertainty before calculating apparent Kd.
- Failure scenario: Use ordinary stoichiometry mode with nonzero competitor, exact competitor inputs, locked intrinsic affinity and bootstrap-varying N. At 10 µM cell and competitor concentrations and competitor Ka = 10^6 M⁻¹, N values 0.9 and 1.1 produce different competition factors, yet reported apparent Kd has zero SD.
- Consequence: A derived affinity that varies across the retained fitted samples is presented without estimated uncertainty. Zero competitor and syringe-correction mode avoid this particular dependence.
- Suggested direction: Include the N dependence and relevant joint uncertainty in apparent-Kd propagation, using retained samples or an explicit propagation rule while preserving the original best-fit center.

## CR-030 — Clearing a fit leaves its cached Null assessment visible

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:349`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:230`; `AnalysisITC.Core/DataClasses/ExperimentData.cs:811`
- Related: ITC-029, ITC-030, ITC-084
- Problem: Both desktops refresh the fitted-parameter summary after solution removal without clearing their separate Null-comparison cache.
- Failure scenario: Select an experiment with a saved Null comparison and invoke Clear Solution while keeping it selected.
- Consequence: Old Null RMSD, ΔAICc, conclusion and optional prediction can remain beside the cleared fit. Selection away/back or refitting repairs the display; saved scientific state is not corrupted.
- Suggested direction: Synchronize Null evidence on every owned solution change using the same UI-dispatch and selection protections as the fit summary.

## CR-031 — Avalonia Final Figure PDFs have the wrong physical dimensions

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/PublicationFigure.cs:64`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:104`; `AnalysisITC.Avalonia/Drawing/SkiaFigureRenderer.cs:579`
- Related: ITC-012
- Problem: The PDF writer treats preview layout units based on 113.5 units per inch as 72-point PDF units, without a conversion.
- Failure scenario: Directly export a Final Figure whose plot width is set to 6 cm. The width becomes approximately 268.11 PDF points, corresponding to 9.4583 cm.
- Consequence: Exported physical dimensions are enlarged by 113.5/72. Printer fit-to-page behavior is separate and may change eventual printed size; no PDF was generated during this review.
- Suggested direction: Convert both page dimensions and drawing coordinates consistently from the preview scale to PDF points.

## CR-032 — Supporting Figure size controls are capped by captured Final Figure dimensions

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/Drawing/SkiaFigureCanvasRenderer.cs:80`; `AnalysisITC.Avalonia/Misc/Tools/SupportingFigureCanvasWindow.cs:499`; `AnalysisITC.MacOS/Drawing/CoreGraphicsFigureCanvasRenderer.cs:122`
- Related: None
- Problem: Avalonia takes the smaller of the Supporting Figure dimensions and its captured Final Figure dimensions, contrary to the documented size-control precedence.
- Failure scenario: Open Supporting Figure with a 6 × 10 cm appearance snapshot and request a valid 10 × 12 cm plot.
- Consequence: Preview and export use 6 × 10 cm; increasing the controls cannot produce the requested size. The actual-size readout shows the capped value, so it is not itself misleading.
- Suggested direction: Use the validated explicit canvas dimensions, as the native renderer does.

## CR-033 — Corrected-view spline handles use raw baseline coordinates

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:654`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:710`; `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs:1033`
- Related: ITC-004
- Problem: Spline anchors move to zero in corrected view, but slope handles are drawn and hit-tested around raw point power.
- Failure scenario: Enable Smooth handles and Corrected data on a trace with a baseline offset larger than the corrected plot range and a modest local slope.
- Consequence: Handles become detached from their point or disappear outside the clipped plot, where they cannot be dragged. Raw view is a workaround; this is distinct from drag accumulation.
- Suggested direction: Use the same raw/corrected coordinate basis for anchor drawing, handle drawing and handle hit-testing.

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

## CR-035 — Avalonia immediately overwrites tandem-scan failure feedback

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Tools/TandemMergerWindow.cs:351`; `AnalysisITC.Avalonia/Misc/Tools/TandemMergerWindow.cs:279`; `AnalysisITC.Core/TandemMixingScan.cs:572`
- Related: None
- Problem: The catch displays the error, but finally refreshes controls and synchronously replaces it with ordinary selection status.
- Failure scenario: Select two ordinary raw experiments with unintegrated included injections and create using Auto back-mixing. UI eligibility permits them, but the scanner rejects missing integration.
- Consequence: No merged experiment appears and the explanation is replaced by the selection count. Source experiments are not modified by this demonstrated failure.
- Suggested direction: Preserve failure feedback while restoring controls, or use the existing handled-error presentation.

## CR-036 — Changing the attribute list discards pending Details edits

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs:204`; `AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs:256`; `AnalysisITC.Avalonia/Misc/Details/ExperimentAttributeEditorControl.cs:420`
- Related: None
- Problem: Avalonia reconstructs every attribute editor from underlying copies before committing drafts held in controls.
- Failure scenario: Change buffer concentration from 20 to 50 mM, then add an attribute, remove another row or change another row's key. Reconstruction restores the buffer to 20 mM.
- Consequence: Ordinary multi-attribute editing silently loses pending input. Some selectors commit immediately, so the finding does not apply to every control.
- Suggested direction: Preserve draft values independently of editor reconstruction, or retain unaffected controls.

## CR-037 — Applying Details alters scientific values and dates that were not edited

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:384`; `AnalysisITC.MacOS/CustomViews/ExperimentAttributeView.cs:755`; `AnalysisITC.Avalonia/Misc/Details/ExperimentAttributeEditorControl.cs:429`; `AnalysisITC.Avalonia/Misc/Details/ExperimentDetailsWindow.cs:380`
- Related: ITC-055, ITC-080
- Problem: Both desktops reparse rounded attribute displays on Apply without preserving untouched values. Avalonia also reparses editable short dates.
- Failure scenario: Apply a comment-only native edit to buffer pH 7.46: its F1 display becomes stored 7.5. G6 competitor concentrations similarly lose digits on both platforms. An untouched Avalonia filesystem date with seconds becomes minute-rounded and UserModified.
- Consequence: Unrelated edits change scientific conditions, can invalidate fits, and can falsify timestamp provenance. Main concentration/temperature fields already preserve unchanged originals; these attribute/date paths do not.
- Suggested direction: Preserve original values and provenance unless their corresponding controls were actually edited.

## CR-038 — Avalonia overrides automatic selection of a newly created result

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/MainWindow.axaml.cs:1068`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:1129`; `AnalysisITC.Core/DataManager.cs:577`
- Related: None
- Problem: The sidebar refresh restores its previous local selection and writes it back over an explicit Core result selection.
- Failure scenario: Finish a fit with Create analysis result and Auto-open new result enabled. Core selects the new result, but the queued Avalonia refresh restores the preceding experiment.
- Consequence: The requested Results workspace does not open automatically; the result remains available for manual selection. Native macOS follows the Core selection.
- Suggested direction: Honor explicit Core selection changes and retain the old row only when no new selection was requested.

## CR-039 — Avalonia silently drops case-distinct Linux import paths

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/MainWindow.axaml.cs:846`; `AnalysisITC.Avalonia/MainWindow.axaml.cs:983`; `AnalysisITC.Avalonia/StartupFileActivationCoordinator.cs:81`
- Related: ITC-028, ITC-077
- Problem: Path deduplication uses case-insensitive comparison on every filesystem.
- Failure scenario: On case-sensitive Linux, select distinct run.itc and Run.itc together in the supported multi-file picker or drop them together.
- Consequence: Only the first reaches the reader, so one selected experiment is silently omitted. Separate imports can succeed because the pending batch is cleared.
- Suggested direction: Use filesystem-appropriate path identity consistently across picker, drop and startup activation paths.

## CR-040 — An Avalonia fitting test exits before its worker and admission lease finish

- Priority: Medium
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Avalonia.Tests/AnalysisWorkspaceControlTests.cs:1104`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:833`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:898`
- Related: ITC-007, ITC-098, ITC-101
- Problem: The test requests Stop, closes its window and clears shared state without joining the real asynchronous fit.
- Failure scenario: The worker remains scheduled after teardown, while the next serialized test starts and requests calculation admission.
- Consequence: The next test can observe busy admission or receive late callbacks from the previous fixture. Serialization does not join asynchronous work; no flaky run was reproduced.
- Suggested direction: Await the owned fit's completion in teardown, including assertion-failure paths, before clearing data and restoring global settings.

## CR-041 — Preferences tests depend on the live interpretation service

- Priority: Medium
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Avalonia.Tests/PreferencesTests.cs:466`; `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:925`; `AnalysisITC.Avalonia/Misc/Preferences/PreferencesWindow.cs:953`; `AnalysisITC.Core/Interpretation/FtItcInterpretationClient.cs:124`
- Related: ITC-007, ITC-065
- Problem: Production Preferences constructors create real HTTP clients; substituting the settings store leaves this transport active in tests.
- Failure scenario: A test opens Preferences with synthetic cached credentials and expects Verified (cached). A timely live response changes that state; blank-code windows can instead enroll a public installation.
- Consequence: Tests depend on service response timing and can update shared settings after their initiating fixture ends. Some account-refresh cancellation guards exist, but public refresh lacks equivalent lifetime control. No request was made during review.
- Suggested direction: Inject controlled transport/client behavior and cancel or join every refresh during cleanup.

## CR-042 — Native Copy Processing buttons have no connected actions

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Main.storyboard:5602`; `AnalysisITC.MacOS/Main.storyboard:5610`; `AnalysisITC.MacOS/Main.storyboard:5618`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1207`
- Related: None
- Problem: The visible All, Active and New buttons have no action, binding, outlet or programmatic handler. Existing connections belong to detached older controls.
- Failure scenario: Select a processed thermogram in native macOS and click any button under Copy processing to.
- Consequence: No processing is copied and no explanatory feedback appears. Existing tests construct outlets directly and do not exercise these storyboard buttons.
- Suggested direction: Connect the visible controls to their intended copy scopes and admission policy, and verify their actual storyboard dispatch.

## CR-043 — Native automatic injection volume is capped at 20 µL only in its primary field

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:400`; `AnalysisITC.MacOS/ViewControllers/ExperimentDesignerViewController2.cs:409`; `AnalysisITC.MacOS/Main.storyboard:1297`
- Related: ITC-078, ITC-082
- Problem: The automatic volume field is clamped by the manual stepper maximum, while simulation uses the unclamped automatic volume.
- Failure scenario: Select VP-ITC, ten injections, a small first injection and automatic volume. The 310 µL syringe gives 34.3 µL regular injections, but the disabled primary field displays 20 µL.
- Consequence: The primary volume field conflicts substantially with the simulated schedule; turning Auto off also changes the volume to 20 µL. The schedule summary correctly shows 34.3 µL, so the effective value is not completely hidden.
- Suggested direction: Display the effective automatic volume without the manual ceiling and align permitted manual ranges with supported instruments and protocols.

## CR-044 — Native graph construction fails when every injection is excluded

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/CGGraph.cs:2188`; `AnalysisITC.MacOS/GraphViews/AnalysisGraphView.cs:172`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs:127`
- Related: ITC-106, ITC-108
- Problem: Included-only enthalpy scaling calls Min/Max on each experiment before guarding empty observation sets, during construction before display options are applied.
- Failure scenario: Exclude every injection while idle, then leave and return to Analyze or reselect the experiment.
- Consequence: An empty-sequence exception interrupts graph construction. Final Figure and selected-fit graphs share the constructor; application termination was not demonstrated.
- Suggested direction: Handle empty eligible observations before aggregation, preserving excluded-point scaling policy and any available prediction range.

## CR-045 — Unified native power axes include experiments with no thermogram

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/CGGraph.cs:939`; `AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs:279`; `AnalysisITC.MacOS/PopOptionViewControllers/FinalFigureOptions/FinalFigureOptionsController.cs:374`
- Related: ITC-012, ITC-036
- Problem: Unified power scaling aggregates every active experiment without filtering empty raw/corrected trace collections.
- Failure scenario: Load an integrated-heats file alongside a processed raw experiment, select the raw experiment and enable unified power axes in Final Figure.
- Consequence: The heat-only member causes an empty-sequence exception. The selected-experiment thermogram guard does not protect other members; the option is session-local and default-off.
- Suggested direction: Build the common power domain only from usable thermogram samples and define an explicit empty-domain fallback.

## CR-046 — Native residual axes can exclude their own uncertainty bars

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/CGGraph.cs:2631`; `AnalysisITC.MacOS/Drawing/CGGraph.cs:2699`; `AnalysisITC.Core/Presentation/PublicationFigure.cs:1069`
- Related: ITC-096
- Problem: Autoscaling selects the smaller absolute uncertainty endpoint, although drawing uses both endpoints.
- Failure scenario: The largest residual is 100 J/mol with SD 100 J/mol and other points do not enlarge the domain. Scaling pads the chosen 100 to ±150 while a bar endpoint reaches 200.
- Consequence: The plot domain omits part of displayed uncertainty. Padding can conceal some cases but cannot ensure containment; no rendered artifact was inspected.
- Suggested direction: Include the largest absolute lower/upper endpoint when calculating residual bounds under the existing visibility policy.

## CR-047 — Two-buffer protonation graphs generate nonfinite native uncertainty bands

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:302`; `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:346`; `AnalysisITC.Core/DataClasses/AnalysisResult.cs:523`
- Related: ITC-094
- Problem: Native line-band preparation divides by n−2, although protonation analysis permits two distinct buffer observations.
- Failure scenario: Run protonation analysis on two one-site experiments with distinct buffer ionization enthalpies and display its graph. Positive observation SD produces infinite bounds; zero SD produces NaN.
- Consequence: Nonfinite uncertainty coordinates enter the native path, while central fitted values remain intact. The exact CoreGraphics response or a crash was not established.
- Suggested direction: Use shared finite envelope preparation with an explicit two-observation policy, retaining the native renderer and distinguishing unavailable uncertainty from invalid arithmetic.

## CR-048 — Constant fitted values produce zero-span native dependence axes

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/Drawing/GraphAxis.cs:147`; `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:42`; `AnalysisITC.MacOS/Drawing/ParameterDependenceGraph.cs:120`
- Related: ITC-096
- Problem: The static axis factory leaves equal bounds unchanged, and subsequent scaling divides by the zero span.
- Failure scenario: Create a valid salt-series result whose affinity is fixed or shared at the same value and open affinity versus salt.
- Consequence: Tick calculations and mapped point coordinates become nonfinite for a valid constant dependence. Uncertainty does not enlarge this domain because extrema use central values.
- Suggested direction: Give the axis factory a finite, scale-appropriate degenerate-range policy consistent with the guarded axis setter.

## CR-049 — Native energy editors change scale while reusing the same visible text

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs:518`; `AnalysisITC.MacOS/CustomViews/OptionAdjustmentView.cs:738`; `AnalysisITC.MacOS/CustomViews/ParameterValueAdjustmentView.cs:50`
- Related: ITC-055, ITC-078, ITC-082
- Problem: Native editors resolve parsing units from the already mutated stored value, while their label and text retain the original unit.
- Failure scenario: A manual competitor enthalpy displays −40 kJ/mol. Enter −0.05, producing −50 J/mol, then edit its SD. The next parse resolves J/mol and turns the unchanged central text into −0.05 J/mol under the kJ/mol label.
- Consequence: An ordinary edit changes a scientific input by a factor of 1000. Designer energy controls share the defect, and automatic simulation can parse an option twice during one edit.
- Suggested direction: Keep the displayed unit stable as editor state. Any unit transition should update label and text together while preserving the internal value.

## CR-050 — Native Results replaces saved competitor intervals with symmetric bounds

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs:2173`; `AnalysisITC.MacOS/CustomViews/AnalysisInspectorViews.cs:456`; `AnalysisITC.Core/Math/NumberStructs.cs:60`
- Related: ITC-042, ITC-044, ITC-055, ITC-094
- Problem: The native Model page reconstructs model-option values from value and SD, discarding their saved interval endpoints and asymmetry.
- Failure scenario: Fit competitive data using competitor properties from a one-site result with asymmetric uncertainty, then view Results → Model using CI or Automatic uncertainty display.
- Consequence: macOS shows different bounds or a different uncertainty style from the saved value and Avalonia. The stored scientific evidence remains intact.
- Suggested direction: Format the complete transformed FloatWithError using shared presentation instead of reconstructing it from two scalars.

## CR-051 — Native integration sliders use another experiment's baseline-exclusion policy

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1242`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:968`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1019`
- Related: ITC-085
- Problem: Slider processing decisions use a cached DiscardIntegratedPoints flag that updates on data changes but not on selection changes.
- Failure scenario: Process A with integration-region exclusion enabled, change the default to disabled, and import B. Reselect A and shorten an integration window with a slider; the stale false flag skips A's required baseline refit.
- Consequence: Heats can use a baseline fitted to the previous integration regions. Graph-marker dragging reads the actual selected processor policy, so equivalent edits can produce different outcomes.
- Suggested direction: Read the selected processor's policy at commit time or rebind the cache whenever selection changes.

## CR-052 — Changing export units resets native correction choices

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:110`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:215`; `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:238`
- Related: None
- Problem: Native export refreshes reload correction checkboxes from settings before pending checkbox edits have been saved.
- Failure scenario: For a fitted experiment, choose Integrated Peaks, uncheck Offset-corrected peaks, then change the molar energy unit. Refresh restores the previously enabled correction.
- Consequence: Export can subtract an offset after the user deliberately chose uncorrected peaks. The reset is visible and can be repaired before confirmation.
- Suggested direction: Retain draft correction choices across unrelated refreshes, or save them when the controls change.

## CR-053 — Native Combined Data export offers correction options it cannot honor

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/ExportSheetViewController.cs:206`; `AnalysisITC.Core/DataExport/Exporter.cs:440`; `AnalysisITC.Avalonia/Misc/Dialogs/ExportDataWindow.cs:162`
- Related: None
- Problem: The native sheet exposes correction checkboxes for Combined Data even though that format intentionally includes both traces and uncorrected peaks.
- Failure scenario: Export Combined Data with Offset-corrected peaks checked for an experiment with nonzero fitted offset. The peak and fit columns retain the offset.
- Consequence: Output contradicts the selected option; the baseline checkbox likewise cannot choose the included trace columns. Existing tests explicitly require the current export contents.
- Suggested direction: Expose only format-supported controls and explain Combined Data's fixed contents, preserving its intentional numerical export policy.

## CR-054 — Native tandem creation lets validation errors escape its UI action

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.MacOS/ViewControllers/ExperimentMergerViewController.cs:262`; `AnalysisITC.MacOS/ViewControllers/ExperimentMergerViewController.cs:289`; `AnalysisITC.Core/TandemMixingScan.cs:572`
- Related: ITC-098
- Problem: The async-void creation action has finally cleanup but no exception handler around its awaited scan.
- Failure scenario: Choose two admitted raw sources with unintegrated included injections and request automatic back-mixing. Core validation throws during the scan.
- Consequence: The exception bypasses the application's handled-error route after controls and admission are restored. Actual process termination was not established.
- Suggested direction: Present validation/creation failures through the handled-error path while retaining operation ownership through that presentation.

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

## CR-059 — Public quota checks race with durable request admission

- Priority: Medium
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/Program.cs:692`; `AnalysisITC.Web/PublicQuotaService.cs:16`; `AnalysisITC.Web/InterpretationQuotaService.cs:45`; `AnalysisITC.Web/InterpretationUsageStore.cs:32`
- Related: None
- Problem: Public quota and active-execution checks run before admission, while the admission transaction skips the relevant checks for Public's null monetary-limit path.
- Failure scenario: With four of five installation requests used, two requests with distinct correlation IDs both pass the public check before either admission commits. Both are then admitted and dispatch provider work.
- Consequence: Public request and concurrency limits can be exceeded; global quota decisions also lack atomic revalidation. This differs from intentionally allowing one admitted request to finish above a recorded cost limit.
- Suggested direction: Include public installation/global quota evaluation and installation ownership in durable admission, preserving duplicate claims and the policy against predicted-cost reservation.

## CR-060 — Public accounting omits durable attempts without a final request summary

- Priority: Medium
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/InterpretationUsageStore.cs:174`; `AnalysisITC.Web/InterpretationUsageStore.Reporting.cs:45`; `AnalysisITC.Web/Program.cs:866`; `AnalysisITC.Web/InterpretationUsageStore.Administration.cs:56`
- Related: None
- Problem: Public usage requires an access-tier field sourced only from the optional final summary; admission does not persist that classification with execution identity.
- Failure scenario: A public attempt records a provider receipt, then the process crashes or summary finalization fails. Other installations' global checks omit it immediately; stopped-execution recovery later removes the originating installation's active guard without restoring classification.
- Consequence: Public counts, recorded spend and unresolved liabilities undercount durable provider activity. The authoritative receipt and operator/account-history queries retain the charge.
- Suggested direction: Persist quota classification with durable admission identity and calculate public usage independently of final-summary availability.

## CR-061 — Concurrent public-registry writes can undo a successful revocation

- Priority: Medium
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/PublicAccessRegistry.cs:49`; `AnalysisITC.Web/PublicAccessRegistry.cs:61`; `AnalysisITC.Web/InterpretationAdminCommands.cs:31`; `AnalysisITC.Web/Program.cs:657`
- Related: ITC-057
- Problem: Whole-file registry mutations use only a process-local semaphore, while the supported administration command runs separately.
- Failure scenario: The server reads the registry while touching identity A; the CLI revokes B and writes; the server then replaces the file with its earlier snapshot.
- Consequence: A successfully revoked credential can become accepted again, even when the concurrent request belongs to another installation. The required interleaving is narrow and its deployment frequency is unknown.
- Suggested direction: Serialize mutations across processes or use transactional storage, covering the real service/CLI writer arrangement.

## CR-062 — Month rollover stops unresolved public costs from blocking new work

- Priority: Medium
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/InterpretationUsageStore.cs:174`; `AnalysisITC.Web/PublicQuotaService.cs:13`; `AnalysisITC.Web/InterpretationQuotaService.cs:48`
- Related: None
- Problem: Public usage applies the current-month cutoff to unresolved-cost counts as well as monthly spending, without an independent unresolved hold.
- Failure scenario: A public request finalizes late on 31 October with unknown provider cost. On 1 November its unknown count disappears from the public snapshot and a fresh request can pass if its rolling allowance permits.
- Consequence: New work is allowed without settlement or waiver of the previous liability, contrary to the documented cross-month rule. This occurs even when the public summary was successfully stored.
- Suggested direction: Limit the date filter to periodic spending totals and query unresolved liabilities over their full lifetime.

## CR-063 — Registry health reports success without reading the registry

- Priority: Medium
- Confidence: Confirmed
- Category: Web
- Location: `AnalysisITC.Web/HealthChecks.cs:50`; `AnalysisITC.Web/GenerationPresetRegistry.cs:43`; `AnalysisITC.Web/DailyStatusEmail.cs:82`
- Related: None
- Problem: The interpretation registry check unconditionally reports readable configuration inside a try block that performs no registry read.
- Failure scenario: An existing generation-preset registry becomes malformed or inaccessible while interpretation/provider configuration is otherwise healthy. Options and generation requests fail when reading it.
- Consequence: Health output and the daily email can report the Interpretation group healthy and omit the outage cause. Other storage probes inspect different files; no actual deployment failure was observed.
- Suggested direction: Validate registries through the request-handling read policy and report safe structured failures, retaining intentional defaults for an absent preset file.

## CR-064 — Registration tests read and may write the service-default registry

- Priority: Medium
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Web.Tests/RegistrationSecurityTests.cs:674`; `AnalysisITC.Web/InterpretationOptions.cs:40`; `AnalysisITC.Web/Program.cs:225`; `AnalysisITC.Web/RegistrationMailSender.cs:28`
- Related: None
- Problem: The fixture redirects several files but omits Registration.OperatorRegistryPath and retains the real delivery worker.
- Failure scenario: Enabled startup reads the ambient registered-account registry. A malformed/unreadable file can fail the test; if the activation fixture lasts until a worker tick and the default directory is writable, credential provisioning writes there before missing mail configuration is detected.
- Consequence: Tests depend on external machine state and can conditionally leave persistent synthetic credentials outside fixture cleanup. No ambient registry was inspected or modified during review.
- Suggested direction: Redirect the distinct registration registry into the fixture and explicitly control the worker and transport in endpoint tests.

## CR-065 — Deployment activation failures bypass the existing rollback branch

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `scripts/publish-web-app.sh:42`; `scripts/publish-web-app.sh:50`; `scripts/publish-web-app.sh:106`; `Documentation/Interpretation/MIST_DEPLOYMENT.md:31`
- Related: None
- Problem: Stop, rename and start commands run under set -e outside recovery handling; only failures reached through the later health check enter rollback.
- Failure scenario: The old release is moved aside and the next rename fails, or systemctl start returns failure. Bash exits before the health-check recovery branch.
- Consequence: The service remains stopped with no active release directory or a failed replacement, requiring manual recovery. No deployment was executed.
- Suggested direction: Handle activation-stage failures explicitly and recover only to an executable compatible with the complete current accounting ledger; keep generation paused until compatibility and health are verified, without restoring an old database.

## CR-066 — Precision reports attach current source hashes to potentially older executions

- Priority: Medium
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Tests/ScientificValidation/ForwardPrecision/report_results.py:27`; `AnalysisITC.Tests/ScientificValidation/ForwardPrecision/report_results.py:66`; `AnalysisITC.Core.Tests/PytcExternalReferenceTests.cs:357`
- Related: None
- Problem: Reporting hashes the current model sources without binding them to the execution that produced the supplied prediction arrays and test results.
- Failure scenario: Edit a model, then run only the documented report command using an earlier passing prediction directory and TRX.
- Consequence: The generated evidence associates 52 passing recorded predictions with hashes of code that did not produce them. Numerical/reference checks remain valid; no existing archive was shown to be wrong.
- Suggested direction: Capture source/build identity when predictions execute and preserve or verify it during reporting, distinguishing report-time checkout identity when useful.

## CR-067 — Avalonia labels the syringe activity input as stoichiometry

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisParameterRowBuilder.cs:385`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisParameterRowBuilder.cs:467`; `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs:681`
- Related: ITC-030
- Problem: Editable rows receive no model-option context, so Nvalue1 always appears as N-value/N even when its meaning changes to syringe activity α.
- Failure scenario: Enable Use Syringe Correction for One Site, then edit or lock the first parameter. The separate Stoichiometry option controls cell-side sites, but the row still describes stoichiometry.
- Consequence: The user can configure the wrong scientific quantity. Native editing and Core output metadata already recognize the alternative meaning; this differs from mixed-result export labeling.
- Suggested direction: Supply option-sensitive editable parameter metadata through shared Core presentation.

## CR-068 — Native salt plots replace fitted centers with fresh random sample means

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.MacOS/GraphViews/ResultGraphView.cs:205`; `AnalysisITC.MacOS/GraphViews/ResultGraphView.cs:227`; `AnalysisITC.Core/Math/NumberStructs.cs:122`
- Related: None
- Problem: Counter-ion and Debye–Hückel graph setup resamples each salt group and adopts the sample mean, including groups with one fitted observation.
- Failure scenario: Open a salt-series result with nonzero affinity uncertainty and one experiment per salt condition, then switch graph modes away and back.
- Consequence: Plotted centers and intervals change without refitting and can differ from the estimates used by the stored advanced fit. Saved results remain intact, but visual comparisons and screenshots are unstable.
- Suggested direction: Preserve singleton values and intervals directly and define a deterministic grouping policy for repeated conditions.

## CR-069 — Avalonia Competitive Designer exposes unidentified internal units and coordinates

- Priority: Medium
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:351`; `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:365`; `AnalysisITC.Avalonia/Misc/Tools/ExperimentDesignerWindow.cs:451`; `AnalysisITC.Core/Analysis2/Models/CompetitiveBinding.cs:97`
- Related: ITC-078, ITC-079, ITC-082
- Problem: Competitor option fields show and accept raw molarity, J/mol and log10 Ka without identifying those units or coordinates.
- Failure scenario: Enter ligand affinity 1 expecting the ordinary editor's Kd = 1 µM convention. Designer stores log10 Ka = 1, equivalent to Kd = 0.1 M; entering −40 expecting kJ/mol similarly becomes −40 J/mol.
- Consequence: Plausible entries simulate a substantially different experiment, undermining the planning workflow. Labels and tooltips provide no compensating explanation.
- Suggested direction: Reuse scientific conversions and explicit unit/coordinate labels for Designer model options.

## CR-070 — Several export actions let filesystem failures escape the UI boundary

- Priority: Medium
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Avalonia/Misc/Tools/AnalysisResultExporterWindow.cs:71`; `AnalysisITC.Avalonia/Misc/Tools/SupportingFigureCanvasWindow.cs:244`; `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs:1233`; `AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs:520`
- Related: None
- Problem: Avalonia table, supporting-figure and selected-figure actions, plus associated-figure loops on both desktops, lack handled-error boundaries around output writes.
- Failure scenario: An accepted export destination becomes disconnected, full or unwritable. WriteAllTextAsync or File.Create throws through the UI action.
- Consequence: An ordinary output failure becomes an unhandled UI exception, and batches can stop after partial completion. Other export paths handle equivalent errors; actual process termination was not established.
- Suggested direction: Handle failures at the complete export-action boundary and make any partial batch completion clear.

## CR-071 — Bootstrap unlocking is bypassed when every shared coordinate is locked

- Priority: Medium
- Confidence: Confirmed
- Category: Scientific integrity
- Location: `AnalysisITC.Core/Analysis2/ParameterSet.cs:282`; `AnalysisITC.Core/Analysis2/Solver2.cs:1410`; `AnalysisITC.Core/Analysis2/Models/Models.cs:472`; `AnalysisITC.Core.Tests/GlobalUncertaintyCloneTests.cs:18`
- Related: ITC-052, ITC-083, ITC-090, ITC-094
- Problem: When primary fitting requires only individual solves, bootstrap uses member clones that retain IsGloballyDetermined. Unlock clears IsLocked but still does not admit shared coordinates to fitting.
- Failure scenario: Fit eligible multiple experiments with shared locked Offset or N, remaining coordinates local, residual bootstrap and Unlock parameters enabled.
- Consequence: Shared coordinates remain fixed in all replicas despite the recorded setting. Primary estimates are unchanged; uncertainty magnitude or bias was not measured. Existing tests exercise the correct global helper directly, bypassing real dispatch.
- Suggested direction: Choose uncertainty dispatch from the unlocked bootstrap topology while preserving shared ownership, primary fitting and assessment policies; test the actual Analyze route.

## CR-072 — Independent child solvers discard explicit iteration and tolerance settings

- Priority: Low
- Confidence: Confirmed
- Category: Design and maintainability
- Location: `AnalysisITC.Core/Analysis2/Solver2.cs:1465`; `AnalysisITC.Core/Analysis2/Solver2.cs:1347`; `AnalysisITC.Core.Tests/ProfileLikelihoodWorkflowTests.cs:176`
- Related: None
- Problem: Independent global fitting copies several parent settings into child solvers but omits MaxOptimizerIterations and SolverToleranceModifier.
- Failure scenario: An API caller requests a 90-iteration independent fit while application defaults allow 20,000. Children retain 20,000, and their derived profile cap becomes 6,666 instead of 30.
- Consequence: Per-operation runtime/convergence limits differ from shared and single fitting. Ordinary desktop parents and children usually share defaults, so this is an API and bounded-test contract defect rather than an established desktop symptom.
- Suggested direction: Pass effective parent solver settings into independent children and assert the effective profile limit in the existing workflow test.

## CR-073 — Attempt Fix repeats the incomplete-injection warning without fixing it

- Priority: Low
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/DataReaders/DataValidator.cs:147`; `AnalysisITC.Core/DataReaders/DataValidator.cs:296`; `AnalysisITC.Core/DataReaders/DataValidator.cs:88`
- Related: ITC-107
- Problem: The incomplete-injection warning offers a removal action whose implementation only removes nonpositive-time injections.
- Failure scenario: An acquisition stops after its first injection at 150 seconds, with the trace ending at 155 seconds. All retained injections meet the near-end warning, but Attempt Fix preserves the positive-time marker and repeats the warning.
- Consequence: The advertised repair makes no progress. The user can escape by choosing Keep or Discard; this is not a crash or an unresponsive infinite loop.
- Suggested direction: Match the repair to the warning predicate, or remove that repair option if deleting the affected injections is inappropriate.

## CR-074 — An exactly zero heat-capacity estimate disappears from summaries

- Priority: Low
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:359`; `AnalysisITC.Core/Presentation/AnalysisResultParameterEvaluation.cs:381`; `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:380`
- Related: None
- Problem: Summary row presence depends on a nonzero slope rather than whether ΔCp is a modeled quantity with uncertainty.
- Failure scenario: A temperature-dependent enthalpy result has an exactly zero ΔCp center and positive uncertainty. The evaluator returns it, but the summary builder removes its row; numerical export can retain it.
- Consequence: Results and reports omit the estimate and interval. Exact-zero uncertain fits were not demonstrated in ordinary data, so practical frequency is limited; constant SameForAll enthalpy is intentionally a different case.
- Suggested direction: Determine row presence from the model constraint and availability, allowing a valid zero estimate without adding ΔCp to constant-enthalpy models.

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

## CR-077 — Same-name experiments lose fixed-parameter entries from reports

- Priority: Low
- Confidence: Confirmed
- Category: Correctness
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1516`; `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:1539`
- Related: ITC-048, ITC-051
- Problem: The A11 fixed-parameter block deduplicates local parameters by display label, using experiment names instead of identities.
- Failure scenario: Two distinct members share a name and each has N fixed, possibly at different values. The second parameter-plus-name label matches the first and is discarded.
- Consequence: The fixed-parameter inventory is incomplete and can hide a different fixed value. Member tables and stored data retain the evidence.
- Suggested direction: Identify local rows by experiment identity and parameter, and distinguish equal names through report references; deduplicate only genuinely shared parameters.

## CR-078 — A native preference describes project reprocessing instead of import recalculation

- Priority: Low
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/Preferences.storyboard:1104`; `AnalysisITC.MacOS/ViewControllers/SettingsViews/MacPreferencesPaneControllers.cs:554`; `AnalysisITC.Core/DataReaders/IntegratedHeatReader.cs:306`
- Related: ITC-036
- Problem: Reprocess integrated heats when loading a project actually supplies the default concentration/ratio-recalculation choice for integrated-table import.
- Failure scenario: Change the checkbox expecting reopened projects to be reprocessed. It instead changes the initial option for subsequent .dat/.aff imports.
- Consequence: The label describes the wrong operation and scientific inputs. The correctly labeled per-import dialog limits the impact; no saved project heats are rewritten by this preference.
- Suggested direction: Describe the import concentration/ratio recalculation default explicitly, matching the dialog and manual.

## CR-079 — Copy to Next Peak is enabled before its hidden zoom requirement is met

- Priority: Low
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:487`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs:1141`; `AnalysisITC.MacOS/GraphViews/DataProcessingGraphView.cs:634`
- Related: None
- Problem: The native button enables for a selected nonfinal injection, but its handler additionally requires that injection to be zoomed.
- Failure scenario: In All Peaks view, single-click a nonfinal injection and press Copy to Next Peak. The enabled action returns without copying or advancing.
- Consequence: The documented selection-based workflow silently does nothing. Focusing the injection first is a workaround; Avalonia supports ordinary selection.
- Suggested direction: Align the enabled state and action prerequisites, preferably supporting the documented selected-injection workflow.

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

## CR-083 — Historical reporting tests assert retired document contents and filenames

- Priority: Low
- Confidence: Confirmed
- Category: Test quality
- Location: `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/test_generate_report.py:56`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/test_generate_report.py:145`; `AnalysisITC.Tests/ScientificValidation/PytcBookkeeping/generate_report.py:579`
- Related: None
- Problem: Tests expect old practical-limit tables in README and newly generated content in REPORT, although those files now redirect to precision results and generation writes DEFAULT_TOLERANCE_REPORT.
- Failure scenario: Run the historical documented unittest command. The README assertion fails; the lifecycle test reads the unchanged copied redirect instead of the newly generated report.
- Consequence: Intended current artifact layout causes false regression failures in historical reporting maintenance. This was established from source, not a suite execution.
- Suggested direction: Target retained historical documents and the actual generated filename while keeping the current precision redirects intact.

## CR-084 — Correcting a tandem-import option requires restarting the import

- Priority: Low
- Confidence: Confirmed
- Category: Platform drift and misplaced logic
- Location: `AnalysisITC.MacOS/Platform/MacTandemImportPromptService.cs:64`; `AnalysisITC.Core/DataReaders/MicroCalITC200Reader.cs:163`; `AnalysisITC.Core/DataReaders/DataReaders.cs:336`
- Related: ITC-026, ITC-079
- Problem: Native tandem import dismisses its options dialog before validating compensation inputs.
- Failure scenario: Import a valid concatenated MicroCal file, enter dead volume 0 or mixing percentage 200, then choose back-mixing compensation.
- Consequence: A handled per-file error skips the current import; correcting the typo requires reopening the file and reentering choices. Remaining files continue, with no crash or persisted-data loss established.
- Suggested direction: Keep the dialog and entered choices available until validation succeeds, consistent with Avalonia.

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
