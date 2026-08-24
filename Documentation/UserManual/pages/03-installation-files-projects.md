---
title: Installation, files, and projects
summary: Install FT-ITC Analysis, open supported data, save portable projects, and use autosave or recovery safely.
slug: installation-files-projects
nav_order: 3
last_verified: 2026-08-24
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Installation, files, and projects

## Install the application

Use the [latest FT-ITC Analysis release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest) for current packages and release notes. Verify that the file came from the project release channel before accepting an operating-system security prompt. The examples below describe package types available at this manual's verification date and may change between releases. Installation problems can be reported through the [GitHub issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues).

### macOS

Open the DMG, drag **FT-ITC Analysis** to **Applications**, and eject the disk image. If macOS blocks a verified download, review it in **System Settings > Privacy & Security** and use **Open Anyway** only after confirming its source.

### Windows

Run the supplied Windows x64 `.exe` installer and follow the setup prompts. The installer registers `.ftxtc` project associations; legacy `.ftitc` projects remain openable from **File > Open...**. If Windows displays an **Unknown publisher** or Microsoft Defender SmartScreen warning, continue only after confirming that the installer came from the project repository.

### Linux

Install the supplied `.deb` package matching the system architecture (AMD64 or ARM64, when available) on a compatible Debian-based distribution. Package trust and dependency behavior depend on the channel through which the package was obtained. Review the current release notes for known platform limitations.

> **Platform note:** Package installation and file-association prompts are controlled by the operating system. Once the application is running, the analysis workflow and labels in this manual are shared.

## Supported input formats

| Extension | Source | Contains thermogram? |
| --- | --- | --- |
| `.itc` | MicroCal-style raw data | Yes |
| `.nitc` | TA Instruments NanoITC native data | Yes |
| `.ta` | TA Instruments/NanoAnalyze export | Usually |
| `.apj` | PEAQ-ITC project export | As supported by import |
| `.opj` | Legacy Origin ITC project | In some projects |
| `.dat` | Integrated heats | No |
| `.aff` | Integrated heats | No |
| `.dh` | Integrated heats | No |
| `.ftxtc` | Current FT-ITC project | Stored project state |
| `.ftitc` | Legacy FT-ITC project | Stored legacy state |

PEAQ project imports are converted into experiment information understood by FT-ITC Analysis. Content that is specific to the source application is not automatically equivalent to a native FT-ITC project.

Native NanoITC `.nitc` imports restore the raw thermogram, injection schedule, concentrations, cell volume, temperature and stirring information, and available source provenance. They open as unprocessed Experiment Data and follow the normal thermogram-processing workflow.

Legacy Origin `.opj` imports select the first compatible ITC worksheet in project order and restore its injection metadata. When the worksheet contains the original time/power trace, FT-ITC Analysis restores the thermogram; opening **Process Data** initializes processing and recalculates the heats from that trace. The worksheet heat values are therefore not retained as the processed result. If no trace is present, the worksheet heat values are used as integrated input and **Process Data** is skipped. ResultsLog text is retained in the experiment comments as provenance. Origin baseline processing, fitted models, and Fit/DY columns are not imported or converted into native FT-ITC fits. Newer `.opju` files are not supported.

## Open files

Choose **File > Open...**, use the welcome-screen action, or drag files into the application. Multiple raw or integrated files can be opened together.

When you open an FT-ITC project while other data is loaded, choose whether to replace the current document or append the project's contents. Replace is appropriate when the opened project should become the complete working document. Append is useful for bringing experiments or results into an existing comparison project.

Legacy `.ftitc` projects can be opened like other supported files. They are read as legacy project data, while subsequent project saves use the current `.ftxtc` format.

> **Caution:** Appending can create similarly named experiments or results. Confirm the data list and details before fitting or exporting.

## Save projects

Choose **File > Save** to update a named current project, or **File > Save As...** to choose a new name or location. Use the current `.ftxtc` format for ongoing work.

An `.ftxtc` project preserves the data and metadata needed to continue analysis, including thermograms where imported, concentrations and uncertainties, attributes and comments, injection inclusion, processing state, fit solutions, Analysis Results, and completed derived analyses. The package is portable and does not depend on the original raw-file path for ordinary reopening.

**Save Selected...** writes selected project content when you need a smaller handoff. Confirm the selection before saving and reopen the result if the subset is critical. Saving the selected experiments saves only the experiment with any solution. Saving the selected Analysis Results saves the result along with the involved experiments.

> **Recommendation:** Save a processed version of the project before fitting if you want a reusable starting point. After fitting, save the project again—under a new name if you want to preserve the processed-only version—to retain the fitted solutions and Analysis Results.

## Autosave and recovery

Autosave behavior is configured in **Preferences...**. When recovery data is available after an interrupted session, the application offers a recovery path. Open the recovered document, inspect the data list and recent changes, then save it under a deliberate `.ftxtc` name.

Recovery mode is designed to salvage valid project components when possible. A recovered project can be detached from its former save location and marked as changed. Use **Save As...** rather than assuming the damaged or interrupted file was repaired in place.

> **Caution:** Recovery cannot guarantee that every optional result or cached component survived. Confirm experiment counts, processing, fits, and result validity before continuing.

## Remove and clear content

Removing an experiment or Analysis Result changes only the open document; it does not delete the original raw file. **Remove All Data/Results** clears the current document after confirmation. **Clear Processing/Results** removes all Analysis Results from the open document.

Saving after removal makes the removal part of the saved project. Use **Save As...** first if you want to preserve the original project version.

## Privacy and online checks

Analysis and the surrounding workflow—including saving, recovery, export, and printing—run locally. The application has no online analysis features. If **Check for updates and online resources on launch** is enabled, it only checks two repository files for version and citation updates; it does not upload experiment data. Disable the setting when launch-time network access is undesirable. A failed or disabled check does not prevent local processing, fitting, or saving.

## Update safely

Before installing a new application version, save important projects and retain the current installer when reproducibility policy requires it. After updating, open a copy of a representative project, confirm processing and results, and save only when you intend the project to be written by the new version.
