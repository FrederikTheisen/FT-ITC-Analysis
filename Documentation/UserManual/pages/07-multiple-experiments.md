---
title: Multiple-experiment analysis
summary: Select compatible experiments, define parameter constraints, run a global fit, and judge combined and member results.
slug: multiple-experiments
nav_order: 7
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Multiple-experiment analysis

**Multiple experiments** fits selected experiments together. It is useful for replicates, condition series, and systems in which selected parameters should be shared or described across temperature. It creates a combined Analysis Result with member fits.

Global fitting is not automatic averaging. Every constraint states a scientific relationship between experiments and changes the model being tested.

## Prepare a coherent set

Before configuring the fit:

1. Load every intended experiment.
2. Check cell and syringe concentrations, uncertainties, temperature, injection volumes, and attributes.
3. Process each thermogram independently and inspect its uncertainty bars.
4. Set experiment and injection inclusion deliberately.
5. Give experiments clear names and save a project checkpoint.

Choose experiments that can defensibly share the selected model. Similar-looking curves are not sufficient if sample composition, construct, protonation state, or competing species differ.

## Select experiments

Open **Analyze Data**, choose **Multiple experiments**, and select the experiments for the fit. Confirm the member list before running.

The project-level enabled state and the analysis member selection can both matter. If an expected dataset is missing, check whether it is loaded, enabled, processed, and compatible with the selected model.

## Define parameter relationships

The available constraint states include:

- **Free** - estimate a separate value for each experiment.
- **Shared** - estimate one common value across the selected experiments.
- **Fixed** - hold the value at the specified input.
- **Temperature-dependent** - describe values across temperature using the relationship exposed by the model.

Not every parameter or model exposes every state. The interface disables relationships that are not supported for the current configuration.

### Free parameters

Use a free parameter when the quantity can vary independently between experiments. This preserves flexibility but increases the number of values the data must constrain.

### Shared parameters

Use a shared parameter when the same physical value is a justified assumption across the entire set. Replicate experiments at matched conditions are a common case, subject to the experimental design.

> **Caution:** Sharing a parameter can make an imprecise dataset appear more precise by borrowing information from others. The assumption must be reported with the result.

### Fixed parameters

Fix a parameter when a value is known independently or deliberately imposed for model identifiability. Record its source and uncertainty outside the result table; a fixed value is not estimated by the fit.

### Temperature-dependent parameters

Use a temperature-dependent relationship only for a suitable temperature series and a model that exposes it. Confirm temperatures and their spread before fitting. The selected reference or evaluation temperature affects how parameters are presented, not the raw measurements.

## Configure weighting and uncertainty

Weighting uses each experiment's injection uncertainties. Check processing consistency across the set before enabling it. A single experiment with unrealistically small errors can dominate a global result.

Bootstrap residuals and leave-one-out analysis repeat the combined fit. They can be computationally intensive and expose weak constraints through failed or widely dispersed refits. Include concentration uncertainty only when the entered uncertainty values are meaningful for all members.

## Run the fit

Choose the model, constraints, initial values, parameter limits, optimizer, weighting, and error-estimation method. Then choose **Run Fit** and create an Analysis Result.

If convergence fails, simplify the constraint pattern first. A highly parameterized global model can be less identifiable even though it contains more data.

## Review the combined result

Select the Analysis Result and inspect:

- the exact member experiments;
- model and model options;
- free, shared, fixed, and temperature-dependent constraints;
- convergence, loss, weighting, and error method;
- combined parameter table and uncertainty display;
- every member fitted curve and residual plot.

A satisfactory combined loss can conceal a poor member fit. Review all experiments, especially those with fewer informative points or unusual uncertainty.

> **Interpretation:** Structured residuals shared across experiments suggest a model deficiency or systematic processing issue. Structure in only one member suggests an experiment-specific issue or an unjustified shared constraint.

## Check fit validity

An Analysis Result retains a snapshot of inputs that affect the fit. Editing experiment data, processing, concentrations, attributes, inclusion state, or fit settings can make the result invalid for the current project.

When a result is invalid:

1. Identify the changed input.
2. Decide whether to revert it or refit.
3. Run the configured analysis again.
4. Update or replace the stored result as appropriate.
5. Regenerate dependent figures and exports.

Do not use **Update Result** merely to silence a warning; the current data and constraints must actually have been fitted.

## Compare alternative constraint models

Duplicate experiments only for processing comparisons, not to create artificial replicates. For alternative global models, retain distinct Analysis Results with descriptive names or comments. Compare:

- parameter plausibility and stability;
- member residuals;
- limit contacts and failed resamples;
- whether the additional constraint or parameter has a scientific basis;
- predictive or held-out behavior when available.

Do not select a model solely from the smallest loss when the candidates have different complexity.

## Export the result

Use **Analysis Result Exporter...** for one summary row per result or member-level rows. Include the chosen uncertainty representation and enough identifiers to reconstruct the experiment set. Use **Export Associated Final Figures...** to export figures linked to the selected result.

