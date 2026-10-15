# Issue tracker

Resolved, completed, and closed entries retain Status, Problem, and Resolution, including useful code references, limitations, and linked follow-ups; completed plans and execution logs are omitted.

## ITC-001 — Asymmetric rounding of negative display values

- Status: Resolved (2026-10-02).
- Problem: `FWEMath.RoundApproximate` rounded −2.5 to −2 but +2.5 to +3; its digits overload also rounded −2.6 to −2. The asymmetry affected Standard/Strict displayed estimates and interval endpoints.
- Resolution: Both overloads use sign-symmetric midpoint tolerance based on the original value and apply the requested midpoint mode to the scaled value. Independent Core cases cover mirrored signs, tolerance margins, negative digits, non-finite inputs, and formatted output.

## ITC-002 — Expanded bounds on logarithmic affinity coordinates

- Status: Resolved (2026-10-02).
- Problem: `Parameter.RefreshLimits` multiplied log₁₀K bounds directly: Extended produced [−40, 400] and No limit [−4000, 40000], allowing affinity overflow/underflow and infinite derived ΔG despite finite model predictions.
- Resolution: Widen physical K bounds by factors of 20 or 2000 using additive log₁₀ factors; negative log₁₀K remains allowed. Reject global candidates when affinity or linked enthalpy coordinates yield non-positive or non-finite K/Kd, while allowing finite affinities outside local presets. Focused tests cover physical endpoints, overflow/underflow, and locked coordinates.

## ITC-003 — pKa correction can fail to converge

- Status: Resolved (2026-10-03).
- Problem: Buffer pKa fixed-point correction could cycle; phosphate at pH 8.198, 25 °C, and 5 M reproduced non-convergence.
- Resolution: `Buffers.cs` solves the existing ionic-strength balance by bounded bisection, returning NaN for invalid inputs or an unverifiable finite solution after 128 iterations. The nearest-pKa/two-species approximation and monovalent-counterion assumption remain; numerical convergence does not establish high-concentration chemical accuracy. Solution-wide correction is deferred under ITC-027.

## ITC-004 — Unlocking a spline point discards a converted spline

- Status: Resolved.
- Problem: Unlocking a spline point discarded the converted spline instead of preserving its current points.
- Resolution: Avalonia `ProcessingGraphControl` and macOS `DataProcessingGraphView` clear the selected position/slope locks and process with replace: false. Unlock is available when either lock is set; Smooth splines recalculate the released slope from the retained points.

## ITC-005 — Standardization of inspector headers

- Status: Completed.
- Problem: Inspector headings and tabs mixed sentence case and title case across macOS and Avalonia.
- Resolution: Standardized affected headings and related tabs to sentence case, preserving proper names and acronyms.

## ITC-006 — Remove the standalone Edit identifiers tool

- Status: Resolved (2026-10-02).
- Problem: The standalone Edit identifiers command duplicated identifier editing in the experiment Details editor.
- Resolution: Removed the macOS Tools and Avalonia Selection/context-menu commands and handlers. Editing remains in Details > Identifiers; import review retains single-experiment dialogs (`ExperimentIdentifiersWindow`/`MacIdentifierEditor`) and batch dialogs. Manual and in-app help point to Details.

## ITC-007 — Avalonia UI tests fail when certain test classes run together

- Priority: Medium
- Status: Resolved (2026-10-10).
- Location: `AnalysisITC.Avalonia.Tests`, the `[Collection("Avalonia UI")]` classes, `AvaloniaTestApplication.cs`, and `AvaloniaDispatcherTests.cs`.
- Problem: The headless platform is set up once with `SetupWithoutStarting`, which binds Avalonia's UI thread to whichever xUnit worker thread runs first. xUnit does not guarantee that later tests in the collection run on that thread. When the thread changes, every following test that creates a window fails with "The calling thread cannot access this object because a different thread owns it", in a cascade unrelated to the code under test.
- Reproduction (2026-10-01): `PreferencesTests` alone passes (24/24), but run together with `ExperimentDetailsWindowTests`, `AnalysisReportRenderingTests`, or `ExperimentIdentifiersWindowTests`, about 18 tests fail, starting with the test after `SavedAccessSurvivesOpeningApplyingAndReopeningPreferences`. `PreferencesTests` on its own also failed this way once in three runs.
- Review verification (2026-10-06): The full Release Avalonia suite again produced the same cross-thread exception cascade and stopped making progress; the review run was interrupted. Separately filtered runs exposed deterministic stale assertions, tracked in ITC-065, so those failures should not be attributed to this threading issue.
- Reproduction (2026-10-08): `PreferencesTests` alone now fails in most runs. Three alternating runs with and without the ITC-080 date changes gave the same pattern (without: 24/24, 18 failed, 19 failed; with: 19, 18, 19 failed), every failure the cross-thread exception, so ITC-080 is not the cause. Some runs hang instead: the blame collector stopped one at `PublicationFontRoundTripsAndShowsResolvedFamily`, and a stalled test host had its main thread blocked in a monitor wait, consistent with `Dispatcher.UIThread.Invoke` waiting for a dispatcher loop that `SetupWithoutStarting` never runs.
- Investigation (2026-10-10): Current Release tests reproduce the ownership failure both with the four originally reported classes (51 passed, 19 failed) and with `PreferencesTests` alone (5 passed, 19 failed). All failures have the same cross-thread exception; both runs first fail at the `advanced` case of `SavedAccessSurvivesOpeningApplyingAndReopeningPreferences`, after its other two cases pass. A control run of the other three classes plus the previously failing `RegistrationButtonShowsUntilAccessIsVerified` passes all 47 tests. These runs complete without hanging. The collection currently spans 46 classes and 248 fact/theory declarations, including five async test methods, so correcting only Preferences would leave the same infrastructure defect elsewhere.
- Consequence: Full or filtered Avalonia runs can report failures that are not regressions; a real failure can be hidden among them.
- Resolution: Migrated only the Avalonia test project to xUnit v3 and [`Avalonia.Headless.XUnit` 12.0.5](https://www.nuget.org/packages/Avalonia.Headless.XUnit/12.0.5), converted all 248 existing UI fact/theory declarations to Avalonia attributes, and removed the worker-thread bootstrap. The supported runner dispatches construction, execution, async continuations, and cleanup on its owned UI thread. Explicit per-assembly isolation preserves the existing shared application, and the UI collection remains serial for shared Core settings and document state. Added three async theory cases covering constructor/cleanup access, resuming after an await, and serving a synchronous UI dispatch from a worker while the test awaits it. The Preferences tab test now establishes and restores its session state instead of assuming that no earlier test selected a tab. No application or scientific code changed.
- Validation: All 73 focused Release tests pass, followed by three additional successful 73-test reproduction runs. The full Release suite completes in six seconds with 360 passed and six failed, with no UI-thread ownership exceptions or hangs. All six remaining failures are in `AnalysisReportEnhancementTests`, reaching `ProcessingClearPlan.Entry` through a retained MainWindow menu subscription and throwing "Max must be greater than or equal to min" for its sparse fixture's default injection delays. That class passes all 14 tests in isolation; this separate suite-state/processing-clear interaction is not resolved by ITC-007 and is tracked as ITC-101. `git diff --check` passes for the changed test files.

## ITC-008 — Null Offset labels can expose unmatched experiment IDs

- Priority: Low (reassessed 2026-10-02; original High-priority unit defect resolved in the current working tree).
- Status: Partially resolved; ID-label fallback remains open.
- Location: Shared `NullModelComparisonPresentation.FormatNullParameters` (`AnalysisITC.Core/Presentation/NullModelComparisonPresentation.cs`), called by `AnalysisResultTableExporter`, `Exporter` and `AnalysisReportBuilder`. The per-formatter `AddAssessment` and `FormatNullOffsets` sites named originally were replaced by this shared formatter during the [Null-model foundation corrections](DILUTION_HEAT_MODELS_PLAN.md#completed-foundation).
- Recheck (2026-10-12): The fallback is still present in the shared formatter: an unmatched member without a report label uses the result's solution name or, failing that, `member.ExperimentId`, independent of Traceability Mode.
- Reassessment: The Offset is heat per mole of injectant, stored in J/mol. Both paths now resolve the selected energy family/override and call `Energy.ToString(unit, "G6", withunit: true, permole: true)`, which correctly converts the value and includes the molar unit. The original raw-value "µJ" export and unitless report defects are no longer present. Normal exports use experiment names; no-binding reports use report labels such as `1A`. `ToFormattedString` is not necessary to correct the units; the current `ToString` overload also performs the conversion.
- Remaining problem: Both formatters fall back to `member.ExperimentId` when the saved comparison member cannot be matched to an experiment name or report label. That fallback is not gated by Traceability Mode. A constructed unmatched-member case reproduces the ID exposure with Traceability Mode disabled; this is a defensive label-handling edge case, not a reproduced error in normal matched results.
- Validation (2026-10-02): 123 targeted Core checks passed, including seven temporary reassessment probes. For −10536 J/mol, both outputs produced −10.536 kJ/mol (automatic Joules) and −2.51816 kcal/mol (automatic Calories); explicit J, kJ, cal, and kcal overrides also passed independent expected-value checks. Normal name/report labels and the unmatched-ID fallback were verified. The temporary probes were removed after reassessment; production code was not changed.
- Follow-up: Use a readable fallback label when a saved comparison member cannot be matched; show internal IDs only under Traceability Mode. Add lasting regression coverage for null-offset unit conversion and matched/unmatched member labels when implementing that follow-up.

## ITC-009 — Null-only figure ignores the experiment x-axis and error bars

- Status: Resolved (2026-10-03).
- Problem: The saved-null figure used saved ratios directly, mislabeled concentration axes, and omitted error bars instead of respecting the experiment’s display settings.
- Resolution: `PublicationFigureBuilder` renders the applicable saved fitted Offset through the ordinary builder on current experiment data, including axes, uncertainties, offset correction, and annotations. Independent members use their own comparison; pooled results match the member. Missing/ambiguous/non-finite fits show observations with “Offset fit unavailable” and no prediction, residuals, or parameters. Stored models/comparison evidence are unchanged; processing edits change observations but not the saved Offset until Update Result. Only effective No binding detected selects this presentation; Inconclusive keeps the binding fit. `ClassifiedNullFigureTests` covers ordinary-Offset parity and unavailable fits.

## ITC-010 — Manual and older interpretations always warn "Assessment context unknown"

- Status: Resolved.
- Problem: Manual and older interpretations lack `AssessmentContextFingerprint`, so reports always warned “Assessment context unknown”, even without assessed results.
- Resolution: Removed assessment-context unknown/changed warnings from `AnalysisReportBuilder` and both report windows (2026-10-02). Interpretation freshness and result validity already flag changed data.

## ITC-011 — Binding-output suppression is undocumented and missing on macOS

- Status: Resolved.
- Problem: Binding-output suppression lacked documentation and equivalent native macOS presentation.
- Resolution: The native report window exposes Standard/Diagnostic output and native result figures use ResultOutputPolicy. The manual and shared `HelpTextResource.txt` explain suppression and access to attempted binding detail. Later policy refinements are recorded in ITC-014 and ITC-051.

## ITC-012 — Explicit model selection in Final Figure

- Status: Resolved.
- Problem: The same data could produce a binding figure or suppressed output after No binding detected depending on whether the entry point was an experiment or its Analysis Result.
- Resolution: Both desktop apps have a session-only **Plotted model** choice, initially Binding model. Final Figure preview, PDF export, printing, and selected/Active/All/associated exports use this choice, with assessment context beside the selector. Core `PublicationFigureSource` / `PublicationFigureBuilder` resolve the exact result member or attached solution's saved comparison. Null figures show the full saved prediction against buffer-corrected observations; missing, ambiguous, or mismatched fits show observations and an unavailable notice. Offset correction and confidence settings are retained but disabled for Null. On macOS every Final Figure model and entry point, including Analysis Result figures, is drawn by the macOS renderer. Supporting Figure and reports retain their assessment policies. The manual, shared help, and report map explain the boundary; no saved-preference or project-format changes.

## ITC-013 — Full and diagnostic reports do not state the binding assessment

- Status: Resolved; superseded.
- Problem: Full reports did not consistently state the effective binding assessment; Diagnostic attempted-fit output needed assessment context.
- Resolution: The 2026-10-02 policy added front-page assessments and result-overview assessments where suppression affected output, with full comparison evidence in Diagnostic reports. This resolution is superseded by the later assessment, member-summary, and provenance rules in ITC-014, ITC-073, and ITC-051.

## ITC-014 — Assessment labels now drive which parameters are hidden

- Status: Resolved (2026-10-05).
- Problem: Assessment cutoffs and their output restrictions needed a consistent policy, especially for Inconclusive results and manual overrides.
- Resolution: Rule `aicc-0-10-v1` assigns No binding detected at signed ΔAICc ≤ 0, Inconclusive at 0 < ΔAICc < 10, and Binding detected at ΔAICc ≥ 10. Saved outcomes/rule IDs restore unchanged. Inconclusive retains binding output/uncertainty and adds a health warning without changing input validity; manual overrides are authoritative. Combined binding output requires every member to be eligible, without subset recombination. These chosen cutoffs have no calibrated false-positive guarantee and do not exclude varying dilution heat, buffer mismatch, or drift. Later Standard report suppression is recorded in ITC-051; Standard table exports omit no-binding values.
- Follow-up (2026-10-10): Rule `aicc-0-15-v1` raises the Binding detected cutoff to ΔAICc ≥ 15; the Inconclusive interval is now 0 < ΔAICc < 15, with No binding detected still at ΔAICc ≤ 0. New fits and refits use this rule. Saved assessments under earlier rules and manual overrides retain their recorded outcomes.
- Follow-up (2026-10-12): Load-time reevaluation supersedes the historical-outcome preservation above. The reader recalculates the automatic assessment from saved comparison evidence using the current rule, regardless of the saved rule ID, and retains manual overrides, fitted values, and stored uncertainty. It does not refit on load or introduce historical-rule evaluators or new persistence fields.

## ITC-015 — Minor null-model output formatting

- Priority: Low
- Status: Partially resolved (2026-10-09); report-purpose persistence remains deferred.
- Location: `AnalysisReportBuilder.BuildSummary`, `AnalysisResultTableExporter.Build`, and both desktop report windows.
- Problem: The diagnostic "Binding assessment and null comparison" block appended heat units to unavailable Null RMSD ("Unavailable µJ"). Result-table numbers, including AICc, ΔAICc, Null RMSD, parameters and uncertainties, depended on the calling numeric culture, while temperatures were invariant.
- Resolution: Diagnostic Null RMSD is unitless when missing or non-finite; numeric values retain their selected heat units and precision. `AnalysisResultTableExporter.Build` uses invariant numeric culture for the entire table and restores the caller's culture in `finally`, covering CSV/TSV files and Copy on both desktop apps. Headers, quoting, units, precision, uncertainty styles and assessment suppression are unchanged. The report map A01, result-export manual and shared help are updated.
- Reassessment: The original header-prefix and missing-conditions complaints are outdated in the current code. The first header begins with "Analysis Result", with a separate Output purpose column; no-binding experiment chapters retain their conditions and applicable identifiers.
- Validation: All 31 new Core cases pass, covering missing/non-finite/zero/fractional RMSD in Joules and Calories, whole-table equality under en-US/da-DK/fr-FR across both formats, row modes, uncertainty layouts and output purposes, pooled diagnostics, literal numeric cells, quoted names, and culture restoration after success, empty input and exceptions. All 225 focused Core checks, 33 isolated Avalonia report checks and native `AnalysisReportReferencesTests` pass. Full Core suite: 2,457 passed, one skipped, one failure in `CompetitorSourceSaveSelectedTests.ResultSaveIncludesCompetitorSourceAndItsMembers`, also reproduced in isolation: its `Single` assertion finds multiple saved experiments with competitor references. That save/reopen test does not exercise the changed formatting paths and was left unchanged. `git diff --check` passes.
- Deferred decision: Standard/Diagnostic report purpose is not persisted or restored with report presentation settings, so reopened windows default to Standard. Decide whether to remember it separately; this change does not alter that behavior or the project format.

## ITC-016 — Independent multi-experiment fits share one binding assessment

- Status: Resolved.
- Problem: Independent multi-experiment fits shared one pooled ΔAICc/verdict, letting strong binders mask non-binding members or suppressing genuine binders. Pooled residual variance could also disagree with local comparisons.
- Resolution: When `GlobalModel.ShouldFitIndividually` is true, save assessments/comparisons by member solution ID and use each member’s local evidence for its output. Combined binding output requires all members to be eligible; Not assessed remains unrestricted. Pooled evidence is diagnostic only. Shared fitted parameters retain a result-level comparison; merely locking equal values does not make independent fits pooled. Collection-summary wording was refined in ITC-073; pooled Null sharing remains ITC-018.

## ITC-018 — Global null model for pooled shared-parameter fits

- Priority: High
- Status: Deferred.
- Problem: Pooled binding fits with shared parameters are currently compared with locally fitted Offset models. A future global null model should represent the same parameter-sharing or constraint structure as the pooled binding model, with corresponding criteria and persistence. Until that design is completed, the saved comparison uses local Offset fits and the established pooled-variance convention.

## ITC-019 — Configurable assessment output set

- Priority: Low
- Status: Deferred.
- Problem: The output-allowed outcomes are currently fixed to Binding detected, Inconclusive, and Not assessed. Reports additionally retain no-binding member estimates as explicitly identified attempted-model values; standard table exports omit them. A future preference could make this set configurable, which would change suppression behavior across reports and exports.

## ITC-017 — Extreme confidence interval bounds render as long fixed-point numbers

- Status: Resolved (2026-10-03).
- Problem: `FloatWithError` confidence bounds used the estimate’s fixed-point format, producing unwieldy extreme numbers.
- Resolution: After unit conversion/rounding, format each estimate, SD, and bound independently with `G6` at magnitude ≥ 10¹⁰; smaller components retain configured precision. Finite values stay numeric, using the unrounded converted value if rounding overflows; infinities use ∞/−∞ and NaN behavior is unchanged. Covered by FloatWithErrorCompactFormattingTests.

## ITC-020 — Multi-result report chapters ignored the output purpose

- Status: Resolved (2026-10-02).
- Problem: `CopyOptionsForResult` omitted `OutputPurpose`, so multi-result Diagnostic reports built Standard chapters despite Diagnostic validation.
- Resolution: `AnalysisReportBuilder` copies the report’s output purpose into every result chapter.

## ITC-021 — Result chapter appendix labeled every result identifier as result 1

- Status: Resolved (2026-10-02).
- Problem: `BuildResultChapter` labeled every Traceability Mode result identifier as result 1, including later chapters.
- Resolution: Each chapter uses the result’s position in the report for its identifier reference.

## ITC-022 — Report-wide details repeat in every result appendix

- Status: Resolved (2026-10-02).
- Problem: `BuildAppendix` repeated report-wide software/version/identifier details in every result chapter.
- Resolution: Reports use one closing appendix containing report details, report warnings, and a combined experiment sources table.

## ITC-023 — Front-page bookkeeping notice wording with one result

- Status: Resolved (2026-10-05).
- Problem: The front-page bookkeeping notice needed wording that also worked for a single result and mixed saved methods.
- Resolution: `BuildFrontPage` uses one neutral report-wide note naming saved concentration and injection-heat methods and their applicable members, including mixed methods within/across results, without repeated warnings or required actions.

## ITC-024 — Report notes for an extra-information option

- Priority: Low
- Status: Open; idea.
- Location: `AnalysisReportBuilder.AddReportAppendix`.
- Problem: The per-result "Scientific notes" and the "AIC likelihood" and "AIC scope" rows were removed from reports. Their content may suit an optional extra-information setting: the reported value is the best fit while bootstrap or profile likelihood only sets the uncertainty; the displayed RMSD is unweighted while weighted fits use a different objective; the AIC likelihood and pooling scope; and what the bookkeeping conventions mean.

## ITC-025 — Remove Pooled Comparison Diagnostics from reports?

- Priority: Low
- Status: Open; decision needed.
- Location: `AnalysisReportBuilder.AddPooledComparisonDiagnostics` (diagnostic output, independently assessed results).
- Problem: The pooled comparison is diagnostic only and does not determine member assessments. It is kept in the result's diagnostic summary for now; decide whether reports should show it at all. Diagnostic Summary exports have separate pooled-diagnostic columns.

## ITC-026 — Tandem source experiments as automatic supporting experiments

- Priority: Low
- Status: Resolved (2026-10-11); existing implementation verified at the user's request.
- Location: report contents selection and `AnalysisReportBuilder.BuildExperimentSourcesTable`.
- Problem: Recorded buffer references can be included automatically as supporting experiments. Tandem source experiments could be included the same way if the tandem provenance identifies them, and listed in the appendix Experiment sources table with their role.
- Resolution: The Experiment Merger records ordered source experiment IDs, retained through project save/reopen and cloning. Shared `AnalysisReportExperimentReferences` supplies direct buffer references and tandem sources in project order to both desktop report pickers, honoring `AutoSelectReportReferenceExperiments` at opening and when checking a result. Sources remain optional; applying an unchecked source does not re-add it, and experiments already covered by a selected result are not repeated as supporting chapters. Picker roles/tooltips distinguish buffer references, tandem sources, and both; the appendix lists all applicable target experiment labels. Missing recorded sources receive a count in the tandem's experiment details without preventing report generation. Older tandems and single-file concatenated imports without source links require manual source selection. The manual, shared help, and report map already describe this behavior. Reopening selection persistence (ITC-031), exposing the preference (ITC-033), and supporting-data request size (ITC-034) remain separate.
- Validation (2026-10-11): All 18 Core `TandemSourceProvenanceTests` and all three focused Avalonia tandem preselection/PDF cases pass. Native managed compilation and `AnalysisReportReferencesTests` pass, exercising automatic selection on/off, project order, combined roles/tooltips, result-selection changes, optional removal, and CoreGraphics PDF generation. No implementation or documentation changes were needed; only this stale tracker entry was updated. Full suites were not rerun for this verification-only task.

## ITC-027 — Solution-wide buffer ionic-strength correction

- Priority: Low
- Status: Open; deferred scientific-method follow-up.
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs`, buffer ionic-strength calculation.
- Problem: Each buffer currently estimates its correction using only its own contribution and nearest pKa transition as a two-species system. It adds neither salt nor other buffer contributions while solving that buffer's correction, so interactions across solution components are not represented.
- Follow-up: Consider a solution-wide calculation covering all buffer species, salts, and their coupled ionic-strength contributions, with independently validated reference cases.

## ITC-028 — Avalonia macOS menu bar and file browser activation

- Priority: Medium
- Status: Open; upgraded to Avalonia 12.1.4 (2026-10-13). App and test-project compilation verified; native missing-menu behavior still requires verification.
- Location: `AnalysisITC.Avalonia`, macOS application activation, native menu bar, and file browser dialogs.
- Problem: Intermittently, the macOS menu bar shows only the application name and does not load the application's menus. The user also reports that the file browser sometimes appears not to be active; this may be related, but a shared cause is unconfirmed.
- Workaround: Minimizing the application and selecting it again restores the menu bar.
- Expected behavior: The application menus should be available whenever the application is active, and file browser dialogs should become active when opened.
- Investigation (2026-10-03): The project references Avalonia 12.0.5. `AppMenuController.Install` attaches the window's File/Edit/Selection/Tools/Help menus in the `MainWindow` constructor, before the window is shown; `App.axaml` supplies the application menu. Menu refresh updates visibility and command state, and does not remove all top-level menus. `DisableDefaultApplicationMenuItems = true` disables Avalonia's added standard application-menu commands, not native menus. The bundle template has neither `LSUIElement` nor `LSBackgroundOnly`, and `MacDockIcon.Apply` only sets the icon.
- Native evidence: Avalonia 12.0.5's desktop lifetime shows the main window before entering the dispatcher loop. Its native `WindowBaseImpl.Show` immediately calls `activateIgnoringOtherApps:`. [Avalonia PR #21799](https://github.com/AvaloniaUI/Avalonia/pull/21799) identifies this early activation as a timing-dependent cause of unresponsive file-picker lists/sidebars that recover after switching apps; its deferred-activation helper is present in the 12.1.1 source and absent from 12.0.5. Separately, `AvnWindow.mm` restores window menus in `becomeKeyWindow` and switches to the application-only menu in `windowDidResignKey`. A missed or disrupted key-window transition could therefore explain the menu symptom and workaround, but this remains an inference rather than a confirmed shared cause.
- New evidence (2026-10-13): The user saw missing menus again on 2026-10-12 after launching through the terminal. The investigation host runs macOS 15.7.4. FT-ITC referenced Avalonia 12.0.5 at the start of the investigation. The current stable release is [12.1.4](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4), released 2026-10-09; its `WindowBaseImpl.mm` retains the deferred-activation fix, confirmed directly against the tagged source.
- Previous caveat resolved: [Avalonia issue #22111](https://github.com/AvaloniaUI/Avalonia/issues/22111) closed on 2026-10-04. The remaining PicView failure was traced to its own startup: the picker service depended on `desktop.MainWindow`, which was assigned only after activation. [PicView PR #392](https://github.com/Ruben2776/PicView/pull/392) assigns it during startup. FT-ITC already assigns `desktop.MainWindow` in `OnFrameworkInitializationCompleted`, so that report is no longer evidence that the upstream activation fix is insufficient here. It still does not establish that FT-ITC's missing menus share the picker defect's cause.
- Separate menu report: [Avalonia issue #22285](https://github.com/AvaloniaUI/Avalonia/issues/22285) describes menu-bar blanking and shortcut-induced unresponsive menus on macOS 27, with an unmerged native patch. Its reported OS and failure details differ from the current investigation; do not treat that patch as a demonstrated solution for FT-ITC.
- Validation (2026-10-13): All eight existing `SelectionMenuTests` and `StartupFileActivationCoordinatorTests` passed again. These headless tests validate managed menu construction/state and queued startup work; they do not reproduce or exclude a native macOS activation race. No native reproduction or upgraded-build comparison was performed, and no application code or dependency versions were changed.
- Upgrade verification (2026-10-13): The user upgraded all five app packages and `Avalonia.Headless.XUnit` to 12.1.4. The app build succeeded with zero warnings/errors; a non-incremental rebuild of the app and test project succeeded with eight warnings and zero errors. The full Avalonia test run completed with 394 passed, nine failed, and one skipped: seven report tests hit ITC-101, one preview test hit ITC-102, and one structuring rerun test returned no result. A focused 26-test comparison on 12.0.5 and 12.1.4 produced identical outcomes: 25 passed (including the report, structuring, menu and startup tests), with only ITC-102 failing. An isolated full 12.0.5 run also reproduced the ITC-101 exception, then stopped progressing and was cancelled. The structuring failure appears suite-state-dependent; its cause was not established. The old-version comparison used temporary package overrides and separate outputs, leaving the upgraded project files and normal restored assets intact. No application-code compatibility changes were required.
- Test-suite follow-up (2026-10-13): All nine failures are fixed under ITC-101/ITC-102. The structuring failure came from a retained test window whose unsaved-changes prompt cancelled teardown, leaving event handlers attached. The full Avalonia suite now passes (406 passed, one skipped), as does the full Core suite built in an isolated output directory (3,433 passed, one skipped). These results do not verify native macOS menu activation.
- Recommended next step: Validate repeated terminal launches on 12.1.4, checking both missing menus and picker file-list/sidebar clicks, then startup with and without Traceability Mode/recovery dialogs and the return to the main window. Retain the existing early main-window assignment and queued file activation. If menus still disappear, capture application activation, main/key window, and installed native menu before and after minimizing/restoring; use that evidence to choose any targeted startup or menu-restoration change. Keep this issue open until native behavior is verified; the separate test-suite failures are not evidence that the dependency upgrade failed to compile.

## ITC-029 — Improve analysis inspector null-test and fit summaries

- Status: Resolved (2026-10-05).
- Problem: The Analysis inspector combined null RMSD/ΔAICc and placed fit RMSD on the model heading, obscuring the test and fit scope.
- Resolution: Both Analysis inspectors render shared `NullModelComparisonPresentation.AnalysisInspectorRows` for `Model`, Null RMSD, ΔAICc, and Conclusion. macOS adds a separator/bold header; its fit summary shows the readable model and Global/Individual scope, with RMSD first and values right-aligned. Results-tab null sections and graph parameter boxes are unchanged.

## ITC-030 — Unify fitted-parameter presentation in Analysis

- Status: Resolved (2026-10-10).
- Problem: The desktop Analysis views differed in where fitted parameters appeared and whether the graph parameter box could be shown independently. The implemented Avalonia inspector also retained stale parameters after clearing a heat-only fit, or “No fit result” after attaching one, because its solution-change refresh required a completed baseline.
- Resolution: Both inspectors use the shared Core `AnalysisParameterSummaryPresentation` for the selected experiment's read-only model, scope, RMSD, fitted and derived parameters. Graph boxes and guides remain independent optional overlays, retaining parameter-display and larger-text preferences; editable starting values remain separate. Avalonia `ExperimentSolutionChanged` now queues the existing owner-checked UI refresh without requiring a baseline, updating the inspector and graph on fit attachment, replacement and removal. Processing-change refreshes retain their baseline check. Native macOS already refreshes its summary directly on solution changes and required no additional change. The manual already describes the intended inspector/overlay behavior; no edge-case help text was added. All 47 relevant Avalonia workspace/graph/reset checks pass, including heat-only changes from UI and worker threads and a queued change followed by experiment selection. The two new thread cases failed before the fix.

## ITC-031 — Removed supporting experiments are reselected when a report reopens

- Status: Resolved (2026-10-12).
- Problem: Both report windows recomputed automatic supporting selections at opening and matched stored reports by ordered result and supporting-experiment IDs. Removing an automatically selected experiment made the saved report appear lost until the user removed that experiment again.
- Resolution: Shared `AnalysisReportPresentation` matches result-ID sets independently of order or supporting selection. Both desktop report tools load the newest-created matching report, comparing creation instants in UTC and choosing the last collection entry for a tie. Opening restores saved supporting selections; automatic references apply only to new reports. Applying an unchanged result selection edits its supporting selection in place. A changed result selection restores its own saved report or creates a new one, ignoring supporting edits made in the same Apply. Interpretation text stays with its result set. Older variants remain accessible through Project Information (ITC-032). PDF chapters follow project order without rewriting stored IDs or advancing the report edit date. The manual and shared help describe the revised selection workflow.

## ITC-032 — Stored reports accumulate without limits or deletion

- Status: Resolved (2026-10-12).
- Problem: Report definitions accumulated in `.ftxtc` projects without a user-facing inventory or deletion action. `DataManager.RemoveReport` existed but neither desktop application exposed it, leaving saved selections, settings, context, and interpretation text difficult to inspect or remove.
- Resolution: Both desktops provide modal File > Project Information with counts and lists of loaded experiments, Analysis Results, and saved reports, including empty and report-only projects. Reports offer exact Open, Rename, selectable Details, Copy All of user-entered text, and individually confirmed Delete through `DataManager.RemoveReport`, without Undo. Missing-result reports retain their contents and allow details/copy/delete; opening and renaming are disabled. Existing names remain unchanged; new unnamed reports use a shared content-based display name. Names are separate from PDF titles and do not affect interpretation freshness, including legacy fingerprints. Optional `lastEditedUtc` records actual saved-content changes without advancing on opening, output generation, saving, restoration, or copies; older reports show Not recorded. Report-only saving, autosave, and saving after the final report is deleted preserve deletion on reopen. The manual, shared help, report map, format documentation, and schema are updated. Storage limits and size display were deliberately not implemented, and registration triggers are unchanged. The automatic-reference preference (ITC-033) and printed supporting-experiment control (ITC-091) remain separate.

## ITC-033 — Automatic report reference selection has no preference control

- Priority: Low
- Status: Open; deferred.
- Location: `AppSettings.AutoSelectReportReferenceExperiments`, `PreferencesState`; macOS and Avalonia preferences windows.
- Problem: The setting is stored and defaults to on, but neither preferences window exposes it, so users cannot turn automatic selection off. The manual (`09-figures-printing-export.md`) says reference selection follows "the application setting".
- Follow-up: Add a control to both preferences windows (tooltip text shared in Core), or remove the setting and update the manual. Revisit when report contents selection or the preferences windows are next changed, including ITC-026.

## ITC-034 — Supporting experiment size in AI interpretation requests

- Priority: Low
- Status: Open; idea.
- Location: `AnalysisInterpretationPackageBuilder` (supporting experiments); interpretation settings in both report windows.
- Problem: Supporting experiments are sent to AI interpretation with the same evidence as result members, including injection rows and, when enabled, compressed thermograms. Automatic tandem-source selection (ITC-026) will add more supporting experiments, and a tandem source's heats largely overlap the tandem's. The source does carry the raw thermogram and baseline, which the tandem lacks because it is built from baseline-corrected data.
- Ideas: A compact form for supporting experiments that are not members of a selected result (identity, metadata, processing summary, and links, without injection rows or thermogram); an "Include supporting data" interpretation setting. A setting would be saved with the report, needs tooltip text in Core and access rules, and must be documented in the relay contract and the manual.

## ITC-035 — Missing glyphs in Avalonia report text

- Priority: Medium
- Status: Resolved (2026-10-08).
- Location: `SkiaAnalysisReportRenderer.DrawText`, `SkiaPublicationFontSet`.
- Problem: The report renderer draws each string with a single selected font and does not substitute a font for missing glyphs. A preparer name containing `李` appears with a missing-glyph box in the preview; the name remains intact in the report document and PDF Author metadata. This uses the existing report text renderer and also affects ordinary report text containing unsupported characters.
- Resolution: Avalonia report and figure text (`SkiaAnalysisReportRenderer`, `SkiaDrawingContext`) now draw and measure through `SkiaPublicationFontSet.DrawText`/`MeasureText`, which split text into runs by font coverage. Characters missing from the publication face use an installed font found by Skia character matching (`ISkiaSystemFontSource.MatchCharacter`), requested with the face's family, weight and slant; fallback faces are cached per publication face and reused for later characters. Characters no installed font covers keep the missing-glyph box. Combining marks and format characters stay in the preceding run, and surrogate pairs are never split. Measurement, wrapping, preview and vector PDF use the same runs; fully covered text measures exactly as before, so existing layouts are unchanged. Line height still comes from the publication face. Long-word breaking in Core `AnalysisReportLayoutEngine` and the Avalonia renderer now breaks between text elements instead of UTF-16 units (affects both apps; no change for text without surrogate pairs or combining marks). Native macOS draws report text with CoreText, which substitutes fonts itself; its rendering of these characters was not inspected.
- Limitations: Substitution only, with no text shaping. Right-to-left and complex scripts (Arabic, Hebrew, Devanagari, Thai) show the correct glyphs but may be unjoined or in the wrong visual order. Coverage depends on installed fonts, so a minimal Linux system without CJK fonts still shows boxes.
- Validation: `PublicationFontFallbackTests` (Avalonia) use a fake font source to cover substituted runs and the requested family/weight/slant, unchanged runs and measurement for covered text, measurement of substituted widths, fallback-face reuse with single queries for uncovered characters, intact surrogate pairs and combining marks, and real installed-font rendering replacing the box (that last check passes without asserting when no installed font covers `李`). Five of the six tests fail with fallback disabled. Core `WrappingLongWordsKeepsSurrogatePairsAndCombiningMarksWhole` (four offsets) fails for two offsets without the wrapping change. The optional cover QA test (`FTITC_REPORT_QA_OUTPUT`) now uses mixed-script names (`Zoë 李明`); the rendered cover and PDF show `李明` in the results table, contents and sign-off, with PingFang SC Light/Regular/Medium subsets embedded. Full Core suite: 2,364 passed, one skipped; 104 Avalonia report, figure and publication-font tests pass; native `AnalysisReportReferencesTests` pass.

## ITC-036 — Processing details shown for integrated-heats imports

- Status: Resolved (2026-10-08).
- Problem: Reports showed baseline/integration details for imported integrated heats without a thermogram, implying processing had been performed.
- Resolution: `AnalysisReportBuilder` selects the block by thermogram availability: retain Processing and integration with a thermogram; otherwise show Data availability, Imported integrated heats, and injection use, omitting baseline/integration status and regions/times. Partial availability counts only integrated injections with finite current heats. Supporting experiments follow the same rule while retaining correction/reference notes; condensed repeats omit the block. Core and both renderers were checked. Bookkeeping-notice placement remains ITC-037.

## ITC-037 — Bookkeeping convention displayed in a large appendix notice

- Priority: Low
- Status: Resolved (2026-10-09).
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`AddReportAppendix`, `BuildExperimentMetadata`, `BuildProcessingItems`).
- Problem: The report presents saved concentration and injection-heat bookkeeping methods together in a large boxed notice in the appendix, away from the experiments they describe. This gives routine method metadata the visual weight of a warning and makes it harder to connect each method to its experiment.
- Resolution: The appendix "Bookkeeping conventions" notice (map P02, now retired) is removed. Each result experiment's "Processing and integration" (E04) or "Data availability" (E04a) block ends with an "Injection bookkeeping" row showing the method saved with that result's fit; without a fit-time record it keeps the existing "Unknown concentration method; <heat> heat" wording. Supporting experiments have no fit and omit the row. Condensed repeats (E03) omit E04/E04a and therefore the row, by decision. When experiments within one result have different saved methods, the result overview shows an orange "Mixed injection bookkeeping" notice (R03a) listing each method with its experiment labels, and Report warnings (P03) gains one line. Different methods across separate results are not flagged. `ANALYSIS_REPORT_MAP.md` and the user manual (`09-figures-printing-export.md`) are updated. Expanded-explanation notices (ITC-024) are unaffected.
- Validation: Core `AnalysisReportBuilderTests` cover the saved method in processing rather than the current preference, separate results with different methods (rows, no mixed warning), mixed methods within one result (overview notice level and text, report warning, appendix list), agreeing members, and the missing-record wording. The Avalonia `IndependentClassifiedReportRenderQaTests` assertion was updated from the appendix notice to the processing row. Full Core suite: 2,374 passed, one skipped, one failure in the allocation-budget test `ColdTenThousandReplicateEnvelopeFitsAllocationBudgetAndWarmReadDoesNotEvaluate`, which passed twice in isolation. 55 Avalonia report tests and native `AnalysisReportReferencesTests` (including PDF rendering) pass. Rendered pages were not inspected.

## ITC-038 — Add experiment name to report page headers

- Status: Resolved (2026-10-06).
- Problem: Continued experiment pages named only the report/result in their running header, making the experiment hard to identify.
- Resolution: `AnalysisReportLayoutEngine` and both renderers include the fitted experiment’s label/name beside the result name. Shared layout preserves the export date, prioritizes the result, and truncates names at text-element boundaries while retaining the experiment label. Supporting-data headers are unchanged.

## ITC-039 — Diagnostic summary graphs include no-binding experiments

- Status: Resolved (2026-10-07).
- Problem: Diagnostic thermodynamic summary graphs included No binding detected members; wide attempted-fit intervals could overwhelm the graph scale.
- Resolution: `BuildThermodynamicSummaryPlot` filters effective assessments in both report modes: exclude No binding detected, retain Inconclusive/Not assessed, honor overrides, and omit the graph if none remain. Diagnostic text names omitted members without claiming combined values are suppressed; attempted estimates remain in detailed tables. Core and both renderer regressions cover mixed/all-no-binding cases and wide intervals.

## ITC-040 – Identify information, caution, and warning box locations

- Priority: Medium
- Status: Resolved (2026-10-09).
- Problem: Currently we have a poor idea of where boxes might be placed.
- Suggestion: Identify places where boxes can be placed, what they contain, and what triggers them. In general, boxes should not show up for no reason. Boxes should be reserved for specific of note items that the reader needs to know about the presented data. This could be different bookkeeping conventions, ... exploration necessary.
- Resolution: Rule recorded under "Rules that affect many blocks" in `ANALYSIS_REPORT_MAP.md`, which also serves as the box inventory with triggers. Warning and Error boxes are reserved for anything that affects whether presented values can be trusted or compared. Information boxes are limited to output omitted or changed by an assessment (A04) or an opt-in expanded explanation (A09, A12, A14/E07, structuring). Routine metadata and data availability use rows. Applied in `AnalysisReportBuilder`:
  - Interpretation "Source and editing history" (I03) is a key-value block (Source, User edited, Provider, Model, Reasoning, Scientific guidance, Generated, Approved, Request; manual text: Source, Saved).
  - "Raw processing unavailable" (E01a) is removed; E04a states "Input heats: Imported integrated heats".
  - Supporting-experiment "Raw thermogram / Integrated heats / Experimental plots unavailable" boxes (S01) are removed; S02a and S03 rows already state missing data.
  - "Attached fit unsuccessful" (S04) is removed, because supporting chapters never present fits.
  - New "Fit warnings" Warning box (E04b) in each affected experiment chapter, before its parameter table. It holds the same boundary, bootstrap/leave-one-out boundary and limit-terminated messages that set result health, only for members whose warnings count (not no-binding).
- Kept: I01 out-of-date interpretation, R03 validity status (including the rare Information box for a Valid result with recorded reasons), R03a mixed bookkeeping, A04 no binding detected, P03 report warnings and the expanded explanations (ITC-024).
- Limitation: a supporting experiment with a thermogram whose injections are all integrated but have non-finite heats no longer gets a statement; only the heats panel is missing.
- Validation: Core tests updated for provenance rows (manual and generated) and the removed E01a box. New tests cover a supporting experiment with no data (rows only, no boxes), no boxes for a heat-only supporting experiment, and Fit warnings appearing only in the affected chapter, before its parameter table. The Avalonia data-availability assertion is updated. Full Core suite: 2,377 passed, one skipped; 61 Avalonia report/interpretation tests and native `AnalysisReportReferencesTests` pass. A rendered interpretation page showing the provenance rows was inspected. The manual now mentions per-experiment fit warnings.

## ITC-041 - Source file and source format in one line for analysis reports

- Status: Resolved (2026-10-06).
- Problem: Report source filename and recorded source format needed to appear together for each experiment.
- Resolution: Full, condensed, and supporting experiment details combine both under Source file, with placeholders for missing metadata. Both apps wrap complete values; the appendix filename column is unchanged.

## ITC-042 - Competitor properties affinity and enthalpy values are not showing up

- Status: Resolved (2026-10-10).
- Problem: The report test project's competitor properties appeared unavailable, suggesting non-finite values were being saved or loaded.
- Resolution: The older competitive result used manual inputs and has no fit-time competitor-source capture. The newer result uses From attributes and already saves and displays finite Kd and ΔH with uncertainty. Fixed the report fallback that could attribute the newer fit's live competitor values to the older fit; result chapters now use only their saved captures and explain missing captures using their saved model options. The shared Core builder serves both desktop renderers; no file-format change was needed.
- Validation: Regression tests cover the older manual result, absent historical captures, both newer result members, and exact preservation of captured values, SDs, and interval bounds through save/reopen. The historical-value regression fails with the old fallback and passes with the fix. Updated the Save Selected test to check every competitor experiment in the expanded fixture. Full Core suite: 2,528 passed, one existing skip; final focused Core run: 123 passed. Avalonia report tests: 47 passed; native `AnalysisReportReferencesTests` passed. The report map, manual, and in-app help now clarify that reports use values from their own fits.

## ITC-043 - Report baseline type could include information on baseline

- Status: Resolved (2026-10-06).
- Problem: The report’s Baseline method showed only Spline or Polynomial, omitting the chosen configuration.
- Resolution: The shared builder appends spline mode/point density or polynomial/segmented degree in both apps.

## ITC-044 - Explain saved FWE

- Status: Resolved (2026-10-06).
- Problem: `CapturedAffinity/SD/Lower/Upper` and corresponding enthalpy fields looked like redundant manual `FloatWithError` storage.
- Resolution: They are primitive fit-time `ExperimentAttributeSnapshot` fields for component-wise `SameDouble` comparison; the live attribute already persists as an FWE. The flat snapshot omits the missing flag, but required Kd/ΔH are captured as a finite pair or cleared together. Added `CapturedAffinityWithError`/`CapturedEnthalpyWithError` accessors (non-finite → `FloatWithError.NaN`) and used them in reports without a format change. Small-value comparison tolerance was addressed in ITC-055.

## ITC-045 - macOS report inspector does not allow scrolling all the way down.

- Status: Resolved (2026-10-06).
- Problem: The macOS report inspector could not scroll to its bottom options because content height was fixed at 720 pt.
- Resolution: `AnalysisReportViewController` pins scroll content to the visible area’s top/left/width and ties the section bottom to the content bottom, so the scroll range follows actual content.

## ITC-046 - Unnecessary summary caveat in report

- Status: Resolved (2026-10-06).
- Problem: Standard reports showed the unwanted caveat about approximate local summary intervals, unestablished 95% coverage, and omitted covariance.
- Resolution: Removed the caveat from Standard reports; its detailed explanation remains available with expanded explanations.

## ITC-047 - Too long block header: "Combined across experiments at the mean temperature: 25.00 °C"

- Status: Resolved (2026-10-06).
- Problem: The combined-parameter heading was too long and did not clearly separate the evaluation temperature.
- Resolution: Use Combined parameters with Evaluation temperature as the first row, without calculation changes. Manual/help explain that temperature series use the current Reference temperature preference; other results use mean experiment target temperature.

## ITC-048 - Join report summary model and fit details blocks 

- Status: Resolved (2026-10-06).
- Problem: The analysis summary split model settings and fit details into separate blocks.
- Resolution: Both report renderers use one `Model` and fit details block retaining settings, constraints, and diagnostics. Fixed parameters and per-experiment fit details remain separate.

## ITC-049 - Experiment name font and truncation

- Status: Resolved (2026-10-06).
- Problem: Bold panel names blurred experiment labels such as 1A, while a fixed character cap truncated names too aggressively.
- Resolution: Figure canvas headings use bold panel labels and regular-weight names. Removed `PanelTitleMaximumCharacters`; both renderers use shared `PublicationFigureCanvasBuilder.FitPanelTitle` to middle-shorten the full name to the measured panel width.

## ITC-050 - Result health can have warning if analysing a no binding experiment 

- Status: Resolved (2026-10-06).
- Problem: No-binding members’ parameter-boundary and uncertainty/optimizer-limit warnings degraded overall result health, including independent collections.
- Resolution: Core `BindingAssessmentInterpretation` treats only effective No binding detected as non-binding; `AnalysisResult.FitWarningMembers` excludes those members from `Health/HealthReasons` for independent, single, and pooled results. Input validity is unchanged; competitor badges follow `source.Health`, so Inconclusive sources show Warning. Other warning surfaces and uncertainty work were outside this fix (ITC-052/ITC-053); competitor-source assessment display is ITC-054.

## ITC-051 - Ensure report summary table and summary values are aligned 

- Status: Resolved (2026-10-08).
- Problem: Standard reports needed consistent suppression of no-binding parameter values, summary labels, fit warnings, and combined output.
- Resolution: In `AnalysisReportBuilder` (`ANALYSIS_REPORT_MAP.md`): A06 leaves no-binding parameter/RMSD cells blank (distinct from non-finite “—”), retaining AICc/conditions; Standard labels only No binding detected/Inconclusive. E05 omits their parameter table, E08 omits RMSD, and P03 omits their fit warnings (ITC-056/ITC-053). Diagnostic values/labels, Inconclusive/Not assessed, and A08 remain unchanged; combined parameters are omitted if any member is no-binding, without subset recombination. Shared assessment formatting marks manual overrides when `ShowAssessmentProvenance` is on or Traceability Mode forces it; E08 always marks them, and collection summaries mark any member override. The hidden/unsaved option remains ITC-076. Standard Individual exports retain ionic strength/protonation enthalpy. Core and both report renderers were checked.

## ITC-052 - Skip uncertainty estimation for results assessed as no binding

- Priority: Medium
- Status: Resolved (2026-10-09).
- Problem: Parameters of an experiment assessed as no binding are not expected to be meaningful, yet bootstrap/LOO/profile uncertainty is still estimated for them, which costs time and produces warnings that ITC-050 now hides from result health.
- Open questions: The assessment is only known after the binding and null fits, and it can be changed manually afterwards. Decide what happens when a manual override switches a result to binding (estimate on demand, require Update Result, or mark uncertainty unavailable), and how Update Result should behave.
- Thoughts: This is mostly to optimise non-converging very long running non-binding analyses error estimation attempts. Manual assessments should not be overwritten on fitting completion. Manual assessment change from non-binding to binding should mark the results as stale.
- Resolution: The fit is now followed by the null comparison and assessment before any uncertainty estimation (`Solver2.cs`: `UncertaintyGate`, `EstimateUncertainty`). Estimation is skipped only when the effective assessment is No binding detected (Inconclusive and Not assessed still estimate). It covers bootstrap, leave-one-out and profile likelihood. Single and pooled (shared-parameter) fits skip for the whole result; independent collections fit every member first, calculate the comparison, then estimate each member unless it is no binding. A skipped convergence records the new outcome `SkippedNoBinding` with the shared text "Not estimated (no binding detected)" (`SolverConvergence.SkippedForNoBindingDescription`); multi-experiment outcomes treat skipped members as not estimated, and the profile summary excludes them. Saved as `errorOutcome` `skipped-no-binding` (`FTXTC_FORMAT.md`, component schema); older readers cannot open packages that contain it.
  - Manual assessments: Update Result passes manual overrides to the solver, so the effective (manual) outcome decides whether to skip, and `AnalysisResult.UpdateSolution` now keeps manual overrides (previously cleared). A manual Binding detected assessment on a skipped fit adds the validity reason "Uncertainty was not estimated because no binding was detected. Use Update Result to estimate it." and makes the result Partially invalid or stale until updated. Update Result accepts skipped results without bootstrap refits and replaces profile results whose fits were all skipped.
  - Display: result views in both apps show "Errors: <method> · Not estimated (no binding detected)" and "Bootstrap: Not estimated" (`UncertaintyEstimationPresentation`); members of independent collections show the largest member replicate count. Report Uncertainty rows read "<method>; not estimated (no binding detected)". Status bar and profile status use the same text.
  - Setting: hidden `AppSettings.SkipUncertaintyEstimationForNoBinding` (default on, persisted, not in Preferences); see ITC-083.
- Accepted limitation: in an independent collection with any skipped member, combined replicate sets are incomplete, so combined/temperature-dependence uncertainty is unavailable. Standard output already omits combined values when a member is no binding; Diagnostic output shows them without uncertainty. Update Result falls back to the current preference iteration count when a skipped result stores no replicates.
- Validation: New Core `NoBindingUncertaintySkipTests` cover single no-binding fits skipping bootstrap, the setting turned off, binding fits still estimated, independent collections skipping only no-binding members with the comparison kept, manual binding overrides passed to the solver, staleness from a manual binding assessment and its removal, `.ftxtc` round trip of the new outcome, and multi-experiment aggregation. `AnalysisResultUpdaterTests.UpdateResultSkipsOrEstimatesUncertaintyFromManualAssessments` covers Update Result skipping with a manual No binding, staleness after switching to Binding, and estimation plus retained override after the next update. A report test covers the Uncertainty wording. Four existing tests were updated: three now expect manual overrides to survive refits, and the independent profile workflow test disables skipping because its constant fixtures are no binding. Full Core suite: 2,423 passed, one skipped. Two failures: `CompetitiveBindingModelTests.StateResolverHasNoSteadyStateAllocation` passed twice in isolation. `CompetitorSourceSaveSelectedTests.ResultSaveIncludesCompetitorSourceAndItsMembers` fails consistently since commit d1640562 replaced `report_test.ftxtc`, which now contains more than one experiment with a competitor attribute; that failure is unrelated to this change. 103 Avalonia result/analysis/report tests and all native AppKit checks pass. Neither app was exercised interactively.

## ITC-053 - Non-binding members still show fit warnings outside result health

- Priority: Low
- Status: Implemented (2026-10-12); pending visual review. Reports were handled on 2026-10-08: Standard reports omit these warnings from report warnings (P03); Diagnostic reports keep them.
- Problem: After ITC-050, fit warnings from members assessed as no binding no longer affect result health, but they still appeared in each experiment's row in the macOS/Avalonia result views, in the status bar after fits and refits (combined convergence boundary contacts), and in web viewer result warnings, so a Valid result could show bound warnings.
- Resolution: Only an effective (manual or automatic) No binding detected changes behavior (`BindingAssessmentInterpretation`); Inconclusive and Not assessed keep their warnings. Result rows (macOS Analysis Result tab, Avalonia Results workspace) keep the member's warnings followed by "Not counted in result health: no binding detected." (`ParameterBoundaryWarningFormatter.ResultMemberMessages`, using `AnalysisResult.FitWarningMembers`); Avalonia shows those lines in muted rather than warning colour. Web viewer result warnings carry the same line; per-fit warnings in the experiment fit view are unchanged because those fits need not belong to a result. The status-bar "parameter bound reached" message omits contacts of no-binding members after fits and Update Result on both desktops (`CountedContacts`; shared contacts are omitted only when no member is treated as binding, i.e. a pooled fit assessed as no binding). Fit-time assessments come from `BindingAssessmentMember.ForFit`, mirroring a new result's automatic assessments; Update Result uses the result's member assessments. The bound warning now follows the completion message on both desktops (macOS previously showed it last). The macOS `AnalysisResultValidityPresentation.ExperimentListText` also lists these warnings but has no callers and was left unchanged. The AI interpretation package still lists no-binding boundary contacts; out of scope. Avalonia's blank uncertainty message after independent fits is left to the in-progress ITC-090/ITC-094 completion wording (macOS already uses `UncertaintyEstimationPresentation.Completion`); see ITC-100.
- Validation: Core `NoBindingFitWarningTests` (7) cover annotation, manual overrides, members without warnings, local/shared contact filtering with literal expected messages, fit-time assessments matching a new result, and the viewer round trip through `.ftxtc`. Avalonia `BoundaryWarningPresentationTests.NoBindingExperimentWarningsAreMarkedAsNotCounted` checks the result rows. A temporary scenario run (single, independent, pooled; binding, no binding, mixed; best-fit and bootstrap bound contacts) confirmed the filtered status messages. Full Core suite: 3,092 passed, 1 skipped, 15 failed, all in `NoBindingUncertaintySkipTests` under concurrent ITC-090 work and unrelated to this change. Avalonia focused suites passed (64 + 4); macOS compiled and `AnalysisParameterDraftTests` native checks passed. Neither app was exercised interactively.

## ITC-054 - Competitor source assessed as no binding

- Status: Resolved (2026-10-06).
- Problem: A competitor source assessed as no binding could show Valid after its boundary warnings stopped affecting health, despite supplying potentially meaningless Kd/ΔH.
- Resolution: `CompetitorResultPreviewBuilder` adds No binding when any effective source member is non-binding, with precedence Unknown > Stale > No binding > Changed > Warning > Valid. Tooltips explain Kd/ΔH implications and member counts; Inconclusive has an explanatory tooltip and health-based Warning. This changes display only, leaving fitting and the source picker unchanged.

## ITC-055 - Validity comparison tolerance hides small competitor Kd changes

- Status: Resolved (2026-10-07).
- Problem: `SameDouble` used an approximately 1 nM absolute floor for molar Kd, hiding a 5 → 5.8 nM source change and pM-scale changes in values, SDs, and intervals; prebound-ligand concentration shared the blind spot.
- Resolution: `AnalysisResultValiditySnapshot` compares captured competitor Kd/SD/interval endpoints and prebound-ligand concentration/SD at their actual molar scale with `1e-9` relative tolerance and no absolute floor. Zero differs from nonzero; NaN/infinity equality is preserved. Other tolerances, fitted values, and schema are unchanged. Focused validity/competitive-model/preview tests cover small values, accepted roundoff, and snapshot round trips.

## ITC-056 - Core test expects no parameter table in Standard no-binding reports

- Status: Resolved (2026-10-08) with ITC-051: Standard reports omit the per-experiment parameter table for no-binding members, so the test passes as written.
- Problem: `NegativeStandardReportUsesAssessmentPathWithoutBindingParameterSection` failed because `BuildExperimentSections` always added attempted parameters for Standard no-binding reports.
- Resolution: ITC-051 changed the builder to omit their per-experiment parameter table, restoring the test’s expected policy and aligning the manual/help. The test passes as written.

## ITC-057 - Interpretation access code is stored in plain text

- Priority: Low
- Status: Open
- Location: `AppSettings.InterpretationOperatorCode` (`SaveToStorage`, load), `MacUserDefaultsSettingsStore`, `AvaloniaJsonSettingsStore`.
- Problem: The interpretation access code is sent as a Bearer credential to the relay, but it is saved unencrypted in NSUserDefaults on macOS and in the Avalonia JSON settings file. Any process running as the user, and any backup of those files, can read it. Only a SHA-256 hash is needed for the cached-access checks, but the plain value is stored as well.
- Suggestion: Store the code in the OS credential store (Keychain on macOS; the platform equivalent for Avalonia) and keep only the hash in preferences. Migrate existing values on first load.
- Comment: the interpretation access code is not super secret and I would prefer not starting to mess with platform secure storage if possible. It should be considered as an activation code, not a personal secret.

## ITC-058 - Asymmetric least squares baseline has no effect

- Priority: Low
- Status: Implemented (2026-10-12); ALS removed by user decision, as it will not return in the near future. Awaiting user confirmation.
- Problem: `AssymetricLeastSquaresInterpolator.Interpolate` computed an ALS curve but never assigned `Baseline`, so a new ALS processor could not produce a baseline and its Iterations, Lambda and Asymmetry settings had no effect. The run was O(n²) in thermogram samples, `Copy` lost the settings, and the native macOS processing view cast a restored ALS processor to `PolynomialLeastSquaresInterpolator`, yielding a null reference. ALS could not be selected in either app, but `.ftxtc` (`"asl"`) and legacy `.ftitc` (type 1) files could still restore it.
- Resolution: The interpolator class is deleted. `BaselineInterpolatorTypes.ASL` remains as an `[Obsolete]` member so saved files and validity snapshots stay readable; `DataProcessor.InitializeBaseline` restores it as a polynomial interpolator with default settings, which covers `.ftxtc` and both legacy `.ftitc` paths. Saved ALS settings are ignored; the stored baseline and completion state are retained, and no notice is shown. Writers never emit the `asl` type and always write `asl: null`. Validity snapshots that recorded `asl` keep it, so such results report changed processing. The AI interpretation package no longer has ALS baseline controls (they were only emitted for ALS processors), and the macOS processing view's ALS case is removed. `FTXTC_FORMAT.md` and the schema's `asl` definition describe the read-only legacy type; the schema enums still accept `asl`. ALS was not documented in the manual or help.
- Validation: New Core `RetiredAlsBaselineTests` (4) cover an `.ftxtc` `asl` processor restoring as a degree-12 polynomial with its stored baseline, corrected trace and completion state, re-saving as `polynomial` with `asl: null`; a legacy `.ftitc` type-1 processor restoring as polynomial; a recorded `asl` validity snapshot differing from the restored polynomial; and the `asl` wire identity. 225 focused persistence, schema, legacy and interpretation tests pass. Full Core suite: 3,100 passed, 1 skipped, and only the 15 known ITC-090 failures. Avalonia builds; the native macOS managed code compiles through `run-analysis-null-graph-tests.sh` and `ProcessingSliderTests` pass. Neither app was run interactively.

## ITC-059 - Completing a save clears edits made while the file is being written

- Status: Resolved (2026-10-07).
- Problem: `SaveAsync/SaveWithPathAsync` unconditionally marked the current document clean after asynchronous writes, clearing newer edits absent from the saved snapshot and allowing close/autosave to miss them.
- Resolution: `ProjectWriter` and `DocumentDirtyTracker` track document identity plus monotonic edit revision, including repeated/suspended edits; saves clear dirty state only if captured identity/revision still match outside import/restoration scopes. Queued destinations resolve under the save gate, held through completion bookkeeping; abandoned requests return false. Both apps use `SaveForCloseAsync`, keeping the document open when newer edits remain. Identity changes on clear/clean native open/recovery into an empty document, while appends retain it. Deterministic save/tracker/autosave tests cover these boundaries; the format is unchanged. Mutations bypassing `MarkModified`/document-change notifications remain invisible.

## ITC-060 - Duplicating buffer-corrected data loses the applied correction

- Status: Resolved (2026-10-07).
- Problem: `DuplicateSelectedData` copied raw injection heats before adding the buffer attribute, leaving advertised buffer correction unapplied and reference updates unsubscribed (10 − 2 µJ became 10 µJ instead of 8 µJ).
- Resolution: Call `SetBufferSubtraction` after duplicate injections/attributes exist, restoring corrected heats and reference subscriptions; unresolved reference attributes remain unchanged. `BufferSubtractionDuplicationTests` covers initial/later heats with and without raw data and confirms clearing the copy’s buffer preserves the original subscription.

## ITC-061 - Batch export can overwrite another experiment in the same batch

- Status: Resolved (2026-10-07).
- Problem: Generated export suffixes could collide with real experiment names: sample, sample, `sample_1` wrote the same `review_sample_1.csv` twice, silently overwriting one experiment.
- Resolution: `Exporter.PlanOutputs` allocates unique paths across the actual batch once. Unique names keep `<base>_<name>`; case-insensitive duplicates and blank names receive the next free `_<n>` suffix, skipping reserved names. Data export counts only thermogram-bearing experiments. Overwrite confirmation and `WriteOutputs` reuse the plan; `ExportOutputPlanTests` covers collisions.

## ITC-062 - Save Selected omits required buffer-reference experiments

- Status: Resolved (2026-10-07); reproduced (2026-10-06).
- Problem: Save Selected omitted external buffer references, causing strict .ftxtc reads to fail and recovery to retain corrected heats without a reproducible correction source.
- Resolution: `ProjectWriter.SaveSelectedAsync` keeps single-experiment saves to one experiment and stores a `BufferSubtractionReferenceSnapshot` in the attribute when the reference is not written: reference name plus included/integrated injection numbers and raw heats/SDs. Without the live reference, stored values recalculate corrections on load, reintegration, attribute edits/copies, and duplication; strict reads succeed. Later saves retain the snapshot; a loaded matching reference takes precedence and refreshes it. Result saves also include loaded member buffer references. Both editors show Missing reference (stored values retained); reports/status identify stored values. The optional schema-1.6 field needs no version bump and earlier readers ignore it. `BufferSubtractionStoredReferenceTests` covers all three methods, strict round trips, live precedence, and edits/copies. Competitor-result dependencies are ITC-074.

## ITC-063 - Trailing slash bypasses the Web viewer upload concurrency limit

- Status: Resolved (2026-10-07); reproduced locally before the fix (2026-10-06).
- Problem: Exact-path upload admission guarded `/api/viewer/open` but routing also accepted `/api/viewer/open/`, allowing concurrent project parsing outside the shared limit.
- Resolution: `ViewerUploadLimits/Program.cs` attach admission metadata to the endpoint and route before middleware. Canonical/trailing-slash/mixed-case paths acquire a disposable `ConcurrencyLimiter` lease before antiforgery/body reads/parsing. Defaults: one active upload per process, five FIFO waiters, 30-second wait-only timeout. Overflow returns `503 viewer_busy`, timeout `503 viewer_queue_timeout`, both with Retry-After: 1. Cancellation frees queue capacity and all admitted completion/error paths release the lease. Per-request limits/UI are unchanged; deterministic admission/endpoint tests cover route variants, FIFO, timeout, and cleanup.

## ITC-064 - Rejected result-update preparation removes attached experiment fits

- Status: Resolved (2026-10-07); reproduced before the fix (2026-10-06).
- Problem: `AnalysisResultUpdater.PrepareSolver` attached new models before validating options/initializing the solver; rejected preparation left live experiments without their previous fits, although the stored result survived.
- Resolution: Capture exact live `Model` references before factory preparation and restore them on any subsequent preparation exception, then rethrow. Rollback preserves distinct/null attachments without solution-change notifications or dirty/revision changes. Successful preparation and desktop error handlers are unchanged; later fitting, cancellation, and acceptance are outside this transaction boundary.

## ITC-065 - Avalonia Release tests assume obsolete labels and a debug-only control

- Status: Resolved (2026-10-07).
- Problem: Avalonia Release tests reflected debug-only `extraTraceabilityCheck` and searched obsolete “Locked Parameters” capitalization, failing before behavioral assertions; separate from ITC-007 threading failures.
- Resolution: `AnalysisReportEnhancementTests` checks report-ID visibility/save/reopen and other result-specific settings without the checkbox. `LockedParameterPresentationTests` expects sentence case and locates the section case-insensitively. Production code is unchanged; the separate report-policy failure was ITC-056.

## ITC-066 - Native interpretation layout test rejects the shared generation guard

- Status: Closed (2026-10-07); stale test assertion, no application defect.
- Problem: `InterpretationLayoutTests.swift` required a literal service/access conjunction after production delegated generation access to `InterpretationPackageSizeEstimate.CanGenerate`, falsely reporting a missing guard.
- Resolution: The native source assertion verifies the shared helper receives service, access, busy, and size state, allowing whitespace variations. Added Core coverage for unavailable service alongside denied access/busy/oversize cases. The native fixture and focused Core interpretation tests passed; production code is unchanged.

## ITC-067 - Injection heat direction depends on buffer-subtraction timing

- Status: Resolved (2026-10-09).
- Problem: `SetPeakArea` classified heat direction from corrected `PeakArea`, so assigning buffer subtraction after integration retained the raw direction while reintegration could change it to the corrected direction. Buffer uncertainty could also change the classification to Unknown.
- Resolution: Direction describes the integrated peak before buffer subtraction, as agreed with the user. Shared Core `InjectionData.SetPeakArea` uses `RawPeakArea` and its SD with the existing strict three-SD threshold. Buffer assignment, reference updates, and removal correctly leave direction unchanged; both desktop apps use this Core state. Corrected heats, uncertainties, fitted results, plotting consumers, and serialization are unchanged. Previously saved direction fields restore as stored until peak areas are set again.
- Validation: Nine independent regression cases fail before the fix and pass after it, covering both signs, buffer-assignment order, reintegration through `SetPeakArea`, duplication, reference updates/removal, raw versus corrected SD, zero heat, and exact three-SD boundaries. All 53 focused buffer-subtraction, injection-integration, and thermogram-scale tests pass. Full Core suite: 2,373 passed, one skipped. `git diff --check` passes.

## ITC-068 - Structuring evaluation-temperature options need scientific review

- Priority: Medium
- Status: Open (2026-10-07).
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`, `SRTempMode` and the entropy term in the structuring calculation; result-view temperature-mode controls on both platforms; report Structuring chapter ("Evaluated at").
- Problem: The published Spolar–Record method, and its adaptation in Theisen et al. (JACS 2021), evaluates the entropy terms at the isoentropic temperature, where ΔS = 0. At issue creation, FT-ITC also offered the mean experiment temperature, where the fitted parameters are arguably best determined, and the reference temperature, to compare interactions at a common temperature. These two options are not part of the published method. It is unresolved whether evaluating away from the isoentropic temperature is valid, and how its results should be interpreted or compared with published values.
- Temporary restriction (2026-10-13): Both desktop apps show a fixed **Evaluated at: Isoentropic point** row and explicitly select that mode when starting a structuring run. Mean and reference modes are hidden pending scientific review. Existing saved outputs retain their values, evaluation temperature, and completed mode when viewed or reported; the Core alternatives remain available for research. The equations, calibration, and project format are unchanged. **ITC-068 remains open** for the scientific review.
- Plan: [Structuring methods — research plan](STRUCTURING_METHODS_PLAN.md). Review scientific justification, matched calibration, and the interpretation of temperature choices there; retain the reference-temperature research direction and keep ITC-072 as a separate alternative. The temporary desktop restriction does not approve an alternative scientific method.
- Validation (2026-10-13): Both desktop checks cover the fixed setup row, new isoentropic runs, saved outputs in all three modes, Celsius/Kelvin display and uncertainty, and preservation after cancelled reruns. The native harness and two Avalonia tests pass; 232 focused Core tests pass, including structuring, saved evidence, report output, and classified-output policy. The full Core run had 3,413 passes, one skip, and two unrelated report-assertion failures during concurrent workspace changes; both failures passed in the subsequent focused run. `git diff --check` passes.

## ITC-069 - Structuring result views scale saved entropies by the current reference temperature

- Status: Resolved (2026-10-07).
- Problem: Both structuring result views multiplied saved entropies by the current reference-temperature preference while labeling the saved temperature. Changing 25 °C to 37 °C shifted −TΔS contributions by about 4% and disagreed with reports; residue counts were unaffected.
- Resolution: Both views use the saved evaluation temperature for hydration/conformational contributions, matching AnalysisReportBuilder.AddSpolarRecord. Focused Core/Avalonia and native output checks confirmed preference changes leave saved contributions/temperature unchanged. Other temperature-mode and uncertainty questions remain ITC-068, ITC-070, and ITC-071.

## ITC-070 - Structuring contribution uncertainty omits joint temperature variation

- Status: Closed, not an issue (2026-10-07).
- Problem: Raised whether `SROutput.HydrationContribution/ConformationalContribution` should propagate joint temperature/entropy variation when scaling saved entropy uncertainty by the central saved temperature.
- Resolution: Closed as not an issue: the central evaluation temperature is treated as a fixed value without uncertainty. The existing entropy-uncertainty scaling is retained; this concern was excluded from ITC-069.

## ITC-071 - Structuring views display saved temperature uncertainty differently

- Status: Resolved (2026-10-09).
- Problem: Avalonia displayed saved structuring temperature uncertainty while native macOS showed only the central value. Avalonia also kept this output in Celsius when Kelvin was selected.
- Resolution: Both desktop output rows and the report's saved structuring temperature use `StructuringPresentation.FormatEvaluationTemperature`, which formats the saved `FloatWithError` with `AsNumber` under the selected uncertainty style and Celsius/Kelvin units. Kelvin shifts the value and interval endpoints without changing the SD; unavailable temperatures remain unitless “Unavailable”. Avalonia refreshes the analysis panel immediately on a unit change. Saved values, project serialization, and entropy-contribution calculations are unchanged. Independent literal Core expectations and report/Avalonia/native output checks cover units, available/absent uncertainty, display styles, and saved output after reference-temperature preference changes.

## ITC-072 - Structuring with an explicit polar-hydration entropy term

- Priority: Medium
- Status: Open; research option (2026-10-07). Related to ITC-068, which is pursuing the reference-temperature method.
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`; per-residue calibration constant; evidence in `Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/`.
- Problem: The current entropy split leaves the polar share of ΔCp in the conformational remainder, making estimated residue counts depend on evaluation temperature. Explicit polar hydration is a research alternative with additional assumptions and calibration requirements.
- Plan: [Structuring methods — research plan](STRUCTURING_METHODS_PLAN.md). Proposed equations, exploratory evidence, and open calibration/validation questions are recorded there. This remains a research option; it is not an approved replacement for the reference-temperature direction under ITC-068.

## ITC-073 - Mixed member assessments reported as a uniform collection verdict

- Status: Resolved (2026-10-07).
- Problem: A precedence-based collection verdict labeled mixed binding/no-binding members No binding detected in reports, exports, clipboard output, and interpretation requests, despite desktop views showing Mixed assessments.
- Resolution: A shared collection-summary type reports the common effective outcome only when all independent members agree; any difference, including Inconclusive/Not assessed and overrides, becomes Mixed assessments under Member assessments. Mixed is not a selectable/persisted individual verdict. Shared presentation feeds both apps, reports/exports/clipboard, and separate effective/automatic AI summaries; relay guidance preserves member authority for new and legacy summaries. Single/pooled assessments and output eligibility are unchanged. Outcome-pair, override, persistence, presentation, interpretation, and renderer checks cover the policy.

## ITC-074 - Saved Analysis Results are not reproducible in isolation

- Status: Resolved (2026-10-08); the related ID-remap gap is tracked as ITC-077.
- Problem: Result Save Selected omitted Analysis Results referenced through `CompetitorResult` attributes, their members, and nested dependencies. Cached Kd/ΔH allowed refitting, but the source could not be inspected or re-evaluated from the file alone.
- Resolution: `ProjectWriter.SaveSelectedAsync` recursively writes loaded competitor source results, their members, and loaded buffer references, applying ITC-062 at every level. Each result is written once to end cycles; unloaded sources retain cached Kd/ΔH fallback. Save the actual source rather than a name/value snapshot. Single-experiment saves remain one experiment; no format change. `CompetitorSourceSaveSelectedTests` covers isolated strict reopening/link resolution, nesting/cycles, unloaded sources, and single-experiment scope. Reopening with colliding result/solution IDs still needs the separate ITC-077 remapping fix.

## ITC-076 - Always identify overridden assessments in reports

- Priority: Low
- Status: Resolved (2026-10-13).
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` and `AnalysisReport.cs`; `Documentation/ANALYSIS_REPORT_MAP.md`.
- Problem: Since ITC-051, report summaries marked overridden assessments "(manual)" only with the hidden, unsaved `ShowAssessmentProvenance` option or traceability enabled; experiment fit details always showed it.
- Resolution: Always identify manual overrides wherever assessment text is displayed in Standard and Diagnostic reports. Collection summaries carry "(manual)" when any member is overridden. Removed the hidden option and its copy/traceability handling instead of exposing or saving it. The shared Core formatter supplies both desktop renderers; assessment visibility and output-suppression rules are unchanged. Updated the report map, manual, and in-app help. This supersedes ITC-051's conditional marker policy without changing project serialization.
- Validation: Eight regression cases cover both manual outcomes, Standard/Diagnostic output, traceability on/off, automatic members without markers, and nested result chapters. Full Core run: 3,413 passed (including all 172 report-builder tests), one skipped, and two outdated marker assertions failed; those were corrected and all 17 `ClassifiedOutputPolicyTests` passed on rerun. Avalonia report checks: 49 passed initially, with the remaining test passing after its uniform manual-assessment expectation was updated. Native `AnalysisReportReferencesTests` passed, including mixed/uniform manual assessments and CoreGraphics PDF rendering.

## ITC-077 - Warn when importing existing saved object IDs

- Priority: Medium
- Status: Resolved (2026-10-08); overlapping imports reproduced in regression tests.
- Location: `AnalysisITC.Core/DataReaders/FtxtcImportResolver.cs`, `DataReaders.cs`, `FTXTCReader.cs`; dedicated Avalonia and native macOS prompts.
- Problem: Importing overlapping `.ftxtc` projects silently renamed saved identities without updating competitor result/source-fit links and other saved references. These are repeated saved objects, not expected collisions from the ID generator.
- Resolution: One warning per incoming file offers **Skip Duplicates** by default or **Import Copies**. Files remain sequential. Skip retains existing experiments, results, and reports while accepting new content, including results using existing experiments and immutable saved fit identities. Incoming primary and saved Null models retain their own result context without reinitializing fits or changing existing attachments/parents; bootstrap samples and historical validity inputs remain intact. Strict reopening restores separate model contexts when multiple results share a saved fit. An entirely skipped file succeeds without changing the save destination or dirty state; new reports import even when no new data/result rows remain.
- Copy behavior: Reserve existing and incoming identities, map only conflicts, and update incoming competitor/source-fit, buffer, tandem, validity, member-assessment, Null comparison, profile, bootstrap, and report references. Preserve external references, captured values/uncertainties, names, and original interpretation provenance; evaluate report freshness against the resolved project. No file-format change; legacy `.ftitc` behavior is unchanged. Enter, Escape, and closing choose Skip on both desktops; the noninteractive fallback logs the warning and skips.
- Validation: All 18 `FtxtcImportResolverTests` pass, covering queued/separate identical files, partial overlap, both actions, shared fits and parent contexts, changed historical inputs, report-only additions, no-op state, external references, fallback/legacy behavior, copied metadata and strict save/reopen. All three isolated Avalonia prompt cases pass, including keyboard defaults with Copy focused. Native AppKit modal checks pass for shared wording/tooltips, Enter, Escape, closing, explicit Copy, and layout. Full Core suite: 2,333 passed and one skipped. All 14 schema tests pass. The refitted JORS fixture exposed an omission in the published solution model enum: it now accepts saved Offset Null solutions already supported by the reader and writer, with focused coverage of the three heat-method schema versions. The JORS project data is unchanged.

## ITC-078 - Experiment designer resets parameters when rebuilding setup

- Priority: Medium
- Status: Resolved (2026-10-08).
- Problem: Avalonia `SetupExperiment` replaced the experiment and `SetupModel` replaced its factory, unconditionally applying N = 1 and enthalpy = -30000 J/mol and reinitializing other parameters from the new experiment. Instrument, concentration, injection, automatic volume, small first injection and tandem changes all reset user values; in One-Set-Of-Sites, injection count 20 → 21 changed N 1.7 → 1, enthalpy -43210 → -30000 J/mol, log10 affinity 7.25 → 7 and offset 1234 → 0 J/mol. Model switching was inconsistent (N/enthalpy reset, affinity/offset reused from the attached solution). Native macOS reused controls bound to obsolete parameter objects and, after its in-place fit, could read fitted values back as simulation inputs. In both apps setup controls stayed enabled during a fit, so a setup change could leave new data without a model; native also reacted to every solver's start/finish events.
- Resolution: Shared internal `ExperimentDesignerState` (`AnalysisITC.Core/Analysis2/ExperimentDesignerState.cs`), one per designer window. It records parameter values and model options only from user edits, keyed by parameter/option key, so values persist across setup changes, survive switching to a model without that key and back, and are shared between models with the same key. `CreateFactory` initializes the model, applies the window's options after `InitializeModel` (overriding options recovered from the global `ModelFactory.PreviousAttributes`), then sets each parameter to the user value clamped to its limits or, if none, the designer default (N = 1, enthalpy -30000 J/mol, otherwise the model's initial value). `ApplyParameters` reapplies these generation values after an option reshapes the parameter table and before each native simulation, so fitted values never become inputs. Model changes now rebuild the experiment as well, so no attached solution leaks values between models. Both apps build the replacement experiment and factory locally and assign them together; a failure keeps the previous simulation. Enter/loss of focus on an unchanged setup field still regenerates the simulation.
- Avalonia (`ExperimentDesignerWindow.cs`): parameter rows are rebuilt from the current table, including after option changes such as the sequential site count. Before the fit starts, the setup and model panels and the fit button are disabled; the container disable leaves each control's own enabled state unchanged. Only the window's own solver unlocks it.
- macOS (`ExperimentDesignerViewController2.cs`, `ParameterValueAdjustmentView.cs`): parameter controls are recreated and bound to the replacement model; entered values are shown as field text (`ShowValueAsInput`), defaults stay placeholders. Values are recorded when entered, so they persist with automatic simulation disabled; clearing a field returns the parameter to its default. Before the fit starts, every control in the window is disabled and its previous enabled state restored when that window's solver finishes; the solver subscription is released when the window closes. The native fit still runs on the generation model in place.
- Remaining difference: native "Simulate noise" still rebuilds the experiment, whereas Avalonia only regenerates noise; with retention this no longer changes parameters. Designer factories no longer read or write the global `ModelFactory.PreviousAttributes` (ITC-082).
- Validation: 10 Core `ExperimentDesignerStateTests` cover setup reconstruction, defaults, model switch and return, option precedence over global stored options, option-added parameters, fitted values being replaced, forgotten values, clamping, failed reconstruction and independent windows. 19 Avalonia `ExperimentDesignerRetentionTests` cover all ten setup triggers (including model switch and return) in One-Set-Of-Sites, injection-count changes keeping every parameter in all five designer models, unchanged Enter commits resampling noise without changing parameters, entry through a parameter row, the sequential site-count option rebuilding rows, and fit locking, guarded handlers, unrelated-solver isolation and restored enabled states; all 24 designer tests pass. Native `ExperimentDesignerTests` (AppKit, code-created outlets, run via `run-analysis-null-graph-tests.sh`) pass for retention with automatic simulation on and off, control rebinding, field-text display, model switching, fit locking/restoration and fitted values not being reused. Full Core suite: 2,357 passed, one skipped; full Avalonia suite: 340 passed. The earlier note that the Avalonia test project failed to compile in `FtxtcDuplicatePromptTests.cs` is obsolete; it compiles. The native designer was not exercised in the running app.

## ITC-079 - Avalonia tool windows write micro units as "u"

- Priority: Low
- Status: Resolved (2026-10-08).
- Problem: Experiment Designer, Tandem Merger, Experiment Details, and the tandem import dialogs wrote "uM"/"uL", while concentrations formatted through `AsFormattedConcentration` in the same windows show "µM"/"mM", so screenshots of adjacent tools showed two unit spellings.
- Resolution: Labels and generated summaries in Avalonia `ExperimentDesignerWindow`, `TandemMergerWindow`, `ExperimentDetailsWindow`, and `AvaloniaTandemImportPromptService`, and in macOS `MacTandemImportPromptService`, use µM/µL (micro sign U+00B5). Deliberately unchanged: `TandemMixingScan.cs` scan log volume text, `IntegratedHeatReader.cs` log lines, `ExportType.cs` export format descriptions, `Exporter.cs` `#EXPINFO … uM` headers read by external tools, and the `dead_volume_uL` key. Native macOS rendering was not inspected.

## ITC-080 - Result dates and experiment dates use different formats

- Priority: High
- Status: Resolved (2026-10-13). User accepted the implementation after review.
- Location: `AnalysisITC.Avalonia/Misc/Tools/AnalysisResultExporterWindow.cs:128` (`result.Date:g`); `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs:670, 682, 693` (`ToString("g")` / `ToString("d")`); compare `AnalysisITC.Core/DataClasses/ITCDataContainer.cs`, `GetShortDateString` (used via `UIShortDateWithTime` in the Tandem Merger and Buffer Subtraction windows).
- Problem: Experiment dates are formatted with the culture from `AppSettings.Locale`, while analysis-result dates use the process culture's general format. On the screenshot machine this gave "08.04.2025 08.24" in the Merger and Buffer Subtraction windows but "9/8/2026 11:10 AM" in the Analysis Result Exporter.
- Implementation (2026-10-08): Added shared `LocalDateTimeFormatter`, extending the existing `ITCDataContainer.GetShortDateString` approach of using `AppSettings.Locale` independently of the numeric process culture. Both desktop apps now use it for result exporters, active report-picker descriptions, recovery notices, and cached account timestamps; Core Preferences account expiry/reset dates and Avalonia report-window account dates share its short-date format. Experiment date properties preserve local separators; long-date names follow the regional locale. Buffer Subtraction details use short date/time in both apps; the native reference selector uses local short time. Invalid/empty locale falls back to `CurrentUICulture`, then invariant culture; the Avalonia date-field parser uses the same resolver.
- Export boundaries: Existing report/PDF timestamp formats, figure ISO dates, and serialization remain unchanged. Native validity experiment-list dates are UI-only. The unused native `ResultTitle` and Avalonia report-window `ResultCell` helpers, which still used the process culture, were removed.
- Known macOS limitation: `AppSettings.Locale` comes from `NSLocale.CurrentLocale.CollatorIdentifier`, which follows the language list rather than the Region setting and ignores custom date formats from System Settings. Locale acquisition is unchanged in this task.
- Validation: Culture regressions cover da-DK, en-GB, en-US, and sv-SE with whitespace-normalized/component expectations, invalid/empty-locale fallback, regional long-date names, account dates, unchanged report timestamps, and editable-date round trips. All 49 focused Core regressions, 80 relevant Avalonia tests (but `PreferencesTests`, which includes the account-date check, is unreliable as a class because of ITC-007; `ExperimentDetailsWindowTests` and `AnalysisReportRenderingTests` pass consistently), and native managed compilation/report-reference checks (including PDF rendering) pass. The full Core run has one failure in `ExperimentDesignerStateTests.OptionsThatAddParametersKeepUserValuesAndDefaultTheNewOnes` (unexpected third-site parameters at its initial factory assertion); that test passes in isolation, indicating a suite-state dependency outside this date change. Full Core run: 2,356 passed, one failed, one skipped. After ITC-082 removed the designer's dependence on the global option list, the full Core run passes (2,360 passed, one skipped); the earlier failure did not reproduce with or without that fix, so the cause is unconfirmed.
- Implementation review (2026-10-10): No new date-formatting defect found in the shared formatter or its desktop consumers. The full Core run passed 2,525 tests with one skip and the already documented competitor Save Selected failure; all five isolated Avalonia editable-date round-trip cases passed. Native managed compilation and report-reference/PDF checks passed. The locale-acquisition limitation above remains.
- Final review and acceptance (2026-10-13): No actionable defects found. Full Core suite: 3,396 passed, one skipped; all 74 relevant Avalonia tests passed. Native managed compilation and report/PDF checks passed. User confirmed resolution; the documented macOS locale-acquisition limitation remains unchanged.

## ITC-081 - Buffer Subtraction graph uses non-round axis tick values

- Priority: Low
- Status: Implemented (2026-10-08); visual review complete (2026-10-10), pending user acceptance. Seen in the JORS supporting-figure screenshots.
- Location: `AnalysisITC.Avalonia/Misc/Tools/BufferSubtractionGraphControl.cs`, tick loop at line 144 and `Format` at line 333; padded range from `BuildDataRange`.
- Problem: Axis labels are placed at the quarter points of the padded data range and printed with `G3`, so they fall on arbitrary values (e.g. x: -1.2, 13.7, 28.5, 43.4, 58.2; y: 12.9, -18.1, -49.1, -80.1, -111). The x axis is injection number, so fractional and negative ticks have no meaning there.
- Implementation: Avalonia uses the shared `PublicationAxis` tick-spacing routine instead of quarter points of the padded range. Injection ticks use positive whole numbers, with a minimum step of one and fractional 2.5-injection spacing increased to five. Heat ticks retain fractional steps where appropriate. Only ticks inside the existing plot range are drawn; data, units, fit, and range padding are unchanged. Native macOS already uses `NiceScale` for tick placement; no native code was changed or visually inspected.
- Validation: Seven focused `BufferSubtractionGraphControlTests` pass, covering the screenshot ranges, one-to-three-injection ranges, fractional injection spacing, and small/positive heat ranges. The full Avalonia suite encountered the existing ITC-007 UI-thread ownership exception cascade; a clean full-suite result is unavailable. `git diff --check` passes.
- Implementation review (2026-10-10): All seven tick tests pass again. Actual Avalonia control renders at 1000 × 600 were inspected for 54- and three-injection examples: the long case shows injection ticks 20/40 and heat ticks −100/−75/−50/−25/0, and the short case shows injection ticks 1/2/3. Labels are readable and inside the control, with no fractional or negative injection ticks. No defect found in this change; native buffer-graph rendering was not inspected. Status remains pending user acceptance.

## ITC-082 - Model options leak between the experiment designer and analysis

- Priority: Low
- Status: Resolved (2026-10-08).
- Location: `AnalysisITC.Core/Analysis2/ModelFactory.cs`, `SingleModelFactory.SetModelOption` (writes `ModelFactory.StorePreviousAttribute`) and `SingleModelFactory.InitializeModel` (reads `PreviousAttributes`).
- Problem: `ModelFactory.PreviousAttributes` is a single static list read by every `SingleModelFactory.InitializeModel`. Avalonia designer option edits and its fit factory wrote to it, so a new Avalonia designer window started with options edited in an earlier window (native did not, because its option views edit in place). Designer edits also became the starting options of Core reconstruction paths that read the list (bootstrap restoration, result-update members, legacy `.ftitc` reading); these apply their saved options afterwards. Correction to the original report: the analysis workspace in both apps keeps options per session (`AnalysisWorkspace`/`AnalysisBuilder`) and neither reads nor writes the list, so analysis options never reached the designer.
- Resolution: Designer-only. `SingleModelFactory` takes `useStoredOptions` (default true); when false, `InitializeModel` skips recovery from the list and `SetModelOption` does not store. `ExperimentDesignerState.CreateFactory` (both apps) and the Avalonia `CreateFitFactory` pass false, so each designer window starts from model default options plus its own edits. Other factories keep the shared memory. Designer and analysis remain fully separate; no designer-to-designer option memory was added.
- Validation: Core `ExperimentDesignerStateTests` cover designer factories ignoring stored options (including a stored sequential site count), designer option edits not being stored, and other factories still recovering stored options; the first two fail with the fix reverted. Avalonia `OptionChangesDoNotCarryIntoOtherWindows` covers a site-count edit and fit factory leaving the list empty and a new window starting at two sites; it fails if the fit factory stores options. The designer test class stays in the serial collection because it still changes the static list and `AppSettings.ParameterLimitSetting`; its comment now says so. Full Core suite: 2,360 passed, one skipped; Avalonia designer tests: 25 passed. The full Avalonia run reported failures in unrelated classes and stalled; `PreferencesTests` fails 19 of 24 in isolation with UI-thread ownership errors, and `BufferSubtractionGraphControlTests` passes in isolation. Native macOS was not compiled or run; its designer uses the changed Core factory path unchanged.

## ITC-083 - Allow disabling assessments and expose no-binding uncertainty settings

- Priority: Low
- Status: Open; idea (2026-10-09), extended 2026-10-13. Not implementation approval.
- Location: `AppSettings.SkipUncertaintyEstimationForNoBinding`; `SolverInterface.SkipNoBindingUncertainty`; Core assessment state, output policy, and shared presentation; Preferences and result views on both desktop platforms.
- Problem: Assessments currently participate in result interpretation and output without a user-facing option to leave them out of an analysis. Separately, ITC-052 skips uncertainty estimation for fits assessed as no binding by default. That setting exists and is persisted, but is not shown in Preferences on either platform.
- Proposed controls: Add a master option, provisionally "Run binding assessments", above the potential "Skip uncertainty estimation for no-binding results" option. Turning assessments off makes the subordinate option inactive; it cannot cause uncertainty to be skipped without an assessment. Keep macOS and Avalonia aligned, with shared Core tooltip text. Whether to expose the subordinate option remains an open choice.
- Deliberately not assessed: Each associated result must record explicitly that assessment was deliberately not run and is not part of that result. Use a distinct status/flag (exact representation to decide), rather than inferring intent from a missing comparison or overloading the existing `BindingAssessmentOutcome.NotAssessed`, which also covers unavailable or inconsistent comparisons. This state is neither No binding detected nor Inconclusive, and is not itself a warning or failure. Carry it at the appropriate single, pooled, or independent-member scope.
- Execution and downstream meaning: Do not run the Null comparison or assessment rules solely for assessment when disabled, including the proposed ITC-092 parameter screening. Fitting, the selected uncertainty method, and ordinary downstream analyses continue without assessment-based gating, parameter suppression, warning suppression, or replacement of the fitted curve with a Null curve. A previous automatic outcome or manual override must not silently govern a deliberately unassessed result. Independent input-validity and fit-health checks still apply; disabling assessment does not declare a fit healthy or valid.
- Presentation: Omit assessment sections, badges, controls, conclusions, and assessment-only comparison content from result UIs wherever possible, rather than filling them with "Not assessed" placeholders. Reports, including Standard, Diagnostic, and Traceability Mode, should omit assessment sections and contain at most one neutral line per associated result: "This analysis result was not assessed." Do not repeat it in member rows or nested sections. Exports, interpretation evidence, and the web viewer must respect the same absence of assessment and must not infer a classification.
- Saved-result behavior: Existing results keep their saved assessment state until explicitly updated. Preserve that state through save/reopen, copying, and presentation independently of the current preference. Loading or displaying a deliberately unassessed result must not automatically assess it or recreate assessment content. Changing the preference alone must not relabel existing results. Handling of previously saved manual overrides during an explicit update remains to be decided before implementation.
- Update Result assessment decision (decided, 2026-10-13): Update Result always uses the current `AppSettings` assessment preference when run, regardless of the preference used for the original result. An explicit update therefore enables or disables assessment according to the current preference; existing results retain their saved assessment state until that update.
- Implementation follow-up: Review `Documentation/ANALYSIS_REPORT_MAP.md`, especially A01, A04, A06, E05/E08, R03, and P03, for assessment sections and downstream suppression; update the map when report behavior is implemented. Read `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md` before choosing persistence changes, retain the existing meaning of files without the new flag, and prevent reader-side reevaluation from undoing a deliberate opt-out. Document the user-facing choice in the manual and shared help when implemented.
- Required validation: Distinguish deliberate disabling from unavailable/failed assessment; verify normal uncertainty and output with assessments off even when no-binding uncertainty skipping is enabled; verify Update Result uses the current assessment preference in both enabled-to-disabled and disabled-to-enabled transitions while preference changes and save/reopen alone preserve the saved state; cover single, pooled, and independent results, manual-override transitions, both desktop UIs, and the report line limit without empty or repeated assessment sections.
- Open decisions: Final control wording and default, whether to expose the subordinate uncertainty option, the saved-state representation, and handling of previously saved manual overrides during an explicit update. The assessment preference used by Update Result is settled above. Related ITC-052/092 behavior is a dependency to account for, not authorization to implement those adjacent ideas.

## ITC-084 - Clear Processing/Results only deletes saved result rows

- Status: Resolved (2026-10-10).
- Problem: Both desktop commands named Clear Processing/Results only deleted saved Analysis Result rows and returned without action when none existed. Baselines, integrated heats, and attached fits remained. The native macOS menu stayed enabled when its handler could not act.
- Resolution: The existing operation is now **Delete All Results**, with explicit result-only naming and native menu validation; its ordering, selection, dirty-state notifications, and Undo are retained. Both apps add **Clear Processing > Selected / Active / All**, backed by a prepared Core reset. Thermogram experiments receive an unlocked empty default spline and default integration settings, lose calculated traces/heats and attached models/solutions, and remain unprocessed when tabs or projects reopen. Heat-only imports lose attached models/solutions while retaining their primary heats. Inputs, identifiers, injection choices, tandem structure, and all attributes—including buffer-subtraction setup/subscriptions—are preserved. Saved results and snapshots remain; existing validity comparisons detect changed inputs.
- Safety and scope: Targets and replacement state are prepared before publication and checked against document identity/revision and a streamed state fingerprint after confirmation. Pending/running processing and fitting disable reset; Core tracks admission through final publication, including silent solves and result-update attachment. Multi-experiment reset always requires confirmation, including when ordinary deletion confirmations are disabled. Empty/already-cleared scopes do nothing. Single-experiment reset is immediate. Processing-reset Undo is deferred to ITC-088; existing result-deletion Undo is unchanged.
- Validation: All 35 focused Core checks pass, including scopes, defaults/locks, unavailable and zero heats, preserved inputs/attributes, retained historical results, strict save/reopen, buffer notifications, stale preparation, queued processing, fitting publication, update attachment, and failed-operation cleanup. Avalonia's combined menu/reset/confirmation check passes; native managed code compiles and its menu validation, Objective-C action dispatch, single reset, inspector/checkbox rebinding, tab reopening, batch cancellation, and adjacent slider regression checks pass. Full Core suite: 2,525 passed, one skipped, one failed in the previously documented `CompetitorSourceSaveSelectedTests.ResultSaveIncludesCompetitorSourceAndItsMembers` (its `Single` assertion finds multiple matching experiments; also recorded in ITC-015). Manual and shared help are updated. No format/schema changes, packaging, signing, or release steps were performed.

## ITC-085 - Processing state flickers after editing the integration slider on macOS

- Priority: Low
- Status: Resolved (2026-10-10); user confirmed the revised fix works in the macOS application.
- Location: `AnalysisITC.MacOS/CustomViews/IntegrationRangeSlider.cs`; `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs`, the integration slider actions; slider classes in `Main.storyboard`; related graph drag handling in `DataProcessingGraphView.cs`.
- Report: Processing state repeatedly flickered after an integration edit with a polynomial baseline on Med25 D2a Y242A data. The user subsequently reproduced it and identified the integration-length UI slider as the trigger.
- Investigation: Slider actions inferred tracking state from `NSApplication.CurrentEvent`. Callbacks classified as completed edits each call `ProcessData`; Core serializes those requests and runs every queued refit, repeatedly clearing and restoring processing state after release. Adding periodic events to the classification passed the earlier constructed-event tests, but the user still reproduced the problem. Those tests forced the current event to match every callback and did not exercise native slider tracking or controller subscriptions. The actual current-event sequence in the user's run has not been captured.
- Reproduction: An expanded native-action test holding the first worker until release reproduced a backlog of 20 polynomial refits/progress cycles when callbacks retained a mouse-up current event. The final regression uses a controlled native `NSSliderCell.TrackMouse` implementation, entered through the slider's actual `MouseDown`, to send repeated actions while tracking remains active, including release actions and stale mouse-up current events.
- Revised fix: Both storyboard sliders use `IntegrationRangeSlider`, which marks the complete native `MouseDown` tracking call explicitly. Actions preview integration changes without requesting baseline refits or publishing processing completion. After tracking returns, the controller processes a changed range once. Repeated actions at an unchanged value and tracking without a change do not queue work; keyboard edits still process immediately. UI refreshes preserve the tracked slider value, and the completion is scoped to the experiment that owned the drag. The earlier graph fixes remain: silent preview integration, consuming the selected feature on release, and clearing it on graph initialization. Polynomial fitting and integration equations are unchanged.
- Validation: Native `ProcessingSliderTests` exercises the controller's real `ViewDidLoad` subscriptions and native slider-cell tracking. Start/end sliders, included/excluded integration regions, repeated release actions, stale current events, unchanged tracking, keyboard edits, and all three local Y242A files with a degree-12 polynomial are covered. Changed drags have zero preview processing notifications/refits and one release refit/progress cycle; unchanged tracking has none. `ProcessingGraphDragTests` retains marker-drag coverage. Local investigation fixtures are optional; synthetic cases always run. Native managed compilation and standalone storyboard compilation pass. Avalonia already uses explicit slider drag events; its earlier nine `ProcessingGraphControlTests` passed. The user subsequently confirmed the revised fix works in the rebuilt macOS application (2026-10-10).

## ITC-086 - "Same for all" sharing ignores differing experimental conditions

- Priority: Low
- Status: Open; idea (2026-10-10). Found while reviewing the linear/exponential dilution-heat plan; outside that plan's scope.
- Location: global "Same for all" constraints (`AnalysisITC.Core/Analysis2/ParameterSet.cs` global table handling, `GlobalConstraintSemantics.cs`) and the global constraint controls on both desktops.
- Problem: A parameter can be shared across all members of a global fit without checking whether the members differ in conditions that may affect it. For example, the current Offset (heat per mole of injectant) may depend on syringe concentration, buffer composition and temperature, so a shared Offset across differing conditions may be inappropriate. The same question applies to other currently supported shared parameters whose values depend on sample preparation or conditions.
- Follow-up: Decide which parameters depend on which member conditions, and whether to warn, block, or only document when shared members differ.
- Comment: Not allowing users to use the constraints they want is probably not a great idea.

## ITC-088 - Undo for Clear Processing

- Priority: Low
- Status: Deferred by user (2026-10-10).
- Location: Core `ProcessingClearPlan`, shared undo/history policy, and both desktop Clear Processing actions; follow-up to ITC-084.
- Problem: Clear Processing restores processing defaults and removes calculated output and attached models/solutions without adding an Undo operation. Multi-experiment reset requires confirmation; single-experiment reset is immediate.
- Follow-up: Define one-operation restoration of processing choices, baseline/corrected traces, injection heats and integration state, locks, and attached fits, while preserving object relationships, subscriptions, saved-result validity, and subsequent edits. Decide how reset Undo interacts with the existing deletion-only history before implementation. Reset preparation is already separate from application; no undo snapshots or history storage were added in ITC-084.

## ITC-089 - Prominent experiment conditions in reports

- Status: Resolved (2026-10-13).
- Location: Per-experiment report sections; `Documentation/ANALYSIS_REPORT_MAP.md`.
- Resolution: Every experiment content heading, including condensed repeats and supporting experiments, has a conditions subtitle in 11 pt light text and normal report ink. Nominal initial concentrations form one syringe ▸ cell field (`100 µM ▸ 10 µM`), followed by spaced pipes between target temperature in the report's °C/K setting, all recorded buffers with pH and salts, recorded competitor concentration, and explicit ionic strength. Unavailable items are omitted, valid zero values retained, and ionic strength is not calculated for the subtitle. Detailed rows and their uncertainties remain.
- Layout and platforms: Core builds the text and measures the heading/subtitle together, with a 3 pt gap and wrapping at the existing font size. Both desktop renderers consume the shared geometry; Avalonia uses bundled Inter Light when its report family lacks a light face. Nested result chapters retain the subtitle; continuation running headers and saved project formats are unchanged. The report map, manual, and shared help describe the new content and the meaning of the temperature/concentrations.
- Validation: Core report tests: 248 passed; full Core suite: 3,433 passed, one existing skip. Avalonia report/font tests: 80 passed. Native managed report harness passed (Mono dependency search path supplied for its existing System.Buffers dependency). Twelve QA PDFs cover short/wrapped headings, supporting experiments, and Avalonia font selections; visual checks and extracted 11 pt font/glyph bounds confirm readable subtitles without clipping or overlap.
- Separate observation: The extreme native long-name QA fixture exposes existing report-title overflow in H02 footer fields. Recorded in the report map; footer fitting was not changed by ITC-089.
- Follow-up (2026-10-13): Subtitle temperatures omit trailing zeros after the existing two-decimal rounding, e.g. 25 °C, 25.1 °C, or 298.15 K. Detailed temperature rows retain their formatting. Validation: 15 Core conditions tests, three Avalonia rendering cases, and the native report harness passed; the updated PDF was visually checked.
- Format follow-up (2026-10-13): The concentration field uses syringe first and cell second (`100 µM ▸ 10 µM`), with pipes between remaining conditions. Missing concentrations leave no orphan separator, and each value retains its Core-formatted unit. Validation: 22 Core conditions tests; full Core suite 3,446 passed with one existing skip; 80 Avalonia report/font tests; native managed report harness passed. Twelve updated QA PDFs confirm triangle glyph coverage, 11 pt light text, wrapping, and subtitle bounds on both renderers.

## ITC-090 - Uncertainty replacement validation and cancellation-state preservation

- Priority: High
- Status: Resolved (2026-10-12).
- Location: `AnalysisITC.Core/Analysis2/AnalysisResultUpdater.cs`, candidate preparation and replacement validation shared by both desktops.
- Completed under ITC-094: Entirely skipped profile summaries retain `SkippedNoBinding`, zero expected endpoints, and no missing-run warning. Recorded profile cancellation/failure remains visible with absent runs; cancellation takes precedence in uncertainty aggregation. Both desktops now share method sections, skip explanations, requested-versus-retained bootstrap presentation, completion messages and transient retention feedback. The earlier short “Not run” wording proposal was superseded by the approved “Not estimated (no binding detected)” wording.
- Replacement validation: Root and member evidence are checked before publication, including recorded cancellation and retained refits. Independent bootstrap/leave-one-out updates require usable eligible-member evidence; stale combined refits or a misleading aggregate cannot substitute for it. Usable partial estimates remain accepted, with missing eligible-member evidence recorded as partial failure and a diagnostic. Profile skips are checked against member evidence; method-None and zero-coordinate-profile behavior are retained.
- Cancellation-state preservation: Preparation and estimation use detached copies of the observed integrated heats and experiment attributes. Failed preparation, rejected updates and cancellation preserve the stored solution, assessments, Null comparison, date, validity snapshot, live model/solution attachments, and document dirty state/revision. Accepted primary models are rebound only after validation; uncertainty refits keep their own data. Refreshed competitor captures are published with the accepted update. A changed document revision prevents publication of an outdated candidate.
- Validation: Reproduced the 15 remaining lifecycle failures before editing (289 passed, 15 failed). Final focused Core run: 328 passed, including all original regressions, leave-one-out cancellation, successful primary attachment, detached buffer-corrected heats and competitor captures, misleading profile cancellation, and document changes during an update. Full Core suite: 3,370 passed, one existing skip, no failures; the two final added regression cases also pass in the focused run. Avalonia fitting/results/update checks: 59 passed. Native macOS managed compilation and uncertainty Results/details checks passed. Evidence: `/private/tmp/itc090-baseline.log`, `/private/tmp/itc090-focused-final.log`, `/private/tmp/itc090-full-core.log`, `/private/tmp/itc090-avalonia.log`, `/private/tmp/itc090-native.log`, and matching Core/Avalonia TRX files.
- Commit verification (2026-10-13): Reviewed issue 090 with its no-binding and ITC-094 prerequisites. Current workspace: 3,439 Core tests passed, one existing skip; 59 relevant Avalonia tests and the native uncertainty UI harness passed. Isolated proposed commit: all 343 focused Core tests and the native uncertainty harness passed. The broader isolated suite had 3,038 passes, one skip and 80 failures; all 80 failure names and messages reproduce against the unchanged base commit. Its Avalonia checks had 48 passes and three unrelated Null-model label failures, also reproduced on the base. Temporary validation copies excluded baseline test files that reference other uncommitted APIs and omitted two deleted native source entries; those adjustments are not part of this commit. Evidence: `/private/tmp/itc090-verify-full-core.log`, `/private/tmp/itc090-verify-avalonia.log`, `/private/tmp/itc090-verify-native.log`, `/private/tmp/itc090-isolated-focused.log`, `/private/tmp/itc090-isolated-core.log`, `/private/tmp/itc090-baseline-comparison.log`, `/private/tmp/itc090-isolated-avalonia.log`, `/private/tmp/itc090-baseline-avalonia.log`, and `/private/tmp/itc090-isolated-native.log`.
- Boundaries: No assessment-threshold, equation, uncertainty-method, combined-uncertainty policy, persistence format or report-layout change. Optional requested-count persistence and inspector presentation belong to completed ITC-094.

## ITC-091 - Control for including supporting experiments in printed reports

- Priority: Low
- Status: Open; idea (2026-10-12).
- Location: Both report windows (`AnalysisReportViewController`, `AnalysisReportWindow`); report presentation settings; `AnalysisReportBuilder` Supporting experiments chapter.
- Context: ITC-032 planning expects supporting experiments (buffer references and tandem sources) to be added automatically when results are selected, rather than chosen by the user.
- Idea: A report setting that excludes supporting experiments from the printed report without changing the stored supporting selection.
- Follow-up: Decide whether the setting also applies to AI interpretation requests (adjacent to ITC-034), whether it is saved with the report (optional `.ftxtc` presentation field), and add shared tooltip text in Core, manual text, and a report-map update.

## ITC-092 - Parameter plausibility in automatic binding assessment

- Priority: Medium
- Status: Open; planning proposal (2026-10-12).
- Problem: The automatic binding assessment uses only the saved Null comparison. It does not evaluate whether fitted Kd, enthalpy, or N support a plausible parameter interpretation. Motivating examples are Kd = 30 mM, ΔH = −50,000 kJ/mol, and N = 0.001; some are outside current Standard solver bounds. The proposed limits are chosen screening limits, not fundamental physical maxima.
- Plan: [Automatic assessment of fitted binding parameters](BINDING_PARAMETER_ASSESSMENT_PLAN.md). Proposes shared Core range checks of the original best fit, including fixed parameters, combined with the existing Null comparison and aligned desktop/output presentation. Out-of-range values give No binding detected; no new reason, warning, or persistence field is added. The reader's existing reevaluation path also uses the parameter checks.
- Open decisions: Confirm the proposed ΔH and N/active-fraction ranges and separately decide whether to raise the Standard N solver upper limit from 10 to 25. The Kd range and Competitive/Dissociation treatment follow the plan. An override after uncertainty was skipped requires Update Result to estimate it. Parameter screening remains unreleased planning work; adjacent uncertainty/update issues remain separate.

## ITC-093 - Tightening parameter limits blocks refitting an existing extreme fit

- Priority: Medium
- Status: Implemented (2026-10-12).
- Location: Core `Analysis2/Models/Models.cs` (`PreviousOrDefault`), `AnalysisBuilder.cs` (parameter overrides), `AnalysisWorkspace.cs` (`PersistFittedParameters`, `PrepareForSolve`), `AnalysisContext.cs` (`RefreshParameterLimits`, `DetectInitialParameterLimitViolations`), and `Solver2.cs` (`ValidateInitialParameterLimits`). Both desktop analysis inspectors consume this shared preflight policy.
- Problem: An experiment fitted under Expanded or No limits can retain a finite fitted value outside subsequently tightened bounds. `PreviousOrDefault` reuses attached solution values, and `PersistFittedParameters` stores fitted values as session overrides that are reapplied on rebuild. Refreshing limits changes only the bounds. The prior initial-value guard blocked Run Fit before either optimizer started, requiring manual edits or wider limits even though the user selected tighter limits to refit the experiment. That was the previously documented policy, rather than an optimizer exception or loss of the existing solution. Clamping only in `PreviousOrDefault` would miss session overrides and shared global coordinates.
- Reproduction: Temporary Core probe with ten integrated peaks whose independently specified constant Offset is +60000 or -60000 J/mol. Complete a fit under Expanded, attach its solution, persist workspace starting values, switch to Standard, and rebuild or refresh limits. Offset retains ±60000 while its bounds become [-50000, 50000] J/mol; `PrepareForSolve(false)` throws `InitialParameterLimitException`. Reproduced for both Nelder-Mead and Levenberg-Marquardt, each sign, and single, independent multiple-experiment, and shared-Offset global fits: 12 cases. The rejected attempt preserves the previous attached models and solutions. Manually clamping the candidate's free coordinates to ±50000 admits the fit and produces the independently expected constrained optimum in all 12 cases, without changing the old solution values.
- Proposed fix: Normalize finite free starting guesses in shared Core with `start = Math.Min(upper, Math.Max(lower, start))` after applying overrides, refreshing active bounds, and constructing global constraints, before the relevant preflight/solver guards. Cover local and shared optimizer coordinates; recompute dependent global values from their owning coordinates. Use the actual optimizer coordinates and bounds, including log₁₀K, rather than treating affinity bounds as linear K. Leave in-range values, locked parameters, historical solution estimates, and uncertainty samples unchanged. Retain rejection of invalid/non-finite inputs. Align inspector feedback with the normalized starts so both apps permit the refit.
- Boundary-start caveat: Admission alone does not prove a useful refit. A separate constant-Offset control has an independently known interior optimum of 2000 J/mol. With optimizer tolerance set to 0, starting exactly at the Standard upper bound gives 2500 with Nelder-Mead and leaves Levenberg-Marquardt at 50000 despite successful termination. At the strictest tolerance (1), exact-bound LM starts still stop far from 2000 in both weighted and unweighted fits; starting 1 J/mol inside the bound recovers 2000 within 0.01 J/mol with both algorithms. Include an interior-optimum regression and, if needed, a small scale-aware inward optimizer start when clamping hits a bound; preserve the selected bounds and final boundary diagnostics.
- Required validation: Independently expected lower/upper clamping, unchanged in-range/equal-bound and locked values, tightening among Standard/Expanded/No limits, logarithmic affinity coordinates, local/shared/independent fits, both algorithms, and both boundary and interior optima. Preserve existing solution/state retention on failed preparation. Update existing tests that deliberately expect inherited out-of-range starts to be blocked (`ParameterBoundaryContactTests`, `AnalysisWorkspaceSolutionRetentionTests`, Avalonia `RunFitPreflightBlocksReusedOutOfRangeStartWithoutEnteringFittingState`) and the current blocking explanation in `Documentation/UserManual/pages/06-fitting-models.md`; inspect other direct-solver/result-update consumers of the same guard before implementation.
- Resolution: Shared Core clamps finite free local and global starting coordinates after bounds/overrides are applied, while leaving exact in-range values, locks, historical estimates, and retained uncertainty evidence unchanged. Both optimizers keep the exact normalized vector as the baseline and use a representable inward optimizer-only start at exact bounds; when that adjustment is used, the original-data objective must strictly improve or the exact baseline is retained. Both desktop inspectors keep accepted guesses unchanged during preview, retain manual validation for newly typed free values, and show the shared Core advisory. The macOS draft also preserves unaccepted invalid text across passive rebuilds. Core normalizes accepted restored guesses at Run Fit; help text and the manual describe this behavior.
- Validation: Full Core command `dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --no-restore --logger 'trx;LogFileName=itc-093-final.trx' --results-directory /private/tmp/itc-093-final-validation-20261012-final` completed with 2,998 passed, 1 skipped, and 22 failures (3,021 total). All 22 failures are in the pre-existing ITC-090 `NoBindingUncertaintySkipTests`: `CancellationAfterRetainedRefitsPreservesTheStoredResult` (5 cases), `MisleadingAggregateCannotAdmitMissingOrCancelledMemberEvidence` (6), `EntirelySkippedProfileRetainsItsReasonInTheSharedSummary` (3), `ProfileMishapWithoutARunMustNotBecomeOrdinaryNotRun` (4), `UsablePartialEstimateKeepsMissingEligibleEvidenceVisible` (2), and `CancelledProfilePreservesLiveAttachmentsAndSavedEvidence` (2). No ITC-093 test failed. Full log: `/private/tmp/itc-093-final-validation-20261012-final/core.log`; TRX: `/private/tmp/itc-093-final-validation-20261012-final/itc-093-final.trx`. Focused Core boundary/refit/history/non-finite cases passed 149/149 (`focused-core.log`/`focused-core.trx`). After correcting the second shared-model fixture to include positive SDs in weighted cases and requiring finite analytic objectives, the complete weighted/unweighted local/shared interior and constrained matrices passed 144/144 (`analytic-core-final.log`/`analytic-core-final.trx`). Avalonia `AnalysisWorkspaceControlTests` passed 31/31 (`avalonia-focused.log`/`avalonia-focused.trx`); the native managed runner command `bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh AnalysisParameterDraftTests` passed both draft and real Run Fit launch checks (`native-managed.log`). Parent's independent 36-case completed-fit preset/scope/algorithm/sign probe also passed (`/private/tmp/itc-093-parent-refit-review.9f5clmt7/final-review.log`). These tests verify fitting robustness and analytic parameter recovery, not external forward-model validation.

## ITC-094 - Method-specific uncertainty presentation

- Priority: Low
- Status: Resolved (2026-10-12); approved implementation completed on macOS and Avalonia.
- Resolution: Core defines shared Bootstrap, Leave-one-out and Profile likelihood sections for Results and details. Solver shows only the uncertainty method; None has no empty section. Intentional no-binding skips retain their explanation and omit counts/endpoints/time. Mixed independent results explain member evidence and unavailable combined uncertainty; cancellation and missing/failed evidence remain visible.
- Bootstrap evidence: Bootstraps shows the saved requested count, with retained complete replicates in its tooltip when different. Historical requests remain Unknown. Primary bootstrap roots and members save the common per-fit request; other methods and uncertainty refits omit it. Reports retain their complete-replicate counts. Saved disabled/enabled settings are shown, including legacy automatic concentration variance. Only `HasLegacyCombinedLeaveOneOut` adds the two historical concentration/unlocking rows for leave-one-out.
- Update Result: Both dialogs always offer 100, 200, 500, 1,000, 2,000, 5,000 and 10,000 plus Stored setting. Equal and smaller requests are accepted; saved requests take precedence over the existing historical fallback. Retained profile updates return a separate convergence copy with transient feedback, never altering the stored convergence or serializing the reason.
- Shared fixes: Profile summaries preserve all-skipped status and recorded failures/cancellation with absent runs. Cancellation has aggregation precedence. These status fixes complete the presentation portion of ITC-090; replacement validation and detached-copy/dirty-state preservation remain there.
- Documentation: Optional `.ftxtc` convergence metadata/schema, result help, and manual explain requested versus retained counts and rerun choices. Required report-map before/after review recorded A10/E08 outcome changes and unchanged no-binding explanations, validity warnings/badges, report layout, and complete-replicate counts. There is no dedicated profile-status report section.
- Validation: Focused Core checks: 321 passed and the 15 known ITC-090 lifecycle failures. New ITC-094 cases, published-schema checks, updater presets and profile workflows pass. Avalonia results/details/update/fitting checks: 59 passed. Native managed AppKit Results/details sections, mixed status, tooltips and narrow layout checks pass. Final full `AnalysisITC.Core.Tests`: 3,092 passed, one existing skip, and only the 15 remaining ITC-090 lifecycle failures; no ITC-094 failures. Real Skia and AppKit renders were inspected for readable method rows and skipped explanations. The separate compression suite passes 10/10 in isolation; argument-binding/compression failures seen in earlier workspace runs are absent from the final run. Evidence: `/private/tmp/itc094-focused-final.log`, `/private/tmp/itc094-full-core-current.log`, `/private/tmp/itc094-avalonia-final.log`, `/private/tmp/itc094-native.log`, `/private/tmp/itc094-render.log`, and matching TRX files in each test project's `TestResults` directory.
- Boundaries: No scientific equation, threshold, replacement acceptance policy, renderer, web redesign, packaging, version, commit or push change.

## ITC-096 — Dissociation concentration axes use molar-ratio coordinates in native graphs

- Status: Resolved (2026-10-12).
- Problem: Native `DataFittingGraph` selected a concentration title and µM scaling for Dissociation or a buffer-only cell but used `InjectionData.Ratio` for its range, observations, fits, confidence bands, residuals, and hit testing. Saved ratios that differ from the saved titrant concentrations give incorrect coordinates; non-finite ratios can erase the ticks and plotted data. Model changes also left graph units and residual titles stale, and Final Figure forced the default Molar Ratio title onto concentration plots.
- Resolution: Shared `AnalysisAxisPresentation` selects saved post-injection titrant concentrations, molar ratios, or injection numbers according to the experiment axis. Both desktops and publication figures use this mapping. Native graphs refresh when the axis type changes, synchronize residual titles and scaling, and unify horizontal ranges only across matching axis types. Final Figure and figure canvases preserve automatic axis titles unless explicitly overridden. Drawing does not rewrite saved concentrations or ratios.
- Validation: Seven native dissociation rendering/interaction cases pass, covering the raw fixture, unusable saved ratios, fit/residual alignment and tooltips, model changes, shared axes, figure limits/custom titles, and injection-number fallback. Native Null, final-figure, and designer fixtures pass; 45 affected Avalonia tests and seven new Core axis tests pass. The full Core suite has 2,829 passed, one skipped, and 22 failures, all in `NoBindingUncertaintySkipTests`; the same 22 failures reproduce in isolation and concern uncertainty lifecycle/presentation behavior outside this axis fix.

## ITC-097 - Normalize displayed guesses immediately when limits change

- Priority: Low
- Status: Open; follow-up idea (2026-10-12).
- Location: Analysis parameter inspectors on macOS and Avalonia.
- Idea: After the user changes active limits, update accepted displayed guesses to the nearest selected limit immediately. Fit-start normalization currently preserves the entered or restored guess during preview and adjusts only the candidate used for a fit.
- Follow-up: Decide whether immediate display normalization should also update the owning session override, and how to retain the original guess for undo or restoring wider limits.

## ITC-098 - Fitting status does not describe what is being fitted

- Priority: Medium
- Status: Open (2026-10-12).
- Location: Shared `SolverInterface.Analyze` start status and `GlobalSolver` individual-fit progress updates in `Solver2.cs`; Avalonia `AnalysisWorkspaceControl.OnSolverUpdated`/`DescribeFit`; macOS `DataAnalysisViewController` (`SolverUpdate.SendToStatusBar`, `OnErrorIteration`).
- Problem: Status text gives little indication of the fit scope or the current phase. Update Result always runs a `GlobalSolver`, so a single-experiment update was announced as "Fitting Global.<model>"; the Global prefix and Avalonia's "global" scope are now shown only for actual global fits. For individually fitted experiments, Avalonia derives "Fitting experiments k/n" from the overall progress fraction, so it still reads "Fitting experiments 0/n" during the first experiment's uncertainty estimation, and neither desktop says which experiment or phase (fitting, assessment, uncertainty estimation) is running.
- Recheck (2026-10-12): The Core start status adds the "Global." prefix only when `ShouldFitIndividually` is false, and Avalonia's `DescribeFitScope` distinguishes global, individual and single fits. Avalonia's "Fitting experiments k/n" is still derived from overall progress. `AnalysisResultUpdaterTests.IndividualUpdateProgressRisesOnceWithoutGlobalFitStatus` (both cases) failed in one full Core run and passed in isolation and in a later full run; in the failing run it captured only "Terminating analysis...", indicating interference through the static `StatusBarManager` events from another test.
- Proposed direction: Shared Core status wording for both desktops, e.g. "Fitting 3 experiments individually", "Fitting experiment 2 of 3", "Estimating uncertainty: experiment 1 of 3 (bootstrap 40/100)", with global fits named as such. Agree the wording before implementation.

## ITC-099 - Update Result keeps the existing profile result without telling the user

- Priority: Medium
- Status: Resolved (2026-10-15).
- Location: `AnalysisResultUpdater.UpdateAsync` profile-likelihood branch and `ProfileRetentionReason`; Update Result callers in Avalonia `AnalysisResultWorkspaceControl`, macOS `AnalysisResultTabViewController` and `MainWindowController.UpdateSelectedResult`.
- Problem: When a profile-likelihood update is not accepted, the existing result is retained and the call returns normally rather than failing. Retention reasons: the primary fit failed or was cancelled; no valid replacement solution; profiling cancelled, not run, or producing no complete interval; or a partial profile that does not improve on the existing result (a valid existing result is replaced by a partial profile only when the existing one is also partial with no more complete coordinate intervals; an invalid existing result by any partial profile with at least one complete interval). Before the working-tree change, the reason was appended to `ErrorEstimationSummary` after the finished event and never shown, so callers reported "RMSD = …" as though the update succeeded.
- Resolution: Missing completion information becomes a concrete failure before desktop progress listeners receive it. The updater rejects missing/failed primary outcomes with a descriptive error and preserves the existing result; failed primary fits use the error path, while cancelled or rejected profile updates retain their shared transient reason. Avalonia raises `ResultUpdated` only for an accepted replacement. The macOS Results tab preserves the evaluation temperature on retention, and the macOS menu keeps a failed-update message visible. The numerical replacement policy and saved scientific evidence are unchanged.
- Validation: New Core regressions reproduced missing-completion and failed-primary behavior before the fix; all 331 affected update/profile/uncertainty checks pass, followed by the full Core suite (3,497 passed, one existing skip). The full Avalonia suite passes (444 passed, one existing opt-in skip); all five final feedback checks pass, including the added preparation-failure case. Native AppKit checks exercise both Results-tab and menu updates for acceptance, cancellation, failed primary fits and failed preparation, preserve retained results/evaluation temperature, and verify visible status plus missing-completion progress cleanup. Existing native uncertainty presentation checks also pass. Preparation failures already clear pending status through the shared exception handler, so no extra fix was needed there. Logs/TRX: `/private/tmp/itc099-validation/`.

## ITC-100 - Fit-completion status messages contain unnecessary or unclear information

- Priority: Low
- Status: Open (2026-10-12). Found while reviewing ITC-053.
- Location: Fit completion in Avalonia `AnalysisWorkspaceControl.OnAnalysisFinished` and macOS `DataAnalysisViewController.OnAnalysisFinished`; Update Result callers in Avalonia `AnalysisResultWorkspaceControl`, macOS `AnalysisResultTabViewController` and `MainWindowController`.
- Problem: After a fit, both desktops queue up to five short messages (2–5 s each): "Analysis Completed Successfully", the parameter-bound warning, "<n> iterations | <time>", "<ALGORITHM> | RMSD = <value>", and an uncertainty line. Seen in real fits: the algorithm in capitals ("SIMPLEX") and RMSD in G4 format without a unit (e.g. "7,146E-06"). The iteration count is of doubtful use to users. The earlier Avalonia raw diagnostic uncertainty line ("BootstrapResiduals: succeeded=6, failed=0, total=6") and blank line for independent multi-experiment fits are no longer current: both desktops now use the shared `UncertaintyEstimationPresentation.Completion` (ITC-094); Avalonia omits the uncertainty message when no summary is available. The remaining issue is the number and wording of the queued completion messages.
- Proposed direction: Decide which items a user needs after a fit and define the wording once in Core for both desktops. Related: ITC-098 (status while fitting), ITC-094, ITC-090.

## ITC-101 - Clear Processing availability check throws for short injection delays

- Priority: Medium
- Status: Resolved (2026-10-13). Related to resolved ITC-084 and the remaining failures recorded under ITC-007.
- Location: `AnalysisITC.Core/ProcessingClearPlan.cs` (`Entry` constructor, end-offset clamp); `DataManager.CanClearProcessing`; Avalonia `AppMenuController` command refresh via `MainWindow.CanClearProcessing`.
- Problem: Preparing a reset plan clamps each injection's default end offset to `[2 × TimeStep, injection.Delay]`. When an injection's delay is shorter than two sampling intervals, the minimum exceeds the maximum and `FWEMath.Clamp` throws "Max must be greater than or equal to min". `CanClearProcessing` builds a plan for every experiment in scope, so the exception is raised from menu-state refreshes, not only when the user chooses Clear Processing.
- Reproduction: Full Avalonia suite (2026-10-12): 7 `AnalysisReportEnhancementTests` fail with this exception (6 when recorded under ITC-007). A MainWindow created by an earlier test remains subscribed to project changes; its menu refresh evaluates `CanClearProcessing` against the class's sparse fixture. The class passes in isolation. Whether real imported data can have injection delays below two time steps has not been checked; native macOS menu validation was not exercised.
- Resolution: The reset endpoint's lower bound is limited to the injection's own delay. Injections shorter than two sampling intervals therefore reset to their full available interval (zero for a zero delay), while ordinary defaults are unchanged. Cleared peaks remain unintegrated with unavailable areas. The existing availability/plan path is retained. Six independent Core cases cover zero, sub-sample, short, and exactly two-sample delays in the first or a later injection; they verify all three availability scopes, no mutation while checking/preparing, literal reset endpoints, and a second clear being a no-op.
- Test isolation: `ProcessingClearMenuTests` and `SelectionMenuTests` now mark their fixture document clean before closing the main window and assert it actually closes. Previously the save prompt cancelled teardown, retaining menu and advanced-analysis event subscribers. Pairing the clearing-menu test with the structuring rerun test reproduced a handled UI thread-access error; proper teardown removes it. The structuring test now also checks for handled errors instead of reporting only an absent result.
- Validation: All 23 Core `ProcessingClearTests` pass, followed by the full Core suite in an isolated output directory (3,433 passed, one skipped). The full Avalonia suite passes with 406 passed and one opt-in visual-QA test skipped, including all seven affected report tests and the structuring rerun test.

## ITC-102 - Report preview is not invalidated when a result is not in the project

- Priority: Low
- Status: Resolved (2026-10-13); stale test fixture after the ITC-031/ITC-032 project-ownership changes.
- Location: Avalonia `AnalysisReportWindow.MarkStale` and `CanWriteReport`; `AnalysisITC.Avalonia.Tests/ClassifiedOutputUiTests.cs`.
- Problem: `MarkStale` now returns before clearing the cached preview unless `CanWriteReport()` holds, which requires every selected result to be in `DataManager.Results`. `ClassifiedOutputUiTests.AssessmentChangeInvalidatesCachedReportWithoutReplacingOriginalFit` fails deterministically (also in isolation): its result is never added to the project, so an assessment change no longer marks the preview stale.
- Resolution: The test now adds its experiment and result to the project and opens `AnalysisReportWindow(result)` through the normal selection path. It still checks that an assessment change clears the cached preview while preserving the original fit/date, and that the rebuilt report identifies the manual override. The expected `(manual)` suffix matches the existing R02 rule in `Documentation/ANALYSIS_REPORT_MAP.md`. Both desktop report windows require project ownership and close when it is lost; those guards remain intact. No report content or manual change was needed.
- Validation: The preview test and the full Avalonia suite pass (406 passed, one opt-in visual-QA test skipped).

## ITC-103 - Avalonia reuses the fitted model for repeat fits

- Priority: High
- Status: Resolved (2026-10-13).
- Problem: A second fit from a ready Avalonia workspace reused models referenced by completed solutions and Analysis Results. A trial evaluation changed retained model parameters and predictions without changing the reported estimate.
- Resolution: Shared `AnalysisWorkspace.PrepareForSolve` copies the current context's configuration into new models before normalization and preflight. Values, locks, options, heat methods, limits, member order, global constraints and captured temperatures are preserved; solutions and prediction caches are not copied. Global relationships are rebuilt with the new members. Successful preparation replaces the context and attachments, advances the document revision, and uses the existing inspector-refresh event. Advancing the revision prevents a save already writing an earlier snapshot from marking the newly prepared fit clean. Failed preparation preserves the old context and attachments; session overrides are not rewritten.
- Verification: Regression checks cover single, independent and pooled repeat fits; sequential options, locks, limits, temperature-dependent and thermodynamically linked constraints; retained parameters, predictions and uncertainty after optimizer/uncertainty cancellation; and failed preflight. Both desktop Run Fit entry points retain the first result through another fit. The original one-site audit probe also confirms distinct models and unchanged retained predictions. See PA-01 and the evidence record in [the combined audit worklist](PUBLICATION_READINESS_ACTIONS.md).
- Boundary: The primary fit still appears before uncertainty finishes. Cancellation does not restore a previous attachment. Broader detached solving remains ITC-104.

## ITC-104 - Fits optimize the live, UI-reachable model instances

- Priority: Low
- Status: Deferred (2026-10-13), following the approved ITC-103/105 implementation scope.
- Location: `AnalysisWorkspace.PrepareForSolve`/`AnalysisContext` (attach prepared models to `ExperimentData.Model`); `Solver.Analyze`/`GlobalSolver.Analyze` background tasks in `Solver2.cs`; `SolutionInterface.FromModel`.
- Architectural option: Fit a private copy and publish the completed model and solution at the end. This changes primary-fit display, global relationships, cancellation and attachment behavior.
- Current boundary: ITC-103 gives each fit fresh models and protects completed results. ITC-105 excludes processing from project snapshot capture. No remaining concrete reader defect requiring detached solving was found in these checks. Revisit only with such a reproduction; no additional publication delay or rollback system is included.

## ITC-105 - Autosave can capture live fitted parameters during a fit

- Priority: High
- Status: Resolved (2026-10-13), together with ITC-103.
- Problem: The audit's actual autosave captured a trial model beside the previous reported estimate while processing was busy. Strict reopening accepted the inconsistent pair without issues.
- Resolution: Autosave defers busy ticks without touching dirty state, recovery metadata or the previous snapshot. Multiple missed ticks become one save as soon as processing is idle; an existing writer can finish first. A pending request is discarded when the document becomes clean, autosave is disabled, or the manager stops. Save, Save As and Save Selected reject busy requests with one shared status message; they are not queued. Save-before-close returns false and keeps the document open. The existing synchronous FTXTC capture now produces private payload bytes under the shared processing-admission gate, only while idle; asynchronous writing and atomic replacement occur after that gate is released. The save semaphore, document identity/revision checks, public save conventions and file schemas are preserved.
- Verification: Controlled tests cover processing starting before capture, exclusion while capture holds admission, and model mutation after capture during writing. Busy save entry points preserve files and dirty state; autosave also preserves recovery metadata. Saves and strict reopening succeed after completion and both cancellation stages. The original audit probe confirms no autosave writes while busy, the previous file is unchanged, and the deferred save resumes with consistent retained and current fits. See PA-02 and the evidence record in [the combined audit worklist](PUBLICATION_READINESS_ACTIONS.md).

## ITC-106 - Injection inclusion could change during fitting

- Status: Resolved (2026-10-13).
- Problem: The objective reads `Include` at each evaluation, but desktop analysis and buffer-subtraction graphs permitted inclusion edits during active fitting/processing. Regression tests reproduced the Core and Avalonia paths and the native analysis graph path before the fix.
- Resolution: Core `InjectionData.TryToggleDataPointActive` admits inclusion edits through the same gate used by processing admission. Busy edits do nothing and are not deferred; change notifications occur outside the gate and only after an accepted edit. The existing toggle entry point delegates to this policy. Avalonia analysis and buffer graphs use the guarded operation, and the analysis workspace marks its graph read-only while fitting. Native analysis/buffer graphs also discard presses received during processing, preventing a later mouse-up from applying them. Read-only graph hover remains available. Core and desktop regressions cover nested activity, unchanged dirty state/notifications, rejected fit/residual clicks, native press/release behavior, buffer graph events and resumed editing. The native fixture is included in the default managed test harness. This fixes inclusion editing, not the separate live-model and autosave issues ITC-103–105.

## ITC-107 - Consider preserving the planned injection count in project files

- Priority: Low
- Status: Open; idea (2026-10-14). Not implementation approval.
- Location: Source-file readers, experiment metadata, and `.ftxtc` persistence; `DataValidator` orphan-injection handling provides the context.
- Question: Should the saved project record the planned number of injections declared by the source file, independently of the retained injection count? Injections without thermogram data can be import artefacts and may legitimately be discarded; the question is whether the original plan should remain available as metadata.
- Boundary: Keep the current handling of injections without data. This is not a proposal to prohibit their removal or force a new Traceability Mode restriction.
- Open decisions: Which source formats provide an explicit planned count; how to represent unavailable counts without treating the retained count as the original plan; and whether this metadata should be displayed. Decide the optional `.ftxtc` representation after reading the format specification if implementation is approved.

## ITC-108 - Consider requiring report injection tables in Traceability Mode

- Priority: Low
- Status: Open; idea (2026-10-14). Not implementation approval.
- Location: `AnalysisReportOptions.IncludeInjectionTables`, both desktop report inspectors, and `Documentation/ANALYSIS_REPORT_MAP.md` E10/E11 and supporting-experiment tables.
- Question: Should Traceability Mode require injection tables in analysis reports, or should they remain optional when the report accompanies the saved project? The current tables retain fitting-excluded injections; this concerns omitting an entire optional report table, not removing excluded rows from numerical exports.
- Tradeoff: Required tables provide numerical evidence within the report but can substantially increase its length. No decision has been made to force their inclusion.
- Boundary: Leave axis scaling, single/global Analysis Result creation, orphan-injection handling, and uncertainty display choices unchanged. Any approved report change should keep macOS and Avalonia aligned and update the report map.

## ITC-109 - Status-bar progress indicator differs between platforms

- Priority: Low
- Status: Open; decision needed (2026-10-15). Split out of the ITC-098/ITC-100 plan ([ITC-098_FIT_PROGRESS_PLAN.md](ITC-098_FIT_PROGRESS_PLAN.md)) at the user's request.
- Location: macOS `mac-status-progress` in `Main.storyboard` (small spinning `NSProgressIndicator`) and `StatusBarViewController.ApplyProgressUpdate`; Avalonia `StatusProgressBar` and `StatusProgressText` in `MainWindow.axaml` and `MainWindow.axaml.cs` (`SetProgressState`).
- Problem: The status-bar indicator looks different on the two platforms. macOS shows a small round spinner, whose determinate form is a small pie that is hard to read. Avalonia shows a bar with a percentage label. Both switch between indeterminate and determinate from the same progress values. Once ITC-098 shows a counter such as "Bootstrap 40/100" in the secondary status, Avalonia's percentage label repeats that information.
- Options:
  - Bar on both platforms (reviewer recommendation). A bar shows a fraction legibly at status-bar size, Avalonia already has one, and it aligns the platforms. macOS would replace the spinner with a small bar.
  - Round on both platforms. This looks calmer for indeterminate work, but Avalonia needs a custom control and the fraction is hard to read.
  - Keep the current platform-specific styles.
- Proposed behaviour if a change is made: indeterminate while fitting and assessing binding, because there is no honest fraction; determinate during uncertainty estimation, using the existing overall uncertainty progress across all members, while the secondary counter shows the current experiment's refits; hidden after completion. Remove or replace Avalonia's percentage label when the counter is shown.
- Related: ITC-098 provides the phase and fraction; this issue decides how they are presented.
