---
title: Settings and defaults
summary: Configure shared defaults for display, processing, fitting, and export.
slug: preferences-troubleshooting
nav_order: 11
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# Settings and defaults

Preferences set the starting behavior for future work, such as display units, processing choices, and export formatting. Changing a default does not by itself redo an existing fit or processing decision saved in a project.

Open **Preferences...**, edit the relevant tab, and choose **Apply**. Defaults initialize new or reset work; display settings change presentation and export settings change output. Saved project settings remain distinct. See [Installation, files, and projects](03-installation-files-projects.md) for saving and recovery.

**Restore Defaults** stages the built-in values in the window. **Apply** saves the staged values as application preferences. **Cancel** closes the window without saving staged edits.

**Apply** saves only the settings you edited and updates their related views. Untouched settings retain changes made elsewhere while Preferences was open. **Restore Defaults** explicitly stages every built-in preference, including settings not shown in that application's window.

## General

| Setting | Effect |
| --- | --- |
| **Enable Traceability Mode** | Requires an operator name. While enabled, FT-ITC asks you to confirm the operator once at each launch; see [Traceability Mode at startup](03-installation-files-projects.md#traceability-mode-at-startup). Off by default. |
| **Operator name** | Name captured on newly created or successfully updated Analysis Results and when a report preview is generated. Existing results and a current preview keep their saved attribution. Required while Traceability Mode is enabled. |
| **Prompt for experiment and sample IDs after import** | While Traceability Mode is enabled, asks for experiment, cell sample, and syringe sample IDs after data files are imported. All IDs are optional. The setting is kept, but has no effect, while the mode is off. |
| **Energy units** | Select **Joule** or **Calories**. Displayed values automatically use the base or kilo prefix according to the magnitude of the displayed central values; empty groups default to kJ or kcal. |
| **Concentration unit** | Shared default unit for concentration entry, parameter display, and supported result-table concentration fields; format-specific data exports use their documented units. |
| **Temperature units** | Sets the Celsius or Kelvin default for Results and Analysis Result Exporter tables. Each keeps its own choice for the current application session. Changing this preference resets both choices immediately. |
| **Designer instrument** | Shared default instrument for the Experiment Designer and its instrument-specific volumes. |
| **Number precision** | Controls numeric presentation: **Strict**, **Standard**, **Single decimal**, or **All decimals**. Under every setting, an estimate, SD, or confidence-interval bound with a magnitude of 10¹⁰ or more is shown in scientific notation with up to six significant digits, and an infinite bound is shown as ∞ or −∞. This changes presentation only. It does not set export decimal places. |
| **Uncertainty display** | Shared uncertainty presentation: **Automatic**, **Standard deviation**, **Confidence interval**, **SD + confidence interval**, or **None**. Automatic uses the confidence interval for a quantity whose stored 95% interval is materially asymmetric around its best-fit value; otherwise it uses SD. This changes presentation, not the underlying fit or calculated uncertainty. |
| **Reference temperature (°C)** | Temperature used where a result or derived quantity is evaluated at a reference temperature. It is also the proposed experiment temperature when importing `.dat` and `.aff` files, which do not record one. |
| **Minimum temperature span (°C)** | Minimum temperature variation required for temperature-dependent result analyses. |
| **Minimum ionic-strength span (mM)** | Salt analysis requires three ionic strengths, selected in increasing order with each more than half this span above the previous selected value. |
| **Include buffer in ionic-strength calculation** | Includes buffer contribution when ionic strength is calculated. |
| **Check for updates and online resources on launch** | Controls launch-time online checks; it does not disable local analysis. |
| **Confirm remove/delete actions** | Controls confirmation before remove or delete commands. |
| **Enable autosave** | Enables periodic recovery copies. |
| **Interval (minutes)** | Sets the autosave interval. The built-in default is **5 minutes**. |
| **Maximum files** | Sets the number of autosave files retained. The built-in default is **10 files**. |
| **Prompt to recover after an interrupted session** | Controls whether the application presents an available autosave recovery after an interrupted session. Recovery prompting is enabled by default. |
| **Open Autosave Folder** | Opens the folder containing autosave recovery files. |

Display units do not change interchange-format units. Automatic energy scaling uses kJ or kcal for empty, zero, non-finite, or at-least-100-unit groups. Fixed figure and result-export overrides are selected in their dialogs; see [Figures and export](09-figures-printing-export.md).

## Processing

| Setting | Effect |
| --- | --- |
| **Injection bookkeeping** | Selects **Discrete displacement** (default and recommended starting point for ordinary pulse injections), **Ideal continuous mixing** (for slow injections or opposite-limit comparisons), or **MicroCal** (to reproduce or compare MicroCal analyses) for new data. Change an existing ordinary experiment explicitly in Experiment Details; rebuild a tandem experiment to change its method. Saved fits are not automatically migrated. |
| **Buffer subtraction** | Sets the default buffer-subtraction model: **Matched**, **Linear**, or **Exp. decay**. |
| **Discard integration regions for baseline** | Controls whether existing integration regions are excluded from baseline construction. |
| **Recalculate concentrations on integrated-heat import** | Sets the initial **Recalculate concentrations and ratios** choice for `.dat` and `.aff` imports. You can change it for an import without changing this preference. Recalculation uses the imported injection volumes and experiment concentrations; it does not create a thermogram or repeat baseline correction and peak integration. `.dh` concentrations are always calculated. |
| **Point density** | Sets the default spline point density: **Sparse**, **Balanced**, or **Dense**. |
| **Handle mode** | Sets the default spline handle calculation: **Mean** or **Median** in the preferences window. |
| **Allow spline point time dragging by default** | Controls whether spline points can be moved in time by default. |
| **Copy integration start with selected region** | Controls whether copying an integration region also copies its start time. |

For baseline, integration, and locking choices, see [Processing](05-processing-thermograms.md). For method assumptions, see [Injection bookkeeping](06-fitting-models.md#injection-bookkeeping-microcal-and-dumas). Stored experiment processing is not replaced by changing defaults.

## Fitting

| Setting | Effect |
| --- | --- |
| **Default solver** | Sets the starting optimizer: **Nelder-Mead [SIMPLEX]** or **Levenberg-Marquardt**. |
| **Error estimation** | Sets the default uncertainty method: **None**, **Bootstrap residuals**, **Leave-one-out**, or **Profile likelihood**. |
| **Bootstrap iterations** | Sets the number of residual-bootstrap refits. Leave-one-out uses one refit per deletion and profile likelihood does not use this count. |
| **Optimizer tolerance** | Sets the solver tolerance preset: **Fast**, **Relaxed**, **Balanced**, **Strict**, or **Very Strict**. |
| **Max iterations** | Sets the maximum number of optimizer iterations. The built-in default is **20,000**, including after **Restore Defaults** is applied. |
| **Parameter limits** | Sets the default parameter-limit policy: **Standard**, **Extended**, or **No limit**. |
| **Use injection-error weighted fitting** | Controls weighting of injection observations by their estimated errors. |
| **Include concentration uncertainty in bootstrap** | Includes concentration uncertainty in residual-bootstrap resampling. Leave-one-out and profile likelihood keep concentrations fixed. |
| **Create single-experiment analysis result** | Controls creation of an Analysis Result after a usable single-experiment fit. |
| **Create global analysis result** | Controls creation of a combined Analysis Result after a usable multiple-experiment fit. |
| **Auto-open new analysis result** | Controls whether a newly created result is opened automatically. |

Concentration uncertainty and parameter unlocking apply only to residual bootstrap. See [Single-experiment fitting](06-fitting-models.md) for model and uncertainty interpretation.

Saving a project or applying Preferences preserves the Analysis inspector's solver, uncertainty method, bootstrap count, weighting, and concentration-uncertainty choices. Defaults for these choices are loaded at application startup or when you choose **Restore defaults** in Analysis.

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

The energy-unit preference also sets the initial molar-energy unit in the Integrated Peaks, Combined Data, and ITCsim export dialogs: kJ/mol for Joules or kcal/mol for Calories. You can choose another unit for each export without changing the preference.

> **Platform note:** The **Publication font** selector is available on Windows and Linux, with choices of **Native**, **Inter**, and **Liberation Sans**. Some macOS releases use the native publication renderer and do not show this selector.

## Interpretation

To request an account with additional interpretation options, use the [FT-ITC interpretation service registration page](https://ft-itc.org/register). If eligible, submit your name and email, accept the Terms and acknowledge the Privacy Notice, then verify your email; the access code is sent separately by email. Keep the code confidential. If access-code delivery fails, resubmit the form with the same email address to retry delivery of the same code; a resend cooldown applies. Contact `support@ft-itc.org` if the code is exposed or still does not arrive.

Without a verified account code, automated interpretation uses Public access and the **Fast** interpretation preset.

Enter the account code and choose **Verify Access** to load the options available to your account. When additional presets are available, choose one from **Interpretation depth**. Preferences show available account details and remaining quota. The code stays masked and is checked when an interpretation is generated.
