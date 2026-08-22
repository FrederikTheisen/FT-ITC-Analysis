---
title: Reference
summary: Look up supported formats, terminology, models, uncertainty language, keyboard conventions, symbols, and source references.
slug: reference
nav_order: 12
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Reference

## File formats

| Extension | Category | User purpose |
| --- | --- | --- |
| `.itc` | Raw thermogram | MicroCal-style experiment import |
| `.vpitc` | Raw thermogram | VP-ITC experiment import |
| `.ta` | Instrument export | TA Instruments/NanoAnalyze import |
| `.apj` | Project export | PEAQ-ITC experiment import |
| `.dat` | Integrated heats | Fit injection heats without thermogram processing |
| `.aff` | Integrated heats | Fit injection heats without thermogram processing |
| `.dh` | Integrated heats | Fit injection heats without thermogram processing |
| `.ftxtc` | Current project | Save and exchange complete FT-ITC analysis state |
| `.ftitc` | Legacy project | Import historical FT-ITC projects, then save as `.ftxtc` |
| `.csv` / `.tsv` | Table export | Exchange peak or fitted-result tables |
| `.pdf` | Figure output | Preserve publication and print layout |

## Workspace terminology

| Term | Meaning in FT-ITC Analysis |
| --- | --- |
| Experiment | One imported or designed ITC dataset, including details and analysis state |
| Data list | Project navigator containing experiments and Analysis Results |
| Selected | The item currently shown or targeted by a selection-specific command |
| Enabled / active | Eligible for a multi-item operation or analysis |
| Injection inclusion | Whether an individual injection participates in fitting |
| Processing | Baseline correction and thermogram peak integration |
| Solution | Fitted model state for an experiment |
| Analysis Result | Stored combined result, members, constraints, uncertainty, and validity snapshot |
| Valid result | A result whose recorded fit inputs still match current project state |
| Attribute | Experiment metadata used for organization, constraints, or derived analyses |
| Tandem experiment | Consecutive syringe-load segments treated as one continuing titration |

## Baseline and integration summary

| Choice | Best considered when | Main diagnostic |
| --- | --- | --- |
| Spline | Local graphical control is needed | Point placement, smoothness, and sensitivity |
| Polynomial | One smooth global drift is plausible | End behavior and degree dependence |
| Segmented | Local drift differs through the run | Continuity and local over-flexibility |
| Time integration | A common response duration is defensible | Consistent return toward baseline |
| Factor integration | Peak shapes require scaled lengths | Stability of estimated endpoints |
| Fit integration | Fitted peak shape can guide endpoints | Fit adequacy for atypical peaks |

## Model-selection summary

| Model | Intended pattern | Principal caution |
| --- | --- | --- |
| One-Set-Of-Sites | One class of equivalent non-interacting sites | Can hide heterogeneity or coupled processes |
| Two-Sets-Of-Sites | Two independently fitted site classes | High parameter demand and label ambiguity |
| Competitive Binding | Titration into a preformed competing system | Depends strongly on competitor inputs and assumptions |
| Dissociation | Injected preformed complex dissociates | Species preparation and concentration bookkeeping must match |

Syringe correction is a model option where exposed, not a fifth public model.

## Constraint terminology

| State | Meaning |
| --- | --- |
| Free | Separate value estimated for each experiment |
| Shared | One value estimated across the selected experiments |
| Fixed | Supplied value held constant during fitting |
| Temperature-dependent | Values linked by the model's temperature relationship |

## Uncertainty glossary

| Term | Meaning |
| --- | --- |
| Injection error | Estimated uncertainty of an integrated injection heat from local corrected noise and integration behavior |
| Weighted fit | A fit in which observations are scaled by their injection-error estimates |
| Bootstrap residuals | Repeated refitting using resampled residual behavior |
| Leave-one-out | Repeated refitting with observations omitted in turn |
| Standard deviation | Spread summary of available resampled parameter values |
| 95% confidence interval | Interval derived from the available uncertainty distribution and method |
| Concentration uncertainty | Entered uncertainty propagated through supported resampling workflows |
| Confidence band | Visual uncertainty region for a fitted curve when supported by stored results |
| Systematic error | Bias not necessarily represented by the fit or resampling method |

## Common thermodynamic symbols

| Symbol | Meaning |
| --- | --- |
| `N` | Stoichiometry or site-number term under the selected model |
| `alpha` | Syringe-side active-concentration correction when syringe correction is enabled |
| `Ka` | Association constant |
| `Kd` | Dissociation constant |
| `Delta H` | Binding enthalpy change |
| `Delta G` | Gibbs free-energy change |
| `T Delta S` | Entropic contribution at the evaluation temperature |
| `Delta Cp` | Heat-capacity change in a supported temperature-dependent analysis |
| `q` | Integrated injection heat |
| `sigma` | Standard uncertainty or spread, according to context |

Always use the units displayed or exported by the application. Do not combine values expressed in different energy, concentration, or temperature units without conversion.

## Keyboard conventions

The operating-system command modifier is **Command** on macOS and **Ctrl** on Windows or Linux. Standard shortcuts for open, save, copy, and preferences follow the active platform where provided.

On the welcome screen, **Enter** opens the file chooser and **Space** can open recent data. While reviewing individual integration regions, **Space** can copy the current integration length to the next injection and advance the view.

Keyboard focus matters. If a shortcut changes text or has no effect, click the intended graph or workspace control and try again.

## Reporting checklist

For a reproducible result, record:

- application version and software citation;
- input source and experiment identifiers;
- concentrations, uncertainties, temperature, and relevant attributes;
- baseline and integration approach;
- excluded experiments or injections and reasons;
- model, initial-value strategy, constraints, limits, optimizer, and weighting;
- uncertainty method and successful refit behavior;
- evaluation temperature and units;
- advanced-analysis assumptions;
- project and export filenames.

## References and further information

- [FT-ITC Analysis repository](https://github.com/FrederikTheisen/FT-ITC-Analysis)
- [FT-ITC Analysis releases](https://github.com/FrederikTheisen/FT-ITC-Analysis/releases)
- [FT-ITC Analysis wiki](https://github.com/FrederikTheisen/FT-ITC-Analysis/wiki)
- [Software DOI: 10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177)
- **Help > Citation** in the application for current paper and versioned software citation text

For scientific use, consult the primary literature appropriate to the binding model, experimental design, error model, and derived analysis. The application citation identifies the software; it does not replace citations for the underlying method.
