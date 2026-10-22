---
title: Glossary and equations
summary: Terminology, keyboard conventions, calculation symbols, equations, and references.
slug: reference
nav_order: 12
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# Glossary and equations

Use the tables to look up terms and symbols, and the equation index to reach each calculation beside its workflow and assumptions. Start with [Quick start](02-quick-start.md) for instructions.

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
| **Correlation view** | Pearson correlations between fitted parameter coordinates across residual-bootstrap refits that have finite values for every displayed parameter, with diagnostics for simulation precision. |
| **Pearson correlation** | A value from −1 to +1 describing linear co-variation between two fitted coordinates. |
| **Monte Carlo precision interval** | Approximate Fisher-transformation interval describing how precisely the correlation coefficient $r$ is estimated from the finite retained-refit ensemble; it is not uncertainty for either fitted parameter or a model-validity assessment. |
| **AIC / AICc** | Fit-comparison scores that account for parameter count; smaller is preferred only for comparable observations and likelihood conventions. They do not establish model adequacy. See [Information criteria](08-results-advanced-analysis.md#information-criteria) for weighting, variance estimation, member scope, and AICc's small-sample approximation. |

## Uncertainty terms

| Term | Meaning in FT-ITC Analysis |
| --- | --- |
| **Best-fit value** | Parameter value from the primary optimization. Resampling estimates uncertainty around this value but does not replace it with a resampling mean or median. |
| **Standard deviation (SD)** | For residual-bootstrap parameters, the root-mean-square deviation of retained refit values from the primary best-fit value, transformed or propagated for display. Profile-likelihood results use the equivalent display scale defined below. |
| **95% confidence interval (CI)** | For a residual-bootstrap parameter, the 2.5th and 97.5th percentiles of the retained refit distribution, transformed for display. Profile-likelihood intervals use likelihood-threshold endpoints. Derived quantities use the application's uncertainty-propagation rules. Intervals can be asymmetric around the best-fit value. |
| **Automatic uncertainty display** | Shows CI for a materially asymmetric stored interval and SD otherwise. It selects a presentation separately for each reported quantity. |
| **Displayed parameter uncertainty** | Fitted-parameter uncertainty after conversion or propagation into the quantity shown to the user. The Automatic display decision is applied to this displayed quantity. |
| **Profile likelihood** | A 95% interval search that holds one parameter at trial values while refitting the others. A bound reached before the threshold is censoring, not a confidence endpoint. See [Profile-likelihood uncertainty](06-fitting-models.md#profile-likelihood-uncertainty) for calibration and relative-weight assumptions. |
| **Profile equivalent SD** | Symmetric `value ± SD` display scale computed from a complete asymmetric profile interval; it is a display equivalent, not a Gaussian sampling SD. |

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
| F1 | Open **Help and Guide** on Windows and Linux. On native macOS, choose **Help > FT-ITC Analysis Help**. |
| Enter | Open **Details** for the selected Data/Results item on Windows and Linux. |
| Left / Right | Change the selected injection in **Process Data**. |
| Space | Copy the selected integration end-point offset to the next injection in **Process Data**; the start is also copied when that processing option is enabled. |

## Thermodynamic symbols

| Symbol | Meaning |
| --- | --- |
| $N$ | Stoichiometry or site-number term under the selected model. |
| $\alpha$ | Syringe-side active-concentration correction when syringe correction is enabled. |
| $K_{\mathrm{a}}$ | Association constant. |
| $K_{\mathrm{d}}$ | Dissociation constant. |
| $K_{i}$ | Macroscopic association constant for sequential transition $i$. |
| $\beta_{i}$ | Cumulative sequential association product $\prod_{j=1}^{i} K_j$. |
| $F_{i}$ | Fraction of macromolecule in sequential state $MX_{i}$. |
| $\bar{\nu}$ | Mean ligand occupancy in the sequential model. |
| $\Delta H$ | Binding enthalpy change. |
| $\Delta G$ | Gibbs free-energy change. |
| $-T\Delta S$ | Entropic contribution reported by the application at the evaluation temperature. |
| $\Delta C_{\mathrm{p}}$ | Heat-capacity change in a supported temperature-dependent analysis. |
| $q$ | Integrated injection heat. |
| $\sigma$ | Standard uncertainty or spread, according to context. |
| **Offset** | Energy-per-mole-of-injectant correction included in the modeled injection heat. |

## Calculation symbols

| Symbol | Meaning |
| --- | --- |
| $P(t)$ | Differential power at time $t$ in a thermogram. |
| $b(t)$ | Estimated baseline at time $t$. |
| $n_{i}$ | Amount injected in injection $i$. |
| $r_{i}$ | Residual for injection $i$: observed heat minus model heat. |
| $r_{jk}$ | Pearson correlation between fitted parameter coordinates $j$ and $k$. |
| $\theta_{bj}$ | Value of fitted coordinate $j$ in complete bootstrap refit $b$. |
| $\sigma_{i}$ | Processing-derived uncertainty for injection $i$. |
| $R$ | Gas constant. |
| $T$ | Absolute temperature, in kelvin, for thermodynamic relationships. |
| $I$ | Ionic strength used in salt analysis. |
| $a_{\mathrm{ion}}$ | Ion activity used in Counter Ion Release analysis. |
| $\Delta n_{\mathrm{H}^{+}}$ | Fitted slope of observed binding enthalpy against buffer ionization enthalpy: net proton uptake (positive) or release (negative) per binding event. |
| $\Delta H_{0}$ | Buffer-independent binding enthalpy at the experimental pH: the Protonation intercept, retaining binding-partner protonation heat. |
| $n_{\mathrm{ion}}$ | Counter-ion slope reported by Counter Ion Release analysis. |
| $\mathrm{RSS}$ | Raw residual sum of squares, $\sum r_i^2$, over included injections. |
| $Q$ | Standardized residual sum of squares, $\sum (r_i/\sigma_i)^2$. Weighted AIC/AICc estimates a common variance multiplier as $Q/n$. |
| $L$ | Gaussian likelihood used for AIC/AICc. |
| $n$ | Number of included observations in the information-criterion calculation. |
| $p$ | Number of free fitted parameters in the saved global model; shared coordinates count once and member coordinates count per member. |
| $K$ | AIC/AICc likelihood parameter count: $p + 1$ in both weighting modes, including one estimated common residual variance or one estimated multiplier of the injection-error variances. |

## Equation index

| Relationship | Local explanation |
| --- | --- |
| Integration and molar normalization | [Integration calculation](05-processing-thermograms.md#integration-calculation) |
| Processing-derived injection heat SD | [Noise calculation and limits](05-processing-thermograms.md#noise-calculation-and-limits) |
| Dilution-driven dissociation of an associated pair | [Dissociation](06-fitting-models.md#dissociation) |
| Sequential binding polynomial and ligand balance | [Sequential Binding Sites](06-fitting-models.md#sequential-binding-sites) |
| Thermodynamic conversion and **−TΔS** | [Thermodynamic relationships](06-fitting-models.md#thermodynamic-relationships) |
| Fit residuals, RMSD, and weighting | [Weight by injection error](06-fitting-models.md#weight-by-injection-error) |
| AIC, AICc, Gaussian likelihood, and parameter counting | [Information criteria](08-results-advanced-analysis.md#information-criteria) |
| Temperature-dependent constraints | [Linked temperature calculation](07-multiple-experiments.md#linked-temperature-calculation) |
| Parameter correlation (Pearson residual bootstrap) | [Parameter correlation](08-results-advanced-analysis.md#parameter-correlation) |
| Bootstrap SD and percentile confidence interval | [Residual-bootstrap calculation](06-fitting-models.md#residual-bootstrap-calculation) |
| Automatic uncertainty display | [Automatic display calculation](08-results-advanced-analysis.md#automatic-display-calculation) |
| Salt dependence | [Salt](08-results-advanced-analysis.md#salt) |
| Protonation dependence | [Protonation](08-results-advanced-analysis.md#protonation) |
| Buffer correction | [Buffer Subtraction](10-additional-tools.md#buffer-subtraction) |
| Tandem reload mixing | [What happens at a reload](10-additional-tools.md#what-happens-at-a-reload) |

Units are those shown by the application or export.

## References

- [FT-ITC Analysis repository](https://github.com/FrederikTheisen/FT-ITC-Analysis)
- [FT-ITC Analysis website](https://ft-itc.org)
- [FT-ITC Project Viewer](https://app.ft-itc.org)
- [Software DOI: 10.5281/zenodo.14832177](https://doi.org/10.5281/zenodo.14832177)
- [Malvern Instruments, *MicroCal ITC Analysis Software Using Origin User Manual*, MAN0577-02-EN-00 (20 May 2015)](https://www.malvernpanalytical.com/en/learn/knowledge-center/user-manuals/man0577en), section 12.3.1, equations 2 and 4; section 12.3.6, equation 30 (dimer dissociation).
- **Help > Citation** for the current paper citation, versioned software citation, and BibTeX
- [Philippe Dumas, *Isothermal titration calorimetry in the single-injection mode with imperfect mixing*, European Biophysics Journal 51, 77–84 (2022)](https://doi.org/10.1007/s00249-021-01588-4). The application's **Ideal continuous mixing** option is inspired by the paper but uses ideal mixing with a finite-injection numerical approximation; it does not implement the paper's imperfect-mixing model.
- [Ernesto Freire, Arne Schön and Adrián Velazquez-Campoy, *Isothermal titration calorimetry: general formalism using binding polynomials*, Methods in Enzymology 455, 127–155 (2009)](https://doi.org/10.1016/S0076-6879(08)04205-5). The application's **Discrete displacement** option uses this convention for concentrations and displaced-volume heat compensation, retaining FT-ITC's equilibrium solvers and offset convention.

Scientific claims and model-specific methods require the primary literature appropriate to the experiment and analysis.
