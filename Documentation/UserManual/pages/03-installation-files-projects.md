---
title: Installation, files, and projects
summary: Install FT-ITC Analysis, open supported data, save portable projects, and use autosave or recovery safely.
slug: installation-files-projects
nav_order: 3
last_verified: 2026-08-28
_verification:
  product_version: "1.5.0"
  commit: "d3e153a0a10a67e3382efe39d368bb259ea8ccbd"
---

# Installation, files, and projects

## Install the application

Use the [latest FT-ITC Analysis release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest) for current packages and release notes. Verify that the file came from the project release channel before accepting an operating-system security prompt. The examples below describe package types available at this manual's verification date and may change between releases. Installation problems can be reported through the [GitHub issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues).

### macOS

Open the DMG, drag **FT-ITC Analysis** to **Applications**, and eject the disk image. If macOS blocks a verified download, review it in **System Settings > Privacy & Security** and use **Open Anyway** only after confirming its source.

### Windows

Run the supplied Windows x64 `.exe` installer and follow the setup prompts. The installer registers `.ftxtc` project associations. If Windows displays an **Unknown publisher** or Microsoft Defender SmartScreen warning, continue only after confirming that the installer came from the project repository.

### Linux

Install the supplied `.deb` package matching the system architecture (AMD64 or ARM64, when available) on a compatible Debian-based distribution. Package trust and dependency behavior depend on the channel through which the package was obtained. Review the current release notes for known platform limitations.

> **Platform note:** Package installation and file-association prompts are controlled by the operating system. The analysis workflow is otherwise shared; the manual notes the few interface labels that differ between desktop editions.

## Supported input formats

| Extension | Source | Imported content |
| --- | --- | --- |
| `.itc` | MicroCal-style raw data | Raw thermogram and injections |
| `.nitc` | TA Instruments NanoITC native data | Raw thermogram and injections |
| `.ta` | TA Instruments/NanoAnalyze export | Raw thermogram and injections |
| `.apj` | PEAQ-ITC project | Raw thermogram and injections from the first experiment |
| `.opj` | Origin project file | Raw thermogram or integrated heats, according to the recognized worksheet |
| `.dat` | Integrated-heats table | Integrated heats and injections |
| `.aff` | Integrated-heats table | Integrated heats and injections |
| `.dh` | Fixed-layout integrated-heats file | Integrated heats, injections, and experiment metadata |
| `.ftxtc` | Current FT-ITC project | Stored project state |

NanoAnalyze `.ta` files are raw thermogram exports. FT-ITC Analysis restores their time/power data and injection information as unprocessed Experiment Data.

PEAQ-ITC `.apj` projects contain a raw thermogram as well as injection and analysis information. FT-ITC Analysis imports the raw thermogram and injection information from the first experiment in the project. Integrated heats, processing choices, and fitted results produced by PEAQ are not imported; process and fit the restored raw data in FT-ITC Analysis.

Native NanoITC `.nitc` imports restore the raw thermogram, injection schedule, concentrations, cell volume, temperature and stirring information, and available source provenance. They open as unprocessed Experiment Data and follow the normal thermogram-processing workflow.

Origin `.opj` files are general project containers. FT-ITC Analysis searches them for the first recognized ITC worksheet and can restore either its original time/power trace or its integrated heats. When a raw trace is available, it is imported as an unprocessed thermogram and is authoritative even if the worksheet also contains integrated heats. If no usable trace is present, the worksheet heat values are used as integrated input and **Process Data** is skipped. ResultsLog text is retained in the experiment comments as provenance. Origin baseline processing, fitted models, and Fit/DY columns are not imported or converted into native FT-ITC fits. Newer `.opju` files are not supported.

Delimited `.dat` and `.aff` inputs must provide positive `INJV` injection volumes and at least one usable heat column. FT-ITC Analysis prefers a complete `DH` column as absolute injection heat. If `DH` is absent or incomplete, it accepts a complete `NDH` column as normalized heat per mole; `NDH` may be absent for the automatically excluded first injection. The separate `.dh` format uses a fixed metadata-and-injection layout rather than the delimited `.dat`/`.aff` column contract.

These files do not encode an unambiguous heat unit. For `DH`, select the absolute-energy unit used by that column. For an `NDH`-based import, select the energy numerator used by the per-mole values (for example, select **calorie** for cal/mol). The reader converts normalized heat to absolute injection heat using the syringe concentration and injection volume. If the syringe concentration cannot be inferred from `Xt`/`Mt`, it must be supplied before an NDH-based import can continue; canceling that prompt skips only the current file. Reuse the selected unit for the remaining files only when every file in that import operation uses the same heat unit and heat-column convention.

When available, the reader infers cell volume and syringe concentration from the energy-independent `Mt`/`Xt` concentration trajectory using the selected dilution model. Each injection row stores the concentrations before that injection; an optional state-only final row stores the concentrations after the last injection. `Mt` and `Xt` are interpreted as mM in normal application imports. If the trajectory is absent, malformed, or internally inconsistent, `DH` heat and injection-volume rows remain importable; an `NDH`-based import first requires a syringe concentration, and validation asks for any other unresolved metadata instead of silently guessing it. These formats contain no thermogram, so importing them cannot reconstruct a baseline or processing-derived injection uncertainties.

## Open files

Choose **File > Open...**, use the welcome-screen action, or drag files into the application. Multiple supported files can be opened together. Files opened into a populated document are added to its existing Data / Results list; this includes current `.ftxtc` projects. Clear the current document first when a current project should be opened by itself.

> **Caution:** After a current `.ftxtc` project is added to an existing document, that opened project becomes the document's current save destination. Use **Save As...** before saving if you do not intend to replace it with the combined document.

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
