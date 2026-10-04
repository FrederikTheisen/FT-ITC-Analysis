<!--
Diagnostics: observable pattern -> ranked explanations -> what discriminates -> bearing on
conclusions. Written as knowledge. "Matters when / minor when" calibrates materiality.
-->

## Flat isotherm or only small constant heats: no binding detected versus nothing to detect
id: dx-flat-isotherm
kind: diagnostic
topics: negative result, sensitivity
basis: mixed
status: draft
matches: flat isotherm, no curvature, constant heats, no saturation, indistinguishable from control, no binding detected, near zero enthalpy, titration shows only dilution
cite: hierarchical_idp_2025, oep21_2023

Situation: injection heats are constant, or scatter around a small offset, with no curvature and no change relative to the matched ligand-into-buffer control.
Explanations: (1) no interaction under these conditions; (2) binding with ΔH near zero at this temperature; (3) Kd far above the concentrations used; (4) inactive, aggregated or already liganded macromolecule; (5) a missing cofactor, metal or pH requirement; (6) cancellation of binding heat by linked protonation heat in the chosen buffer.
Discriminating evidence: a different temperature (ΔH shifts by ΔCp × ΔT), a different buffer ionization enthalpy, an independent activity check, and an orthogonal method that does not rely on heat.
Bearing on conclusions: the supported statement is that no heat-associated binding was detected under the tested conditions; weak or enthalpy-silent binding is not excluded.
Next step: temperature or buffer change is usually the most informative single repeat.

## Heats comparable to noise or to the blank: weak signal limits every parameter
id: dx-small-heats
kind: diagnostic
topics: signal to noise, weak heats
basis: general
status: draft
matches: small heats, low signal, noisy isotherm, heats near baseline noise, poor signal to noise, weak ΔH, tiny peaks, injection SD large relative to heat

Situation: integrated heats are of the same order as their uncertainties or as the control titration.
Explanations: low |ΔH| at this temperature; cell or syringe concentration too low or injection volume too small; weak affinity; partial dilution-heat cancellation of the signal.
Discriminating evidence: ratio of early-injection heat to injection SD; how many injections lie clearly above the control; whether heat decays in a binding-like way or is flat.
Bearing on conclusions: parameter intervals are wide and the fitted shape may be driven by a few points; Kd may be an upper or lower bound. If curvature is genuine, a qualitative "weak binding with modest ΔH" can be supported even when numbers are imprecise.
Matters when: precise values or comparisons are the question. Minor when: the claim is only that binding exists and curvature is clear.
Next step: raise reacting material per injection (concentration or volume) or move to a temperature with larger |ΔH|.

## Isotherm is a sharp step (very high c): Kd cannot be quantified although n and ΔH can
id: dx-step-isotherm
kind: diagnostic
topics: high c, tight binding, identifiability
basis: mixed
status: draft
matches: step like isotherm, vertical transition, very high c, c above 1000, tight binding, sharp inflection, only one or two points on transition, Kd below resolution, upper limit of Kd, cell concentration too high for the affinity
cite: wiseman_1989, brautigam_2016, fam118_2025

Situation: heats are constant until near equivalence, then drop almost vertically to the baseline; a fitted Kd in the low nM or lower is returned with a very wide or one-sided interval.
Explanations: Kd far below the cell concentration (c above about 500 to 1000).
Discriminating evidence: number of injections inside the transition (one or two means little Kd information); profile likelihood or bootstrap for Kd (open on the low side); fitted Kd compared with the cell concentration (Kd below roughly 1/200 to 1/500 of it is poorly constrained).
Bearing on conclusions: n and ΔH remain well determined and stoichiometry is credible if concentrations are right; the fit converging is not evidence that Kd is precise, so Kd should be stated as an upper bound.
Next step: a lower cell concentration, a displacement design, or an orthogonal kinetic method.

## Shallow, nearly linear isotherm without a clear plateau (low c or incomplete saturation)
id: dx-shallow-no-plateau
kind: diagnostic
topics: low c, incomplete saturation
basis: mixed
status: draft
matches: shallow isotherm, no plateau, not saturated, low c, n and ΔH correlated, weak binding, curve does not level off, linear decrease, truncated titration, whether to fix n to one, constrain the stoichiometry
cite: wiseman_1989, tellinghuisen_2007_variable, brautigam_2016

Situation: heats decline gradually and the final injections are still above the blank; fitted n and ΔH have very wide intervals or run to bounds.
Explanations: (1) low c from weak binding; (2) titration ended before saturation (final molar ratio too low); (3) ligand or macromolecule concentration lower than intended; (4) a combination of both.
Discriminating evidence: final-injection heat relative to the ligand-into-buffer control; strong n–ΔH correlation in bootstrap or profile; a fit that fixes n at a defensible value and changes little in Kd.
Bearing on conclusions: n and ΔH cannot be read independently; Kd may still be bounded; the fitted offset is strongly coupled with ΔH, so a bad offset biases ΔH and Kd.
Next step: higher concentrations and a longer titration, a measured blank, or a complementary titration for a global fit; fixing n is defensible only with independent concentration and activity information.

## Late heats do not return to zero: dilution or mixing heat, offset, secondary binding or incomplete saturation
id: dx-late-heats-not-zero
kind: diagnostic
topics: offset, dilution heat, plateau
basis: mixed
status: draft
matches: plateau not zero, late heats offset, dilution heat plateau, heats do not return to baseline, constant offset large, late heats opposite sign, over subtracted blank, nonzero baseline heat
cite: tellinghuisen_2011, oep21_2023

Situation: heats after apparent saturation sit at a nonzero, roughly constant level (or slope slowly), possibly with a sign opposite to the binding heats.
Explanations: ligand heat of dilution or mixing (concentrated syringe, buffer or DMSO mismatch, pH mismatch); incomplete saturation; a weak second binding event; an over-subtracted blank or a mismatched offset model; ligand self-association.
Discriminating evidence: ligand-into-buffer control heat and its injection-to-injection trend; sign and size of the plateau relative to binding heats; salt or pH dependence of the late heats; whether the offset parameter is large compared with the binding heat.
Bearing on conclusions: a small constant offset is handled by the offset parameter; a large or varying one biases ΔH and Kd, especially at low c. Small late heats after a clear transition rarely need a warning. Fitted offsets with magnitude above about 10 kJ/mol per mole of injectant are large and usually point to mixing or mismatch heats.
Next step: matched control titration; explicit blank subtraction when control heats vary.

## Heats rise over the first injections or rise and then fall (bell-shaped or increasing isotherm)
id: dx-increasing-then-decreasing
kind: diagnostic
topics: cooperativity, assembly, non-sigmoidal
basis: mixed
status: draft
matches: increasing heats, bell shaped isotherm, first injections smaller than later, non monotonic heats, ligand induced oligomerization, positive cooperativity thermogram, heats go up then down
cite: tochtrop_ibabp_nmr_itc_2002, cpn10_self_association_2005

Situation: early injections are smaller than later ones, or the heat per injection passes through a maximum.
Explanations: positive cooperativity; ligand-induced oligomerization or assembly; two processes of opposite sign; precipitation or aggregation; slow equilibration; first-injection artifacts or a bubble.
Discriminating evidence: reproducibility across runs; peak shapes (slow components early); light scattering or SEC before and after; the sign of component heats; whether a cooperative or sequential model improves AICc and gives sensible parameters; reverse titration behavior.
Bearing on conclusions: a one-site model is not appropriate; fitted n, ΔH and Kd from it are averages with no mechanistic meaning. A cooperative or assembly model should be reported only with independent support.
Next step: reverse titration or concentration series, and a size-based check of complex formation.

## Heat sign changes during the titration (exothermic to endothermic or the reverse)
id: dx-sign-change
kind: diagnostic
topics: multiple processes, sign change
basis: mixed
status: draft
matches: sign reversal, exothermic then endothermic, heat changes sign, two processes opposite sign, mixed enthalpy, composite thermogram, switch from exothermic to endothermic
cite: kgf2_heparin_2014, peptide_membrane_2011

Situation: early injections give heats of one sign and later injections the other (or heats cross zero).
Explanations: two or more processes with opposite ΔH (binding plus a conformational change, oligomerization, a second site, or ligand dilution); dilution heat of opposite sign dominating late; buffer mismatch; for lipids, a membrane transition.
Discriminating evidence: the control titration; whether the sign change tracks the molar ratio (binding stoichiometry) or total ligand concentration (dilution or self-association); component signs in a two-site fit; temperature or buffer dependence.
Bearing on conclusions: a single ΔH from a one-site fit is a net average and has no clear meaning; component ΔH values can be assigned only if the composite model is independently justified.

## Biphasic isotherm with two visible transitions: two sites, sequential binding or heterogeneity
id: dx-biphasic
kind: diagnostic
topics: two-site, sequential, heterogeneity
basis: mixed
status: draft
matches: two transitions, biphasic isotherm, two binding sites, sequential binding, shoulder in isotherm, heterogeneous sample, n equals 2, two site fit justified
cite: prestel_nhe1_cam_2021, nemo_2009, brautigam_2016

Situation: two plateaus, a shoulder, or a fitted total n near two.
Explanations: two classes or ordered steps with different affinities; sample heterogeneity (isoforms, partial degradation, mixed oligomers); ligand impurity; binding followed by conformational change or ligand-induced oligomerization.
Discriminating evidence: whether the architecture supports two sites; whether both transitions fall in a measurable c range and differ enough in Kd (about 10 to 100 times beyond statistical factors) or in ΔH sign; parameter intervals and correlations; AICc improvement with sensible stoichiometries; reproducibility.
Bearing on conclusions: heterogeneity can be supported without microscopic constants being identified; very wide intervals mean the second site is accommodated rather than determined.
Matters when: individual site parameters are being reported or compared. Next step: complementary concentrations, reverse titration, or site-directed mutation of one site.

## Fitted n well below the expected stoichiometry (for example 0.4 to 0.8 for an expected 1:1)
id: dx-n-low
kind: diagnostic
topics: stoichiometry, concentration
basis: mixed
status: draft
matches: n lower than expected, n 0.5, n less than one, low stoichiometry, inactive protein, overestimated protein concentration, active fraction, half of sites
cite: tellinghuisen_2011, gruner_itc_impurities_2014, brautigam_2016

Situation: fitted n clearly below the expected value although the curve is otherwise well behaved.
Explanations (usual order): (1) active fraction of the cell macromolecule below one (misfolded, aggregated, pre-bound); (2) macromolecule concentration overestimated (extinction coefficient, scattering, nucleic acid contamination); (3) ligand concentration underestimated or the ligand partly lost to precipitation or adsorption; (4) genuine half-of-sites or negative-cooperativity behavior, or a dimer binding one ligand with concentration quoted per monomer.
Discriminating evidence: n varying between independent preparations with Kd and ΔH stable (favors concentration or activity); concentration by a second method; an independent activity check; SEC or DLS; structural stoichiometry.
Bearing on conclusions: ΔH follows the ligand concentration and Kd shifts modestly, so these may still be usable; n should not be reinterpreted as an unusual stoichiometry without support. Moderate deviations do not by themselves contradict a plausible discrete stoichiometry.
Next step: independent concentration and activity verification before constraining n, or a syringe-side correction if the ligand is the uncertain species.

## Fitted n well above the expected stoichiometry (for example 1.3 to 2 for an expected 1:1)
id: dx-n-high
kind: diagnostic
topics: stoichiometry, concentration
basis: mixed
status: draft
matches: n higher than expected, n 2, n above one, ligand concentration overestimated, peptide net content, additional binding site, nonspecific binding, oligomer concentration units, stoichiometry larger than the number of sites in the structure, more ligands bound than sites
cite: gruner_itc_impurities_2014, tellinghuisen_2011

Situation: fitted n clearly above the expected value.
Explanations: (1) ligand concentration overestimated (peptide net content below 100 %, hydrated or impure compound, partial precipitation); (2) macromolecule concentration underestimated (low A280 from tags or extinction coefficient error); (3) additional or nonspecific sites (electrostatic, surface hydrophobic) saturating at higher ratios; (4) concentration units mismatch between monomer and oligomer; (5) genuine multivalency.
Discriminating evidence: concentration of both species by independent methods; dependence on ionic strength (nonspecific electrostatic sites weaken with salt); heats beyond the first transition; structure and oligomeric state.
Bearing on conclusions: if concentration errors explain it, ΔH and n are scaled together; if extra sites exist, a one-site fit averages two events.
Next step: verify ligand identity, purity and concentration before constraining n or adding a second site.

## Fitted n differs between replicates while Kd and ΔH agree
id: dx-n-varies-replicates
kind: diagnostic
topics: stoichiometry, replicates, activity
basis: mixed
status: draft
matches: n varies between replicates, stoichiometry not reproducible, n changes with sample age, activity variation, effective concentration differs, same Kd different n, degradation
cite: tellinghuisen_2011

Situation: independent titrations give similar Kd and ΔH but different n.
Explanations: different effective (active) concentrations between preparations or over time (partial unfolding, aggregation, degradation, ligand loss), concentration measurement error, or carry-over of bound ligand or metal.
Discriminating evidence: whether n correlates with sample age, freeze-thaw history or batch; whether the differences are consistent with plausible concentration error; shared calibration uncertainty versus true between-run variation.
Bearing on conclusions: this pattern favors an activity or effective-concentration explanation over a change in binding mechanism, so n should be treated as a sample property and Kd and ΔH may still be pooled. If n changes together with ΔH, suspect ligand concentration.

## Very wide or one-sided interval on Kd (or ΔH): the fit converged but the parameter is not determined
id: dx-wide-interval
kind: diagnostic
topics: identifiability, uncertainty
basis: mixed
status: draft
matches: wide confidence interval, Kd poorly determined, one sided interval, profile likelihood flat, bootstrap wide, parameter correlation high, interval spans orders of magnitude, censored interval
cite: brautigam_2016, nguyen_bayesian_itc_2018

Situation: an interval spans more than about tenfold, is open on one side, or hits the optimizer bound.
Explanations: c too high or too low; strong correlation (n–ΔH, Kd–n); too few injections in the transition; large noise; over-parameterized model.
Discriminating evidence: shape of the profile (a flat side means the data cannot exclude values there; a limit reached before the threshold is censoring, not an interval endpoint); agreement between bootstrap and profile; correlation magnitude; c estimated from the fit.
Bearing on conclusions: the point estimate should not be quoted as precise; a bound or range is the honest statement. A tight interval can still be wrong if concentrations are off.
Matters when: the parameter is the question. Next step: design change driven by c (see tight-binding or low-affinity design).

## A fitted parameter sits at its bound or limit
id: dx-parameter-at-bound
kind: diagnostic
topics: fit diagnostics, bounds
basis: general
status: draft
matches: parameter at bound, hit the limit, optimizer boundary, Kd at lower limit, n at upper limit, constrained fit, fit stuck at boundary

Situation: Kd, n, ΔH or an offset equals a boundary value after fitting.
Explanations: the data do not constrain the parameter (step isotherm, low c, flat curve); bounds or starting values too tight; model sign wrong (exothermic versus endothermic); an over-complex model.
Discriminating evidence: relaxing the bound and refitting; profile likelihood; multiple starting values.
Bearing on conclusions: the value is not an estimate; it should be read as a limit, and conclusions drawn from the other parameters should be conditioned on it.

## Different starting values give different fits (local minima, label switching, non-unique solutions)
id: dx-multiple-minima
kind: diagnostic
topics: optimization, local minima, two-site
basis: general
status: draft
matches: local minima, different starting values different results, label switching, non unique fit, convergence to different solutions, two site fit unstable, multistart

Situation: fits from different starting values converge to different parameter sets with similar objective values.
Explanations: interchangeable site labels in two-site models (swapping all parameters of site 1 and 2 describes the same physics); strong correlation between parameters; multimodal likelihood from over-parameterization or low c.
Discriminating evidence: multi-start fitting; comparison of AICc; physical plausibility of each solution; independent knowledge of site properties.
Bearing on conclusions: a lower objective alone does not establish two distinguishable processes; report the ambiguity or choose by independent evidence.

## Reading residual patterns: what systematic structure usually means
id: dx-residual-patterns
kind: diagnostic
topics: residuals, model adequacy
basis: mixed
status: draft
matches: residual pattern, systematic residuals, structured residuals, runs of same sign, residuals at transition, alternating residuals, weighting residuals, model inadequacy, residual interpretation
cite: brautigam_2016, tellinghuisen_2011

Situation: residuals (observed minus predicted heats) show organized structure.
Patterns and usual meaning: sustained one-sign runs across the whole titration, symmetric about the transition, suggest wrong n or concentrations; misfit concentrated at the transition suggests the wrong shape (cooperativity, heterogeneity, competing equilibrium) or an inaccurate Kd; a trend over late injections suggests the dilution offset or baseline treatment; large first-injection residual is the usual first-injection artifact; zig-zag alternation suggests integration or baseline inconsistency or wrong injection volumes; a single isolated large residual suggests a bubble or mis-integration; residual size growing with heat suggests weighting is needed.
Bearing on conclusions: structured residuals mean the fitted model is not a complete description of the data. Random residuals support internal consistency only; they do not rule out concentration scaling errors. Residual magnitude should be judged against the injection uncertainties and the scale of the heats.

## A single discrepant injection (spike, bubble, mis-integration) and the decision to exclude it
id: dx-outlier-injection
kind: diagnostic
topics: outliers, exclusion
basis: general
status: draft
matches: outlier injection, excluded injection, spike, bubble peak, bad integration, discrepant point, exclude data point, accidental exclusion

Situation: one injection deviates strongly from the curve.
Explanations: air bubble, syringe mishap, baseline placement or integration window problem, particulate or transient artifact.
Discriminating evidence: the raw thermogram around that injection (shape, spike, baseline jump), its integration uncertainty, and its position (an outlier on the transition influences n and Kd much more than one on the plateau).
Bearing on conclusions: exclusion is legitimate when the raw data show an artifact, and should be disclosed; excluding a point only because it worsens the fit is circular. Excluded injections are usually intentional, and exclusion away from the transition has little effect on n. An unexcluded point with ordinary uncertainty and no anomaly that was excluded accidentally deserves a check.

## Drifting or unsettled baseline at the start or during the run
id: dx-baseline-drift
kind: diagnostic
topics: baseline, equilibration
basis: mixed
status: draft
matches: baseline drift, unstable baseline, insufficient equilibration, initial delay, noisy baseline, baseline jump, dirty cell, slow drift, sloping baseline
cite: keller_2012, brautigam_2016

Situation: the baseline slopes, steps or is noisy before or between injections.
Explanations: insufficient thermal equilibration or too short an initial delay; temperature change in the room; a dirty or contaminated cell; air bubbles or outgassing; stirring or electronic issues; slow chemistry in the sample (oxidation, hydrolysis).
Discriminating evidence: drift in water-into-water or buffer controls; whether drift reverses sign or scales with time; whether noise correlates with injections; whether it recovers by the next injection.
Bearing on conclusions: smooth drift with enough settled baseline between injections is normally handled by the baseline model and has little effect; step changes, noise that rises after injections, or a baseline that does not recover affect integrated heats. Baseline placement should be questioned only with strong evidence, and a manually edited baseline near a peak usually reflects deliberate review.
Next step: cleaning, longer equilibration, and avoiding temperature changes.

## Erratic spikes, noise bursts or sudden jumps: bubbles, particulates or injection problems
id: dx-spikes-noise
kind: diagnostic
topics: noise, bubbles, particulates
basis: general
status: draft
matches: spikes in thermogram, erratic noise, air bubble, particulates, precipitate in cell, sudden jump, noisy peaks, irregular heats, syringe clog

Situation: irregular spikes, bursts of noise, unexplained jumps or one-sided noise in the thermogram.
Explanations: air bubble in cell or syringe tip; particulates or aggregates near the stirrer; clogged or damaged syringe tip; unstable stirring; electrical noise.
Discriminating evidence: whether noise is reproducible between runs; its relation to injections; a cloudy cell or visible precipitate after the run; baseline noise level compared with a buffer control.
Bearing on conclusions: affected injections are unreliable; if many injections are affected, the run is unusable; exclusion should be documented.
Next step: degassing, centrifugation or filtration of samples, cleaning, and repeating.

## Flat-topped or clipped peaks: heat flow exceeds the instrument's dynamic range
id: dx-clipped-peaks
kind: diagnostic
topics: clipping, reference power, large heats
basis: general
status: draft
matches: clipped peaks, flat topped peaks, saturated power signal, reference power too low, too much heat per injection, signal at zero power, truncated peaks

Situation: peaks have flat tops or are truncated, typically for strongly exothermic early injections.
Explanations: reference power set too low for the heat flow, so the compensating heater power reaches its limit; too large injection volume or concentration.
Discriminating evidence: the raw power signal approaching zero or the limit during the peak; peaks affected most at the beginning where binding is complete.
Bearing on conclusions: integrated heats of affected injections are underestimated, biasing ΔH and the early plateau; fitted n and Kd are also affected.
Next step: raise the reference power or reduce concentration, volume or injection rate.

## Slow return to baseline, broad or tailing peaks
id: dx-slow-return
kind: diagnostic
topics: slow kinetics, peak shape
basis: mixed
status: draft
matches: slow return to baseline, broad peaks, tailing peaks, slow binding kinetics, peak width increases, incomplete equilibration, slow conformational change, long relaxation, integration window too short
cite: theisen_jacs_2025, vandermeulen_association_kinetics_2016

Situation: signals take much longer than the instrument response to return to baseline, or peak width grows during the titration.
Explanations: slow binding or slow conformational exchange (for example proline isomerization); slow aggregation or assembly; enzymatic turnover; instrument response (feedback mode); slow mixing in viscous samples. Some peak broadening near the transition is normal.
Discriminating evidence: peak width versus injection number; comparison with a standard or dilution peak; effect of lengthening spacing; whether the tail is the same sign as the main peak.
Bearing on conclusions: if the integration window truncates a real tail, heats are underestimated, biasing ΔH and the apparent Kd; Kd becomes an apparent value. Peak-width differences between feedback modes are instrument effects. Shortened integration is not itself a concern for small peaks.
Next step: longer spacing, wider window, or a kinetic model (kinetic ITC).

## Biphasic peak shape within single injections: a fast heat followed by a slower heat
id: dx-biphasic-peak-shape
kind: diagnostic
topics: multiple processes, kinetics
basis: general
status: draft
matches: biphasic peak, two component peak, fast and slow heat, shoulder on peak, binding followed by conformational change, aggregation after binding, slow second phase

Situation: each peak has a sharp initial component and a slower trailing component, or an exothermic part followed by an endothermic part.
Explanations: binding followed by a conformational change, oligomerization or aggregation; two sequential or parallel processes; slow dissolution or precipitation.
Discriminating evidence: how the relative amplitudes change with molar ratio; temperature dependence; control titration; spectroscopic or SEC evidence for a second process.
Bearing on conclusions: integrated heat is the sum of both and ΔH is the combined value; the mechanism cannot be assigned from ITC alone; fitting a single equilibrium model gives an effective description.

## Unexpectedly large |ΔH| (tens to hundreds of kJ/mol) with a large opposing entropy term
id: dx-large-enthalpy-entropy
kind: diagnostic
topics: large ΔH, compensation, linked processes
basis: mixed
status: draft
matches: very large negative enthalpy, large enthalpy large entropy penalty, ΔH -100 kJ/mol, compensation in single titration, large ΔH suspicious, coupled folding enthalpy, buffer ionization heat, large positive TΔS
cite: nguyen_cgp_2006, spolar_record_science_1994

Situation: |ΔH| of about 80 to 100 kJ/mol or more, accompanied by an opposing −TΔS, while ΔG is moderate.
Explanations: buffer ionization contribution from proton uptake or release (Tris contributes about 47 kJ/mol per proton); coupled folding or ordering, with a large enthalpic gain and entropic cost; metal or ion linkage; ligand concentration underestimated (heat per mole overestimated); buffer mismatch; real large interface.
Discriminating evidence: a second buffer with different ionization enthalpy; ΔH versus temperature (ΔCp); an independent ligand concentration check; structure and expected coupled folding.
Bearing on conclusions: because ΔG for binders rarely exceeds about -55 kJ/mol, a very large |ΔH| necessarily comes with a large opposing term, so the combination is not alarming by itself, but its composition (protons, folding, concentration error) determines the mechanistic reading.
