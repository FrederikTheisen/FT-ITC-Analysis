---
title: Single-experiment fitting
summary: Fit one experiment, configure model and uncertainty options, control injection inclusion, and interpret fit diagnostics.
slug: fitting-models
nav_order: 6
last_verified: 2026-08-23
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Single-experiment fitting

**Analyze Data** in **Single experiment** mode fits the integrated heats of the selected Experiment Data. The fit uses the included injections together with the experiment concentrations, injection volumes, cell volume, temperature, and any model-specific information.

The inspector has four tabs:

- **Fit** selects the model, optimizer, error-estimation method, limits, weighting, and result output.
- **Parameters** shows the model parameters and their starting or fixed values.
- **Options** contains settings specific to the selected model.
- **Display** controls the fitted curve and diagnostic information shown in the graph.

![Analyze Data workspace showing a fitted one-set-of-sites curve, residuals, fitting controls, and fit status.](../assets/fitting-workspace.png)

*Analyze Data combines the fitted-heats graph and residuals with the controls for configuring and running the fit.*

Multiple-experiment fitting uses additional experiment selection and parameter constraints; see [Multiple-experiment fitting](07-multiple-experiments.md).

## Injection inclusion

Selecting an injection point in the integrated-heats graph changes whether it is included in the fit. Excluded injections do not contribute to the objective function. The **Excluded points** option in **Display** keeps excluded injections visible and available for selection.

![Analyze Data graph with excluded injections visible and the Display controls for the fit and its diagnostics.](../assets/fitting-injection-inclusion.png)

*Excluded injections remain visible when Excluded points is enabled and can be selected for inclusion again.*

Changing injection inclusion does not rerun the fit. The fitted curve and parameters continue to represent the previous fit until **Run Fit** is used again. An inclusion change can also invalidate an Analysis Result that contains the experiment.

## Models

### One-Set-Of-Sites

**One-Set-Of-Sites** represents one class of equivalent, independent binding sites. It fits stoichiometry, dissociation constant, binding enthalpy, and an injection-heat offset. The solution also reports thermodynamic quantities derived from the fitted affinity and enthalpy.

The **Options** tab provides **Use Syringe Correction** and **Stoichiometry**. Without syringe correction, the fitted N-value represents the apparent site stoichiometry. With syringe correction enabled, **Stoichiometry** fixes the number of cell-side sites and the fitted N parameter becomes the active syringe-concentration factor `alpha`.

The model cannot by itself distinguish a concentration error from other effects that change an apparent stoichiometry.

### Two-Sets-Of-Sites

**Two-Sets-Of-Sites** represents two independent classes of sites. It fits separate stoichiometries, dissociation constants, and enthalpies for the two classes, together with a shared injection-heat offset.

**Shared N-Values** makes the two site classes use the same fitted stoichiometry. **Use Syringe Correction** instead fixes the first and second **Stoichiometry** values and fits one active syringe-concentration factor, `alpha`.

The two site labels are interchangeable: exchanging all parameters assigned to site 1 and site 2 describes the same physical model. A lower fitting loss alone does not establish that two distinguishable binding processes are supported by the experiment.

### Competitive Binding

**Competitive Binding** represents titration of a target ligand into a macromolecule that is initially in equilibrium with a prebound ligand in the cell. It fits the target ligand's stoichiometry, dissociation constant, binding enthalpy, and injection-heat offset.

The **Options** tab requires the prebound ligand **[Ligand]**, **Ligand Affinity**, and **Ligand Enthalpy**. **From attributes** makes **[Ligand]** use the corresponding value stored in the Experiment Data attributes instead of the value entered in the model options. The model also provides **Use Syringe Correction** and **Stoichiometry** with the same concentration-factor interpretation as One-Set-Of-Sites.

The fitted target affinity and enthalpy depend on the supplied prebound-ligand properties. Those values are model inputs rather than quantities independently determined by the competitive fit.

### Dissociation

**Dissociation** represents dilution-driven monomer-dimer self-association. The syringe contains the macromolecule and the cell initially contains buffer. Dilution and mixing change the dimer population, and the model fits the association equilibrium through its reported dissociation constant, the association enthalpy per mole of dimer formed, and an injection-heat offset.

> **Calculation:**
>
> 2 <i>M</i> ⇌ <i>D</i>
>
> <i>K</i><sub>a</sub> = [<i>D</i>] / [<i>M</i>]<sup>2</sup>
>
> Here, [<i>M</i>] and [<i>D</i>] are the monomer and dimer concentrations, and <i>K</i><sub>a</sub> is the association constant.

This is not a general model for dissociation of an arbitrary preformed complex or for other oligomerization schemes. It has no stoichiometry or syringe-correction options.

### Thermodynamic relationships

The application derives thermodynamic quantities from the fitted affinity and enthalpy. Temperature <i>T</i> is expressed in kelvin.

> **Calculation:**
>
> <i>K</i><sub>d</sub> = 1 / <i>K</i><sub>a</sub>
>
> Δ<i>G</i> = <i>R</i><i>T</i> ln(<i>K</i><sub>d</sub>) = −<i>R</i><i>T</i> ln(<i>K</i><sub>a</sub>)
>
> −<i>T</i>Δ<i>S</i> = Δ<i>G</i> − Δ<i>H</i>
>
> Here, <i>R</i> is the gas constant, and the final relationship matches the **−TΔS** quantity reported by the application.

## Parameters and model options

![Parameters and Options inspectors showing fitted values, Locked controls, syringe correction, and fixed stoichiometry.](../assets/fitting-parameters-options.png)

*Parameters exposes parameter values and locks; Options contains settings specific to the selected model.*

The application generates initial parameter values from the experiment and can reuse an attached fitted solution where applicable. Entering a value replaces the generated starting value for that parameter. Values are displayed in the current application units. Clearing a value returns the parameter to automatic initialization.

**Locked** holds a parameter at its displayed value during the primary fit. An unlocked parameter is adjusted by the optimizer. Locked values remain part of the model and affect every other fitted parameter even though they are not estimated by that fit.

Analysis choices are retained separately for the available fitting modes and models. **Restore defaults** clears the stored analysis inputs and restores the fitting-related defaults, including the standard limit policy and result-output defaults.

The **Limits** control selects a common parameter-bound policy:

- **Standard** uses the normal parameter bounds.
- **Expanded** permits a wider parameter range.
- **No limits** removes the configured parameter bounds.

A fitted value at a bound is not an interior estimate. It indicates that the reported value depends on the selected bound as well as on the data and model.

## Fitting calculation

### Algorithm

**Levenberg-Marquardt** uses local derivative information and can be efficient when the starting values describe a suitable region of the fitting surface.

**Nelder-Mead** is a derivative-free simplex optimizer. It provides an alternative calculation for surfaces or starting conditions that are less cooperative for the local derivative-based method. Agreement between optimizers does not by itself establish that the selected model is scientifically adequate.

### Weight by injection error

**Weight by injection error** uses the integration uncertainty estimated during thermogram processing when calculating the fitting objective. Injections with larger estimated uncertainty consequently have less influence than injections with smaller estimated uncertainty.

> **Calculation:**
>
> <i>r</i><sub>i</sub> = <i>q</i><sub>i,obs</sub> − <i>q</i><sub>i,model</sub>
>
> RMSD = √[(Σ<i>r</i><sub>i</sub><sup>2</sup>) / <i>N</i>]
>
> weighted objective = Σ(<i>r</i><sub>i</sub> / <i>σ</i><sub>i</sub>)<sup>2</sup>
>
> Only included injections enter these sums. The value <i>σ</i><sub>i</sub> is the processing-derived uncertainty for injection *i*; weighting changes the fitting objective but does not remove systematic uncertainty.

The weighting describes the application's processing-derived uncertainty model. It does not account for every systematic source of experimental or processing uncertainty.

## Parameter uncertainty

The **Errors** control determines whether the primary best fit is followed by repeated refitting:

- **None** retains the primary fit without resampling-based parameter uncertainty.
- **Bootstrap residuals** constructs synthetic datasets from the fit residuals and refits them.
- **Leave-one-out** refits reduced datasets with included injections omitted in turn.

**Bootstrap** sets the requested number of resampling iterations. The fit status distinguishes successful and failed refits. Reported resampling uncertainty is calculated around the primary solution; the primary parameter values are not replaced with the average of the resampled fits.

When concentration errors are enabled in Preferences, the concentration uncertainties entered in **Details...** are propagated through supported resampling calculations. These uncertainties affect the synthetic experiment concentrations used for the refits, not the concentrations used for the primary best fit.

### Unlock parameters during error estimation

**Locked** parameters remain fixed during the primary fit. With **Unlock parameters** enabled, copies of those parameters are unlocked for the error-estimation refits and can vary in the resampled solutions.

This setting does not change or rerun the primary best fit. It changes only the parameter state used by the repeated error-estimation fits, and it has no effect when no fitted parameter is locked.

## Fit execution and diagnostics

**Run Fit** starts the primary optimization and the selected error-estimation calculation. **Stop** requests cancellation of the active calculation. When fitting ends, the status reports the termination state, RMSD, iteration count, and elapsed time. A resampling calculation also reports its outcome and the number of successful and failed refits.

The **Display** tab exposes complementary diagnostics:

- **Fit line** shows the curve calculated from the current fitted solution.
- **Residuals** show the difference between each included observation and the fitted curve.
- **Error bars** show the processing-derived integration uncertainty.
- **Confidence band** shows uncertainty around the fitted curve when the solution contains suitable resampling results.
- **Excluded points** shows injections that do not contribute to the current fit.

These displays describe different aspects of the fitted solution. A small RMSD does not rule out systematic residual structure, poorly identified parameters, or dependence on model assumptions.

### Store the fitted solution

With **Create analysis result** disabled, a successful single-experiment solution remains attached to its Experiment Data and is available in that experiment's analysis and figure workflows.

With **Create analysis result** enabled, a usable completed fit also creates a separate Analysis Result containing the experiment, model, fit settings, and solution. **Auto-open new result** determines whether the new result workspace opens immediately. A stopped or unusable fit does not create or replace an Analysis Result.

## Fit availability and non-convergence

A single-experiment analysis requires processed or imported heats and at least three included injections with usable numerical values. The binding models also require nonzero cell and syringe concentrations. **Dissociation** uses the syringe concentration and does not require a macromolecule concentration in the initially buffered cell.

An unavailable model, failed termination, bound-limited solution, or failed resampling population can reflect missing or degenerate heat and concentration information, the supplied starting values, the selected limit policy, the optimizer's interaction with the fitting surface, model complexity, or weak parameter identifiability. Resampling can fail even when the primary fit succeeds because each refit presents a different or reduced dataset to the same model.
