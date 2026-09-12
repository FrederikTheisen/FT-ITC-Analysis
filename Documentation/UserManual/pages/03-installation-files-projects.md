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

Delimited `.dat` and `.aff` inputs must provide positive `INJV` injection volumes and at least one usable heat column. FT-ITC Analysis prefers a complete `DH` column as absolute injection heat. If `DH` is absent or incomplete, it accepts a complete `NDH` column as normalized heat per mole; `NDH` may be absent for the automatically excluded first injection. The separate `.dh` format uses a fixed metadata-and-injection layout rather than the delimited column layout used by `.dat` and `.aff` files.

These files do not encode an unambiguous heat unit. For `DH`, select the absolute-energy unit used by that column. For an `NDH`-based import, select the energy unit in the per-mole values (for example, select **calorie** for cal/mol). The reader converts normalized heat to absolute injection heat using the syringe concentration and injection volume. If the syringe concentration cannot be inferred from `Xt`/`Mt`, it must be supplied before an NDH-based import can continue; canceling that prompt skips only the current file. Reuse the selected unit for the remaining files only when every file in that import operation uses the same heat unit and heat-column convention.

When `Mt`/`Xt` concentration values are available, the reader uses their progression and the selected dilution model to infer cell volume and syringe concentration. This inference does not depend on the heat values. Each injection row stores the concentrations before that injection; an optional final row without an injection stores the concentrations after the last injection. `Mt` and `Xt` are interpreted as mM in normal application imports. If the concentration sequence is absent, malformed, or internally inconsistent, `DH` heat and injection-volume rows remain importable; an `NDH`-based import first requires a syringe concentration, and validation asks for any other unresolved metadata instead of silently guessing it. These formats contain no thermogram, so importing them cannot reconstruct a baseline or processing-derived injection uncertainties.

## Open files

Choose **File > Open...**, use the welcome-screen action, or drag files into the application. Multiple supported files can be opened together. Files opened into a populated document are added to its existing Data / Results list; this includes `.ftxtc` projects. Clear the current document first if you want to open a project by itself.

> **Caution:** After an `.ftxtc` project is added to an existing document, that opened project becomes the document's current save destination. Use **Save As...** before saving if you do not intend to replace it with the combined document.

> **Caution:** Appending can create similarly named experiments or results. Confirm the data list and details before fitting or exporting.

## Save projects

Choose **File > Save** to update the current named project, or **File > Save As...** to choose a new name or location. Use the current `.ftxtc` format for ongoing work.

An `.ftxtc` project preserves the data and metadata needed to continue analysis, including thermograms where imported, concentrations and uncertainties, attributes and comments, injection inclusion, processing state, fit solutions, Analysis Results, and completed derived analyses. The package is portable and does not depend on the original raw-file path for ordinary reopening.

**Save Selected...** writes selected project content when you need to share a subset of the project. Confirm the selection before saving and reopen the result if the subset is critical. Selecting Experiment Data saves each selected experiment and its attached solution, if any. Selecting an Analysis Result saves the result and its member experiments.

> **Recommendation:** Save a processed version of the project before fitting if you want a reusable starting point. After fitting, save the project again—under a new name if you want to preserve the processed-only version—to retain the fitted solutions and Analysis Results.

## Autosave and recovery

Autosave behavior is configured in **Preferences...**. When recovery data is available after an interrupted session, the application offers a recovery path. Open the recovered document, inspect the data list and recent changes, then save it as a named `.ftxtc` project.

Recovery mode is designed to salvage valid project components when possible. If an experiment is unavailable, its saved fit is omitted; Analysis Results that depend on that fit are also omitted rather than being restored with a scientifically different set of members. Unaffected experiments, fits, and results can still be recovered. When recovery was required, the application displays a warning and records the individual recovery issues in the application log. A recovered project can be detached from its former save location and marked as changed. Use **Save As...** rather than assuming the damaged or interrupted file was repaired in place.

> **Caution:** Recovery cannot guarantee that every optional result or cached component survived. Confirm experiment counts, processing, fits, and result validity before continuing.

## Remove and clear content

Removing an experiment or Analysis Result changes only the open document; it does not delete the original raw file. **Remove All Data/Results** clears the current document after confirmation. **Clear Processing/Results** removes all Analysis Results from the open document.

Saving after removal makes the removal part of the saved project. Use **Save As...** first if you want to preserve the original project version.

## Privacy and online checks

Processing, fitting, saving, recovery, export, and printing run locally. Optional AI interpretation in **Analysis Report** sends the selected report evidence, question, and context to an online service when you request generation. See [Analysis Report](09-figures-printing-export.md#analysis-report) for details.

If **Check for updates and online resources on launch** is enabled, it retrieves GitHub release metadata and the repository's citation metadata file; it does not upload experiment data. Disable this setting to prevent launch-time checks; it does not control AI interpretation requests. A failed or disabled check does not prevent local processing, fitting, or saving.

## Update safely

Before installing a new application version, save important projects and retain the current installer when reproducibility policy requires it. After updating, open a copy of a representative project, confirm processing and results, and save only when you intend the project to be written by the new version.

### Online data flow and retention

**Before generating:** selecting **Generate** in the AI dialog sends the selected report evidence, main question and additional context over HTTPS to **app.ft-itc.org**, the FT-ITC interpretation service (MIST). Evidence includes result and experiment names, comments, conditions and concentrations, saved fits and uncertainties, diagnostics, and injection-level data. Supporting experiments are included when selected. Compressed thermograms are off by default and are included only when enabled by an eligible user. Use **Save AI package…** to inspect a local snapshot without sending it. The relay forwards compact model input and instructions to **OpenAI's Responses API**; scientific guidance can be retrieved from its configured knowledge store. The generation path does not upload the project as a provider file or add it to that knowledge store.

Opening the dialog or Preferences can contact MIST for status, options and account information without sending scientific evidence. A supplied capability code is sent to MIST for verification and generation authorization. Operator records can associate a code ID with a name/email and access entitlement. Ordinary processing, fitting, saving and export require no generation request; launch-time GitHub checks are separately optional.

**Service records:** SQLite usage logging is enabled by default and in the deployment inspected on 12 September 2026. It records request/trace/report/analysis and operator-code IDs, timestamps, request sizes, task and model/preset/guidance versions, latency, outcomes/error codes, provider response/request IDs, attempts, token/search usage and estimated costs. These are linkable metadata, not anonymous statistics. The usage tables do not store complete scientific evidence, context, generated prose or the capability-code secret. Application diagnostics also record stages, IDs, sizes, timing and errors. They are not a deliberate full-payload archive.

**Viewer:** opening a file uploads the entire selected `.ftxtc` project to app.ft-itc.org, including its embedded data and comments. The server returns parsed data to the browser and does not keep the parsed document as application session state. Upload buffering and expanded archive entries can use server temporary files. The archive reader attempts deletion when disposed, including normal error exits, but cleanup failures or process interruption can leave files behind. FTXTC parsing logs stages, counts, component IDs and recovery/error details; therefore the viewer is not log-free. Viewer opening does not itself call OpenAI.

**Retention and deletion:** the usage database has no application-level automatic expiry or user deletion endpoint; records persist until an operator removes them. Viewer temporary-file deletion is best effort, not secure erasure or a guaranteed deadline. The inspected MIST service uses private temporary storage and sends process output to the system journal. No explicit journal retention deadline was configured in the inspected files. Host/proxy logs, temporary-file cleanup after crashes, disk snapshots and backups have no verified end-to-end deletion schedule here. Closing the viewer, canceling generation or deleting a local report does not delete server records or retract data already sent.

The relay sends `store: false`. This does not establish zero provider retention. OpenAI documents default abuse-monitoring retention of up to 30 days, with legal and safety exceptions, and separate feature-specific storage such as prompt caching. API training is off by default unless the account opts in. The service account's data-sharing, retention and residency settings have not been verified. See [OpenAI data controls](https://developers.openai.com/api/docs/guides/your-data). No zero-retention or fixed deletion guarantee is made for this service.
