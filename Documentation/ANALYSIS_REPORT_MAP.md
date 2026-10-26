# Analysis report map

Current report content (preview and PDF, macOS and Avalonia), top to bottom. Baseline 2026-10-07.

Codes are discussion references, not printed; keep them stable. The Analysis summary chapter
groups alternative presentations under one block code, with an order list followed by block
details. Elsewhere, bold rows are blocks; `·` rows are the lines, rows, or columns printed in that
block, in order. An empty *When* column means always.
**Std** = Standard output, **Diag** = Diagnostic output, **Trace** = Traceability Mode,
**no-binding** = assessed "No binding detected" in Std. `[Plot]` = figure; `[Info]`, `[Warn]`,
`[Error]` = information, warning, error box.

Std. and Diag. are alternative versions of a block, not separate report items. Trace. marks
additions or wording changes within either version; it is not a third output mode. A block with
no version split uses the same structure in Std. and Diag.; its inclusion conditions still apply.

Page order: F · I · per result (R · A · X · E per experiment) · S · P. Every chapter starts a new page.

Integrated-heats and residual figures use the experiment's x-axis type: molar ratio, post-injection
titrant concentration (µM) for Dissociation or a buffer-only cell, or injection number when both
concentrations are absent. `AnalysisAxisPresentation` supplies the coordinates and display scaling.
Figure canvases retain automatic titles per experiment unless an explicit title override is saved.
Report figures retain excluded injections. Traceability Mode also forces their visibility in
Final Figure and supporting figures, regardless of the excluded-point visibility choice.
Generated numeric text and scientific graph labels use period decimal markers regardless of
regional settings, consistently in previews, PDF exports, and printing in both desktop applications.

In both desktop builders, the top Report inspector section has a separate Saved Reports…
picker above equally sized Save Report and Save Report As… buttons
in one row. The saved-report picker lists the latest saved reports first with their result
names and last-save time in compact three-line rows, with emphasized titles and separators
between reports. Long titles and result names truncate, with full text available on hover;
the macOS scroll area uses the popover background. It refreshes each time it opens,
including after Save As, and
uses the existing unsaved-draft prompt before replacing a draft. With no saved reports it
shows an empty-state message. The Report contents picker contains only results and
supporting experiments, with emphasized names above their details in both apps;
its macOS scroll area also uses the popover background.

PDF label/value rows use 9 pt text on a 12 pt line height, with 2 pt padding above
and below the text. A single-line row is 16 pt tall in both desktop renderers.


## F. Front page

| Code | Content | When / omitted |
| --- | --- | --- |
| **F01** | **Title and status badge** | full title wraps beside the measured badge with an 8 pt gap |
| | · Title: configured report title; when blank, the single result's name or "Analysis report" | the title also supplies the saved report name; automatic or blank titles use a content-summary name |
| | · Badge: ANALYSIS VALID / ANALYSES VALID · REVIEW WARNINGS · PARTIAL / STALE · INVALID / STALE | worst status of all results |
| **F02** | **Subtitle** | only when a document label is set |
| **F03** | **Sign-off** (bottom of front page) | Trace only |
| | · Prepared by: operator, or Not recorded | |
| | · Generated at: timestamp | |
| | · Report ID: ID, or Not recorded | |
| | · Preparer signature ____ | |
| | · Date signed ____ | |
| **F04** | **Report scope** | |
| | · Analysis results | |
| | · Distinct result experiments | supporting experiments not counted |
| | · Supporting experiments | only with supporting experiments |
| **F05** | **Included results** (table, one row per result) | |
| | · Result: "1. Name" | |
| | · Model: "Attempted: <model>" when binding is hidden | "Attempted:" Std only |
| | · Analysis date | |
| | · Experiments | |
| | · Status: Valid · Warnings · Partial / stale · Invalid / stale · Unknown | |
| | · Binding assessment: outcome, or Member assessments summary | "(manual)" when any assessment is overridden |
| **F06** | **Contents** (with page numbers) | |
| | · Interpretation | only with saved interpretation |
| | · Result n. Name | one per result |
| | ·· <Advanced analysis title>, indented under its result | one per X chapter of that result |
| | ·· 1A. Experiment name, indented under its result, after X entries | one per experiment chapter of that result |
| | · Supporting experiments | only with supporting experiments |
| | · Appendix | |

## I. Interpretation

Generated provenance uses the saved provider (OpenAI or Mistral), model and reasoning. The provider selector does not add report rows or change the native project format.

The interpretation evidence level applies fully to fitted result members.
Selected supporting experiments always contribute summary metadata; Processing
details and Thermograms add processing settings and scalar baseline statistics
and reasons, while injection rows, traces, landmarks and baseline controls are
omitted. These transmission rules do not change printed report contents.

Whole chapter omitted when the report has no saved interpretation.

Opening another report clears the builder's text undo and redo history.
Opening a saved report also refreshes its save buttons.

User text edits immediately replace active AI provenance with manual origin and
mark the detached draft unsaved until explicit report saving, including edits
returned to the original wording. Generated text is read-only in the generation
dialog and becomes editable after Use in Report. The builder-only review notice
applies to both origins after any included result content change (including names,
comments, assessments, and solution updates) or changed result selection. Confirm
preserves the text and its source; editing records new user text. This notice is
not I01 and is not printed.

| Code | Content | When / omitted |
| --- | --- | --- |
| **I01** | **[Warn] Out-of-date interpretation / Interpretation freshness unknown** | generated text not current; never for manual text |
| | · Reason + "The approved text has been retained and should be reviewed before use." | |
| **I02** | **Interpretation text**: saved ## / ### headings, paragraphs, - bullets, tables | manual text without "## " gets "Overall interpretation" heading |
| **I03** | **Source and editing history** (rows) | Trace only |
| | · Source: Written by the user · Saved | manual text |
| | · Source: Automatically generated · User edited (Yes / Not marked as user-edited) · AI model (provider, model, reasoning in one row) | generated text; omit missing model details, or "not supplied" if all are missing; no request ID, scientific guidance, generation or approval timestamps |

## R. Result overview (per result)

R01/R03 use shared input validity. Temperature edits produce "temperature changed";
repeated property changes use the shared category/count summary. Any detected fit-input
change makes a result with captured temperature dependence Invalid, including individually
fitted series. Independent collections without temperature dependence retain partial invalidity.
For results in the active document, a source experiment absent from the experiment browser
makes the whole result Invalid with a named missing-experiment reason; Undo recomputes validity.

| Code | Content | When / omitted |
| --- | --- | --- |
| **R01** | **Result n. Name** + status badge for this result | full title wraps beside the measured badge with an 8 pt gap |
| **R02** | **Analysis** | |
| | · Analysis date | |
| | · Analysis operator: name, or Not recorded | Trace only |
| | · Model, or "Attempted binding model" | "Attempted" when Std hides binding |
| | · Member assessments (independent collection) or Binding assessment (single/pooled) | only when Std hides binding (shown in Diag too); independent collections show their common outcome or Mixed assessments |
| | · Experiments | |
| **R03** | **[Info/Warn/Error] status title** (Valid with warnings · Partially invalid or stale · Invalid or stale · Validity unknown) | omitted when Valid with no recorded reasons |
| | · Validity reasons unchanged; fit-warning categories and inconclusive member assessments use method-specific messages with counts and report references | |
| **R03a** | **[Warn] Mixed injection bookkeeping** | only when the result's experiments have different saved methods |
| | · "Experiments in this result use different injection bookkeeping methods." | |
| | · <method>: <labels>, one line per method | |
| **R04** | **Comments** | only when written |
| **R05** | **Experiment overview** [Plot] | |
| | · One small final-fit panel per experiment: 1A, 1B, … + name; heats, fit, residuals | |
| | · Saved Null solution instead of binding fit; uncorrected integrated heats | no-binding experiments |
| | · "Null model fit unavailable" tag, no residuals | no-binding without a matching saved Null solution |
| | · "Binding-fit diagnostics" tag | Diag |

## A. Analysis summary (per result)

Blocks appear in this order; conditional blocks are skipped. Std. and Diag. versions occupy
the same position. A03 is a historical alias for **A01 · Std.**, not a separate block. Later
codes retain their existing identifiers.

| Order | Block | Versions / inclusion |
| --- | --- | --- |
| 1 | [A01 · Binding assessment](#a01-binding-assessment) | Std. / Diag.; details below |
| 2 | [A02 · Pooled comparison diagnostics](#a02-pooled-comparison-diagnostics) | Diag. only; independent collection |
| 3 | [A04 · No binding detected](#a04-no-binding-detected) | Std. / Diag.; any no-binding member |
| 4 | [A05 · Thermodynamic summary](#a05-thermodynamic-summary) | same structure; eligible finite values required |
| 5 | [A06 · Experiment parameter overview](#a06-experiment-parameter-overview) | same columns; Std. / Diag. row differences |
| 6 | [A07 · Buffer subtraction used in fit](#a07-buffer-subtraction-used-in-fit) | only with subtraction at fit time |
| 7 | [A08 · Combined parameters](#a08-combined-parameters) | multiple experiments; output allowed and evaluable |
| 8 | [A09 · Summary uncertainty](#a09-summary-uncertainty) | expanded explanations + CI style + approximate intervals |
| 9 | [A10 · Model and fit details](#a10-model-and-fit-details) | both |
| 10 | [A11 · Fixed parameters](#a11-fixed-parameters) | only with locked parameters |
| 11 | [A12 · Reading fit diagnostics](#a12-reading-fit-diagnostics) | expanded explanations + weighted fit |
| 12 | [A13 · Shared parameter correlation](#a13-shared-parameter-correlation) | selected + residual bootstrap; output allowed |
| 13 | [A14 · Reading parameter correlations](#a14-reading-parameter-correlations) | expanded explanations + A13 |

### A01. Binding assessment

**Std.** — printed heading: **Binding assessment**. Single or pooled result only;
omitted for Not assessed and independent collections. Rows, in order:

| Row | Content / conditions |
| --- | --- |
| Conclusion | Effective outcome; "(manual)" for an override. |
| Binding model | Saved binding model name. |
| Null model | Saved Null model name. |
| Null fit | Parameters per experiment (1A: Offset = …) + RMSD, or failure status. |
| ΔAICc | "+42.1 (binding 120.3, null 162.4)", or reason unavailable. |

**Diag.** — printed heading: **Binding assessment and null comparison**. For a
single or pooled result, rows appear in this order:

| Row | Content / conditions |
| --- | --- |
| Conclusion | Effective outcome; "(manual)" for an override. |
| Null model | Saved Null model name. |
| Null fit | Fit status. |
| Null model parameters | Saved parameters per experiment. |
| Null RMSD | Selected heat unit; "Unavailable" has no unit. |
| Binding AICc | Saved binding criteria. |
| Null AICc | Saved Null criteria. |
| ΔAICc | Saved signed difference, or unavailable. |
| Comparison reason | Availability or comparison explanation. |

For an **independent collection**, Diag. instead prints these two kinds of rows:

| Row | Content / conditions |
| --- | --- |
| Member assessments | Common effective outcome or Mixed assessments; includes Inconclusive and Not assessed. Adds "(manual)" when any member is overridden. |
| <Outcome> | Count, one row per outcome present. |

A02 follows this independent-collection version of A01.

### A02. Pooled comparison diagnostics

**Diag. only; independent collection.** Printed heading: **Pooled Comparison Diagnostics**.

| Content, in order | Conditions |
| --- | --- |
| "Pooled across the independently fitted experiments with one common variance; diagnostic only…" | Does not determine member assessments. |
| ΔAICc, or reason unavailable | |
| Null fit: RMSD, or status | |

### A04. No binding detected

**[Info]** Shown when any member is effectively No binding detected. Wording differs:

| Version | Printed text |
| --- | --- |
| Std. | "Combined binding values and dependent analyses are omitted because no binding was detected in: <names>. These members are omitted from the thermodynamic summary." |
| Diag. | "No binding was detected in: <names>. These members are omitted from the thermodynamic summary." |

### A05. Thermodynamic summary

**[Plot]** Same structure in both modes. Excludes effectively No binding detected members;
omitted when no eligible finite ΔH/−TΔS/ΔG remain. Manual overrides are respected.

| Content, in order | Conditions |
| --- | --- |
| Bar groups ΔH · −TΔS · ΔG (numbered per step) | |
| One series per experiment, legend 1A, 1B, … | No-binding members omitted in both modes. |
| Whiskers: saved 95% CI | None for fixed values. |
| Note: "Bars: 95% CI is the saved interval from the <source>." | |

### A06. Experiment parameter overview

Table with one row per experiment. Columns appear in this order; only cell contents differ
between Std. and Diag.

| Column | Content / conditions |
| --- | --- |
| Experiment | "1A. Name", with assessment on a second line when applicable. |
| Temperature (°C) | Temperature dependence enabled. |
| [Ions] (mM) | Uses the captured run buffer policy. Electrostatics enabled: one-set-of-sites model, salt metadata for every member, and three usable ionic strengths selected by the strict greedy half-minimum-span spacing rule. |
| ∆H,ion (kJ/mol) | Protonation enabled: every member has usable buffer thermochemistry and at least two distinct finite buffer enthalpies exist. Missing values never supply a zero. |
| One column per binding model parameter | Saved model parameters only: N (or α), Kd, and ΔH as applicable per site/step. Offset and derived values (including apparent Kd, ΔG, and −TΔS) are omitted from this overview. Fixed and free affinities use the unit in their column header. "(fixed)" for fixed values, without uncertainty; "—" for non-finite values. |
| RMSD (µJ) | Member fit RMSD. |

AIC/AICc are omitted from this overview. Full parameter tables and the separate fit
diagnostic and binding-assessment blocks retain their existing contents.

| Version | Cell differences |
| --- | --- |
| Std. | Assessment shown only for No binding detected / Inconclusive. Parameter and RMSD cells are blank for no-binding members. |
| Diag. | Every assessed outcome is shown; attempted binding parameter and RMSD cells are retained. |

Assessment text always includes "(manual)" for overridden members in either mode.

### A07. Buffer subtraction used in fit

Fit-time setting; omitted when no experiment was subtracted at fit time.

| Content | Conditions |
| --- | --- |
| <label>: <reference>; <method>, one row per subtracted experiment | "Reference experiment unavailable" if missing. |

### A08. Combined parameters

Omitted for a single experiment, when combined binding output is hidden (Std.), or when
the result is not evaluable. Rows, in order:

| Row | Content / conditions |
| --- | --- |
| Evaluation temperature | Reference temperature, or mean experiment temperature. |
| Heat capacity change (∆Cp) | Temperature dependence enabled. |
| Enthalpy (ΔH) | Per step. |
| Entropy contribution (−TΔS) | Per step. |
| Gibbs energy (ΔG) | Per step. |
| Kd (from ΔG) | Per step. Shared summary evaluator: fitted Gibbs center transformed to Kd, Gibbs SD propagated through the exponential, and Gibbs interval endpoints transformed. Same convention in desktop evaluation, table/clipboard export, and web viewer (CR-024). |

### A09. Summary uncertainty

**[Info]** Expanded explanations + CI style + approximate intervals. Explains that intervals
come from individual 95% CIs and spread, coverage is not established, and covariance is omitted.

### A10. Model and fit details

Same structure in both modes. Rows, in order:

| Row | Content / conditions |
| --- | --- |
| Analysis | Experiments fitted globally / individually. |
| Model | Saved model name. |
| <option name> | One row per model option, indented under Model; routine defaults omitted. |
| Ligand affinity (Kd) | Indented under Model; concentration unit, converted from stored log10(Ka); report uncertainty style. |
| Ligand enthalpy | Indented under Model; energy/mol unit; report uncertainty style. |
| Constraint | <parameter>: <description>, one row per constraint. |
| RMSD / Molar RMSD | µJ / kJ/mol. |
| Uncertainty | Method; outcome; n of m refits succeeded, or method; "not estimated (no binding detected)". Omitted without convergence info; skipped form when estimation was skipped for no binding (ITC-052). Profile outcomes use this generic row; there is no separate profile-status section. Shared aggregation preserves cancellation and recorded profile failure, including absent runs (ITC-094). |
| AIC / AICc | Omitted for independent collections. |
| Solver | Algorithm; termination; iterations (time); weighting. Omitted without convergence info. |

### A11. Fixed parameters

Only when parameters are locked. Rows, in order:

| Row | Content |
| --- | --- |
| <Parameter> — <experiment> | Single-experiment fits only; value and unit. |
| Shared <Parameter> | One row per locked shared coordinate; value and unit. Multi-experiment fits list only these rows, independent of experiment names. |

### A12. Reading fit diagnostics

**[Info]** Expanded explanations + weighted fit. Explains the weighted fitting objective
and displayed unweighted RMSD.

### A13. Shared parameter correlation

Selected + residual bootstrap; omitted when combined binding output is hidden (Std.).

| Content, in order | Conditions |
| --- | --- |
| Matrix, Pearson r; labels "Global ·" / "Experiment ·" | |
| "Residual bootstrap (Pearson); n complete replicates." | |
| Reliability warnings | Only when any. |

### A14. Reading parameter correlations

**[Info]** Expanded explanations + A13.

## X. Advanced analyses (per result, after A)

Each is a chapter, only when selected and available, always in the order X01–X06. All omitted when Std hides the result's binding.

| Code | Content | When / omitted |
| --- | --- | --- |
| **X01** | **Temperature dependence** | |
| | · [Plot] ΔH / −TΔS / ΔG vs temperature, per step; saved fitted lines; band in CI styles | |
| | · Parameters at <T>: same lines as A08 except Evaluation temperature | omitted when not evaluable |
| | · [Info] Summary uncertainty (as A09) | as A09 |
| **X02** | **Structuring** (no plot) | |
| | · Saved result heading | |
| | · Method: Spolar–Record | |
| | · Interaction type: Two folded proteins / Intermediate / Folded and disordered protein | |
| | · Evaluated at: Iso-entropic point / Mean experimental temperature / Reference temperature | uses the completed run’s saved mode; new desktop runs are temporarily restricted to the isoentropic point (ITC-068), while saved mean/reference outputs remain reportable |
| | · Iso-entropic / Mean / Reference temperature | saved value and available uncertainty via `FloatWithError.AsNumber`, following report units and uncertainty style |
| | · Hydration entropy (−TΔS_HE) | |
| | · Conformational entropy (−TΔS_conf) | |
| | · Residues folding upon binding | |
| | · Uncertainty: "Repeated random sampling of saved input uncertainties." | |
| | · Completed | |
| | · [Info] Reading structuring estimates: how entropy is split; Theisen et al. (J. Am. Chem. Soc. 2021) coefficients; model-based, not structural measurements | expanded explanations only |
| **X03** | **Affinity versus salt** | electrostatics enabled (same condition as A06 [Ions]); omitted when the spacing rule fails |
| | · [Plot] Kd vs salt concentration (mM), saved 95% CI, no fitted line | |
| **X04** | **Debye-Huckel dependence** | requested and completed ionic-strength component has usable parameter centers and unchanged calculation inputs; saved outputs remain eligible when only the current spacing preference fails |
| | · [Plot] log10(Kd / M) vs sqrt(Ionic strength / M); points + fitted curve (legend: "Fitted curve") | Observations use the completed fit's saved buffer-inclusion policy, independently of current eligibility and the next run's captured preferences. Incomplete saved electrostatics payloads are omitted. |
| | · Saved result heading | |
| | · Kd at zero ionic strength | only when the ionic-strength component is available |
| | · Salt sensitivity | only when the ionic-strength component is available |
| | · Curvature | only when the ionic-strength component is available and curvature is used |
| | · Salt-dependence slope | only when the shared Core component-availability check passes |
| | · Uncertainty | |
| | · Completed | |
| **X05** | **Salt dependence** | requested and completed concentration-slope component has usable slope/intercept centers and unchanged calculation inputs; saved outputs remain eligible when only the current spacing preference fails |
| | · [Plot] ln(Kd / M) vs ln(Salt concentration / M); points + fitted line (legend: "Fitted line"); caption identifies salt | Same single-salt Core coordinates as desktop/viewer. Version-1 counter-ion fits are omitted on read; recalculate to obtain this component. |
| | · Saved result: same lines as X04 | |
| **X06** | **Protonation dependence** | |
| | · [Plot] Observed enthalpy vs Buffer ionization enthalpy; points + fitted line (legend: "Fitted line") | |
| | · Saved result heading | |
| | · Buffer-independent enthalpy (at the experimental pH) | |
| | · Net proton uptake (positive uptake; negative release) | |
| | · Sign convention: positive uptake; negative release | |
| | · Intercept meaning: at experimental pH; retains binding-partner protonation heat | |
| | · Uncertainty | |
| | · Completed | |

## E. Experiment chapter (per experiment)

| Code | Content | When / omitted |
| --- | --- | --- |
| **E00** | **1A. Experiment name** | |
| | · Conditions subtitle: nominal initial concentrations as syringe ▸ cell; target temperature; buffers with pH; salts; recorded competitor concentration and explicit ionic strength, separated by pipes | all modes, including condensed repeats; unavailable items omitted |
| **E01** | **Experiment figures** | only with raw thermogram (else E01a) |
| | · [Plot] Baseline and integration windows: raw thermogram, baseline, windows | |
| | · [Plot] Final fit: corrected thermogram, heats + error bars, fit line, band, residuals | |
| | · Paired graph frames share their top and bottom positions; the final figure includes its residual panel and gap within that height | macOS and Avalonia |
| | · Saved Null solution instead of binding fit; uncorrected integrated heats | no-binding |
| | · "Null model fit unavailable" tag, no residuals | no-binding without a matching saved Null solution |
| | · "Binding-fit diagnostics" tag | Diag |
| **E01a** | **No raw thermogram** | only without raw thermogram |
| | · [Plot] Final fit (as E01) | E04a states the input |
| **E02** | **Experiment details** | first appearance (else E03) |
| | · Experiment date (data file) / (user provided) | omitted otherwise; Trace adds (file system date) or Not recorded |
| | · Source file: name (format) | |
| | · External experiment ID | omitted if blank; Trace: Not recorded |
| | · Cell sample/batch ID | omitted if blank; Trace: Not recorded |
| | · Syringe sample/batch ID | omitted if blank; Trace: Not recorded |
| | · Temperature: Measured …; target … | |
| | · Cell concentration | |
| | · Syringe concentration | |
| | · Injections | |
| | · Experiment settings (subheading) | |
| | ·· Instrument | |
| | ·· Cell volume (µL) | |
| | ·· Stirring speed (rpm) | omitted if not recorded |
| | ·· Feedback | omitted if not recorded |
| | ·· Initial delay (s) | omitted if zero |
| | · Attributes (subheading) | omitted when no attributes |
| | ·· One line per attribute (Buffer, Salt, pH, …) | |
| | ·· Buffer subtraction: "<ref> (Experiment S1; <method>)" (current setting) | report label only when the reference is in the report |
| | ·· Competitor properties: From result "…", Kd = …, ΔH = … on separate lines, or reason not captured | competitor attribute; result chapters use fit-time captures only, never later live-attribute values |
| | · Tandem merge origin: mode, mixing settings, and source files on separate lines | recorded tandem provenance, including single-file concatenated imports |
| | · Tandem sources: "n of m recorded source experiments … not in this project." | only when sources are missing |
| **E03** | **Experiment details — condensed** | repeat appearance + condense on |
| | · Experiment date | as E02 |
| | · Previously reported as: 1A | |
| | · Source file | |
| | · External / Cell / Syringe IDs | as E02 |
| | · Instrument | |
| | · Temperature | |
| | · Cell concentration | |
| | · Syringe concentration | |
| | · Attributes, Tandem lines | as E02 |
| | · (Injections and the rest of Experiment settings omitted; E04 and E04a omitted) | |
| **E04** | **Processing and integration** | with raw thermogram; omitted when condensed (E03) |
| | · Baseline method: Spline · Linear/Smooth interpolation · n points (sparse/balanced/dense) / Polynomial · nth degree / Segmented · Constant/Linear/Quadratic segments | density: sparse < injections + 2 ≤ balanced < 2 × injections ≤ dense |
| | · Baseline status: Incomplete | only when incomplete |
| | · Injection use: n included; excluded: … | |
| | · Integration regions: Fit Peaks / Set manually / Peak shape × n | "Set manually" also covers older files saved before Fit Peaks was recorded |
| | ·· Start after injection (s) | |
| | ·· End after injection (s) | |
| | · Injection bookkeeping: saved fit-time method, or "Unknown concentration method; <heat> heat" | |
| **E04a** | **Data availability** | without raw thermogram; after E02; omitted when condensed (E03) |
| | · Input heats: Imported integrated heats | |
| | · Injection use: n included; excluded: … | |
| | · Available heats: n of m | only when any heat is absent, non-finite, or not integrated; n counts injections marked integrated with a finite current peak area; shown as 0 of 0 when there are no injections |
| | · Injection bookkeeping: as E04 | result experiments only; omitted for supporting experiments (S02a) |
| **E04b** | **[Warn] Fit warnings**: one line per warning (parameter at boundary; bootstrap/leave-one-out boundary; limit-terminated refits) | members whose fit warnings count toward result health (not no-binding); before E05 |
| **E05** | **Fitted and derived parameters** (table) | omitted for no-binding |
| | · Columns: Parameter · Type (Fitted / Fixed / Derived) · Value · Unit | |
| | · One row per reported parameter, incl. Offset; fixed values without uncertainty; "—" non-finite | |
| **E06** | **Parameter correlation** (this experiment) | selected + >1 experiment + bootstrap; omitted for no-binding |
| | · Matrix, Pearson r | |
| | · "Residual bootstrap (Pearson); n complete replicates." | |
| | · Reliability warnings | only when any |
| **E07** | **[Info] Reading parameter correlations** | expanded + E06 |
| **E08** | **Fit details** | |
| | · RMSD / Molar RMSD | omitted for no-binding |
| | · Wiseman c-value / (site 1), (site 2) / c-value (step n) / Apparent c-value | by model; uses the report uncertainty style; omitted for no-binding |
| | · c-value concentration basis: Initial tandem segment | tandem only |
| | · Uncertainty: same form as A10, including "not estimated (no binding detected)" and preserved profile/cancellation outcomes | omitted for global or constrained fits |
| | · Binding assessment + "(manual)" if overridden | independent collection, assessed |
| | · ΔAICc (null − binding) | independent collection, assessed |
| **E09** | **Comments** | only when written |
| **E10** | **Injection table** (one row per injection) | tables on; replaced by E11 for no-binding |
| | · # | |
| | · Use: Yes / No | |
| | · Vol. (µL) | |
| | · [M] (µM) | |
| | · [L] (µM) | |
| | · Ratio, or "[L] axis (µM)" / "Injection" by x axis | |
| | · Heat (kJ/mol) | blank if not integrated |
| | · Heat SD (kJ/mol) | blank if not integrated |
| | · Fit (kJ/mol) | blank if not integrated |
| | · Residual (kJ/mol) | blank if not integrated |
| **E11** | **Saved null comparison injection evidence**, or **Injection table — Current experiment data** | tables on + no-binding; second title when no saved points |
| | · Injection | |
| | · Use: Included / Excluded | |
| | · Saved ratio | |
| | · Injected amount (mol) | |
| | · Observed heat (selected heat unit) | |
| | · Null prediction (selected heat unit) | blank without saved points or successful null fit |
| | · Residual (selected heat unit) | blank without saved points or successful null fit |

## S. Supporting experiments (one chapter)

Whole chapter omitted when there are no supporting experiments. No fit is shown, even if attached.

| Code | Content | When / omitted |
| --- | --- | --- |
| **S00** | **S1. Experiment name** | one per supporting experiment |
| | · Conditions subtitle: same syringe ▸ cell concentration field and remaining conditions as E00 | all modes |
| **S01** | **Figures** | |
| | · Current experiment data — observations: [Plot] Baseline and integration windows + [Plot] Integrated heats — no fit | raw + heats |
| | · Paired graph frames share their top and bottom positions | macOS and Avalonia |
| | · [Plot] Current experiment data — baseline and integration windows | raw only |
| | · [Plot] Integrated heats — Current experiment data; no fit | heats only |
| | · (no figure) | neither; S02a and S03 state what is missing |
| **S02** | **Experiment details**: same lines as E02 | |
| **S02a** | **Data availability** | after S02; only without raw thermogram; same rows as E04a |
| **S03** | **Notes** | omitted when no line applies |
| | · Baseline: Incomplete | raw + incomplete baseline |
| | · Integrated heats: Stored corrected heats; configured reference <name> (<method>) | buffer-subtracted |
| | · Used as subtraction reference by: 1A, … | used by a saved fit |
| **S05** | **Comments** | only when written |
| **S06** | **Injection table**: E10 columns without Fit and Residual | tables on |

## P. Appendix

| Code | Content | When / omitted |
| --- | --- | --- |
| **P01** | **Experiment sources** (table, one row per distinct experiment incl. supporting) | |
| | · Reported as: all labels (1A, 2A) | |
| | · Experiment | |
| | · Source file | |
| | · T (°C), measured | |
| | · Cell | |
| | · Syringe | |
| | · Inj. | |
| | · Role: Buffer reference for … / Tandem source for … | blank when neither |
| **P03** | **[Warn] Report warnings** | omitted when no warnings |
| | · <Result>: The saved result is reported with status: <status>. | status not Valid |
| | · <Result>: Experiments use different injection bookkeeping methods (<method>: <labels>; …). | as R03a |
| | · <Result>: Best fit reached a parameter boundary. N of M experiments (<labels>). | P03 eligibility below |
| | · <Result>: One or more bootstrap/leave-one-out fits reached a parameter boundary. N of M experiments (<labels>). | method-specific; P03 eligibility below |
| | · <Result>: One or more bootstrap/leave-one-out refits reached an optimizer limit and were excluded. N of M experiments (<labels>). | method-specific; P03 eligibility below |
| **P04** | **Report details** | |
| | · Software | |
| | · Application version | |
| | · Report identifier | Trace only |
| **P05** | **Result identifiers** | Trace only |
| | · One independently pageable row per result: Result 1 · <id> | report order |

## H. Page furniture and validation

| Code | Content | When / omitted |
| --- | --- | --- |
| **H01** | **Header** | every page |
| | · FT-ITC ANALYSIS REPORT | front page |
| | · FT-ITC Analysis · <result> · 1A. <experiment> | other pages; parts that apply |
| | · Generation timestamp (right) | |
| **H02** | **Footer** | every page |
| | · Software and version (left) | |
| | · Report title (centre) | omitted on front page; single line, shortened with an ellipsis to preserve an 8 pt gap from both side fields |
| | · Page n of N (right) | |
| **H03** | **" – continued"** after a block heading split across pages, including notices and key-value rows split into line slices | |
| **V01** | **Error list instead of a report** | no result; duplicate or missing selection; no model or experiments; missing/non-finite parameters where binding is shown |

## Rules that affect many blocks

- CR-023 warning summaries: M is every member of the result; N counts distinct eligible member indices for the category. Multi-member results use the count/reference format even when N = 1; single-member results show the plain message without “1 of 1.” References follow member order, compressing runs of at least three consecutive indices (including Z→AA). P03 retains the result-name prefix and orders status, mixed bookkeeping, best-fit boundary, uncertainty boundary, then uncertainty limit within each result. Standard P03 omits effective no-binding members; Diagnostic P03 includes them. R03 fit warnings always use `FitWarningMembers`, excluding effective no-binding members. R03 orders validity reasons, best-fit boundary, uncertainty boundary, uncertainty limit, then inconclusive assessment (including single/pooled assessments). It uses structured health reasons; app health-reason text is unchanged. E04b keeps the detailed member warnings.

- Buffer-protonation eligibility requires usable thermochemistry for every member and at least two distinct finite buffer enthalpies. A06 follows this shared eligibility; missing values never supply zero and members are not silently omitted. X06 remains conditional on a completed saved fit. Unavailability explanations appear in both desktop inspectors; no report block or layout is added (CR-021).

- Numerical profile uncertainty uses the owning run's eligibility. Coordinates retained in a `CompleteFailure` run are diagnostic only, including completed affinity intervals and linked thermodynamic contributions. A05/A08/A10/E05 must not turn those coordinates into reported uncertainty; existing fit details keep the failed outcome and diagnostic endpoint counts. No report layout changes (CR-010).

- Experiment conditions subtitles use 11 pt light text in the normal text color and a 3 pt gap below their heading. Initial concentrations form one field with syringe first and cell second (`100 µM ▸ 10 µM`); remaining fields use spaced pipes (` | `). Each concentration retains its own unit; unavailable concentrations are omitted without leaving an orphan triangle. The heading and complete wrapped subtitle stay together. Temperature follows the report's °C/K setting and retains the report's two-decimal rounding while omitting trailing zeros; concentrations omit uncertainty. Ionic strength is included only from an explicit attribute, without calculation. Detailed rows retain their full values and uncertainties.

- Boxes: [Warn]/[Error] only for something that affects whether presented values can be trusted or compared; [Info] only for output omitted or changed by an assessment (A04) or an opt-in expanded explanation. Routine metadata and data availability use rows. Exception kept: R03 shows [Info] for a Valid result with recorded reasons.
- Std hides binding output for no-binding; manual overrides count; Inconclusive and Not assessed count as binding.
- Assessment text always includes "(manual)" for overrides, independently of Traceability Mode; collection summaries are marked when any member is overridden.
- Independent collection: each experiment assessed separately; combined output only if all show binding.
- Fitted values come from the saved fit; experiment details, processing, plotted heats, and injection tables are current data, except the E04/E04a Injection bookkeeping row, which is saved with the fit.
- Uncertainty style (default SD + 95% CI) formats values; fixed values have none; A05 and X03–X06 error bars are always 95% CI.
- RMSD is always µJ, molar RMSD kJ/mol, whatever the energy unit.
- Defaults: injection tables on, condense repeats on, expanded explanations off, advanced analyses off.
- Both desktop report builders disable Expanded explanations while busy and restore it only when results are selected. A later change invalidates the preview; PDF export rebuilds stale content using the selected option (CR-034).

## Known issues

- E11: current-data fallback still headed "Saved ratio".

## Sources

`AnalysisITC.Core/Presentation/`: `AnalysisReportBuilder.cs` (blocks, conditions), `ResultOutputPolicy.cs` (binding output),
`AnalysisResultOverviewTable.cs` (A06), `AnalysisResultParameterEvaluation.cs` (A08, X01), `ExperimentOverviewTable.cs` (E10, S06),
`PublicationFigure*.cs` (figures), `AnalysisReport*Layout.cs` (header, footer, sign-off, layout, A05).
Renderers: `CoreGraphicsAnalysisReportRenderer.cs` (macOS), `SkiaAnalysisReportRenderer.cs` (Avalonia).

## Revision notes

- 2026-10-26 (ITC-222): F01/R01 reserve the measured status-stamp width plus an 8 pt gap before wrapping the full title. Both desktop renderers draw the stamp from the shared heading geometry; pagination reserves the full wrapped heading height. Stamp text, colors and validity policy are unchanged. No map discrepancy was found.

- 2026-10-25 (ITC-224): Both desktop report builders' Uncertainties selectors share the tooltip "Automatic: 95% CI when interval sides differ by about 50%; SD otherwise." The rounded explanation leaves the existing display calculation and report output unchanged.

- 2026-10-21 (ITC-034): Supporting experiments transmit summary metadata at every evidence level; Processing details and Thermograms add processing settings and scalar baseline statistics/reasons. They never send injection rows, thermograms, landmarks or baseline control representations. Fitted result members retain the selected level. Printed report contents are unchanged.

- 2026-10-19 (CR-034): Avalonia now includes Expanded explanations in its existing busy policy, matching native macOS. The option remains fixed during construction; later changes still invalidate the preview and rebuild on export. A09/A12/A14 explanation content and triggers are unchanged.

- 2026-10-19 (CR-023 review): Notice pagination and both desktop renderers share notice geometry, body bounds and line advance. Renderer tests verify that each notice fragment actually paints its final line. R03 single/pooled inconclusive assessments again follow fit warnings, matching their previous order; validity reasons remain first. Near-boundary pagination cannot append a zero-line notice fragment. P03/R03 eligibility and count definitions are unchanged.

- 2026-10-18 (CR-023): P03 replaces per-experiment sentences with method-specific count/reference summaries. Previously its `<Experiment>` was the experiment name, not the report label, and its “bootstrap” sentence also appeared for leave-one-out results; both discrepancies are corrected. R03 adds counts/references to already-deduplicated fit-warning sentences and groups inconclusive member assessments. P04's multiline identifier value moves to independently pageable P05 rows. Notices and exceptionally tall key-value rows continue without dropping lines; measurement follows the existing drawn geometry, so other key-value blocks can reflow slightly. The shared app formatter retains its prior text and sentence deduplication; same-name inconclusive app reasons remain tracked separately.

- 2026-10-16: Both builders use detached report drafts with explicit Save Report and Save Report As. Project save, autosave, preview, and PDF export do not commit drafts. Saved names follow the title, and Project Information Rename changes both. Result/supporting selections apply together and edit the current draft without automatically opening another report. A builder-only review warning covers changed result scope or results updated since the text was approved/confirmed, for both manual and AI text. Confirm persists review time/scope without changing fingerprints or I01. Save As preserves original fingerprint identity/date for v1 and v2, avoiding a freshness warning from copying alone. Printed I01 and interpretation provenance rows are unchanged.

- 2026-10-16: The macOS report builder's interpretation editor surround uses the system window background for a lighter appearance. Report preview and PDF content are unchanged.
- 2026-10-12: Analysis summary uses an order list and grouped block details. A01 contains its Std. and Diag. versions; A03 is retained as a historical alias for A01 · Std. Trace. changes are marked within the affected rows. A06 now explicitly distinguishes Standard no-binding blanks from retained Diagnostic values; the earlier blanket "blank for no-binding" wording was incomplete. Report behavior is unchanged.
- 2026-10-07: X moved from after E to directly after A; each X chapter still starts a new page.
- 2026-10-07: F06 Contents lists X chapters, indented under their result.
- 2026-10-07: F06 Contents lists experiment chapters, indented under their result after X entries. Supporting experiments remain one entry.
- Open: X01 "Parameters at T" repeats A08.
- 2026-10-07: X chapters use a fixed order (X01–X06) instead of selection order.
- 2026-10-07: X02 renamed from "Spolar Record" to "Structuring"; rows relabelled and a "Method: Spolar–Record" line added (wording from Theisen et al., JACS 2021).
- 2026-10-07: X02 adds the "Reading structuring estimates" info box with the Theisen et al. (2021) reference, expanded explanations only.
- 2026-10-07: Result views (both platforms): "Temperature" view keeps its name; its analysis panel is "Structuring" with shared Core labels and tooltips.
- 2026-10-09 (ITC-071): X02 and both desktop structuring outputs share temperature formatting, retaining available saved uncertainty and following their Celsius/Kelvin and uncertainty display selections. Avalonia updates its structuring output immediately on a unit change. Saved values and entropy-contribution calculations are unchanged.
- Open: ITC-068, review of the structuring evaluation-temperature options.
- 2026-10-07: P03 "<analysis> was omitted" warning removed; selected analyses a result cannot provide are skipped silently (the report builders only offer available analyses).
- 2026-10-07: P04 result identifiers print one per line instead of one wrapped line.
- 2026-10-07: F07 Report comments removed (no app could edit them). F08 Supporting evidence removed; F04 gains a "Supporting experiments" row. Codes F07 and F08 are retired.
- 2026-10-07: E04 baseline method uses "Type · detail" format; spline density is judged from the actual point count, not the stored setting.
- 2026-10-07: E04 "Integration regions" states how end points were set (Fit Peaks now recorded in the project file); "Integration mode" row removed.
- 2026-10-08: E04 applies only when a thermogram is available; E04a reports imported heats and injection use without implying processing. Supporting experiments add E04a-equivalent availability rows only without a thermogram; raw supporting experiments retain baseline/integration notes. Condensed repeats omit E04 and E04a.

- 2026-10-08 ([Null-model foundation](../Planning/DILUTION_HEAT_MODELS_PLAN.md#completed-foundation)): Null labels use saved model identity; comparison summaries name the saved Null parameters. E01/E11 and their supporting overview entries use saved Null solutions and generic prediction labels. Null figures ignore offset correction, retain processing error bars, and show an unavailable label when solution/model/injection identities do not match.
- 2026-10-08 ([model-field follow-up](../Planning/DILUTION_HEAT_MODELS_PLAN.md#completed-foundation)): Null model fields show the actual model name, with fit status and comparison scope reported separately. Standalone figure annotations retain the role-and-model label.
- 2026-10-09: P02 Bookkeeping conventions notice removed (ITC-037). Each result experiment shows its saved method as the last E04/E04a row; condensed repeats (E03) do not. Mixed methods within one result add R03a and a P03 line. P02 code is retired; later appendix codes are unchanged.
- 2026-10-09 (ITC-040): box rule added. I03 becomes rows. E01a Raw processing unavailable, S01 unavailable notices and S04 Attached fit unsuccessful removed; S04 code retired. E04b Fit warnings added to affected experiment chapters.
- 2026-10-09 (ITC-052): Uncertainty rows (summary model/fit details and E08) read "<method>; not estimated (no binding detected)" when estimation was skipped for a no-binding fit.

- 2026-10-12 (ITC-094): Required before/after review covers A10/E08 uncertainty, A04 no-binding explanations, F01/R01 badges, R03 reasons, and P03 warnings. Changed: a cancelled member beside a skipped/completed member retains Cancelled instead of a generic partial outcome; recorded failed/cancelled profiles with absent runs remain failed/cancelled instead of ordinary Not run. A10/E08 consume these saved shared outcomes. Unchanged: report layout, all-skipped no-binding wording, A04 omission/explanation policy, validity badges/reasons and P03 warning selection, and A13/E06 “n complete replicates” correlation counts. Map discrepancy: profile status is presented through generic A10/E08 uncertainty rows, not a dedicated profile-summary section. Inspector requested counts do not replace report evidence counts.
- 2026-10-13 (PA-22): Reproduced H02 footer overlap in both desktop renderers. Shared measured layout keeps the title centered, fits it between software/version and page numbering, and truncates only at Unicode text-element boundaries. Both renderers preserve their footer height, font and rule. The full document title, cover content and pagination are unchanged; a title is omitted if even an ellipsis cannot fit. Generic A10/E08 profile rows remain the documented presentation of saved outcomes; no incorrect outcome was identified in this investigation.

- 2026-10-18 (CR-025/CR-027): Both interpretation dialogs use cumulative Results, Injection data, Processing details and Thermograms evidence levels. Capability refresh preserves dialog intent; Thermograms needs a fresh opening opt-in. Full local canonical evidence is distinct from the selected transmitted evidence. Printed report content is unchanged; interpretation freshness continues to use full canonical evidence. No-binding observation restrictions survive trace removal.
