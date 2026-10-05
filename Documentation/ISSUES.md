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
- Resolution: For **No binding detected** and **Inconclusive** standard output, the figure builder now selects the fitted Offset from the applicable comparison (independent members use their own comparison; pooled results match the experiment) and draws it through the ordinary builder as a fresh Offset model on the current experiment. Current observations, concentrations, uncertainties, axis type, and all ordinary display controls apply, including offset correction and parameter annotations. The experiment's attached model and stored comparison solutions are not changed. When no successful, uniquely matching, finite Offset fit exists, the figure shows current observations with "Offset fit unavailable" and no prediction, residuals, or parameters. The separate saved-null builder and its "Saved…/Current…" annotations were removed. Missing convergence omits RMSD instead of failing. macOS no longer forces parameter annotations and offset correction off for these figures; Avalonia result-figure exports use the displayed model for shared axes.
- Kept: Saved comparison points remain the evidence for report and export tables. The plotted Offset is the value fitted with the result; later processing edits change the plotted observations but not the Offset until the result is updated.
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
- Status: Open; decision needed.
- Location: `AnalysisITC.Core/DataClasses/BindingAssessmentState.cs` (rule `aicc-6-10-v1`) and `ResultOutputPolicy`.
- Problem: No binding detected covers every ΔAICc ≤ 6. For 0 < ΔAICc ≤ 6 AICc itself favours the binding model, by up to about e³ ≈ 20 times at ΔAICc = 6, so this band mixes weak support for binding with support for the Offset model. Binding detected only shows that the heats are not described by a constant heat per mole. Concentration-dependent dilution heat, buffer mismatch, or drift can also produce it. Because No binding detected and Inconclusive remove binding parameters from standard outputs, the labels determine what users see, not only what they read.
- Decision needed: Keep, rename, or split the lower band (for example "Not established" for 0 < ΔAICc ≤ 6). Decide whether Binding detected should state its constant-background assumption.

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
- Problem: The output-allowed outcomes are currently fixed to Binding detected and Not assessed. A future preference could make this set configurable, which would change suppression behavior across reports and exports.

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
- Status: Open.
- Location: `AnalysisReportBuilder.BuildFrontPage`.
- Problem: The front-page notice "This report contains results using different bookkeeping conventions." is raised whenever the saved fits in the report use mixed conventions, including within a single result. With one result, the notice refers to "results" although only its members differ; the result's own analysis summary already lists the member conventions.

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
