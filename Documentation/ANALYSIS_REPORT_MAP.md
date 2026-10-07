# Analysis report map

Current report content (preview and PDF, macOS and Avalonia), top to bottom. Baseline 2026-10-07.

Codes are discussion references, not printed; keep them stable. Bold rows are blocks; `·` rows are
the lines, rows, or columns printed in that block, in order. An empty *When* column means always.
**Std** = Standard output, **Diag** = Diagnostic output, **Trace** = Traceability Mode,
**no-binding** = assessed "No binding detected" in Std. `[Plot]` = figure; `[Info]`, `[Warn]`,
`[Error]` = information, warning, error box.

Page order: F · I · per result (R · A · X · E per experiment) · S · P. Every chapter starts a new page.

## F. Front page

| Code | Content | When / omitted |
| --- | --- | --- |
| **F01** | **Title and status badge** | |
| | · Title: report name, or the title set in the builder | |
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
| **F05** | **Included results** (table, one row per result) | |
| | · Result: "1. Name" | |
| | · Model: "Attempted: <model>" when binding is hidden | "Attempted:" Std only |
| | · Analysis date | |
| | · Experiments | |
| | · Status: Valid · Warnings · Partial / stale · Invalid / stale · Unknown | |
| | · Binding assessment: outcome + "(manual)" if overridden | |
| **F06** | **Contents** (with page numbers) | |
| | · Interpretation | only with saved interpretation |
| | · Result n. Name | one per result |
| | · Supporting experiments | only with supporting experiments |
| | · Appendix | |
| **F07** | **Report comments** | only when written |
| **F08** | **Supporting evidence** | only with supporting experiments |
| | · Supporting experiments | |
| | · Distinct experiments in report | |

## I. Interpretation

Whole chapter omitted when the report has no saved interpretation.

| Code | Content | When / omitted |
| --- | --- | --- |
| **I01** | **[Warn] Out-of-date interpretation / Interpretation freshness unknown** | generated text not current; never for manual text |
| | · Reason + "The approved text has been retained and should be reviewed before use." | |
| **I02** | **Interpretation text**: saved ## / ### headings, paragraphs, - bullets, tables | manual text without "## " gets "Overall interpretation" heading |
| **I03** | **[Info] Source and editing history** | |
| | · "Interpretation written by the user; saved: <time>." | manual text |
| | · User edited · Provider · Model · Reasoning · Scientific guidance · Generated · Approved · Request | generated text; missing fields "not supplied" |

## R. Result overview (per result)

| Code | Content | When / omitted |
| --- | --- | --- |
| **R01** | **Result n. Name** + status badge for this result | |
| **R02** | **Analysis** | |
| | · Analysis date | |
| | · Analysis operator: name, or Not recorded | Trace only |
| | · Model, or "Attempted binding model" | "Attempted" when Std hides binding |
| | · Binding assessment | only when Std hides binding (shown in Diag too) |
| | · Experiments | |
| **R03** | **[Info/Warn/Error] status title** (Valid with warnings · Partially invalid or stale · Invalid or stale · Validity unknown) | omitted when Valid with no recorded reasons |
| | · One line per reason | |
| **R04** | **Comments** | only when written |
| **R05** | **Experiment overview** [Plot] | |
| | · One small final-fit panel per experiment: 1A, 1B, … + name; heats, fit, residuals | |
| | · Offset fit instead of binding fit | no-binding experiments |
| | · "Offset fit unavailable" tag, no residuals | no-binding without Offset fit |
| | · "Binding-fit diagnostics" tag | Diag |

## A. Analysis summary (per result)

| Code | Content | When / omitted |
| --- | --- | --- |
| **A01** | **Binding assessment and null comparison** | Diag only |
| | · Conclusion | |
| | · Null model | not independent |
| | · Null fit (status) | not independent |
| | · Null offsets | not independent |
| | · Null RMSD | not independent |
| | · Binding AICc | not independent |
| | · Null AICc | not independent |
| | · ΔAICc | not independent |
| | · Comparison reason | not independent |
| | · <Outcome>: count, one line per outcome | independent collection only |
| **A02** | **Pooled Comparison Diagnostics** | Diag + independent collection |
| | · "Pooled across the independently fitted experiments with one common variance; diagnostic only…" | |
| | · ΔAICc, or reason unavailable | |
| | · Null fit: RMSD, or status | |
| **A03** | **Binding assessment** | Std; omitted for Not assessed and independent collections |
| | · Conclusion + "(manual)" if overridden | |
| | · Binding model | |
| | · Null model | |
| | · Null fit: offset per experiment (1A: …) + RMSD, or failure status | |
| | · ΔAICc: "+42.1 (binding 120.3, null 162.4)", or reason | |
| **A04** | **[Info] No binding detected**: "Combined binding values and dependent analyses are omitted because no binding was detected in: <names>. These members are omitted from the thermodynamic summary." | Std, any no-binding experiment |
| **A05** | **Thermodynamic summary** [Plot] | omitted when no finite ΔH/−TΔS/ΔG |
| | · Bar groups ΔH · −TΔS · ΔG (numbered per step) | |
| | · One series per experiment, legend 1A, 1B, … | no-binding omitted in Std (not in Diag) |
| | · Whiskers: saved 95% CI | none for fixed values |
| | · Note: "Bars: 95% CI is the saved interval from the <source>." | |
| **A06** | **Experiment parameter overview** (table, one row per experiment) | |
| | · Experiment: "1A. Name" + assessment on second line | assessment line omitted when Not assessed |
| | · Temperature (°C) | temperature dependence enabled |
| | · [Ions] (mM) | electrostatics enabled |
| | · ∆H,prot (kJ/mol) | protonation enabled |
| | · One column per model parameter; "(fixed)" for fixed, "—" non-finite | |
| | · RMSD (µJ) | |
| | · AICc / AIC ("AIC <value>" when no AICc) | omitted when no per-experiment criteria |
| **A07** | **Buffer subtraction used in fit** (fit-time setting) | omitted when no experiment was subtracted at fit time |
| | · <label>: <reference>; <method>, one line per subtracted experiment | "Reference experiment unavailable" if missing |
| **A08** | **Combined parameters** | omitted: single experiment, binding hidden (Std), or not evaluable |
| | · Evaluation temperature (Reference temperature, or mean experiment temperature) | |
| | · Heat capacity change (∆Cp) | temperature dependence enabled |
| | · Enthalpy (ΔH) | per step |
| | · Entropy contribution (−TΔS) | per step |
| | · Gibbs energy (ΔG) | per step |
| | · Kd (from ΔG) | per step |
| **A09** | **[Info] Summary uncertainty**: intervals from individual 95% CIs and spread; coverage not established; covariance omitted | expanded + CI style + approximate intervals |
| **A10** | **Model and fit details** | |
| | · Model | |
| | · Analysis: Experiments fitted globally / individually | |
| | · Option: <name>: <value>, one per option | routine defaults omitted |
| | · Constraint: <parameter>: <description>, one per constraint | |
| | · RMSD / Molar RMSD (µJ / kJ/mol) | |
| | · Uncertainty: method; outcome; n of m refits succeeded | omitted without convergence info |
| | · AIC / AICc | omitted for independent collections |
| | · Solver: algorithm; termination; iterations (time); weighting | omitted without convergence info |
| **A11** | **Fixed parameters** | only when parameters are locked |
| | · <Parameter> — <experiment>: value unit | |
| | · Shared <Parameter>: value unit | |
| **A12** | **[Info] Reading fit diagnostics**: weighted fit, unweighted RMSD | expanded + weighted fit |
| **A13** | **Shared parameter correlation** | selected + residual bootstrap; omitted when binding hidden (Std) |
| | · Matrix, Pearson r; labels "Global ·" / "Experiment ·" | |
| | · "Residual bootstrap (Pearson); n complete replicates." | |
| | · Reliability warnings | only when any |
| **A14** | **[Info] Reading parameter correlations** | expanded + A13 |

## X. Advanced analyses (per result, after A)

Each is a chapter, only when selected and available. All omitted when Std hides the result's binding.

| Code | Content | When / omitted |
| --- | --- | --- |
| **X01** | **Temperature dependence** | |
| | · [Plot] ΔH / −TΔS / ΔG vs temperature, per step; saved fitted lines; band in CI styles | |
| | · Parameters at <T>: same lines as A08 except Evaluation temperature | omitted when not evaluable |
| | · [Info] Summary uncertainty (as A09) | as A09 |
| **X02** | **Spolar Record** (no plot) | |
| | · Saved result heading | |
| | · Folded mode | |
| | · Temperature mode | |
| | · Iso-entropic / Mean / Reference temperature | |
| | · Hydration contribution | |
| | · Conformational contribution | |
| | · Residue estimate | |
| | · Uncertainty: "Repeated random sampling of saved input uncertainties." | |
| | · Completed | |
| **X03** | **Affinity versus salt** | |
| | · [Plot] Kd vs salt concentration (mM), saved 95% CI, no fitted line | |
| **X04** | **Debye-Huckel dependence** | |
| | · [Plot] log10(Kd / M) vs sqrt(Ionic strength / M); points + fitted curve | |
| | · Saved result heading | |
| | · Kd at zero ionic strength | |
| | · Salt sensitivity | |
| | · Curvature | only when used |
| | · Counter-ion release | only when calculated |
| | · Uncertainty | |
| | · Completed | |
| **X05** | **Counter-ion release** | |
| | · [Plot] ln(Kd / M) vs ln(Salt activity); points + fitted line | |
| | · Saved result: same lines as X04 | |
| **X06** | **Protonation dependence** | |
| | · [Plot] Observed enthalpy vs Buffer protonation enthalpy; points + fitted line | |
| | · Saved result heading | |
| | · Binding enthalpy | |
| | · Protonation change | |
| | · Uncertainty | |
| | · Completed | |

## E. Experiment chapter (per experiment)

| Code | Content | When / omitted |
| --- | --- | --- |
| **E00** | **1A. Experiment name** | |
| **E01** | **Experiment figures** | only with raw thermogram (else E01a) |
| | · [Plot] Baseline and integration windows: raw thermogram, baseline, windows | |
| | · [Plot] Final fit: corrected thermogram, heats + error bars, fit line, band, residuals | |
| | · Offset fit instead of binding fit | no-binding |
| | · "Offset fit unavailable" tag, no residuals | no-binding without Offset fit |
| | · "Binding-fit diagnostics" tag | Diag |
| **E01a** | **No raw thermogram** | only without raw thermogram |
| | · [Info] Raw processing unavailable | |
| | · [Plot] Final fit (as E01) | |
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
| | ·· Competitor properties: Kd = …; ΔH = … (from result "…"), or reason not captured | competitor attribute |
| | · Tandem merge origin | merged tandem only |
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
| | · (Injections and the rest of Experiment settings omitted; E04 omitted) | |
| **E04** | **Processing and integration** | omitted when condensed (E03) |
| | · Baseline method: Spline, … / Polynomial, nth degree / Segmented, nth degree | |
| | · Baseline status: Incomplete | only when incomplete |
| | · Integrated injections: n of m | only when not all integrated |
| | · Integration mode | only when not time-based |
| | · Injection use: n included; excluded: … | |
| | · Integration regions (subheading) | |
| | ·· Start after injection (s) | |
| | ·· End after injection (s) | |
| **E05** | **Fitted and derived parameters** (table) | |
| | · Columns: Parameter · Type (Fitted / Fixed / Derived) · Value · Unit | |
| | · One row per reported parameter, incl. Offset; fixed values without uncertainty; "—" non-finite | |
| **E06** | **Parameter correlation** (this experiment) | selected + >1 experiment + bootstrap; omitted for no-binding |
| | · Matrix, Pearson r | |
| | · "Residual bootstrap (Pearson); n complete replicates." | |
| | · Reliability warnings | only when any |
| **E07** | **[Info] Reading parameter correlations** | expanded + E06 |
| **E08** | **Fit details** | |
| | · RMSD / Molar RMSD | |
| | · Wiseman c-value / (site 1), (site 2) / c-value (step n) / Apparent c-value | by model; omitted for no-binding |
| | · c-value concentration basis: Initial tandem segment | tandem only |
| | · Uncertainty | omitted for global or constrained fits |
| | · Binding assessment | independent collection, assessed |
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
| | · Observed heat (µJ) | |
| | · Offset prediction (µJ) | blank without saved points or successful null fit |
| | · Residual (µJ) | blank without saved points or successful null fit |

## S. Supporting experiments (one chapter)

Whole chapter omitted when there are no supporting experiments. No fit is shown, even if attached.

| Code | Content | When / omitted |
| --- | --- | --- |
| **S00** | **S1. Experiment name** | one per supporting experiment |
| **S01** | **Figures** | |
| | · Current experiment data — observations: [Plot] Baseline and integration windows + [Plot] Integrated heats — no fit | raw + heats |
| | · [Plot] Current experiment data — baseline and integration windows | raw only |
| | · [Plot] Integrated heats — Current experiment data; no fit | heats only |
| | · [Info] Experimental plots unavailable | neither |
| | · [Info] Raw thermogram unavailable | no raw |
| | · [Info] Integrated heats unavailable | no finite heats |
| **S02** | **Experiment details**: same lines as E02 | |
| **S03** | **Notes** | omitted when no line applies |
| | · Baseline: Incomplete | raw + incomplete baseline |
| | · Integration: n of m injections integrated | not all integrated |
| | · Integrated heats: Stored corrected heats; configured reference <name> (<method>) | buffer-subtracted |
| | · Used as subtraction reference by: 1A, … | used by a saved fit |
| **S04** | **[Warn] Attached fit unsuccessful**: reason + "Fitted parameters are not reported." | attached fit failed |
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
| **P02** | **[Info] Bookkeeping conventions** | effectively always |
| | · <method>: <labels>, one line per distinct method | no fit-time record: "Unknown concentration method; <heat> heat" |
| **P03** | **[Warn] Report warnings** | omitted when no warnings |
| | · <Result>: The saved result is reported with status: <status>. | status not Valid |
| | · <Result>: <Experiment> has a fitted parameter at a boundary. | |
| | · <Result>: <Experiment> has bootstrap estimates at a parameter boundary. | |
| | · <Result>: <Experiment> has limit-terminated uncertainty refits. | |
| | · <Result>: <Analysis> was omitted: <reason> | selected analysis no result has |
| **P04** | **Report details** | |
| | · Software | |
| | · Application version | |
| | · Report identifier | Trace only |
| | · Result identifiers | Trace only |

## H. Page furniture and validation

| Code | Content | When / omitted |
| --- | --- | --- |
| **H01** | **Header** | every page |
| | · FT-ITC ANALYSIS REPORT | front page |
| | · FT-ITC Analysis · <result> · 1A. <experiment> | other pages; parts that apply |
| | · Generation timestamp (right) | |
| **H02** | **Footer** | every page |
| | · Software and version (left) | |
| | · Report title (centre) | omitted on front page |
| | · Page n of N (right) | |
| **H03** | **" – continued"** after a block heading split across pages | |
| **V01** | **Error list instead of a report** | no result; duplicate or missing selection; no model or experiments; missing/non-finite parameters where binding is shown |

## Rules that affect many blocks

- Std hides binding output for no-binding; manual overrides count; Inconclusive and Not assessed count as binding.
- Independent collection: each experiment assessed separately; combined output only if all show binding.
- Fitted values come from the saved fit; experiment details, processing, plotted heats, and injection tables are current data.
- Uncertainty style (default SD + 95% CI) formats values; fixed values have none; A05 and X03–X06 error bars are always 95% CI.
- RMSD is always µJ, molar RMSD kJ/mol, whatever the energy unit.
- Defaults: injection tables on, condense repeats on, expanded explanations off, advanced analyses off.

## Known issues

- A05: Diag includes no-binding experiments (ITC-039).
- A06, E05: no-binding experiments show attempted binding values in Std (ITC-051).
- E04: shown for integrated-heats-only imports (ITC-036).
- E11: current-data fallback still headed "Saved ratio".
- X: omission warnings are dropped when binding output is hidden.
- P02: placement and weight (ITC-037).
- P03: includes no-binding experiments (ITC-053).

## Sources

`AnalysisITC.Core/Presentation/`: `AnalysisReportBuilder.cs` (blocks, conditions), `ResultOutputPolicy.cs` (binding output),
`AnalysisResultOverviewTable.cs` (A06), `AnalysisResultParameterEvaluation.cs` (A08, X01), `ExperimentOverviewTable.cs` (E10, S06),
`PublicationFigure*.cs` (figures), `AnalysisReport*Layout.cs` (header, sign-off, layout, A05).
Renderers: `CoreGraphicsAnalysisReportRenderer.cs` (macOS), `SkiaAnalysisReportRenderer.cs` (Avalonia).

## Revision notes

- 2026-10-07: X moved from after E to directly after A; each X chapter still starts a new page.
- Open: expand F06 Contents (e.g. list X chapters and experiments).
- Open: X01 "Parameters at T" repeats A08.
