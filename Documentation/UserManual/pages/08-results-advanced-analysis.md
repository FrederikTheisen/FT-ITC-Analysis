---
title: Results and advanced analyses
summary: Read Analysis Results, control uncertainty presentation, maintain valid solutions, and use temperature, salt, counter-ion, or protonation analyses.
slug: results-advanced-analysis
nav_order: 8
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Results and advanced analyses

An **Analysis Result** stores a fit across one or more experiments, its constraints and model options, convergence information, weighting and uncertainty settings, member solutions, and a validity snapshot. It is the basis for combined result tables and compatible advanced analyses.

## Read the summary

Select an Analysis Result and confirm:

- result name and included experiments;
- selected model and options;
- fitted, shared, free, fixed, or dependent parameters;
- optimizer, convergence, and loss;
- whether injection-error weighting was used;
- uncertainty method and successful result population;
- validity for the current project state.

Use the selected-fit control to inspect each member experiment. Review its curve and residuals even when the main table reports a combined value.

## Evaluate at a temperature

The result view can present thermodynamic quantities at a chosen evaluation temperature. When temperature dependence is fitted, the configured reference temperature is the natural starting point; otherwise the application can use the mean experiment temperature.

Changing the evaluation temperature changes derived presentation according to the stored model. It does not alter the observed injection heats or silently refit the data.

Report the evaluation or reference temperature with temperature-dependent values. Values such as enthalpy, free energy, entropy contribution, dissociation constant, and heat-capacity change are meaningful only with their model, units, and temperature context.

## Present uncertainty

When the result contains the necessary resampling output, the view can show:

- standard deviations;
- 95% confidence intervals;
- both representations.

Changing the display changes presentation only. It does not recalculate the fit or convert one underlying sampling method into another.

> **Interpretation:** A symmetric standard deviation and an asymmetric interval can emphasize different properties of the same parameter distribution. Inspect the distribution or failure behavior when intervals are skewed, broad, or bounded.

## Copy or export a result table

Use **Copy Result Table** for a quick clipboard table. Use **Analysis Result Exporter...** when you need a controlled CSV or TSV output, multiple results, summary versus individual rows, or separate uncertainty columns.

Include units, uncertainty type, model, evaluation temperature, and fixed or shared assumptions in the surrounding report. A bare parameter table is not a complete analysis record.

## Update or load solutions

**Update Result** refreshes a stored result from a fit that corresponds to its current data and configuration. **Load Solutions to Experiments** makes member solutions available to the corresponding experiments, for example for review and figure generation. **Select Result Experiments** selects the experiments associated with the result.

Loading a solution is not a new fit. Check result validity before using a loaded solution for export.

## Result invalidation

Changes to data, processing, details, attributes, inclusion, or fit settings can invalidate a result. An invalid result remains useful as a record of an earlier state, but it must not be presented as the current solution.

Refit after the change, confirm all members, and regenerate tables and figures. Saving a project preserves the validity state; reopening does not make an invalid result valid.

## Advanced-analysis availability

Advanced tabs are intentionally conditional. They require compatible **One-Set-Of-Sites** results, sufficient experimental variation, and complete metadata. An unavailable control usually indicates unmet prerequisites rather than an interface failure.

### Temperature analysis

Temperature analysis becomes available when the contributing experiments span more than the configured minimum temperature range. It can expose temperature-dependent thermodynamic interpretation, including heat-capacity and structuring-related calculations where applicable.

Before interpreting it:

1. Verify every experiment temperature.
2. Confirm a common binding model and defensible constraints across the series.
3. Inspect each member fit and residuals.
4. Confirm the temperature range is broad enough to support the fitted relationship.

> **Interpretation:** A derived heat-capacity or structuring term inherits uncertainty and assumptions from the global fit. It is not direct structural evidence.

### Salt and ionic-strength analysis

Salt analysis requires ionic-strength variation and salt metadata for every contributing experiment. Depending on the selected mode, the view can examine affinity versus salt, Debye-Huckel behavior, or counter-ion release.

Use consistent concentration and ionic-strength conventions across the series. Include all relevant ionic species in the external calculation used to enter metadata. A salt label without correct ionic strength is not sufficient.

> **Interpretation:** A trend with ionic strength can be consistent with electrostatic contributions or ion release, but the software does not establish a unique molecular mechanism.

### Counter-ion analysis

Use the counter-ion-release mode only when the experiment series and salt identities support that interpretation. Check that the relevant metadata are complete and that changes in affinity are not confounded with pH, buffer, activity, temperature, or sample preparation.

### Protonation analysis

Protonation analysis requires buffer metadata for all contributing experiments and at least two buffer identities. It relates observed binding enthalpy to buffer protonation behavior under the selected assumptions.

Confirm pH, temperature, buffer identity, and appropriate buffer ionization enthalpy information for the experiment conditions. Buffer name alone cannot correct inconsistent pH or unrecorded additives.

> **Interpretation:** The result estimates linked proton exchange under the analysis assumptions. It does not identify a particular residue or microscopic protonation event.

## When an advanced tab is unavailable

Check, in order:

1. The result uses a compatible one-set-of-sites model.
2. All intended experiments are members of the result.
3. Member results are valid and fitted successfully.
4. Temperature, buffer, salt, and ionic-strength details are complete.
5. The required condition range or number of identities is present.

After correcting metadata, refit or update the analysis as required. Simply adding an attribute to an old result does not recompute the fit or its derived analysis.

## Report advanced results responsibly

Report the member experiments, condition range, base binding model, constraints, evaluation temperature, uncertainty method, excluded data, and any externally supplied constants. Preserve the `.ftxtc` project and exported table used for the report.
