---
title: Processing thermograms
summary: Choose and refine baselines, set integration regions, manage injections, and judge whether processing is defensible.
slug: processing-thermograms
nav_order: 5
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Processing thermograms

Processing subtracts an estimated baseline from differential power and integrates the corrected response within each injection region. It produces an injection heat and estimated uncertainty for fitting. It does not decide whether the baseline, integration window, or experiment is scientifically appropriate.

Integrated-heat imports do not contain a thermogram and therefore do not use this workflow.

## Prepare the experiment

Before processing, open **Details...** and confirm temperature, concentrations, injection schedule, and instrument metadata. Then open **Process Data** and inspect the complete trace before zooming into individual injections.

Look for equilibration drift, discontinuities, unusually shaped peaks, failed injections, and changes in noise. These observations guide baseline choice and identify injections that deserve closer review.

## Choose a baseline

### Spline

**Spline** places control points in usable baseline regions and interpolates between them. It provides the most direct graphical control.

- **Linear** connects neighboring control points without smooth curvature.
- **Smooth** interpolates a continuous smooth baseline through the controls.
- **Sparse**, **Balanced**, and **Dense** adjust the target point density.
- **Mean**, **Median**, and **Min volatility** determine how a representative local handle value is obtained.

Show spline points when reviewing the fit. Add or remove a point when an interval needs more or less control; drag a point to correct its value. Lock an individual point when it should survive later automatic processing changes.

> **Recommendation:** Start with **Balanced** density. Add complexity only where the baseline trace supports it. Dense control points can follow noise or absorb genuine injection signal.

### Polynomial

**Polynomial** fits one polynomial across the thermogram. Points with large residuals are iteratively rejected according to the z-limit, helping prevent injection peaks from dominating the fit. Polynomial degree controls global flexibility.

Use this baseline when drift is smooth across the entire run. Inspect both ends of the trace, where high-degree polynomials can behave poorly. A lower degree is easier to justify; a higher degree should solve a visible baseline problem rather than merely reduce residuals.

### Segmented

**Segmented** fits local constant, linear, or quadratic behavior around integration regions and evaluates a piecewise baseline through the run. It is useful when drift changes locally or a single global polynomial is too rigid.

Check continuity and the baseline immediately before and after each peak. Local flexibility can accommodate real drift but can also hide a poorly chosen integration region.

### Convert to spline

Convert a polynomial or segmented baseline to **Spline** when the automatic result is a good starting point but needs point-level editing. The conversion changes the editable representation; review the whole trace again before accepting it.

## Control which data establish the baseline

By default, data inside integration regions are excluded from baseline fitting. The **Discard integrated regions** setting controls this behavior. Because the baseline and integration windows are coupled, moving a boundary can change both the area of integration and the baseline estimate.

> **Caution:** Do not tune boundaries solely to obtain expected heats. Boundaries should follow the thermogram response and a consistent processing rule.

## Set integration regions

Each injection has a start delay and an end offset within its injection scope. Use global controls when all or most injections need the same rule, and graphical markers when an injection needs an individual adjustment.

The supported integration-length modes are:

- **Time** - use a common time length.
- **Factor** - estimate a peak-dependent length from injection shapes and apply a scale factor.
- **Fit** - fit injection peak shapes and use the fitted behavior to estimate end offsets.

End times are constrained to the injection scope and a minimum interval. If a requested window is outside those limits, the application uses a valid interval instead.

### Review injection by injection

Zoom to an individual injection and confirm that the region starts at the intended response and ends after the signal has returned sufficiently toward baseline. When the keyboard shortcut is active, **Space** copies the current integration length to the next injection and advances the view. Always confirm the next region rather than assuming identical kinetics.

Review the first injection separately. A small first injection can behave differently because of syringe diffusion, backlash, or experimental design. Exclude it from fitting only for an identified reason.

## Understand injection uncertainty

The application estimates uncertainty from corrected baseline noise around an injection and the number of corrected samples in its integration region. The estimate uses robust noise handling and an autocorrelation correction before combining integration and baseline-level contributions.

Display the uncertainty bars and compare them across the run. Longer regions normally collect more baseline noise; an unstable baseline can increase uncertainty. Weighting a fit by injection error uses these estimates, so inspect them before enabling weighting.

> **Interpretation:** Error bars describe estimated measurement uncertainty under the selected processing. They do not account for every systematic error, and they do not validate the baseline model.

## Copy processing

Use the processing-copy command when multiple experiments were collected with sufficiently similar timing and response characteristics. Copying is a starting point, not confirmation that every baseline and integration region is suitable.

After copying:

1. Inspect the entire destination thermogram.
2. Check baseline controls and every integration region.
3. Compare uncertainty behavior.
4. Reprocess and save only after review.

## Lock processing

Lock the processor after the baseline and integration regions are accepted. A processing lock freezes the result and disables integration-region and spline-point editing. Unlock it to revise the processing.

Locking protects against accidental edits; it does not certify the analysis. If the processor is locked and controls appear unavailable, check the lock state before diagnosing a software problem.

## Include or exclude injections

Injection inclusion affects fitting. Exclude an injection when the raw trace or experimental record identifies a delivery, equilibration, or integration problem. Use the display to confirm which points are omitted.

> **Recommendation:** Fit with and without a questionable injection and record the reason for the final choice. A large residual alone is evidence to investigate, not an automatic exclusion rule.

## Diagnose the processed result

Before fitting, ask:

- Does the corrected thermogram return sensibly toward zero between injections?
- Are baseline transitions or oscillations introduced by the chosen method?
- Do integration regions follow a consistent experimental rule?
- Are uncertainty bars plausible relative to visible noise?
- Are discontinuities associated with known events?
- Is the heat trend stable under small, defensible changes in processing?

If not, revisit the simplest relevant choice: details, excluded baseline regions, baseline family, baseline flexibility, or integration boundaries. Do not use model fitting to compensate for unresolved thermogram processing.

## Preserve processing decisions

Save the project as `.ftxtc` after processing. The project stores the configured baseline, integration state, raw and corrected peak information, optional buffer subtraction, and inclusion state. Export integrated peaks when you need a human-readable injection table, but retain the project for a reproducible continuation.

