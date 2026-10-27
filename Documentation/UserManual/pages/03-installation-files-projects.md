---
title: Installation, files, and projects
summary: Install FT-ITC Analysis, open supported data, save portable projects, and use autosave or recovery safely.
slug: installation-files-projects
nav_order: 3
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# Installation, files, and projects

## Install the application

Use the [latest FT-ITC Analysis release](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest) for current packages and release notes. Verify that the file came from this official release page before accepting an operating-system security prompt. Package options may change between releases. For installation help, contact `support@ft-itc.org`.

### macOS

Open the DMG, drag **FT-ITC Analysis** to **Applications**, and eject the disk image. If macOS blocks a verified download, review it in **System Settings > Privacy & Security** and use **Open Anyway** only after confirming its source.

### Windows

Run the supplied Windows x64 `.exe` installer and follow the setup prompts. The installer registers `.ftxtc` project associations. If Windows displays an **Unknown publisher** or Microsoft Defender SmartScreen warning, continue only after confirming that the installer came from the official FT-ITC release page.

### Linux

Install the supplied `.deb` package matching the system architecture (AMD64 or ARM64, when available) on a compatible Debian-based distribution. Package trust and dependency behavior depend on the channel through which the package was obtained. Review the current release notes for known platform limitations.

> **Platform note:** Package installation and file-association prompts are controlled by the operating system. The analysis workflow is otherwise shared; the manual notes the few interface labels that differ between desktop editions.

## Supported input formats

Choose the input that matches the stage of your work. A **raw thermogram** contains the instrument's power trace and still needs processing. An **integrated-heats** file already contains a heat value for each injection, so it goes directly to fitting. A **project** can reopen earlier FT-ITC work.

| Extension | Source | Imported content |
| --- | --- | --- |
| `.itc` | MicroCal-style raw data | Raw thermogram and injections |
| `.nitc` | TA Instruments NanoITC native data | Raw thermogram and injections |
| `.ta` | TA Instruments/NanoAnalyze export | Raw thermogram and injections |
| `.apj` | PEAQ-ITC project | Raw thermogram and injections from each experiment |
| `.opj` | Origin project file | Raw thermogram or integrated heats, according to the recognized worksheet |
| `.dat` | Integrated-heats table | Integrated heats and injections |
| `.aff` | Integrated-heats table | Integrated heats and injections |
| `.dh` | Fixed-layout integrated-heats file | Integrated heats, injections, and experiment metadata |
| `.ftxtc` | Current FT-ITC project | Stored project state |

Raw thermogram imports (`.itc`, `.nitc`, `.ta`, and `.apj`) open as unprocessed Experiment Data. Process and fit them in FT-ITC Analysis. A PEAQ-ITC `.apj` import adds every experiment in the file; its existing processing and fits are not imported.

For an Origin `.opj` file, FT-ITC Analysis uses the first recognized ITC worksheet. It imports the raw thermogram when available, or integrated heats otherwise. Origin processing and fits are not imported. The newer `.opju` format is unsupported.

If a MicroCal `.itc` file contains concatenated runs, an import prompt offers the standard **Use MicroCal Concat** calculation or **Use Back-Mixing Compensation**. Use the latter when you have the required mixing information. The imported experiment's **Tandem merge origin** records the number of runs and the calculation applied. See [Experiment Merger](10-additional-tools.md#experiment-merger) for details.

Integrated-heat files (`.dat`, `.aff`, and `.dh`) skip thermogram processing. During import, select the heat unit used by the file and supply any requested concentration information. For `.dat` and `.aff`, **Recalculate concentrations and ratios** replaces the file's concentration progression with one calculated from the injections. These two formats do not record the experiment temperature, so the user is prompted to enter it. It can be corrected later in the experiment details. Choose **Use selected action for remaining files** only if those files use the same heat units, temperature, and import choices; leave it unchecked when importing a temperature series. These formats contain no thermogram, so they cannot restore a baseline or processing-derived injection uncertainties.

The first injection is imported but excluded from fitting by default because its heat is often unreliable. You can include it manually; its volume still contributes to later concentration calculations.

A zero cell concentration is retained, for example for Dissociation into buffer; plots then use titrant concentration instead of molar ratio. Review import warnings about concentrations, cell volume, or gaps of 10 s or more in the thermogram, which can affect baseline fitting and integration.

## Open files

Choose **File > Open...**, use the welcome-screen action, or drag files into the application. Multiple supported files can be opened together. Files opened into a populated document are added to its existing Data / Results list; this includes `.ftxtc` projects. Clear the current document first if you want to open a project by itself.

If a project file contains experiments, Analysis Results, reports, or saved fits already loaded, choose **Skip Duplicates** to keep the existing objects and add new content, including new results that use existing experiments. Choose **Import Copies** to add separate copies while keeping their references connected to the copied content. **Skip Duplicates** is the default.

Opening one complete `.ftxtc` project into an empty document makes that file the destination for **Save**. Adding files to an existing document keeps its current save destination. If the document has not been named, or you open multiple projects together into an empty document, **Save** asks for a name and location.

> **Caution:** Appending can create similarly named experiments or results. Confirm the data list and details before fitting or exporting.

### Traceability Mode at startup

With Traceability Mode enabled, confirm the operator at launch before recovery or requested files are opened. **Continue** saves the name. **Quit** or closing the confirmation exits without changing it or resolving recovery files.

## Save projects

Choose **File > Save** to update the current named project, or **File > Save As...** to choose a new name or location. Use the current `.ftxtc` format for ongoing work.

Save after fitting or processing finishes. Save commands requested while busy are declined; choose the command again afterward. Autosave waits for the operation to finish.

If a save finishes but newer changes remain unsaved, the document stays marked as changed. When this happens while closing, quitting, clearing, or replacing a project, the app keeps the current document open; save again to include those changes.

An `.ftxtc` project preserves the data and metadata needed to continue analysis, including thermograms where imported, concentrations and uncertainties, attributes and comments, injection inclusion, processing state, fit solutions, Analysis Results, and completed derived analyses. The package is portable and does not depend on the original raw-file path for ordinary reopening. Optional external experiment, cell sample/batch, and syringe sample/batch IDs are saved as experiment metadata; each result also retains the operator recorded when it was created or successfully updated.

**Save Selected...** writes selected project content when you need to share a subset of the project. Confirm the selection before saving and reopen the result if the subset is critical. Selecting Experiment Data saves each selected experiment and its attached solution, if any. If the experiment uses Buffer Subtraction, the reference's heats are stored with it so the correction can be reproduced without the reference experiment. Selecting an Analysis Result saves the result, its member experiments, and the buffer reference experiments they use. It also saves any Analysis Results selected in their **Competitor properties** attributes, with those results' experiments, so the competitor source stays available when the file is opened on its own.

> **Recommendation:** Save a processed version of the project before fitting if you want a reusable starting point. After fitting, save the project again—under a new name if you want to preserve the processed-only version—to retain the fitted solutions and Analysis Results.

## Autosave and recovery

Autosave behavior is configured in **Preferences...**. An autosave due during fitting or processing waits and runs when the operation finishes. When recovery data is available after an interrupted session, the application offers a recovery path. Open the recovered document, inspect the data list and recent changes, then save it as a named `.ftxtc` project.

Recovery can restore unaffected experiments, fits, and results. If an experiment cannot be recovered, its fit and dependent results are omitted rather than reconstructed from a different set of members. Review the recovery warning and log, then use **Save As...**; the original file is not repaired in place.

> **Caution:** Recovery cannot guarantee that every optional result or saved analysis survived. Confirm experiment counts, processing, fits, and result validity before continuing.

## Remove and clear content

Removing an experiment or Analysis Result changes only the open document; it does not delete the original raw file. **Remove All Data/Results** clears the current document after confirmation. **Delete All Results** removes all saved Analysis Results while retaining experiment processing and attached fits.

**Clear Processing > Selected / Active / All** restores processing defaults and removes attached models and fitted solutions for the chosen experiments. Thermogram experiments lose their calculated baselines and integrated heats; imported integrated heats are retained. Raw data, concentrations, injection choices, and attributes—including buffer-subtraction setup—are preserved. Saved Analysis Results remain available; their existing validity checks identify changed experiment inputs.

Clearing multiple experiments requires confirmation. Clear Processing has no Undo and is unavailable while processing or fitting is running. After clearing, process and fit the experiments again as needed.

Saving after removal makes the removal part of the saved project. Use **Save As...** first if you want to preserve the original project version.

## Privacy and online checks

Processing, fitting, saving, recovery, export, and printing run locally. Optional automated interpretation in **Analysis Report** sends the selected report evidence, question, and context to an online service when you request generation. See [Analysis Report](09-figures-printing-export.md#analysis-report) for details.

If **Check for updates and online resources on launch** is enabled, the application checks for release and citation updates; it does not upload experiment data. Disable this setting to prevent launch-time checks; it does not control automated interpretation requests. A failed or disabled check does not prevent local processing, fitting, or saving.

### Online data flow and retention

**Automated interpretation:** Choosing **Generate** sends the selected report evidence and your question or context to FT-ITC's online interpretation service at app.ft-itc.org. The service uses your selected provider, OpenAI or Mistral, to generate the interpretation. Mistral requests use Mistral's EU regional endpoint. This sets where the interpretation is generated, not where all account or operational records are kept. Sent evidence can include experiment and result names, comments, conditions, concentrations, saved fits, uncertainty summaries, diagnostics, and injection data. The selected **Evidence level** limits fitted result detail; Public access uses Results. Supporting experiments contribute summary information at every level, without injection rows or traces. Compressed thermograms for fitted result members are off when the dialog opens and are sent only if you opt in. **Save package** saves the full local evidence and the exact payload for the selected level without sending them. Opening Preferences or the generation dialog can check service availability and account access without sending scientific evidence. A supplied access code is sent to verify access.

**Project Viewer:** Opening a project in the browser-based Project Viewer uploads the complete `.ftxtc` file, including its embedded data and comments, to app.ft-itc.org for display. This does not submit the project for automated interpretation or send it to an AI provider. The viewer has a 50 MB upload limit. Closing the viewer does not retract an upload or guarantee that temporary processing copies or infrastructure records have been deleted; the full service has no verified end-to-end deletion schedule.

**Retention:** FT-ITC's interpretation service retains usage metadata that can be linked to requests or accounts, without automatic expiry. These usage records do not contain the complete scientific report evidence or generated interpretation. There is no in-app control to delete service records. Canceling generation or deleting a local report does not retract information already sent. Data sent to OpenAI or Mistral is subject to that provider’s applicable data controls and retention policies; see [OpenAI data controls](https://developers.openai.com/api/docs/guides/your-data) and [Mistral regional inference](https://docs.mistral.ai/inference/regional-inference). No zero-retention or fixed-deletion guarantee is made for the complete service flow.

## Update safely

Before installing a new application version, save important projects and retain the current installer when reproducibility policy requires it. After updating, open a copy of a representative project, confirm processing and results, and save only when you intend the project to be written by the new version.
