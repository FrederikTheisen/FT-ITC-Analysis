---
title: Installation, files, and projects
summary: Install FT-ITC Analysis, open supported data, save portable projects, and use autosave or recovery safely.
slug: installation-files-projects
nav_order: 3
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Installation, files, and projects

## Install the application

Download an available release for your operating system from the [FT-ITC Analysis releases](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases) page. Verify that the file came from the project release channel before accepting an operating-system security prompt. Package availability and release status are stated on the release page; at this manual's verification date, macOS is the stable public release, Linux is pre-release, and Windows packaging is announced but not yet public.

### macOS

Open the DMG, drag **FT-ITC Analysis** to **Applications**, and eject the disk image. If macOS blocks a verified download, review it in **System Settings > Privacy & Security** and use **Open Anyway** only after confirming its source.

### Windows

When a Windows release is available, install the supplied MSIX package. A release can register `.ftxtc` and `.ftitc` project associations. If Windows displays a publisher or certificate warning, confirm the release source before continuing.

### Linux

Install the pre-release Debian package on a compatible Debian-based distribution. Package trust and dependency behavior depend on the channel through which the package was obtained. Review the current release notes for known platform limitations.

> **Platform note:** Package installation and file-association prompts are controlled by the operating system. Once the application is running, the analysis workflow and labels in this manual are shared.

## Supported input formats

| Extension | Source | Contains thermogram? | Typical next step |
| --- | --- | --- | --- |
| `.itc` | MicroCal-style raw data | Yes | Check details, then process |
| `.vpitc` | VP-ITC data | Yes | Check details, then process |
| `.ta` | TA Instruments/NanoAnalyze export | Usually | Check imported metadata |
| `.apj` | PEAQ-ITC project export | As supported by import | Check imported experiment data |
| `.dat` | Integrated heats | No | Check details, then fit |
| `.aff` | Integrated heats | No | Check details, then fit |
| `.dh` | Integrated heats | No | Check details, then fit |
| `.ftxtc` | Current FT-ITC project | Stored project state | Continue the saved workflow |
| `.ftitc` | Legacy FT-ITC project | Stored legacy state | Import, then save as `.ftxtc` |

PEAQ project imports are converted into experiment information understood by FT-ITC Analysis. Content that is specific to the source application is not automatically equivalent to a native FT-ITC project.

## Open files

Choose **File > Open...**, use the welcome-screen action, or drag files into the application. Multiple raw or integrated files can be opened together.

When you open an FT-ITC project while other data is loaded, choose whether to replace the current document or append the project's contents. Replace is appropriate when the opened project should become the complete working document. Append is useful for bringing experiments or results into an existing comparison project.

> **Caution:** Appending can create similarly named experiments or results. Confirm the data list and details before fitting or exporting.

## Save projects

Choose **File > Save** to update a named current project, or **File > Save As...** to choose a new name or location. Use the current `.ftxtc` format for ongoing work.

An `.ftxtc` project preserves the data and metadata needed to continue analysis, including thermograms where imported, concentrations and uncertainties, attributes and comments, injection inclusion, processing state, fit solutions, Analysis Results, and completed derived analyses. The package is portable and does not depend on the original raw-file path for ordinary reopening.

**Save Selected...** writes selected project content when you need a smaller handoff. Confirm the selection before saving and reopen the result if the subset is critical.

> **Recommendation:** Retain immutable copies of original instrument files, the named `.ftxtc` analysis project, and exported publication output. They serve different record-keeping purposes.

## Import a legacy project

Open `.ftitc` like any other supported file. Review the imported experiments, processing, and results, then choose **Save As...**. The saved file uses `.ftxtc`; the legacy source is not overwritten unless you deliberately choose its path and confirm an overwrite.

Some historical state can require recovery or review because current validation is stricter. Treat an imported legacy result as something to inspect, not automatically as a newly verified analysis.

## Autosave and recovery

Autosave behavior is configured in **Preferences...**. When recovery data is available after an interrupted session, the application offers a recovery path. Open the recovered document, inspect the data list and recent changes, then save it under a deliberate `.ftxtc` name.

Recovery mode is designed to salvage valid project components when possible. A recovered project can be detached from its former save location and marked as changed. Use **Save As...** rather than assuming the damaged or interrupted file was repaired in place.

> **Caution:** Recovery cannot guarantee that every optional result or cached component survived. Confirm experiment counts, processing, fits, and result validity before continuing.

## Remove and clear content

Removing an experiment or Analysis Result changes only the open document; it does not delete the original raw file. **Remove All** clears the current document after confirmation. **Clear Processing** discards processing state for the selected experiment, and **Clear Results** removes dependent solution state. Use these commands only when you intend to repeat that work.

Saving after removal makes the removal part of the saved project. Use **Save As...** first if you want to preserve the original project version.

## Privacy and online checks

Analysis is performed locally. The optional **Check for updates and online resources on launch** preference allows version and citation metadata checks; it does not upload experiment data as part of ordinary analysis. Disable the setting when launch-time network access is undesirable. A failed or disabled check does not prevent local processing, fitting, or saving.

## Update safely

Before installing a new application version, save important projects and retain the current installer when reproducibility policy requires it. After updating, open a copy of a representative project, confirm processing and results, and save only when you intend the project to be written by the new version.
