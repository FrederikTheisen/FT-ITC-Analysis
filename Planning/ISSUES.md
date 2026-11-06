# Issue tracker

**[Resolved]** and **[Closed]** entries are decision records for related work. Search them for prior policy, rationale, and boundaries before choosing a fix. Entries without these flags retain their working notes and status.

## ITC-001 — Asymmetric rounding of negative display values [Resolved] <a name="itc-001--asymmetric-rounding-of-negative-display-values"></a>

Use sign-symmetric midpoint tolerance from the original value, then apply the requested midpoint mode to the scaled value. Applies to both `FWEMath.RoundApproximate` overloads and to estimates and interval endpoints.

## ITC-002 — Expanded bounds on logarithmic affinity coordinates [Resolved] <a name="itc-002--expanded-bounds-on-logarithmic-affinity-coordinates"></a>

Expand physical K bounds by factors of 20 or 2000 via additive log₁₀ shifts. Negative log₁₀K is valid. Reject global candidates yielding non-positive/non-finite K or Kd, including through linked enthalpy, while permitting finite affinities outside local presets.

## ITC-003 — pKa correction can fail to converge [Resolved] <a name="itc-003--pka-correction-can-fail-to-converge"></a>

Solve the existing ionic-strength balance by bounded bisection because fixed-point correction can cycle. Invalid or unverified solutions return NaN. Numerical convergence does not validate the nearest-pKa/two-species and monovalent-counterion assumptions at high concentration. See ITC-027.

## ITC-004 — Unlocking a spline point discards a converted spline [Resolved] <a name="itc-004--unlocking-a-spline-point-discards-a-converted-spline"></a>

Avalonia `ProcessingGraphControl` and macOS `DataProcessingGraphView` clear the selected position/slope locks and process with replace: false. Unlock is available when either lock is set; Smooth splines recalculate the released slope from the retained points.

## ITC-005 — Standardization of inspector headers [Resolved] <a name="itc-005--standardization-of-inspector-headers"></a>

Standardized affected headings and related tabs to sentence case, preserving proper names and acronyms.

## ITC-006 — Remove the standalone Edit identifiers tool [Resolved] <a name="itc-006--remove-the-standalone-edit-identifiers-tool"></a>

Removed the macOS Tools and Avalonia Selection/context-menu commands and handlers. Editing remains in Details > Identifiers; import review retains single-experiment dialogs (`ExperimentIdentifiersWindow`/`MacIdentifierEditor`) and batch dialogs. Manual and in-app help point to Details.

## ITC-007 — Avalonia UI tests fail when certain test classes run together [Resolved] <a name="itc-007--avalonia-ui-tests-fail-when-certain-test-classes-run-together"></a>

Avalonia UI tests use xUnit v3 and Avalonia.Headless.XUnit so a runner-owned dispatcher spans construction, async continuations and cleanup. Keep per-assembly isolation and serial UI tests for shared state. Use Skia rendering/hit testing because the synthetic renderer produced false width and hover failures. See ITC-101 for the separate Clear Processing failure.

## ITC-008 — Null Offset labels can expose unmatched experiment IDs [Resolved] <a name="itc-008--null-offset-labels-can-expose-unmatched-experiment-ids"></a>

FTXTC restoration validates comparison member identities against the binding result, so raw-ID fallback only reproduced with manufactured unmatched state. Keep the shared formatter and use normal experiment names/report labels and converted molar energy units for Offset.

## ITC-009 — Null-only figure ignores the experiment x-axis and error bars [Resolved] <a name="itc-009--null-only-figure-ignores-the-experiment-x-axis-and-error-bars"></a>

Draw the saved fitted Offset against current observations through the ordinary figure builder, honoring axes, error bars, offset correction and annotations. Match the exact member. Missing, ambiguous or non-finite fits show observations only. Processing edits do not refit the Offset. Only effective No binding detected selects this view.

## ITC-010 — Manual and older interpretations always warn "Assessment context unknown" [Resolved] <a name="itc-010--manual-and-older-interpretations-always-warn-assessment-context-unknown"></a>

Removed assessment-context unknown/changed warnings from `AnalysisReportBuilder` and both report windows (2026-10-02). Interpretation freshness and result validity already flag changed data.

## ITC-011 — Binding-output suppression is undocumented and missing on macOS [Resolved] <a name="itc-011--binding-output-suppression-is-undocumented-and-missing-on-macos"></a>

The native report window exposes Standard/Diagnostic output and native result figures use ResultOutputPolicy. The manual and shared `HelpTextResource.txt` explain suppression and access to attempted binding detail. Later policy refinements are recorded in ITC-014 and ITC-051.

## ITC-012 — Explicit model selection in Final Figure [Resolved] <a name="itc-012--explicit-model-selection-in-final-figure"></a>

Final Figure uses a session-only Plotted model choice, initially Binding model, across preview, print and every export entry point. Resolve the exact saved member comparison. Null shows the full saved prediction over buffer-corrected observations, with offset correction and confidence controls disabled. Missing or mismatched fits show observations only. Keep each desktop renderer and the separate Supporting Figure/report policies.

## ITC-013 — Full and diagnostic reports do not state the binding assessment [Resolved] <a name="itc-013--full-and-diagnostic-reports-do-not-state-the-binding-assessment"></a>

Superseded. ITC-014, ITC-073 and ITC-051 define current assessment, member-summary and provenance rules.

## ITC-014 — Assessment labels now drive which parameters are hidden [Resolved] <a name="itc-014--assessment-labels-now-drive-which-parameters-are-hidden"></a>

Current automatic rule `aicc-0-15-v1` uses signed ΔAICc ≤ 0 for No binding detected, ≥ 15 for Binding detected, and the interval between for Inconclusive. These are chosen cutoffs without a calibrated false-positive guarantee. Inconclusive keeps binding output and adds a health warning. Combined output requires every member to be eligible. On load recompute the automatic verdict from saved comparison evidence under the current rule, preserving manual overrides, fit and uncertainty. See ITC-051 for report and export policy.

## ITC-015 — Minor null-model output formatting

- Priority: Low
- Status: Partially resolved (2026-10-09); report-purpose persistence remains deferred.
- Location: `AnalysisReportBuilder.BuildSummary`, `AnalysisResultTableExporter.Build`, and both desktop report windows.
- Problem: The diagnostic "Binding assessment and null comparison" block appended heat units to unavailable Null RMSD ("Unavailable µJ"). Result-table numbers, including AICc, ΔAICc, Null RMSD, parameters and uncertainties, depended on the calling numeric culture, while temperatures were invariant.
- Resolution: Diagnostic Null RMSD is unitless when missing or non-finite; numeric values retain their selected heat units and precision. `AnalysisResultTableExporter.Build` uses invariant numeric culture for the entire table and restores the caller's culture in `finally`, covering CSV/TSV files and Copy on both desktop apps. Headers, quoting, units, precision, uncertainty styles and assessment suppression are unchanged. The report map A01, result-export manual and shared help are updated.
- Reassessment: The original header-prefix and missing-conditions complaints are outdated in the current code. The first header begins with "Analysis Result", with a separate Output purpose column; no-binding experiment chapters retain their conditions and applicable identifiers.
- Validation: All 31 new Core cases pass, covering missing/non-finite/zero/fractional RMSD in Joules and Calories, whole-table equality under en-US/da-DK/fr-FR across both formats, row modes, uncertainty layouts and output purposes, pooled diagnostics, literal numeric cells, quoted names, and culture restoration after success, empty input and exceptions. All 225 focused Core checks, 33 isolated Avalonia report checks and native `AnalysisReportReferencesTests` pass. Full Core suite: 2,457 passed, one skipped, one failure in `CompetitorSourceSaveSelectedTests.ResultSaveIncludesCompetitorSourceAndItsMembers`, also reproduced in isolation: its `Single` assertion finds multiple saved experiments with competitor references. That save/reopen test does not exercise the changed formatting paths and was left unchanged. `git diff --check` passes.
- Deferred decision: Standard/Diagnostic report purpose is not persisted or restored with report presentation settings, so reopened windows default to Standard. Decide whether to remember it separately; this change does not alter that behavior or the project format.
- C-value display correction (2026-10-21, D1/D5): E08 c-values followed the application uncertainty preference instead of the report selector. The shared report builder now passes the selected report style to the existing formatter, preserving its numerical format and calculations for all c-value variants. Runtime reproduction disproved the earlier debug-suffix claim: the existing call selected the ordinary numeric overload, not the debug formatter.
- Scope decision (2026-10-21): User-entered concentration errors in P01/E02/E03 retain their existing display; the report selector does not override them. Shared Null-parameter formatting and report-purpose persistence remain outside this correction.
- C-value validation (2026-10-21): All five report-selector regressions fail before the fix and pass afterward, using literal expected c-values across three experiment chapters in a multi-result report while checking that concentration-error text stays unchanged. All 502 focused report/c-value/number-formatting checks pass. Full Core suite: 4,874 passed, one existing opt-in skip, no failures. The report map E08, manual, and shared help are updated; `git diff --check` passes.


## ITC-016 — Independent multi-experiment fits share one binding assessment [Resolved] <a name="itc-016--independent-multi-experiment-fits-share-one-binding-assessment"></a>

Independent collections use member-local evidence keyed by solution ID, with pooled evidence diagnostic only. Combined binding output requires every member to be eligible. Not assessed remains unrestricted. Shared fitted coordinates require a result-level comparison, while equal locked values do not imply pooling. See ITC-073 and ITC-018.

## ITC-018 — Global null model for pooled shared-parameter fits

- Priority: High
- Status: Deferred.
- Problem: Pooled binding fits with shared parameters are currently compared with locally fitted Offset models. A future global null model should represent the same parameter-sharing or constraint structure as the pooled binding model, with corresponding criteria and persistence. Until that design is completed, the saved comparison uses local Offset fits and the established pooled-variance convention.
- Follow-up plan (2026-10-23): [Linear and Exponential dilution-heat models](ADDITIONAL_NULL_MODELS.md) plans matching background limits, fixed constants and sharing in both hypotheses, with free coefficients refitted separately. The user confirmed that the whole feature is deferred until after publication, including the Offset Null changes to limits, locks and sharing. This planning update does not resolve ITC-018 or authorize implementation; broader global Null design remains deferred.

## ITC-019 — Configurable assessment output set

- Priority: Low
- Status: Deferred.
- Problem: The output-allowed outcomes are currently fixed to Binding detected, Inconclusive, and Not assessed. Reports additionally retain no-binding member estimates as explicitly identified attempted-model values; standard table exports omit them. A future preference could make this set configurable, which would change suppression behavior across reports and exports.

## ITC-017 — Extreme confidence interval bounds render as long fixed-point numbers [Resolved] <a name="itc-017--extreme-confidence-interval-bounds-render-as-long-fixed-point-numbers"></a>

After unit conversion/rounding, format each estimate, SD, and bound independently with `G6` at magnitude ≥ 10¹⁰; smaller components retain configured precision. Finite values stay numeric, using the unrounded converted value if rounding overflows; infinities use ∞/−∞ and NaN behavior is unchanged. Covered by FloatWithErrorCompactFormattingTests.

## ITC-020 — Multi-result report chapters ignored the output purpose [Resolved] <a name="itc-020--multi-result-report-chapters-ignored-the-output-purpose"></a>

`AnalysisReportBuilder` copies the report’s output purpose into every result chapter.

## ITC-021 — Result chapter appendix labeled every result identifier as result 1 [Resolved] <a name="itc-021--result-chapter-appendix-labeled-every-result-identifier-as-result-1"></a>

Each chapter uses the result’s position in the report for its identifier reference.

## ITC-022 — Report-wide details repeat in every result appendix [Resolved] <a name="itc-022--report-wide-details-repeat-in-every-result-appendix"></a>

Reports use one closing appendix containing report details, report warnings, and a combined experiment sources table.

## ITC-023 — Front-page bookkeeping notice wording with one result [Resolved] <a name="itc-023--front-page-bookkeeping-notice-wording-with-one-result"></a>

`BuildFrontPage` uses one neutral report-wide note naming saved concentration and injection-heat methods and their applicable members, including mixed methods within/across results, without repeated warnings or required actions.

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

## ITC-026 — Tandem source experiments as automatic supporting experiments [Resolved] <a name="itc-026--tandem-source-experiments-as-automatic-supporting-experiments"></a>

Store ordered tandem source IDs through save/reopen and clone. Shared report picker references honor `AutoSelectReportReferenceExperiments`; auto-selection is optional and excludes experiments already covered by selected results. Missing links allow reporting and require manual selection for older/unlinked tandems. See ITC-031, ITC-033 and ITC-034.

## ITC-027 — Solution-wide buffer ionic-strength correction

- Priority: Low
- Status: Open; deferred scientific-method follow-up.
- Location: `AnalysisITC.Core/DataClasses/Buffers.cs`, buffer ionic-strength calculation.
- Problem: Each buffer currently estimates its correction using only its own contribution and nearest pKa transition as a two-species system. It adds neither salt nor other buffer contributions while solving that buffer's correction, so interactions across solution components are not represented.
- Follow-up: Consider a solution-wide calculation covering all buffer species, salts, and their coupled ionic-strength contributions, with independently validated reference cases.

## ITC-028 — Avalonia macOS menu bar and file browser activation [Closed] <a name="itc-028--avalonia-macos-menu-bar-and-file-browser-activation"></a>

Closed at the maintainer's request. No further investigation or implementation is planned; closure does not claim new native verification.

## ITC-029 — Improve analysis inspector null-test and fit summaries [Resolved] <a name="itc-029--improve-analysis-inspector-null-test-and-fit-summaries"></a>

- The Analysis inspector combined null RMSD/ΔAICc and placed fit RMSD on the model heading, obscuring the test and fit scope.
- Both Analysis inspectors render shared `NullModelComparisonPresentation.AnalysisInspectorRows` for `Model`, Null RMSD, ΔAICc, and Conclusion. macOS adds a separator/bold header; its fit summary shows the readable model and Global/Individual scope, with RMSD first and values right-aligned. Results-tab null sections and graph parameter boxes are unchanged.

## ITC-030 — Unify fitted-parameter presentation in Analysis [Resolved] <a name="itc-030--unify-fitted-parameter-presentation-in-analysis"></a>

Both inspectors use Core `AnalysisParameterSummaryPresentation` for read-only fit values, separate from editable starts and optional graph overlays. Avalonia solution-change events refresh inspector and graph without a baseline so heat-only fits update on attach, replace and clear. Keep owner checks on queued UI refreshes.

## ITC-031 — Removed supporting experiments are reselected when a report reopens [Resolved] <a name="itc-031--removed-supporting-experiments-are-reselected-when-a-report-reopens"></a>

Restore the exact saved report explicitly via Saved Reports or Project Information (ITC-032). Ordinary opening starts a detached draft. Auto-select references in new drafts or when checking results, and honor removals. Edit results and supporting selections together, keeping interpretation text with a review warning if scope/results change. Print order follows project order without rewriting saved selection or timestamp.

## ITC-032 — Project Information and saved-report management [Resolved] <a name="itc-032--project-information-and-saved-report-management"></a>

Use explicit report saving with detached drafts. Project Information manages saved reports, including inspect/copy/delete when results are missing. Rename changes the printed title without changing interpretation freshness. Last saved advances only on content changes. Recovery accepts report-only and intact empty snapshots as dirty unnamed documents, and rejects unreadable archives or recovery issues with no usable content. Unsaved drafts remain outside autosave (ITC-111). See ITC-033 and ITC-091.

## ITC-033 — Automatic report reference selection has no preference control

- Priority: Low
- Status: Open; deferred.
- Location: `AppSettings.AutoSelectReportReferenceExperiments`, `PreferencesState`; macOS and Avalonia preferences windows.
- Problem: The setting is stored and defaults to on, but neither preferences window exposes it, so users cannot turn automatic selection off. Both report builders use it for initial supporting selections and when checking results in the contents picker; users can still remove individual supporting selections.
- Follow-up: Add a control to both preferences windows (tooltip text shared in Core), or remove the setting and update the manual. Revisit when report contents selection or the preferences windows are next changed, including ITC-026.

## ITC-034 — Supporting experiment size in AI interpretation requests [Resolved] <a name="itc-034--supporting-experiment-size-in-ai-interpretation-requests"></a>

- Detailed supporting-experiment evidence duplicated tandem source data and increased interpretation payloads.
- Keep fitted members at the selected evidence level and send supporting experiments as summaries. Retain identity, relationships, metadata, and processing completion. Higher levels also retain processing settings, scalar baseline statistics, and reasons. Omit supporting injection rows, thermograms, landmarks, and baseline controls. Limit experiment annotations to transmitted experiment IDs and normalize legacy singular bootstrap correlations. Canonical capture and freshness fingerprints stay complete.
- **Follow-up.** The shared inline/shared finalizer strips unused baseline-control descriptions from fresh and legacy compact payloads while preserving numeric precision. A synthetic tandem payload shrank from 74,351 to 12,824 bytes with fitted-member injection evidence retained. Transport checks used local fakes.

## ITC-035 — Missing glyphs in Avalonia report text [Resolved] <a name="itc-035--missing-glyphs-in-avalonia-report-text"></a>

Use identical Skia fallback runs for measurement, wrapping, preview and vector PDF. Match installed faces by glyph coverage and face attributes, cache substitutions, and keep combining marks with their run. Break long words at text-element boundaries in shared layout. Line height follows the publication face. Font substitution alone does not provide shaping or bidi ordering, and missing installed coverage still produces a box.

## ITC-036 — Processing details shown for integrated-heats imports [Resolved] <a name="itc-036--processing-details-shown-for-integrated-heats-imports"></a>

`AnalysisReportBuilder` selects the block by thermogram availability: retain Processing and integration with a thermogram; otherwise show Data availability, Imported integrated heats, and injection use, omitting baseline/integration status and regions/times. Partial availability counts only integrated injections with finite current heats. Supporting experiments follow the same rule while retaining correction/reference notes; condensed repeats omit the block. Core and both renderers were checked. Bookkeeping-notice placement remains ITC-037.

## ITC-037 — Bookkeeping convention displayed in a large appendix notice [Resolved] <a name="itc-037--bookkeeping-convention-displayed-in-a-large-appendix-notice"></a>

The appendix "Bookkeeping conventions" notice (map P02, now retired) is removed. Each result experiment's "Processing and integration" (E04) or "Data availability" (E04a) block ends with an "Injection bookkeeping" row showing the method saved with that result's fit; without a fit-time record it keeps the existing "Unknown concentration method; <heat> heat" wording. Supporting experiments have no fit and omit the row. Condensed repeats (E03) omit E04/E04a and therefore the row, by decision. When experiments within one result have different saved methods, the result overview shows an orange "Mixed injection bookkeeping" notice (R03a) listing each method with its experiment labels, and Report warnings (P03) gains one line. Different methods across separate results are not flagged. `ANALYSIS_REPORT_MAP.md` and the user manual (`09-figures-printing-export.md`) are updated. Expanded-explanation notices (ITC-024) are unaffected.

## ITC-038 — Add experiment name to report page headers [Resolved] <a name="itc-038--add-experiment-name-to-report-page-headers"></a>

`AnalysisReportLayoutEngine` and both renderers include the fitted experiment’s label/name beside the result name. Shared layout preserves the export date, prioritizes the result, and truncates names at text-element boundaries while retaining the experiment label. Supporting-data headers are unchanged.

## ITC-039 — Diagnostic summary graphs include no-binding experiments [Resolved] <a name="itc-039--diagnostic-summary-graphs-include-no-binding-experiments"></a>

`BuildThermodynamicSummaryPlot` filters effective assessments in both report modes: exclude No binding detected, retain Inconclusive/Not assessed, honor overrides, and omit the graph if none remain. Diagnostic text names omitted members without claiming combined values are suppressed; attempted estimates remain in detailed tables. Core and both renderer regressions cover mixed/all-no-binding cases and wide intervals.

## ITC-040 – Identify information, caution, and warning box locations [Resolved] <a name="itc-040--identify-information-caution-and-warning-box-locations"></a>

Use Warning/Error boxes for consequences to trust or comparability. Use Information boxes for assessment-driven omissions or opt-in explanations. Put provenance, availability and other routine metadata in rows. Suppress fit warnings when no fit is presented or the assessment excludes them. Triggers and exceptions live in the report map. See ITC-024.

## ITC-041 - Source file and source format in one line for analysis reports [Resolved] <a name="itc-041---source-file-and-source-format-in-one-line-for-analysis-reports"></a>

Full, condensed, and supporting experiment details combine both under Source file, with placeholders for missing metadata. Both apps wrap complete values; the appendix filename column is unchanged.

## ITC-042 - Competitor properties affinity and enthalpy values are not showing up [Resolved] <a name="itc-042---competitor-properties-affinity-and-enthalpy-values-are-not-showing-up"></a>

The older competitive result used manual inputs and has no fit-time competitor-source capture. The newer result uses From attributes and already saves and displays finite Kd and ΔH with uncertainty. Fixed the report fallback that could attribute the newer fit's live competitor values to the older fit; result chapters now use only their saved captures and explain missing captures using their saved model options. The shared Core builder serves both desktop renderers; no file-format change was needed.

## ITC-043 - Report baseline type could include information on baseline [Resolved] <a name="itc-043---report-baseline-type-could-include-information-on-baseline"></a>

The shared builder appends spline mode/point density or polynomial/segmented degree in both apps.

## ITC-044 - Explain saved FWE [Resolved] <a name="itc-044---explain-saved-fwe"></a>

They are primitive fit-time `ExperimentAttributeSnapshot` fields for component-wise `SameDouble` comparison; the live attribute already persists as an FWE. The flat snapshot omits the missing flag, but required Kd/ΔH are captured as a finite pair or cleared together. Added `CapturedAffinityWithError`/`CapturedEnthalpyWithError` accessors (non-finite → `FloatWithError.NaN`) and used them in reports without a format change. Small-value comparison tolerance was addressed in ITC-055.

## ITC-045 - macOS report inspector does not allow scrolling all the way down. [Resolved] <a name="itc-045---macos-report-inspector-does-not-allow-scrolling-all-the-way-down"></a>

`AnalysisReportViewController` pins scroll content to the visible area’s top/left/width and ties the section bottom to the content bottom, so the scroll range follows actual content.

## ITC-046 - Unnecessary summary caveat in report [Resolved] <a name="itc-046---unnecessary-summary-caveat-in-report"></a>

Removed the caveat from Standard reports; its detailed explanation remains available with expanded explanations.

## ITC-047 - Too long block header: "Combined across experiments at the mean temperature: 25.00 °C" [Resolved] <a name="itc-047---too-long-block-header-combined-across-experiments-at-the-mean-temperature-2500-c"></a>

Use Combined parameters with Evaluation temperature as the first row, without calculation changes. Manual/help explain that temperature series use the current Reference temperature preference; other results use mean experiment target temperature.

## ITC-048 - Join report summary model and fit details blocks  [Resolved] <a name="itc-048---join-report-summary-model-and-fit-details-blocks-"></a>

Both report renderers use one `Model` and fit details block retaining settings, constraints, and diagnostics. Fixed parameters and per-experiment fit details remain separate.

## ITC-049 - Experiment name font and truncation [Resolved] <a name="itc-049---experiment-name-font-and-truncation"></a>

Figure canvas headings use bold panel labels and regular-weight names. Removed `PanelTitleMaximumCharacters`; both renderers use shared `PublicationFigureCanvasBuilder.FitPanelTitle` to middle-shorten the full name to the measured panel width.

## ITC-050 - Result health can have warning if analysing a no binding experiment  [Resolved] <a name="itc-050---result-health-can-have-warning-if-analysing-a-no-binding-experiment-"></a>

Core `BindingAssessmentInterpretation` treats only effective No binding detected as non-binding; `AnalysisResult.FitWarningMembers` excludes those members from `Health/HealthReasons` for independent, single, and pooled results. Input validity is unchanged; competitor badges follow `source.Health`, so Inconclusive sources show Warning. Other warning surfaces and uncertainty work were outside this fix (ITC-052/ITC-053); competitor-source assessment display is ITC-054.

## ITC-051 - Ensure report summary table and summary values are aligned  [Resolved] <a name="itc-051---ensure-report-summary-table-and-summary-values-are-aligned-"></a>

Standard reports leave no-binding parameter/RMSD cells blank, retain AICc/conditions, and omit member parameter tables, RMSD and fit warnings (ITC-053 and ITC-056). Omit combined parameters if any member is ineligible, without recombining a subset. Diagnostic output retains attempted values. Mark manual assessments when requested or forced by Traceability Mode, always in E08 and when any collection member was overridden (ITC-076). Eligible ionic strength (CR-020) and protonation conditions remain in Standard Individual exports.

## ITC-052 - Skip uncertainty estimation for results assessed as no binding [Resolved] <a name="itc-052---skip-uncertainty-estimation-for-results-assessed-as-no-binding"></a>

Assess binding before estimating uncertainty. Skip only effective No binding detected, at whole-fit level for single/pooled fits and per member for independent fits. Update Result retains manual overrides and can estimate after switching a skipped fit to Binding detected. Persist `skipped-no-binding`; older readers cannot open it. Incomplete member replicates cannot support combined uncertainty. See ITC-083 for the default-on setting.

## ITC-053 - Non-binding members still show fit warnings outside result health

- Priority: Low
- Status: Implemented (2026-10-12); pending visual review. Reports were handled on 2026-10-08: Standard reports omit these warnings from report warnings (P03); Diagnostic reports keep them.
- Problem: After ITC-050, fit warnings from members assessed as no binding no longer affect result health, but they still appeared in each experiment's row in the macOS/Avalonia result views, in the status bar after fits and refits (combined convergence boundary contacts), and in web viewer result warnings, so a Valid result could show bound warnings.
- Resolution: Only an effective (manual or automatic) No binding detected changes behavior (`BindingAssessmentInterpretation`); Inconclusive and Not assessed keep their warnings. Result rows (macOS Analysis Result tab, Avalonia Results workspace) keep the member's warnings followed by "Not counted in result health: no binding detected." (`ParameterBoundaryWarningFormatter.ResultMemberMessages`, using `AnalysisResult.FitWarningMembers`); Avalonia shows those lines in muted rather than warning colour. Web viewer result warnings carry the same line; per-fit warnings in the experiment fit view are unchanged because those fits need not belong to a result. The status-bar "parameter bound reached" message omits contacts of no-binding members after fits and Update Result on both desktops (`CountedContacts`; shared contacts are omitted only when no member is treated as binding, i.e. a pooled fit assessed as no binding). Fit-time assessments come from `BindingAssessmentMember.ForFit`, mirroring a new result's automatic assessments; Update Result uses the result's member assessments. The bound warning now follows the completion message on both desktops (macOS previously showed it last). The macOS `AnalysisResultValidityPresentation.ExperimentListText` also lists these warnings but has no callers and was left unchanged. The AI interpretation package still lists no-binding boundary contacts; out of scope. Avalonia's blank uncertainty message after independent fits is left to the in-progress ITC-090/ITC-094 completion wording (macOS already uses `UncertaintyEstimationPresentation.Completion`); see ITC-100.
- Validation: Core `NoBindingFitWarningTests` (7) cover annotation, manual overrides, members without warnings, local/shared contact filtering with literal expected messages, fit-time assessments matching a new result, and the viewer round trip through `.ftxtc`. Avalonia `BoundaryWarningPresentationTests.NoBindingExperimentWarningsAreMarkedAsNotCounted` checks the result rows. A temporary scenario run (single, independent, pooled; binding, no binding, mixed; best-fit and bootstrap bound contacts) confirmed the filtered status messages. Full Core suite: 3,092 passed, 1 skipped, 15 failed, all in `NoBindingUncertaintySkipTests` under concurrent ITC-090 work and unrelated to this change. Avalonia focused suites passed (64 + 4); macOS compiled and `AnalysisParameterDraftTests` native checks passed. Neither app was exercised interactively.

## ITC-054 - Competitor source assessed as no binding [Resolved] <a name="itc-054---competitor-source-assessed-as-no-binding"></a>

`CompetitorResultPreviewBuilder` adds No binding when any effective source member is non-binding, with precedence Unknown > Stale > No binding > Changed > Warning > Valid. Tooltips explain Kd/ΔH implications and member counts; Inconclusive has an explanatory tooltip and health-based Warning. This changes display only, leaving fitting and the source picker unchanged.

## ITC-055 - Validity comparison tolerance hides small competitor Kd changes [Resolved] <a name="itc-055---validity-comparison-tolerance-hides-small-competitor-kd-changes"></a>

- `SameDouble` used an approximately 1 nM absolute floor for molar Kd, hiding a 5 → 5.8 nM source change and pM-scale changes in values, SDs, and intervals; prebound-ligand concentration shared the blind spot.
- `AnalysisResultValiditySnapshot` compares captured competitor Kd/SD/interval endpoints and prebound-ligand concentration/SD at their actual molar scale with `1e-9` relative tolerance and no absolute floor. Zero differs from nonzero; NaN/infinity equality is preserved. Other tolerances, fitted values, and schema are unchanged. Focused validity/competitive-model/preview tests cover small values, accepted roundoff, and snapshot round trips.

## ITC-056 - Core test expects no parameter table in Standard no-binding reports [Resolved] <a name="itc-056---core-test-expects-no-parameter-table-in-standard-no-binding-reports"></a>

ITC-051 changed the builder to omit their per-experiment parameter table, restoring the test’s expected policy and aligning the manual/help. The test passes as written.

## ITC-057 - Interpretation access code is stored in plain text

- Priority: Low
- Status: Open
- Location: `AppSettings.InterpretationOperatorCode` (`SaveToStorage`, load), `MacUserDefaultsSettingsStore`, `AvaloniaJsonSettingsStore`.
- Problem: The interpretation access code is sent as a Bearer credential to the relay, but it is saved unencrypted in NSUserDefaults on macOS and in the Avalonia JSON settings file. Any process running as the user, and any backup of those files, can read it. Only a SHA-256 hash is needed for the cached-access checks, but the plain value is stored as well.
- Suggestion: Store the code in the OS credential store (Keychain on macOS; the platform equivalent for Avalonia) and keep only the hash in preferences. Migrate existing values on first load.
- Comment: the interpretation access code is not super secret and I would prefer not starting to mess with platform secure storage if possible. It should be considered as an activation code, not a personal secret.
- Additional finding (2026-10-21, F11; Plausible credential-retention question by source review only): sent `registration_delivery.protected_code` remains recoverable using the database plus usable Data Protection keys; it is encrypted outbox material, distinct from plaintext client preferences. Decide whether to clear it after successful delivery, retain it for justified resend/reconciliation with bounded expiry, or use narrower recovery storage. Check startup reconciliation/resend dependencies and deployed key protection before choosing. Preserve the maintainer’s activation-code sensitivity assessment above. No runtime reproduction or tests run. Tracker registration only; no implementation authorized.


## ITC-058 - Asymmetric least squares baseline has no effect

- Priority: Low
- Status: Implemented (2026-10-12); ALS removed by user decision, as it will not return in the near future. Awaiting user confirmation.
- Problem: `AssymetricLeastSquaresInterpolator.Interpolate` computed an ALS curve but never assigned `Baseline`, so a new ALS processor could not produce a baseline and its Iterations, Lambda and Asymmetry settings had no effect. The run was O(n²) in thermogram samples, `Copy` lost the settings, and the native macOS processing view cast a restored ALS processor to `PolynomialLeastSquaresInterpolator`, yielding a null reference. ALS could not be selected in either app, but `.ftxtc` (`"asl"`) and legacy `.ftitc` (type 1) files could still restore it.
- Resolution: The interpolator class is deleted. `BaselineInterpolatorTypes.ASL` remains as an `[Obsolete]` member so saved files and validity snapshots stay readable; `DataProcessor.InitializeBaseline` restores it as a polynomial interpolator with default settings, which covers `.ftxtc` and both legacy `.ftitc` paths. Saved ALS settings are ignored; the stored baseline and completion state are retained, and no notice is shown. Writers never emit the `asl` type and always write `asl: null`. Validity snapshots that recorded `asl` keep it, so such results report changed processing. The AI interpretation package no longer has ALS baseline controls (they were only emitted for ALS processors), and the macOS processing view's ALS case is removed. `FTXTC_FORMAT.md` and the schema's `asl` definition describe the read-only legacy type; the schema enums still accept `asl`. ALS was not documented in the manual or help.
- Validation: New Core `RetiredAlsBaselineTests` (4) cover an `.ftxtc` `asl` processor restoring as a degree-12 polynomial with its stored baseline, corrected trace and completion state, re-saving as `polynomial` with `asl: null`; a legacy `.ftitc` type-1 processor restoring as polynomial; a recorded `asl` validity snapshot differing from the restored polynomial; and the `asl` wire identity. 225 focused persistence, schema, legacy and interpretation tests pass. Full Core suite: 3,100 passed, 1 skipped, and only the 15 known ITC-090 failures. Avalonia builds; the native macOS managed code compiles through `run-analysis-null-graph-tests.sh` and `ProcessingSliderTests` pass. Neither app was run interactively.

## ITC-059 - Completing a save clears edits made while the file is being written [Resolved] <a name="itc-059---completing-a-save-clears-edits-made-while-the-file-is-being-written"></a>

- `SaveAsync/SaveWithPathAsync` unconditionally marked the current document clean after asynchronous writes, clearing newer edits absent from the saved snapshot and allowing close/autosave to miss them.
- `ProjectWriter` and `DocumentDirtyTracker` track document identity plus monotonic edit revision, including repeated/suspended edits; saves clear dirty state only if captured identity/revision still match outside import/restoration scopes. Queued destinations resolve under the save gate, held through completion bookkeeping; abandoned requests return false. Both apps use `SaveForCloseAsync`, keeping the document open when newer edits remain. Identity changes on clear/clean native open/recovery into an empty document, while appends retain it. Deterministic save/tracker/autosave tests cover these boundaries; the format is unchanged. Mutations bypassing `MarkModified`/document-change notifications remain invisible.

## ITC-060 - Duplicating buffer-corrected data loses the applied correction [Resolved] <a name="itc-060---duplicating-buffer-corrected-data-loses-the-applied-correction"></a>

- `DuplicateSelectedData` copied raw injection heats before adding the buffer attribute, leaving advertised buffer correction unapplied and reference updates unsubscribed (10 − 2 µJ became 10 µJ instead of 8 µJ).
- Call `SetBufferSubtraction` after duplicate injections/attributes exist, restoring corrected heats and reference subscriptions; unresolved reference attributes remain unchanged. `BufferSubtractionDuplicationTests` covers initial/later heats with and without raw data and confirms clearing the copy’s buffer preserves the original subscription.

## ITC-061 - Batch export can overwrite another experiment in the same batch [Resolved] <a name="itc-061---batch-export-can-overwrite-another-experiment-in-the-same-batch"></a>

- Generated export suffixes could collide with real experiment names: sample, sample, `sample_1` wrote the same `review_sample_1.csv` twice, silently overwriting one experiment.
- `Exporter.PlanOutputs` allocates unique paths across the actual batch once. Unique names keep `<base>_<name>`; case-insensitive duplicates and blank names receive the next free `_<n>` suffix, skipping reserved names. Data export counts only thermogram-bearing experiments. Overwrite confirmation and `WriteOutputs` reuse the plan; `ExportOutputPlanTests` covers collisions.

## ITC-062 - Save Selected omits required buffer-reference experiments [Resolved] <a name="itc-062---save-selected-omits-required-buffer-reference-experiments"></a>

- Save Selected omitted external buffer references, causing strict .ftxtc reads to fail and recovery to retain corrected heats without a reproducible correction source.
- `ProjectWriter.SaveSelectedAsync` keeps single-experiment saves to one experiment and stores a `BufferSubtractionReferenceSnapshot` in the attribute when the reference is not written: reference name plus included/integrated injection numbers and raw heats/SDs. Without the live reference, stored values recalculate corrections on load, reintegration, attribute edits/copies, and duplication; strict reads succeed. Later saves retain the snapshot; a loaded matching reference takes precedence and refreshes it. Result saves also include loaded member buffer references. Both editors show Missing reference (stored values retained); reports/status identify stored values. The optional schema-1.6 field needs no version bump and earlier readers ignore it. `BufferSubtractionStoredReferenceTests` covers all three methods, strict round trips, live precedence, and edits/copies. Competitor-result dependencies are ITC-074.

## ITC-063 - Trailing slash bypasses the Web viewer upload concurrency limit [Resolved] <a name="itc-063---trailing-slash-bypasses-the-web-viewer-upload-concurrency-limit"></a>

- Exact-path upload admission guarded `/api/viewer/open` but routing also accepted `/api/viewer/open/`, allowing concurrent project parsing outside the shared limit.
- `ViewerUploadLimits/Program.cs` attach admission metadata to the endpoint and route before middleware. Canonical/trailing-slash/mixed-case paths acquire a disposable `ConcurrencyLimiter` lease before antiforgery/body reads/parsing. Defaults: one active upload per process, five FIFO waiters, 30-second wait-only timeout. Overflow returns `503 viewer_busy`, timeout `503 viewer_queue_timeout`, both with Retry-After: 1. Cancellation frees queue capacity and all admitted completion/error paths release the lease. Per-request limits/UI are unchanged; deterministic admission/endpoint tests cover route variants, FIFO, timeout, and cleanup.

## ITC-064 - Rejected result-update preparation removes attached experiment fits [Resolved] <a name="itc-064---rejected-result-update-preparation-removes-attached-experiment-fits"></a>

- `AnalysisResultUpdater.PrepareSolver` attached new models before validating options/initializing the solver; rejected preparation left live experiments without their previous fits, although the stored result survived.
- Capture exact live `Model` references before factory preparation and restore them on any subsequent preparation exception, then rethrow. Rollback preserves distinct/null attachments without solution-change notifications or dirty/revision changes. Successful preparation and desktop error handlers are unchanged; later fitting, cancellation, and acceptance are outside this transaction boundary.

## ITC-065 - Avalonia Release tests assume obsolete labels and a debug-only control [Resolved] <a name="itc-065---avalonia-release-tests-assume-obsolete-labels-and-a-debug-only-control"></a>

`AnalysisReportEnhancementTests` checks report-ID visibility/save/reopen and other result-specific settings without the checkbox. `LockedParameterPresentationTests` expects sentence case and locates the section case-insensitively. Production code is unchanged; the separate report-policy failure was ITC-056.

## ITC-066 - Native interpretation layout test rejects the shared generation guard [Closed] <a name="itc-066---native-interpretation-layout-test-rejects-the-shared-generation-guard"></a>

The native source assertion verifies the shared helper receives service, access, busy, and size state, allowing whitespace variations. Added Core coverage for unavailable service alongside denied access/busy/oversize cases. The native fixture and focused Core interpretation tests passed; production code is unchanged.

## ITC-067 - Injection heat direction depends on buffer-subtraction timing [Resolved] <a name="itc-067---injection-heat-direction-depends-on-buffer-subtraction-timing"></a>

- `SetPeakArea` classified heat direction from corrected `PeakArea`, so assigning buffer subtraction after integration retained the raw direction while reintegration could change it to the corrected direction. Buffer uncertainty could also change the classification to Unknown.
- Direction describes the integrated peak before buffer subtraction, as agreed with the user. Shared Core `InjectionData.SetPeakArea` uses `RawPeakArea` and its SD with the existing strict three-SD threshold. Buffer assignment, reference updates, and removal correctly leave direction unchanged; both desktop apps use this Core state. Corrected heats, uncertainties, fitted results, plotting consumers, and serialization are unchanged. Previously saved direction fields restore as stored until peak areas are set again.

## ITC-068 - Structuring evaluation-temperature options need scientific review

- Priority: Medium
- Status: Open (2026-10-07).
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`, `SRTempMode` and the entropy term in the structuring calculation; result-view temperature-mode controls on both platforms; report Structuring chapter ("Evaluated at").
- Problem: The published Spolar–Record method, and its adaptation in Theisen et al. (JACS 2021), evaluates the entropy terms at the isoentropic temperature, where ΔS = 0. At issue creation, FT-ITC also offered the mean experiment temperature, where the fitted parameters are arguably best determined, and the reference temperature, to compare interactions at a common temperature. These two options are not part of the published method. It is unresolved whether evaluating away from the isoentropic temperature is valid, and how its results should be interpreted or compared with published values.
- Temporary restriction (2026-10-13): Both desktop apps show a fixed **Evaluated at: Isoentropic point** row and explicitly select that mode when starting a structuring run. Mean and reference modes are hidden pending scientific review. Existing saved outputs retain their values, evaluation temperature, and completed mode when viewed or reported; the Core alternatives remain available for research. The equations, calibration, and project format are unchanged. **ITC-068 remains open** for the scientific review.
- Plan: [Structuring methods — research plan](STRUCTURING_METHODS_PLAN.md). Review scientific justification, matched calibration, and the interpretation of temperature choices there; retain the reference-temperature research direction and keep ITC-072 as a separate alternative. The temporary desktop restriction does not approve an alternative scientific method.
- Validation (2026-10-13): Both desktop checks cover the fixed setup row, new isoentropic runs, saved outputs in all three modes, Celsius/Kelvin display and uncertainty, and preservation after cancelled reruns. The native harness and two Avalonia tests pass; 232 focused Core tests pass, including structuring, saved evidence, report output, and classified-output policy. The full Core run had 3,413 passes, one skip, and two unrelated report-assertion failures during concurrent workspace changes; both failures passed in the subsequent focused run. `git diff --check` passes.

## ITC-069 - Structuring result views scale saved entropies by the current reference temperature [Resolved] <a name="itc-069---structuring-result-views-scale-saved-entropies-by-the-current-reference-temperature"></a>

Both views use the saved evaluation temperature for hydration/conformational contributions, matching AnalysisReportBuilder.AddSpolarRecord. Focused Core/Avalonia and native output checks confirmed preference changes leave saved contributions/temperature unchanged. Other temperature-mode and uncertainty questions remain ITC-068, ITC-070, and ITC-071.

## ITC-070 - Structuring contribution uncertainty omits joint temperature variation [Closed] <a name="itc-070---structuring-contribution-uncertainty-omits-joint-temperature-variation"></a>

- Raised whether `SROutput.HydrationContribution/ConformationalContribution` should propagate joint temperature/entropy variation when scaling saved entropy uncertainty by the central saved temperature.
- Closed as not an issue: the central evaluation temperature is treated as a fixed value without uncertainty. The existing entropy-uncertainty scaling is retained; this concern was excluded from ITC-069.

## ITC-071 - Structuring views display saved temperature uncertainty differently [Resolved] <a name="itc-071---structuring-views-display-saved-temperature-uncertainty-differently"></a>

Both desktop output rows and the report's saved structuring temperature use `StructuringPresentation.FormatEvaluationTemperature`, which formats the saved `FloatWithError` with `AsNumber` under the selected uncertainty style and Celsius/Kelvin units. Kelvin shifts the value and interval endpoints without changing the SD; unavailable temperatures remain unitless “Unavailable”. Avalonia refreshes the analysis panel immediately on a unit change. Saved values, project serialization, and entropy-contribution calculations are unchanged. Independent literal Core expectations and report/Avalonia/native output checks cover units, available/absent uncertainty, display styles, and saved output after reference-temperature preference changes.

## ITC-072 - Structuring with an explicit polar-hydration entropy term

- Priority: Medium
- Status: Open; research option (2026-10-07). Related to ITC-068, which is pursuing the reference-temperature method.
- Location: `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs`; per-residue calibration constant; evidence in `Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/`.
- Problem: The current entropy split leaves the polar share of ΔCp in the conformational remainder, making estimated residue counts depend on evaluation temperature. Explicit polar hydration is a research alternative with additional assumptions and calibration requirements.
- Plan: [Structuring methods — research plan](STRUCTURING_METHODS_PLAN.md). Proposed equations, exploratory evidence, and open calibration/validation questions are recorded there. This remains a research option; it is not an approved replacement for the reference-temperature direction under ITC-068.

## ITC-073 - Mixed member assessments reported as a uniform collection verdict [Resolved] <a name="itc-073---mixed-member-assessments-reported-as-a-uniform-collection-verdict"></a>

- A precedence-based collection verdict labeled mixed binding/no-binding members No binding detected in reports, exports, clipboard output, and interpretation requests, despite desktop views showing Mixed assessments.
- A shared collection-summary type reports the common effective outcome only when all independent members agree; any difference, including Inconclusive/Not assessed and overrides, becomes Mixed assessments under Member assessments. Mixed is not a selectable/persisted individual verdict. Shared presentation feeds both apps, reports/exports/clipboard, and separate effective/automatic AI summaries; relay guidance preserves member authority for new and legacy summaries. Single/pooled assessments and output eligibility are unchanged. Outcome-pair, override, persistence, presentation, interpretation, and renderer checks cover the policy.

## ITC-074 - Saved Analysis Results are not reproducible in isolation [Resolved] <a name="itc-074---saved-analysis-results-are-not-reproducible-in-isolation"></a>

- Related work includes ITC-077.
- Result Save Selected omitted Analysis Results referenced through `CompetitorResult` attributes, their members, and nested dependencies. Cached Kd/ΔH allowed refitting, but the source could not be inspected or re-evaluated from the file alone.
- `ProjectWriter.SaveSelectedAsync` recursively writes loaded competitor source results, their members, and loaded buffer references, applying ITC-062 at every level. Each result is written once to end cycles; unloaded sources retain cached Kd/ΔH fallback. Save the actual source rather than a name/value snapshot. Single-experiment saves remain one experiment; no format change. `CompetitorSourceSaveSelectedTests` covers isolated strict reopening/link resolution, nesting/cycles, unloaded sources, and single-experiment scope. Reopening with colliding result/solution IDs still needs the separate ITC-077 remapping fix.

## ITC-076 - Always identify overridden assessments in reports [Resolved] <a name="itc-076---always-identify-overridden-assessments-in-reports"></a>

Always identify manual overrides wherever assessment text is displayed in Standard and Diagnostic reports. Collection summaries carry "(manual)" when any member is overridden. Removed the hidden option and its copy/traceability handling instead of exposing or saving it. The shared Core formatter supplies both desktop renderers; assessment visibility and output-suppression rules are unchanged. Updated the report map, manual, and in-app help. This supersedes ITC-051's conditional marker policy without changing project serialization.

## ITC-077 - Warn when importing existing saved object IDs [Resolved] <a name="itc-077---warn-when-importing-existing-saved-object-ids"></a>

On duplicate saved IDs, prompt once per file and default to Skip Duplicates, including dismissal and noninteractive use. Import new content against existing identities while preserving detached result-specific model context and snapshots. Import Copies reserves IDs, remaps only conflicts and updates all incoming dependent references without rewriting external links or provenance. Entirely skipped files leave destination and dirty state intact. Legacy imports use the same resolver under ITC-143.

## ITC-078 - Experiment designer resets parameters when rebuilding setup [Resolved] <a name="itc-078---experiment-designer-resets-parameters-when-rebuilding-setup"></a>

- Rebuilding the experiment reset entered parameters, and the native designer could keep controls bound to obsolete parameters or use fitted values as simulation inputs.
- One `ExperimentDesignerState` per window retains explicit parameter and option edits by key across setup and model switches. Rebuilds apply window options before clamping entered values to the current limits, using defaults for unedited parameters. Both apps publish the replacement experiment and model together after successful preparation and rebind controls when the parameter table changes. Native simulation reapplies generation values after in-place fits.
- **Fit ownership.** Both apps lock setup during the designer's own fit and restore control eligibility on its completion. Native noise simulation still rebuilds the experiment, while Avalonia regenerates noise directly. Global option-history isolation is covered by ITC-082. Native validation used an AppKit harness.

## ITC-079 - Avalonia tool windows write micro units as "u" [Resolved] <a name="itc-079---avalonia-tool-windows-write-micro-units-as-u"></a>

Labels and generated summaries in Avalonia `ExperimentDesignerWindow`, `TandemMergerWindow`, `ExperimentDetailsWindow`, and `AvaloniaTandemImportPromptService`, and in macOS `MacTandemImportPromptService`, use µM/µL (micro sign U+00B5). Deliberately unchanged: `TandemMixingScan.cs` scan log volume text, `IntegratedHeatReader.cs` log lines, `ExportType.cs` export format descriptions, `Exporter.cs` `#EXPINFO … uM` headers read by external tools, and the `dead_volume_uL` key. Native macOS rendering was not inspected.

## ITC-080 - Result dates and experiment dates use different formats [Resolved] <a name="itc-080---result-dates-and-experiment-dates-use-different-formats"></a>

- Experiment dates followed the selected locale while result dates followed the process culture.
- Both desktops use `LocalDateTimeFormatter` for UI dates in experiment and result tools, recovery, and account displays. It resolves `AppSettings.Locale` independently of numeric culture and falls back to UI culture and then invariant culture. Avalonia's editable-date parser uses the same resolver. Report/PDF timestamps, figure ISO dates, and serialization retain their existing conventions.
- **Limitation.** On macOS the configured locale comes from the collator identifier, which follows the language list and does not capture the system Region or custom date formats.

## ITC-081 - Buffer Subtraction graph uses non-round axis tick values

- Priority: Low
- Status: Implemented (2026-10-08); visual review complete (2026-10-10), pending user acceptance. Seen in the JORS supporting-figure screenshots.
- Location: `AnalysisITC.Avalonia/Misc/Tools/BufferSubtractionGraphControl.cs`, tick loop at line 144 and `Format` at line 333; padded range from `BuildDataRange`.
- Problem: Axis labels are placed at the quarter points of the padded data range and printed with `G3`, so they fall on arbitrary values (e.g. x: -1.2, 13.7, 28.5, 43.4, 58.2; y: 12.9, -18.1, -49.1, -80.1, -111). The x axis is injection number, so fractional and negative ticks have no meaning there.
- Implementation: Avalonia uses the shared `PublicationAxis` tick-spacing routine instead of quarter points of the padded range. Injection ticks use positive whole numbers, with a minimum step of one and fractional 2.5-injection spacing increased to five. Heat ticks retain fractional steps where appropriate. Only ticks inside the existing plot range are drawn; data, units, fit, and range padding are unchanged. Native macOS already uses `NiceScale` for tick placement; no native code was changed or visually inspected.
- Validation: Seven focused `BufferSubtractionGraphControlTests` pass, covering the screenshot ranges, one-to-three-injection ranges, fractional injection spacing, and small/positive heat ranges. The full Avalonia suite encountered the existing ITC-007 UI-thread ownership exception cascade; a clean full-suite result is unavailable. `git diff --check` passes.
- Implementation review (2026-10-10): All seven tick tests pass again. Actual Avalonia control renders at 1000 × 600 were inspected for 54- and three-injection examples: the long case shows injection ticks 20/40 and heat ticks −100/−75/−50/−25/0, and the short case shows injection ticks 1/2/3. Labels are readable and inside the control, with no fractional or negative injection ticks. No defect found in this change; native buffer-graph rendering was not inspected. Status remains pending user acceptance.

## ITC-082 - Model options leak between the experiment designer and analysis [Resolved] <a name="itc-082---model-options-leak-between-the-experiment-designer-and-analysis"></a>

Designer factories pass `useStoredOptions: false` so global `ModelFactory.PreviousAttributes` neither seeds nor receives their options. Use model defaults plus per-window edits for both simulation and designer fit factories. Both Analysis workspaces already use session-owned options. Other legacy factory behavior is separate (ITC-203).

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

## ITC-084 - Clear Processing/Results only deletes saved result rows [Resolved] <a name="itc-084---clear-processingresults-only-deletes-saved-result-rows"></a>

Separate undoable Delete All Results from Clear Processing. Prepare reset state, then verify document/revision and state after confirmation before publishing. Reject busy resets and always confirm multiple targets. Raw experiments lose calculated traces/heats, processing settings and fits; integrated-only imports retain their primary heats. Preserve inputs, injection choices, relationships, buffer setup and saved results; validity detects changed inputs. Reset Undo is ITC-088. See ITC-101 for short delays.

## ITC-085 - Processing state flickers after editing the integration slider on macOS [Resolved] <a name="itc-085---processing-state-flickers-after-editing-the-integration-slider-on-macos"></a>

Track the full native MouseDown loop explicitly in `IntegrationRangeSlider`; inferring drag state from the current event queued repeated refits. Preview silently, commit one changed range to the owning experiment at release, and process keyboard edits immediately. Preserve the tracked value during refresh and clear graph drag ownership on release/reinitialization.

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

## ITC-089 - Prominent experiment conditions in reports [Resolved] <a name="itc-089---prominent-experiment-conditions-in-reports"></a>

Show nominal syringe ▸ cell concentrations, target temperature, buffers/pH, salts, competitor and explicit ionic strength under experiment headings, including supporting and condensed sections. Omit unavailable fields, keep valid zeroes and avoid calculating ionic strength for the subtitle. Share layout across renderers and retain detailed uncertainty rows.

## ITC-090 - Uncertainty replacement validation and cancellation-state preservation [Resolved] <a name="itc-090---uncertainty-replacement-validation-and-cancellation-state-preservation"></a>

Validate root and eligible-member uncertainty evidence before publishing. Accept usable partial estimates with diagnostics, but stale combined refits or aggregates cannot substitute for member evidence. Detached preparation preserves saved results, attachments and dirty state on rejection, cancellation or failure. Publish refreshed competitor evidence only with an accepted update and reject if document revision changed. See ITC-094 for method presentation and cancellation precedence.

## ITC-091 - Control for including supporting experiments in printed reports

- Priority: Low
- Status: Open; idea (2026-10-12).
- Location: Both report windows (`AnalysisReportViewController`, `AnalysisReportWindow`); report presentation settings; `AnalysisReportBuilder` Supporting experiments chapter.
- Context: Both report builders can automatically select supporting experiments (buffer references and tandem sources) for a new draft or when checking results. Users can include or remove them in the contents picker, and saved reports restore their saved supporting selection (ITC-031/ITC-032).
- Idea: A report setting that excludes supporting experiments from the printed report without changing the stored supporting selection.
- Follow-up: Decide whether the setting also applies to AI interpretation requests (adjacent to ITC-034), whether it is saved with the report (optional `.ftxtc` presentation field), and add shared tooltip text in Core, manual text, and a report-map update.

## ITC-092 - Parameter plausibility in automatic binding assessment

- Priority: Medium
- Status: Open; planning proposal (2026-10-12).
- Problem: The automatic binding assessment uses only the saved Null comparison. It does not evaluate whether fitted Kd, enthalpy, or N support a plausible parameter interpretation. Motivating examples are Kd = 30 mM, ΔH = −50,000 kJ/mol, and N = 0.001; some are outside current Standard solver bounds. The proposed limits are chosen screening limits, not fundamental physical maxima.
- Plan: [Automatic assessment of fitted binding parameters](BINDING_PARAMETER_ASSESSMENT_PLAN.md). Proposes shared Core range checks of the original best fit, including fixed parameters, combined with the existing Null comparison and aligned desktop/output presentation. Out-of-range values give No binding detected; no new reason, warning, or persistence field is added. The reader's existing reevaluation path also uses the parameter checks.
- Open decisions: Confirm the proposed ΔH and N/active-fraction ranges and separately decide whether to raise the Standard N solver upper limit from 10 to 25. The Kd range and Competitive/Dissociation treatment follow the plan. An override after uncertainty was skipped requires Update Result to estimate it. Parameter screening remains unreleased planning work; adjacent uncertainty/update issues remain separate.

## ITC-093 - Tightening parameter limits blocks refitting an existing extreme fit [Resolved] <a name="itc-093---tightening-parameter-limits-blocks-refitting-an-existing-extreme-fit"></a>

Clamp inherited finite free starts in local/global optimizer coordinates after applying overrides and active bounds. Preserve locks and saved evidence. At exact bounds start optimizers slightly inward because exact-bound starts missed a known interior optimum; retain the normalized baseline unless the original-data objective strictly improves. Keep manual validation for new entries. See ITC-182 for ordinary-start acceptance.

## ITC-094 - Method-specific uncertainty presentation [Resolved] <a name="itc-094---method-specific-uncertainty-presentation"></a>

Show method-specific uncertainty evidence from Core. Distinguish requested bootstrap count (persisted for primary fits, unknown historically) from complete retained refits. Keep explicit no-binding skips and recorded cancellation/failure even when runs are absent; cancellation takes precedence. Mixed independent outcomes explain unavailable combined uncertainty. Update Result offers smaller/equal counts and prefers saved requests. Retained-profile feedback stays transient on a copy. See ITC-090 for replacement validation.

## ITC-096 — Dissociation concentration axes use molar-ratio coordinates in native graphs [Resolved] <a name="itc-096--dissociation-concentration-axes-use-molar-ratio-coordinates-in-native-graphs"></a>

Shared `AnalysisAxisPresentation` selects saved post-injection titrant concentrations, molar ratios, or injection numbers according to the experiment axis. Both desktops and publication figures use this mapping. Native graphs refresh when the axis type changes, synchronize residual titles and scaling, and unify horizontal ranges only across matching axis types. Final Figure and figure canvases preserve automatic axis titles unless explicitly overridden. Drawing does not rewrite saved concentrations or ratios.

## ITC-097 - Normalize displayed guesses immediately when limits change

- Priority: Low
- Status: Open; follow-up idea (2026-10-12).
- Location: Analysis parameter inspectors on macOS and Avalonia.
- Idea: After the user changes active limits, update accepted displayed guesses to the nearest selected limit immediately. Fit-start normalization currently preserves the entered or restored guess during preview and adjusts only the candidate used for a fit.
- Follow-up: Decide whether immediate display normalization should also update the owning session override, and how to retain the original guess for undo or restoring wider limits.

## ITC-098 - Fitting status does not describe what is being fitted [Resolved] <a name="itc-098---fitting-status-does-not-describe-what-is-being-fitted"></a>

Use operation-owned immutable progress snapshots with real phase and original member numbering. Parallel refit progress stays monotonic; stale delivery cannot overwrite newer progress or Stop. Acquire admission before preparation or live edits, let nested work join its owner, queue background work without an idle gap and reject synchronous unowned UI admission. Native initialization validates its captured experiment/processor/document and keeps admission through errors. Fresh solver ownership and joining refits before return underpin delivery safety. See ITC-109 and ITC-110.

## ITC-099 - Update Result keeps the existing profile result without telling the user [Resolved] <a name="itc-099---update-result-keeps-the-existing-profile-result-without-telling-the-user"></a>

Publish a missing or failed primary outcome as an error before progress completion. Cancellation or rejected profiling retains the old result with transient feedback, including evaluation temperature, and emits no successful replacement event. Partial profiles need a complete interval; for a valid existing result they can replace only an existing partial profile with no more complete intervals. See ITC-090 and ITC-094.

## ITC-100 - Fit-completion status messages contain unnecessary or unclear information [Resolved] <a name="itc-100---fit-completion-status-messages-contain-unnecessary-or-unclear-information"></a>

Completion reports shared whole-analysis and uncertainty outcomes, with filtered parameter-bound warnings between them when applicable. Retention/failure has precedence (ITC-099), using effective member assessments (ITC-053/094). Omit routine RMSD, optimizer and iteration diagnostics. The transient clock covers preparation through publication and excludes admission waits, prompts, dialog display and message delays. Keep saved timings separate. See ITC-098.

## ITC-101 - Clear Processing availability check throws for short injection delays [Resolved] <a name="itc-101---clear-processing-availability-check-throws-for-short-injection-delays"></a>

When injection delay is below two sample intervals, cap the reset lower bound at that delay so the clamp range remains valid even during menu availability checks. The reset then spans the available interval, including zero, without integrating peaks. See ITC-084 and ITC-007.

## ITC-102 - Report preview is not invalidated when a result is not in the project [Resolved] <a name="itc-102---report-preview-is-not-invalidated-when-a-result-is-not-in-the-project"></a>

- Related work includes ITC-031, ITC-032.
- The test now adds its experiment and result to the project and opens `AnalysisReportWindow(result)` through the normal selection path. It still checks that an assessment change clears the cached preview while preserving the original fit/date, and that the rebuilt report identifies the manual override. The expected `(manual)` suffix matches the existing R02 rule in `Documentation/ANALYSIS_REPORT_MAP.md`. Both desktop report windows require project ownership and close when it is lost; those guards remain intact. No report content or manual change was needed.

## ITC-103 - Avalonia reuses the fitted model for repeat fits [Resolved] <a name="itc-103---avalonia-reuses-the-fitted-model-for-repeat-fits"></a>

Repeat fits prepare fresh models with current configuration, reconstructing global constraints without copying solutions/caches. Publish new attachments and advance document revision only after successful preflight. Existing saved fits cannot be mutated by optimizer trials. Primary attachments still publish before uncertainty ends; broader detached solving is ITC-104. See [publication readiness actions](PUBLICATION_READINESS_ACTIONS.md).

## ITC-104 - Fits optimize the live, UI-reachable model instances

- Priority: Low
- Status: Deferred (2026-10-13), following the approved ITC-103/105 implementation scope.
- Location: `AnalysisWorkspace.PrepareForSolve`/`AnalysisContext` (attach prepared models to `ExperimentData.Model`); `Solver.Analyze`/`GlobalSolver.Analyze` background tasks in `Solver2.cs`; `SolutionInterface.FromModel`.
- Architectural option: Fit a private copy and publish the completed model and solution at the end. This changes primary-fit display, global relationships, cancellation and attachment behavior.
- Current boundary: ITC-103 gives each fit fresh models and protects completed results. ITC-105 excludes processing from project snapshot capture. No remaining concrete reader defect requiring detached solving was found in these checks. Revisit only with such a reproduction; no additional publication delay or rollback system is included.

## ITC-105 - Autosave can capture live fitted parameters during a fit [Resolved] <a name="itc-105---autosave-can-capture-live-fitted-parameters-during-a-fit"></a>

Capture private FTXTC bytes synchronously under idle processing admission to avoid pairing trial parameters with saved estimates. Write atomically after releasing admission, with normal identity/revision checks. Coalesce busy autosave ticks into one idle retry. Manual saves reject busy requests and a failed Save Before Close keeps the document open. See ITC-103 and [publication readiness actions](PUBLICATION_READINESS_ACTIONS.md).

## ITC-106 - Injection inclusion could change during fitting [Resolved] <a name="itc-106---injection-inclusion-could-change-during-fitting"></a>

Core `InjectionData.TryToggleDataPointActive` admits inclusion edits through the same gate used by processing admission. Busy edits do nothing and are not deferred; change notifications occur outside the gate and only after an accepted edit. The existing toggle entry point delegates to this policy. Avalonia analysis and buffer graphs use the guarded operation, and the analysis workspace marks its graph read-only while fitting. Native analysis/buffer graphs also discard presses received during processing, preventing a later mouse-up from applying them. Read-only graph hover remains available. Core and desktop regressions cover nested activity, unchanged dirty state/notifications, rejected fit/residual clicks, native press/release behavior, buffer graph events and resumed editing. The native fixture is included in the default managed test harness. This fixes inclusion editing, not the separate live-model and autosave issues ITC-103–105.

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

## ITC-109 - Status-bar progress indicator differs between platforms [Resolved] <a name="itc-109---status-bar-progress-indicator-differs-between-platforms"></a>

- The desktops used different progress indicators, and Avalonia's percentage label duplicated the new refit counter. Late reports after Stop could also make progress determinate again.
- Retained the native spinner/pie and Avalonia bar, and removed Avalonia's separate percentage label. Shared presentation uses indeterminate progress for fitting, assessment, stopping, and uncertainty without a fraction. Uncertainty with a fraction uses overall progress while the secondary counter describes the current member. Both coordinators check Stop at UI delivery so queued reports cannot restore counters or determinate progress. Active solver progress stays visible at 100% until its owning completion clears it.
- **Related.** ITC-098 owns the phase, fractions, and delivery ordering. See the [fit progress plan](ITC-098_FIT_PROGRESS_PLAN.md).

## ITC-110 - Queued processing can outlive its experiment or project

- Priority: Medium; functional follow-up to ITC-098, separate from publication work.
- Status: Open for the broader queued-work lifetime question (2026-10-15). The native Process-view admission regression is resolved within ITC-098.
- Location: macOS `DataProcessingViewControllet.InitializeData` and `UpdateProcessing`; Core `DataProcessor.ProcessData`, `DataManager.RemoveSourceItemAt`, `Clear`, and `InvalidateSolutionsForExperiment`.
- Problem: Processing requested outside an entered operation now waits for FIFO admission. Removing the experiment or closing/replacing the project does not discard that request. Two bounded Core cases confirm that the detached experiment is processed and emits two `ProcessingCompleted` callbacks after removal or clear. It is not reinserted into the project, following work completes correctly, and the gate returns to idle.
- Native entry point (resolved within ITC-098): Opening Process for an unprocessed experiment previously changed inputs before admission, queued duplicate work with integration regions excluded, and displayed a handled admission-error dialog otherwise. Native AppKit checks reproduced these regressions. Initialization now waits before changing inputs, rechecks the captured document/current experiment/processor, skips stale targets, and processes once. Avalonia's corresponding `InitializeExperimentAsync` already awaits admission and rechecks its selected target. Spline context-menu edits on both desktops already enter their operation before starting unawaited processing and retain admission through its completion.
- Reproduced consequence: A bounded temporary probe queued a production reader ahead of orphan processing, then cleared the old project. The freshly restored same-ID member/global solutions had `IsValid = true` before orphan processing and `false` afterward. The replacement's processing revision stayed unchanged, snapshot-based `IsValidForCurrentData` stayed true, the orphan stayed detached, and admission returned to idle. This demonstrates inconsistent validity flags, without demonstrating changes to replacement measurements. `InvalidateSolutionsForExperiment` searches current saved results by experiment ID. The reader must be admitted first; ordinary processing-then-reader FIFO order runs processing first. Probe source and expected failing output are preserved under `/private/tmp/itc098-queue-review/`; the temporary probe was removed from the suite.
- Remaining follow-up: Decide the relevance rule for other queued callers that can outlive their target. The Core `ProcessData` policy and the broader same-ID lifetime reproduction remain unchanged. Do not impose a blanket loaded-experiment requirement on Core processing: legitimate imports restore detached experiment objects before adding them to the project.
- Required validation for a broader fix: Cover a same-ID project restored ahead of orphan work, valid detached imports, failure/cancellation handoffs, and both desktops' relevant entry points. Preserve processing mathematics, FIFO policy and indicator behavior. Native initializer regressions are now covered separately under ITC-098.

## ITC-111 - Unsaved report drafts are lost on a crash

- Priority: Low
- Status: Open; accepted limitation (2026-10-16).
- Location: Explicit report saving (detached report drafts in both desktop report builders); `AutoSaveManager` and autosave recovery.
- Problem: With explicit report saving, edits in the report builder exist only in a detached draft until Save Report. Autosave writes registered reports only, so a crash loses unsaved draft edits, including AI-generated interpretation text. Previously, implicit registration let autosave capture them.
- Decision: Accepted for the explicit-saving change. Revisit if draft loss causes real problems, for example by autosaving drafts separately from saved reports.

## ITC-112 - Consider showing report IDs as barcodes or QR codes

- Priority: Low
- Status: Open; idea (2026-10-16). Not implementation approval.
- Location: `AnalysisReportOptions.ReportId`, report front page and footer layout, both desktop report inspectors, and `Documentation/ANALYSIS_REPORT_MAP.md`.
- Question: Should the user-entered report ID optionally print as a barcode or QR code, so printed reports can be scanned into lab systems such as LIMS or electronic notebooks?
- Open decisions: Which code types to offer, where on the page they appear, whether the plain-text ID remains beside the code, and how the option is stored with report presentation settings.

## ITC-113 - Exhausted access-code delivery leaves a provisioned registration without its code [Resolved] <a name="itc-113---exhausted-access-code-delivery-leaves-a-provisioned-registration-without-its-code"></a>

Keep a provisioned credential active after delivery exhaustion and persist failed delivery separately. Verified resubmission requeues the same encrypted code to its original address after cooldown, with one new idempotency key and fresh retry budget. Exclude ineligible deliveries and credential mismatches. Reconciliation preserves failure, and successful delivery advances independently of account-state transitions (CR-009). Mail configuration unavailability does not spend that budget (ITC-120).

## ITC-114 - Recover the primary fit when profiling finds a better candidate

- Priority: Medium; feature proposal.
- Status: Planned (2026-10-16). Plan registration only; implementation has not been requested.
- Plan: [Primary fit recovery during profile likelihood](ITC-114_PROFILE_PRIMARY_RECOVERY_PLAN.md).
- Location: Core `ProfileLikelihoodEstimator`, local/global solvers in `Solver2.cs`, `AnalysisResultUpdater`, convergence and shared fitting/uncertainty presentation; both desktop fitting and Update Result workflows.
- Problem: Profiling can find a parameter combination that fits the original data better than the primary fit. The estimator currently declares profile failure without restarting the primary fit from that candidate. The complete mixed-coordinate scenario discussed under CR-010 has not been reproduced with a real dataset; this feature does not claim a measured frequency.
- Agreed behavior: Recover automatically with a clear status message, allow up to two primary-fit restarts per owning fit, and rerun all of that fit's profiles from each accepted recovered minimum. If an improved primary fit is accepted but subsequent profiling fails, retain the improved estimates with profile uncertainty unavailable. Independent members have separate limits; a shared global fit has one group limit.
- Planning assumptions: Preserve original data, optimizer/settings, user locks, bounds, constraints, and assessment overrides; keep existing cancellation/document-retention rules. No new setting or schema field. Update Result needs a narrow exception for verified improved-primary recovery, rather than accepting every failed profile update.
- Boundary: CR-010's failed-profile numerical-consumer issue remains separate and deferred. This entry neither fixes nor closes it. Bootstrap/leave-one-out recovery, general multistart optimization, and scientific-method changes are outside the feature.

## ITC-115 - Leading orphan injection cleanup breaks indexing and concentration history [Resolved] <a name="itc-115---leading-orphan-injection-cleanup-breaks-indexing-and-concentration-history"></a>

Omit only unmarked trailing planned MicroCal shots before concentration bookkeeping and tandem construction. Reject a missing marker before a recorded shot or a recorded start strictly outside the trace; endpoint equality passes. Renumbering cannot repair lost delivered-volume history. Saved projects retain warning-only Keep/Discard without deleting injections or recomputing saved values. See ITC-107, CR-015 and CR-073.

## ITC-116 - Native bootstrap slider truncates some saved iteration counts [Resolved] <a name="itc-116---native-bootstrap-slider-truncates-some-saved-iteration-counts"></a>

A controller-owned integer draft retains the exact loaded count, supplies the label directly, and is committed by Run Fit. The logarithmic slider only represents that count; deliberate slider actions round `10^position` to the nearest integer with midpoint ties away from zero. The existing 10–10,000 drag range remains, while loaded out-of-range counts are preserved and only the knob position is bounded. Nonpositive counts avoid logarithms. View reentry reloads the live count; explicit Restore defaults reloads saved preferences; ordinary saves and Preferences Apply retain live settings and pending slider drafts. Core, Avalonia, scientific calculations, public APIs and serialization are unchanged.

## ITC-117 - Derived-parameter uncertainty across all models

- Priority: Medium; feature.
- Status: Implemented for residual bootstrap and leave-one-out (2026-11-06).
- Plan: [Derived-parameter uncertainty from retained refits](ITC-117_DERIVED_PARAMETER_UNCERTAINTY_PLAN.md).
- Problem: Derived values (Kd, −TΔS, ΔS, competitive apparent values, total ΔH, c-values) get their uncertainty from `FloatWithError` arithmetic that treats the fitted values as independent.
  - CR-029: competitive apparent Kd omits N's uncertainty.
  - Binding-model review finding 4 (2026-10-20): −TΔS = ΔG − ΔH loses the ΔG/ΔH correlation. For example, SDs of 0.3 and 1.0 kJ/mol with correlation +0.8 give about 0.78 kJ/mol, versus 1.04 from quadrature.
- Agreed design: for residual bootstrap and leave-one-out, evaluate each derived value's existing getter on every retained refit and summarize around the best fit with the existing summarizer. The full estimates are computed and stored when error estimation finishes and when refits are restored on load; there is no file-format change. Kd uses the pool; ΔG is unchanged (linear in log K). An unusable refit value omits that quantity's uncertainty.
- Scope boundary: profile likelihood keeps today's arithmetic; its own defects are [ITC-228](#itc-228---failed-input-profiles-still-give-derived-values-an-interval) and [ITC-229](#itc-229---profile-mode-apparent-values-omit-n-and-linked-affinity-uncertainty). Handled elsewhere: ITC-217 (single refit) and ITC-226 (entered input SDs).
- Constraints:
  - Preserve best-fit centres.
  - Zero error means the error doesn't exist or can't be calculated, and it isn't displayed.
  - Preserve the CR-024/SUMMARY_SEM summary convention.
  - Keep CR-010's failed-profile safeguards and ITC-114 separate.
- CR-029 remains unresolved until this feature (refit methods) and ITC-229 (profile) are implemented.

## ITC-118 - Bootstrap unlocking of locked shared parameters needs a solver policy

- Priority: Medium.
- Status: Focused implementation plan requested and written (2026-10-23), following investigation. Scientific policies are agreed; implementation has not been authorized.
- Plan: [Shared-parameter bootstrap unlocking](ITC-118_SHARED_PARAMETER_BOOTSTRAP_PLAN.md).
- Origin: Moved from [CR-071 in the October code review](CODE_REVIEW_2026-10.md#cr-071--bootstrap-unlocking-is-bypassed-when-every-shared-coordinate-is-locked), including its runtime investigation and subsequent design discussion.
- Location: Core `GlobalModelParameters.RequiresGlobalFitting`, `GlobalSolver.Analyze`, local/global `BoostrapResiduals` in `Solver2.cs`, `Model.SetSynthModelParameters`, `Parameter.Copy`/`Unlock`/`IsFitted`, and `GlobalModel.CopyUncertaintyFitTopologyTo`; both desktop applications use these shared paths.
- Trigger and problem: In a multi-experiment analysis, share and lock N or Offset, leave the remaining coordinates local, and select residual bootstrap with Unlock parameters enabled. When no other constraint requires a joint primary fit, `GlobalSolver.Analyze` dispatches to individual member solvers. Their bootstrap copies retain `IsGloballyDetermined`; clearing `IsLocked` therefore does not admit the shared coordinate to fitting. The recorded unlocking setting is enabled, but the parameter remains fixed.
- Reproduction (2026-10-17): A temporary harness exercised the actual `GlobalSolver.Analyze` route on an isolated copy of the then-current working tree, using two controlled binding experiments with 22 integrated heats each and 20 requested bootstrap refits. Both members were assessed as Binding detected and retained all 20 refits. With shared Offset locked, every refit retained -1200 and its SD was zero; shared N likewise remained 1 with zero SD. The Offset failure also reproduced with weighted fitting and with no-binding uncertainty skipping disabled. Single-experiment unlocking worked, as did shared Offset unlocking when another shared coordinate remained free. The disabled-unlocking control correctly retained the fixed value. All eight workflow probes completed; seven existing global-clone checks also passed. This was a local Core reproduction, not external forward-model validation, an interactive desktop check, or a full-suite run. Probe source, log, results, and source hashes are under `/private/tmp/cr071-investigation-br00fwyr/`.
- Consequence and test gap: The primary estimates stay at their requested locked values, but the shared parameter has zero bootstrap spread despite requested unlocking. Other fitted parameters cannot compensate for its variation; the effect on their uncertainty magnitudes has not been quantified. `GlobalUncertaintyCloneTests` exercise the correct global helper directly and miss the actual analysis dispatch.
- Agreed direction (2026-10-23): Unlocking removes parameter locks while preserving sharing and other model constraints. A shared N or Offset remains one common coordinate within each bootstrap replicate and may vary between replicates. Joint bootstrap refits retain every experiment in the original analysis, including members assessed as no binding; no member may be silently removed to simplify uncertainty estimation. Preserve the original-data primary best-fit centers.
- Planning direction (2026-10-23): Following the investigation, the user requested a minimal plan using the existing deferred member-uncertainty path and parent joint bootstrap routine. Skip the whole joint bootstrap for no binding only when every effective member assessment is No binding detected, honoring manual overrides; otherwise retain all experiments. Preserve existing replicate admission and cancellation behavior. No new routing framework or broader solver redesign is included. See the linked plan for the proposed changes, validation and agreed constraints.
- Alternative discussed: Run the whole solver globally whenever shared constraints exist, even when the shared parameters are locked. This could simplify the primary/bootstrap flow, but replaces separate low-dimensional primary optimizations with one larger optimization over all free local coordinates. Performance and convergence costs have not been measured. The current global/independent decision also selects pooled versus per-member assessments and no-binding uncertainty skipping, so adopting this approach would require an explicit policy for those behaviors.
- Investigation (2026-10-23), existing machinery: `GlobalSolver.Analyze` already supports deferring member uncertainty until primary fits and assessments finish. `GlobalSolver.BoostrapResiduals` can then run directly against that assembled primary solution; it does not require a second primary solve or call `Analyze` on each replicate. It already clones the complete member set and constraints, fits each replicate with `GlobalSolver.Solve`, admits only converged complete refits, assigns replicate indices, and distributes samples through `GlobalSolution.SetBootstrapSolutions`. Existing clone topology and uncertainty summarization preserve the original primary values. A new scheduler, routing strategy hierarchy, solver type, or duplicate bootstrap loop is not indicated.
- Smallest candidate, not an implementation plan: Within the existing individual-fit branch, recognize residual bootstrap with effective unlocking and a locked coordinate in the global parameter table; defer member uncertainty even when no-binding skipping is disabled, assemble the primary `GlobalSolution` without reconstructing member bootstraps, and use the existing parent uncertainty method instead of the member uncertainty loop. Keep the ordinary independent and already-global paths. Reuse the existing no-binding assessment pass and preserve its results. Do not call the parent `Solve` to start uncertainty: that would refit the original data and replace the member primary solutions. Do not rebuild the root from member uncertainty outcomes afterward, since summing their counters would count each joint replicate once per member.
- Necessary bookkeeping: The existing global bootstrap routine writes the root uncertainty outcome, but independent primary member convergence records also need that joint outcome, counts, limits and cancellation state. Their primary termination, objective, RMSD and fit time must remain their own. Existing `ApplyErrorEstimationResult`, `CaptureBootstrapRequest` and skip methods supply these operations; a new status representation is not indicated. Update Result rejected a probe with usable joint samples but untouched member outcomes, and accepted the partial joint result after member outcomes were populated using the existing API.
- Scope coupling to handle narrowly: `PublishProgress` infers scope from primary `ShouldFitIndividually`, so the reused parent bootstrap currently emits Individual progress; the uncertainty phase needs joint scope without relabelling the primary fit. `UncertaintyEstimationPresentation.CompletionOutcome` sums member counts for independent fits; a controlled 19-of-20 joint-refit presentation probe reported "38 of 40 refits". The selected-member shortcut in `BootstrapCorrelationAnalyzer.Analyze(GlobalSolution, ...)` also uses primary topology and needs review for shared-coordinate selection and joint diagnostics. Preserve primary assessment scope, information criteria and residual generation: `ExperimentData.GetBootstrappedResiduals` correctly uses the original primary fit's local residual degrees of freedom here. Do not globally redefine `RequiresGlobalFitting`/`ShouldFitIndividually` to mean bootstrap scope.
- Storage and consumers: Existing `.ftxtc` member snapshots, replicate indices and optional `globalReplicates` already represent the common unlocked coordinates and complete membership; source tracing indicates no format extension is needed. Actual global refits retain their global parameter table, whereas the old independent common-index reconstruction lacks it. Any result-side classification should respect that saved evidence rather than infer that historical unlocking requests were honored. Both desktop applications consume the shared solver/progress/presentation paths; save/reopen, Update Result, partial/cancelled outcomes and affected correlation views still need validation with an eventual implementation. No desktop interaction or serialization round trip was performed in this investigation.
- Runtime evidence (2026-10-23): Built current Core/tests successfully, then used a temporary caller of existing routines, with no production or test-source edits. Four actual `GlobalSolver.Analyze` probes (shared locked N/Offset, weighted/unweighted) each retained 20 refits with no shared-coordinate variation. Calling the existing global bootstrap routine directly on those primary results retained 20 joint refits in each case; the shared coordinate varied across refits, was identical across both members within each refit, and both model parameter values and reported primary centers remained unchanged. A mixed Binding detected/No binding detected probe retained both experiments in joint refits. Seven existing clone checks also passed. These are controlled workflow/reuse probes, not a solver benchmark, full-suite run, implemented dispatch fix, or external forward-model validation. Caller source, build/run logs and results: `/private/tmp/itc118-investigation/` and `/private/tmp/itc118-build.log`.
- Confirmed constraints and boundary (2026-10-23): The user confirmed preserving the existing no-binding skip setting (disabled means run even for an all-no-binding group), optimizer budget, and assessment policy for already-global primary fits. No budget increase or adjacent redesign is approved. Preserved sharing, complete membership, the all-members no-binding gate, and existing admission/cancellation policies are also settled. Implementation must verify the identified metadata, scope/count presentation, correlation and save/reopen integration points. Related ITC-052, ITC-083, ITC-090, ITC-094 and ITC-117 remain outside this focused change.

## ITC-119 - Review duplicate-import prompt close handling

- Priority: Low; review question.
- Status: Open question (2026-10-17). Recorded during CR-070 simplification; no implementation change requested.
- Location: `AnalysisITC.MacOS/Platform/MacFtxtcDuplicatePromptService.cs`, `ChooseAction` and `CreateAlert`; existing `FtxtcDuplicatePromptTests` harness.
- Question: Does the duplicate-import prompt need its current `WillCloseNotification` → `AbortModal` observer alongside its key-event monitor and explicit observer cleanup?
- Evidence boundary: Unlike the Final Figure overwrite alert, `CreateAlert` explicitly adds `NSWindowStyle.Closable`. Its user-close path therefore cannot be dismissed as impossible merely because it uses `NSAlert`. No redundant handling or runtime defect has been established.
- Follow-up: Review actual close and keyboard behavior before proposing a change. Keep this prompt and its harness outside the CR-070 simplification.

## ITC-120 - Registration mail configuration failures bypass the retry budget [Resolved] <a name="itc-120---registration-mail-configuration-failures-bypass-the-retry-budget"></a>

Validate mail configuration before provisioning or sending. An unavailable configuration leaves queue rows and retry budget intact; the worker retries the readiness check after a minute and resumes on repair. Health and sender share typed validation. Provider delivery failures and resubmission retain their existing retry rules (ITC-113).

## ITC-121 - Same-named inconclusive members collapse in app health reasons

- Priority: Low.
- Status: Open; recorded during CR-023 report pagination work (2026-10-18). Shared app display behavior is outside that change.
- Location: `AnalysisITC.Core/Presentation/AnalysisResultHealthReasonFormatter.cs`, `Format`; Avalonia Results workspace/main window and macOS result tab.
- Problem: The app formatter applies sentence-level `.Distinct()`. Two independently assessed inconclusive members with the same solution name produce the same sentence, so only one reason line remains visible.
- Evidence: CR-023 regression coverage creates two same-named inconclusive members. Structured reasons retain both member indices; the existing app text projection still returns one inconclusive sentence. Report R03 groups the structured reasons and preserves both report references.
- Follow-up: Decide how app views should distinguish same-named members before replacing sentence-level deduplication. Keep the existing deduplication of shared fit-warning sentences and avoid changing assessment outcomes.

## ITC-122 - FloatWithError formatting loses the missing-value marker [Resolved] <a name="itc-122---floatwitherror-formatting-loses-the-missing-value-marker"></a>

Preserve `FloatWithError.NaN` through scalar multiplication/division. Shared formatting renders missing values as Unavailable without units or uncertainty. Check missing affinity before transformation so it cannot become Kd = 1 M. Legitimate zero remains numeric; table export keeps blank missing cells. Salt-component availability is separate (CR-020).

## ITC-123 - No-binding interpretation restriction overwritten when traces are absent [Resolved] <a name="itc-123---no-binding-interpretation-restriction-overwritten-when-traces-are-absent"></a>

Compose trace-availability and No-binding clauses independently in canonical and transmitted evidence, so removing thermograms cannot drop the attempted-fit restriction. Repeated updates must be idempotent. Corrected canonical evidence may stale saved interpretations; retain text and original fingerprints. See CR-025 and CR-027.

## ITC-124 - Mark Final Figures when the displayed fit no longer matches the data

- Status: Potential future feature (2026-10-18). Logged at the user's request; implementation is not approved or scheduled.
- Investigation: [Final Figure freshness marking and comparable software](ITC-124_FINAL_FIGURE_FRESHNESS.md).
- Request: Add a visible mismatch notice to exported Final Figures and a second, preferably invisible record that may remain if the visible notice is removed during subsequent editing.
- Preferred survival order (confirmed 2026-10-18): Vector editing in an application such as Illustrator, Affinity Designer or Inkscape first, then subsequent placement into PowerPoint, Word or a manuscript. Specific applications and export methods remain to be selected and tested.
- Current evidence: A constructed Core probe retained the fit curve without a freshness annotation or metadata after temperature and integrated-heat changes. Saved-result input comparison detected both changes; the attached solution's validity flag alone missed the direct temperature edit. Both desktop PDF writers already support metadata. This is a Core/source investigation, not an interactive desktop or external-editor reproduction.
- Related policy: macOS currently blocks associated exports for non-valid results; Avalonia's equivalent path lacks that check. Reconciliation remains a design question within this feature, not an approved fix.
- Comparable software: Origin marks pending recalculation, Prism updates linked analyses and labels deliberately frozen sheets, and Excel visibly formats stale calculations. Matplotlib supports PDF metadata and page notes placed outside the page for invisible print output; OriginLab's Word app can add graph-source metadata to alternative text. The linked investigation records primary sources and separates application indicators from export behavior.
- Open decisions: Matching evidence for standalone fits; changed versus unknown freshness; Binding/Null/model-free and independent/global scope; visible warning presentation; export eligibility; hidden-record contents and inspection; relationship to Traceability Mode; and specific applications and export methods for the preferred editing workflows.
- Limit: Hidden metadata and PDF notes are removable and may be lost during conversion. They describe status at export, do not authenticate the figure, and must not be promised to survive arbitrary editing. No implementation, refitting, export-policy, serialization or manual/help change is authorized by this entry.

## ITC-125 - Desktop result options can mislabel zero stoichiometry as one to one [Resolved] <a name="itc-125---desktop-result-options-can-mislabel-zero-stoichiometry-as-one-to-one"></a>

The shared Results formatter reads only `DoubleValue`. Matching presets retain their existing names and 1e-9 matching tolerance. Other finite values, including zero and negatives, display as compact G6 numbers using desktop culture; non-finite values display as "Unavailable". Formatting preserves the saved fields. Model equations, factory defaults, setup selectors, shared preset selection, project serialization, and viewer formatting are unchanged. This is a presentation correction, not a change to input validity or fit eligibility.

## ITC-126 - Native export sheet does not initialize Active or All scope correctly [Resolved] <a name="itc-126---native-export-sheet-does-not-initialize-active-or-all-scope-correctly"></a>

- Related work includes CR-052.
- **Consequence.** An export requested for Active or All can instead contain only the current experiment unless the user selects the intended scope in the sheet.
- Initialize `SelectedSegment` directly from the requested scope's index. This is the only production change; subsequent scope handling, eligibility, correction drafts, persistence and export calculations are unchanged.

## ITC-127 - Final Figure preview repeats a PNG encode/decode round trip

- Priority: Low; performance follow-up.
- Status: Open; recorded by request during bootstrap-display optimization (2026-10-19). Record only; no conversion change is included in that work.
- Location: `AnalysisITC.Avalonia/Workspace/FinalFigure/FinalFigureWorkspaceControl.cs`, `ToAvaloniaBitmap` (previously around line 1415).
- Problem: Each rendered Skia preview is encoded to PNG and decoded into an Avalonia bitmap. Earlier component probes measured approximately 100 ms for this bridge, including revisits after bootstrap predictions are cached. This is an observed timing, not a fixed threshold.
- Consequence: Visible Final Figure refreshes retain this cost even after hidden previews are deferred and model evaluation is reused. Review direct bitmap transfer separately while preserving rendered pixels and bitmap ownership/disposal.

## ITC-128 - Temporary analysis messages belong in the status bar

- Priority: Low; presentation follow-up.
- Status: Open; recorded by request during bootstrap-display optimization (2026-10-19). Record only; no message-routing change is included in that work.
- Location: `AnalysisITC.Avalonia/Workspace/Analysis/AnalysisWorkspaceControl.cs`, temporary fit status presentation and inspector footer.
- Requested behavior: Temporary fit messages should appear in the status bar rather than the Analyze Data inspector footer. Keep persistent analysis information distinct from temporary progress and completion messages.

## ITC-129 - Competitive bootstrap samples competitor concentration regardless of the concentration-error option [Closed] <a name="itc-129---competitive-bootstrap-samples-competitor-concentration-regardless-of-the-concentration-error-option"></a>

- Competitor concentration is an uncertain model option and is sampled through the same common sampler as affinity, enthalpy and other option errors during residual bootstrap. The concentration-error checkbox separately controls cell and syringe concentration variation; it does not gate model-option errors. Keep that scope and the existing common sampler. Options with zero error remain fixed, and primary best-fit inputs are unchanged.
- **Follow-up.** Negative competitor-concentration draws and their failure/retention policy are a separate open question under ITC-162.

## ITC-130 - Two-site syringe mode disables an independently used fixed site count [Resolved] <a name="itc-130---two-site-syringe-mode-disables-an-independently-used-fixed-site-count"></a>

Share one Core policy across both Analysis views and Designers. Syringe correction enables both independently used fixed counts and disables Share N-values. Outside syringe correction, sharing controls fitted stoichiometries and fixed counts are disabled. Preserve entered values and checked sharing state when toggling.

## ITC-131 - Competitive initialization omits reuse of the attached affinity estimate [Resolved] <a name="itc-131---competitive-initialization-omits-reuse-of-the-attached-affinity-estimate"></a>

Use `PreviousOrDefault(Affinity1, GuessLogAffinity())` for Competitive Binding, including valid zero log-affinity. Explicit overrides take precedence. Native previews honor workspace reuse policy so Restore defaults shows the default without discarding the attached solution. Fit preparation retains its existing reuse boundary.

## ITC-132 - One-site heat content loses precision through subtractive cancellation [Closed] <a name="itc-132---one-site-heat-content-loses-precision-through-subtractive-cancellation"></a>

Retain one-site heat arithmetic because independently measured cancellation errors were negligible for plausible inputs (≤ 6.9e-12 µcal per injection). Extreme weak-binding or forced high-affinity near-equivalence inputs can still lose precision or yield non-finite predictions. Rationalizing the outer subtraction alone does not fix the discriminant failure.

## ITC-133 - Microcalorie scale is missing from the shared Energy scaling API

- Priority: Low; latent API inconsistency.
- Status: Open (2026-10-20); binding-model review finding 6. Static review only; no builds or tests.
- Location: `AnalysisITC.Core/UnitClasses/Energy.cs`, `ScaleFactor`, `Energy(FloatWithError, EnergyUnit)`, `GetMod` and `ToFormattedString`; `FloatWithError.AsFormattedEnergy` in `Math/NumberStructs.cs`.
- Problem: `ScaleFactor(EnergyUnit.MicroCal)` falls through to 1, the joule scale; `GetMod` delegates to it. The unit-aware constructor and shared formatted-energy path therefore use the wrong scale for MicroCal, potentially showing joule magnitudes with a µcal label. `ToUnit`, `ToString(unit, ...)`, `ConvertToJoule` and `ConvertFromJoule` have explicit correct MicroCal cases.
- Reachability qualification: No active user-facing caller of the defective MicroCal paths was established. Figure/result-export override validation rejects MicroCal, and the inspected import/export MicroCal calls use explicit conversions. This is a confirmed API inconsistency, not a demonstrated current figure/report failure.
- Suggested direction: Add the MicroCal scale `1_000_000.0 / CalToJouleFactor` to `ScaleFactor`, which also corrects `GetMod`, and check constructor/formatter consistency against independent unit expectations. Preserve the existing restriction on figure/result-export overrides; correcting conversion does not imply enabling a new display option.

## ITC-134 - Thermodynamic conversion constants are truncated [Resolved] <a name="itc-134---thermodynamic-conversion-constants-are-truncated"></a>

Use exact SI Boltzmann × Avogadro for `Energy.R` (8.31446261815324 J/(mol K) in double precision), and `Math.Log(10.0)` for log₁₀ SD propagation. Update exact-byte export baselines only for independently isolated numerical changes. [NIST CODATA](https://physics.nist.gov/cuu/Constants/Table/allascii.txt).

## ITC-135 - Stoichiometry guesses depend on a stored plotting-axis coordinate [Resolved] <a name="itc-135---stoichiometry-guesses-depend-on-a-stored-plotting-axis-coordinate"></a>

Guess N from half the last injection's physical titrant-to-cell concentration ratio. Plotting coordinates may instead hold concentration after a Dissociation edit. Preserve attached-result reuse, overrides and limits. Synthetic fixtures need physical concentrations even when they already have plotting ratios.

## ITC-143 - Duplicate root IDs can produce a project that cannot reopen [Resolved] <a name="itc-143---duplicate-root-ids-can-produce-a-project-that-cannot-reopen"></a>

Use the reference-preserving Skip Duplicates / Import Copies resolver for both project import formats (ITC-077). Reject empty/conflicting root IDs before writing or replacing the destination. Preserve legitimate sharing of saved fits. The writer does not silently rename or discard distinct objects.

## ITC-144 - Appending a project replaces the current Save destination [Resolved] <a name="itc-144---appending-a-project-replaces-the-current-save-destination"></a>

`DataReader.ReadPathsAsync` now chooses the destination at the import boundary; `FTXTCReader.ReadPath` no longer assigns it. Appending preserves the current destination, including an unnamed or report-only document. A clean single-project open into an empty document adopts its source, including empty and report-only projects. Opening multiple files together into an empty document requires Save As; a partially recovered single-project open remains unnamed. Skipped imports preserve the current destination, and appending during a save retains document identity and newer unsaved changes. The regression now verifies the merged original and byte-for-byte preservation of the appended source. The native AppKit append workflow, manual source, and shared in-app help use the agreed policy. No wire-format changes.

## ITC-145 - Saving an unintegrated experiment loses its stored inclusion choice [Resolved] <a name="itc-145---saving-an-unintegrated-experiment-loses-its-stored-inclusion-choice"></a>

- **Qualification.** The defect is loss of the underlying choice across save/reopen. An unintegrated experiment already reports false through the getter before saving; this is not evidence that the checkbox visibly displayed true at that point.
- The shared FTXTC snapshot reads the original choice through the internal read-only `ExperimentData.StoredInclude` accessor. The public eligibility gate, setter, reader assignment, desktop controls, and scientific processing remain unchanged. Both inclusion choices survive reopening; existing saved false values are restored literally. The boolean field, schema, and format version are unchanged. The maintainer format documentation explains the distinction.

## ITC-146 - Web byte budgets charge referenced project payloads twice [Resolved] <a name="itc-146---web-byte-budgets-charge-referenced-project-payloads-twice"></a>

- ZIP validation charged every expanded JSON/binary entry, then manifest/project reads and root-reference validation charged most payloads again. Configured cumulative byte limits could therefore reject projects below their intended size limits.
- Cumulative JSON/binary quotas charge each normalized, case-sensitive entry path once across entry validation and reference traversal, including manifest/project JSON and bootstrap matrices. Configured defaults, absolute archive/entry protections, authentication, cancellation, and root/sample/bootstrap/injection limits remain unchanged. Sample counts still charge shared trace/baseline restoration; bootstrap and injection counts still charge separate result contexts. No wire-format change. The format documentation and limit-property comments define these semantics.

## ITC-147 - Null-comparison serialization drops detail without logging the first failure [Resolved] <a name="itc-147---null-comparison-serialization-drops-detail-without-logging-the-first-failure"></a>

`DataManager.InvalidateSolutionsForExperiment` also invalidates paired Null fits by experiment ID; `FTXTCReader.RestoreNullComparison` derives Null validity from its paired saved binding fit when available. Other members retain their validity. Fit-time conventions, captured evidence, success flags, assessments and overrides remain unchanged; explicit refitting uses the current convention for both fits. `FTXTCWriter.CaptureNullComparisonElement` no longer substitutes status-only evidence: capture failures reach existing save-error/logging handling before replacing the destination. No new stale state, UI, public API, schema version, or fitting changes. `NullComparisonBookkeepingPersistenceTests` and `NullModelContractTests` cover actual fitting through bookkeeping changes and two strict round trips for all three methods, attached/single/pooled/independent fits, detached Null data, mixed validity, retained evidence and overrides, refitting, and failed saves preserving existing files and dirty state.

## ITC-148 - Reopening changes the embedded Null solution's heat-method provenance [Resolved] <a name="itc-148---reopening-changes-the-embedded-null-solutions-heat-method-provenance"></a>

Restore a detached Offset Null model with its paired saved binding model's heat convention, matched by experiment ID. If Save Selected omitted that fit, fall back to the embedded method, then the historical default. Current processing settings cannot substitute for saved provenance. See ITC-221 for the separate embedded ID issue.

## ITC-149 - Published experiment schema rejects saved tandem provenance fields [Resolved] <a name="itc-149---published-experiment-schema-rejects-saved-tandem-provenance-fields"></a>

`Documentation/Schemas/FTXTC/component.schema.json` now declares the optional nullable description string and optional nullable source-ID array, whose items use the existing nonempty-ID definition. All other schema content, the writer, reader and format version remain unchanged. `TandemSourceProvenanceTests` validates actual writer output for all four merge modes and three concatenated-import paths. `FtxtcJsonSchemaValidationTests` covers ordinary experiments without provenance, absent/null/empty/populated values, and rejection of invalid property types, non-string/null array items and empty IDs.

## ITC-150 - Native Final Figure ignores the experiment-details default [Resolved] <a name="itc-150---native-final-figure-ignores-the-experiment-details-default"></a>

- Native Final Figure treated the fit-parameter preference as the experiment-details default and coupled model/parameter visibility to details. Later, applying only model-info or parameter-category changes could leave selected content absent.
- Native General has independent Experiment details, Model info, and Fit parameters switches that use their existing preferences and preserve hidden subgroup selections. Preview, export, and Supporting Figure capture share the resulting content flags. Both apps use Core tooltips.
- **Follow-up.** `MacSettingsApplier.Apply` recalculates box visibility after model-info, fit-parameter-default, or category changes, and category changes invalidate the figure. Native Preferences regressions verify actual Apply actions and rendered output.

## ITC-151 - Native Final Figure dimension parsing silently changes entered numbers [Resolved] <a name="itc-151---native-final-figure-dimension-parsing-silently-changes-entered-numbers"></a>

`FinalFigureOptionsController` parses complete numeric tokens with an optional case-insensitive `cm` suffix, decimal comma or point, and scientific notation, without grouping or mixed separators. It validates finite values against inclusive width 3–20 cm and height 4–28 cm before converting to the renderer's float representation. Dimensions commit independently. `FinalFigureViewController` retains each rejected draft and its previous live dimension through ordinary refreshes, tab changes and settings notifications; invalid fields are red with a range tooltip. Valid fields use culture-aware round-trip formatting. Existing Preferences ranges/defaults, Core, serialization and ITC-150/152 behavior are unchanged by this fix. The manual and shared in-app help state the figure-control ranges.

## ITC-152 - Native Final Figure does not apply changed fit-line smoothness preferences [Resolved] <a name="itc-152---native-final-figure-does-not-apply-changed-fit-line-smoothness-preferences"></a>

After successful explicit Preferences Apply, `MacPreferencesWindowController` copies the saved mode to native figure state before the existing invalidation. `FinalFigureViewController` synchronizes only its smoothness selector on the existing Invalidated event and detaches that handler on disposal. It uses the selected segment index, independently of storyboard tags. `MacSettingsApplier` remains unchanged: ordinary settings saves and document Save As preserve manual figure choices. Startup, existing captures, interpolation algorithms, renderer, public APIs, preference keys and serialization are unchanged. Existing manual coverage is sufficient.

## ITC-153 - Analysis Results remain valid after source experiments leave the browser [Resolved] <a name="itc-153---analysis-results-remain-valid-after-source-experiments-leave-the-browser"></a>

Active-document result validity checks that every original source object still belongs to the browser. A missing source makes the whole result Invalid, even for independent fits or without a saved snapshot. Equal IDs do not substitute for object membership. Undo recomputes validity; detached reads retain saved-input comparison. Existing output consumers use the status.

## ITC-154 - Native Analysis Result Exporter has no temperature-unit choice [Resolved] <a name="itc-154---native-analysis-result-exporter-has-no-temperature-unit-choice"></a>

Add a native Temperature popup with Celsius/Kelvin, defaulting to Celsius, and pass its selection through the existing export options for both copy and file output. Both desktops use shared Core labels and tooltip text. Correct the native option-row width anchors to use an inset rather than a negative multiplier, so the controls remain visible after sheet layout. Temperature selection only changes the displayed unit; summary evaluation still uses the existing default evaluation temperature, and replicate rows retain their saved fitted temperatures. Scientific calculations, results, preferences, serialization and quick Copy Result Table are unchanged. Manual and in-app export guidance clarify the display-only choice.

## ITC-155 - Remove the unused affinity-input preference [Resolved] <a name="itc-155---remove-the-unused-affinity-input-preference"></a>

- **Finding.** Neither app exposed an affinity-input mode control. The native `ParameterOptionControl_Activated` method was unreferenced and unconnected. Avalonia already used Kd unconditionally; native editors had an alternate log10 association-constant path reachable only through the hidden stored preference. This was not a demonstrated mismatch reachable through ordinary controls.
- Keep Kd entry in both apps, using the selected concentration unit. Removed the preference from settings, persistence and refresh handling, removed the unused native handler, and made native formatting, parsing and unit labels unconditionally use Kd. Obsolete stored keys are ignored. No selector was added; canonical internal log10 association-constant coordinates and fitted values are unchanged.

## ITC-156 - Selective Preferences Apply and session temperature units [Resolved] <a name="itc-156---selective-preferences-apply-and-session-temperature-units"></a>

Apply only draft fields changed from the opening baseline, merging into current settings and refreshing affected controls. Restore Defaults explicitly stages all fields. Persistence failure restores memory and retains the draft. Results and exports have separate session temperature choices; changing the shared persistent default resets both, preserving the physical evaluation temperature. Migrate the native legacy default only when the new key is absent.

## ITC-157 - Desktop command wording and capitalization are inconsistent

- Priority: Low; UI consistency.
- Status: Partially addressed locally (2026-10-30). Native menu capitalization now follows title case, including toolbar, context and processing menus and storyboard-defined items. Common command wording, ellipses and push-button capitalization remain open.
- Capitalization validation: Native managed code and storyboard compile; all 25 archived processing-copy cases and the native settings harness pass. The Publish content harness passes 21 of 22 groups, including dispatch through the renamed attribute-menu items; its remaining failure concerns model/parameter-box visibility with an empty parameter selection, outside the capitalization changes. The five code/storyboard/test files differ from their pre-edit snapshots only in letter case; storyboard IDs, actions, shortcuts and layout are preserved. No mouse-driven verification was performed.
- Origin: UI review finding 8.
- Location: Native `ViewControllers/MainWindowController.cs` context/toolbar menus and `Main.storyboard`; Avalonia `Misc/Menus/AppMenuController.cs`, `Misc/Details/AnalysisResultDetailsWindow.cs` and tool/workspace push buttons.
- Problem: Native menus mix sentence/title case and use different names from Avalonia for equivalent commands. Push buttons such as `Add selected`, `Save package`, `Restore defaults` and `Copy result table` violate the repository's title-case rule. `Set Active Experiments` also appears as `Select result experiments`; dialog-opening commands mix `...` and `…`.
- Suggested direction: Agree common names for commands with the same behaviour and one ellipsis convention, then apply title case to menus/push buttons and sentence case to inspector labels/checkboxes. Put genuinely shared command wording in Core where appropriate and update native storyboard and runtime-created items together. Keep command behaviour and shortcuts unchanged.
- Verify: Main menus, context/toolbar menus and matching window buttons on both desktops, including dynamically created items.

## ITC-158 - Shared desktop tooltips are duplicated, inconsistent or absent

- Priority: Low; presentation and maintainability.
- Status: Open (2026-10-20). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: UI review finding 9 and the exporter tooltip portion of finding 5.
- Location: Both Supporting Figure windows, Preferences number precision, Analysis Report/package controls, result exporters and identifier editors; Avalonia `MainWindow.axaml` All/None, Raw Data/Injections and Open File/Reload Last File controls.
- Problem: Matching tooltip text is independently hard-coded in both apps, some equivalents drift, several static explanations exceed the 1-10 word guidance, and the listed Avalonia main-window controls have no assigned tooltip. Extra traceability is debug-only and must not be presented as a production feature defect.
- Suggested direction: Define matching tooltip text once in Core and assign it in both consumers; add concise, useful text to the identified missing controls. Keep ordinary tooltips within 1-10 words, retaining the permitted longer value/unit/unavailability explanations and useful accessibility help. Move long scientific explanations to suitable help only when they aid interpretation. The duplicated identifier `ApplySharedValue` helper is optional refactoring, not required to fix tooltip drift.
- Verify: Matching controls use the same shared text, formerly missing controls have meaningful tooltips, and debug-only content remains correctly scoped.

## ITC-159 - Avalonia removal confirmations omit the suppression shortcut

- Priority: Low; dialog convenience parity.
- Status: Open (2026-10-20). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: UI review finding 11.
- Location: `AnalysisITC.MacOS/ConfirmationDialog.cs` (`ConfirmRemoveOrDelete`); Avalonia `Misc/Dialogs/ConfirmationDialogWindow.cs` and `MainWindow.ConfirmAsync`.
- Problem: Native removal/deletion confirmations offer `Do not show this again` and save `ConfirmRemoveDelete = false` after confirmation. Avalonia omits this dialog shortcut, although it already honours the setting and exposes it in Preferences. This is not an ignored preference or missing destructive-action confirmation.
- Suggested direction: Add equivalent optional suppression to the removal/deletion confirmation path and save it only after an affirmative response. Keep cancellation and window closure non-destructive. Do not automatically extend suppression to processing-clear, report-delete or other confirmation policies.
- Verify: Confirm/cancel/close with the option on/off, saved preference restoration and unchanged unrelated confirmation behaviour.

## ITC-160 - Native app-lifetime views incompletely detach static events

- Priority: Low; lifecycle maintenance, with no demonstrated ordinary-workflow leak or crash.
- Status: Open (2026-10-20). Missing cleanup confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Corrected scope of UI review finding 12.
- Location: `AnalysisITC.MacOS/ViewControllers/MainViews/DataProcessingViewControllet.cs` (`ViewDidLoad`, `Dispose`), `MainViews/ViewController.cs`, `ViewControllers/MainWindowController.cs` and `GraphViews/ResultGraphView.cs`.
- Problem: Static-event subscriptions outlive the cleanup implemented for these views/controllers. For example, processing Dispose removes two DataManager handlers but leaves `SelectionDidChange`, print, processing and graph subscriptions; ResultGraphView uses anonymous static-event handlers that cannot currently be detached. These objects are mostly app-lifetime storyboard instances, so missing cleanup alone does not establish a growing production leak. `TemperatureDependenceGraphView` has no identified instantiation and is not evidence of an active leak.
- Suggested direction: Match subscriptions to actual ownership/lifetime and detach them when the owning object is disposed, using named or retained delegates where needed. Preserve required app-lifetime subscriptions; avoid introducing a general event-management framework solely for this cleanup.
- Verify: Exercise the actual dispose/recreate path where supported, confirm disposed views receive no later callbacks and live replacements receive each event once.

## ITC-161 - Desktop unit and numeric-label formatting still differs

- Priority: Low; display consistency.
- Status: Open (2026-10-20). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: UI review finding 13; adjacent to resolved ITC-079, whose scope did not include these native locations.
- Location: Native `ViewControllers/ExperimentDesignerViewController2.cs` and `Drawing/GUIutils.cs` volume text; both Preferences iteration labels; native `DataProcessingViewControllet` and Avalonia `ProcessingWorkspaceControl` integration labels; both Designer injection-volume summaries.
- Problem: Native Designer/cursor text uses `µl` while Avalonia uses `µL`; iteration counts use native `0` versus Avalonia `N0`; integration-time labels omit the native space before `s`; Designer volume lists use native `F1` versus Avalonia `G4`. These are presentation differences, not established changes to stored quantities or calculations.
- Suggested direction: Use `µL` consistently in user-facing native text and include a space before time units. Agree common count formatting and adequate volume-summary precision, using shared Core formatters where applicable. Preserve numeric values, scientific calculations, serialized/export tokens and external-format compatibility.
- Verify: Matching labels and summaries on both desktops, including small injection volumes and locale-dependent count grouping; keep data/export formatting policies separate.

## ITC-162 - Handling negative competitor-concentration draws during bootstrap

- Priority: Medium-low; scientific sampling-policy question.
- Status: Open question (2026-10-20), split from ITC-129 by maintainer request. No handling change has been selected or implemented.
- Location: `FloatWithError.Sample` / `Distribution.SampleSplitNormal`, Core `Model.SetSynthModelParameters`, `CompetitiveBinding.TryEvaluateCompetitive`, and the local/global residual-bootstrap loops in `Solver2.cs`.
- Observation: The common option sampler can draw a negative competitor concentration. With a deliberately large SD of 50% of the positive input, a deterministic production-cloning probe observed six negative values among 256 single-model draws and eleven among 512 member draws across 256 two-member global clones. These synthetic examples do not establish the frequency in ordinary experiments.
- Current handling: Competitive evaluation rejects negative competitor concentration. The local/global bootstrap loops catch replicate failures, exclude them from retained uncertainty samples, and count them as failed refits. A read-only probe using real single-experiment and pooled-global solvers with controlled random streams verified the accounting: 16 attempts with eight negative draws retained eight refits and counted eight failures; 16 negative draws retained none and counted 16 failures. In all four runs the primary fit succeeded and its parameter value was unchanged through bootstrap. These controlled cases verify handling, not ordinary-workflow frequency.
- Open question: Retain rejection as a failed refit, redraw invalid option values from the same common sampler with an explicit stopping rule, or require different handling at input? Define whether any retry redraws only the invalid concentration or the complete synthetic replicate. Rejecting or redrawing invalid values conditions the retained population; explain that consequence without changing the primary best-fit input.
- Boundary: Preserve ITC-129's intentional common option sampler and the cell/syringe checkbox scope. Do not silently substitute zero, clamp negative values, or introduce a different distribution as an incidental fix. Any change needs an explicit scientific policy and focused tests.
- Next verification: Once a handling policy is chosen, add focused regression tests through the real local/global bootstrap entry points for attempted/successful/failed counts, retained inputs, cancellation and all-invalid behavior. Preserve the original best-fit center and verify that valid uncertain options and zero-error options retain their agreed behavior.
- Evidence: `/private/tmp/itc129-option-sampling-probe.log`, `/private/tmp/itc162-refit-accounting.log`, and source inspection of evaluation and bootstrap exception paths. No implementation code was changed.

## ITC-163 - TA injection duration mixes volume and time units

- Priority: Low.
- Status: Open question (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, A2; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `InjectionData.FromTAFileLine` (`DataClasses/InjectionData.cs:90–111`); `TAFileReader.AddInjection`; `PeakShapeIntegrationEstimator.EstimateEndOffset`.
- Problem: TA volumes are converted to litres, then assigned as `Duration = 2 * (float)v`; 2 µL therefore becomes 4e-6 seconds. The peak-shape fallback `max(Duration, 0.05 * Delay)` masks some effects; an actual duration error in rendered or integrated output was not reproduced.
- Suggested direction: Establish the intended TA duration source: recorded timing, a documented instrument assumption, or unavailable duration. Do not infer a seconds value directly from litres.
- Verify: Check an independently understood TA file and all duration consumers, including peak-shape integration; preserve injection volumes and bookkeeping.

## ITC-164 - TA filesystem dates are formatted as UTC clock time

- Priority: Low.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, A3; current-tree verification 2026-10-21.
- Location: `DataReaders/TAFileReader.cs:18`; `DataClasses/ITCDataContainer.cs`, date presentation; `Presentation/LocalDateTimeFormatter.cs` (Core).
- Problem: TA stores `File.GetLastWriteTimeUtc`, whereas peer filesystem-date readers store local times. Experiment date labels format the stored clock directly without converting UTC to local time, so TA and peer readers can display different local clock conventions.
- Suggested direction: Choose a consistent timezone convention for imported filesystem dates and their display, retaining accurate FileSystem provenance. Creation-versus-modification timestamp policy is a separate choice.
- Verify: Compare source timestamp, stored kind and displayed time outside UTC, including a daylight-saving boundary; no date probe was run.

## ITC-165 - PEAQ import assumes complete method and instrument metadata [Resolved] <a name="itc-165---peaq-import-assumes-complete-method-and-instrument-metadata"></a>

Read all APJ experiments in file order and keep readable members; fail the file only if none are readable. For missing planned Method spacing, use recorded gaps to the next injection or trace end. Missing/unrecognized CellID gives Unknown instrument; do not infer cell volume for PEAQ imports. See ITC-166 and ITC-167.

## ITC-166 - Malformed integrated concentration tokens become zero [Resolved] <a name="itc-166---malformed-integrated-concentration-tokens-become-zero"></a>

- `TryGet` initially assigns NaN, but failed `double.TryParse(..., out value)` overwrites it with zero. Xt/Mt/Xmt callers ignore the return flag, so n/a or #N/A can become finite zero state/ratio values. Blank and -- tokens take a different absence path.
- A failed parse now leaves NaN. A present Xt/Mt/Xmt token that is not a finite number stops import with the column and line, matching the existing INJV check. Blank and -- remain unavailable; numeric zero is unchanged.

## ITC-167 - Import protocol casing depends on the current culture [Resolved] <a name="itc-167---import-protocol-casing-depends-on-the-current-culture"></a>

- Extension detection uses current-culture `ToLower()`, so uppercase .ITC becomes .ıtc under tr-TR and misses the expected format. PEAQ Feedback uses the same casing pattern for protocol tokens such as HIGH. The boolean token true has no analogous Turkish-I failure.
- Extension and Feedback casing use `ToLowerInvariant`; the PEAQ boolean uses ordinal case-insensitive comparison. Accepted tokens unchanged.

## ITC-168 - Origin cell-volume inference uses a different displacement assumption

- Priority: Medium-low; scientific convention question.
- Status: Open question (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, A9; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `DataReaders/OriginProjectReader.cs`, `InferCellVolume`, neighboring syringe-concentration and actual-concentration fallbacks (`718–790`).
- Problem: Without ITC_CELL_VOL, inferred volume is q/(1−r), where q is injection volume and r the successive Mt ratio. Neighboring fallbacks use the symmetric MicroCal concentration update. For one symmetric step, inversion gives q(1+r)/(2(1−r)), differing by q/2. Cumulative-row conventions can change that comparison; the correct Origin fallback convention remains unverified.
- Suggested direction: Choose a source-supported bookkeeping convention, require missing volume metadata, or retain inference with an explicit assumption. This concerns concentration bookkeeping, not dilution-heat models.
- Verify: Use an independently understood Origin dataset lacking ITC_CELL_VOL, including variable injection volumes and row timing; compare inferred volume and concentrations.

## ITC-169 - Buffer-reference refresh bypasses target processing invalidation [Resolved] <a name="itc-169---buffer-reference-refresh-bypasses-target-processing-invalidation"></a>

Compare net reference/method and corrected heat/SD state in `ChangeBufferSubtraction`, publishing one processing update per actual change. This clears caches and invalidates attached/saved fits while preserving raw heats. Restore matching saved corrections without invalidation. In-session comparisons remain exact even below the saved snapshot tolerance (ITC-187).

## ITC-170 - Comment parsing reuses the first buffer default pH [Closed] <a name="itc-170---comment-parsing-reuses-the-first-buffer-default-ph"></a>

A sample has one pH, so buffers parsed from a single comment share it. Per-buffer default pH is unnecessary here.

## ITC-171 - Duplicating an experiment omits its comments

- Priority: Low.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B3; current-tree verification 2026-10-21.
- Location: Core `DataManager.cs`, `DuplicateSelectedData` (`643–686`).
- Problem: The duplicate initializer copies other metadata and copied attributes but omits Comments. This loses user/import notes on duplication. Resolved ITC-060 covered applied buffer correction and remains unchanged; comments are a separate omitted field.
- Suggested direction: Preserve comments through ordinary duplication, following existing metadata-copy conventions.
- Verify: Duplicate a commented experiment and check text equality, edit independence, attributes and correction retention; leave ITC-060 resolved.

## ITC-172 - Locked spline merge indexes a list after changing its positions

- Priority: Medium-low.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B4; current-tree verification 2026-10-21.
- Location: Core `DataProcessing.cs`, `MergeLockedSplinePoints` (`1782–1805`).
- Problem: Earlier locked-point iterations remove matching generated points and append replacements. A later non-user locked point with no nearby generated match indexes that changed list by its old ID. Bounds checks prevent an invalid index, but do not establish identity; the wrong point could be replaced. A triggering ordering was not reproduced.
- Suggested direction: Resolve replacements using stable identity/time or preserve the original indexing during merge, keeping position and slope locks intact.
- Verify: Construct multiple locked points with earlier removals and a later no-match generated point; check retained points, ordering and locked values independently.

## ITC-173 - Baseline subtraction publishes a list before filling it

- Priority: Medium.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B5; current-tree verification 2026-10-21.
- Location: Core `DataProcessing.cs`, `SubtractBaselineCore` (`868–880`), asynchronous peak/integration processing; corrected-trace consumers.
- Problem: The method assigns a new empty BaseLineCorrectedDataPoints list, then appends into that published instance. Worker processing can overlap readers unless their synchronization prevents it, creating a possible partial trace or enumeration failure. No actual reader interleaving was established.
- Suggested direction: Build a complete replacement before publication, after checking current processing ownership and UI-read guarantees. Preserve existing success/failure behavior.
- Verify: Trace graph/export/autosave consumers during worker processing; controlled interruption or concurrent enumeration should establish whether partial state is observable before implementation.

## ITC-174 - Choose fitted buffer-subtraction uncertainty at saturated fits

- Priority: Medium-low; scientific decision.
- Status: Open question (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B7; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `DataClasses/BufferSubtraction.cs`, fitted models and `EstimateResidualSd` (`269–309`, `452–463`).
- Problem: Residual SD is zero when points do not exceed parameter count (two for Linear, three for exponential decay). Fitted-heat SD is based on residual scatter rather than the reference injections’ integration SDs. This does not establish an unintended omission: zero error means no error, and the statistical convention needs a decision.
- Suggested direction: Choose whether saturated fits report no residual error, unavailable fitted uncertainty, or another justified estimate; separately choose whether measured reference SDs enter weighting/propagation. Distinguish input measurement SD from residual scatter.
- Verify: Specify independent expectations for saturated/overdetermined fits, zero/nonzero reference SDs and fitted predictions; preserve exact zero-error inputs and best-fit centers.

## ITC-175 - Choose compatibility requirements for matched buffer injections

- Priority: Medium-low; scientific decision.
- Status: Open question (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B8; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `DataClasses/BufferSubtraction.cs`, `BuildMatchedInjectionModel` and reference injection-number lookup.
- Problem: Matched subtraction selects reference heats by injection number without comparing target/reference volumes. A differing injection program can therefore pair different injected amounts. The mechanism is present; whether those pairs are scientifically acceptable is unproven.
- Suggested direction: Choose number-only matching, a volume-compatibility requirement/warning, or an explicitly justified scaling/interpolation rule. Do not silently select a new subtraction model.
- Verify: Specify expected corrected heats for matched and mismatched programs, variable volumes, missing reference injections and excluded injections.

## ITC-176 - Experiment information formatting fails with no injections

- Priority: Low.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B9; current-tree verification 2026-10-21.
- Location: Core `DataClasses/ExperimentData.cs`, `GetInfoString` (`894–935`).
- Problem: An empty injection list leaves the description empty, then Substring(0, Length−2) requests a negative length. The formatting method can throw; reachability through an ordinary accepted import was not established.
- Suggested direction: Handle an empty injection summary explicitly, retaining existing nonempty text.
- Verify: Format an empty experiment and ordinary single/grouped injection descriptions; inspect actual details-view admission of empty data.

## ITC-177 - Clone attribute isolation is incomplete

- Priority: Medium-low.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B10; current-tree verification 2026-10-21.
- Location: Core `DataClasses/ExperimentData.cs`, synthetic clone attribute copy; `TandemConcatenationTool.ConcatCore`; `RefreshBufferSubtraction`.
- Problem: Synthetic clones reuse ExperimentAttribute instances. Tandem concatenation copies the BufferSubtraction attribute but shares other attribute instances. Reference refresh can replace BufferReferenceSnapshot on a synthetic clone's shared attribute; that specific mechanism does not apply to the copied tandem buffer attribute. No workflow producing an incorrect result from the remaining sharing was established.
- Suggested direction: Define clone ownership for mutable attributes and captured reference evidence; use existing copying semantics where isolation is required.
- Verify: Trace synthetic and concatenated data through reference refreshes and attribute edits; check original/clone independence and preserved captured evidence without changing primary inputs.

## ITC-178 - Concentration perturbation overwrites clone Ratio with molar ratio

- Priority: Low; consumer impact unproven.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, B11; current-tree verification 2026-10-21.
- Location: Core `DataClasses/ExperimentData.cs`, `AddConcentrationVariance` (`704–724`); `InjectionDisplacementCalculator.ApplyToInjection`; `Analysis2/BootstrapModelSnapshot.cs`.
- Problem: Concentration perturbation assigns Ratio=titrant/cell regardless of AxisType, whereas normal displacement selects the requested coordinate. Ratio is retained in bootstrap snapshots, but no consequential clone consumer was established. Resolved ITC-135 fixed GuessN using physical concentrations and remains unchanged.
- Suggested direction: Trace clone-coordinate consumers before choosing whether to recompute the selected coordinate or retain the internal molar-ratio convention. Do not reopen the settled stoichiometry-guess change.
- Verify: Dissociation/concentration-axis clones with concentration variation on/off; inspect bootstrap snapshot/restoration and any prediction/plot consumer before asserting user-visible impact.

## ITC-179 - Percentile confidence-bound indexing is sensitive to floating rounding [Resolved] <a name="itc-179---percentile-confidence-bound-indexing-is-sensitive-to-floating-rounding"></a>

Use integer indices `(n - 1) / 40` and `n - 1 - lowerIndex` for outward 95% order statistics. Floating-point rounding before ceiling can shift the lower bound by one, including at n = 200 and 1000. Preserve original-data centers.

## ITC-180 - Saved-result summaries resample uncertainty without a stable random stream [Resolved] <a name="itc-180---saved-result-summaries-resample-uncertainty-without-a-stable-random-stream"></a>

Return a lone member unchanged if the center is omitted or equals its estimate. Otherwise use fresh local `Random(0)` for summary sampling and linear intersections, preserving draw counts, supplied original-data centers and existing SD denominator. Multi-member omitted-center summaries still use the sampled mean. Explicit Spolar–Record Monte Carlo reruns remain stochastic. See CR-068.

## ITC-181 - Choose the autocorrelation ceiling for integration uncertainty

- Priority: Medium; scientific decision.
- Status: Open question (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, C3; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `Math/Statistics.cs`, `EstimateAutoCorrelation` (`59–85`), and integration variance inflation consumers.
- Problem: After clamping r to ±0.999, the method returns min(r,0.85). Positive r above 0.85 is therefore capped, limiting the AR(1) factor (1+r)/(1−r) to about 12.3. The earlier upper 0.999 clamp is ineffective; its lower clamp still matters. Source review did not establish the rationale or actual underestimation.
- Suggested direction: Choose whether to retain a justified ceiling, use a different documented bound/estimator, or decline uncertainty when correlation cannot be estimated reliably. Do not assume stronger correlation from filtering alone.
- Verify: Independent covariance/variance expectations across known AR(1) correlations, sampling gaps and integration lengths; distinguish an intentional regularization policy from accuracy claims.

## ITC-182 - Optimizer fallback tolerance may dominate small unweighted objectives [Resolved] <a name="itc-182---optimizer-fallback-tolerance-may-dominate-small-unweighted-objectives"></a>

Compare recomputed objectives directly because an absolute tolerance can exceed the entire unweighted J² score. Ordinary starts accept valid candidates at ≤ baseline; inward-adjusted starts require strict improvement (ITC-093). On worsening, restore exact original parameters including after the LM zero-start kick. Bootstrap shares this path; retention and effort remain ITC-185.

## ITC-183 - Scalar reciprocal lacks the shared zero-crossing interval guard

- Priority: Medium.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, C5; current-tree verification 2026-10-21.
- Location: Core `Math/NumberStructs.cs`, `operator /(double, FloatWithError)`; `Math/FWEMath.cs`, `Divide`; stoichiometry-guide calculation.
- Problem: The scalar reciprocal transforms endpoints without checking whether the denominator interval spans zero, unlike FWEMath.Divide. It can produce a finite interval excluding the estimate across a singularity. The operator is used by syringe-correction stoichiometry guides; occurrence in actual fitted inputs was not established.
- Suggested direction: Choose consistent interval handling with the guarded division policy for singular denominators, retaining exact values and zero-error semantics.
- Verify: Independent positive, negative, crossing-zero and endpoint-zero intervals and zero-SD values, through both shared arithmetic and stoichiometry guides.

## ITC-184 - Log transforms silently lose invalid confidence bounds

- Priority: Medium-low.
- Status: Open question (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, C6; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `Math/FWEMath.cs`, `Log`/`Log10`; `Math/NumberStructs.cs`, interval availability/formatting.
- Problem: Both logarithms transform Lower directly. A negative lower bound yields NaN and can hide the interval; a zero bound yields −∞, so behavior depends on availability/formatting. A positive best-fit value can remain visible while uncertainty is lost. This does not establish an invalid positive center.
- Suggested direction: Choose explicit domain handling for nonpositive bounds: retain a supported one-sided/unbounded interval, report unavailable uncertainty with a reason, or require positive-domain inputs. Preserve valid saved intervals and zero-error quantities.
- Verify: Independent strictly positive, lower-zero and lower-negative intervals with positive centers; check salt transforms and scientific text output as well as arithmetic.

## ITC-185 - Choose bootstrap refit effort and acceptance policy

- Priority: Medium; scientific decision.
- Status: Open question (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, C7; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `Analysis2/Solver2.cs`, bootstrap max iterations/tolerance and local/global retained-refit filters.
- Problem: Bootstrap refits use max(10, MaxIterations/3) and a doubled tolerance. Limit-terminated/non-usable refits are excluded and counted as failures. This is explicit behavior; selection could affect uncertainty for difficult parameters, but narrowing/coverage impact was not measured.
- Suggested direction: Choose reduced or full/adaptive refit effort and whether unsuccessful refits remain excluded, trigger retries or make uncertainty unavailable. Retain failure accounting and the original-data best-fit center; do not substitute a bootstrap mean.
- Verify: Independently specified acceptance cases for local/global fits, retained/failed counts, iteration limits and weakly determined parameters; evaluate uncertainty effects separately from parameter recovery.

## ITC-186 - Name-tag tokenization contains an invalid regex range

- Priority: Low.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, D2; current-tree verification 2026-10-21.
- Location: Core `DataClasses/AnalysisResultNameParser.cs`, name tokenization (`279–292`) and exception fallback.
- Problem: The fallback character class `[\s_-.+]+` places a hyphen between _ and ., forming a reversed range. Regex construction throws and the outer catch returns no suggestion. Names with whitespace/underscores use a separate branch.
- Suggested direction: Escape/place the hyphen so the intended punctuation is matched, retaining the existing naming scope and fallback.
- Verify: Multi-experiment names containing neither whitespace nor underscore, including plain and hyphen/dot/plus names; ordinary safe-branch controls.

## ITC-187 - Validity tolerance hides small heat and concentration-SD changes [Closed] <a name="itc-187---validity-tolerance-hides-small-heat-and-concentration-sd-changes"></a>

Retain the snapshot comparison floor (~1 nJ heat, ~1 nM concentration SD) because tighter tolerances risk false stale flags near zero. Processing snapshots detect their own edits and buffer-reference changes invalidate immediately in session (ITC-169). Revisit if low-nM cell-concentration edits fail to flag a fit.

## ITC-188 - Fixed affinity cells can use a different unit from the overview header [Resolved] <a name="itc-188---fixed-affinity-cells-can-use-a-different-unit-from-the-overview-header"></a>

Overview columns retain typed affinity-unit metadata from the existing unit resolution. A06 supplies it when formatting fixed affinities, retaining “(fixed)” without uncertainty; standalone parameter tables retain magnitude-based formatting with explicit units. Automatic column-unit selection is unchanged; concentration preferences do not introduce an override. Updated report map A06.

## ITC-189 - Rounded delta AICc can obscure assessment thresholds [Closed] <a name="itc-189---rounded-delta-aicc-can-obscure-assessment-thresholds"></a>

Keep ordinary rounding (e.g. 14.96 displays as +15.0). The separately shown verdict uses unrounded evidence. Threshold-aware precision and negative-zero cleanup were declined.

## ITC-190 - Report processing order and null-evidence unit descriptions drift from the map [Resolved] <a name="itc-190---report-processing-order-and-null-evidence-unit-descriptions-drift-from-the-map"></a>

Removed E04’s “Integrated injections” count and S03’s “Integration: n of m injections integrated” note, including unused counting variables. Baseline status, injection-use details, integration settings, saved bookkeeping, subtraction notes and heat-only availability rows remain. Removed both count rows from the map; E11 observed heat and residual now say “selected heat unit,” matching the existing µJ/µcal headers and conversions. E11 report code, processing, fitting readiness, validity and scientific calculations are unchanged.

## ITC-191 - Profile duration formatting does not carry rounded units [Resolved] <a name="itc-191---profile-duration-formatting-does-not-carry-rounded-units"></a>

Round to the displayed precision before selecting units or splitting minutes, using the fitting formatter's away-from-zero midpoint convention. Profile durations retain whole milliseconds and tenths of a second; 999.6 ms becomes `1.0 s`, 59.97 s becomes `1 min 0.0 s`, and 119.97 s becomes `2 min 0.0 s`. Both desktop inspectors use the shared Core formatter; no desktop UI runtime check was performed.

## ITC-192 - Report labels use different member index spaces [Resolved] <a name="itc-192---report-labels-use-different-member-index-spaces"></a>

- Labels are built after filtering solutions with Data==null, while several report/health paths index them using unfiltered positions. A null-data member before a usable one can misalign labels or force fallback labels. A supported saved-result path producing such members was not established.
- Normal result creation associates fit members with experiments. FTXTC restoration rejects unavailable experiment/member links, or omits the affected solution/result during recovery, rather than publishing members with null Data. No supported workflow producing the hypothesized member list was found. Closed by user decision: manufactured states that cannot arise through normal use are outside scope. No label-mapping or serialization change is needed.

## ITC-193 - Report date display assumes Unspecified dates are UTC [Resolved] <a name="itc-193---report-date-display-assumes-unspecified-dates-are-utc"></a>

- Details calls report.Date.ToLocalTime(); .NET interprets Unspecified as UTC for that conversion. A hypothetical local-clock Unspecified value could therefore shift, but the review did not identify a supported workflow producing that input.
- Report creation and content-changing saves use UtcNow; project saving, copying, and restoration preserve the recorded UTC designation. Normal report timestamps therefore convert correctly to the viewing computer's local time. The hypothesized local-clock value without its time basis requires tampering with saved report metadata, which is outside the supported scope and does not warrant normalization or defensive handling. Closed by user decision; no code or serialization change is needed.

## ITC-194 - Choose one proton sign and buffer-enthalpy convention across surfaces [Resolved] <a name="itc-194---choose-one-proton-sign-and-buffer-enthalpy-convention-across-surfaces"></a>

Use buffer ionization enthalpy on x and +slope for net proton uptake per binding event; negative means release. The intercept is buffer-independent binding enthalpy at experimental pH, including binding-partner protonation heat. Display saved values directly across surfaces. Retain saved semantics and the opposite-sign registry protonation API. See ITC-195 for series eligibility and ITC-150 for the later figure-controls fix.

## ITC-195 - Choose protonation spacing and multiple-buffer requirements

- Priority: Medium-low; scientific decision.
- Status: Open question (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, E2; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: Core `Analysis2/AdvancedAnalysis/ProtonationAnalysisAvailability.cs`; `DataClasses/Buffers.cs`, `TryGetProtonationEnthalpy`; contrast electrostatics spacing policy.
- Problem: Eligibility requires exact Distinct count≥2 with no minimum enthalpy span, and the lookup uses only the first Buffer attribute. Near-identical x values or mixed buffers can therefore pass under first-buffer metadata. Conditioning and physical consequences are unmeasured. Resolved CR-021 correctly handles missing thermochemistry and remains untouched.
- Suggested direction: Choose exact distinction or a justified conditioning/span requirement; separately decide whether mixed buffers are unsupported, require an explicit representative buffer, or need a scientifically defined combined coordinate.
- Verify: Independent near-coincident enthalpy regressions, normal separated buffers, genuine zero thermochemistry and multiple-buffer order; no subset filtering or invented values.

## ITC-196 - Reset can suppress the workspace invalidation event

- Priority: Low; latent event contract.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, E4; current-tree verification 2026-10-21.
- Location: Core `Analysis2/AnalysisWorkspace.cs`, `ResetStoredAnalysisState`, `TryRebuild`, `InvalidateContext`; both desktop reset callers.
- Problem: Reset nulls Context before rebuilding. If rebuilding fails or data are unavailable, InvalidateContext sees null and emits no ContextInvalidated; successful rebuilding emits ContextRebuilt. Both current desktop reset callers explicitly refresh afterwards, so no stale UI trigger was found.
- Suggested direction: Clarify the workspace event contract and invalidate through its owning method if subscribers must observe every ready-to-unready transition. Preserve current reset scope and failure state.
- Verify: A previously ready workspace reset with unavailable/unready data or rebuild failure, counting events and checking existing desktop refresh behavior.

## ITC-197 - Data-change refresh can temporarily exit the result view

- Priority: Low.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, E5; current-tree verification 2026-10-21.
- Location: Core `StateManager.cs`, `OnDataDidChange`/`SetProgramState`; `DataManager.NotifySourceItemsChanged` and `SelectIndex`.
- Problem: OnDataDidChange calls SetProgramState(CurrentState); while in AnalysisView this takes the toggle-out branch and publishes saveState. Ordinary source-change helpers then SelectIndex and can return to the selected result. A persistent unwanted exit was not established, but intermediate state events can cause navigation churn.
- Suggested direction: Separate refreshing availability from an explicit result-view toggle if the intermediate transition is observable. Preserve deliberate navigation to experiments and the saved workflow state.
- Verify: Source-list add/remove/update while a result remains selected; record state transitions and actual desktop navigation, including callbacks that reselect afterwards.

## ITC-198 - A completed scrolling status clears newer messages

- Priority: Low.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, E6; current-tree verification 2026-10-21.
- Location: Core `StatusBarManager.cs`, `SetStatusScrolling`, `SetStatus` and `ClearAppStatus`.
- Problem: New status calls set abortscroll, but the scrolling method eventually calls unconditional ClearAppStatus. That clears the entire status list/queue and stops progress, including newer statuses published during its awaits.
- Suggested direction: Expire only the scrolling operation’s own status and suppression state, preserving later messages and progress ownership.
- Verify: Post an ordinary or queued status during each scrolling delay/abort point; verify it survives scroll completion and queue cancellation remains scoped.

## ITC-199 - Concentration formatting temporarily changes global precision [Resolved] <a name="itc-199---concentration-formatting-temporarily-changes-global-precision"></a>

Pass a local precision override into the shared formatter without assigning the global preference. Preserve existing one-decimal, uncertainty, unit, culture, and compact-number output. Regression tests reproduce the original mutation during formatting and concurrent calls, and the leak after an injected formatting exception; the corrected formatter leaves precision unchanged throughout.

## ITC-200 - An empty downloaded citation suppresses the bundled fallback

- Priority: Low.
- Status: Open (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, E8; current-tree verification 2026-10-21.
- Location: Core `Utils/CitationManager.cs`, `TryFetchOnlineCitation`, `SaveCache`, `TryLoadCache`, `GetPaperCitation`.
- Problem: Any non-null deserialized CitationInfo is cached/accepted. A valid JSON object such as {} supplies no citation fields yet overrides the bundled citation on later launches until replaced/removed. No actual online empty response was observed.
- Suggested direction: Validate the minimum usable citation fields before replacing or accepting the cache, retaining the last usable/bundled fallback.
- Verify: Empty, partial, malformed and valid downloaded/cache payloads; check fallback and subsequent recovery without making a network request.

## ITC-201 - Automatic concentration units switch below exact boundaries [Resolved] <a name="itc-201---automatic-concentration-units-switch-below-exact-boundaries"></a>

Magnitude boundaries are inclusive, so automatic displays fall in [1, 1000). The unit is chosen from the absolute value. Zero and non-finite values return `AppSettings.DefaultConcentrationUnit`. The automatic `AsFormattedConcentration`/`AsF1FormattedConcentration` overloads and the concentration tooltip omit the unit for non-finite values. Removed duplicated zero→µM and `Math.Abs` handling in the report conditions line, report parameter formatting, viewer attribute text and viewer fit-parameter units; result-level averaging filters are unchanged. Display rounding across a boundary (999.97 µM → "1000.0 µM" at one decimal) remains out of scope. Report map unchanged; it does not state the selection rule.

## ITC-202 - Foreign separators can survive interpretation basename extraction [Resolved] <a name="itc-202---foreign-separators-can-survive-interpretation-basename-extraction"></a>

- Path.GetFileName uses host separator rules. A saved Windows backslash path can survive on Unix and enter SourceFileBasename. Normal raw imports already store basenames; full paths were found only as arbitrary serialized/programmatically assigned values, not an ordinary capture path. No actual user-name disclosure was established.
- Normal raw imports store basenames, and project saving/restoration preserves those filenames. The hypothesized disclosure depends on tampering with saved filename metadata or manufacturing a programmatically assigned full path; no ordinary capture path producing it was identified. Closed by user decision: tampered files and manufactured states without a normal-use trigger are outside scope. No additional path normalization or serialization change is needed.

## ITC-203 - Legacy global factories retain live parameters in static history [Resolved] <a name="itc-203---legacy-global-factories-retain-live-parameters-in-static-history"></a>

Desktop workspace builders bypass legacy global factory history. Contamination required deliberately seeding live parameter history and, for Update Result, constructing a result with a missing saved coordinate. No normal-use trigger was established; closure followed the maintainer's manufactured-state policy. See ITC-082.

## ITC-204 - Correct the salt-dependence calculation [Resolved] <a name="itc-204---correct-the-salt-dependence-calculation"></a>

Regress `ln(Kd / M)` on `ln(recorded salt concentration / M)`; positive slope means weaker binding. Avoid valence scaling, activity corrections and ion-count interpretation. Validate all metadata before excluding zero concentrations; sum repeated same-salt entries, retain repeated observations and require three distinct usable positive concentrations. Reject mixtures or changing salts. Version 2 saves the corrected slope; older projects omit the obsolete slope/count and retain other outputs. Edits invalidate only affected components, including on save; recalculate explicitly. Salt-only edits preserve primary-fit validity but changed evidence stales interpretations. [IUPAC](https://iupac.org/wp-content/uploads/2025/03/IUPAC-GB4Abridged.pdf#page=71), [Record et al.](https://doi.org/10.1016/S0022-2836(76)80023-X), [Chowdhury et al.](https://pmc.ncbi.nlm.nih.gov/articles/PMC10576128/).

## ITC-205 - Registration delivery worker processes at most one message per minute [Resolved] <a name="itc-205---registration-delivery-worker-processes-at-most-one-message-per-minute"></a>

`RegistrationDeliveryWorker` now waits one second after progress and one minute after an empty queue or unavailable service. Its internal sender result distinguishes these states; quarantining an unreadable payload counts as progress. Daily retention, cancellation, activation expiry, and ordinary provider retries remain intact. The repeated real-time workload delivered all 200 activation/access-code emails in 620.037 seconds from the first arrival, with a 56.921-second maximum eligible-message wait and no remaining queue, meeting the ten-minute per-message target. The local fake provider responded immediately; these results do not establish real-provider capacity, inbox arrival time, deployed performance, or a 24-hour expiry incident. The isolated full Web suite passes 564/564.

## ITC-206 - Full-address IPv6 rate-limit buckets can be rotated within a prefix [Resolved] <a name="itc-206---full-address-ipv6-rate-limit-buckets-can-be-rotated-within-a-prefix"></a>

Use shared `NetworkPartition` for network-rate limits: one bucket per IPv6 /64, ignoring scope IDs, with IPv4-mapped addresses normalized to IPv4. Keep identity quotas separate and give Turnstile the full address. Local behavior was verified; production IPv6 exposure through the website/proxy path was unverified (ITC-214).

## ITC-207 - Operator authentication repeatedly parses registries on unthrottled GET routes

- Priority: Low; scaling impact unmeasured.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, F5; current-tree verification 2026-10-21.
- Location: `AnalysisITC.Web/OperatorCodeRegistry.cs`, `Authenticate`; `OperatorTombstoneRegistry.Contains`; options/account/operator-options routes in `Program.cs`.
- Problem: Authentication rereads registry data and checks the tombstone file per record. Those GET routes lack endpoint rate-limit policies. Tombstone checks take a static process lock; authentication as a whole is not under that lock. Cost grows with records/request frequency; no performance or deployed-size evidence was collected.
- Suggested direction: Measure before choosing bounded caching, a narrow GET limiter or retaining the current small-registry design. Preserve prompt revocation and correct authentication failure behavior.
- Verify: Source-supported registry sizes and repeated valid/invalid credential requests; measure work/latency and revocation freshness without production load testing.

## ITC-208 - Public identity touches rewrite an unbounded registry

- Priority: Low; scaling concern.
- Status: Open (2026-10-21). Plausible by source review only; trigger or impact remains unproven; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, F6; current-tree verification 2026-10-21.
- Location: `AnalysisITC.Web/PublicAccessRegistry.cs`, `TryTouch` and cross-process mutation lock.
- Problem: A successful touch reads and rewrites the complete registry, which has no visible size/retention bound. Lock acquisition can sleep request threads in a bounded five-second loop. CR-061 intentionally fixed lost revocations with cross-process reread/locking and remains resolved; this is a distinct unmeasured growth/latency concern.
- Suggested direction: Measure realistic registry growth and touch cost before choosing coalesced last-use writes, bounded retention or transactional storage. Preserve the settled cross-process revocation guarantee.
- Verify: Registry-size and contention measurements with separate service/admin writers, including lock timeout and revocation visibility; no benchmarks were run.

## ITC-209 - Registration body limits depend only on Content-Length [Resolved] <a name="itc-209---registration-body-limits-depend-only-on-content-length"></a>

Enforce registration/activation body limits (16/4 KiB) on streamed bytes as well as Content-Length, because chunked requests can bypass the header check. Wrap `request.Body` only during JSON reading and restore in finally. Exact-limit bodies pass. Preserve overflow responses, submission 413 and activation 400. Host limits are a separate layer (ITC-206).

## ITC-210 - Upload admission remains occupied through validation and parsing

- Priority: Low; bounded contention concern.
- Status: Investigated; remains open (2026-10-21). Local contention and cleanup are demonstrated; no leak or evidence requiring an admission-policy change was established. Upload behavior is unchanged.
- Origin: Read-only code review 2026-10-20/21, F8; approved local investigation 2026-10-21.
- Location: `AnalysisITC.Web/ViewerUploadLimits.cs`, admission middleware; `Program.cs`, `/api/viewer/open`; `ViewerUploadTransferDeadline`.
- Behavior: One lease covers antiforgery, form buffering and resource-budgeted parsing. The transfer deadline ends after the form is received, before parsing; the lease remains held until downstream processing unwinds. Tokens are freely issued by `/api/viewer/token`, so an anonymous caller with a valid header token can occupy the slot with a slow body. Moving admission after form validation could permit unbounded concurrent buffering; holding it through parsing preserves ITC-063's serialization guarantee.
- Measurement setup: Real loopback Kestrel only, no local Caddy, with one active upload, queue limit 0 and transfer timeout 2 seconds to exercise recovery promptly. Shipped defaults remain one active upload, five FIFO waiters, 30-second queue wait and 300-second transfer timeout. Temporary measurement instrumentation and the probe were removed. Durations below are single local diagnostic observations, not performance guarantees or test thresholds.
- Case 1, valid header token and slow body: Read 128 bytes, held the slot for 2,015.1 ms, cancelled the blocked read at the shortened deadline and returned `408 viewer_upload_timeout`; a competing valid upload received `503 viewer_busy`. A subsequent valid project opened successfully after lease release. This is the longest plausible transfer-hold case; the shipped 300-second deadline was not waited out.
- Case 2, invalid header token and slow body: Returned `400 antiforgery_validation_failed` in 1.1 ms, with zero request-body reads even though the form contained a valid token. The slot was available immediately afterward and a valid project opened. A new deterministic controlled-stream regression retains this behavior without elapsed-time assertions.
- Case 3, no header token and slow body: Antiforgery read the form looking for its token; after 128 bytes, the slot remained held until the shortened deadline. Returned `408 viewer_upload_timeout` after 2,002.9 ms and cancelled the blocked read. A competing valid upload received `503 viewer_busy`; the next valid upload succeeded after cleanup. Existing controlled-stream/fake-clock tests cover both header/form-token deadline handling, cancellation unwind and client-disconnect precedence; these remain unchanged.
- Case 4, valid token and synthetic near-budget projects: A project with 990,000 raw samples (99% of the default 1,000,000-sample budget; 6,804,255-byte archive) parsed in 682.6 ms and completed the server pipeline in 1,384.8 ms. A project with 990 roots (99% of the default 1,000-root budget; 1,060,143-byte archive) parsed in 731.3 ms and completed in 736.8 ms. Both returned 200, rejected a competing valid upload with 503 during parsing, and released the lease afterward. These cases do not establish the worst-case cost of every allowed combination of parser budgets.
- Disconnect cleanup: Explicit TCP resets during valid-header and no-header slow transfers set Kestrel's request-aborted state and released the lease in 0.6 ms and 0.5 ms respectively; subsequent valid uploads succeeded. No outstanding parser work was abandoned to release capacity early.
- Hosting/deployment evidence: Local Kestrel reports a 240-byte/second minimum request-body data rate with a five-second grace period and a 51 MiB global body maximum. The shortened transfer deadline fired before that grace period. Production Caddy request-body handling is unverified; behind a proxy, Kestrel's minimum data rate applies to the proxy connection, not the client. No repository Caddyfile exists, and the website/proxy sources are external; the supplied DNS facts are recorded under ITC-206.
- Conclusion/follow-up: Retain admission, form-token support, transfer deadline and parser budgets. The expected single-slot contention is reproducible and bounded transfers recover correctly; no indefinite leak or production abuse was demonstrated. Verify deployed ingress/proxy buffering and realistic workload measurements before proposing any precheck, separate ingress capacity or parsing deadline. No such behavior change or new tracker entry was made.
- Validation/evidence: Kestrel probe passed, including competing uploads during both synthetic parses and recovery after timeout/disconnect. The full final Web suite passes all 508 tests with no failures/skips, including route variants, queue behavior, resource limits and deterministic transfer lifecycle coverage. Evidence: `/private/tmp/itc210-kestrel.jsonl`, `/private/tmp/itc210-parse.jsonl`, `/private/tmp/itc210-probe.log`, `/private/tmp/itc206-210-results/probe.trx`, `/private/tmp/itc206-210-results/web-full.trx`. No production traffic, packaging, versions, commits or pushes.

## ITC-211 - Tombstone read failures look like an empty registry [Resolved] <a name="itc-211---tombstone-read-failures-look-like-an-empty-registry"></a>

`OperatorTombstoneRegistry` distinguishes genuine absence from invalid or unavailable data. Failed reads deny operator authentication, defer queued delivery without spending attempts, and reject operator mutations/additions without replacing the file. Existing health reporting checks deletion markers even while interpretation is paused. Add uses a persistent cross-process lock, acquired after operator locks during scrubbing. Malformed/null/permission-denied files remain byte-for-byte intact; concurrent additions and writer termination preserve completed markers. Regression probes and the isolated full Web suite (564/564) pass. JSON format and legitimate first-run absence are unchanged; this is not a claim of multi-file crash atomicity or live deployment validation.

## ITC-212 - Operator registry mutations are not serialized across all writers [Resolved] <a name="itc-212---operator-registry-mutations-are-not-serialized-across-all-writers"></a>

Take both persistent registry locks in a consistent order before reading through replacement. Use close-on-exec descriptors and release partial acquisitions on failure. Put the manual-registry lock in the writable account directory, keyed by full manual-registry path, so service and CLI agree with read-only config. Locks serialize writers; crash atomicity is per-file. [Permission case](Investigations/RegistrationReliability-2026-10-22/evidence/lock-permissions/README.md).

## ITC-213 - Registration retention can remove a newly resent activation

- Priority: Medium-low; lifecycle policy question.
- Status: Open question (2026-10-21). Confirmed by source review only; no runtime reproduction or tests run. Tracker registration only; no implementation authorized.
- Origin: Read-only code review 2026-10-20/21, F12; current-tree verification 2026-10-21. Scientific/design choice; no option selected.
- Location: `AnalysisITC.Web/RegistrationDeliveryOutbox.cs`, resend/activation expiry; `SelfRegistrationStore` retention selection; `RegistrationDeliveryWorker` daily cleanup.
- Problem: Resending after the existing activation link expires creates a fresh expiry without changing original account created_at_utc; resending an unexpired link preserves its expiry. Retention scrubs pending/activation-sent accounts by that original age (default 30 days), potentially before the fresh 24-hour link expires. No actual cleanup incident was reproduced.
- Suggested direction: Choose whether fresh activation extends retention, retention respects current link expiry, or late resends are refused. State the usability/privacy tradeoff before changing lifecycle policy.
- Verify: Resend immediately before/after the retention cutoff, unexpired versus expired links, cleanup before delivery and exact expiry boundaries with a controlled clock.

## ITC-214 - Verify real-client address forwarding for website registration traffic

- Priority: Low; IPv4 address loss reproduced, user-visible throttling incidents unmeasured.
- Status: Deployed; real-client IPv4 forwarding verified live (2026-10-21). IPv6 forwarding remains explicitly unverified because a controlled live IPv6 connection was unavailable.
- Origin: Approved follow-up to ITC-206/ITC-209, investigated at the user's request on 2026-10-21. The initial MIST SSH timeout was caused by the disconnected VPN; access worked after the user connected it.
- Location: Separate website repository `/Users/frederiktheisen/Projects/ft-itc-website`, `_worker.js`, `wrangler.jsonc`, `script.js:321-323`, `activate.html:39`; deployed MIST `/etc/caddy/Caddyfile:11-20` and active Caddy configuration; application `AnalysisITC.Web/Program.cs`, forwarded-header handling and `NetworkPartition`.
- Confirmed IPv4 problem before the fix: Requests from two controlled client networks reached the website with distinct visitor IPv4 addresses in CF-Connecting-IP, but Caddy forwarded the same Cloudflare egress address (`104.22.69.102`) in X-Forwarded-For for both. The application consumes one forwarded hop and uses that result for its network bucket, so these visitors share the Cloudflare egress bucket rather than being grouped by their own network. This is a header/configuration reproduction; production rate-limit buckets were not exhausted. Not all Cloudflare traffic is asserted to use this single egress address.
- Source/deployment evidence before the fix: The website makes same-origin API calls. Its Worker proxies registration submission, activation, status and viewer-token requests to `https://app.ft-itc.org`, copying incoming headers except Host and Content-Length. MIST runs Caddy 2.11.7 with the site reverse-proxied to `127.0.0.1:5000`. The active admin-API configuration has no server or handler trusted_proxies, no client_ip_headers override, and no reverse-proxy header overrides. Thus the deployed plain proxy reports its immediate Cloudflare peer in X-Forwarded-For. The visitor's CF-Connecting-IP survives separately, but the application does not use it. Application `ForwardLimit = 1` remains unchanged.
- Initial live trace (2026-10-21, before the fix): A temporary read-only loopback capture was restricted at the packet filter to `GET /api/registration/status?itc214=...`; its parser emitted only the tagged URI and selected address/routing headers. Six tagged requests were captured with no kernel drops, and the capture stopped automatically. Both a website GET from MIST and one from this machine returned 200, had different CF-Connecting-IP values and CF-Worker `ft-itc.org`, and shared X-Forwarded-For `104.22.69.102`. Corresponding direct API GETs returned 200 and carried their respective connection-peer addresses in X-Forwarded-For. The private client address is retained only in the temporary local trace, not copied into this tracker.
- Initial header-spoof check before the fix: A website status GET supplying X-Forwarded-For `198.51.100.214` returned 200; the forwarded value remained the Cloudflare egress address. A direct API status GET supplying that address in both X-Forwarded-For and CF-Connecting-IP returned 200; Caddy replaced X-Forwarded-For with the actual peer but passed the forged CF-Connecting-IP through unchanged. This demonstrates why globally trusting CF-Connecting-IP in the application would introduce a spoofing path. No registration or activation POST was used.
- Initial investigation limits: Only harmless status GETs were traced; actual registration/activation submissions, account/token state changes and production bucket exhaustion were deliberately excluded. The shared Worker route set and unconditional Caddy proxy support applying the same forwarding diagnosis to those API routes, but a production POST/bucket outcome was not directly exercised. API DNS remains A-only; website A/AAAA records were confirmed. Forced IPv6 website probes failed from both this machine and MIST, so live IPv6 visitor forwarding is still unverified. The initial investigation did not retrieve the deployed Worker; deployment verification below subsequently retrieved it and confirmed an exact match with the reviewed source.
- Validation/evidence: Six existing local forwarding tests pass, covering IPv4 Caddy-reported-address spoof protection, IPv6 /64 and scope behavior, mapped IPv4 and both registration policies. Evidence: `/private/tmp/itc214-forwarded-address-trace.log`, `/private/tmp/itc214-caddy-forwarding-summary.json`, `/private/tmp/itc214-forwarding-tests.log`, `/private/tmp/itc214-results/itc214-forwarding.trx`. Public status responses also corroborate Cloudflare -> Caddy -> Kestrel. The initial investigation changed only this tracker entry and introduced no production configuration change, service restart, registration, activation, interpretation generation or persistent server diagnostic.
- Correction direction: Authenticate requests from the owned website proxy before accepting its visitor address, then have Caddy send Kestrel one vetted visitor address. Direct API clients must continue using their connection-peer address; retain the application's one-hop boundary. Merely trusting Cloudflare ranges, retaining a longer X-Forwarded-For chain or increasing ForwardLimit is not an established fix for this topology. The reviewed Worker/Caddy change below implements that boundary; IPv6 ingress still needs a working controlled client.
- References: [Caddy proxy-header defaults](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy#defaults), [Caddy trusted proxies](https://caddyserver.com/docs/caddyfile/options#trusted-proxies), [Cloudflare Worker client-IP headers](https://developers.cloudflare.com/fundamentals/reference/http-headers/#cf-connecting-ip-in-worker-subrequests). These explain defaults and trust requirements; the IPv4 diagnosis above is supported by the active configuration and live trace, rather than inferred solely from documentation.

- Implemented correction: The website Worker overwrites Proxy-Authorization and X-FTITC-Visitor-IP using a dedicated 256-bit secret binding and canonical CF-Connecting-IP, removes public forwarding headers and keeps manual redirects. Caddy validates both assertions on the four existing routes, rejects malformed/duplicate/wrong-route assertions, emits exactly one vetted X-Forwarded-For address and strips credentials before Kestrel. Direct clients retain their connection-peer address; ForwardLimit remains 1. Missing Worker configuration fails closed with a generic non-cacheable 503. Ordinary Authorization, cookies, CSRF, body/query, Set-Cookie and activation security headers are preserved.
- Deployment files: `scripts/caddy/website-forwarding.caddy`, exact two-hunk `Caddyfile.patch`, `website-forwarding.service.conf`, executable local tests and deployment/rollback notes in `scripts/caddy/README.md`; the narrow `.gitignore` exception includes only this reviewed deployment directory. The service override removes the deployed --environ flag before loading the new protected EnvironmentFile, avoiding secret disclosure in startup output. A new daemon environment requires an approved Caddy restart. Apply Caddy first, then Worker; roll back Worker first. These were review-only until the user explicitly approved deployment; production rollout and verification are recorded below. No commits or pushes were made.
- Local validation (2026-10-21): Worker 38/38; actual Caddy 2.11.7 34/34, including actual Worker-to-Caddy requests and two distinct visitor addresses over the same connection-peer identity; AnalysisITC.Web.Tests 508/508. Forged direct headers cannot select an identity; IPv4, native IPv6, mapped IPv4, route boundaries, malformed/duplicate assertions, missing/wrong secrets, credential stripping and normal request/response preservation pass. The exact Caddyfile patch dry-ran and applied to a disposable copy of the observed deployment file, and the resulting candidate validated with Caddy 2.11.7. No Core implementation changed for ITC-214.
- Independent review: No concrete surviving bypass, legitimate regression or rollout/rollback error found. Reviewer independently passed Worker 38/38 and Caddy 34/34 plus three in-memory probes covering Connection-nominated headers, duplicate empty assertion fields and encoded sibling routes.
- Remaining verification: Live IPv6 ingress still needs a working controlled client. The forced local IPv6 probe timed out (curl exit 28); MIST has no default IPv6 route. Production rate-limit exhaustion and state-changing registrations remain outside this verification. The local source remains uncommitted, so future repository-based Worker builds must include this fix to retain it.
- Retained candidate evidence: `/Users/frederiktheisen/.codex/state/plugins/codex-security/scans/FT-ITC-Analysis/artifacts-fd69127c8ed602f324247d24ecfb9b04100b08e9d3bfe25a3c55739425555404/artifacts/itc214/` contains `worker-tests.log`, `caddy-tests.log`, `web-tests.log` and `independent-review.md`.

- Approved production deployment (2026-10-21, 16:29 CEST): Confirmed live Caddy/service configuration matched the reviewed inputs and Cloudflare Pseudo IPv4 was Off. Installed the exact Caddyfile patch, snippet, root-owned mode-0600 shared-secret EnvironmentFile and service override; removed --environ, validated the final configuration and restarted Caddy before changing the Worker. Protected Caddy rollback backup: `/root/itc214-20261021T142324Z`. The running daemon has the expected secret; the value is absent from its journal and admin-API configuration. Application binaries, configuration schema and ForwardLimit were unchanged.
- Worker production version: `43012a2d-482e-431a-bc83-24477551af6b`, at 100% traffic; deployment `f158e125-956b-4acf-8c75-b17f735b636e`. Previous rollback version: `9b90daf1-589c-49c6-8821-370f670e997f`. Used Cloudflare's API to add the secret binding, then replace only Worker code; existing assets, asset routing, compatibility date, usage model and other settings were retained. Retrieved live source exactly matches the reviewed `_worker.js` (SHA-256 `29b05f77a781fc1ab9bd082cb668040e9c34a34096e6cbc062bd5a3e50e0a9ff`).
- Live forwarding verification after deployment: Nine tagged harmless status GETs reached Kestrel in a short automatically stopped capture with zero kernel drops; ten HTTP checks passed. Website requests from MIST and this machine now carry two distinct real IPv4 visitor addresses, each matching that client's independent Cloudflare trace. Direct API requests retain their actual connection identity; this machine's direct path uses its private VPN socket address rather than its public Cloudflare egress address. Forged X-Forwarded-For, Forwarded and X-Real-IP cannot change identity. The Worker overwrites forged authentication/visitor assertions; a direct forged assertion returns 403 and never reaches Kestrel. Authentication and assertion headers, CF-Connecting-IP/IPv6, Forwarded and X-Real-IP are absent at Kestrel. Private client addresses are excluded from retained summary evidence and this tracker.
- Live health: Caddy and ftitc-web active; registration status, interpretation status, website home and activation page return 200; activation retains no-store/no-referrer; public internal health remains 404. No registration/activation POST, activation-token consumption, rate-limit exhaustion, application deployment, commit or push was performed.
- Operational observations: The first Caddy attempt automatically rolled back because Cloudflare returned 403 to Python's health-check client even with the original proxy; ordinary curl requests passed, and deployment succeeded with that check corrected. Forged CF-Connecting-IP requests to the website independently receive Cloudflare 403/error 1000 before the Worker; these were separated from the successful live spoofing probes. Neither observation was treated as proof of a forwarding failure.
- Retained deployment evidence: `deployment-20261021.json` in the candidate-evidence directory above records deployment identifiers, sanitized live checks, secret-handling checks, health, rollback location and the explicit IPv6 limitation.

## ITC-215 - NanoITC imports could be labelled with a MicroCal instrument [Resolved] <a name="itc-215---nanoitc-imports-could-be-labelled-with-a-microcal-instrument"></a>

`.nitc` and `.ta` imports share `ResolveTAInstrument`: cell volume above 200 µL is TA ITC Standard, otherwise TA ITC Low Vol. The general cell-volume guess no longer runs for these files.

## ITC-216 - Restored bootstrap replicates dominate project-load memory

- Priority: Not set.
- Status: Open; two independent investigations complete (2026-10-23). Recommendation: leave the replicate representation and restoration ownership unchanged; adopting the metadata cache below is undecided. No production implementation authorized.
- Origin: Review of the bootstrap storage plan, 2026-10-22 (`Planning/BOOTSTRAP_STORAGE_PLAN.md`).
- Location: Core `FTXTCReader.RestoreBootstrap`, `BootstrapModelSnapshot.Restore`; web `ViewerDocumentReader`, `ViewerUploadLimits` (`BootstrapReplicates`, `InjectionRecords`).
- Problem: Every bootstrap/LOO replicate is restored as an independent experiment, model and solution. Load memory therefore scales with replicates × injections, independently of how the replicates are stored.
- Corrected measurement: `JORS Table1.ftxtc` (13 fits × 1,000 replicates; 305,000 bootstrap injection records). Two independent harnesses on copied source (`5f6af81f`, `bbd600d8`; Core and Web identical), macOS arm64 (M1 Pro, 10 cores)/.NET 10, five fresh processes per case, workstation GC unless stated, agree within about 2%. Release Core: 951–952 MiB cumulative allocation, 149.6–149.7 MiB post-GC live heap, 300–310 MiB peak RSS, 4.95–5.08 s. Debug Core: 963–964 MiB, 149.6–149.7 MiB, 289–291 MiB, 5.29 s. Bootstrap-removed attribution control: 133–135 MiB, 14.9 MiB, 102–104 MiB, 0.91–1.06 s. The bootstrap state is 132.7–134.8 MiB live, about 10.6 kB per replicate including the global replicate graphs. Compact JSON leaves the live heap unchanged. The previous 306 MiB retained figure is superseded: `GC.GetTotalMemory(true)` reports 311 MiB in the same process where GC heap accounting gives 149.6 MiB, and heap dumps agree near 150 MiB. Most cold-load time is JIT warm-up: a second load in the same process takes 1.96 s (0.27 s without bootstrap), and a cold load with tiered compilation disabled takes 2.88 s.
- Attribution: Injection records hold 69.6 MiB of the bootstrap state: 53.52 MiB `InjectionData` objects (184 B each, of which about 37 B are saved replicate inputs), 11.63 MiB unused `RawPeakArea` interval arrays and about 4.5 MiB list storage. Model options hold 13.7 MiB, experiment infrastructure (`ExperimentData`, `DataProcessor`, semaphore, cancellation source) 10.1 MiB, parameter tables 10.0 MiB, global replicate temperature dependences 10.0 MiB, reported-parameter dictionaries 9.0 MiB and global replicate models/solutions 5.3 MiB. Of about 818 MiB bootstrap-related cold allocation, single-fit replicate restoration takes 564 MiB/1.37 s (default model initialization that is then overwritten 181 MiB/0.48 s; snapshot building 147 MiB/0.38 s), global replicate construction 137 MiB/1.43 s, and saved-interpretation freshness 206–210 MiB/1.8 s (bands and correlations for every fit, plus unrelated thermogram diagnostics). Uncached reflection in `GetProperties` for `ParameterType`, `AttributeKey` and `AnalysisModel` (about 1 µs and 0.4–0.5 kB per call; `new Parameter` 2.5 µs and 1.2 kB) is a large share of restoration churn.
- Viewer lifetime and peak: Complete Release `ViewerDocumentReader` construction allocates 978–979 MiB, peaks at 311 MiB RSS and takes 5.0–5.2 s cold, but leaves only the document (2.15 MiB) and process caches live (15.7–18.3 MiB); no Core experiment/model/solution instances remain reachable. Response serialization adds 0.06 s for a 3.8 MiB response. The smallest GC heap hard limit that completes the read is 171 MiB (31 MiB without bootstrap), so the peak live heap is essentially the restored graph. Under server GC, the web host's configured mode, peak RSS is 654 MiB (173 MiB without bootstrap). A synthetic near-cap package (replicates tiled × 1.5: 19,500 replicates, 457,500 records; within default web limits with compact JSON) has 216 MiB live (199 MiB bootstrap, linear), needs a 237 MiB heap limit and peaks at 400 MiB RSS with workstation GC or 1,018 MiB with server GC. Uploads are admitted one at a time by default, and queued requests wait before reading the form. These are single-document costs on one machine; production core count, GC configuration and memory are unknown. Logical caps bound restoration work but do not establish safe process-memory capacity.
- Consumer constraints: Bands and saving need full replicate models (sampled concentrations, injection states, segments, options, locks, temperature). `TwoSetsOfSites` and `SequentialBindingSites` keep evaluation scratch state on each model instance, so shared evaluation models are unsafe under concurrent use. The band cache identifies the ensemble by list reference; global pairing and saving rely on member `Data.UniqueID` and `BootstrapReplicateIndex`. Saved-interpretation freshness and global summaries touch every replicate during load, so lazy restoration saves nothing on the viewer path.
- Alternatives measured: A disposable initialization shortcut reduced allocation by about 135 MiB (14%); retained memory did not improve and workstation peak RSS fell 4.9–6.1 MiB. Caching the three `GetProperties` lookups (prototype P1, about 15 lines in `EnumExtensions`) reduced cold-load allocation from 951 to 736 MiB (22%), single-fit replicate restoration from 1.37 to 0.73 s and a warm load without reports from 1.26 to 0.78 s. Live heap and workstation peak RSS were unchanged, but server-GC peak RSS fell from 654 to 493 MiB (JORS Table1) and from 1,018 to 658 MiB (near-cap). Running the web host with workstation GC gives a larger peak reduction in these measurements (311 vs 654 MiB; 400 vs 1,018 MiB), but is a host-wide deployment decision and was not measured in the web host itself. Compact, shared or lazy representations could remove up to about 110 MiB live (estimate) but add scientific state, ownership, cache and concurrency risks without an established host memory target.
- Recommendation: Leave the runtime representation and restoration ownership unchanged. Continue treating compact persistence as a byte-budget measure, not a runtime-memory fix; keep replicates, uncertainty features and upload limits unchanged. P1 is the only small change with a measured benefit (allocation churn, load time and server-GC peaks, not live memory). It changes a shared Core utility used by all applications, so adopting it is a separate decision. Revisit the representation only with a concrete memory target and representative workload evidence, including the production host.
- Validation and evidence: Initialization-shortcut prototype outputs matched exactly for all 13,000 replicate states, 610,000 predictions, bands, derived values, correlations, global pairings and viewer JSON; strict save/read checks passed, as did 282 focused existing tests. P1 prototype: viewer JSON byte-identical to baseline on JORS Table1, re-saved package identical in all 128 entries with regenerated GUIDs masked, full Core suite 5,067 passed and 1 skipped; no code in Core, macOS, Avalonia or Web writes to a cached attribute object. Neither prototype qualifies a production implementation across all models and legacy states. Before adopting P1: compare cached and direct metadata for every value of the three enums; compare viewer JSON and saved packages on the storage-plan fixtures and `HSA_H9A_H67A_Co_SequentialBindingData.ftxtc`; run Core and Web suites and build both desktop applications; re-measure server-GC peaks. The [investigation report](Investigations/ITC-216-bootstrap-memory-2026-10-23.md) covers the first investigation (definitions, attribution, ranges, initialization shortcut, reproduction). Raw evidence and disposable harnesses: `/private/tmp/itc216-20261023/` and `/private/tmp/claude-501/-Users-frederiktheisen-Projects-FT-ITC-Analysis/03c42abd-9b5f-41a2-8c17-d40d86ff362b/scratchpad/` (see `README-ITC-216.md`).
- Related findings (outside this issue): [ITC-218](#itc-218---bootstrap-restoration-still-reads-and-writes-the-stored-model-options) (replicate restoration uses the stored session model options), [ITC-219](#itc-219---global-replicate-restoration-and-saving-rescan-member-replicate-lists) (quadratic global replicate lookup on load and save), [ITC-220](#itc-220---interpretation-baseline-diagnostics-rescan-the-thermogram-for-every-injection) (baseline diagnostics in load-time freshness evaluation), [ITC-221](#itc-221---reopening-and-saving-regenerates-embedded-null-comparison-solution-ids) (regenerated Null-comparison solution ids).

## ITC-217 - A single retained refit gives an interval without an SD [Resolved] <a name="itc-217---a-single-retained-refit-gives-an-interval-without-an-sd"></a>

One retained refit cannot estimate dispersion. `FloatWithError.FromDistributionInPlace` keeps the original-data center (or sole value if no reference), zero SD and collapsed bounds. This also covers prediction envelopes and routinely affects members of two-experiment global leave-one-out fits. Derived-quantity policy is [ITC-117](#itc-117---derived-parameter-uncertainty-across-all-models).

## ITC-218 - Bootstrap restoration still reads and writes the stored model options

- Priority: Low; no supported normal-use trigger identified.
- Status: Investigated; remains open (2026-11-02). The conditional mechanism is runtime-confirmed only with a deliberately incomplete snapshot and manually seeded option history. No normal-use reproduction was found; resolution on that basis is recommended, not yet applied.
- Origin: ITC-216 investigation.
- Location: Core `Analysis2/BootstrapModelSnapshot.cs` (`Restore`, which calls `SingleModelFactory.InitializeModel`); `Analysis2/ModelFactory.cs` (`InitializeModel` option recovery, `SetModelOption`, `StorePreviousAttribute`). Primary solutions are restored differently, in `DataReaders/FTXTCReader.cs` (`RestoreSolutions`: `FtxtcModelRegistry.Create` and `InitializeParameters`).
- Problem: `SingleModelFactory.InitializeModel` can recover stored options whose keys are exposed by the restored model before `BootstrapModelSnapshot.Restore` overlays the snapshot's own options. An option missing from the snapshot could therefore retain a recovered value. With an empty history, this recovery loop neither recovers nor stores any options; restoration does not seed the list with catalog defaults.
- Normal-use tracing (2026-11-02): Shared desktop analysis builds complete catalog option tables and overlays user choices without using the static history. Designer factories explicitly disable stored options. Synthetic models copy all source options; snapshot capture and native writing retain every option, including disabled/default options. The native writer has captured the full snapshot option list since its introduction (`ecef5ba7`). No supported older snapshot convention omitting an option key was established.
- Saved-file evidence: Inspected all 25 native ZIP projects under `AnalysisITC.Tests`, spanning schemas 1.2, 1.3, 1.4 and 1.6. All 64,630 serialized bootstrap records contained the current model-specific option keys and matched their primary fit's key set. These cover one-set, two-set, competitive, sequential and dissociation models. No project files were edited.
- Runtime comparison: All 24 projects accepted by the current strict reader restored identically with empty history and deliberately conflicting stored values: option values, fitted values/locks/fitting flags, stored reported values/SDs/intervals, and first/middle/last injection predictions matched across 64,530 unique-primary bootstrap fits. The remaining existing fixture, `SolutionIdentityTracingTest/deleted.ftxtc`, was rejected before option restoration for an ambiguous solution-to-experiment reference; it was not modified or treated as a successful comparison.
- Normal operations: Analysis-builder and designer option changes for all six implemented models, preparation of the eight results in `JORS Table1.ftxtc`, and reading the unchanged legacy `OneSetOfSites/data.ftitc` fixture left the option history empty. This exercises the shared Core entry points, not the desktop UI. Source tracing found no normal-use source of the conflicting history required by the hypothesis.
- Conditional reproduction: An intact snapshot retained its saved options despite manually seeded conflicting history. Removing `UseSyringeActiveFraction` from a disposable in-memory snapshot and manually storing that option as true caused the missing key to inherit true instead of its false catalog default, changing all 19 injection predictions. This is a manufactured positive control, not a normal-use or unmodified-file reproduction.
- Assessment: Recommend closing under the maintainer's normal-use scope: the demonstrated effect requires both manufactured missing-option state and manually seeded history; neither required input was found in a supported workflow or the inspected older projects. No application, serialization, or defensive-handling change was made. ITC-082 remains unchanged. The earlier speculative thread-safety concern was not reproduced; ordinary restoration with empty history does not rewrite the list.
- Historical cost (2026-10-23): The recovery step and its first `ApplyModelOptions` took 12 ms and 4.7 MiB for 13,000 replicates in `JORS Table1.ftxtc`. This was a consistency question, not a performance one.
- Validation (2026-11-02): All 165 focused Core tests passed (`FTXTCFormatTests`, `FTITCBootstrapSnapshotTests`, `ExperimentDesignerStateTests`, `AnalysisBuilderTests`, `AnalysisResultUpdaterTests`). The disposable runtime probe passed 64,783 checks. No full-suite or desktop UI run was performed; no production code was changed.
- Related: [ITC-082](#itc-082---model-options-leak-between-the-experiment-designer-and-analysis), [ITC-216](#itc-216---restored-bootstrap-replicates-dominate-project-load-memory).
- Evidence: Runtime probe and logs: `/private/tmp/itc218-20261102/` (`Program.cs`, `probe.log`, `focused-tests-retry.log`, `tests/focused.trx`). Historical timing evidence as for ITC-216.

## ITC-219 - Global replicate restoration and saving rescan member replicate lists

- Priority: Not set.
- Status: Open (2026-10-23). Measured on one fixture; no fix proposed or authorized.
- Origin: ITC-216 investigation.
- Location: Core `DataReaders/FTXTCReader.cs` (`RestoreResults`, global replicate loop: `BootstrapSolutions.SingleOrDefault(... BootstrapReplicateIndex == replicate.Index)` per member); `DataExport/FTXTCFormat.cs` (`CaptureGlobalReplicates`: `BootstrapSolutions.Select(...).Contains(index)` per member).
- Problem: For each saved global replicate, restoration finds each member's replicate by scanning that member's whole bootstrap list, and saving checks membership the same way. The work therefore grows with members × replicates² per result. The reconstruction path in `GlobalSolution` already indexes replicates in a dictionary.
- Measurement: `JORS Table1.ftxtc` (8 results, 13 member fits, 1,000 replicates each): the restoration lookup took 75 ms in total, about 6 ns per comparison. Save-side time was not separated.
- Estimate (not measured): At the same cost per comparison, a 2-member result with 10,000 replicates per fit would spend about 1.2 s in the lookup on load, and a single fit with 20,000 replicates about 2.3 s; saving would be similar. The desktop applications have no replicate cap, so larger ensembles grow quadratically.
- Constraint: Any change must keep pairing by replicate index, and a missing member replicate must still not shift later pairings.
- Related: [ITC-216](#itc-216---restored-bootstrap-replicates-dominate-project-load-memory).
- Evidence: Instrumented timing on a copied Core (2026-10-23). Evidence directory as for ITC-216.

## ITC-220 - Interpretation baseline diagnostics rescan the thermogram for every injection

- Priority: Not set.
- Status: Open (2026-10-23). Source review and one sampled profile; no fix proposed or authorized.
- Origin: ITC-216 investigation (load-time profile).
- Location: Core `Interpretation/AnalysisInterpretationDiagnostics.cs` (`AddInjectionBaseline`, `Integrate`, `Interpolate`), called from `AnalysisInterpretationPackageBuilder.BuildExperiment` for every integrated injection. Project loading reaches it through `AnalysisInterpretationService.EvaluateFreshness` for each saved report with an approved automatic interpretation.
- Problem: For each injection, `AddInjectionBaseline` builds and sorts a list pairing every thermogram sample with its baseline value. `Integrate` then interpolates at every sample inside the integration window, and each interpolation finds its position with a linear `FindIndex` from the start of the list. Per experiment, the work grows with injections × samples for the sorting plus window samples × samples for the interpolation.
- Measurement: In a cold load of `JORS Table1.ftxtc` (two saved reports), freshness evaluation took about 1.8 s and 206–210 MiB of allocation. A sampled thread-time profile (one run, with the ITC-216 metadata-cache prototype) attributed about 0.95 s to `AddInjectionBaseline` and about 0.6 s to bootstrap bands. The web viewer pays the same cost because it reads projects through the same loader.
- Constraint: The diagnostic values belong to the interpretation package that is fingerprinted for freshness. Any change must keep them bit-identical, or approved interpretations may be reported as stale.
- Not checked: Warm (JIT-compiled) cost, other projects, and other callers of the package builder.
- Related: [ITC-216](#itc-216---restored-bootstrap-replicates-dominate-project-load-memory).
- Evidence: dotnet-trace sampled profile and source review (2026-10-23). Evidence directory as for ITC-216.

## ITC-221 - Reopening and saving regenerates embedded Null-comparison solution ids [Resolved] <a name="itc-221---reopening-and-saving-regenerates-embedded-null-comparison-solution-ids"></a>

Restore each nonblank saved embedded Null solution ID via `SetID` after rebuilding the detached solution. Treat IDs as opaque and generate only for absent/blank evidence. Stable IDs matter to package hashes, duplicate-import checks and remapping. See [ITC-148](#itc-148---reopening-changes-the-embedded-null-solutions-heat-method-provenance) for embedded heat-method provenance.

## ITC-222 - Result titles extend into the validity stamp [Resolved] <a name="itc-222---result-titles-extend-into-the-validity-stamp"></a>

Shared report layout reserves the measured stamp width plus an 8 pt gap, wraps the full title in the remaining space, and allocates the complete heading height. Native macOS and Avalonia draw the stamp from this shared geometry for both result headings (R01) and the cover title (F01). Stamp wording, colors and validity policy are unchanged.

## ITC-223 - Competitor properties state label is misaligned [Resolved] <a name="itc-223---competitor-properties-state-label-is-misaligned"></a>

Native macOS includes the state label in the existing trailing value group, with vertical centering for this standalone label. The label retains its natural text height and right alignment instead of stretching to the row height. Avalonia already uses its standard centered label layout and needs no change.

## ITC-224 - Uncertainty display selectors do not explain Automatic [Resolved] <a name="itc-224---uncertainty-display-selectors-do-not-explain-automatic"></a>

Results, Analysis Report, Final Figure, and Preferences on both desktop platforms use one shared Core tooltip: "Automatic: 95% CI when interval sides differ by about 50%; SD otherwise." The explanation remains available for every selected style. "About 50%" is the agreed approximation; the existing calculation and the manual's precise explanation are unchanged.

## ITC-225 - Concentration draws ignore shared stocks in global bootstrap

- Priority: Not set; scientific sampling-policy question.
- Status: Open (2026-11-05). Registered from the error-estimation review by maintainer request. Found by source review only; size of effect not measured. No handling selected or implemented.
- Location: Core `ExperimentData.AddConcentrationVariance` (one lognormal factor each for cell and syringe per synthetic clone) and `GlobalModel.GenerateSyntheticModel` (each member clone draws its own factors from the replicate stream).
- Problem: When concentration variance is enabled, each member of a global fit gets independent cell and syringe concentration factors in every bootstrap replicate, even when members share a `CellSampleId` or `SyringeSampleId`. An error in a shared stock is systematic: it moves every experiment made from that stock in the same direction. Independent draws let it partly average out across members, so the bootstrap understates its contribution to shared parameters such as a global ΔH. Single-experiment fits are not affected.
- Open question: Should members sharing a sample id share one factor per replicate? Decide whether the shared factor applies per stock id, how members with blank or differing ids are treated, and whether a member's own entered fractional SD still applies when stocks are shared.
- Boundary: Keep the lognormal mean-one factor, the cell/syringe checkbox scope and per-replicate random streams. Concentration bookkeeping (MicroCal or exponential) and dilution-heat models are separate and unaffected.
- Verify: Independent expectations for draw correlation (shared ids give identical factors within a replicate; distinct ids give independent factors) and a global case showing the shared-parameter SD with and without shared stocks.

## ITC-227 - Avalonia accepts bootstrap counts that macOS cannot request [Resolved] <a name="itc-227---avalonia-accepts-bootstrap-counts-that-macos-cannot-request"></a>

Clamp Avalonia's requested bootstrap count to at least 10, matching the native slider, and write back the effective count including for invalid text. Both use the Core tooltip. Native saved counts below 10 still restore (ITC-116). A single retained refit cannot estimate dispersion (ITC-217). Requested count is separate from retained-refit eligibility and completion policy ([ITC-185](#itc-185---choose-bootstrap-refit-effort-and-acceptance-policy)).

## ITC-226 - Entered input SDs were added to apparent values and c-values [Resolved] <a name="itc-226---entered-input-sds-were-added-to-apparent-values-and-c-values"></a>

Read entered competitor affinity/enthalpy and non-tandem cell concentration as point inputs when deriving apparent Ka/Kd, apparent ΔH and Wiseman c. Adding input SD at fixed fitted coordinates ignores fit compensation and can double-count residual-bootstrap sampling. Profile and leave-one-out uncertainty is conditional on inputs. Keep original-data centers; per-refit apparent-value propagation belongs to [ITC-117](#itc-117---derived-parameter-uncertainty-across-all-models).

## ITC-228 - Failed input profiles still give derived values an interval

- Priority: Not set.
- Status: Open (2026-11-06). Split from the ITC-117 design review. Found by source review only; not reproduced at runtime. No handling selected or implemented.
- Location: Core `Analysis2/Models/ThermodynamicSolution.cs` (`EntropyContributionFor` and the other derived getters that combine `FloatWithError` values), `Analysis2/Solver2.cs` `ProfileLikelihood`, which writes only complete intervals into `Parameters`, so incomplete coordinates keep zero error.
- Problem: In profile mode, a fitted value whose interval is incomplete keeps zero error, meaning not determined. Derived values combine their inputs in quadrature, which treats that zero like a locked, exactly known value. Example: with a complete log K interval and an incomplete ΔH interval, −TΔS and ΔS show ΔG's interval alone, which looks like a valid result. The same applies to apparent values when N's interval is incomplete, to c-values and to total ΔH.
- Direction discussed (2026-11-05): give the derived value zero error when any free input it depends on has an incomplete interval. Tell locked inputs apart through the saved lock and constraint state, not `SD == 0`. Still open: check per quantity (only that quantity's inputs) or per fit (any incomplete free coordinate).
- Boundary: Profile likelihood only. Keep CR-010's safeguards. No new display state.
- Verify: Complete log K and incomplete ΔH interval: −TΔS and ΔS have zero error. With ΔH locked instead, −TΔS takes ΔG's interval unchanged.

## ITC-229 - Profile-mode apparent values omit N and linked affinity uncertainty

- Priority: Not set.
- Status: Open (2026-11-06). Split from the ITC-117 design review as the profile-likelihood counterpart of CR-029. Found by source review only. No handling selected or implemented.
- Location: Core `Analysis2/Models/CompetitiveBinding.cs` (`CompetitionFactor`, `Kapp`, `Kdapp`, `dHapp`), `Analysis2/AnalysisCValueCalculator.cs` (competitive c-value uses `Kdapp`), `Analysis2/Solver2.cs` linked global profile writing.
- Problem:
  1. The competition factor reads N's plain value, so in profile mode apparent Kd, Ka and ΔH and the competitive c-value omit N's uncertainty. ITC-117 covers refit methods through per-refit evaluation; profile mode has no refits. In the CR-029 example, N = 0.9 to 1.1 moves apparent Kd from 41.6 to 33.2 nM around 37.0 nM.
  2. In thermodynamically linked global profile fits, member affinities are skipped and keep zero width. Kd and ΔG use the linked evaluator, but the apparent values read the member Ka directly, so they also miss the affinity uncertainty.
- Direction discussed (2026-11-05):
  - For 1, evaluate the competition factor at both ends of N's complete profile interval (the factor decreases monotonically with N), keep the competitor inputs at their saved values, and combine with the affinity (or ΔH) interval through the existing arithmetic. An incomplete N interval falls under ITC-228.
  - For 2, take the affinity from the linked evaluator, as Kd does.
- Boundary: Profile likelihood only. Syringe-correction mode uses the fixed site count, so the factor has no N dependence there. No new saved data.
- Verify: CR-029 inputs with the affinity fixed and a complete N interval [0.9, 1.1] around 1.0 give an apparent Kd interval [33.1662479036, 41.6227766017] nM around 37.0156211872 nM.
