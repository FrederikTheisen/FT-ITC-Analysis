# Issue tracker

## ITC-001 — Asymmetric rounding of negative display values

- Priority: High
- Status: Open; implementation deferred.
- Location: `AnalysisITC.Core/Math/FWEMath.cs`, both `RoundApproximate` overloads.
- Problem: −2.5 rounds to −2 while +2.5 rounds to +3. The digits overload also rounds −2.6 to −2. Standard/Strict uncertainty formatting uses the one-argument overload, so the midpoint inconsistency reaches displayed values and interval endpoints.
- Follow-up: Make midpoint tolerance and rounding symmetric for both signs, preserve the requested midpoint mode, and add independent positive/negative reference cases for both overloads and formatted output.

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
- Status: Open.
- Location: Edit identifiers commands in the Tools menu and the standalone experiment identifier dialogs in the macOS and Avalonia applications.
- Problem: Experiment and sample identifiers can already be edited as part of the experiment's Details editor. The separate Edit identifiers tool and dialog duplicate that workflow and make identifier editing appear to be a standalone tool.
- Follow-up: Remove Edit identifiers from the Tools menu and direct users to edit identifiers within the selected experiment's Details. Keep identifier review as part of the import workflow.
- Constraint: Remove only the Tools menu command and its handlers. Keep the identifier dialog's single-experiment form (`ExperimentIdentifiersWindow` on Avalonia, `MacIdentifierEditor` on macOS): the import review shows it when an import adds one experiment.

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
- Follow-up: Use the same x-value and axis-title rules as the standard fit figure. Decide whether error bars should come from current injection SDs or be omitted explicitly.

## ITC-010 — Manual and older interpretations always warn "Assessment context unknown"

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`BuildInterpretationSection`) and `AnalysisITC.Core/DataClasses/AnalysisReportDefinition.cs` (`SetManualInterpretation`).
- Problem: Only AI-generated interpretation records receive `AssessmentContextFingerprint`. Manually written interpretations and every interpretation saved before this field existed have none. The report therefore shows an "Assessment context unknown" warning for all of them, even when no result in the report has a binding assessment.
- Decision needed: Record the current fingerprint when a manual interpretation is saved or approved, or show the warning only for AI-generated records. For older records, warn only when at least one report result has an assessment.

## ITC-011 — Binding-output suppression is undocumented and missing on macOS

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/ResultOutputPolicy.cs` and its callers. Report and export windows in `AnalysisITC.Avalonia/Misc/Tools/` and `AnalysisITC.MacOS/ViewControllers/`. `Documentation/UserManual/pages/08-results-advanced-analysis.md`, `09-figures-printing-export.md`, and the help resources.
- Problem: When the effective assessment is No binding detected, standard reports, table exports, clipboard copies, result figures, and interpretation packages omit binding parameters, thermodynamics, confidence bands, and advanced analyses. The manual and help text do not mention this, or the standard/diagnostic choice that restores the omitted content. Avalonia offers the choice in the report and export windows; macOS has no selector, so macOS users cannot produce diagnostic output.
- Follow-up: Document what standard output omits and how to obtain diagnostic output. Add the matching selector to the macOS report and export windows.

## ITC-012 — Suppression applies to results, not to the experiments they contain

- Priority: Medium
- Status: Open; decision needed.
- Location: `ResultOutputPolicy.SuppressBindingOutputs` callers. `PublicationFigureCanvas.Expand` and `PublicationFigureBuilder.Build(ExperimentData, ...)` build figures from an experiment's own solution without a result.
- Problem: A figure or copy made from an experiment, rather than from its Analysis Result, still shows the binding fit and fitted parameters after the result is assessed No binding detected. The same data can therefore produce suppressed or full output depending on the entry point.
- Decision needed: Decide whether the assessment should govern experiment-level outputs whose solution belongs to an assessed result, or whether experiment outputs are deliberately unaffected. If unaffected, say so where the assessment is documented.

## ITC-013 — Full and diagnostic reports do not state the binding assessment

- Priority: Medium
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs`. The assessment is only rendered in `BuildNoBindingSections`.
- Problem: Reports for Binding detected, Inconclusive, and Not assessed results contain no null hypothesis test section. Inconclusive results are reported with full binding parameters and no caveat. A diagnostic report of a No binding detected result shows full parameters, and the only indication of the assessment is the "Diagnostic output" label on each fit figure. The table export, by contrast, adds the assessment columns for every result.
- Follow-up: Add the assessment summary (conclusion, mode, ΔAICc, reason) to every result chapter. In diagnostic output, state the effective assessment next to the binding parameters.

## ITC-014 — Assessment labels now drive which parameters are hidden

- Priority: Medium
- Status: Open; decision needed.
- Location: `AnalysisITC.Core/DataClasses/BindingAssessmentState.cs` (rule `aicc-6-10-v1`) and `ResultOutputPolicy`.
- Problem: No binding detected covers every ΔAICc ≤ 6. For 0 < ΔAICc ≤ 6 AICc itself favours the binding model, by up to about e³ ≈ 20 times at ΔAICc = 6, so this band mixes weak support for binding with support for the Offset model. Binding detected only shows that the heats are not described by a constant heat per mole. Concentration-dependent dilution heat, buffer mismatch, or drift can also produce it. Because No binding detected now removes binding parameters from standard outputs, the label determines what users see, not only what they read.
- Decision needed: Keep, rename, or split the lower band (for example "Not established" for 0 < ΔAICc ≤ 6). Decide whether Binding detected should state its constant-background assumption.

## ITC-015 — Minor null-model output formatting

- Priority: Low
- Status: Open.
- Location: `AnalysisReportBuilder.BuildNoBindingSections`, `NullModelComparisonPresentation`, `AnalysisResultTableExporter`, `AnalysisReportWindow`.
- Problem:
  - An unavailable null RMSD in the no-binding report reads "Unavailable µJ".
  - AICc, ΔAICc, and null RMSD in CSV/TSV exports use the current culture's number format, while temperatures in the same file are culture-invariant.
  - The first header cell of every table export now starts with "Standard" or "Diagnostic", which changes the header for scripts that read existing exports.
  - The no-binding report omits each experiment's conditions table (concentrations, temperature, identifiers).
  - The report purpose is not saved with report presentation settings and reopens as Standard. This may be intentional.
- Follow-up: Append units only to numeric values, use invariant formatting in file exports, and confirm the intended header and persistence behaviour.
