---
title: Figures, printing, and export
summary: Configure final and supporting figures, print active graphs, and export data, peaks, results, and publication-ready PDFs.
slug: figures-printing-export
nav_order: 9
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Figures, printing, and export

FT-ITC Analysis separates visual figures from numerical exports. Decide whether the downstream need is a publication graphic, an injection table, processed traces, or a fitted-parameter table before choosing an export command.

## Build a Final Figure

Select an experiment or a compatible Analysis Result and open **Final Figure**. The view builds a publication-oriented figure from the current experiment, processing, and solution.

Depending on the available data and controls, a final figure can include:

- raw thermogram and baseline;
- integration-region markers or fills;
- integrated heats and excluded points;
- injection uncertainty bars;
- fitted model curve and confidence band;
- residual panel;
- parameter or experiment annotation;
- axis, line, symbol, font, and page controls.

Choose only elements that communicate the analysis. A diagnostic view can show baseline and excluded points; a final publication figure may use a cleaner subset while the methods describe processing.

## Check residuals and confidence bands

Enable residuals to expose systematic disagreement that the main curve can hide. Residuals should be interpreted with their scale and weighting.

A confidence band is available when the solution contains suitable uncertainty information. It represents fitted uncertainty under the selected model and resampling workflow, not the full range of possible models or systematic experimental errors.

> **Interpretation:** A narrow band around a systematically wrong curve is not evidence of accuracy. Review residual structure and model assumptions together.

## Coordinate axes across figures

Use shared or unified axis options when comparing experiments on a common visual scale. Check that all points and confidence regions remain visible. Independent axes maximize use of space but can exaggerate or conceal between-panel differences.

Set axis ranges deliberately for final output. Avoid clipping error bars, fit curves, or annotations. Use consistent energy, concentration, and temperature units across a figure set.

## Export a final figure as PDF

Use the PDF export action in **Final Figure** or **Export Associated Final Figures...** for a selected Analysis Result. Choose a clear filename that identifies the result and analysis version.

Reopen the exported PDF and check:

- page size and orientation;
- text, symbols, subscripts, and units;
- line weights and color contrast;
- full axis ranges and uncropped annotations;
- residual and confidence-band visibility at publication size.

PDF preserves vector output where the renderer supports it and is the preferred handoff for layout and print workflows.

## Print the active graph

Choose **File > Print** while the intended graph or figure is active. The application prepares the current printable view and passes it to the operating-system print workflow.

> **Platform note:** macOS and Windows use their native print dialogs. Linux uses the available CUPS printers and can offer PDF saving through the application print dialog. Printer options and naming therefore differ, but the source graph is the same.

Use print preview where available and check scaling before submitting. For reproducible archival output, export a PDF and retain it alongside the project.

## Export raw or processed data

Choose **File > Export Data...** for data traces in the selected export format. Use **Export Selected Data...** when the operation should be limited to the current selection. Review the export dialog because available columns depend on the source and processing state.

Exported text data are useful for downstream plotting or review, but they do not carry the complete project state. Preserve the `.ftxtc` project as the reproducible source.

## Export integrated peaks

Choose **File > Export Integrated Peaks...** for injection-level integrated heat information. Verify inclusion flags, raw versus corrected values, buffer subtraction, and uncertainty columns in the resulting file.

Use this export to audit processing or interoperate with another analysis workflow. When publishing a table, state whether heats were buffer-corrected and which injections were excluded.

## Export result tables

Open **Analysis Result Exporter...**. Select one or more results and configure:

- summary rows or individual fitted/member rows;
- inline or separate value and uncertainty columns;
- standard deviations, 95% confidence bounds, or both;
- CSV or TSV output.

Copy the configured table to the clipboard for a quick transfer, or save it to preserve exact delimiters and encoding. Open the saved file in the destination tool and check that decimal values and delimiters were interpreted correctly.

## Copy to the clipboard

Use **Copy Result Table** or a tool-specific copy command for tabular text. Clipboard output is convenient but easier to alter accidentally than a saved export. Keep a saved table for reported results.

When copying figures, confirm the destination retained sufficient resolution and transparency. Prefer PDF export when the destination accepts it.

## Create a Supporting Figure

Choose **Supporting Figure...** to arrange several experiment or result figures in a configurable grid.

1. Select the source figures.
2. Choose rows and columns.
3. Place the figures in the intended reading order.
4. Use shared visual options and axis alignment where appropriate.
5. Confirm that the grid has capacity for every selected figure.
6. Export the canvas as PDF and inspect it at final size.

The supporting-figure tool aligns multi-panel output; it does not change the underlying processing or fit. If a source result becomes invalid, rebuild the source result and export the composition again.

## A publication handoff checklist

Before handing off files:

- save the `.ftxtc` project;
- confirm result validity;
- export the exact numerical result table;
- export final and supporting figures as PDF;
- retain descriptions of processing, constraints, uncertainty, and exclusions;
- cite the application version and software DOI;
- reopen every exported file.

