---
title: Workspace and experiment management
summary: Navigate the workspace, manage experiments and results, edit details, and keep project selections and metadata consistent.
slug: workspace-experiments
nav_order: 4
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Workspace and experiment management

## Understand the data list

The data list is the project navigator. It contains experiments and completed Analysis Results. Selecting an experiment opens experiment views; selecting a result opens its combined summary and member fits.

The enabled state controls whether an experiment participates in operations that use active datasets. Enable, disable, or invert active selections from the data commands. Inclusion of an individual injection is separate from inclusion of its experiment.

> **Note:** “Selected” means the item currently shown. “Enabled” or “active” means an item is eligible for a multi-item operation. Check both concepts before a global fit, merge, subtraction, or batch export.

## Navigate an experiment

For an experiment, use:

- **Overview** to inspect origin, instrument information, duration, temperature, concentrations, and analysis state.
- **Process Data** to define the baseline and integration regions for a thermogram.
- **Analyze Data** to configure and run a single- or multiple-experiment fit.
- **Final Figure** to prepare the current experiment and result for presentation.

The available processing controls depend on whether the import contains a thermogram. Result-dependent controls remain unavailable until a compatible fit exists.

## Review and edit details

Choose **Details...** for the selected experiment. The details view can include:

- experiment name, comments, date, temperature, and instrument metadata;
- cell and syringe concentrations;
- concentration uncertainties;
- attributes such as buffer identity, salt, ionic strength, competitor concentration, or prebound species.

Use the units shown by the control. Confirm changes before fitting because concentration and temperature values enter the model, while attributes can determine constraints and advanced-analysis availability.

> **Recommendation:** Record why a value was changed in the experiment comment or in the laboratory record. The application stores the value, but it cannot establish the provenance of a correction.

Merged tandem experiments store calculated starting concentrations for each segment. Edit merge inputs or recreate the merge when those values need correction; do not treat segment bookkeeping as an ordinary single-experiment concentration field.

## Duplicate an experiment

Use **Duplicate** when you want to compare processing or fitting choices without reimporting the raw file.

1. Select the source experiment.
2. Choose the duplicate command.
3. Rename or comment the copy so its purpose is clear.
4. Change only the intended processing or analysis choices.

The duplicate belongs to the same project. It is not a second independent raw measurement and should not be counted as a biological or technical replicate.

## Remove experiments and results

Select an item and choose **Remove Data** or **Remove Result**. Confirm the operation. Removal does not delete an instrument file on disk, but it will be reflected in the project the next time you save.

Removing or changing an experiment can invalidate or remove the usefulness of associated results. If you need an archival comparison, save the project under a new name before making structural changes.

## Sort the project

Use sorting commands to organize experiments by the exposed metadata, including experimental or attribute values where available. Sorting changes presentation order; it does not change the underlying fit or create a grouping constraint.

Use explicit names and attributes in addition to sorting. A global analysis should be reproducible from the selected experiments and constraints, not from visual adjacency alone.

## Copy and clear attributes

Project commands can copy attributes from one experiment to others, perform an attribute operation, or clear attributes. These are efficient for consistent metadata across a series, but they can also propagate a mistake.

1. Check the source experiment and target selection.
2. Apply the copy or operation.
3. Reopen details on representative targets.
4. Save after confirming the result.

> **Caution:** Buffer, salt, ionic strength, competitor, and prebound-species attributes can change model availability, constraints, or derived analyses. They are analysis inputs, not decorative labels.

## Manage injection inclusion

An injection can be included in processing display yet excluded from fitting. Use the injection inclusion control to omit a demonstrably compromised injection, such as a first-injection artifact or a known delivery failure. Reinclude it to test sensitivity.

Do not remove points solely because their residuals are large. First check raw signal, integration boundaries, baseline, concentrations, and model adequacy. Record the reason for exclusions.

## Clear processing or results

**Clear Processing** returns the selected thermogram to an unprocessed state. **Clear Results** removes fitted solution state associated with the selection. These commands are useful when starting a controlled reanalysis, but they deliberately discard work.

Changes to data, details, processing, attributes, injection inclusion, or fit configuration can make an existing Analysis Result invalid for the current project state. The validity indicator is the authoritative warning; updating a figure does not make an invalid result current.

## A maintainable project pattern

For a multi-condition study:

1. Name experiments consistently.
2. Enter concentrations, uncertainties, temperature, and attributes before processing.
3. Process each thermogram and record justified injection exclusions.
4. Save a checkpoint project.
5. Configure and run the combined analysis.
6. Save again before exporting tables and figures.

This separates experiment curation from model fitting and makes later troubleshooting easier.
