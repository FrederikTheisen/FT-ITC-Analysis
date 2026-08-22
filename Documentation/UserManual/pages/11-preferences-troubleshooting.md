---
title: Preferences and troubleshooting
summary: Set user defaults, recover interrupted work, diagnose unavailable controls or failed fits, and create useful support reports.
slug: preferences-troubleshooting
nav_order: 11
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Preferences and troubleshooting

Open **Preferences...** to change user-level defaults. Preferences affect new work or display behavior as indicated; they do not retroactively certify an existing project.

## General preferences

General settings include unit and presentation choices, uncertainty display, autosave and recovery behavior, and the optional launch-time check for updates and online resources.

Choose units at the beginning of an analysis and report them with exports. Changing display units should not be confused with changing an entered physical value.

Disable online checks when working offline or when launch-time network access is restricted. Local analysis remains available.

## Processing preferences

Processing defaults control the starting baseline and integration behavior for new or reset processors. Choose defaults that suit the instrument and routine protocol, then inspect every experiment rather than accepting a default invisibly.

Project-specific processing is stored in the project. Changing a preference does not necessarily rewrite an already processed experiment.

## Fitting preferences

Fitting defaults include optimizer, parameter-limit, weighting, resampling, and presentation choices exposed by the current interface. Use defaults to reduce repetitive setup, but confirm the actual configuration in each Analysis Result.

## Export preferences

Export defaults control available formatting and presentation choices such as units, uncertainty columns, figure style, or destination-specific behavior. Reopen an exported file after changing these settings.

## Autosave and recovery

Keep autosave enabled for routine work unless local policy requires otherwise. After an interrupted session:

1. Accept the recovery candidate when it represents the work you need.
2. Inspect experiments, results, and recent edits.
3. Choose **Save As...** and create a named `.ftxtc` project.
4. Reopen the saved project and confirm important content.

If recovery reports omitted content, reprocess or refit only after confirming the underlying experiment data.

## A control is unavailable

Check these common prerequisites:

- **Process Data** requires an input with a thermogram.
- Graphical processing edits are disabled while processing is locked.
- Fit actions require suitable processed or integrated heats and an eligible experiment selection.
- Result actions require an Analysis Result with compatible member solutions.
- Advanced tabs require a compatible one-set-of-sites result, valid members, sufficient condition variation, and complete metadata.
- Export and print actions require the relevant active graph, figure, data, or result.

If the prerequisite is present, save the project, restart the application, reopen it, and reproduce the shortest sequence that demonstrates the issue.

## A result became invalid

The project changed after the result was created. Review recent changes to details, processing, attributes, experiment or injection inclusion, and fit settings. Either revert the change or rerun the fit. Then inspect member fits and regenerate dependent exports.

Do not report an invalidated result as current simply because its table remains visible.

## A fit fails or gives implausible values

Use this order:

1. Check units, concentrations, temperature, volumes, and attributes.
2. Check baseline, integration regions, uncertainty bars, and injection inclusion.
3. Confirm the chosen model matches the experiment.
4. Use plausible initial values and standard limits.
5. Try the alternate optimizer.
6. Reduce model complexity or global constraints.
7. Inspect whether the titration spans an informative transition.
8. Expand parameter limits only with an independent scientific reason.

Retain failed configurations when they provide useful sensitivity evidence; do not keep only the visually preferred fit.

## Import problems

If a file does not open:

- confirm the extension and source application;
- confirm the export is complete and not a shortcut, cloud placeholder, or partially copied file;
- try a fresh export from the instrument software;
- avoid editing structured source files in a spreadsheet before import;
- record the exact error and application version.

For a damaged `.ftxtc` project, use the offered recovery path. Never overwrite the only copy while testing recovery.

## Export or print problems

If output is empty or unavailable, confirm the correct experiment or result and active view. For result tables, confirm that the result contains fitted solutions. For figures, confirm the current processing and fit are valid.

If a CSV opens in one column, choose the matching delimiter during import or use TSV. If symbols or page elements are clipped, export again with a standard page size and inspect the PDF before printing.

Printing depends on an operating-system printer service. Export to PDF to distinguish a figure-rendering problem from a printer-driver problem.

## Create a support report

Open the support command and use **Copy Report** when available. Include:

- FT-ITC Analysis version;
- operating system and application implementation;
- shortest reproducible sequence;
- exact error message;
- whether the issue survives restart and project reopen;
- a minimal, shareable project or synthetic input when permitted;
- screenshots that show the complete relevant control state.

Remove confidential sample names and comments before sharing. Confirm that the reduced project still reproduces the problem.

## Citation and issue reporting

Choose **Help > Citation** for the current recommended paper and versioned software citation, BibTeX copy, or export. The persistent software DOI is [10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177).

Search existing reports and create a new issue in the [GitHub issue tracker](https://github.com/FrederikTheisen/FT-ITC-Analysis/issues) when needed. One reproducible problem per issue is easiest to diagnose.

