# Analysis report map

Current report content (preview and PDF, macOS and Avalonia), top to bottom. Baseline 2026-10-07.

Codes are discussion references, not printed; keep them stable. **Std** = Standard output,
**Diag** = Diagnostic output, **Trace** = Traceability Mode, **no-binding** = assessed "No binding
detected" in Std. `[Plot]`, `[Info]`, `[Warn]`, `[Error]` = figure or notice box.

## Overview

Page order: F · I · per result (R · A · X · E per experiment) · S · P. Every chapter starts a new page.

### F. Front page

| Code | Content | When |
| --- | --- | --- |
| F01 | Title + status badge (worst result) | always |
| F02 | Subtitle | label set |
| F03 | Sign-off: Prepared by · Generated at · Report ID · signature · date | Trace |
| F04 | Report scope: results · distinct result experiments | always |
| F05 | Included results: Result · Model · Analysis date · Experiments · Status · Binding assessment | always |
| F06 | Contents: Interpretation · Result n · Supporting experiments · Appendix | always |
| F07 | Report comments | comments set |
| F08 | Supporting evidence: supporting count · distinct experiments in report | supporting experiments |

### I. Interpretation

| Code | Content | When |
| --- | --- | --- |
| I01 | `[Warn]` Out-of-date interpretation / freshness unknown | generated text not current |
| I02 | Interpretation text | saved text |
| I03 | `[Info]` Source and editing history | saved text |

### R. Result overview (per result)

| Code | Content | When |
| --- | --- | --- |
| R01 | "Result n. Name" + status badge | always |
| R02 | Analysis: date · operator · model · binding assessment · experiments | always |
| R03 | `[Info/Warn/Error]` status + reasons | not Valid, or reasons recorded |
| R04 | Comments | result comments |
| R05 | `[Plot]` Experiment overview: small final fit per experiment (1A, 1B, …) | always |

### A. Analysis summary (per result)

| Code | Content | When |
| --- | --- | --- |
| A01 | Binding assessment and null comparison: conclusion · null model/fit/offsets/RMSD · AICc · ΔAICc · reason | Diag |
| A02 | Pooled Comparison Diagnostics: ΔAICc · null fit | Diag, independent collection |
| A03 | Binding assessment: conclusion · binding model · null model · null fit · ΔAICc | Std, assessed, not independent |
| A04 | `[Info]` No binding detected: omitted experiments | Std, any no-binding |
| A05 | `[Plot]` Thermodynamic summary: ΔH · −TΔS · ΔG bars per experiment, 95% CI | finite values |
| A06 | Experiment parameter overview: Experiment · (T · Ions · ΔHprot) · parameters · RMSD · AICc/AIC | always |
| A07 | Buffer subtraction used in fit: label → reference; method | fit-time subtraction |
| A08 | Combined parameters: evaluation T · ΔCp · ΔH · −TΔS · ΔG · Kd | >1 experiment, binding shown |
| A09 | `[Info]` Summary uncertainty | expanded, CI style, approximate intervals |
| A10 | Model and fit details: model · fit mode · options · constraints · RMSD · uncertainty · AIC/AICc · solver | always |
| A11 | Fixed parameters | locked parameters |
| A12 | `[Info]` Reading fit diagnostics | expanded, weighted fit |
| A13 | Shared parameter correlation matrix | selected, bootstrap, binding shown |
| A14 | `[Info]` Reading parameter correlations | expanded, A13 |

### X. Advanced analyses (per result, each selected in inspector, each a separate chapter)

| Code | Content | When |
| --- | --- | --- |
| X01 | Temperature dependence: `[Plot]` ΔH/−TΔS/ΔG vs T + Parameters at T (+ A09) | saved temperature dependence |
| X02 | Spolar Record: saved result, no plot | saved |
| X03 | Affinity versus salt: `[Plot]` Kd vs salt | electrostatics, salt attribute |
| X04 | Debye-Huckel dependence: `[Plot]` log10 Kd vs √I + saved result | saved fit |
| X05 | Counter-ion release: `[Plot]` ln Kd vs ln activity + saved result | saved fit |
| X06 | Protonation dependence: `[Plot]` ΔHobs vs ΔHprot + saved result | saved fit |

### E. Experiment chapter (per experiment, inclusion should optional in inspector)

| Code | Content | When |
| --- | --- | --- |
| E00 | "1A. Experiment name" | always |
| E01 | `[Plot]` Baseline and integration windows + `[Plot]` Final fit (heats, fit, band, residuals) | raw data |
| E01a | `[Info]` Raw processing unavailable + `[Plot]` Final fit | no raw data |
| E02 | Experiment details: date · file · IDs · T · concentrations · injections · settings · attributes · tandem | first appearance |
| E03 | Experiment details — condensed: previously reported as · file · IDs · instrument · T · concentrations · attributes | repeat, condense on |
| E04 | Processing and integration: baseline · injection use · integration regions | not condensed |
| E05 | Fitted and derived parameters: Parameter · Type · Value · Unit | always |
| E06 | Parameter correlation matrix (this experiment) | selected, >1 experiment, binding shown |
| E07 | `[Info]` Reading parameter correlations | expanded, E06 |
| E08 | Fit details: RMSD · c-value · uncertainty · assessment + ΔAICc | always |
| E09 | Comments | experiment comments |
| E10 | Injection table: # · Use · Vol · [M] · [L] · Ratio · Heat · Heat SD · Fit · Residual | tables on, binding shown |
| E11 | Null comparison table: Injection · Use · Ratio · Amount · Observed · Offset prediction · Residual | tables on, no-binding |

### S. Supporting experiments (one chapter)

| Code | Content | When |
| --- | --- | --- |
| S00 | "S1. Experiment name" | per supporting experiment |
| S01 | `[Plot]` baseline/integration + `[Plot]` integrated heats, no fit; `[Info]` for missing stages | always |
| S02 | Experiment details (as E02) | always |
| S03 | Notes: baseline · integration · corrected heats · used as reference by | any apply |
| S04 | `[Warn]` Attached fit unsuccessful | attached fit failed |
| S05 | Comments | experiment comments |
| S06 | Injection table without Fit/Residual | tables on |

### P. Appendix

| Code | Content | When |
| --- | --- | --- |
| P01 | Experiment sources: Reported as · Experiment · File · T · Cell · Syringe · Inj. · Role | always |
| P02 | `[Info]` Bookkeeping conventions: method → labels | effectively always |
| P03 | `[Warn]` Report warnings | any warning |
| P04 | Report details: software · version · report/result IDs | always |

### H. Page furniture and validation

| Code | Content | When |
| --- | --- | --- |
| H01 | Header: FT-ITC Analysis · result · 1A. experiment; timestamp right | every page |
| H02 | Footer: software/version · title · Page n of N | every page |
| H03 | " – continued" on split block headings | page breaks |
| V01 | Error list instead of a report | invalid selection |

## Rules that affect many blocks

- Std hides binding output for no-binding; manual overrides count; Inconclusive and Not assessed count as binding.
- No-binding experiment: Offset fit in E01/R05, E11 replaces E10, left out of A05, no c-value, no E06.
- Result with hidden binding: "Attempted" model in F05/R02, assessment row in R02, no A08, A13, or X.
- Independent collection: each experiment assessed separately; combined output only if all show binding.
- Diag: attempted binding fit everywhere, figures tagged "Binding-fit diagnostics", A01/A02 replace A03/A04.
- Trace: F03, analysis operator, P04 IDs; missing IDs and dates print "Not recorded".
- Fitted values come from the saved fit; experiment details, processing, plotted heats, and injection tables are current data.
- Uncertainty style (default SD + 95% CI) formats values; fixed values have none; A05 and X03–X06 error bars are always 95% CI.
- RMSD is always µJ, molar RMSD kJ/mol, whatever the energy unit.
- Defaults: injection tables on, condense repeats on, expanded explanations off, advanced analyses off.

## Details

- F01 badge: ANALYSIS VALID / ANALYSES VALID · REVIEW WARNINGS · PARTIAL / STALE · INVALID / STALE.
- F05: "(manual)" after overridden assessments; Status Valid · Warnings · Partial / stale · Invalid / stale.
- I02: recognises "## "/"### " headings, "- " bullets, tables; manual text without "## " gets "Overall interpretation".
- I03: manual = author + saved time; generated = provider · model · reasoning · guidance · times · request ID.
- R03 titles: Valid · Valid with warnings · Partially invalid or stale · Invalid or stale.
- A03: null fit = offset per experiment + RMSD; ΔAICc e.g. "+42.1 (binding 120.3, null 162.4)" or reason.
- A06: T/Ions/ΔHprot columns only when that dependence is enabled; assessment under name; fixed "(fixed)"; non-finite "—".
- A07 is fit-time subtraction; E02 "Buffer subtraction" is the current setting.
- A08: evaluated at Reference temperature for temperature models, otherwise mean experiment temperature.
- A10 uncertainty e.g. "Bootstrap residuals; completed; 98 of 100 refits succeeded"; AIC/AICc not for independent collections.
- A13: single experiment = its matrix; global fit = joint matrix labelled "Global ·" / "Experiment ·"; replicate count + warnings.
- E01: missing Offset fit → "Offset fit unavailable" tag, no residual panel.
- E02: date tagged (data file / user provided); file-system date only in Trace; settings and IDs only when recorded.
- E02 attributes: subtraction "<ref> (Experiment S1; method)"; competitor Kd, ΔH + source result, or why not captured.
- E03 drops Injections, Experiment settings, and E04; everything else in the chapter stays.
- E04: baseline status, integrated count, integration mode shown only when not normal.
- E05 Type: Fitted · Fixed · Derived (ΔG, −TΔS, ΔS, ΔCp, linked Kd).
- E08: c-value Wiseman (site) / step n (sequential) / apparent (competitive); uncertainty only for individual unconstrained fits; assessment only for independent collections.
- E10: Ratio column is "[L] axis (µM)" or "Injection" for other x axes; heats per mole.
- E11: title "Saved null comparison injection evidence", or "Injection table — Current experiment data" without predictions; heats per injection.
- X: a selection only another result has is skipped silently; one no result has adds "<name> was omitted: <reason>" to P03.
- X02–X06 saved result "Uncertainty" reads "Repeated random sampling of saved input uncertainties."
- S01: pair when raw and heats exist, single plot when one does; boxes for missing raw, heats, or both.
- S03: "Used as subtraction reference by" lists result experiments whose saved fit used it.
- P01 Role: "Buffer reference for 1A" · "Tandem source for 1B".
- P03 lines: status not valid · parameter at boundary · bootstrap at boundary · limit-terminated refits · omitted analyses.
- V01: no result; duplicate or missing result/supporting experiment; no model or experiments; missing or non-finite parameters where binding is shown.

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
