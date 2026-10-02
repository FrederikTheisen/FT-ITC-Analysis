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
- Status: Open; do not change bounds until the intended policy is agreed.
- Location: `AnalysisITC.Core/Analysis2/ParameterSet.cs`, `Parameter.RefreshLimits`.
- Problem: Affinity is stored as log₁₀K. Multiplying the standard bounds `[-2, 20]` gives `[-40, 400]` for Extended and `[-4000, 40000]` for No limit. Exponentiating the upper limits overflows; the No limit lower bound underflows. A one-site model can still predict finite heat at log₁₀K = 400 while reporting K = ∞ and ΔG = −∞.
- Decision needed: Clarify whether negative log-affinity bounds are permitted. A negative log₁₀K represents a positive K below 1 in the application's affinity units; it is not a negative equilibrium constant. Also decide what physical widening each preset should represent before choosing additive log-space bounds and checking transformed global coordinates.

## ITC-003 — pKa correction can fail to converge

- Priority: Medium
- Status: Partially mitigated; convergence handling deferred.
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs`, `BufferAttribute.optpKa`.
- Problem: Fixed-point iteration can enter a repeating cycle. The actual phosphate calculation hangs at pH 8.198, 25 °C, and concentration 5 M without a cap. This is an extreme-concentration reproduction; the tested registry grid through 1 M converged.
- Current mitigation: Stop after 1,000 iterations and return the last estimate. There is deliberately no new failure, warning, or convergence-status handling.
- Follow-up: Decide how a non-converged ionic-strength correction should be represented and whether the iteration should be replaced by a more robust calculation.

## ITC-004 — Unlocking a spline point discards a converted spline

- Priority: Medium
- Status: Open; accepted consequence of protecting converted splines with the processing lock.
- Location: Spline point context menu "Unlock" in `AnalysisITC.Avalonia/Workspace/Processing/ProcessingGraphControl.cs` and `AnalysisITC.MacOS/GraphViews/DataProcessingGraphView.cs`.
- Problem: "Unlock" clears the point's position and slope locks and then calls `ProcessData()` with `replace: true`, which regenerates every spline point from the raw data. On a spline converted from a Polynomial or Segmented baseline, unlocking one point therefore discards the whole conversion rather than releasing that point. Converted Smooth-spline points carry locked slopes, so this is also the only way to release a converted slope.
- Follow-up: Decide what unlocking a single point should do on an existing spline (for example, release the point and refresh from the current points with `replace: false`) and keep both applications aligned.

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

## ITC-008 — Null Offset values exported with the wrong unit

- Priority: High
- Status: Open.
- Location: `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs` (`AddAssessment`, Null offsets column) and `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`FormatNullOffsets`).
- Problem: The Offset is a heat per mole of injectant in J/mol. The table export prints the raw value with a "µJ" suffix, so −10536 J/mol appears as −10536 µJ. The no-binding report prints the same value with no unit and labels each member with its internal experiment ID instead of the experiment name or report label.
- Follow-up: Format the Offset as a molar energy with `Energy.ToFormattedString(..., permole: true)` in the selected energy unit. Label members by name or report label, and show internal IDs only under Traceability Mode.

## ITC-009 — Null-only figure ignores the experiment x-axis and error bars

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/PublicationFigure.cs`, `BuildSavedNullFigure`.
- Problem: The figure plots the saved `Ratio` directly under a fixed "Saved injection ratio" title. For dissociation experiments the saved value is the titrant concentration in M, while the standard figure shows µM under the axis-type title, so the axis is mislabelled and differs by 10⁶. Injection-number axes get the same wrong title, and a user-set x-axis title is ignored. Observed points are drawn with zero-width error bars, because injection SDs are not part of the saved comparison points.
- Note: The null-only figure issue also affects **Inconclusive** output.
- Follow-up: Use the same x-value and axis-title rules as the standard fit figure. Decide whether error bars should come from current injection SDs or be omitted explicitly.

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
- Status: Open.
- Location: `AnalysisITC.Core/Math/NumberStructs.cs`, `FloatWithError.WithMod` and `ConfidenceIntervalString`.
- Problem: Confidence interval endpoints are formatted with the same fixed-point format as the central estimate. When an interval endpoint is unbounded or approaches the largest finite floating-point value, it can appear as an unwieldy long number instead of a concise indication that the bound is effectively infinite. This obscures the useful interval and makes the result difficult to read.
- Follow-up: Handle non-finite and extreme finite confidence bounds explicitly, using a concise representation such as `∞` (or scientific notation where the bound is finite), while preserving ordinary interval formatting.

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
- Status: Open.
- Location: `AnalysisReportBuilder.BuildAppendix`.
- Problem: Each result chapter's appendix ends with a **Report details** block (software, application version and, in Traceability Mode, report identifier). These describe the whole report, so a report with N results repeats them N times. A report-level closing section or the front page may be a better home.

## ITC-023 — Front-page bookkeeping notice wording with one result

- Priority: Minor
- Status: Open.
- Location: `AnalysisReportBuilder.BuildFrontPage`.
- Problem: The front-page notice "This report contains results using different bookkeeping conventions." is raised whenever the saved fits in the report use mixed conventions, including within a single result. With one result, the notice refers to "results" although only its members differ; the result's own analysis summary already lists the member conventions.
