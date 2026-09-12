---
title: FT-ITC Analysis user manual
summary: Start here for a tour of FT-ITC Analysis and the conventions used throughout this manual.
slug: index
nav_order: 1
last_verified: 2026-08-28
_verification:
  product_version: "1.5.0"
  commit: "d3e153a0a10a67e3382efe39d368bb259ea8ccbd"
---

# FT-ITC Analysis user manual

FT-ITC Analysis is a desktop application for processing, fitting, comparing, and presenting isothermal titration calorimetry (ITC) experiments.

This manual is for ITC practitioners who are new to FT-ITC Analysis. It explains how to use the application and provides guidance on scientific interpretation; no programming knowledge is required.

These instructions apply on macOS, Windows, and Linux. The manual treats the supported desktop applications as one product. A **Platform note** appears only when an operating-system or interface difference changes how you complete a task.

> **Verified:** This edition reflects FT-ITC Analysis 1.5.0 and was verified on the date shown for each page.

## What the application does

FT-ITC Analysis supports the complete workflow from compatible instrument data to a saved analysis project and publication figures:

1. Import a raw thermogram, integrated heats, or an existing project.
2. Review and edit experiment details, concentrations, comments, and attributes.
3. Process thermograms using baseline models, spline editing, and manual or automatic adjustment of integration regions.
4. Fit one or more experiments with a supported model.
5. Review fitted parameters, uncertainties, residuals, and result validity.
6. Save portable projects in the `.ftxtc` format.
7. Export figures, numerical data, and result tables.

Raw input files are read, not rewritten. Desktop analysis is local: experiment data are not uploaded during ordinary analysis. An optional launch-time online check retrieves version and citation information. Optional AI interpretation sends the selected report evidence and supplied context to app.ft-itc.org (MIST), then OpenAI; usage metadata are retained without automatic expiry. See [Privacy and online checks](03-installation-files-projects.md#privacy-and-online-checks) and [Analysis Report](09-figures-printing-export.md#analysis-report).

## Product tour

The data list contains loaded experiments and completed Analysis Results. Selecting an experiment exposes four shared task views:

- **Overview** summarizes the experiment and provides access to its details.
- **Process Data** builds and refines baselines, adjusts injection regions, and converts a raw thermogram into integrated heats with estimated uncertainties.
- **Analyze Data** fits a single experiment or multiple experiments.
- **Final Figure** presents the thermogram, heats, fitted curve, residuals, and annotations.

Selecting an Analysis Result opens its result workspace, with a parameter summary, member fits, parameter correlation, uncertainty display, and any compatible advanced analyses. The menus provide project operations, experiment management, export commands, preferences, additional tools, citation information, and support links. The [FT-ITC Analysis website](https://ft-itc.org), [latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest), [source repository](https://github.com/FrederikTheisen/FT-ITC-Analysis), and [software DOI](https://doi.org/10.5281/zenodo.14832177) provide project, installation, and citation context.

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

**Citation** presents the current paper and versioned software citations and supports copying or exporting BibTeX. **Contact Support...** helps you contact support by email and prepares a diagnostic report containing the application version, operating system, recent activity, and full application log. **Copy Support Report** places that report on the clipboard. Official links include the [FT-ITC Analysis website](https://ft-itc.org), [latest release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest), [project viewer](https://app.ft-itc.org), [source repository](https://github.com/FrederikTheisen/FT-ITC-Analysis), [issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues), and software [DOI 10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177).
