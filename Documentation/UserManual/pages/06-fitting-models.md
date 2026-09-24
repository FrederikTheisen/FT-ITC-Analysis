---
title: Single-experiment fitting
summary: Fit one experiment, configure model and uncertainty options, control injection inclusion, and interpret fit diagnostics.
slug: fitting-models
nav_order: 6
last_verified: 2026-09-23
_verification:
  product_version: "1.5.0"
  commit: "04340db8d6baf1d322efb9629b0f9349d7ab4663"
---

# Single-experiment fitting

After processing a thermogram or importing integrated heats, you have one heat value per injection. A binding model predicts those heats from quantities such as affinity and binding enthalpy. **Analyze Data** adjusts the model parameters to bring its predicted heats close to the observed heats. In **Single experiment** mode, it uses the included injections from the selected Experiment Data, along with the concentrations, injection volumes, cell volume, temperature, and any model-specific information.

The fitted values describe this dataset under the selected model. Check the pattern of differences between prediction and observation (**residuals**) and whether the fitted values make scientific sense; a successful optimizer stop alone is not a model check.

The inspector has four tabs:

- **Fit** selects the model, optimizer, error-estimation method, limits, weighting, and result output.
- **Parameters** shows the model parameters and their starting or fixed values.
- **Options** contains settings specific to the selected model.
- **Display** controls the fitted curve and diagnostic information shown in the graph.

![Analyze Data workspace showing a fitted one-set-of-sites curve, residuals, fitting controls, and fit status.](../assets/fitting-workspace.png)

*Analyze Data combines the fitted-heats graph and residuals with the controls for configuring and running the fit.*

Multiple-experiment fitting uses additional experiment selection and parameter constraints; see [Multiple-experiment fitting](07-multiple-experiments.md).

## Profile-likelihood uncertainty

**Profile likelihood** estimates parameter uncertainty by asking how far a fitted parameter can be moved away from its best-fit value before the agreement with the experimental data becomes significantly worse. Profile likelihood does not create a refit ensemble or a confidence band.

For each fitted parameter, FT-ITC first starts from the best-fit solution. The parameter of interest is then fixed at a different value, while all other fitted parameters are allowed to readjust. This is repeated in both directions from the best-fit value. The resulting profile therefore accounts for compensation between parameters: for example, a change in affinity may partly be compensated by changes in enthalpy, stoichiometry, or other fitted parameters.

The 95% confidence limits are the parameter values at which the best achievable fit reaches a statistically defined 95% threshold. FT-ITC uses an F-calibrated threshold based on the number of observations and fitted parameters. The confidence interval is therefore determined by the shape of the fit surface rather than by assuming that parameter uncertainty is symmetric or normally distributed.



A complete interval is reported only when the threshold is crossed on both sides of the best-fit value. If the search reaches a parameter limit first, that side is recorded as censored and has no confidence endpoint. For a complete interval, the displayed `value ± SD` uses a symmetric display equivalent calculated from the asymmetric interval; it is not a sample standard deviation. Save the result in an `.ftxtc` project to preserve the profile diagnostics.

## Injection inclusion

Selecting an injection point in the integrated-heats graph changes whether it is included in the fit. Excluded injections do not contribute to the objective function.

![Analyze Data graph with excluded injections visible and the Display controls for the fit and its diagnostics.](../assets/fitting-injection-inclusion.png)

*Excluded injections remain visible when Excluded points is enabled and can be selected for inclusion again.*

Changing injection inclusion does not rerun the fit. The fitted curve and parameters continue to represent the previous fit until **Run Fit** is used again. An inclusion change can also invalidate an Analysis Result that contains the experiment.

<a id="injection-bookkeeping-microcal-and-dumas"></a>

## Injection bookkeeping: MicroCal, Ideal continuous mixing, and Discrete displacement

Injection bookkeeping determines the concentrations used by the fit and accounts for reaction heat carried out of the active cell by displaced solution. It does not change measured peak areas or baseline integration.

- **MicroCal** reproduces the documented displaced-volume convention, including its approximate ligand concentration expression alongside its rational retained-cell curve and endpoint displacement correction. Use it to reproduce or compare MicroCal analyses.
- **Ideal continuous mixing** models concentrations with ideal exponential mixing and accounts for displaced heat along the continuous mixing trajectory. It uses the exponential concentration law previously exposed as the Exponential preference, but its heat calculation is different. This is the convention previously labelled Dumas in the user interface.
- **Discrete displacement** is the default and recommended starting point for ordinary pulse injections. It represents the physical limiting case where injection displaces the previous cell mixture before appreciable mixing, then the remaining material mixes with the injected solution. This follows the discrete-injection formalism described by [Freire, Schön and Velazquez-Campoy (2009)](https://doi.org/10.1016/S0076-6879(08)04205-5), also implemented by pytc.

For MicroCal, let *u* = cumulative injected volume / active cell volume, *M*₀ be the initial cell concentration, and *C*ₛ the syringe concentration. Starting with no ligand in the cell:

> *M* = *M*₀(1 − *u*/2)/(1 + *u*/2)<br>
> *X* = *C*ₛ*u*(1 − *u*/2)

Every delivered injection contributes to cumulative volume, including injections excluded from fitting. The ligand equation is the approximate expression used by FT-ITC for MicroCal processing. The displaced-volume assumptions remain approximate. Existing projects retain saved concentrations and fits until concentration reprocessing.

For one injection, let *v* be injection volume, *V* active cell volume, and *Q* the equilibrium binding heat content of the cell in joules. Ordinary binding models use

> MicroCal: *q* = *Q*end − *Q*start + (*v*/*V*)(*Q*start + *Q*end)/2<br>
> Ideal continuous mixing: *q* = *Q*end − *Q*start + (*v*/*V*)(*Q*start + 4*Q*mid + *Q*end)/6<br>
> Discrete displacement: *q* = *Q*end − (1 − *v*/*V*)*Q*start

For Discrete displacement, concentrations advance as *M*end = (1 − *v*/*V*)*M*start and *X*end = (1 − *v*/*V*)*X*start + (*v*/*V*)*C*ₛ. Across a fixed-syringe segment, the retained fraction is the product of each injection's (1 − *v*/*V*), not an exponential of cumulative volume. Every shot must be smaller than the cell volume; cumulative injected volume can exceed it. Zero-volume steps leave the state unchanged. No numerical integration or substeps are used. Displaced heat is treated consistently with this discrete replacement: the correction is (*v*/*V*)*Q*start, using the pre-injection heat content, rather than MicroCal's average of the start and end heat contents.

The midpoint is evaluated halfway through the injection on the exponential concentration trajectory. Dissociation additionally accounts for dimer heat entering from the syringe; its legacy calculation is retained under MicroCal. Offsets are applied separately, as before.

**Preferences > Processing > Injection bookkeeping** selects the default for new data. **Experiment Details > Injection bookkeeping** explicitly switches an ordinary experiment and invalidates its fits without reintegrating measured heats. Changing the preference or editing a name/comment does not switch existing data. Older projects retain their saved concentrations and historical heat behavior; if their method is unknown, the selector displays **Saved processing — unchanged**. Choose a method explicitly before recalculating unknown saved concentrations. Rebuild tandem experiments through the tandem tool to change their method.

The ideal continuous mixing convention is the opposite physical limit: it assumes mixing throughout the injection, so the outgoing mixture changes composition continuously. Consider it for injections slow relative to cell mixing or as a sensitivity comparison with the discrete limit. It is inspired by [Dumas (2022)](https://doi.org/10.1007/s00249-021-01588-4), with an FT-ITC finite-injection numerical integration; it does not implement that paper's imperfect-mixing/adjustable-volume model or kinetic single-injection analysis. No option is assumed to be empirically superior. Simpson integration uses one fixed panel per injection, so unusually large injections or very sharp transitions can need additional scrutiny; there is no adaptive refinement. It requires three equilibrium states, whereas MicroCal and Discrete displacement require two.

Discrete displacement is useful when comparing analyses that use the same injection convention. It is not an imperfect-mixing correction. FT-ITC retains its own equilibrium solvers and offset convention, so results may differ from other programs even when the same displacement bookkeeping is selected. Fractional-stoichiometry two-site binding and monomer–dimer dissociation use FT-ITC-specific extensions of the convention. Neither fitted parameters nor background-heat conventions are automatically converted between programs.

## Models

Choose a model based on what is in the cell and syringe and what interactions are plausible. Adding parameters can improve a curve's fit even when the extra binding process is unsupported, so compare residuals and parameter uncertainty as well as the curve. The descriptions below state what each model assumes and reports.

### One-Set-Of-Sites

**One-Set-Of-Sites** represents one class of equivalent, independent binding sites. It fits stoichiometry, dissociation constant, binding enthalpy, and an injection-heat offset. The solution also reports thermodynamic quantities derived from the fitted affinity and enthalpy.

The **Options** tab provides **Use Syringe Correction** and **Stoichiometry**. Without syringe correction, the fitted N-value represents the apparent site stoichiometry. With syringe correction enabled, **Stoichiometry** fixes the number of cell-side sites and the fitted N parameter becomes the active syringe-concentration factor `alpha`.

The model cannot by itself distinguish concentration uncertainty from other effects that change an apparent stoichiometry.

### Two-Sets-Of-Sites

**Two-Sets-Of-Sites** represents two independent classes of sites. It fits separate stoichiometries, dissociation constants, and enthalpies for the two classes, together with a shared injection-heat offset.

**Shared N-Values** makes the two site classes use the same fitted stoichiometry. **Use Syringe Correction** instead fixes the first and second **Stoichiometry** values and fits one active syringe-concentration factor, `alpha`.

The two site labels are interchangeable: exchanging all parameters assigned to site 1 and site 2 describes the same physical model. A lower RMSD or optimizer objective alone does not establish that two distinguishable binding processes are supported by the experiment.

### Sequential Binding Sites

**Sequential Binding Sites** represents two, three, or four ordered binding
steps on a macromolecule in the cell. **Sequential binding steps** in the
**Options** tab selects the number of steps. The model fits one
macroscopic stepwise association constant and one molar step enthalpy for each
transition, together with the ordinary molar injection-heat offset. It does not
fit an N-value or syringe activity.

For step count *n*, let β<sub>0</sub> = 1,
β<sub>i</sub> = ∏<sub>j=1…i</sub>*K*<sub>j</sub>, and let *x* be the free ligand concentration.
The state weights and fractions are

> **Calculation:**
>
> *w*<sub>i</sub> = β<sub>i</sub>*x*<sup>i</sup>
>
> *F*<sub>i</sub> = *w*<sub>i</sub> / Σ<sub>j=0…n</sub>*w*<sub>j</sub>
>
> ν̄ = Σ<sub>i=0…n</sub>*iF*<sub>i</sub>
>
> *X*<sub>t</sub> = *x* + *M*<sub>t</sub>ν̄

Here *M*<sub>t</sub> and *X*<sub>t</sub> are total macromolecule and ligand
concentrations in the cell. The model solves the ligand balance as part of the fit and
calculates the cell heat content from the population of every sequential state:

> **Calculation:**
>
> *Q* = *V M*<sub>t</sub> Σ<sub>i=1…n</sub> *F*<sub>i</sub>
> (Σ<sub>j=1…i</sub> Δ*H*<sub>j</sub>)

The reported *K*<sub>i</sub> values are phenomenological, macroscopic step
constants for the ordered transitions *M* → *MX* → *MX*<sub>2</sub> and so on.
They are not microscopic intrinsic site constants. Step numbers therefore have
a physical order, so fitted steps are never sorted or treated as interchangeable.

The macromolecule must be in the cell and ligand in the syringe. Reverse
titrations with macromolecule in the syringe are outside this model. Multi-step
fits can be weakly identifiable, especially when the concentration window does
not populate every transition. Inspect parameter bounds, residuals, bootstrap
uncertainty, and parameter correlations; an improved RMSD does not by itself
establish the selected number of sequential steps.

### Competitive Binding

**Competitive Binding** represents titration of a target ligand into a macromolecule that is initially in equilibrium with a prebound ligand in the cell. It fits the target ligand's stoichiometry, dissociation constant, binding enthalpy, and injection-heat offset.

The **Options** tab requires the pre-equilibrated competitor's **Total competitor** concentration, **Ligand Affinity**, and **Ligand Enthalpy**. **Total competitor** is the total analytical competitor concentration in the cell after pre-equilibration: free competitor plus competitor bound to the macromolecule. Do not enter only the initially bound complex. **From attributes** makes **Total competitor** use the corresponding value stored in the Experiment Data attributes instead of the value entered in the model options. **Ligand Affinity** and **Ligand Enthalpy** each have a separate **From attributes** option. Add a **Competitor properties** experiment attribute and select a one-set-of-sites Analysis Result to supply its global summary Kd and ∆H, including their uncertainty. Each experiment in a global fit can select its own result. The row shows the source status; hover over it or the selector for the full result name and a short Kd and ∆H summary with units and SD. A missing source can still use captured values when available. Opening the editor only previews values; the saved capture refreshes when a fit starts. The model also provides **Use Syringe Correction** and **Stoichiometry** with the same concentration-factor interpretation as One-Set-Of-Sites.

Model options remain available to edit even when no experiment is ready for fitting. When **From attributes** is selected, starting a fit copies each experiment's attribute value into its model option; the entered value is used only if **From attributes** is turned off. Different experiments in a global fit can therefore have different effective option values. Every included experiment must have the attributes required by the enabled selections. If any are missing, one error dialog lists the affected experiments and attributes so they can be added or the selections turned off. A fit that cannot start leaves any previously attached solution in place. **Update Result** reads the attributes again; existing results continue to use the values saved with their fits.

The **Total competitor** experiment attribute has separate concentration and SD fields in both desktop apps. Buffer, salt, and ionic strength concentration attributes have a value field without an SD field.

If the source result has temperature dependence enabled, its summary is evaluated at the experiment temperature. Otherwise its usual summary temperature is used, with a notice when the experiment temperature differs. The attribute keeps the values used by the fit: if the source result is removed, existing results remain valid and a new fit can use the captured values. A source removed before any values were captured must be restored or replaced before fitting. Updating the source result marks dependent analysis results stale; fitting again copies the updated summary. Residual bootstrap samples the copied affinity and enthalpy uncertainties separately, as it does for manually entered model options.

The **Ligand Affinity** and **Ligand Enthalpy** labels describe the pre-equilibrated competitor's properties. The fitted target affinity and enthalpy depend on those supplied properties. The competitor properties are model inputs, not quantities independently determined by this fit.

The reported apparent target *K*<sub>d</sub> includes a competition factor calculated from the initial free competitor concentration. This calculation accounts for competitor already bound to cell sites rather than substituting the total competitor concentration. The apparent target *K*<sub>d</sub> can therefore differ from its intrinsic value.

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

Analysis choices are retained separately for the available fitting modes and models. **Restore defaults** clears the stored analysis inputs and reloads the fitting controls from the current preferences. It also resets the inspector's **Unlock parameters** setting. The action does not change preferences, including saved limits, result-output options, and display settings.

The **Limits** control selects a common parameter-bound policy:

- **Standard** uses the normal parameter bounds.
- **Expanded** permits a wider parameter range.
- **No limits** removes the configured parameter bounds.

A fitted value at a bound is not an interior estimate. It indicates that the reported value depends on the selected bound as well as on the data and model.

Before a fit starts, the application checks every free starting parameter against
the currently selected limits. This includes values entered manually, values
reused from an attached solution, and automatic values that become stale after
the **Limits** policy changes. Bounds are inclusive, so a value exactly at a
bound is allowed. Locked and globally determined parameters are not checked
because they are not optimizer coordinates. An out-of-range value is marked in
the **Parameters** inspector. **Run Fit** remains available, but choosing it
blocks the fit and lists the affected parameters; edit the
value, clear it to restore the automatic default, or widen **Limits**. In a
global fit, a local parameter that has no exposed global editor is identified by
its experiment name in the fit status area; edit that experiment in
single-experiment mode if needed.

## Fitting calculation

### Algorithm

**Levenberg-Marquardt** uses local derivative information and can be efficient when the starting values describe a suitable region of the fitting surface.

**Nelder-Mead** is a derivative-free simplex optimizer. It provides an alternative when the derivative-based method has difficulty converging from the chosen starting values. Agreement between optimizers does not by itself establish that the selected model is scientifically adequate.

### Weight by injection error

**Weight by injection error** uses the integration uncertainty estimated during thermogram processing when calculating the fitting objective. An injection with a larger estimated error has less influence on the fitted parameters than one with a smaller estimated error. Choose this only when those processing-derived error estimates are suitable for the dataset.

> **Calculation:**
>
> <i>r</i><sub>i</sub> = <i>q</i><sub>i,obs</sub> − <i>q</i><sub>i,model</sub>
>
> RMSD = √[(Σ<i>r</i><sub>i</sub><sup>2</sup>) / <i>N</i>]
>
> weighted objective = Σ(<i>r</i><sub>i</sub> / <i>σ</i><sub>i</sub>)<sup>2</sup>
>
> Only included injections enter these sums, and <i>N</i> is their number. The value <i>σ</i><sub>i</sub> is the processing-derived uncertainty for injection *i*; weighting changes the fitting objective but does not remove systematic uncertainty.

**RMSD** summarizes the typical size of the heat differences between the observed points and the fitted curve. It is always calculated from the unweighted residuals, including after a weighted fit. The result tooltip reports the optimizer objective separately: the raw residual sum of squares for unweighted fitting or the standardized residual sum of squares for weighted fitting. Weighting can change the fit even though the displayed RMSD remains an unweighted summary.

For a multiple-experiment result, the displayed global RMSD is pooled across every included injection in every member experiment. Each member row retains its own local RMSD, so the global value remains comparable when members contain different numbers of included injections.

**Weight by injection error** is available only when every included injection has a finite, positive peak-area SD. The application checks this again before fitting. An injection without an estimated SD can be excluded from the fit, or its thermogram can be reprocessed to obtain an estimate.

The weighting describes the application's processing-derived uncertainty model. It does not account for every systematic source of experimental or processing uncertainty.

## Parameter uncertainty

The reported parameter value comes from the best fit to the original data. The **Errors** control chooses how to estimate its uncertainty afterward; these methods do not replace that reported value with an average of refits:

- **None** retains the primary fit without estimating parameter uncertainty.
- **Bootstrap residuals** samples from the included injections' centered primary-fit residuals and adds each draw to a best-fit prediction. For an unweighted fit, it uses the raw residuals: injection peak-area SDs do not affect the synthetic peak areas or refits. This assumes the residual errors have roughly equal spread across injections. For an error-weighted fit, it divides each residual by its peak-area SD before centering and sampling, then multiplies each draw by the target injection's SD. Synthetic injections retain their stored peak-area SDs, so weighted refits use the same per-injection weights.
- **Leave-one-out** performs one deterministic refit for each deletion: one refit per included injection in a single-experiment analysis, or one refit per omitted experiment in a globally fitted multiple-experiment analysis. Concentrations, uncertain model options, and parameter locks are held at their primary-fit values so the resulting spread isolates deletion sensitivity.
- **Profile likelihood** varies one fitted parameter at a time and refits the other free parameters at each trial value. It uses the local objective for independently fitted experiments and the complete objective for shared global parameters. An interval requires threshold crossings on both sides; reaching a bound before a crossing is reported as censoring. See [Profile-likelihood uncertainty](#profile-likelihood-uncertainty).

**Bootstrap** sets the requested number of residual-bootstrap iterations. It is enabled and used only for residual bootstrap; leave-one-out and profile likelihood have deterministic schedules and do not use this count. Only included injections supply residuals, and only retained usable refits enter the parameter distributions. Because residual-bootstrap sampling is with replacement, one residual can occur more than once in a synthetic dataset while another may not occur at all. The fit status distinguishes successful and failed refits.

Correlation diagnostics distinguish all attempted refits, those with usable optimizer results, and complete refits with finite values for every displayed parameter. If many refits fail, the retained set may not represent the full range of resampled outcomes. Increasing the requested count improves Monte Carlo precision but does not resolve systematic refit failures.

Each replicate uses a fresh independent random stream; seeds are not stored, so rerunning a bootstrap does not reproduce the same random sequence.

When **Update Result** is used on a stored residual-bootstrap Analysis Result, its dialog shows the retained usable-refit count and offers the stored iteration count plus larger supported presets up to 10,000 requested iterations. The update performs a fresh complete fit and bootstrap; it does not append samples to the saved distribution. Canceling the calculation or completing it without any usable bootstrap refits preserves the previous Analysis Result.

The primary best-fit parameter remains the reported value. For a parameter with best-fit value *θ̂* and values *θ*<sub>b</sub> from *B* retained refits, the application summarizes the bootstrap distribution as follows:

> **Calculation:**
>
> SD = √[Σ<sub>b</sub>(*θ*<sub>b</sub> − *θ̂*)<sup>2</sup> / *B*]
>
> 95% CI = [*P*<sub>2.5</sub>({*θ*<sub>b</sub>}), *P*<sub>97.5</sub>({*θ*<sub>b</sub>})]
>
> SD is the root-mean-square deviation of the retained refits from the primary best fit. The confidence limits are the 2.5th and 97.5th percentiles of the retained refit distribution itself, so they need not be equally spaced around the best fit. Neither calculation replaces the best-fit value with the bootstrap mean or median.

The uncertainty display can show SD, the 95% confidence interval, both, or select between them automatically. This presentation rule is described under [Uncertainty and evaluation temperature](08-results-advanced-analysis.md#uncertainty-and-evaluation-temperature).

### Concentration uncertainty

With **Concentration uncertainty** enabled in the **Fit** tab, the concentration SDs entered in **Details...** are propagated through residual-bootstrap calculations. The control is initialized from the corresponding preference and is active only for residual bootstrap; leave-one-out and profile likelihood keep primary concentrations fixed. Each nonzero fractional SD is the arithmetic standard deviation relative to the entered concentration.

Each synthetic experiment uses a positive, mean-preserving lognormal concentration multiplier: if the fractional SD is *c*, then σ²<sub>log</sub> = ln(1 + *c*²), μ<sub>log</sub> = −σ²<sub>log</sub>/2, and the multiplier is exp(μ<sub>log</sub> + σ<sub>log</sub>*Z*) for a standard-normal *Z*. Thus the multiplier has mean 1 and SD *c*, so the sampled concentrations remain positive and their distribution has the entered arithmetic mean and SD.

Explicit cell or syringe SDs take precedence over the automatic value configured in Preferences. These uncertainties affect the synthetic experiment concentrations used for bootstrap refits, not the concentrations used for the primary best fit.

### Displayed parameter uncertainty

The bootstrap summary is first calculated for each fitted parameter coordinate. The application then converts that summary into the quantity shown to the user. For example, affinity is fitted as log<sub>10</sub>(*K*<sub>a</sub>) but is normally displayed as *K*<sub>d</sub>. The displayed central value comes from the primary best fit, SD is propagated through the transformation, and the percentile limits are transformed and reordered as required.

Quantities calculated from more than one reported parameter, such as −*T*Δ*S*, use the application's uncertainty-propagation rules for that calculation. Their displayed limits are therefore not necessarily the percentiles that would be obtained by recalculating the complete derived quantity independently for every bootstrap refit. The **Automatic** SD-or-CI decision is applied after transformation or propagation, separately for each displayed quantity.

### Unlock parameters during error estimation

**Locked** parameters remain fixed during the primary fit. With **Unlock parameters** enabled, copies of those parameters are unlocked only for residual-bootstrap refits and can vary in the resampled solutions. Leave-one-out and profile likelihood always preserve the primary-fit locks.

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

An unavailable model, failed fit, solution at a parameter bound, or large number of failed refits can reflect missing or degenerate heat and concentration information, the supplied starting values, the selected limit policy, the optimizer's interaction with the fitting surface, model complexity, or weak parameter identifiability. Resampling can fail even when the primary fit succeeds because each refit presents a different or reduced dataset to the same model.
