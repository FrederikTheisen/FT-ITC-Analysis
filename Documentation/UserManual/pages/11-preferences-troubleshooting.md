---
title: Settings and defaults
summary: Configure shared defaults for display, processing, fitting, and export.
slug: preferences-troubleshooting
nav_order: 11
last_verified: 2026-08-27
_verification:
  product_version: "1.5.0"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Settings and defaults

**Preferences...** contains **General**, **Processing**, **Fitting**, and **Export**. Settings labelled as defaults provide starting values for new or reset work. Display settings affect presentation; export settings affect generated tables and figures. Project-specific values stored in a project remain distinct from application preferences. Project and recovery behavior is covered in [Installation, files, and projects](03-installation-files-projects.md).

**Restore Defaults** stages the built-in values in the window. **Apply** saves the staged values as application preferences. **Cancel** closes the window without saving staged edits.

## General

| Setting | Effect |
| --- | --- |
| **Energy units** | Select **Joule** or **Calories**. Displayed values automatically use the base or kilo prefix from the finite central values being shown; empty groups default to kJ or kcal. |
| **Concentration unit** | Shared default unit for concentration entry, parameter display, and supported result-table concentration fields; format-specific data exports use their documented units. |
| **Designer instrument** | Shared default instrument for the Experiment Designer and its instrument-specific volumes. |
| **Number precision** | Controls numeric presentation: **Strict**, **Standard**, **Single decimal**, or **All decimals**. It does not set export decimal places. |
| **Uncertainty display** | Shared uncertainty presentation: **Automatic**, **Standard deviation**, **Confidence interval**, **SD + confidence interval**, or **None**. Automatic uses the confidence interval for a quantity whose stored 95% interval is materially asymmetric around its best-fit value; otherwise it uses SD. This changes presentation, not the underlying fit or calculated uncertainty. |
| **Reference temperature (°C)** | Temperature used where a result or derived quantity is evaluated at a reference temperature. |
| **Minimum temperature span (°C)** | Minimum temperature variation required for temperature-dependent result analyses. |
| **Minimum salt span (mM)** | Minimum ionic-strength variation required for salt-dependent result analyses. |
| **Include buffer in ionic-strength calculation** | Includes buffer contribution when ionic strength is calculated. |
| **Check for updates and online resources on launch** | Controls launch-time online checks; it does not disable local analysis. |
| **Confirm remove/delete actions** | Controls confirmation before remove or delete commands. |
| **Automatically discard injections outside the thermogram range** | Controls automatic removal of orphan injections when data are loaded. |
| **Enable autosave** | Enables periodic recovery copies. |
| **Interval (minutes)** | Sets the autosave interval. The built-in default is **5 minutes**. |
| **Maximum files** | Sets the number of autosave files retained. The built-in default is **10 files**. |
| **Prompt to recover after an interrupted session** | Controls whether the application presents an available autosave recovery after an interrupted session. Recovery prompting is enabled by default. |
| **Open Autosave Folder** | Opens the folder containing autosave recovery files. |

Energy family, concentration unit, number precision, and uncertainty presentation are shared application settings where the corresponding control is available. The energy family does not change internal joule storage or format-specific interchange exports. Automatic display uses kJ or kcal when a value group is empty, zero, non-finite, or at/above the 100-unit threshold; fixed publication/result-export overrides are configured in their respective dialogs.

## Processing

| Setting | Effect |
| --- | --- |
| **Dilution method** | Sets the default dilution correction model: **MicroCal** or **Exponential**. |
| **Buffer subtraction** | Sets the default buffer-subtraction model: **Matched**, **Linear**, or **Exp. decay**. |
| **Discard integration regions for baseline** | Controls whether existing integration regions are excluded from baseline construction. |
| **Reprocess integrated heats on load** | For `.dat` and `.aff` imports, recalculates the injection concentrations and ratios from the imported injection volumes and experiment concentrations. It does not create a thermogram or repeat baseline correction and peak integration. |
| **Point density** | Sets the default spline point density: **Sparse**, **Balanced**, or **Dense**. |
| **Handle mode** | Sets the default spline handle calculation: **Mean** or **Median** in the preferences window. |
| **Allow spline point time dragging by default** | Controls whether spline points can be moved in time by default. |
| **Copy integration start with selected region** | Controls whether copying an integration region also copies its start time. |

Processing preferences provide defaults for new processors. Processing values already stored with an experiment are not replaced merely by changing a preference.

## Fitting

| Setting | Effect |
| --- | --- |
| **Default solver** | Sets the starting optimizer: **Nelder-Mead [SIMPLEX]** or **Levenberg-Marquardt**. |
| **Error estimation** | Sets the default uncertainty method: **None**, **Bootstrap residuals**, or **Leave-one-out**. The built-in method is **Bootstrap residuals**. |
| **Bootstrap iterations** | Sets the number of residual-bootstrap refits. Leave-one-out uses one refit per deletion and ignores this count. The built-in count is **100**. |
| **Optimizer tolerance** | Sets the solver tolerance preset: **Fast**, **Relaxed**, **Balanced**, **Strict**, or **Very Strict**. The built-in default is **Balanced**. |
| **Max iterations** | Sets the maximum number of optimizer iterations. |
| **Parameter limits** | Sets the default parameter-limit policy: **Standard**, **Extended**, or **No limit**. |
| **Use injection-error weighted fitting** | Controls weighting of injection observations by their estimated errors. |
| **Include concentration uncertainty in bootstrap** | Includes concentration uncertainty in residual-bootstrap resampling. Leave-one-out keeps concentrations fixed. |
| **Automatic concentration SD (%)** | Sets the automatic fractional concentration SD used when concentration-uncertainty handling is enabled. |
| **Create single-experiment analysis result** | Controls creation of an Analysis Result after a usable single-experiment fit. Disabled in the built-in defaults. |
| **Create global analysis result** | Controls creation of a combined Analysis Result after a usable multiple-experiment fit. Enabled in the built-in defaults. |
| **Auto-open new analysis result** | Controls whether a newly created result is opened automatically. |

Bootstrap method and count are shared fitting defaults. Fit-specific settings captured in an Analysis Result remain part of that result. See [Single-experiment fitting](06-fitting-models.md) for model and uncertainty interpretation.

## Export

| Setting | Effect |
| --- | --- |
| **Selection** | Sets the default export scope: **Selected experiment**, **Active experiments**, or **All experiments**. |
| **Decimals** | Sets the number of decimal places in exported numeric tables. |
| **Export baseline-corrected data** | Includes baseline-corrected data in data exports. |
| **Export fit points with peaks** | Includes fitted peak points with exported peak data. |
| **Molar ratio** | Includes molar-ratio values in exported tables. |
| **Injection info** | Includes injection volume, delay, peak error, integration length, and temperature columns. |
| **Concentrations** | Includes cell and syringe concentration columns. |
| **Included state** | Includes the injection-inclusion state. |
| **Peak heats** | Includes integrated peak heats. |
| **Fit values** | Includes fitted values. |
| **Width cm** and **Height cm** | Set the default final-figure dimensions. The built-in dimensions are **6.5 × 10 cm**. |
| **Publication font** | Selects the publication figure font where the platform provides this selector. |
| **Show residual graph** | Includes the residual graph in final figures. |
| **Show residual graph gap** | Includes the gap separating residual and fit panels. |
| **Unify residual graph axis** | Uses a common residual-axis scale across applicable panels. |
| **Fit line** | Sets fit-line smoothing: **Smooth**, **Spline**, or **Linear**. |
| **Show parameter box by default** | Includes the parameter information box in new final figures. |
| **Show experiment details by default** | Includes experiment metadata in the figure information box. |
| **Show model info by default** | Includes model information in the figure information box. |
| **Auto axes ignore excluded/bad points** | Excludes points marked excluded or bad when automatic axes are calculated. |
| **Thermodynamic parameters** | Includes thermodynamic parameters in final-figure information. |
| **Offset parameter** | Includes the fit offset in final-figure information. |
| **Derived parameters** | Includes derived parameters in final-figure information. |
| **Temperature** | Includes temperature in final-figure information. |
| **Concentrations** | Includes concentrations in final-figure information. |
| **Injection delay** | Includes injection delay in final-figure information. |
| **Instrument** | Includes instrument information in final-figure information. |
| **Attributes** | Includes experiment attributes in final-figure information and limits their display to **Used in analysis**, **All**, or **None**. |

Export preferences affect newly generated exports and figure defaults; they do not rewrite an existing export or a stored figure configuration. See [Figures and export](09-figures-printing-export.md) for output formats and units.

> **Platform note:** The **Publication font** selector is available on Windows and Linux, with choices of **Native**, **Inter**, and **Liberation Sans**. Some macOS releases use the native publication renderer and do not show this selector.
