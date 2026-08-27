---
title: Glossary and equations
summary: Terminology, keyboard conventions, calculation symbols, equations, and references.
slug: reference
nav_order: 12
last_verified: 2026-08-26
_verification:
  product_version: "1.5.0"
  commit: "d3e153a0a10a67e3382efe39d368bb259ea8ccbd"
---

# Glossary and equations

## Workspace terms

| Term | Meaning in FT-ITC Analysis |
| --- | --- |
| **Experiment Data** | One imported or application-created ITC dataset, including its details and analysis state. |
| **Data / Results** | The workspace list containing experiments and stored Analysis Results. |
| **Selected** | The item currently shown or targeted by a selection-specific command. |
| **Active experiments** | Experiments included in a multi-experiment operation. |
| **Solution** | A fitted model state for one experiment. |
| **Analysis Result** | A stored fit result with one or more member solutions and the fit state used to create it. |
| **Attribute** | Experiment metadata used for organization or analysis context. |
| **Valid result** | An Analysis Result whose recorded fit inputs still match the current project state. |
| **Correlation view** | Pearson correlations between fitted parameter coordinates across complete residual-bootstrap refits. |
| **Pearson correlation** | A value from −1 to +1 describing linear co-variation between two fitted coordinates. |

## Uncertainty terms

| Term | Meaning in FT-ITC Analysis |
| --- | --- |
| **Best-fit value** | Parameter value from the primary optimization. Resampling estimates uncertainty around this value but does not replace it with a resampling mean or median. |
| **Standard deviation (SD)** | Root-mean-square deviation of retained resampling values from the primary best-fit value. |
| **95% confidence interval (CI)** | The 2.5th and 97.5th percentiles of the retained resampling distribution. It can be asymmetric around the best-fit value. |
| **Automatic uncertainty display** | Shows CI for a materially asymmetric stored interval and SD otherwise. It selects a presentation separately for each reported quantity. |
| **Displayed parameter uncertainty** | Fitted-coordinate uncertainty after conversion or propagation into the quantity shown to the user. The Automatic display decision is applied to this displayed quantity. |

## Keyboard conventions

The application command modifier is **Command** on macOS and **Ctrl** on Windows or Linux.

| Shortcut | Function |
| --- | --- |
| Command/Ctrl + O | Open data or a project. |
| Command/Ctrl + S | Save the current project. |
| Command/Ctrl + Shift + S | Save the project under another name. |
| Command/Ctrl + E | Open data export. |
| Command/Ctrl + P | Print the active graph or figure. |
| Command/Ctrl + Z | Restore the most recently removed item. |
| Command/Ctrl + I | Invert the Active Experiment Data set. |
| Command/Ctrl + comma | Open Preferences. |
| Command/Ctrl + Q | Quit FT-ITC Analysis. |
| F1 | Open **Help and Guide** on Windows and Linux. On macOS, open Help through the **?** menu command. |
| Enter | Open **Details** for the selected Data/Results item on Windows and Linux. |
| Left / Right | Change the selected injection in **Process Data**. |
| Space | Copy the selected integration length to the next injection in **Process Data**; the start is also copied when that processing option is enabled. |

## Thermodynamic symbols

| Symbol | Meaning |
| --- | --- |
| *N* | Stoichiometry or site-number term under the selected model. |
| *α* | Syringe-side active-concentration correction when syringe correction is enabled. |
| *K*<sub>a</sub> | Association constant. |
| *K*<sub>d</sub> | Dissociation constant. |
| *K*<sub>i</sub> | Macroscopic association constant for sequential transition *i*. |
| β<sub>i</sub> | Cumulative sequential association product ∏<sub>j=1…i</sub>*K*<sub>j</sub>. |
| *F*<sub>i</sub> | Fraction of macromolecule in sequential state *MX*<sub>i</sub>. |
| ν̄ | Mean ligand occupancy in the sequential model. |
| *ΔH* | Binding enthalpy change. |
| *ΔG* | Gibbs free-energy change. |
| −*T*Δ*S* | Entropic contribution reported by the application at the evaluation temperature. |
| *ΔC*<sub>p</sub> | Heat-capacity change in a supported temperature-dependent analysis. |
| *q* | Integrated injection heat. |
| *σ* | Standard uncertainty or spread, according to context. |
| **Offset** | Energy-per-mole-of-injectant correction included in the modeled injection heat. |

## Calculation symbols

| Symbol | Meaning |
| --- | --- |
| *P*(*t*) | Differential power at time *t* in a thermogram. |
| *b*(*t*) | Estimated baseline at time *t*. |
| *n*<sub>i</sub> | Amount injected in injection *i*. |
| *r*<sub>i</sub> | Residual for injection *i*: observed heat minus model heat. |
| *r*<sub>jk</sub> | Pearson correlation between fitted parameter coordinates *j* and *k*. |
| *θ*<sub>bj</sub> | Value of fitted coordinate *j* in complete bootstrap refit *b*. |
| *σ*<sub>i</sub> | Processing-derived uncertainty for injection *i*. |
| *R* | Gas constant. |
| *T* | Absolute temperature, in kelvin, for thermodynamic relationships. |
| *I* | Ionic strength used in salt analysis. |
| *a*<sub>ion</sub> | Ion activity used in Counter Ion Release analysis. |
| *m* | Fitted slope in the Protonation relationship; the application reports **Protons** as −*m*. |
| *n*<sub>ion</sub> | Counter-ion slope reported by Counter Ion Release analysis. |

## Equation index

| Relationship | Local explanation |
| --- | --- |
| Integration and molar normalization | [Processing](05-processing-thermograms.md#processing) |
| Monomer–dimer association | [Dissociation](06-fitting-models.md#dissociation) |
| Sequential binding polynomial and ligand balance | [Sequential Binding Sites](06-fitting-models.md#sequential-binding-sites) |
| Thermodynamic conversion and **−TΔS** | [Thermodynamic relationships](06-fitting-models.md#thermodynamic-relationships) |
| Fit residuals, RMSD, and weighting | [Weight by injection error](06-fitting-models.md#weight-by-injection-error) |
| Temperature-dependent constraints | [Multiple-experiment fitting](07-multiple-experiments.md#parameters) |
| Parameter correlation (Pearson residual bootstrap) | [Parameter correlation](08-results-advanced-analysis.md#parameter-correlation) |
| Bootstrap SD and percentile confidence interval | [Parameter uncertainty](06-fitting-models.md#parameter-uncertainty) |
| Automatic uncertainty display | [Uncertainty and evaluation temperature](08-results-advanced-analysis.md#uncertainty-and-evaluation-temperature) |
| Salt dependence | [Salt](08-results-advanced-analysis.md#salt) |
| Protonation dependence | [Protonation](08-results-advanced-analysis.md#protonation) |
| Buffer correction | [Buffer Subtraction](10-additional-tools.md#buffer-subtraction) |

Units are those shown by the application or export.

## References

- [FT-ITC Analysis repository](https://github.com/FrederikTheisen/FT-ITC-Analysis)
- [FT-ITC Analysis website](https://ft-itc.org)
- [FT-ITC Project Viewer](https://app.ft-itc.org)
- [Software DOI: 10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177)
- **Help > Citation** for the current paper citation, versioned software citation, and BibTeX

Scientific claims and model-specific methods require the primary literature appropriate to the experiment and analysis.
