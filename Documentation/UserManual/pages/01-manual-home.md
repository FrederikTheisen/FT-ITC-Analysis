---
title: FT-ITC Analysis user manual
summary: Start here for a tour of FT-ITC Analysis and the conventions used throughout this manual.
slug: index
nav_order: 1
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# FT-ITC Analysis user manual

In isothermal titration calorimetry (ITC), an instrument measures heat as one solution is added to another. FT-ITC Analysis turns those measurements into heat values for individual injections, fits a proposed explanation of the interaction, and helps you inspect and present the result.

This manual is for ITC practitioners who are new to FT-ITC Analysis. It explains how to use the application and provides guidance on scientific interpretation; no programming knowledge is required.

These instructions apply on macOS, Windows, and Linux. The manual treats the supported desktop applications as one product. A **Platform note** appears only when an operating-system or interface difference changes how you complete a task.

> **Verified:** This edition reflects FT-ITC Analysis 1.5.0 and was verified on the date shown for each page.

## What the application does

The workflow has two main questions: **How much heat did each injection produce?** and **What interaction could explain those heats?** Processing answers the first question from a raw power trace (a **thermogram**). Fitting compares the injection heats with the predictions of a chosen model. A close fit is useful evidence, but it does not prove that the model describes the chemistry.

Start with [Quick start](02-quick-start.md): import data, check experiment details, process any thermogram, fit a model, review the result, then save and export. Use the task chapters below for setting choices, calculations, and interpretation limits.

Raw input files are not modified. Ordinary desktop analysis runs locally. Optional online interpretation and the browser-based Project Viewer send data only through their respective workflows; review [Privacy and online checks](03-installation-files-projects.md#privacy-and-online-checks) before using them.

## Product tour

The data list contains loaded experiments and completed Analysis Results. Selecting an experiment exposes four shared task views:

- **Overview** summarizes the experiment and provides access to its details.
- **Process Data** builds and refines baselines, adjusts injection regions, and converts a raw thermogram into integrated heats with estimated uncertainties.
- **Analyze Data** fits a single experiment or multiple experiments.
- **Final Figure** presents the thermogram, heats, fitted curve, residuals, and annotations.

Selecting an Analysis Result opens its saved fits, parameter summary, uncertainty, correlations, and supported advanced analyses. See [Workspace](04-workspace-experiments.md) for navigation and [Results and advanced analyses](08-results-advanced-analysis.md) for interpretation.

## Manual conventions

The shortest route to a first result is [Quick start](02-quick-start.md), which uses your own compatible data. The relevant task chapter provides setting details and diagnostic guidance when a workflow needs closer inspection.

## Manual directory

- [Quick start](02-quick-start.md)
- [Installation, files, and projects](03-installation-files-projects.md)
- [Workspace](04-workspace-experiments.md)
- [Processing](05-processing-thermograms.md)
- [Single-experiment fitting](06-fitting-models.md)
- [Multiple-experiment fitting](07-multiple-experiments.md)
- [Results and advanced analyses](08-results-advanced-analysis.md)
- [Figures and export](09-figures-printing-export.md)
- [Tools](10-additional-tools.md)
- [Settings and defaults](11-preferences-troubleshooting.md)
- [Glossary and equations](12-reference.md)

The manual uses these callouts:

> **Note:** A detail that affects how the application behaves.

> **Caution:** A condition that can produce a failed task, invalid result, or misleading output.

> **Interpretation:** Scientific context for judging output; it is not an automated conclusion made by the application.

> **Recommendation:** A defensible working practice rather than a software requirement.

> **Platform note:** A genuine operating-system or interface difference that changes how a task is completed.

> **Calculation:** A compact relationship used by FT-ITC Analysis or needed to interpret its output.

Interface labels appear in **bold**. A path such as **File > Save As...** means choose the menu and then the command. The names of views and controls are used instead of position-dependent phrases, so the instructions remain usable when the window size or platform changes.

## Help and support

**Help and Guide** opens the topic-based in-app help, with practical guidance followed by scientific explanations and calculations. On native macOS, choose **FT-ITC Analysis Help** from the **Help** menu. **Citation** presents the current paper and versioned software citations and supports copying or exporting BibTeX. **Contact Support...** helps you contact support by email and prepares a diagnostic report containing the application version, operating system, recent activity, and full application log. **Copy Support Report** places that report on the clipboard. Official links include the [FT-ITC Analysis website](https://ft-itc.org), [latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest), [project viewer](https://app.ft-itc.org), and software [DOI 10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177).
