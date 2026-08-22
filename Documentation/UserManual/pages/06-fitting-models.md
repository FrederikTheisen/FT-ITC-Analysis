---
title: Fitting models
summary: Select a supported binding model, configure fitting and uncertainty options, run the fit, and diagnose common model problems.
slug: fitting-models
nav_order: 6
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Fitting models

Open **Analyze Data**, select **Single experiment** or **Multiple experiments**, and choose a model. The fit uses processed integrated heats for enabled injections together with experiment concentrations, volumes, temperature, and applicable attributes.

## Choose the model

### One-Set-Of-Sites

Use **One-Set-Of-Sites** for one class of equivalent, non-interacting sites under the model assumptions. It is the best starting point when the data and system support a single transition.

Typical outputs include stoichiometry, affinity or dissociation behavior, and binding enthalpy, with derived free-energy and entropy terms where applicable.

### Two-Sets-Of-Sites

Use **Two-Sets-Of-Sites** for two fitted classes of independent sites. This model has more adjustable parameters and therefore requires substantially more information in the titration curve.

> **Caution:** A lower loss does not by itself justify the second site. Check whether both transitions are supported, parameters are identifiable, results are stable, and the system provides a plausible interpretation.

### Competitive Binding

Use **Competitive Binding** when a higher-affinity interaction is measured by titrating into a preformed competing complex. Enter the relevant competitor or prebound-species details and fixed knowledge required by the interface.

The useful affinity range depends on the chosen competition conditions. Incorrect competitor concentration or affinity assumptions can produce a convincing but biased result.

### Dissociation

Use **Dissociation** when the injected material is a preformed complex that dissociates under the modeled titration conditions. Confirm the preparation, species definitions, and concentration entries match this experiment rather than an association titration.

## Set initial values

Initial values tell the optimizer where to begin. Estimate them from the experiment and known chemistry at the correct unit scale.

- Use the transition position and concentration ratio to guide stoichiometry and affinity.
- Use the sign and scale of integrated heats to guide enthalpy.
- For a two-site or competitive model, avoid starting two processes at numerically indistinguishable values unless that is deliberate.

Run from more than one defensible starting point when the model is complex. Agreement improves confidence that the same solution basin was found; disagreement reveals sensitivity that should be reported or resolved.

## Choose parameter limits

The interface provides standard, expanded, or no limit policies, together with model-specific fixed values or bounds.

Start with standard limits. Expand a limit only when independent knowledge supports the parameter range and the data can constrain it. Removing limits can expose a numerical solution but can also allow physically meaningless regions.

> **Interpretation:** A fitted value pressed against a limit is not a normal interior estimate. Treat it as a diagnostic of the data, starting values, model, or constraint.

## Choose an optimizer

- **Levenberg-Marquardt** uses local derivative information and is efficient near a suitable solution.
- **Nelder-Mead** is a derivative-free simplex method and can be useful when the local surface or starting point is less favorable.

Try the other optimizer when a fit fails, converges at a limit, or is sensitive to initial values. Comparable solutions from both optimizers are reassuring, but model adequacy still requires scientific review.

## Weight by injection error

Enable **Weight by injection error** to give observations influence according to their estimated integration uncertainty. This is appropriate only when those uncertainties are meaningful and comparable.

Compare weighted and unweighted fits when uncertainty varies strongly. A major shift can be informative: inspect which injections dominate and whether their errors reflect signal quality or a processing artifact.

## Estimate error

Fitting chooses a best solution. Error estimation repeatedly refits perturbed or reduced data; it does not replace the best-fit parameters with an average.

- **None** reports the fitted solution without resampling-based uncertainty.
- **Bootstrap residuals** resamples residual behavior and refits to estimate a parameter distribution.
- **Leave-one-out** refits while omitting observations in turn to show sensitivity to individual points.

Set an adequate number of iterations for the intended precision and available time. Review how many refits succeeded. A distribution based on many failed or limit-bound refits is a warning, not a reliable uncertainty statement.

### Include concentration uncertainty

When enabled, concentration uncertainties from experiment details are propagated in the resampling workflow. Enter those uncertainties before fitting and use values supported by preparation and assay records.

> **Interpretation:** Concentration uncertainty can dominate stoichiometry and affinity behavior. Entering zero is a substantive assumption, not merely leaving a field blank.

## Use syringe correction

Models that expose **Use Syringe Correction** can fit a syringe-side active-concentration correction. In this mode the reported factor is `alpha` in result tables and figures, while the configured site stoichiometry is fixed as required by the model option.

Use it when the principal concentration uncertainty is believed to be in syringe material. The ordinary fitted stoichiometry is more appropriate when the conventional interpretation and cell-side concentration uncertainty apply.

> **Caution:** Syringe correction does not identify which preparation is wrong. It changes how the model represents a concentration correction and must be justified externally.

## Run and evaluate the fit

Choose **Run Fit**, then inspect:

1. convergence state and fit loss;
2. the curve over the complete concentration range;
3. residuals for structure, drift, or outliers;
4. parameter values, units, limits, and correlations implied by instability;
5. uncertainty distributions or intervals, when calculated;
6. sensitivity to initial values, optimizer, weighting, and defensible injection inclusion.

Choose the option to create an **Analysis Result** when you need a stored combined result, advanced analysis, result export, or a durable validity snapshot.

## When a fit fails

Work through these checks in order:

1. Confirm concentrations, temperature, units, and injection volumes.
2. Confirm baseline, integrated heats, uncertainties, and included injections.
3. Confirm the experiment actually spans a transition informative for the chosen parameters.
4. Use plausible initial values and standard limits.
5. Try the alternate optimizer.
6. Simplify the model or constraints.
7. Expand limits only with a scientific reason.

Do not interpret a numerical solution until the fitted curve, residuals, and parameters all support it.

