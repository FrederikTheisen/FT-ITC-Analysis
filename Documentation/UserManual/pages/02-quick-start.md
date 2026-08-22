---
title: Quick start
summary: Open your own ITC dataset, process it, fit a one-set-of-sites model, review the result, save the project, and export a figure.
slug: quick-start
nav_order: 2
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Quick start

This procedure takes a compatible file through an ordinary one-set-of-sites analysis. Use a dataset for which that model is scientifically plausible. The goal is to learn the application workflow, not to prescribe the correct model for every interaction.

![Six-step FT-ITC Analysis workflow from opening data through saving and export.](../assets/workflow.svg)

*The analysis workflow. Integrated-heat imports enter after thermogram processing.*

## Before you begin

Have one of these inputs available:

- a raw MicroCal-style thermogram (`.itc` or `.vpitc`);
- a TA Instruments/NanoAnalyze export (`.ta`) or PEAQ-ITC export (`.apj`);
- injection-level integrated heats (`.dat`, `.aff`, or `.dh`);
- an FT-ITC project (`.ftxtc`) or legacy project (`.ftitc`).

> **Note:** Integrated-heat files do not contain a raw thermogram. They skip baseline correction and peak integration; begin with experiment details and fitting.

## 1. Open your data

Launch FT-ITC Analysis and choose **Open File...** on the welcome screen, choose **File > Open...**, or drag compatible files into the application window. Select your file and confirm the open operation.

The experiment appears in the data list. Select it and open **Overview**. Confirm that the instrument, temperature, date, concentrations, injection count, and other available metadata agree with your experiment record.

## 2. Check experiment details

Open **Details...** for the selected experiment. Check at least:

- cell and syringe concentrations;
- concentration uncertainties, if you intend to propagate them;
- temperature;
- comments and attributes relevant to later analysis.

Apply corrections only when you have an independent experimental basis. Concentration entries influence the calculated concentration ratio and fitted parameters.

> **Caution:** A fit can converge with incorrect concentrations. Convergence is not evidence that the experiment details are correct.

## 3. Process a raw thermogram

If the import contains a thermogram, open **Process Data**.

1. Start with **Spline** baseline, **Balanced** point density, and the default integration settings.
2. Inspect whether baseline points represent the signal between injections rather than the peaks.
3. Inspect the start and end of every integration region. A region should include the injection response without extending unnecessarily into baseline noise.
4. Adjust the global start or length controls when most injections need the same change. Drag an individual region marker when one injection is different.
5. Reprocess and inspect the integrated heats and uncertainty bars.

Use **Polynomial** when a single smooth global drift describes the baseline, or **Segmented** when local baseline behavior is more appropriate. These alternatives are explained in [Processing thermograms](processing-thermograms.md).

> **Interpretation:** A visually smooth baseline is not sufficient by itself. Check whether the integrated heat trend and residual baseline are physically plausible and whether the result is stable under reasonable processing choices.

## 4. Fit one set of sites

Open **Analyze Data**, choose **Single experiment**, and select **One-Set-Of-Sites**.

1. Review the initial parameter values. Use physically plausible orders of magnitude for affinity, enthalpy, and stoichiometry.
2. Keep standard parameter limits unless the system requires a justified alternative.
3. Choose an optimizer. **Levenberg-Marquardt** is efficient near a suitable solution; **Nelder-Mead** can be useful when the starting surface is less cooperative.
4. Optionally enable **Weight by injection error** when the integration uncertainties are meaningful for the dataset.
5. Choose **None**, **Bootstrap residuals**, or **Leave-one-out** for error estimation.
6. Choose **Run Fit**.

If the fit fails or reaches a limit, revisit the details, processing, initial values, and model choice before expanding limits. See [Fitting models](fitting-models.md) and [Preferences and troubleshooting](preferences-troubleshooting.md).

## 5. Review the result

Inspect the fitted curve together with the heats, uncertainty bars, and residuals. Then select the resulting **Analysis Result** and check:

- the included experiment and injections;
- fitted parameter values and units;
- convergence and fit loss;
- whether weighting and uncertainty estimation match your intention;
- parameter uncertainty or confidence intervals, when calculated;
- result validity.

> **Interpretation:** Parameter precision does not establish model adequacy. Look for structured residuals, sensitivity to initial values, values at limits, excessive parameter correlation, or disagreement with known stoichiometry and experimental conditions.

## 6. Save a portable project

Choose **File > Save As...** and save the project as `.ftxtc`. The project contains imported data, experiment details, processing state, fits, results, and available advanced-analysis output. Keep the original instrument files as source records even though the project is portable.

If autosave is enabled, it supplements normal saving; it is not a replacement for a named project file.

## 7. Export a figure

Return to the experiment and open **Final Figure**. Choose the elements you need, such as the thermogram, integrated heats, fitted curve, residuals, confidence band, or parameter information. Review axis ranges and labels, then use the figure export or print command provided by the view.

Use **Analysis Result Exporter...** when you need numerical result tables rather than a graphic. Use **File > Export Integrated Peaks...** for injection-level heats.

## What you have created

You now have a saved, reproducible analysis project and a figure based on its current processing and fit. Continue with:

- [Workspace and experiment management](workspace-experiments.md) for multi-file projects;
- [Multiple-experiment analysis](multiple-experiments.md) for global fits;
- [Figures, printing, and export](figures-printing-export.md) for publication output.
