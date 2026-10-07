# Issue tracker

## ITC-001 — Asymmetric rounding of negative display values

- Priority: High
- Status: Resolved (2026-10-02).
- Location: `AnalysisITC.Core/Math/FWEMath.cs`, both `RoundApproximate` overloads.
- Problem: −2.5 rounds to −2 while +2.5 rounds to +3. The digits overload also rounds −2.6 to −2. Standard/Strict uncertainty formatting uses the one-argument overload, so the midpoint inconsistency reaches displayed values and interval endpoints.
- Follow-up: Make midpoint tolerance and rounding symmetric for both signs, preserve the requested midpoint mode, and add independent positive/negative reference cases for both overloads and formatted output.
- Resolution: Both overloads now use a sign-symmetric midpoint tolerance based on the original value and apply the requested midpoint mode to the scaled value. Independent Core tests cover mirrored values, tolerance margins, negative digits, non-finite inputs, and Standard/Strict formatted estimates and interval endpoints.

## ITC-002 — Expanded bounds on logarithmic affinity coordinates

- Priority: Medium
- Status: Resolved (2026-10-02).
- Location: `AnalysisITC.Core/Analysis2/ParameterSet.cs`, `Parameter.RefreshLimits`.
- Problem: Affinity is stored as log₁₀K. Multiplying the standard bounds `[-2, 20]` gives `[-40, 400]` for Extended and `[-4000, 40000]` for No limit. Exponentiating the upper limits overflows; the No limit lower bound underflows. A one-site model can still predict finite heat at log₁₀K = 400 while reporting K = ∞ and ΔG = −∞.
- Resolution: Negative log₁₀K values are permitted. Extended and No limit widen K by factors of 20 and 2000 in both directions by adding/subtracting the corresponding log₁₀ factor to affinity bounds. Global candidates controlled by fitted affinity or linked enthalpy coordinates are rejected when any converted K or Kd is non-positive or non-finite; valid converted affinities may exceed local preset bounds. Focused tests cover physical endpoints, finite out-of-local-range values, overflow/underflow, and locked coordinates.

## ITC-003 — pKa correction can fail to converge

- Priority: Medium
- Status: Resolved (2026-10-03).
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs`, buffer ionic-strength calculation.
- Problem: Fixed-point iteration can enter a repeating cycle. The phosphate calculation at pH 8.198, 25 °C, and concentration 5 M reproduced the failure.
- Resolution: Solve the existing ionic-strength balance with bounded bisection. Return NaN for invalid inputs or when a finite solution cannot be verified within 128 iterations. The public API remains `double`.
- Limitation: The numerical solver retains the nearest-pKa/two-species approximation and monovalent-counterion assumption; convergence does not establish chemical accuracy at high concentration.
- Follow-up: See ITC-027 for the deferred solution-wide correction.

## ITC-004 — Unlocking a spline point discards a converted spline

- Priority: Medium
- Status: Resolved.
- Location: Spline point context menu "Unlock" in `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs` and `AnalysisITC.MacOS/GraphViews/DataProcessingGraphView.cs`.
- Resolution: "Unlock" clears the selected point's position and slope locks and processes with `replace: false`, preserving the current spline points. Both Avalonia and macOS expose Unlock when either lock is set. In a Smooth spline, the released slope is recalculated from the current points.

## ITC-005 — Standardization of inspector headers

- Priority: Low
- Status: Completed.
- Location: Inspector section headings and tabs across the macOS and Avalonia applications.
- Problem: Heading capitalization was inconsistent, with sentence case and title case mixed across inspectors and tools.
- Resolution: Standardized the affected inspector headings and related tabs to sentence case, preserving proper names and acronyms.

## ITC-006 — Remove the standalone Edit identifiers tool

- Priority: Medium
- Status: Resolved (2026-10-02).
- Location: Edit identifiers commands in the Tools menu and the standalone experiment identifier dialogs in the macOS and Avalonia applications.
- Problem: Experiment and sample identifiers can already be edited as part of the experiment's Details editor. The separate Edit identifiers tool and dialog duplicate that workflow and make identifier editing appear to be a standalone tool.
- Follow-up: Remove Edit identifiers from the Tools menu and direct users to edit identifiers within the selected experiment's Details. Keep identifier review as part of the import workflow.
- Constraint: Remove only the Tools menu command and its handlers. Keep the identifier dialog's single-experiment form (`ExperimentIdentifiersWindow` on Avalonia, `MacIdentifierEditor` on macOS): the import review shows it when an import adds one experiment.
- Resolution: Removed the macOS Tools command and the Avalonia Selection/context-menu command and their menu handlers. Identifier editing remains in the selected experiment's Details > Identifiers; import review retains both single-experiment and batch dialogs. Manual and in-app help now direct users to Details.

## ITC-007 — Avalonia UI tests fail when certain test classes run together

- Priority: Medium
- Status: Open; test infrastructure change deferred.
- Location: `AnalysisITC.Avalonia.Tests`, the `[Collection("Avalonia UI")]` classes and `AvaloniaTestBootstrap.EnsureInitialized` in `PreferencesTests.cs`.
- Problem: The headless platform is set up once with `SetupWithoutStarting`, which binds Avalonia's UI thread to whichever xUnit worker thread runs first. xUnit does not guarantee that later tests in the collection run on that thread. When the thread changes, every following test that creates a window fails with "The calling thread cannot access this object because a different thread owns it", in a cascade unrelated to the code under test.
- Reproduction (2026-10-01): `PreferencesTests` alone passes (24/24), but run together with `ExperimentDetailsWindowTests`, `AnalysisReportRenderingTests`, or `ExperimentIdentifiersWindowTests`, about 18 tests fail, starting with the test after `SavedAccessSurvivesOpeningApplyingAndReopeningPreferences`. `PreferencesTests` on its own also failed this way once in three runs.
- Review verification (2026-10-06): The full Release Avalonia suite again produced the same cross-thread exception cascade and stopped making progress; the review run was interrupted. Separately filtered runs exposed deterministic stale assertions, tracked in ITC-065, so those failures should not be attributed to this threading issue.
- Consequence: Full or filtered Avalonia runs can report failures that are not regressions; a real failure can be hidden among them.
- Follow-up: Run UI tests on Avalonia's dedicated test thread, for example with the `Avalonia.Headless.XUnit` package and `[AvaloniaFact]`/`[AvaloniaTheory]`, or by having the bootstrap marshal each test onto one owned dispatcher thread. Async tests then also need to resume on that thread.

## ITC-008 — Null Offset labels can expose unmatched experiment IDs

- Priority: Low (reassessed 2026-10-02; original High-priority unit defect resolved in the current working tree).
- Status: Partially resolved; ID-label fallback remains open.
- Location: `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs` (`AddAssessment`, Null offsets column) and `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`FormatNullOffsets`).
- Reassessment: The Offset is heat per mole of injectant, stored in J/mol. Both paths now resolve the selected energy family/override and call `Energy.ToString(unit, "G6", withunit: true, permole: true)`, which correctly converts the value and includes the molar unit. The original raw-value "µJ" export and unitless report defects are no longer present. Normal exports use experiment names; no-binding reports use report labels such as `1A`. `ToFormattedString` is not necessary to correct the units; the current `ToString` overload also performs the conversion.
- Remaining problem: Both formatters fall back to `member.ExperimentId` when the saved comparison member cannot be matched to an experiment name or report label. That fallback is not gated by Traceability Mode. A constructed unmatched-member case reproduces the ID exposure with Traceability Mode disabled; this is a defensive label-handling edge case, not a reproduced error in normal matched results.
- Validation (2026-10-02): 123 targeted Core checks passed, including seven temporary reassessment probes. For −10536 J/mol, both outputs produced −10.536 kJ/mol (automatic Joules) and −2.51816 kcal/mol (automatic Calories); explicit J, kJ, cal, and kcal overrides also passed independent expected-value checks. Normal name/report labels and the unmatched-ID fallback were verified. The temporary probes were removed after reassessment; production code was not changed.
- Follow-up: Use a readable fallback label when a saved comparison member cannot be matched; show internal IDs only under Traceability Mode. Add lasting regression coverage for null-offset unit conversion and matched/unmatched member labels when implementing that follow-up.

## ITC-009 — Null-only figure ignores the experiment x-axis and error bars

- Priority: Medium
- Status: Resolved (2026-10-03).
- Location: `AnalysisITC.Core/Presentation/PublicationFigure.cs` (`PublicationFigureBuilder.Build`, `TryResolveNullFit`, `CreateNullDisplaySolution`).
- Original problem: The separate saved-null figure plotted saved `Ratio` values directly (concentration axes in M), used "Saved/Current injection ratio" titles, and drew no error bars.
- Resolution: For **No binding detected** standard output, the figure builder now selects the fitted Offset from the applicable comparison (independent members use their own comparison; pooled results match the experiment) and draws it through the ordinary builder as a fresh Offset model on the current experiment. Current observations, concentrations, uncertainties, axis type, and all ordinary display controls apply, including offset correction and parameter annotations. The experiment's attached model and stored comparison solutions are not changed. When no successful, uniquely matching, finite Offset fit exists, the figure shows current observations with "Offset fit unavailable" and no prediction, residuals, or parameters. The separate saved-null builder and its "Saved…/Current…" annotations were removed. Missing convergence omits RMSD instead of failing. macOS no longer forces parameter annotations and offset correction off for these figures; Avalonia result-figure exports use the displayed model for shared axes.
- Current assessment policy (2026-10-05): Inconclusive retains the binding fit and its uncertainty; only effective No binding detected selects the Offset presentation.
- Kept: Saved comparison points remain the evidence for report and export comparison tables. The plotted Offset is the value fitted with the result; later processing edits change the plotted observations but not the Offset until the result is updated.
- Validation (2026-10-03): `ClassifiedNullFigureTests` compares classified figures against ordinary Offset figures on molar-ratio, concentration, and injection-number axes, with offset correction on/off, energy overrides, excluded points, custom titles, explicit limits, display toggles, reopening and later edits, independent/pooled matching, unavailable fits, missing convergence, non-finite binding parameters, mixed canvases, and unchanged analysis state.

## ITC-010 — Manual and older interpretations always warn "Assessment context unknown"

- Priority: Medium
- Status: Resolved.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`BuildInterpretationSection`) and `AnalysisITC.Core/DataClasses/AnalysisReportDefinition.cs` (`SetManualInterpretation`).
- Problem: Only AI-generated interpretation records receive `AssessmentContextFingerprint`. Manually written interpretations and every interpretation saved before this field existed have none. The report therefore shows an "Assessment context unknown" warning for all of them, even when no result in the report has a binding assessment.
- Resolution (2026-10-02): The "Assessment context unknown/changed" warnings were removed from reports and from the report window in both applications. Interpretation freshness and result validity already flag changed data.

## ITC-011 — Binding-output suppression is undocumented and missing on macOS

- Priority: Medium
- Status: Resolved.

## ITC-012 — Suppression applies to results, not to the experiments they contain

- Priority: Medium
- Status: Open; decision needed.
- Location: `ResultOutputPolicy.SuppressBindingOutputs` callers. `PublicationFigureCanvas.Expand` and `PublicationFigureBuilder.Build(ExperimentData, ...)` build figures from an experiment's own solution without a result.
- Problem: A figure or copy made from an experiment, rather than from its Analysis Result, still shows the binding fit and fitted parameters after the result is assessed No binding detected. The same data can therefore produce suppressed or full output depending on the entry point.
- Decision needed: Decide whether the assessment should govern experiment-level outputs whose solution belongs to an assessed result, or whether experiment outputs are deliberately unaffected. If unaffected, say so where the assessment is documented.

## ITC-013 — Full and diagnostic reports do not state the binding assessment

- Priority: Medium
- Status: Resolved; superseded.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`. The assessment is only rendered in `BuildNoBindingSections`.
- Problem: Reports for Binding detected and Not assessed results contain no null hypothesis test section. Inconclusive members already show their null comparison in Standard output, but the reports need to state their effective assessment alongside that evidence. A diagnostic report of a suppressed result shows full parameters, and the assessment should remain explicit beside those parameters. Table exports already add assessment columns for every result.
- Resolution (2026-10-02): Reports state the assessment only where it changes the output. The front-page table has a "Binding assessment" column, marked "(manual)" for overrides. The result overview has a "Binding assessment" row when standard output omits binding results, or when diagnostic output shows results that standard output would omit. The full comparison block appears only in diagnostic output. Binding detected and Not assessed results otherwise show no assessment.

## ITC-014 — Assessment labels now drive which parameters are hidden

- Priority: Medium
- Status: Resolved (2026-10-05).
- Location: `AnalysisITC.Core/DataClasses/BindingAssessmentState.cs` (rule `aicc-0-10-v1`) and `ResultOutputPolicy`.
- Resolution: New fits classify signed ΔAICc ≤ 0 as No binding detected, 0 < ΔAICc < 10 as Inconclusive, and ΔAICc ≥ 10 as Binding detected. Saved outcomes and historical rule IDs restore unchanged. Inconclusive retains binding output and produces a health warning, without changing input validity. Reports include every member’s estimates and assessment; no-binding values are identified as attempted-model estimates. Standard table exports still omit no-binding values. Combined binding output is omitted if any member is effectively No binding detected, without recalculating from a subset. Manual overrides remain optional and authoritative.
- Interpretation: The cutoffs are chosen, without a calibrated false-positive guarantee. Binding detected favors the binding model over constant background heat per mole; it does not rule out concentration-dependent dilution, buffer mismatch, or drift.

## ITC-015 — Minor null-model output formatting

- Priority: Low
- Status: Open.
- Location: `AnalysisReportBuilder.BuildNoBindingSections`, `NullModelComparisonPresentation`, `AnalysisResultTableExporter`, `AnalysisReportWindow`.
- Problem:
  - An unavailable null RMSD reads "Unavailable µJ" in the diagnostic "Binding assessment and null comparison" block. The no-binding report block no longer shows it.
  - AICc, ΔAICc, and null RMSD in CSV/TSV exports use the current culture's number format, while temperatures in the same file are culture-invariant.
  - The first header cell of every table export now starts with "Standard" or "Diagnostic", which changes the header for scripts that read existing exports.
  - The no-binding report omits each experiment's conditions table (concentrations, temperature, identifiers).
  - The report purpose is not saved with report presentation settings and reopens as Standard. This may be intentional.
- Follow-up: Append units only to numeric values, use invariant formatting in file exports, and confirm the intended header and persistence behaviour.

## ITC-016 — Independent multi-experiment fits share one binding assessment

- Priority: High
- Status: Resolved.
- Location: `AnalysisITC.Core/Analysis2/NullModelComparisonCalculator.cs` (global `Calculate`, which assigns one comparison to every member), `AnalysisITC.Core/DataClasses/BindingAssessmentState.cs`, `AnalysisResult.BindingAssessment`, `ResultOutputPolicy`, and the `.ftxtc` `bindingAssessment` record.
- Problem: When a multi-experiment result has no shared parameters (`GlobalModel.ShouldFitIndividually`), each experiment is a separate fit, but the null hypothesis test produces one pooled ΔAICc and one result-level verdict for all members. The verdict tracks the strongest data, not each experiment:
  - One non-binding experiment among binders inherits Binding detected, and its fitted Kd, ΔH, and N are reported without a caveat.
  - If the pooled verdict is No binding detected, standard outputs hide the parameters of the experiments that do bind.
  - The pooled criteria estimate one residual variance across all members, so the pooled ΔAICc is not the sum of the member values and can disagree with them.
- Available evidence: Independent members already have their own binding AICc (`SolutionInterface.InformationCriteria`, set in `AnalysisResult.RefreshInformationCriteria`). With a local Offset, the null fit already fits each member separately (`nullSolutions`). A per-member ΔAICc therefore needs no additional fitting, only per-member null criteria.
- Scope: A per-member test is defined when `GlobalModel.ShouldFitIndividually` is true. Locking a parameter to the same value for every member does not by itself make the binding fits pooled. When a fitted parameter is shared across members, the binding fit is pooled and the result-level comparison remains the applicable test.
- Resolution: Independent results save assessments and comparisons by member solution ID. Their collection outcome is derived from member outcomes; individual output follows each member's outcome, while combined binding output requires every member to be eligible. Not assessed remains unrestricted. Pooled evidence is diagnostic only and does not determine member assessments.

## ITC-018 — Global null model for pooled shared-parameter fits

- Priority: High
- Status: Deferred.
- Problem: Pooled binding fits with shared parameters are currently compared with locally fitted Offset models. A future global null model should represent the same parameter-sharing or constraint structure as the pooled binding model, with corresponding criteria and persistence. Until that design is completed, the saved comparison uses local Offset fits and the established pooled-variance convention.

## ITC-019 — Configurable assessment output set

- Priority: Low
- Status: Deferred.
- Problem: The output-allowed outcomes are currently fixed to Binding detected, Inconclusive, and Not assessed. Reports additionally retain no-binding member estimates as explicitly identified attempted-model values; standard table exports omit them. A future preference could make this set configurable, which would change suppression behavior across reports and exports.

## ITC-017 — Extreme confidence interval bounds render as long fixed-point numbers

- Priority: Minor
- Status: Resolved (2026-10-03).
- Location: `AnalysisITC.Core/Math/NumberStructs.cs`, `FloatWithError.WithMod` and `ConfidenceIntervalString`.
- Problem: Confidence interval endpoints are formatted with the same fixed-point format as the central estimate. When an interval endpoint is unbounded or approaches the largest finite floating-point value, it can appear as an unwieldy long number instead of a concise indication that the bound is effectively infinite. This obscures the useful interval and makes the result difficult to read.
- Follow-up: Handle non-finite and extreme finite confidence bounds explicitly, using a concise representation such as `∞` (or scientific notation where the bound is finite), while preserving ordinary interval formatting.
- Resolution: After unit conversion and display rounding, each estimate, SD, and interval bound with magnitude ≥ 10¹⁰ is formatted independently with `G6` (scientific notation, up to six significant digits); smaller values keep the format selected by the number-precision setting. Finite values stay numeric, including near `double.MaxValue`; when display rounding overflows a finite component, its unrounded unit-converted value is formatted instead. Infinite values show as `∞` / `−∞`; NaN formatting is unchanged. Covered by `FloatWithErrorCompactFormattingTests`.

## ITC-020 — Multi-result report chapters ignored the output purpose

- Priority: High
- Status: Resolved (2026-10-02).
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`CopyOptionsForResult`).
- Problem: The per-result options did not copy `OutputPurpose`. A Diagnostic report with more than one result validated as Diagnostic but built every result chapter as Standard output.
- Resolution: Result chapters inherit the report's output purpose. Found while making single-result reports use the result-chapter path.

## ITC-021 — Result chapter appendix labeled every result identifier as result 1

- Priority: Minor
- Status: Resolved (2026-10-02).
- Location: `AnalysisReportBuilder.BuildResultChapter`.
- Problem: In Traceability Mode, each result chapter's **Result identifiers** row used the label 1, so result 2 appeared as "1: <ID>".
- Resolution: Each chapter's result reference uses the result's position in the report.

## ITC-022 — Report-wide details repeat in every result appendix

- Priority: Low
- Status: Resolved (2026-10-02).
- Location: `AnalysisReportBuilder.BuildAppendix`.
- Problem: Each result chapter's appendix ends with a **Report details** block (software, application version and, in Traceability Mode, report identifier). These describe the whole report, so a report with N results repeats them N times. A report-level closing section or the front page may be a better home.
- Resolution: Reports have one appendix at the end with report details, report warnings and a combined experiment sources table.

## ITC-023 — Front-page bookkeeping notice wording with one result

- Priority: Minor
- Status: Resolved (2026-10-05).
- Location: `AnalysisReportBuilder.BuildFrontPage`.
- Resolution: One neutral report-wide note identifies the saved concentration and injection-heat methods and their applicable members. It handles mixed methods within one result or across results without repeated warning notices or required actions.

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
- Status: Open; idea.
- Location: report contents selection and `AnalysisReportBuilder.BuildExperimentSourcesTable`.
- Problem: Recorded buffer references can be included automatically as supporting experiments. Tandem source experiments could be included the same way if the tandem provenance identifies them, and listed in the appendix Experiment sources table with their role.

## ITC-027 — Solution-wide buffer ionic-strength correction

- Priority: Low
- Status: Open; deferred scientific-method follow-up.
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs`, buffer ionic-strength calculation.
- Problem: Each buffer currently estimates its correction using only its own contribution and nearest pKa transition as a two-species system. It adds neither salt nor other buffer contributions while solving that buffer's correction, so interactions across solution components are not represented.
- Follow-up: Consider a solution-wide calculation covering all buffer species, salts, and their coupled ionic-strength contributions, with independently validated reference cases.

## ITC-028 — Avalonia macOS menu bar and file browser activation

- Priority: Medium
- Status: Open; investigated (2026-10-03). Suspected native activation issue; the reported failure has not yet been reproduced locally.
- Location: `AnalysisITC.Avalonia`, macOS application activation, native menu bar, and file browser dialogs.
- Problem: Intermittently, the macOS menu bar shows only the application name and does not load the application's menus. The user also reports that the file browser sometimes appears not to be active; this may be related, but a shared cause is unconfirmed.
- Workaround: Minimizing the application and selecting it again restores the menu bar.
- Expected behavior: The application menus should be available whenever the application is active, and file browser dialogs should become active when opened.
- Investigation (2026-10-03): The project references Avalonia 12.0.5. `AppMenuController.Install` attaches the window's File/Edit/Selection/Tools/Help menus in the `MainWindow` constructor, before the window is shown; `App.axaml` supplies the application menu. Menu refresh updates visibility and command state, and does not remove all top-level menus. `DisableDefaultApplicationMenuItems = true` disables Avalonia's added standard application-menu commands, not native menus. The bundle template has neither `LSUIElement` nor `LSBackgroundOnly`, and `MacDockIcon.Apply` only sets the icon.
- Native evidence: Avalonia 12.0.5's desktop lifetime shows the main window before entering the dispatcher loop. Its native `WindowBaseImpl.Show` immediately calls `activateIgnoringOtherApps:`. [Avalonia PR #21799](https://github.com/AvaloniaUI/Avalonia/pull/21799) identifies this early activation as a timing-dependent cause of unresponsive file-picker lists/sidebars that recover after switching apps; its deferred-activation helper is present in the 12.1.1 source and absent from 12.0.5. Separately, `AvnWindow.mm` restores window menus in `becomeKeyWindow` and switches to the application-only menu in `windowDidResignKey`. A missed or disrupted key-window transition could therefore explain the menu symptom and workaround, but this remains an inference rather than a confirmed shared cause.
- Caveat: [Avalonia issue #22111](https://github.com/AvaloniaUI/Avalonia/issues/22111) remains open and reports similar file-picker activation failures in bundled apps on 12.1.1 despite that fix. A dependency upgrade alone is not a verified solution. FT-ITC also opens optional Traceability Mode/recovery dialogs from `Opened`, so reproduction should cover startup with and without those dialogs and the return to the main window.
- Validation: Eight existing `SelectionMenuTests` and `StartupFileActivationCoordinatorTests` passed. They validate managed menu construction/state and queued startup work using the headless backend; they do not reproduce or exclude a native macOS activation race. No application code or dependency versions were changed during this investigation.
- Follow-up: Record the affected macOS version, app/terminal/IDE launch context, whether the menu is missing from launch or disappears after a dialog, and whether the picker opens but ignores file-list/sidebar clicks. During native reproduction, compare application activation, the main/key window, and the installed native menu before and after the workaround. Evaluate deferred initial activation or an upstream version containing the fix against both symptoms, including bundled-app behavior, before choosing an implementation.

## ITC-029 — Improve analysis inspector null-test and fit summaries

- Priority: Low
- Status: Resolved (2026-10-05).
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs` (`BuildAnalysisNullComparisonSection`) and `AnalysisITC.MacOS/AnalysisParameterSummaryPresentation.cs` / `AnalysisITC.MacOS/CustomViews/AnalysisFitSummaryView.cs`.
- Problem: The Analysis inspector's null hypothesis test summary needs clearer visual hierarchy and formatting as a section. The parameter summary currently places RMSD on the model heading line; it should identify whether the solution is individual or global, with RMSD shown on its own line below.
- Follow-up: Improve the null hypothesis test section's layout and readability. In the parameter section, label the solution scope (individual or global) and move RMSD to a separate line below the model/scope heading.
- Resolution: The live null hypothesis test rows (Model, Null RMSD, ΔAICc, Conclusion) are defined once in `NullModelComparisonPresentation.AnalysisInspectorRows` and rendered by both the macOS and Avalonia Analysis inspectors; the combined "RMSD / ΔAICc" row is split. On macOS the section has a full-width separator and bold header aligned with Fit Summary. The fit summary heading shows the readable model name with the scope (Global/Individual, from `IsGlobalAnalysisSolution`) right-aligned, and RMSD is the first parameter row. Fit summary values now draw right-aligned. The Results tab null section and graph parameter boxes are unchanged.

## ITC-030 — Unify fitted-parameter presentation in Analysis

- Priority: Medium
- Status: Implemented; pending review.
- Location: macOS Analysis inspector and graph; Avalonia Analysis inspector and `IntegratedHeatsGraphControl`.
- Investigation: macOS always presents the selected solution's fit summary in the Analysis inspector, while its Analysis graph explicitly disables the parameter box. Avalonia's Analysis graph draws the fitted parameters in an optional parameter box; its inspector's Parameters tab edits starting values and global constraints, rather than showing fitted results. The Avalonia graph's Parameter box option also controls parameter guides. Both platforms already use `AnalysisParameterDisplay` to select displayed parameter categories, and share the larger-text preference.
- Decision: When a fitted solution exists, always show its fit summary in the Analysis inspector on both platforms. Keep the plotted parameter box optional and independent of the inspector summary on both platforms. Retain Avalonia's current enabled-by-default plot behavior when adding the option on macOS; the inspector summary must remain visible regardless of that option. Keep the existing parameter display selection and formatting conventions.
- Follow-up: Add the fitted summary to Avalonia's inspector; expose an independent plot-box control on macOS; ensure both controls use the selected solution and the shared display preferences. Keep Avalonia's editable Parameters tab distinct from the read-only fit summary.

## ITC-031 — Removed supporting experiments are reselected when a report reopens

- Priority: Low
- Status: Open; deferred (2026-10-05).
- Location: macOS `AnalysisReportViewController` (`PopulateResults`, `LoadReport`); Avalonia `AnalysisReportWindow` equivalents.
- Problem: The report window always opens from a result and recomputes automatic supporting selections. Stored reports are matched by the exact ordered result and supporting-experiment IDs. If a user removes an automatically selected experiment and then saves report settings or an approved interpretation, reopening selects that experiment again, and the stored report is not loaded until the user removes it again. The stored report is not lost, but appears to be. This applies to buffer references now, and will apply more often to tandem sources once ITC-026 is implemented.
- Follow-up: Decide whether opening the report window should prefer an existing stored report for the result, or remember removals per report, before automatic selection is applied.

## ITC-032 — Stored reports accumulate without limits or deletion

- Priority: Low
- Status: Open; idea.
- Location: `DataManager.Reports`; `EnsureReportRegistered` in both report windows; `.ftxtc` `reports/` collection.
- Problem: Both report windows register a stored report when an applied selection includes supporting experiments, when presentation settings change, and when an interpretation is edited or generated. Each distinct ordered selection becomes a separate stored report saved with the project. `DataManager.RemoveReport` exists, but neither application calls it, so stored reports cannot be deleted. Automatic tandem-source selection (ITC-026) will register reports more often.
- Ideas: A maximum stored-report count; a storage-size limit or size display; a user option to delete stored reports; registering a report only once it holds user content (changed settings, comments, or an interpretation).

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
- Status: Open; observed during report signing layout QA (2026-10-05).
- Location: `SkiaAnalysisReportRenderer.DrawText`, `SkiaPublicationFontSet`.
- Problem: The report renderer draws each string with a single selected font and does not substitute a font for missing glyphs. A preparer name containing `李` appears with a missing-glyph box in the preview; the name remains intact in the report document and PDF Author metadata. This uses the existing report text renderer and also affects ordinary report text containing unsupported characters.
- Follow-up: Decide how report font fallback should work, keeping text measurement, wrapping, preview drawing, and vector PDF export consistent. Include mixed-script names in visual regression checks.

## ITC-036 — Processing details shown for integrated-heats imports

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`BuildExperimentSections`, `BuildProcessingItems`).
- Problem: Analysis reports include a **Processing and integration** block even when an experiment has integrated heats but no raw thermogram. Baseline method, integration mode, and integration-region values can therefore appear as though baseline correction and peak integration were performed on the imported data, although only integrated heats are available.
- Follow-up: Present only processing details that apply to the available source data, and make clear which reported values were imported versus derived by processing.

## ITC-037 — Bookkeeping convention displayed in a large appendix notice

- Priority: Low
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`AddReportAppendix`, `BuildExperimentMetadata`, `BuildProcessingItems`).
- Problem: The report presents saved concentration and injection-heat bookkeeping methods together in a large boxed notice in the appendix, away from the experiments they describe. This gives routine method metadata the visual weight of a warning and makes it harder to connect each method to its experiment.
- Suggestion: Show each experiment's saved bookkeeping method with its processing information, and remove the routine appendix notice. Reserve boxed notices for cautions or for expanded explanations when that option is enabled (related: ITC-024). If an analysis result contains experiments with different bookkeeping, this should result in a warning/caution level box (either red or orange). 

## ITC-038 — Add experiment name to report page headers

- Priority: Low
- Status: Resolved (2026-10-06).
- Location: `AnalysisReportLayoutEngine` (page plan `ResultName`), `SkiaAnalysisReportRenderer.DrawHeader`, `CoreGraphicsAnalysisReportRenderer` page header.
- Problem: When an experiment chapter continues onto later pages, those pages show only the report and result name in the running header, so the reader must look back to find which experiment the content belongs to. Section titles are deliberately not repeated as “continued”; continued block titles carry “– continued” instead.
- Idea: Add the current experiment label and name (for example “E1. Experiment name”) to the running header on experiment pages, beside the existing result name. Long names need truncation so the export date stays visible.
- Resolution: Every fitted experiment chapter page includes its existing reference label and name beside the result name. Both renderers use the shared header layout to preserve the export date, prioritize the result name, and truncate names at text-element boundaries; the experiment label remains when its name cannot fit. Supporting-data headers are unchanged.

## ITC-039 — Diagnostic summary graphs include no-binding experiments

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`BuildThermodynamicSummaryPlot`) and `AnalysisITC.Core/Presentation/ResultOutputPolicy.cs` (`IsMemberBindingOutputAllowed`).
- Problem: The thermodynamic summary graph passes the report's output purpose to the generic member-output policy. Diagnostic output bypasses assessment filtering, so experiments assessed **No binding detected** appear in the graph. Their attempted estimates or wide uncertainty intervals can dominate the scale and make the eligible experiments unreadable. Standard output already excludes them.
- Reproduction (2026-10-06): A result with two binding experiments and one no-binding experiment plots two members in Standard mode but all three in Diagnostic mode. Giving the no-binding member a finite enthalpy of −25,000 J/mol and a finite interval of [−10¹¹, 10¹¹] J/mol increases the plotted extent from about 34 to 100 million kJ/mol.
- Required behavior: Exclude effectively No binding detected experiments from the thermodynamic summary graph in both Standard and Diagnostic reports. Keep Inconclusive experiments eligible, respect manual assessment overrides, and omit the graph when no eligible members remain. Attempted estimates can remain in the detailed parameter tables.
- Follow-up: Apply assessment filtering to this graph independently of the Diagnostic permission to show attempted fit details. Correct the tests that currently require Diagnostic inclusion; cover mixed assessments, all-no-binding collections, and wide finite intervals in Core and both renderer suites.

## ITC-040 – Identify information, caution, and warning box locations

- Priority: Medium
- Status: Open
- Problem: Currently we have a poor idea of where boxes might be placed.
- Suggestion: Identify places where boxes can be placed, what they contain, and what triggers them. In general, boxes should not show up for no reason. Boxes should be reserved for specific of note items that the reader needs to know about the presented data. This could be different bookkeeping conventions, ... exploration necessary.

## ITC-041 - Source file and source format in one line for analysis reports

- Priority: Low
- Status: Resolved (2026-10-06).
- Resolution: Full, condensed, and supporting experiment details show the source filename and recorded source format together under Source file, with explicit placeholders for unavailable metadata. Both apps retain complete values through wrapping; the appendix filename column is unchanged.

## ITC-042 - Competitor properties affinity and enthalpy values are not showing up

- Priority: High
- Status: Deferred
- Problem: report test project competitor attributes do no display values. They appear to be saved/loaded as NaN or non finite.

## ITC-043 - Report baseline type could include information on baseline

- Priority: Low
- Status: Resolved (2026-10-06).
- Problem: Spline or Polynomial is not a lot of information
- Suggestion: Add ", dense" or ", 12th degree", etc. Some description in the same line.
- Resolution: The shared report builder appends spline mode and point density, or the selected polynomial or segmented degree, to the Baseline method value in both apps.

## ITC-044 - Explain saved FWE

- Status: Resolved (2026-10-06).
- Problem: Why are these saved like this, and not just as a float with error? "saved.CapturedAffinity, saved.CapturedAffinitySD, saved.CapturedAffinityLower, saved.CapturedAffinityUpper"
- Sugestion: Investigate
- Resolution: The fields belong to the fit-time validity snapshot (`ExperimentAttributeSnapshot`), not the live attribute (which is already persisted as an FWE). The snapshot holds only primitive fields so it can be compared component by component with `SameDouble`, following the older `ParameterValue`/`ParameterSD` convention; the wire format mirrors it. The flat form drops the FWE missing flag, but captured Kd and ΔH are always written as a finite pair or cleared together, and a fit cannot run with a required value missing, so no missing value reaches a snapshot today. Added `CapturedAffinityWithError`/`CapturedEnthalpyWithError` accessors (non-finite value → `FloatWithError.NaN`) and used them in the report instead of manual reconstruction. No format change. Follow-up: ITC-055.

## ITC-045 - macOS report inspector does not allow scrolling all the way down.

- Priority: High
- Status: Resolved (2026-10-06).
- Location: `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs`, report inspector scroll view.
- Problem: Cannot scroll to the bottom of the macOS inspector and thus cannot access all options.
- Resolution: The inspector's scroll content had a fixed height of 720 pt, and the bottom of the inspector sections wasn't tied to it. The content is now pinned to the top, left and width of the visible scroll area, and the bottom of the sections is pinned to its bottom. The scroll range now follows the inspector's content.

## ITC-046 - Unnecessary summary caveat in report

- Priority: Medium
- Status: Resolved (2026-10-06).
- Problem: Extra information present that should no be there: "Local summary intervals are approximate: 95% coverage is not established, and covariance between experiments is omitted."
- Resolution: Removed the caveat from standard reports; the detailed explanation remains available with expanded explanations.

## ITC-047 - Too long block header: "Combined across experiments at the mean temperature: 25.00 °C"

- Priority: Low
- Status: Resolved (2026-10-06).
- Problem: Header is too long. Figure out what the user should understand and communicate that in fewer words.
- Additional: Clarify the temperature used when temperature dependence is present.
- Resolution: Use **Combined parameters** with **Evaluation temperature** as the first row. Calculations are unchanged. The manual and report help explain that temperature series use the current Reference temperature preference, while other results use the mean experiment target temperature.

## ITC-048 - Join report summary model and fit details blocks 

- Priority: Low
- Status: Resolved (2026-10-06).
- Resolution: The analysis summary uses one **Model and fit details** block in both macOS and Avalonia reports. It retains all model settings, constraints, and fit diagnostics. Fixed parameters and each experiment's fit details remain separate.

## ITC-049 - Experiment name font and truncation

- Priority: Low
- Status: Resolved (2026-10-06).
- Problems:
  - The bold font can make it difficult to destinguish the experiment number (Eg 1A) from the name in some cases where the exp name starts with sometihng like "C1"
  - The overview exp names are now mid truncated which is ok, but the truncation is too aggressive. Ideally the maximum length is either adaptive to the width of the graph displayed, or we provide a better guess than currently.
- Resolution: Figure canvas panel headings (report overview and Supporting Figure Canvas) draw the panel label bold and the experiment name in regular weight, separated by a space. The fixed `PanelTitleMaximumCharacters` cap is removed; canvas cells keep the full name, and both renderers middle-shorten it with the shared `PublicationFigureCanvasBuilder.FitPanelTitle` to the measured panel width.

## ITC-050 - Result health can have warning if analysing a no binding experiment 

- Priority: High
- Status: Resolved (2026-10-06).
- Problem: No binding experiments still contribute to the analysis result health, this may result in parameter limits or fitting issues being reported as overall result health issues. Even for bootstrap solutions.
- Suggestion: parameter limit clashing of non-binding results in an individually fitted analysis result should not degrade the overall analysis result health. There might be multiple similar issues not yet encountered, thus I suggest starting by checking for these before starting on the implementation of a solution.
- Resolution: `BindingAssessmentInterpretation` (Core) is the single translator from assessment to binding/non-binding; only an effective No binding detected is non-binding. `AnalysisResult.FitWarningMembers` excludes non-binding members, so their best-fit boundary, bootstrap/LOO boundary, and optimizer-limit warnings no longer affect `Health` or `HealthReasons`. This applies to independent, single, and pooled results. Input validity is unaffected. The competitor source badge now follows `source.Health`, so Inconclusive sources show Warning. Unchanged: member rows in the result views, post-refit status bar, web viewer per-fit warnings, and report per-experiment diagnostics. Follow-ups: ITC-052, ITC-053, ITC-054.

## ITC-051 - Ensure report summary table and summary values are aligned 

- Priority: High
- Status: Open
- Suggestion: do a focused update of the analysis report summary parameter table building and the summary value calculator. My current suggestion is to (in the standard report type) not show values for experiments that are assessed to be non-binding. These rows would be left blank. I would also skip the binding assessment label in the title column for experiments that are assessed to be binding, and perhaps try to retain that assessment elsewhere for experiments that are non-binding. Again we should not forget how non-assessed and inconclusive assessments are considered. The summary value should 

## ITC-052 - Skip uncertainty estimation for results assessed as no binding

- Priority: Medium
- Status: Open
- Problem: Parameters of an experiment assessed as no binding are not expected to be meaningful, yet bootstrap/LOO/profile uncertainty is still estimated for them, which costs time and produces warnings that ITC-050 now hides from result health.
- Open questions: The assessment is only known after the binding and null fits, and it can be changed manually afterwards. Decide what happens when a manual override switches a result to binding (estimate on demand, require Update Result, or mark uncertainty unavailable), and how Update Result should behave.

## ITC-053 - Non-binding members still show fit warnings outside result health

- Priority: Low
- Status: Open
- Problem: After ITC-050, fit warnings from members assessed as no binding no longer affect result health, but they still appear in each experiment's row in the macOS/Avalonia result views, in the status bar after a refit (combined convergence boundary contacts), in web viewer per-fit warnings, and in report per-experiment diagnostics.
- Suggestion: Decide whether these should stay, be demoted, or be annotated as not counted. Use `BindingAssessmentInterpretation` for any change.

## ITC-054 - Competitor source assessed as no binding

- Priority: Medium
- Status: Resolved (2026-10-06).
- Problem: A competition experiment consumes the Kd/ΔH of its competitor source result. Since ITC-050 the source badge follows `source.Health`, so a source assessed as no binding whose fit hit a parameter boundary now shows Valid, even though its values are being reused.
- Suggestion: Flag non-binding competitor sources explicitly (status or tooltip), independent of result health.
- Resolution: `CompetitorResultPreviewBuilder` shows a `No binding` status when any source member is non-binding per `BindingAssessmentInterpretation`. Precedence is Unknown > Stale > No binding > Changed > Warning > Valid. The tooltip says the source was assessed as no binding (or "n of m experiments" for a partially non-binding independent collection) and that Kd and ΔH may not be meaningful. Inconclusive assessments get a matching tooltip line; their status still follows health (Warning). Display only: fitting still uses the source values, and the source picker is unchanged.

## ITC-055 - Validity comparison tolerance hides small competitor Kd changes

- Priority: Medium
- Status: Open
- Problem: `AnalysisResultValiditySnapshot.SameDouble` uses `1e-12 + 1e-9·max(1, |x|)`. Kd is stored in M, so the scale is always 1 and the tolerance is about 1 nM absolute. A competitor source refit that moves Kd from 5 nM to 5.8 nM does not mark the dependent competition fit stale, and any pM-range change is invisible; the captured SD and interval endpoints have the same blind spot. The attribute snapshot is the only guard: model options derived from the attribute are not re-checked. Concentrations (µM–mM) and enthalpies (J/mol) are unaffected in practice.
- Suggestion: Compare Kd-scale fields with a relative tolerance or on log Kd. Check whether other small-magnitude snapshot fields (for example prebound ligand concentration in the nM range) share the issue.

## ITC-056 - Core test expects no parameter table in Standard no-binding reports

- Priority: High
- Status: Open; decision needed.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`BuildExperimentSections`, per-experiment "Fitted and derived parameters" table) and `AnalysisITC.Core.Tests/ClassifiedOutputPolicyTests.cs` (`NegativeStandardReportUsesAssessmentPathWithoutBindingParameterSection`).
- Problem: `AnalysisITC.Core.Tests` fails 1 of 2172 tests at d7c5f51d. Commit 7deb0799 removed the `BuildNoBindingSections` route, so a Standard report for a result assessed No binding detected now goes through `BuildExperimentSections`, which always adds the per-experiment parameter table. The test still asserts the earlier behaviour. The uncommitted manual and help edits (2026-10-06) describe the current behaviour: every member's values are shown with its assessment. ITC-051 instead proposes leaving no-binding rows blank in the Standard summary table.
- Suggestion: Decide with ITC-051 whether Standard per-experiment tables show, blank, or omit values for no-binding members. Then update either the test or `BuildExperimentSections`, and keep the manual consistent.

## ITC-057 - Interpretation access code is stored in plain text

- Priority: Low
- Status: Open
- Location: `AppSettings.InterpretationOperatorCode` (`SaveToStorage`, load), `MacUserDefaultsSettingsStore`, `AvaloniaJsonSettingsStore`.
- Problem: The interpretation access code is sent as a Bearer credential to the relay, but it is saved unencrypted in NSUserDefaults on macOS and in the Avalonia JSON settings file. Any process running as the user, and any backup of those files, can read it. Only a SHA-256 hash is needed for the cached-access checks, but the plain value is stored as well.
- Suggestion: Store the code in the OS credential store (Keychain on macOS; the platform equivalent for Avalonia) and keep only the hash in preferences. Migrate existing values on first load.
- Comment: the interpretation access code is not super secret and I would prefer not starting to mess with platform secure storage if possible. It should be considered as an activation code, not a personal secret.

## ITC-058 - Asymmetric least squares baseline has no effect

- Priority: Low
- Status: Open; decision needed.
- Location: `AnalysisITC.Core/DataProcessing.cs` (`AssymetricLeastSquaresInterpolator`).
- Problem: `Interpolate` computes the ALS curve but never assigns `Baseline` (the assignment is commented out). Re-running interpolation therefore keeps whatever baseline was stored, and the Iterations, Lambda and Asymmetry settings have no effect. The run is expensive: the second-difference matrix is built from dense rows, which is O(n²) in the number of thermogram samples. `Copy` is not overridden, so a copied processor becomes a plain `BaselineInterpolator` and loses the ALS settings. ALS cannot be selected in either app, but `.ftxtc` (`"asl"`) and legacy files can still restore it.
- Suggestion: Decide whether to remove ALS (map restored ALS processors to a supported type while keeping the stored baseline) or finish the implementation.

## ITC-059 - Completing a save clears edits made while the file is being written

- Priority: High
- Status: Resolved (2026-10-07).
- Location: `AnalysisITC.Core/DataExport/ProjectWriter.cs`, `SaveAsync` and `SaveWithPathAsync`; `AnalysisITC.Core/DocumentDirtyTracker.cs`, `MarkClean`.
- Problem: Native serialization captures the document before its asynchronous writes finish. Both save methods then unconditionally mark the current document and all its containers clean, including edits made after that capture. The write gate serializes saves but does not prevent edits. Closing can therefore discard an unsaved change without a prompt, and autosave no longer sees a dirty document.
- Reproduction: Load `AnalysisITC.Tests/OneSetOfSites/data_1.itc`, set its comment to `before save`, and start `SaveWithPathAsync` on a single-thread synchronization context. While the save is pending, change the comment to `edit made while saving`, then let the save finish. A temporary executable probe returned success with both `DocumentDirtyTracker.IsDirty` and the experiment's `IsModified` false, while reopening the file restored only `before save`.
- Resolution: Core now tracks a document identity and monotonic edit revision, including repeated edits and changes during `Suspend()`. Saves retain the captured identity/revision and clear dirty state only when both still match and no import/restoration scope is active. A stale save preserves dirty state, deferring its notification when necessary until suspension ends. Identity changes only on Clear, opening a clean native project, and successful recovery into an empty document; appends retain identity. Requests abandoned before writing return false with an explanatory status. Queued saves resolve their destination after acquiring the save gate, which remains held through completion bookkeeping. Both desktop apps use `SaveForCloseAsync` for Save before close, quit, clear, or replacement; newer edits keep the document open without a retry or second prompt. macOS no longer adds a second clear confirmation after Save/Discard, while clean-document confirmation remains preference-aware. The manual and in-app help explain saving again when newer changes remain.
- Validation (2026-10-07): 30 focused Core save/tracker/autosave tests passed, including deterministic snapshot/write pauses, repeated and suspended edits, report changes, append/open/recovery identity boundaries, stale file dialogs and queued requests, queued Save after Save As, same-filename replacement, and continued autosave eligibility. Both focused Avalonia close-save integration cases passed (successful and failed writes); native macOS managed compilation passed without packaging or signing. The full Core suite completed with 2,204 passed, 1 failed, and 1 skipped; the failure is the existing report-policy assertion tracked in ITC-056. No project-format change.
- Limitation: Mutations that bypass `MarkModified()` and document-change notifications remain invisible to dirty tracking; auditing those paths is outside this fix.

## ITC-060 - Duplicating buffer-corrected data loses the applied correction

- Priority: High
- Status: Resolved (2026-10-07).
- Location: `AnalysisITC.Core/DataManager.cs`, `DuplicateSelectedData`; `AnalysisITC.Core/DataClasses/InjectionData.cs`, `Copy`; `AnalysisITC.Core/DataClasses/ExperimentData.cs`, `SetBufferSubtraction`.
- Problem: Duplication copies injections before the buffer-subtraction attribute. `InjectionData.Copy` initializes the new peak from `RawPeakArea`, when no buffer reference is available, and adding the attribute afterwards neither reapplies the correction nor subscribes to reference changes. The duplicate advertises a buffer reference while its downstream peak areas remain uncorrected. Both desktop apps use this shared path.
- Reproduction: With integrated target heats of 10 µJ and a matched blank of 2 µJ, the original has corrected heats of 8 µJ. After duplication, the copy retains the same reference but has 10 µJ peaks. Changing the blank to 3 µJ and publishing its processing update changes the original to 7 µJ while the copy stays at 10 µJ. Confirmed with a temporary executable probe.
- Follow-up: Restore buffer subtraction through the central entry point after the duplicate's attributes and injections exist. Verify the initial corrected values and subsequent reference updates, including integrated-heats data that will not undergo baseline reprocessing.
- Resolution: `DuplicateSelectedData` now calls `SetBufferSubtraction` once the copy's injections and attributes exist, so the duplicate starts with corrected heats and follows reference updates; an unresolved reference attribute is copied unchanged. `BufferSubtractionDuplicationTests` covers initial and updated heats with and without raw data, and confirms clearing the copy's buffer leaves the original subscribed.

## ITC-061 - Batch export can overwrite another experiment in the same batch

- Priority: Medium
- Status: Resolved (2026-10-07).
- Resolution: `Exporter.PlanOutputs` allocates every output path once per export, from the experiments that will actually be written (Data export counts only experiments with a thermogram). A name that is unique in the batch keeps `<base>_<name>`. Duplicate names (case-insensitive) and blank names take the next free `_<n>` suffix from 1, skipping names already in use. Repro result: `review_sample_2`, `review_sample_3`, `review_sample_1`. The overwrite prompt and `WriteOutputs` use the same plan. Covered by `ExportOutputPlanTests`.
- Location: `AnalysisITC.Core/DataExport/Exporter.cs`, `BuildOutputFileName`, `GetPlannedOutputPaths`, and the per-experiment writers.
- Problem: The filename builder adds an index when experiment names collide, but does not check whether the resulting name is already another experiment's name. The writers then open the same output path twice, silently replacing the earlier experiment. Checking for files already on disk does not detect collisions within the planned batch.
- Reproduction: Export three experiments named `sample`, `sample`, and `sample_1` with output base name `review`. Their paths are `review_sample_1.csv`, `review_sample_2.csv`, and `review_sample_1.csv`. A temporary probe performed the actual peak export into a fresh directory: only two files remained, and `review_sample_1.csv` contained the third experiment's values.
- Follow-up: Allocate unique final filenames across the complete batch, including sanitized and generated suffixes, and reuse that allocation for overwrite confirmation and writing. Test colliding original names and names that already contain a generated suffix.

## ITC-062 - Save Selected omits required buffer-reference experiments

- Priority: Medium
- Status: Open; reproduced (2026-10-06).
- Location: `AnalysisITC.Core/DataExport/ProjectWriter.cs`, `SaveSelectedAsync`; `AnalysisITC.Core/DataReaders/FTXTCReader.cs`, `RestoreBufferReferences`.
- Problem: Saving an experiment writes only that experiment. Saving a result writes only its fitted members. Neither includes a buffer-subtraction reference outside that set, although the saved attributes still identify it. The resulting native file cannot be opened under the strict read policy. Recovery retains persisted corrected heats but reports a partial load; the missing blank prevents reproducing the correction after processing edits.
- Reproduction: Apply matched buffer subtraction to an experiment, then save just that target through `SaveSelectedAsync`. A temporary probe returned save success, but strict reopening threw `InvalidDataException` for an unavailable buffer reference. Recovery returned `IsPartial = true` with `buffer-reference-unavailable`.
- Follow-up: Include required buffer dependencies in selected-item saves, or explicitly define a detached representation that preserves corrected data without an unresolved reference. Verify strict round trips for selected experiments and results whose blank is not a fitted member.

## ITC-063 - Trailing slash bypasses the Web viewer upload concurrency limit

- Priority: High
- Status: Resolved (2026-10-07); reproduced locally before the fix (2026-10-06).
- Location: `AnalysisITC.Web/ViewerUploadLimits.cs`, `ViewerUploadAdmissionMiddleware.InvokeAsync`; `/api/viewer/open` endpoint in `AnalysisITC.Web/Program.cs`.
- Problem: Admission uses an exact string comparison with `/api/viewer/open`, while endpoint routing also accepts `/api/viewer/open/`. The trailing-slash request reaches native project parsing without acquiring the shared semaphore. Per-request archive limits still apply, but callers can bypass the configured aggregate concurrency bound and run multiple memory- and CPU-intensive parses together.
- Reproduction: In an in-process `WebApplicationFactory` test, obtain a normal viewer antiforgery token, acquire the sole `ViewerUploadAdmission` permit, and upload the same valid `jors.ftxtc` fixture to each path. `/api/viewer/open` returns 503; `/api/viewer/open/` returns 200 with parsed experiments while the permit is still occupied. No deployed service was contacted.
- Resolution: The upload endpoint carries admission metadata, and routing now runs explicitly before the admission middleware. Canonical, trailing-slash, and mixed-case routes share a disposable `ConcurrencyLimiter` lease before antiforgery validation, form reading, or parsing. One upload remains active per server process, with configurable defaults of five requests waiting in arrival order and a 30-second wait-only timeout. Queue overflow preserves `503 viewer_busy`; timeout returns `503 viewer_queue_timeout`; both include `Retry-After: 1`. Disconnected or timed-out waiters free queue capacity, and every completed, rejected, failed, or cancelled admitted request releases its lease. The browser UI and per-request file/resource limits remain unchanged.
- Validation: Added 30 deterministic admission and endpoint tests covering accepted route spellings under contention and after release, FIFO/capacity, no body reads before admission, the 30-second timeout boundary and capacity recovery, continued processing after admission, cancellation racing with acquisition, exception/cancellation/antiforgery rejection cleanup, unrelated endpoints and unsupported methods, and configuration defaults/overrides/rejection. The complete Release `AnalysisITC.Web.Tests` suite passed 344/344; both existing JavaScript suites passed 16/16. Controlled time and explicit signals replace timing-based sleeps. No deployed service was contacted, and no deployment was performed.

## ITC-064 - Rejected result-update preparation removes attached experiment fits

- Priority: Medium
- Status: Open; reproduced (2026-10-06).
- Location: `AnalysisITC.Core/Analysis2/AnalysisResultUpdater.cs`, `PrepareSolver`; `AnalysisITC.Core/Analysis2/ModelFactory.cs`, `GlobalModelFactory.BuildModel`.
- Problem: `PrepareSolver` calls `BuildModel`, which replaces each live experiment's `Model`, before validating required model-option attributes and initializing the solver. If validation throws, the experiment keeps the new model without a solution. Neither desktop handler restores its previous model. The stored Analysis Result survives, but the experiments' attached fits disappear after a rejected update.
- Reproduction: Load the competitive-displacement `.ftxtc` fixture, use the prebound-ligand concentration from experiment attributes, and remove that required attribute from the member experiments. A temporary probe confirmed two attached solutions before `PrepareSolver`, a `MissingModelOptionAttributesException`, and zero attached solutions afterwards; the stored result solution was unchanged.
- Follow-up: Complete preparation and validation before attaching candidate models, or restore the previous attachments on failure. Verify that missing attributes and rejected parameter settings leave both the stored result and each experiment's attached solution intact.

## ITC-065 - Avalonia Release tests assume obsolete labels and a debug-only control

- Priority: Medium
- Status: Open; reproduced (2026-10-06).
- Location: `AnalysisITC.Avalonia.Tests/AnalysisReportEnhancementTests.cs` and `LockedParameterPresentationTests.cs`.
- Problem: Two report tests reflect `extraTraceabilityCheck`, but the production field is inside `#if DEBUG`, so the documented Release test command fails with a null reflection result. Three locked-parameter tests locate a section named `Locked Parameters`, while production correctly uses sentence case, `Locked parameters`; their lookup fails before checking parameter content. These are deterministic assertion/setup failures independent of ITC-007.
- Validation: A filtered Release report/figure run completed with 58 passed and 2 failed (`TraceabilityModeLocksEffectiveCheckboxAndRestoresSavedChoice` and `ReportSettingsStayWithTheirResultAcrossAtoBtoASelectionChanges`). `LockedParameterPresentationTests` run alone completed with 1 passed and 3 failed at `LockedSection`. The separate Core report-policy failure is already tracked in ITC-056.
- Follow-up: Make debug-control coverage configuration-aware while retaining Release coverage of saved report options. Update the section lookup and expected text for sentence case, preferably identifying the section without coupling all parameter assertions to its capitalization.

## ITC-066 - Native interpretation layout test rejects the shared generation guard

- Priority: Low
- Status: Open; reproduced (2026-10-06).
- Location: `AnalysisITC.MacOS.Tests/InterpretationLayoutTests.swift`, source assertion for generation access; `AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs`, `UpdateGenerateButton`.
- Problem: The Swift test requires the literal expression `serviceAllowsGeneration && interpretationAccessAllowsGeneration` in the controller. Production now calls `InterpretationPackageSizeEstimate.CanGenerate`, passing both values plus the busy and size checks. The shared helper still requires both access conditions, so the test reports a missing guard even though it is present.
- Validation: `xcrun swift -module-cache-path /tmp/ftitc-review-swift-cache AnalysisITC.MacOS.Tests/InterpretationLayoutTests.swift` exited with the sole reported failure `generation must require both service and access checks`. The shared helper explicitly combines both conditions.
- Follow-up: Update the layout fixture's source-wiring check for the shared helper and keep behavioral generation-guard assertions in Core tests instead of requiring an inlined expression.

## ITC-067 - Injection heat direction ignores later buffer-subtraction changes

- Priority: Low
- Status: Open (2026-10-07).
- Location: `AnalysisITC.Core/DataClasses/InjectionData.cs`, `SetPeakArea` and `UpdateCorrectedPeakArea`; `AnalysisITC.Core/DataClasses/ExperimentData.cs`, `Reference_ProcessingUpdated`.
- Problem: Per-injection `HeatDirection` is set only in `SetPeakArea`, from the `PeakArea` available at that moment. Buffer-subtraction updates change `PeakArea` without recalculating it. The direction is therefore based on raw heats when a buffer is assigned after integration, on corrected heats after reintegration, always on raw heats for duplicates, and becomes stale when the reference heats change. The experiment-level heat direction is derived from these values.
- Follow-up: Decide whether heat direction should describe raw or corrected heats, then update it consistently wherever `PeakArea` changes.

## ITC-068 - Structuring evaluation-temperature options need scientific review

- Priority: Medium
- Status: Open (2026-10-07).
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`, `SRTempMode` and the entropy term in the structuring calculation; result-view temperature-mode controls on both platforms; report Structuring chapter ("Evaluated at").
- Problem: The published Spolar–Record method, and its adaptation in Theisen et al. (JACS 2021), evaluates the entropy terms at the isoentropic temperature, where ΔS = 0. FT-ITC also offers the mean experiment temperature, where the fitted parameters are arguably best determined, and the reference temperature, to compare interactions at a common temperature. These two options are not part of the published method. It is unresolved whether evaluating away from the isoentropic temperature is valid, and how its results should be interpreted or compared with published values.
- Follow-up: Review whether the isoentropic temperature is required by the method's assumptions, whether the mean and reference options are scientifically justified, and how they should be labelled, documented, or restricted.

## ITC-069 - Structuring result views scale saved entropies by the current reference temperature

- Priority: Low
- Status: Resolved (2026-10-07).
- Location: `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs` and `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs`, structuring Output section (`analysis.EvalutationTemperature(false)`); compare `AnalysisReportBuilder.AddSpolarRecord`.
- Problem: Both result views compute the displayed −TΔS_HE and −TΔS_conf by multiplying the saved entropies by the current evaluation temperature, while "Evaluated at" shows the saved temperature. In Reference temperature mode the current value is the preference, so changing the reference temperature after a run (or before reopening a project) applies the new temperature to entropies calculated at the old one. Nothing reruns the analysis when the preference changes. The report uses the saved temperature, so views and report disagree. Example: run at 25 °C, then change the preference to 37 °C; the views scale both contributions by 310.15/298.15 (about 4%) while still showing 25 °C. Residue counts are unaffected.
- Resolution: Both result views now derive the contributions from the saved evaluation temperature. Focused checks passed (Core 13/13; Avalonia 1/1), and the unsigned macOS Debug compile plus native AppKit output check passed. After changing the preference from 25 °C to 37 °C, native output remained hydration 0.5963 kJ/mol, conformational −0.89445 kJ/mol, saved temperature 25 °C, and 12 residues. The full Core suite had one baseline-confirmed unrelated failure (2,206 passed, 1 skipped); the Avalonia suite stalled after logging 26 failures and was stopped without a summary.

## ITC-070 - Structuring contribution uncertainty omits joint temperature variation

- Priority: Low
- Status: Closed, not an issue (2026-10-07).
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`, `SROutput.HydrationContribution()` and `SROutput.ConformationalContribution()`.
- Problem: The saved entropy uncertainties already include sampled isoentropic-temperature variation. Converting the entropy to −TΔS with the central saved temperature scales that entropy uncertainty but does not propagate the full joint uncertainty of temperature and entropy.
- Follow-up: Review the desired uncertainty treatment for −TΔS. Do not change it as part of ITC-069.
- Closed: central temperature does not have an uncertainty, it is simply a value.

## ITC-071 - Structuring views display saved temperature uncertainty differently

- Priority: Low
- Status: Open (2026-10-07).
- Location: `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs` and `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs`, structuring Output section.
- Problem: Avalonia displays the saved temperature uncertainty with `AsNumber()`, while native macOS displays only the central saved temperature value.
- Follow-up: Review whether both views should display the temperature uncertainty. Do not change it as part of ITC-069.
