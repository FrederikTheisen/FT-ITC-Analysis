<!--
Scientific meaning of FT-ITC's models, statistics and analysis tools, kept consistent with the
user manual (Documentation/UserManual/pages). These entries do not restate package field
definitions; the scientific-guidance prompt owns those.
-->

## One-set-of-sites model: equivalent independent sites, what N means and what is poorly identified
id: md-one-set-of-sites
kind: model
topics: one-set-of-sites, N, identifiability
basis: mixed
status: draft
matches: one set of sites model, single site model, N value meaning, apparent stoichiometry, Kd ΔH n offset fit, equivalent independent sites, one site fit adequacy
cite: wiseman_1989, brautigam_2016

Notes: The model assumes one class of equivalent, independent sites and fits N, Kd, ΔH and a heat offset. N is an apparent binding capacity, which absorbs concentration or activity errors as well as stoichiometry; moderate deviation from a plausible integer does not by itself contradict that stoichiometry.
Identifiability: N–ΔH are correlated at low c, Kd is poorly determined at very high c, and the offset correlates with ΔH when no plateau is reached.
Adequacy: the model cannot represent cooperativity, two site classes, linked folding or aggregation; structured residuals or a poor fit of the transition signal that the equivalence or independence assumption fails. Agreement with a plausible discrete stoichiometry plus random residuals is consistent with, but not proof of, a single class of sites.

## Two-sets-of-sites model: two independent classes, interchangeable labels, and when two classes are supported
id: md-two-sets-of-sites
kind: model
topics: two-sets-of-sites, heterogeneity
basis: general
status: draft
matches: two sets of sites model, two site fit, site 1 site 2 interchangeable, heterogeneous binding, shared N, accommodating second site, two classes of sites

Notes: Each class has its own N, Kd and ΔH, with a shared offset; the labels are interchangeable, so exchanging all parameters of the two sites describes the same model (label switching appears as different fits with the same objective).
Support for two classes needs more than a lower RMSD: classes that differ enough in Kd (about 10- to 100-fold beyond statistical factors) or in ΔH sign, both transitions inside a measurable c range, parameters with closed intervals, sensible stoichiometries, and independent structural or chemical reason. Without these, the second class merely absorbs misfit, noise or concentration error.
Shared N: constrains the two classes to the same capacity; justified when they are the same kind of site in different states.

## Sequential binding sites model: ordered macroscopic steps, not microscopic site constants
id: md-sequential-sites
kind: model
topics: sequential binding, cooperativity, macroscopic constants
basis: mixed
status: draft
matches: sequential binding sites, stepwise binding constants, macroscopic constants, M to MX to MX2, three step binding, four step binding, statistical factors, cooperativity from sequential fit
cite: prestel_nhe1_cam_2021, nemo_2009

Notes: The model fits one stepwise association constant and one enthalpy per step (two to four steps) for macromolecule in the cell and ligand in the syringe; it has no N. The fitted constants are ordered macroscopic constants that include statistical factors; for identical independent sites, two steps already differ by a factor of four (K1 = 2k, K2 = k/2), so cooperativity judgments must use the statistical baseline.
Identifiability: steps are physical and never interchangeable, but the concentration window must populate each transition; later steps are usually weakly determined, so three- and four-step fits often have unstable parameters. A lower RMSD does not itself establish the number of steps; the molecular architecture should support it.
Compared with a two-sets-of-sites fit, sequential steps describe order and coupling; independent classes do not.

## Competitive binding model: apparent target Kd, competitor inputs and their uncertainty
id: md-competitive-binding
kind: model
topics: competition, displacement
basis: mixed
status: draft
matches: competitive binding model, competitor properties, apparent target Kd, intrinsic Kd, total competitor concentration, displacement fit, competitor affinity enthalpy inputs
cite: sigurskjold_2000

Notes: The fitted target ligand competes with a pre-bound competitor for the same sites. Competitor total concentration, affinity and enthalpy are supplied inputs, not results of the fit; the target's affinity, enthalpy, N and offset are fitted.
Interpretation: the reported apparent Kd contains a competition factor computed from the initial free competitor concentration; it differs from the intrinsic Kd and depends on the supplied competitor properties and their uncertainty. Competitor properties should be measured in the same buffer and temperature.
Cautions: competition needs mutually exclusive binding at one site; the enthalpies of the two ligands must differ for the displacement to be visible; a poorly characterized competitor limits the accuracy of the target affinity, and attribute-supplied values reflect the source result used when fitting.

## Dissociation model: dimers diluted into buffer, effective Kd and the heterodimer scaling
id: md-dissociation
kind: model
topics: dissociation, dimerization, dilution
basis: mixed
status: draft
matches: dissociation model, dimer dissociation ITC, heterodimer scaling Kd, effective dissociation constant, dilution of complex, syringe contains dimer, equimolar complex dissociation
cite: cpn10_self_association_2005

Notes: A preformed dimer in the syringe separates into free components when diluted into buffer; the model fits an effective dissociation constant, the heat per mole of dimers formed (association enthalpy) and an offset. Dissociation heats have the opposite sign to the association enthalpy.
Scaling: for an equimolar A + B ⇌ AB complex the syringe concentration is entered as the sum of A and B units; the usual heterodimer association constant is four times the model's, so the usual heterodimer Kd is one quarter of the fitted Kd.
Limits: it does not represent unequal component amounts, other oligomerization schemes, or slow dissociation beyond the injection spacing; it has no stoichiometry or syringe-correction options.

## Syringe correction mode: what the fitted factor represents (cell-side N versus syringe-side α)
id: md-syringe-correction
kind: model
topics: concentration uncertainty, active fraction
basis: mixed
status: draft
matches: syringe correction, alpha syringe, active fraction of titrant, N as capacity, concentration uncertainty parameterization, cell side versus syringe side, stoichiometry fixed
cite: tellinghuisen_2011

Notes: In the default parameterization the fitted N absorbs error in the effective concentration or activity of the species in the cell. In syringe-correction mode the stoichiometry is fixed and the fitted factor α scales the titrant concentration delivered from the syringe. Heats and integrated peaks are unchanged; only the mass-balance interpretation differs.
Interpretation: a single titration cannot tell whether the cell species or the titrant carries the concentration error; the choice should follow independent knowledge (for example a weighed small molecule of uncertain purity versus a well-quantified protein). The two parameterizations are not strictly equivalent, so results should be compared with the mode stated.
Bearing: an α or N far from unity points to an activity or concentration problem, not an unusual stoichiometry, unless independently supported.

## Injection bookkeeping (MicroCal, ideal continuous mixing, discrete displacement): why fitted values can differ between programs
id: md-injection-bookkeeping
kind: model
topics: bookkeeping, displaced heat, comparability
basis: general
status: draft
matches: MicroCal Origin comparison, injection bookkeeping, displaced volume heat correction, cell concentration after injection, discrete displacement, continuous mixing, comparing results from different software, why parameters differ from Origin

Notes: Bookkeeping fixes how cell and ligand concentrations evolve after each injection and how heat carried out in displaced solution is accounted for. MicroCal uses averaged approximations; discrete displacement assumes the injection displaces the previous cell mixture before mixing; ideal continuous mixing assumes exponential mixing throughout the injection. No option has been shown to be universally superior.
Consequences: peak areas are unchanged, but fitted N, Kd and ΔH can differ modestly between conventions, more so for large injections relative to the cell volume or sharp transitions. When comparing with Origin results or other programs, injection convention, baseline treatment, offset treatment and concentration definitions all matter; agreement within these differences is not a discrepancy.
Use: a sensitivity comparison of conventions shows whether a conclusion depends on bookkeeping.

## Null-hypothesis (offset) test: ΔAICc cut-offs and what "no binding detected" means
id: md-null-hypothesis
kind: model
topics: null model, ΔAICc, binding detection
basis: general
status: draft
matches: null hypothesis test, no binding detected, ΔAICc cutoff, offset only model, binding detected inconclusive, evidence for binding, model versus background heat, assess whether binding is supported

Notes: The null model assumes only a constant background heat per mole of injectant. ΔAICc = AICc(null) − AICc(binding); positive values favor binding. The app's automatic classification uses ΔAICc ≤ 6 for "no binding detected", between 6 and 10 for "inconclusive", and ≥ 10 for "binding detected". These cut-offs are chosen and carry no calibrated false-positive rate.
Reading: "binding detected" means binding explains the data clearly better than a flat offset, not that the chosen binding model is adequate. "No binding detected" means the experiment does not establish binding relative to the offset, which can result from ΔH near zero, very weak binding, noise or inactive material; it does not show that the molecules cannot bind.
Power: ΔAICc depends on the number of injections, noise and curvature, so small heats and low c give low power.

## AIC and AICc: how to compare candidate models and what they cannot say
id: md-information-criteria
kind: model
topics: AIC, AICc, model selection
basis: general
status: draft
matches: AIC, AICc, model comparison, information criterion, ΔAICc interpretation, which model is better, penalty for parameters, small sample correction, model selection one site two site

Notes: With a Gaussian residual model, −2 log L is computed from the residual sum of squares (weighted fits use standardized residuals); K counts model parameters plus one residual-variance parameter; AIC = −2 log L + 2K and AICc = AIC + 2K(K+1)/(n − K − 1). AICc is undefined when n ≤ K + 1, which is common for ITC with 15 to 25 injections and many parameters.
Rules of thumb (Burnham–Anderson style): differences below about 2 mean essentially equivalent support; about 4 to 7 mean clearly less support for the higher-AICc model; above about 10 mean essentially none. They apply only to models fitted to identical observations, response and weighting.
Cannot say: whether any model is adequate, whether parameters are identifiable, or whether the mechanism is right; residual structure, interval widths and scientific plausibility are examined alongside. Pooled criteria across experiments are not sums of member criteria.

## Parameter-uncertainty methods in FT-ITC: residual bootstrap, leave-one-out and profile likelihood
id: md-uncertainty-methods
kind: model
topics: bootstrap, profile likelihood, leave-one-out, correlation
basis: mixed
status: draft
matches: bootstrap confidence interval, profile likelihood, leave one out, parameter correlation matrix, concentration uncertainty propagation, censored profile interval, confidence limits meaning
cite: nguyen_bayesian_itc_2018, brautigam_2016

Notes: The point estimates are best fits to the original data, not bootstrap means. Residual bootstrap resamples centered residuals (scaled for degrees of freedom) and refits; it reflects noise in this dataset under the model. Leave-one-out measures sensitivity to individual injections (or whole experiments in a global fit) rather than giving a confidence interval. Profile likelihood varies one parameter, refits the others, and gives intervals that can be asymmetric; a limit reached before the threshold is censoring, not an endpoint.
Concentration uncertainty can be propagated in bootstrap when SDs are entered; without them concentrations stay fixed and systematic concentration error is absent from the intervals.
Correlation from bootstrap refits describes coupling among parameters (for example n–ΔH) with a Monte Carlo precision interval; it is not proof of identifiability or non-identifiability, and a high correlation magnitude is a prompt to examine the intervals.

## Weighted versus unweighted fitting, and what RMSD shows
id: md-weighting-rmsd
kind: model
topics: weighting, RMSD, injection errors
basis: general
status: draft
matches: weight by injection error, weighted fit, unweighted RMSD, heteroscedastic noise, injection SD weighting, objective function, residual size, effect of weighting on parameters

Notes: Weighting by injection error down-weights injections with larger processing uncertainty. It helps when noise differs strongly between injections (for example small late peaks versus large early peaks), and has little effect when uncertainties are similar. RMSD is always unweighted, so a weighted fit can have a different RMSD ranking than its objective.
Cautions: processing-derived uncertainties do not include systematic errors (concentration, baseline placement at the edges, mixing); a very small SD on a peak with an unrecognized artifact can dominate; weighted and unweighted fits are different questions and should not be compared by RMSD alone. A large injection SD does not by itself indicate poor baseline placement.

## Multi-experiment constraint schemes: independent, shared ΔG, shared Kd and thermodynamically linked
id: md-global-constraints
kind: model
topics: global constraints, temperature linking
basis: mixed
status: draft
matches: shared Kd, shared ΔG, thermodynamically linked, independent fits, constraint scheme global fit, temperature dependent affinity, linked Gibbs energy, ΔH temperature dependence ΔCp, constant ΔG constraint
cite: brautigam_2016

Notes: Independent: each experiment has its own affinity. Shared Kd or shared ΔG: one common value across experiments (identical at one temperature; differing when temperatures differ). Thermodynamically linked: the Gibbs energy follows the enthalpy and heat capacity through the Gibbs–Helmholtz relationship, so Kd(T) is constrained by ΔH(T) and ΔCp.
Justification: sharing is appropriate for replicates under the same conditions; linking is appropriate when one equilibrium and a roughly constant ΔCp describe all temperatures. If the process changes with temperature or ΔCp varies, linking forces consistency and hides curvature.
Reading: agreement of shared values is imposed, not evidence; compare with independent fits and AICc; uncertainties from linked fits depend on whether correlations among fitted coordinates are propagated.

## Temperature view: ΔH(T), ΔCp, isoentropic point and Spolar–Record outputs
id: md-temperature-analysis
kind: model
topics: temperature analysis, Spolar-Record
basis: mixed
status: draft
matches: temperature analysis view, ΔCp from temperature series, Spolar Record in software, hydration contribution, conformational contribution, residue estimate, folded mode ID interaction, reference temperature mode, isoentropic point mode
cite: spolar_record_science_1994, theisen_jacs_2021

Notes: The analysis needs a temperature span above a minimum and a One-set-of-sites result. Folded mode chooses the calibration (globular proteins versus interactions involving disordered regions); temperature mode chooses where the decomposition is evaluated (isoentropic point, mean temperature or a reference temperature). Outputs include reference temperature, a hydration term, a conformational term and a residue estimate.
Reading: these are conditional model estimates built on ΔCp, ΔH(T), the chosen modes and the calibration, not measurements of burial or residue counts. The span and precision of ΔCp set their uncertainty; the globular calibration can underestimate ordering for disordered regions, and different temperature modes give different numbers.
Uncertainty: propagation by independent Monte Carlo sampling of linked inputs can omit correlations among fitted coordinates; results should be reported with the modes used.

## Salt view: Debye–Hückel and counter-ion release modes
id: md-salt-analysis
kind: model
topics: salt analysis, ionic strength, counter-ion release
basis: mixed
status: draft
matches: salt analysis view, Debye Hückel mode, counter ion release mode, Kd0 extrapolation, affinity versus salt, ionic strength dependence analysis, ion activity regression, salt metadata, NaCl KCl series, binding weakens with salt
cite: lundback_sso7d_salt_1996, vandermeulen_ihf_water_2008

Notes: Debye–Hückel mode regresses ln Kd on √I (ionic strength) with intercept ln Kd0, giving a salt-extrapolated reference affinity; counter-ion release mode regresses ln Kd on ln (ion activity), whose slope relates to the number of ions released. Both require a salt attribute for every member and a meaningful span in ionic strength.
Reading: slopes are conditional on the recorded salt identity and ionic-strength values, on buffer contribution to ionic strength, and on the quality and span of the member Kd values; curvature, specific ion and anion effects, and Kd precision limit interpretation. Sign and magnitude are reported by the analysis and are not automatically a charge count.

## Protonation view: buffer series regression, sign convention and what it can show
id: md-protonation-analysis
kind: model
topics: protonation analysis, buffer series
basis: mixed
status: draft
matches: protonation analysis view, Protons value, Binding H intercept, buffer protonation enthalpy, sign convention protons, buffer registry approximations, protonation regression software, net proton uptake
cite: nguyen_cgp_2006

Notes: The analysis fits the observed binding enthalpy against the buffer protonation enthalpy: ΔH_obs = ΔH_bind + m·ΔH_buffer. The reported Protons value is −m, following the application's protonation-enthalpy convention, and Binding H is the intercept at zero buffer protonation enthalpy. In the standard linkage derivation with the buffer protonation enthalpy on the x-axis, −m corresponds to the net protons taken up by the complex (positive) or released (negative); the application itself does not assign an uptake or release mechanism, so the sign should be read from the supplied definitions.
Reading: needs at least two distinct buffers (more are strongly preferred) at the same pH and comparable conditions; results are conditional on buffer identity, temperature-dependent protonation enthalpies and the buffer registry values (which are approximations with some unknown quantities). The intercept is not free of the ionization heat of the affected group, and the analysis does not identify a residue or microscopic event.

## Experiment Designer: simulating titrations to choose concentrations, schedules and models
id: md-experiment-designer
kind: model
topics: experiment design, simulation
basis: mixed
status: draft
matches: Experiment Designer, simulate titration, plan experiment concentrations, synthetic titration, injection count volume design, design across parameter values, simulation noise, tandem simulation
cite: tellinghuisen_2007_variable

Notes: The designer generates a synthetic titration from instrument, concentrations, injection schedule, model parameters and noise, without altering project data. It lets the user see how c, final molar ratio, injection count and temperature-dependent ΔH change the information in the isotherm.
Good use: compare candidate designs across the plausible range of Kd and ΔH, not just best-fit values; judge designs by expected information (closed intervals on the parameters of interest, several points on the transition, heats above noise), not peak size alone.
Limits: simulated designs use assumed noise and ideal concentrations, so real systematic errors (concentration, buffer mismatch) are outside it.

## Buffer Subtraction tool: matched, linear and exponential-decay blank models and how noise propagates
id: md-buffer-subtraction
kind: model
topics: blank subtraction, error propagation
basis: mixed
status: draft
matches: buffer subtraction, blank titration subtraction, matched linear exponential decay reference, background heat model, dilution heat subtraction noise, subtract control titration
cite: tellinghuisen_2011

Notes: The tool models background heat from a reference titration and subtracts it from target heats; the original integrated heats are kept. Matched uses the reference heat at the same injection number; Linear and exponential decay fit a smooth model through included reference injections.
Noise: target and reference SDs combine in quadrature; a matched reference adds its full scatter, whereas a fitted model adds the (usually smaller) uncertainty of the fitted value, but that error is shared among injections. A reference SD of zero (no degrees of freedom) does not mean the correction is exact.
Use: appropriate when control heats are large, vary with injection or concentration, or when no plateau exists; the reference must match the target injection scheme and composition. Subtracting a mismatched reference can invert late-heat signs.

## Tandem experiments and the Experiment Merger: extending molar ratio with back-mixing correction
id: md-tandem-merger
kind: model
topics: tandem titrations, back-mixing
basis: general
status: draft
matches: tandem titration, experiment merger, syringe reload, back mixing, extended molar ratio, merge segments, dead volume, reload mixing fraction, sequential binding needing high molar ratio

Notes: A tandem titration continues after refilling the syringe, extending the molar-ratio range beyond one load (useful for low c, multi-step binding or weak binding). The merger concatenates segments and tracks starting concentrations per segment; back-mixing models partial mixing of displaced titrated solution with the active cell between loads.
Caution: the back-mixing fraction is a model-based inferred correction (fixed, one-site auto, or model-free auto), not a measured quantity; where heats barely depend on it, the estimate falls back to a typical value of a few percent. Reload artifacts (bubbles, thermal disturbance, concentration changes) can produce a discontinuity at each reload.
Use: confirm continuity of heats across the reload (after molar-ratio alignment) before trusting a merged isotherm.

## Baseline and integration choices: spline, polynomial and segmented baselines, integration window and injection uncertainty
id: md-integration-baseline
kind: model
topics: baseline, integration, injection uncertainty
basis: mixed
status: draft
matches: baseline method spline polynomial segmented, integration window, peak integration uncertainty, injection error bars, baseline control points, discarded integration regions, integration range effect, noise autocorrelation
cite: keller_2012, brautigam_2016

Notes: Spline baselines place local control points around injections; a polynomial baseline fits one smooth function and suits smooth drift; segmented baselines handle local changes. The integration window and baseline are coupled: changing the window changes which points inform the baseline and the integrated area.
Error bars: the injection uncertainty combines integrated noise and baseline-level estimation terms using the local noise level, integration length and effective number of samples corrected for autocorrelation; it is a processing-based estimate, not the total experimental error, and does not detect an artifact inside the peak.
Effects: for clean data, method differences have little effect; they matter for small heats, drifting baselines and slow tails. Manual baseline edits after seeing the desired fit risk circular analysis; automated peak-shape or standardized processing reduces operator bias.
