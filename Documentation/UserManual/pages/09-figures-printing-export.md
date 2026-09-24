---
title: Figures and export
summary: Configure final and supporting figures, print active graphs, and export data, results, figures, and complete analysis reports.
slug: figures-printing-export
nav_order: 9
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# Figures and export

FT-ITC Analysis keeps publication figures, numerical data, and fitted result tables as separate output types. **Final Figure** is an Experiment Data workflow. An Analysis Result does not open directly as a Final Figure; **Export Associated Final Figures...** on a result exports figures for the experiment solutions associated with that result.

## Final Figure

The **Final Figure** workspace renders the selected Experiment Data as a publication figure. Its preview and PDF output reflect the selected experiment, processing state, fitted solution, and figure options. To export figures using the fits stored in an Analysis Result, use **Export Associated Final Figures...** on that result.

The **Export PDF** controls offer three scopes:

- **Selected** opens a save dialog for the displayed experiment figure and suggests the experiment name as the PDF filename.
- **Active** exports figures for Active Experiment Data. **All** exports figures for all Experiment Data in the project.

For **Active** and **All**, choose a parent folder and the app creates or reuses a subfolder named after the saved project file (without its extension). For an unsaved session, it uses the local export date (`yyyyMMdd`) as the folder name. Existing PDFs are kept; when a filename is already in use, the new figure gets a numeric suffix such as `Experiment (2).pdf`.

The result-list command **Export Associated Final Figures...** loads the result’s member solutions into their experiments and writes one final-figure PDF per associated experiment. It is available for a result with exportable solutions. See [Results and advanced analyses](08-results-advanced-analysis.md) for result validity and stored member-solution behavior.

> **Where Final Figure settings are used**
>
> - **Final Figure PDFs**, including Active, All, and **Export Associated Final Figures...**, use the current Final Figure appearance controls.
> - **Supporting Figure...** starts with a snapshot of those controls when its window opens. This includes units, axis limits, visible traces and fits, and excluded-point automatic Y scaling. Its own plot size, grid, typography, panel labels, grouping, and information-box controls take precedence; later Final Figure edits do not update an already open Supporting Figure.
> - **Analysis Report** uses a separate compact figure preset and its own energy and uncertainty selections. Final Figure axis limits and excluded-point automatic Y scaling do not carry over to report figures.

### General

The **General** tab defines the page and common content. Page controls specify width and height in centimeters and the base font size. The **Energy units** selector provides **Automatic**, **J**, **kJ**, **cal**, and **kcal**. **Automatic** chooses a common molar-energy unit based on the figure's plotted and reported central values. A fixed choice uses the selected energy unit throughout the figure, except for the thermogram units described below. The selected family also controls thermogram units: differential power is **µW** or **µcal/s**, while integrated heat is **µJ** or **µcal**. Time controls set the displayed time unit. Content controls include the data graph, axis titles, experiment details, model information, fit parameters, and the information-box placement. The uncertainty selector provides **Automatic**, **SD**, **CI**, **SD + CI**, and **None**. **Automatic** uses the same per-quantity asymmetry rule described under [Uncertainty and evaluation temperature](08-results-advanced-analysis.md#uncertainty-and-evaluation-temperature).

The parameter controls determine which information appears in the information box: thermodynamic, derived, or offset parameters; temperature; concentrations; injection delay; instrument; and user-defined attributes. The information box is descriptive figure content and does not alter the underlying fit.

![Final Figure workspace showing a publication preview, Automatic energy selection, page dimensions, information content, uncertainty, and PDF output scopes.](../assets/final-figure-workspace.png)

### Data Graph

The **Data Graph** tab controls the differential-power trace. Power and time axis titles, tick density, and explicit minimum and maximum values define the axes. **Corrected data** selects the baseline-corrected trace when one is available. **Shared power axis** applies one power-axis range across the active experiments represented by a figure set.

Baseline controls expose the baseline, its **Solid** or **Dashed** style, **Under data** or **Over data** layer, and line width. **Integration ranges** displays the integration intervals as **Bar**, **Fill**, or **Endpoint lines**. These overlays describe processing and do not recalculate integration.

### Fit Graph

The **Fit Graph** tab controls the integrated-heats and model panels. Enthalpy and molar-ratio axis titles, tick density, and explicit ranges define the axes. Symbols are **Square** or **Circle**, with an independent point size. **Shared X axis** and **Shared enthalpy axis** unify the corresponding ranges across active experiments; the residual-axis range follows the shared enthalpy setting when configured for unified residual axes.

**Include excluded points in automatic Y scaling** lets excluded injections affect the automatic integrated-heat and residual axis ranges. It starts from the application preference. Explicit axis limits take precedence.

**Fit line** displays the fitted binding curve with a selected width and **Smooth**, **Spline**, or **Linear** smoothness. **Show residuals graph** adds the residual panel, and **Residual gap** separates it visually from the fit panel. The display controls include the zero enthalpy line, confidence band, error bars, excluded points, excluded error bars, and offset-corrected heats. Error bars use processed injection uncertainties; confidence bands require bootstrap uncertainty in the fitted solution; fit lines and residuals require a fitted solution.

![Data Graph and Fit Graph inspectors showing axis, corrected-data, baseline, symbol, shared-axis, fit-line, and PDF output controls.](../assets/final-figure-graph-controls.png)

## Numerical data export

**File > Export Data...** opens the numerical export dialog. **Export Selected Data...** invokes the same export for the selected experiment. The data scope is one of **Selected experiment**, **Active experiments**, or **All experiments**.

The format list contains:

- **Thermogram Data** — a `.csv` file containing time and power samples, with the **Export baseline-corrected trace** option when baseline-corrected samples exist.
- **Integrated Peaks** — a `.csv` file containing injection-axis values, molar heats, their SD, fitted values, and residuals when available, with **Export offset-corrected peaks** when a fitted solution is available.
- **Combined Data** — a `.csv` file placing thermogram samples and integrated-peak columns side by side; raw, corrected, fitted, and residual columns are included when available.
- **MicroCal / SEDPHAT** — a `.dat` file with MicroCal-style `DH`, `INJV`, `Xt`, `Mt`, `XMt`, `NDH`, `DY`, and `Fit` columns. `DH` is in µcal, `INJV` is in µL, concentrations are in mM, and `NDH`, `DY`, and `Fit` are in cal/mol. The first injection uses `--` for normalized heat and residual, following the conventional excluded-first-injection layout.
- **pytc** — a `.dh` file containing the pytc-compatible injection and metadata fields.
- **ITCsim** — a `.csv` file containing ITCsim-compatible injection data and metadata, with offset-corrected peaks available when a fitted solution exists.

**Molar-energy unit** is available for Integrated Peaks, Combined Data, and ITCsim: choose **J/mol**, **kJ/mol**, **cal/mol**, or **kcal/mol**. Each export dialog starts at kJ/mol for the Joules energy preference or kcal/mol for the Calories preference; a choice applies to that export only. Integrated Peaks and Combined Data identify the chosen unit in the heat, SD, model, and residual column headers. ITCsim keeps its `PeakHeat` column name and records the unit as `#EXPINFO ENERGYUNIT J`, `KJ`, `CAL`, or `KCAL`; those tokens denote energy per mole. Peak area is an absolute heat in J and is distinct from the exported molar heat.

Other output units remain format-specific. Thermogram samples use seconds and watts; MicroCal/SEDPHAT and pytc use their documented concentration, volume, temperature, and heat conventions. Fitted columns and correction controls are disabled when the selected data do not contain the corresponding processed or fitted state. Export defaults are described in [Settings and defaults](11-preferences-troubleshooting.md).

These exports are not project backups. General `.csv` and `.tsv` exports cannot be reopened through **File > Open...**. A MicroCal / SEDPHAT `.dat` export or pytc `.dh` export can be reopened as integrated-heat input, but doing so restores neither a thermogram nor the FT-ITC project state and requires choosing the source heat unit. Select **microcalorie** for the `DH` column when reopening a newly generated MicroCal / SEDPHAT export. Historical FT-ITC `.dat` exports may instead require **joule**, matching the unit used when they were created. Save an `.ftxtc` project when the analysis must remain editable.

## Analysis Result Exporter

**Analysis Result Exporter...** builds a table from one or more selected Analysis Results. **Summary rows** produces one row per result; **All replicate rows** produces one row per member fit. Error layout is **Value with error** or **Separate columns**. Uncertainty style is **SD**, **CI**, or **SD + CI**. The file format is **CSV** or **TSV**. **Energy units** provides **Automatic**, **J**, **kJ**, **cal**, and **kcal**. Automatic chooses one stable molar-energy unit across all selected results and one independent unit for ΔCp columns; a fixed choice uses the selected energy unit consistently. Temperature presentation is **Celsius** or **Kelvin**.

The configured table is available through **Copy** and **Export...**. Copy places the same delimited text on the clipboard; Export writes it to a file. Profile-likelihood SD columns use the equivalent display scale implied by their stored endpoints and are labeled as such in exported metadata; the exact lower/upper endpoints remain available in separate-column mode. The exporter does not include the parameter-correlation matrix, which is calculated for display rather than stored as a result-table field.

Summary rows use the same Parameter Evaluation calculation at its default evaluation temperature: the configured reference temperature for a temperature series, otherwise the mean experiment temperature. The exported temperature column labels that evaluation temperature. Local summary rows report **Combined SD** and an **Approximate propagated interval**, using the same calculation described under [Parameter Evaluation](08-results-advanced-analysis.md). Summary interval columns have neutral `_interval_lower` and `_interval_upper` suffixes; individual rows retain their CI95 columns. Kd is derived from the summarized Gibbs energy rather than averaged directly, and ΔCp columns are included for applicable temperature-dependent results. No results are pooled across separate analyses.

Result tables, clipboard output, final-figure metadata, and viewer output create
thermodynamic columns according to the fitted model. A sequential result therefore
exports one Kd, ΔH, ΔG, and −TΔS value for every active step, including steps 3
and 4 when present. The fixed step
count is included as model information. Exporting preserves the interpretation of these values: macroscopic sequential
constants remain step constants, not microscopic site constants.

Each affinity column chooses its concentration unit independently from its own
magnitude. For example, Kd1 may be shown in nM while Kd4 is shown in µM. The
column header and its values always use the same unit; this is display scaling
only and does not change fitted or saved values.

![Analysis Result Exporter showing selected results, summary-row mode, uncertainty layout and style, CSV format, temperature units, Automatic energy selection, Copy, and Export.](../assets/analysis-result-exporter.png)

## Analysis Report

**Tools > Analysis Report...** creates a complete, printable report from one or more saved Analysis Results. The same tool is available as **Export Analysis Report...** in result-specific menus, where that result is initially selected. Use **Select report contents...** to choose results and optional supporting experiments together. The picker follows application order; applying the same ordered selection again restores its saved study context and approved interpretation. At least one result is required.

### Report contents and layout

The report is a multipage A4 portrait PDF with margins suitable for printing. A report containing multiple results begins with a combined result index and concise table of contents on the first page, followed by the report-level interpretation and one self-contained chapter for each result. Results are referenced as **1**, **2**, and so on, and their experiments as **1A**, **1B**, **2A**, **2B**, and so on. These labels are derived from the saved result and experiment order, so figures, captions, and interpretation evidence use the same stable references. No experiments or scientific estimates are merged across results. Each chapter overview arranges the experiments in compact, borderless figure panels labeled with their references and names. The report automatically chooses three to five columns, centers incomplete rows, and suppresses repeated axis titles.

The analysis summary contains a compact grouped thermodynamic bar chart for every saved member and active binding step. Its whiskers show only the saved 95% confidence interval from the solution distribution or profile-likelihood calculation; the symmetric SD approximation remains available in report tables but is not superimposed on this plot. Each experiment section places a wider, baseline-focused view of the uncorrected thermogram—with its saved baseline and compact horizontal markers for the integration windows—beside a top-aligned Final Figure at its standard size containing the complete thermogram, followed closely by fitted and derived parameters. **Fit details** also reports dimensionless c-values where the fitted model defines them. The one- and two-site Wiseman values use `c = N[cell]₀/Kd`; syringe-correction fits use the fixed site count rather than the fitted syringe active fraction. Sequential fits report `[cell]₀/Kd` for each active step, while competitive fits report the apparent c-value based on `Kd_app`. When **Injection tables** is selected, a table records every saved injection, its inclusion state, volume, concentrations, analysis-axis value, integrated heat and uncertainty, fitted heat, and residual. This processing presentation is an automatic report preset and does not add another figure control. A concise notice replaces the processing graph when raw thermogram samples are unavailable. Selected shared correlations follow the analysis summary; selected member correlations follow that experiment’s parameter table, using a compact native matrix rather than a dedicated page.

Directly selected experiments that are not represented by a selected result appear once in a report-level **Supporting experiments** chapter after the result chapters and are labeled **S1**, **S2**, and so on. The report uses only saved content: a raw thermogram with any saved baseline and integration boundaries, finite integrated heats explicitly labeled as having no fit, experiment metadata, notes about processing or correction exceptions, dates with a verified source, attributes, comments, and the report-wide optional injection table. Missing stages receive concise availability notices. Experiments already represented by a checked result remain visible but unavailable as supporting selections. Recorded buffer-subtraction references may be selected automatically according to the application setting, but remain ordinary optional selections; the report does not judge whether a reference is an appropriate blank. Report creation never fits, reintegrates, or performs subtraction.

### Configure the report

Choose a title, an optional subtitle, energy and temperature units, and an **Uncertainties** style. The subtitle appears as secondary, regular-weight text below the report title. Reports default to **SD + 95% CI**. The selected style controls uncertainty in tables and applicable analysis plots. The thermodynamic summary bar chart always shows available saved 95% confidence intervals, as described above; it does not add SD whiskers. This selection is remembered only for the current application session. Final Figure injection errors and fitted confidence bands retain their established scientific meanings.

The subtitle editor accepts longer descriptions. It wraps text and scrolls vertically so you can review it before previewing the report.

**Optional content** includes report-wide **Injection tables** and **Condense repeated experiments** switches and the completed analysis types available in any selected result. Each option applies to every eligible result chapter; chapters without that saved content simply omit it. The injection-table switch is on by default and applies consistently to every experiment. When the same experiment occurs in a later result, condensed mode retains both figures, comments, source file, date with a verified source, instrument, temperature, concentrations, and all result-specific fitted content, but replaces repeated secondary acquisition and processing details with a reference to its first occurrence. The **Parameter correlations** option includes the shared matrix and all available member matrices alongside their corresponding summaries. Available analyses are selected by default; **Select all** and **Clear** change that analysis list together. Temperature dependence is included once per eligible result with its saved fit and parameter summary, while saved electrostatics, protonation, and Spolar outputs are presented without recomputation. Report creation never reruns fitting or advanced analysis.

### Write and preview an interpretation

The builder opens in **Interpretation** mode with an editor for your remarks. Text wraps automatically. Interpretation Markdown supports `##` section headings, `###` subsection headings, bullets, `**bold**`, and `*italic*` emphasis. Use the **Interpretation / Preview** control above the workspace to move between writing and the rendered report. Selecting **Preview** automatically builds the PDF when it has not yet been built or is out of date; a current preview is reused. **Update Preview** forces a rebuild. Editing while a preview exists marks it as out of date without repeatedly rendering while you type. **Export PDF...** rebuilds stale content and saves the completed PDF. Results with warnings, outdated inputs, or partially invalid status remain exportable and carry a prominent notice. A structurally unusable result shows a validation error and cannot be previewed or exported.

### Generate an automated interpretation

**Online generation privacy:** Generate sends selected names, comments, conditions, fits and injection evidence, plus your question or context, to the FT-ITC online interpretation service, which uses OpenAI to generate a response. Thermograms start unchecked each time the generator opens and require an explicit opt-in. The service retains usage metadata without automatic expiry; full scientific evidence and generated prose are not included in that metadata. Provider and infrastructure retention are separate; no deletion deadline is guaranteed. Canceling does not retract sent data. Local processing, fitting, saving and export work without generation. [Data flow and retention](03-installation-files-projects.md#privacy-and-online-checks).

The inspector’s **Interpretation** section reports whether the text is manual, automatically generated, user edited, or out of date. **Edit** returns focus to the large editor. **Generate interpretation…** opens a dialog with optional **Main question** and **Additional context** fields. Use the question to focus the assessment and the context to explain the experiment. The cross-platform dialog labels its preset selector **Interpretation depth**. Requesting generation sends the selected report evidence and these inputs to the online interpretation service. Compressed thermograms are unchecked each time the generator opens; if **Include compressed thermograms** is available for your access, you can opt in for that request.

Generation assesses all selected results and supporting experiments together, using the same references as the PDF. Results remain independent, including alternative fits of the same experiment. Supporting experiments contribute available observations and metadata without being assigned a fitted result or assumed control role. Generation uses existing processing, integration, residual, and uncertainty evidence where available; it does not rerun integration, baseline fitting, or model fitting. When compressed thermograms are included, they contain minimum and maximum power values for each 15-second interval, plus an aligned fitted baseline when available. Exact peak timing, the order of values within each interval, and the detailed waveform are unavailable. The dialog shows the compact package size against your current request limit and blocks generation when the estimated request is too large. An opted-in thermogram is retained in the request; a size or model-context error is shown instead of silently omitting it.

When generation succeeds, the desktop app sends an **Interpretation ready** system notification. Whether it appears depends on operating-system notification support and permissions. The draft remains available in the generation dialog for review.

For fitted result members, the interpretation evidence includes the same model-specific c-values shown in **Fit details**. They remain attached to that result member rather than to shared experiment-source evidence. Supporting experiments have no c-value because the report does not assign them a fit.

Completed Spolar–Record, electrostatics and protonation analyses are also included for the selected results, independently of the injection-table, processing-information and thermogram choices. The package includes their saved estimates and uncertainties, available completed settings, and sampling information. Settings edited after an analysis completed do not replace its saved settings. The interpretation considers what these analyses add to the scientific conclusions without requiring a paragraph about each one. Generation does not rerun advanced analyses; missing historical method details remain unavailable.

When several result members have identical source data and processing, the request can share that evidence between them. Each result retains its own fitted values, uncertainty, validity, and constraints. Sharing source evidence does not imply independent replication or equivalent fits.

If the selection contains outdated or failed analyses, a warning gives you an opportunity to review them before continuing. The report distinguishes saved fit inputs and estimates from current observations; some comparisons are unavailable when the saved fit cannot be matched to the current data. An interpretation can identify potential concerns, but it does not certify the experiment or establish a mechanism. The service may suggest references from details such as a paper title, journal, or year, but it does not search the web.

Review and edit the returned draft, then choose **Use in report** to replace the approved interpretation. The draft focuses on observed issues and targeted follow-up checks, using references such as **Result 2** and **Experiment 1B**. It avoids routine warnings based solely on modest deviations in fitted N-values or missing blank titrations.

The interpretation service is asked to keep straightforward assessments to about 500–600 words or fewer, with roughly 1,000 words available for complex collections. These are writing targets, not acceptance limits. Longer text flows onto additional pages without automatic shortening or regeneration. Text length, headings, and Markdown formatting do not prevent you from accepting a draft.

Markdown tables require a header and separator row. They support bold or italic cell text and left, center, or right alignment. Cells wrap, and table headers repeat across pages. Use a few columns to keep tables readable. Unsupported formatting may appear as literal text in the report.

Canceling, closing the dialog, or encountering a service error leaves previously approved text unchanged. Study context, the thermogram setting, approved text, and generation details are saved with the report. Changes to selected results, supporting evidence, context, or settings can mark an approved automated interpretation as out of date. Older approved interpretations remain readable even when their freshness cannot be verified. The report distinguishes manually written interpretations from automatically generated text and subsequent edits.

The **Summary** generation task produces a compact factual report without knowledge-base retrieval. Local report editing, saved interpretations, and PDF export remain available if automated interpretation generation is unavailable.

### Save a local copy of interpretation evidence

Use **Save package** in the generation dialog to save a local ZIP of the evidence, question, context, and settings selected for that request. Saving the ZIP does not contact the interpretation service or run new fits. The archive contains scientific data and context; review it before sharing.

### Troubleshoot automated interpretation

If generation fails, note the visible error and use **Copy Support Report** when contacting support. Local report editing and PDF export remain available if online generation is unavailable.

### Choose the appropriate output tool

The Analysis Report is distinct from the other output tools:

- **Analysis Result Exporter** writes compact CSV or TSV tables for numerical reuse.
- **Final Figure** writes a publication figure for an experiment, while **Export Associated Final Figures...** writes one figure per result member.
- **Supporting Figure** composes selected figures into one multi-panel canvas.
- **Analysis Report** assembles one or more saved results as self-contained chapters with figures, detailed tables, diagnostics, optional advanced plots, and source and analysis history.

## Supporting Figure

**Supporting Figure...** composes figures from Experiment Data and Analysis Results into a multi-panel canvas. The **Figure order** list defines source order; **Add…**, **Remove**, **Up**, and **Down** change the composition. The source picker can filter available experiment and result figures by name or type.

Common plot size specifies width and height in centimeters. Grid controls specify columns and rows. Typography controls define base font size, point size, line weight, and tick style. **Panel letters** adds stable A, B, … identifiers. **Panel titles** optionally adds the experiment name after that identifier (for example, **A. Experiment name**) and is off by default for existing Supporting Figure workflows. **Group result figures** and the parameter and information box control affect result grouping and annotations. The preview zoom shows the rendered canvas at 25%, 50%, 75%, or 100%. **Export PDF...** writes the composed supporting figure as a PDF.

![Supporting Figure window showing one Analysis Result expanded into three preview panels, with preview zoom, grid, plot dimensions, typography, and PDF export.](../assets/supporting-figure.png)

## Printing

**File > Print** prints the active graph or figure through the operating system’s print workflow. The active target can be the overview thermogram, processing graph, analysis graph, result graph—including an available **Correlation** matrix—or Final Figure, depending on the selected workspace. The operating-system print dialog supplies the available printer and PDF destinations; the graph content comes from the active application view.
