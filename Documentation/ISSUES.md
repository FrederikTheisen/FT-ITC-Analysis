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

## ITC-012 — Suppression applies to results, not to the experiments they contain

- Priority: Medium
- Status: Open; decision needed.
- Location: `ResultOutputPolicy.SuppressBindingOutputs` callers. `PublicationFigureCanvas.Expand` and `PublicationFigureBuilder.Build(ExperimentData, ...)` build figures from an experiment's own solution without a result.
- Problem: A figure or copy made from an experiment, rather than from its Analysis Result, still shows the binding fit and fitted parameters after the result is assessed No binding detected. The same data can therefore produce suppressed or full output depending on the entry point.
- Decision needed: Decide whether the assessment should govern experiment-level outputs whose solution belongs to an assessed result, or whether experiment outputs are deliberately unaffected. If unaffected, say so where the assessment is documented.

## ITC-013 — Full and diagnostic reports do not state the binding assessment

- Status: Resolved; superseded.
- Problem: Full reports did not consistently state the effective binding assessment; Diagnostic attempted-fit output needed assessment context.
- Resolution: The 2026-10-02 policy added front-page assessments and result-overview assessments where suppression affected output, with full comparison evidence in Diagnostic reports. This resolution is superseded by the later assessment, member-summary, and provenance rules in ITC-014, ITC-073, and ITC-051.

## ITC-014 — Assessment labels now drive which parameters are hidden

- Status: Resolved (2026-10-05).
- Problem: Assessment cutoffs and their output restrictions needed a consistent policy, especially for Inconclusive results and manual overrides.
- Resolution: Rule `aicc-0-10-v1` assigns No binding detected at signed ΔAICc ≤ 0, Inconclusive at 0 < ΔAICc < 10, and Binding detected at ΔAICc ≥ 10. Saved outcomes/rule IDs restore unchanged. Inconclusive retains binding output/uncertainty and adds a health warning without changing input validity; manual overrides are authoritative. Combined binding output requires every member to be eligible, without subset recombination. These chosen cutoffs have no calibrated false-positive guarantee and do not exclude varying dilution heat, buffer mismatch, or drift. Later Standard report suppression is recorded in ITC-051; Standard table exports omit no-binding values.

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

- Status: Resolved (2026-10-05).
- Problem: The Analysis inspector combined null RMSD/ΔAICc and placed fit RMSD on the model heading, obscuring the test and fit scope.
- Resolution: Both Analysis inspectors render shared `NullModelComparisonPresentation.AnalysisInspectorRows` for `Model`, Null RMSD, ΔAICc, and Conclusion. macOS adds a separator/bold header; its fit summary shows the readable model and Global/Individual scope, with RMSD first and values right-aligned. Results-tab null sections and graph parameter boxes are unchanged.

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

- Status: Resolved (2026-10-08).
- Problem: Reports showed baseline/integration details for imported integrated heats without a thermogram, implying processing had been performed.
- Resolution: `AnalysisReportBuilder` selects the block by thermogram availability: retain Processing and integration with a thermogram; otherwise show Data availability, Imported integrated heats, and injection use, omitting baseline/integration status and regions/times. Partial availability counts only integrated injections with finite current heats. Supporting experiments follow the same rule while retaining correction/reference notes; condensed repeats omit the block. Core and both renderers were checked. Bookkeeping-notice placement remains ITC-037.

## ITC-037 — Bookkeeping convention displayed in a large appendix notice

- Priority: Low
- Status: Open.
- Location: `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs` (`AddReportAppendix`, `BuildExperimentMetadata`, `BuildProcessingItems`).
- Problem: The report presents saved concentration and injection-heat bookkeeping methods together in a large boxed notice in the appendix, away from the experiments they describe. This gives routine method metadata the visual weight of a warning and makes it harder to connect each method to its experiment.
- Suggestion: Show each experiment's saved bookkeeping method with its processing information, and remove the routine appendix notice. Reserve boxed notices for cautions or for expanded explanations when that option is enabled (related: ITC-024). If an analysis result contains experiments with different bookkeeping, this should result in a warning/caution level box (either red or orange). 

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
- Status: Open
- Problem: Currently we have a poor idea of where boxes might be placed.
- Suggestion: Identify places where boxes can be placed, what they contain, and what triggers them. In general, boxes should not show up for no reason. Boxes should be reserved for specific of note items that the reader needs to know about the presented data. This could be different bookkeeping conventions, ... exploration necessary.

## ITC-041 - Source file and source format in one line for analysis reports

- Status: Resolved (2026-10-06).
- Problem: Report source filename and recorded source format needed to appear together for each experiment.
- Resolution: Full, condensed, and supporting experiment details combine both under Source file, with placeholders for missing metadata. Both apps wrap complete values; the appendix filename column is unchanged.

## ITC-042 - Competitor properties affinity and enthalpy values are not showing up

- Priority: High
- Status: Deferred
- Problem: report test project competitor attributes do no display values. They appear to be saved/loaded as NaN or non finite.

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
- Status: Open
- Problem: Parameters of an experiment assessed as no binding are not expected to be meaningful, yet bootstrap/LOO/profile uncertainty is still estimated for them, which costs time and produces warnings that ITC-050 now hides from result health.
- Open questions: The assessment is only known after the binding and null fits, and it can be changed manually afterwards. Decide what happens when a manual override switches a result to binding (estimate on demand, require Update Result, or mark uncertainty unavailable), and how Update Result should behave.
- Thoughts: This is mostly to optimise non-converging very long running non-binding analyses error estimation attempts. Manual assessments should not be overwritten on fitting completion. Manual assessment change from non-binding to binding should mark the results as stale. 

## ITC-053 - Non-binding members still show fit warnings outside result health

- Priority: Low
- Status: Partially resolved (2026-10-08): Standard reports omit these warnings from report warnings (P03); Diagnostic reports keep them. Result views, the status bar and web viewer warnings remain open.
- Problem: After ITC-050, fit warnings from members assessed as no binding no longer affect result health, but they still appear in each experiment's row in the macOS/Avalonia result views, in the status bar after a refit (combined convergence boundary contacts), in web viewer per-fit warnings, and in report per-experiment diagnostics.
- Suggestion: Decide whether these should stay, be demoted, or be annotated as not counted. Use `BindingAssessmentInterpretation` for any change.

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
- Status: Open; decision needed.
- Location: `AnalysisITC.Core/DataProcessing.cs` (`AssymetricLeastSquaresInterpolator`).
- Problem: `Interpolate` computes the ALS curve but never assigns `Baseline` (the assignment is commented out). Re-running interpolation therefore keeps whatever baseline was stored, and the Iterations, Lambda and Asymmetry settings have no effect. The run is expensive: the second-difference matrix is built from dense rows, which is O(n²) in the number of thermogram samples. `Copy` is not overridden, so a copied processor becomes a plain `BaselineInterpolator` and loses the ALS settings. ALS cannot be selected in either app, but `.ftxtc` (`"asl"`) and legacy files can still restore it.
- Suggestion: Decide whether to remove ALS (map restored ALS processors to a supported type while keeping the stored baseline) or finish the implementation.

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

- Status: Resolved (2026-10-07).
- Problem: Both structuring result views multiplied saved entropies by the current reference-temperature preference while labeling the saved temperature. Changing 25 °C to 37 °C shifted −TΔS contributions by about 4% and disagreed with reports; residue counts were unaffected.
- Resolution: Both views use the saved evaluation temperature for hydration/conformational contributions, matching AnalysisReportBuilder.AddSpolarRecord. Focused Core/Avalonia and native output checks confirmed preference changes leave saved contributions/temperature unchanged. Other temperature-mode and uncertainty questions remain ITC-068, ITC-070, and ITC-071.

## ITC-070 - Structuring contribution uncertainty omits joint temperature variation

- Status: Closed, not an issue (2026-10-07).
- Problem: Raised whether `SROutput.HydrationContribution/ConformationalContribution` should propagate joint temperature/entropy variation when scaling saved entropy uncertainty by the central saved temperature.
- Resolution: Closed as not an issue: the central evaluation temperature is treated as a fixed value without uncertainty. The existing entropy-uncertainty scaling is retained; this concern was excluded from ITC-069.

## ITC-071 - Structuring views display saved temperature uncertainty differently

- Priority: Low
- Status: Open (2026-10-07).
- Location: `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs` and `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs`, structuring Output section.
- Problem: Avalonia displays the saved temperature uncertainty with `AsNumber()`, while native macOS displays only the central saved temperature value.
- Follow-up: Review whether both views should display the temperature uncertainty. Do not change it as part of ITC-069.

## ITC-072 - Structuring with an explicit polar-hydration entropy term

- Priority: Medium
- Status: Open; research option (2026-10-07). Related to ITC-068, which is pursuing the reference-temperature method.
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`; per-residue calibration constant; evidence in `Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/`.
- Problem: The Spolar–Record method assigns c·ΔCp to hydrophobic hydration and leaves the polar share, (1 − c)·ΔCp, in the conformational remainder. The remainder therefore contains polar hydration entropy, which changes with temperature, so residue counts depend on the evaluation temperature. Both folding calibration sets (12 and 46 proteins) show this: per-residue values at each protein's own T_S trend with T_S (r = 0.79), and the spread is smallest near 333–342 K, close to the literature polar convergence temperature of 335 K (D'Aquino et al. 1996).
- Option: Model polar hydration explicitly, ΔS_polar(T) = (1 − c)·ΔCp·ln(T/T_p*), with T_p* ≈ 335 K. Then ΔS_conf = −ΔCp·ln(T_S/T_eff) − ΔS_rt with T_eff = 386^c·T_p*^(1−c) (405 K folded–folded, 424 K folded–disordered) and a calibration constant of about −20.2 J/(mol·K) per residue. The residue count no longer depends on an evaluation temperature, and energy contributions can be reported at any temperature as four terms that sum to −TΔS.
- Evidence so far: non-inferior to the T_S convention on 15 structural benchmarks (RMS log error 0.34 vs 0.36), lowest mean bias, and the lowest method uncertainty (12.8% vs 19.5% for DREB2A 243–272). It is about half as sensitive to the polar/nonpolar surface ratio, which shrinks the folded–disordered correction (DREB2A 243–272: 29.2 vs 32.2 published).
- Open questions: T_p* is a new parameter (bootstrap range 308–359 K); published R_th,ID values would change by about 10%; the validation set is small.

## ITC-073 - Mixed member assessments reported as a uniform collection verdict

- Status: Resolved (2026-10-07).
- Problem: A precedence-based collection verdict labeled mixed binding/no-binding members No binding detected in reports, exports, clipboard output, and interpretation requests, despite desktop views showing Mixed assessments.
- Resolution: A shared collection-summary type reports the common effective outcome only when all independent members agree; any difference, including Inconclusive/Not assessed and overrides, becomes Mixed assessments under Member assessments. Mixed is not a selectable/persisted individual verdict. Shared presentation feeds both apps, reports/exports/clipboard, and separate effective/automatic AI summaries; relay guidance preserves member authority for new and legacy summaries. Single/pooled assessments and output eligibility are unchanged. Outcome-pair, override, persistence, presentation, interpretation, and renderer checks cover the policy.

## ITC-074 - Saved Analysis Results are not reproducible in isolation

- Status: Resolved (2026-10-08); the related ID-remap gap is tracked as ITC-077.
- Problem: Result Save Selected omitted Analysis Results referenced through `CompetitorResult` attributes, their members, and nested dependencies. Cached Kd/ΔH allowed refitting, but the source could not be inspected or re-evaluated from the file alone.
- Resolution: `ProjectWriter.SaveSelectedAsync` recursively writes loaded competitor source results, their members, and loaded buffer references, applying ITC-062 at every level. Each result is written once to end cycles; unloaded sources retain cached Kd/ΔH fallback. Save the actual source rather than a name/value snapshot. Single-experiment saves remain one experiment; no format change. `CompetitorSourceSaveSelectedTests` covers isolated strict reopening/link resolution, nesting/cycles, unloaded sources, and single-experiment scope. Reopening with colliding result/solution IDs still needs the separate ITC-077 remapping fix.

## ITC-075 - Null model nomenclature and compatibility with alternative dilution-heat models

- Priority: Medium
- Status: Open; approved revision 3 corrections implemented (2026-10-08). The initial inventory below is retained as the audit record; implementation and deferred boundaries are recorded here.
- Problem: Offset is the current Null model, but Null describes a no-binding model role. Future linear or exponential dilution-heat models could fill that role and also supply dilution compensation in binding models. Several general Null paths instead assume the name, one parameter, equation, or persistence identity of Offset. A wording cleanup alone would leave substantive compatibility barriers.
- Initial inventory scope: Static review of the current working tree across Core, Avalonia, native macOS, Web/AI guidance, manual/help resources, native project schemas, and relevant tests. Existing unrelated working-tree changes were retained. Build output, vendor code, binary screenshots, and historical investigation outputs were not treated as authoritative application text. Locations below use repository-relative paths and line numbers from the scan; symbol names identify the same sites after lines move.
- Related issues: ITC-008 concerns null-offset export labels; ITC-009 describes the current Offset-only figure implementation; ITC-018 covers the separate question of global Null parameter sharing; ITC-025 concerns pooled comparison diagnostics. This inventory does not resolve or expand those issues.

### Approved corrections (revision 3)

- N01–N07: general wording distinguishes the no-binding Null role from the Offset model and parameter. Shared labels derive from saved identity (`Null (Offset)`, not calculated, or model unknown), including both desktop collection views. Reports, tables and clipboard exports name Null parameters from matching saved solutions and use appropriate units; scalar fallback requires confirmed Offset evidence. Current scientific guidance 3.7.3 and summary 2.2 permit both binding and Null model terminology; older variants remain unchanged.
- N06: AI evidence includes actual identity and named parameters on independent and Null-member records. Values and constraints come from the Null solution; uncertainty is omitted unless computed, including for rebuilt native snapshots. The legacy Offset field is guarded by identity. Evidence schema is 2.2, retaining acceptance of 2.0 and 2.1 and the compact input path. **Deployment dependency:** MIST must accept 2.2 before a client sending 2.2 is released. No deployment is part of this pass.
- N08/N08a/N09/N12/N13: generic evidence has no implicit identity. Calculator and Offset-only restore paths assign it explicitly. Empty identity is caught at capture and saved as a readable status-only Offset-schema record with reason “Saved Null model identity was missing.” Explicit `Model.IsNullModel` prevents recursive comparisons; the current Offset strategy, diagnostics about its coordinate, and native wire shape are retained.
- N14: publication figures draw exactly one saved Null solution matching the experiment, registered model identity, and current injection IDs. Missing, mismatched or ambiguous fits show the role-and-model unavailable label. Null figures ignore offset correction and show uncorrected integrated heats; residuals use observed minus Null prediction and processing error bars retain their meaning. Binding figures retain their controls and names. Scalar reconstruction is removed.
- N15: focused presentation, export, AI, save/restore, Null-role and saved-solution figure contract coverage accompanies these changes. These presentation contracts do not claim forward-model validation. Validation: Core suite 2,312 passed / 1 existing skip; Web suite 347 passed; 72 relevant Avalonia tests passed in isolated class runs; native managed/CoreGraphics and AppKit inspector layout checks passed. Standard and diagnostic Skia report pages were rendered and inspected. The full Avalonia run encountered UI-thread ownership failures and stalled; it was stopped before isolated checks. No packaging, signing, deployment or push was performed.
- Remains deferred: N10/N11 native schema expansion and generic native restoration; general Null fitting strategies; new linear/exponential dilution-heat laws; model selection and background composition (A01/A03); sharing policy (ITC-018); and broader live-graph, export and bootstrap correction (A02). Concentration bookkeeping (A04), equations, assessment thresholds and app versions are unchanged. ITC-075 stays open.

### Terminology and presentation findings

| ID | Locations | Finding and consequence |
| --- | --- | --- |
| N01 | `Documentation/UserManual/pages/08-results-advanced-analysis.md:101,103,105,119`; `Resources/HelpTextResource.txt:85`; `Resources/ScienceHelpResource.txt:81` | The manual defines the titration null hypothesis as constant background heat, and Help defines the general Null model by that equation. This makes a specific background law part of the concept. The manual's "Null model value" also assumes a scalar. Science Help partly qualifies Offset as current, but equates a hypothesis with a model. The statement that the current comparison uses Offset, and its equation, are accurate implementation descriptions; retain that distinction when reviewing wording. |
| N02 | `AnalysisITC.Core/Presentation/NullModelComparisonPresentation.cs:28,45,132,138` (`RuleExplanation`, `NullModel`, `NullFitReason`, `NullRmsdReason`) | The shared presenter ignores `NullModelId` and always returns Offset, including "Offset (not calculated)", "Offset fitted per experiment", and "Offset (failed)". Success/RMSD explanations and the general assessment rule also name Offset unconditionally. Another Null model would be mislabeled throughout both apps, reports, exports, and AI evidence. |
| N03 | `AnalysisITC.Avalonia/Workspace/Results/AnalysisResultWorkspaceControl.cs:843,1122`; `AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs:802,1630` | Both desktop result views separately hardcode "Offset fitted per experiment" and "saved Offset comparison" in general Null sections. These bypass the shared presenter, so correcting shared wording would not cover them. |
| N04 | `AnalysisITC.Core/Presentation/AnalysisReportBuilder.cs:721,744,811,827,987` (`FormatNullFit`, `FormatNullOffsets`, saved injection table, `ComparisonReasonForReport`) | Generic Null fit summaries consist of scalar offsets plus RMSD, with "Null offsets", "Offset prediction", and "Saved binding and Offset comparison" labels. Saved prediction values themselves are generic; the table would misname an alternative model's predictions. Parameter summaries cannot describe that model's additional parameters or units. |
| N05 | `AnalysisITC.Core/DataExport/AnalysisResultTableExporter.cs:314,505,520,545,564`; `AnalysisITC.Core/DataExport/Exporter.cs:781,834,847` | Result tables, pooled diagnostic statuses, and copy/export summaries assume "Null offsets", "Offset fit", and "Offset AICc". Both formatters read `member.Offset` and always format it as energy per mole. Generic Null evidence would acquire Offset labels and an inappropriate one-parameter representation. |
| N06 | `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackage.cs:136,160,184`; `AnalysisITC.Core/Interpretation/AnalysisInterpretationPackageBuilder.cs:277,490,533,557` | The AI data boundary explicitly tells the interpreter to use "saved Offset predictions" as general null evidence. The top-level Null model name comes from the Offset-only presenter; independent member evidence has no Null model identity, and both member evidence shapes expose only `OffsetJoulesPerMole` for parameters. Generic captured observations/predictions exist, but model identity, model-specific parameters, and background-law context are not independently conveyed. |
| N07 | `AnalysisITC.Web/ScientificInstructions/itc-scientific-guidance-3.7.3.txt:13`; `itc-summary-guidance-2.2.txt:13`; related guidance variants | "Reserve model for the binding model" conflicts with the Null model concept. The same restriction occurs in scientific guidance 3.4, 3.5, 3.6, 3.6.1–3.6.4, 3.7.0 experiment-design, 3.7.1, 3.7.2, and 3.7.0 structured, plus older stored guidance and summary 2.0/2.1. `ScientificGuidance.Variants` includes the listed scientific variants, and summary guidance 2.2 is current. This is an adjacent terminology restriction, not a direct claim that Null equals Offset. |

### Model and persistence findings

| ID | Locations | Finding and consequence |
| --- | --- | --- |
| N08 | `AnalysisITC.Core/DataClasses/NullModelComparison.cs:7,10,21,28` | The generic evidence class is described as an automatic Offset comparison, implicitly defaults its identity to `"offset"`, and represents every member with one required `double Offset`. `NullSolutions` can hold generic solutions, but its comment calls them ordinary Offset objects and incorrectly says they are omitted after restoration; the reader now rebuilds them. The implicit default can conceal missing identity in future general-purpose construction. |
| N09 | `AnalysisITC.Core/Analysis2/NullModelComparisonCalculator.cs:58,103,77,143,219,232,244,298,327` | Both comparison overloads always call `CreateOffset`, release the Offset coordinate, and extract `ParameterType.Offset` in `FillMember`. The primary-model exclusion recognizes only `AnalysisModel.Offset`, rather than a Null role. Failure messages name Offset throughout. `UnreachableOffsetLimits` is explicitly derived for the constant Offset equation and must remain specific to it; it does not establish bounds for arbitrary dilution-heat models. This is the current implementation, not evidence that the concept must be Offset. |
| N10 | `Documentation/Schemas/FTXTC/component.schema.json:190,192` (`nullComparisonMember`, `result.nullComparison`) | The comparison schema fixes `nullModelId` to `"offset"`, requires `offsetParameter`, and disallows additional properties. It cannot validate a different Null identity or a model-specific parameter collection. These are actual schema-1 restrictions, so any later extension must preserve readability of existing `.ftxtc` evidence rather than rename existing wire IDs. |
| N11 | `AnalysisITC.Core/DataExport/FTXTCFormat.cs:639,1752,1780` (`FtxtcNullComparisonMemberState`, `CaptureNullComparisonElement`, `CaptureNullComparison`) | The writer can capture an ordinary solution and copies the top-level `NullModelId`, but always emits a separate Offset parameter and rewrites that parameter to `member.Offset` with its lock cleared. The serialization-failure fallback invents `"offset"` when identity is missing. Capturing a generic solution alone therefore does not make the surrounding record generic. |
| N12 | `AnalysisITC.Core/DataReaders/FTXTCReader.cs:1079,1117,1125,1158,1194,1226,1251,1275` (`RestoreNullComparison`, `RestoreIndependentNullComparison`, `RestoreOffsetSolution`) | Both read paths reject any Null identity other than Offset; each member must contain an Offset parameter and any saved solution must be an Offset solution. Restoration then constructs a new Offset from captured masses/heats instead of restoring the actual model and all its parameters. Detached input uses synthetic syringe concentration 1, cell concentration 0, and cell volume 1. Models depending on real concentrations or displacement history would need sufficient saved input context. Independent recovery also hardcodes Offset identities and failure messages. |
| N13 | `AnalysisITC.Core/DataExport/FTXTCFormat.cs:588`; `AnalysisITC.Core/DataReaders/FTXTCReader.cs:980,1025` (`FtxtcMemberAssessmentState`, independent assessment restoration/validation) | Independent member assessment records have no Null model identity. Their reconstructed `NullModelComparison` objects silently inherit the default Offset identity, even when observations and solution objects are copied from the pooled snapshot. Merely relaxing the top-level identity check would still lose the actual identity on these paths. |
| N14 | `AnalysisITC.Core/Presentation/PublicationFigure.cs:645,689,701,737` (`TryResolveNullFit`, `CreateNullDisplaySolution`) | Standard no-binding figures require a finite `member.Offset`, select only `AnalysisModel.Offset` solutions, and always construct `new Offset(data)`. The comparison's `NullModelId` is not consulted. An alternative saved Null solution could be ignored and replaced with a constant Offset curve, rather than just mislabeled. Unavailability text also says "Offset fit unavailable". This shared builder affects both desktop apps and report figures. |
| N15 | `AnalysisITC.Core.Tests/NullModelComparisonPresentationTests.cs:28,54,63`; `NullComparisonWorkflowTests.cs:180`; `NullComparisonPersistenceTests.cs:64,74,152`; `ClassifiedNullFigureTests.cs`; `NullComparisonDatasetTests.cs:192`; `ClassifiedOutputEvidenceTests.cs:142,160`; `AnalysisITC.MacOS.Tests/AnalysisNullInspectorLayout.swift:81` | General comparison/presentation/persistence/figure coverage is built around the current Offset implementation, including absent-comparison labels and equivalence with ordinary Offset figures. Those checks are valid for today's Offset cases, but provide no evidence that generic labeling, model identity restoration, or classified figures support an alternative. Keep explicit Offset mathematics tests distinct from future tests of the general Null role. |

### Adjacent dilution-compensation dependencies

These are accurate descriptions or implementations of today's constant Offset, not current mathematical defects. They need review when alternatives are designed, because the request includes dilution compensation in binding models as well as standalone Null fits.

- **A01 — Background composition:** `AnalysisITC.Core/Analysis2/Models/OneSetOfSites.cs:35,50`, `TwoSetsOfSites.cs:65,84`, `CompetitiveBinding.cs:40,138`, `SequentialBindingSites.cs:71,146`, and `Dissociation.cs:43,54` all construct a single Offset coordinate and add Offset × injected amount. There is no selectable alternative background law in these paths. Model-specific "Offset" annotations remain correct for that coordinate.
- **A02 — Corrected heats, predictions, and uncertainty:** `AnalysisITC.Core/Analysis2/Models/Models.cs:40,165,180,185,542` exposes `withoffset` evaluation, a scalar `SolutionInterface.Offset`, scalar initial-background guesses, and bootstrap correction by subtracting that scalar. `AnalysisITC.Core/DataClasses/InjectionData.cs:65` does the same for corrected observations. `PublicationFigure.cs:943`, Avalonia `IntegratedHeatsGraphControl.cs:847,1014`, and macOS `Drawing/CGGraph.cs:2333,2487` also subtract one scalar; `DataExport/Exporter.cs:479,918` uses those corrected values. A varying dilution-heat law would need consistent correction of observations, predictions, overlays, and uncertainty. Changing these calculations is outside this audit.
- **A03 — Configuration and model registration:** `AnalysisITC.Core/Analysis2/AnalysisBuilder.cs:110,294,430,462`, `ModelFactory.cs:131,346,509`, `Models/ModelOptionCatalog.cs:24`, `Models/AnalysisModel.cs:19,44`, `Models/Models.cs:653`, and `DataExport/FTXTCPersistenceRegistry.cs:84,241,249` register or special-case Offset and its one global background coordinate. These are future integration sites, not instructions to expose Offset in the ordinary binding-model selector or rename it. The current definition of the Offset model and parameter is correct.
- **A04 — Distinct meanings of dilution:** Existing MicroCal/exponential concentration bookkeeping and finite-injection heat bookkeeping are separate from the proposed exponential dilution-heat model. See `Documentation/UserManual/pages/06-fitting-models.md:61`, `Resources/ScienceHelpResource.txt:71`, and `AnalysisITC.Core/Analysis2/Models/Models.cs` concentration/heat-content helpers. Dissociation also models a physical equilibrium process despite being described as a dilution model. Do not classify it as a no-binding Null model merely because its name or description mentions dilution.

### Existing foundations and follow-up boundaries

- `BindingAssessmentState.FromComparison` (`AnalysisITC.Core/DataClasses/BindingAssessmentState.cs:69`) already compares generic saved criteria without requiring Offset. `NullModelComparisonPoint` also stores generic observed/predicted heats. The AICc sign, rule cutoffs, best-fit estimates, and distinction between weighted objectives and unweighted RMSD are not nomenclature findings.
- Both live graph overlay paths already evaluate the supplied ordinary model and fall back to captured predictions: Avalonia `IntegratedHeatsGraphControl.cs:850` and macOS `Drawing/CGGraph.cs:2334`. Nonconstant Null rendering is explicitly covered by `AnalysisITC.Avalonia.Tests/IntegratedHeatsGraphControlTests.cs:132` and `AnalysisITC.MacOS.Tests/AnalysisNullGraphTests.cs:146`. Their remaining scalar correction is A02; their prediction rendering should not be mistaken for the Offset-only publication-figure path N14.
- `Documentation/ANALYSIS_REPORT_MAP.md:75,76,88,103,200,201,280` reflects current Offset-only summaries and figures. Its implementation map will need reconciliation with any later changes; historical issue resolutions and archived guidance should not be rewritten indiscriminately.
- Continue review using N01–N15 and A01–A04 as stable sub-item IDs. The inventory predates the approved corrections above. Deferred decisions include how the Null/background law is selected, how it relates to the binding model's dilution compensation, what inputs and parameters must be saved, and how sharing is handled under ITC-018. No linear/exponential equations, model-selection policy, or cutoff changes are assumed here.
- Agent guidance now records the role/model distinction in root `AGENTS.md` and `CLAUDE.md`. No runtime tests were run for this audit; production code, user-facing text, scientific guidance, schemas, and tests were inspected only.

## ITC-076 - Expose and save the report assessment-provenance option

- Priority: Low
- Status: Open; idea (2026-10-08).
- Location: `AnalysisReportOptions.ShowAssessmentProvenance`; report windows on both platforms; `.ftxtc` report presentation settings.
- Problem: Since ITC-051, "(manual)" marks overridden assessments in report text only when this option is on or Traceability Mode is active; experiment fit details always show it. The option is neither user exposed nor saved with report presentation settings.
- Follow-up: If needed, add a report-window control on both platforms (tooltip text in Core) and an optional presentation-settings field, following `FTXTC_FORMAT.md`.

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
- Remaining difference: native "Simulate noise" still rebuilds the experiment, whereas Avalonia only regenerates noise; with retention this no longer changes parameters. The designer's option edits still write to the global `ModelFactory.PreviousAttributes` (ITC-082).
- Validation: 10 Core `ExperimentDesignerStateTests` cover setup reconstruction, defaults, model switch and return, option precedence over global stored options, option-added parameters, fitted values being replaced, forgotten values, clamping, failed reconstruction and independent windows. 19 Avalonia `ExperimentDesignerRetentionTests` cover all ten setup triggers (including model switch and return) in One-Set-Of-Sites, injection-count changes keeping every parameter in all five designer models, unchanged Enter commits resampling noise without changing parameters, entry through a parameter row, the sequential site-count option rebuilding rows, and fit locking, guarded handlers, unrelated-solver isolation and restored enabled states; all 24 designer tests pass. Native `ExperimentDesignerTests` (AppKit, code-created outlets, run via `run-analysis-null-graph-tests.sh`) pass for retention with automatic simulation on and off, control rebinding, field-text display, model switching, fit locking/restoration and fitted values not being reused. The earlier note that the Avalonia test project failed to compile in `FtxtcDuplicatePromptTests.cs` is obsolete; it compiles. Native runtime behavior in the full app was not exercised.

## ITC-079 - Avalonia tool windows write micro units as "u"

- Priority: Low
- Status: Resolved (2026-10-08).
- Problem: Experiment Designer, Tandem Merger, Experiment Details, and the tandem import dialogs wrote "uM"/"uL", while concentrations formatted through `AsFormattedConcentration` in the same windows show "µM"/"mM", so screenshots of adjacent tools showed two unit spellings.
- Resolution: Labels and generated summaries in Avalonia `ExperimentDesignerWindow`, `TandemMergerWindow`, `ExperimentDetailsWindow`, and `AvaloniaTandemImportPromptService`, and in macOS `MacTandemImportPromptService`, use µM/µL (micro sign U+00B5). Deliberately unchanged: `TandemMixingScan.cs` scan log volume text, `IntegratedHeatReader.cs` log lines, `ExportType.cs` export format descriptions, `Exporter.cs` `#EXPINFO … uM` headers read by external tools, and the `dead_volume_uL` key. Native macOS rendering was not inspected.

## ITC-080 - Result dates and experiment dates use different formats

- Priority: High
- Status: Open (2026-10-08). Implementation complete; awaiting user confirmation before resolution.
- Location: `AnalysisITC.Avalonia/Misc/Tools/AnalysisResultExporterWindow.cs:128` (`result.Date:g`); `AnalysisITC.Avalonia/Misc/Tools/AnalysisReportWindow.cs:670, 682, 693` (`ToString("g")` / `ToString("d")`); compare `AnalysisITC.Core/DataClasses/ITCDataContainer.cs`, `GetShortDateString` (used via `UIShortDateWithTime` in the Tandem Merger and Buffer Subtraction windows).
- Problem: Experiment dates are formatted with the culture from `AppSettings.Locale`, while analysis-result dates use the process culture's general format. On the screenshot machine this gave "08.04.2025 08.24" in the Merger and Buffer Subtraction windows but "9/8/2026 11:10 AM" in the Analysis Result Exporter.
- Implementation (2026-10-08): Added shared `LocalDateTimeFormatter`, extending the existing `ITCDataContainer.GetShortDateString` approach of using `AppSettings.Locale` independently of the numeric process culture. Both desktop apps now use it for result exporters, active report-picker descriptions, recovery notices, and cached account timestamps; Core Preferences account expiry/reset dates and Avalonia report-window account dates share its short-date format. Experiment date properties preserve local separators; long-date names follow the regional locale. Buffer Subtraction details use short date/time in both apps; the native reference selector uses local short time. Invalid/empty locale falls back to `CurrentUICulture`, then invariant culture; the Avalonia date-field parser uses the same resolver.
- Export boundaries: Existing report/PDF timestamp formats, figure ISO dates, and serialization remain unchanged. Native validity experiment-list dates are UI-only. Unused native `ResultTitle` and Avalonia `ResultCell` helpers remain unchanged.
- Known macOS limitation: `AppSettings.Locale` comes from `NSLocale.CurrentLocale.CollatorIdentifier`, which follows the language list rather than the Region setting and ignores custom date formats from System Settings. Locale acquisition is unchanged in this task.
- Validation: Culture regressions cover da-DK, en-GB, en-US, and sv-SE with whitespace-normalized/component expectations, invalid/empty-locale fallback, regional long-date names, account dates, unchanged report timestamps, and editable-date round trips. All 49 focused Core regressions, 80 relevant Avalonia tests, and native managed compilation/report-reference checks (including PDF rendering) pass. The full Core run has one failure in `ExperimentDesignerStateTests.OptionsThatAddParametersKeepUserValuesAndDefaultTheNewOnes` (unexpected third-site parameters at its initial factory assertion); that test passes in isolation, indicating a suite-state dependency outside this date change. Full Core run: 2,356 passed, one failed, one skipped.
- Follow-up: User confirmation is required before marking ITC-080 resolved.

## ITC-081 - Buffer Subtraction graph uses non-round axis tick values

- Priority: Low
- Status: Open (2026-10-08). Seen in the JORS supporting-figure screenshots.
- Location: `AnalysisITC.Avalonia/Misc/Tools/BufferSubtractionGraphControl.cs`, tick loop at line 144 and `Format` at line 333; padded range from `BuildDataRange`.
- Problem: Axis labels are placed at the quarter points of the padded data range and printed with `G3`, so they fall on arbitrary values (e.g. x: -1.2, 13.7, 28.5, 43.4, 58.2; y: 12.9, -18.1, -49.1, -80.1, -111). The x axis is injection number, so fractional and negative ticks have no meaning there.
- Follow-up: Choose "nice" tick steps (and integer injection ticks on x), as the other graphs do if they already have such a routine. Native macOS was not checked.

## ITC-082 - Model options leak between the experiment designer and analysis

- Priority: Low
- Status: Open (2026-10-08). Found while resolving ITC-078; code inspection only.
- Location: `AnalysisITC.Core/Analysis2/ModelFactory.cs`, `SingleModelFactory.SetModelOption` (writes `ModelFactory.StorePreviousAttribute`) and `SingleModelFactory.InitializeModel` (reads `PreviousAttributes`).
- Problem: `ModelFactory.PreviousAttributes` is a single static list. Avalonia designer option edits go through `SetModelOption` and are stored there, so a later analysis factory can recover options the user set only in the designer; options from the analysis workspace are likewise recovered into new designer factories. The designer now applies its own remembered options after initialization (ITC-078), so only options the user has not edited in that designer window are affected. Native designer option views edit options in place and do not write the global list.
- Follow-up: Decide whether the designer should bypass the global list (for example a designer-local `SetModelOption` path) or whether shared option memory is intended.
