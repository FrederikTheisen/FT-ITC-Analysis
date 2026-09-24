---
title: Quick start
summary: Open your own ITC dataset, process it, fit a one-set-of-sites model, review the result, save the project, and export a figure.
slug: quick-start
nav_order: 2
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# Quick start

This walkthrough takes a compatible file from measured heats to a fitted result. A **One-Set-Of-Sites** model is a starting example when the binding sites can reasonably be treated alike; choose another model if the chemistry calls for it. The steps show how to use the application, not how to decide which scientific model is true.

![FT-ITC Analysis workflow from opening data through saving and export.](../assets/workflow.svg)

*The overall analysis workflow. Integrated-heat imports enter after thermogram processing.*

## Before you begin

Have one of these inputs available:

- A raw thermogram from a MicroCal-style file (`.itc`), native TA Instruments NanoITC file (`.nitc`), NanoAnalyze export (`.ta`), or PEAQ-ITC project (`.apj`).
- An Origin project file (`.opj`). When a recognized ITC worksheet contains the original time/power trace, FT-ITC Analysis restores it for processing; otherwise, it uses the worksheet heat values as integrated input.
- Injection-level integrated heats (`.dat`, `.aff`, or `.dh`).
- An FT-ITC project (`.ftxtc`).

Supported formats and project behavior are described in [Installation, files, and projects](03-installation-files-projects.md).

> **Note:** Integrated-heat files and Origin projects without an original time/power trace skip baseline correction and peak integration; begin with experiment details and fitting.

## 1. Open your data

Launch FT-ITC Analysis and choose **Open File...** on the welcome screen, choose **File > Open...**, or drag compatible files into the application window. Select your file and choose **Open**.

The experiment appears in the data list. Select it and open **Overview** to review the imported experiment. You can drag data or result rows to change their order. This order is used throughout the application and is retained when the project is saved as `.ftxtc`.

## 2. Edit experiment details

Open **Details...** for the selected experiment. Concentration entries and the experiment date/time can be changed here when needed. Comments and attributes relevant to later analysis can also be added or edited. A changed date is marked as user-modified in the experiment overview.

> **Caution:** Apply corrections only when you have an independent experimental basis. Concentration entries influence the calculated concentration ratio and fitted parameters.

## 3. Process a raw thermogram

If the import contains a thermogram, open **Process Data**.

1. The baseline estimates the signal the instrument would show between injection peaks. If that background drifts smoothly, try **Polynomial**. For a more irregular background, try **Spline**; for changes that differ across parts of the run, try **Segmented**. Keep the default integration settings initially.
2. Inspect whether the baseline represents the signal between injections rather than the peaks.
3. Inspect the start and end of every integration region. A region should include the injection response without extending unnecessarily into baseline noise.

These baseline alternatives are explained in [Processing](05-processing-thermograms.md).

> **Recommendation:** Zoom to a single peak by double-clicking it, copy the integration region to the next peak using **Space**.

> **Interpretation:** A visually smooth baseline is generally desired. As a rough rule of thumb, aim for at least 20% of the data points to be baseline data; this is guidance, not a hard threshold. Apparent jumps in the baseline may indicate insufficient equilibration time between injections. Major spikes may require advanced baseline editing; the **Spline** baseline type provides more flexibility.

## 4. Fit a model

Open **Analyze Data**, choose **Single experiment**, and select **One-Set-Of-Sites** or a more appropriate model, depending on your system.

1. Review the initial parameter values: affinity describes how strongly the substances bind, enthalpy describes the heat change associated with binding, and stoichiometry describes the binding ratio in the chosen model. Use physically plausible starting values.
2. Choose an optimizer. **Levenberg-Marquardt** is efficient near a suitable solution; **Nelder-Mead** provides a derivative-free alternative when convergence is difficult.
3. Optionally enable **Weight by injection error** when the integration uncertainties are meaningful for the dataset.
4. Choose an uncertainty method. **None** skips parameter uncertainty; **Bootstrap residuals** refits simulated variations of the data; **Leave-one-out** checks how much the fit changes when an observation is omitted; **Profile likelihood** tests how far a parameter can move while the fit remains acceptable.
5. Choose **Run Fit**.

**Create analysis result** determines whether a usable fit also creates a separate Analysis Result. Enable it to continue through the result workspace in the next step; otherwise, the fitted solution remains attached to the Experiment Data.

> **Recommendation:** More complex models such as the two-sets-of-sites model may require considerable trial and error with starting parameters in order to obtain a good solution.

If the fit fails or reaches a limit, the possible causes are described under [Fit availability and non-convergence](06-fitting-models.md#fit-availability-and-non-convergence).

## 5. Review the result

Inspect the fitted curve together with the residuals (observed heat minus predicted heat). Random-looking small residuals are more reassuring than a repeated pattern the model misses. When **Create analysis result** is enabled, select the resulting **Analysis Result** and check:

- the included experiment and injections;
- fitted parameter values and units;
- convergence status and RMSD;
- whether weighting and uncertainty estimation match your intention;
- parameter uncertainty or confidence intervals, when calculated;
- parameter correlations.

When **Create analysis result** is disabled, the fitted solution remains available in **Analyze Data** rather than as a stored Analysis Result.

> **Interpretation:** Parameter precision does not establish model adequacy. Look for structured residuals, sensitivity to initial values, values at limits, excessive parameter correlation, or disagreement with known stoichiometry and experimental conditions.

## 6. Save a portable project

Choose **File > Save As...** and save the project as `.ftxtc`. The project contains imported data, experiment details, processing state, fits, results, and available advanced-analysis output. Keep the original instrument files as source records even though the project is portable.

If autosave is enabled, it supplements normal saving; it is not a replacement for a named project file.

## 7. Export a figure

Return to the experiment and open **Final Figure**. Choose the elements you need, such as the thermogram, integrated heats, fitted curve, residuals, confidence band, or parameter information. Review axis ranges and labels, then use the figure export or print command provided by the view.

Use **Analysis Result Exporter...** when you need numerical result tables rather than a graphic. For injection-level heats, choose **File > Export Integrated Peaks...**. The same output is available through **File > Export Data...** by choosing **Integrated Peaks**.

The figure and table workflows continue in [Figures and export](09-figures-printing-export.md).

> **Note:** An Analysis Result can export a figure using its saved fit for each member experiment. Use **Export Associated Final Figures...** on the result to make those figures without first loading the saved solutions into the Experiment Data.
