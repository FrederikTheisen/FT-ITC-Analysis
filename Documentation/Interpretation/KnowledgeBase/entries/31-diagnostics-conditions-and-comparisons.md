<!--
Diagnostics about sample behavior, conditions, series (temperature, buffer, salt) and
comparisons with other measurements.
-->

## ITC Kd differs from SPR, BLI, MST, fluorescence anisotropy or literature values
id: dx-kd-disagrees-other-method
kind: diagnostic
topics: method comparison, discrepancy
basis: mixed
status: draft
matches: ITC Kd differs from SPR, discrepancy between methods, Kd different from literature, BLI versus ITC, MST versus ITC, fluorescence anisotropy versus ITC, factor of ten disagreement, different affinity by different technique, compare to a published value measured at another temperature or in other conditions
cite: day_caii_multimethod_2002, papalia_abrf_mirg_2004, deinum_thrombin_inhibitors_2002, lee_peterson_fret_fp_itc_2016

Situation: affinities for the same pair differ between ITC and another method or a published value.
Explanations (checked in this order): condition differences (temperature, buffer, pH, salt, additives, cofactors); construct and tag differences; sample activity and concentration; immobilization effects, avidity, mass transport or rebinding in SPR and BLI; labeling or fluorophore effects in MST and anisotropy; extreme c in one method; model or fitting differences (two-state versus heterogeneous); steady-state versus kinetic Kd in SPR.
Discriminating evidence: matched-condition repeats; monovalent versus bivalent analyte; reversed orientation or capture format; label-free versus labeled comparison.
Bearing on conclusions: agreement within about 2- to 3-fold is good; differences beyond 5- to 10-fold usually indicate a systematic condition, sample or design difference. The discrepancy is informative about artifacts of one method, not necessarily a failure of ITC. Kd from ITC and kinetics-based Kd should agree for a simple 1:1 binding.

## Replicate titrations disagree: how large a discrepancy is ordinary and what drives larger ones
id: dx-replicates-disagree
kind: diagnostic
topics: reproducibility, replicates
basis: general
status: draft
matches: replicates disagree, reproducibility of ITC, variation between runs, inconsistent replicate titrations, ordinary variability, how much scatter is normal, different Kd in repeats

Situation: independent titrations give different parameters.
Ordinary variability for well-behaved systems: Kd within about 1.2- to 2-fold, ΔH within about 5 to 10 %, n within about 10 to 20 % between independent preparations; scatter grows at extreme c or low signal.
Explanations of larger scatter: concentration or activity differences between preparations; buffer or temperature differences; sample aging or aggregation; baseline or integration choices; different c windows from different concentrations.
Discriminating evidence: which parameter varies (n only suggests concentration or activity; n and ΔH together suggests ligand concentration; Kd shifting with concentration suggests aggregation or linked equilibria); whether scatter tracks preparation batch or run date.
Bearing on conclusions: different preparations often have different concentrations, which by itself is not a problem; the replicate SD is a more realistic uncertainty than the fit-derived interval.

## Signs of aggregation or precipitation during the titration
id: dx-aggregation-precipitation
kind: diagnostic
topics: aggregation, precipitation, sample integrity
basis: mixed
status: draft
matches: aggregation during titration, precipitation, cloudy cell, irregular heats, irreproducible titration, protein instability in cell, drifting baseline with noise, ligand induced aggregation, crosslinking
cite: dam_2016, maruno_stirring_itc_2020

Situation: heats are irregular or irreproducible, baselines noisy or drifting, n anomalously low, curve shape implausible, or the cell is cloudy afterwards.
Explanations: macromolecule unstable at the temperature or stirring speed; ligand-induced aggregation or crosslinking of multivalent partners; poorly soluble ligand or high DMSO; high concentration.
Discriminating evidence: visual and light-scattering inspection of cell and syringe before and after; centrifugation and recovered concentration; lower concentration, lower temperature or lower stirring; reproducibility; DLS or SEC.
Bearing on conclusions: thermodynamic parameters should be regarded as unreliable unless aggregation is prevented or shown to be negligible; heats can include aggregation enthalpy.
Next step: change conditions (concentration, stirring, temperature, additive) or use a monovalent partner.

## Dilution of the ligand alone gives concentration-dependent (curved) heats: ligand self-association or micellization
id: dx-ligand-self-association
kind: diagnostic
topics: ligand self-association, dilution heats
basis: mixed
status: draft
matches: ligand dilution heat curved, ligand aggregation heat, dye self association, micelle dissociation, critical aggregation concentration, control titration not constant, heat decreasing in control, amphiphile dilution
cite: nbi_selfassembly_2019

Situation: a ligand-into-buffer control gives heats that decrease systematically with injection number or with final concentration instead of being constant.
Explanations: the ligand self-associates (dimers, stacked dyes, peptide oligomers) or forms micelles and aggregates in the syringe, dissociating on dilution; the effect is largest at high syringe concentration.
Discriminating evidence: dependence on syringe concentration; control titration at several concentrations; a break at a critical concentration (CMC or CAC); light scattering.
Bearing on conclusions: a constant fitted offset is inadequate; a matched blank subtraction or an explicit model is needed. The measured dilution enthalpy is for disassembly, and the corresponding assembly enthalpy has the opposite sign.
Next step: lower the syringe concentration or measure and subtract an identical control titration.

## n or heat drifts with sample age or over the run: degradation, aggregation or loss of activity
id: dx-time-dependent-sample
kind: diagnostic
topics: sample stability, degradation
basis: general
status: draft
matches: sample instability, loss of activity over time, n decreases with sample age, syringe sample degradation, protein unstable at 25 °C, aggregation in syringe, heats change with time, freeze thaw

Situation: later titrations from the same stock give lower n or smaller heats, or heats drift within a run.
Explanations: protein degradation, unfolding or aggregation at the working temperature or at the high syringe concentration; ligand degradation or hydrolysis (ATP, esters), oxidation, adsorption to tubing or syringe; evaporation of DMSO stocks.
Discriminating evidence: repeated titrations from the same stock over hours or days; SEC or DLS of aged samples; activity assay; whether Kd and ΔH stay stable while n changes.
Bearing on conclusions: apparent n falls and Kd may shift weaker; comparisons across batches or days must account for it.

## Late-titration heats suggest an additional weak binding event (secondary or nonspecific site)
id: dx-secondary-weak-binding
kind: diagnostic
topics: secondary site, nonspecific binding
basis: mixed
status: draft
matches: second weak site, nonspecific binding at high ligand, late heats decline slowly, secondary binding mode, weak second site below ITC sensitivity, high ratio heats
cite: oep21_2023

Situation: after the main transition, heats decline slowly rather than falling to the blank, or an additional shallow phase appears at high molar ratio.
Explanations: a genuine weak second site; nonspecific electrostatic binding; ligand-induced oligomerization; ligand dilution or self-association.
Discriminating evidence: comparison with the control; salt dependence (nonspecific electrostatic binding weakens with salt); structural or mutational support; whether a two-site fit gives sensible, determined parameters.
Bearing on conclusions: ITC's effective one-site description may describe the dominant calorimetric transition without excluding weaker modes; ignoring a real second event biases the offset and ΔH.

## Experiments fitted jointly give poor agreement with shared parameters
id: dx-global-fit-inconsistency
kind: diagnostic
topics: global fitting, consistency
basis: mixed
status: draft
matches: global fit poor, shared parameters inconsistent, experiments disagree in joint fit, forced sharing, constraint not supported, individual versus global fit differences
cite: brautigam_2016

Situation: a global fit with shared parameters leaves structured residuals in individual experiments, or its parameters differ from individual fits.
Explanations: concentration inconsistencies between experiments; different conditions or samples treated as identical; a parameter that should not be shared (n when activity differs, Kd when temperature differs); an inadequate model.
Discriminating evidence: individual fits and their intervals; AICc of independent versus shared parameterization; whether releasing n or the offset per experiment resolves the misfit.
Bearing on conclusions: shared parameters are imposed; their equality is not evidence. Pooling helps only where it is justified; otherwise differences should be reported.

## Few or narrowly spaced temperatures: ΔCp and the Spolar–Record estimates are poorly determined
id: dx-few-temperatures
kind: diagnostic
topics: ΔCp, temperature series, precision
basis: mixed
status: draft
matches: ΔCp uncertainty, narrow temperature range, two temperatures, temperature span, ΔH versus temperature few points, poorly determined heat capacity, Spolar Record uncertainty, residue estimate uncertainty, how many temperatures are needed for a reliable heat capacity
cite: spolar_record_science_1994, theisen_jacs_2021

Situation: ΔCp (and derived hydration, conformational or residue estimates) rests on two to four temperatures or a span of less than about 10 K.
Explanations of low precision: slope uncertainty is approximately σ(ΔH) divided by about 0.75 times the span for four evenly spaced points, so a 10 K span and σ(ΔH) of 1 kJ/mol gives about ±0.13 kJ/(mol K); two points give no test of linearity.
Discriminating evidence: interval on the slope; residuals of ΔH versus temperature; whether buffer composition and pH were kept constant across temperatures.
Bearing on conclusions: derived quantities inherit and compound this uncertainty; residue counts and hydration/conformational splits are conditional model estimates and are weakly supported when ΔCp is imprecise.
Next step: more temperatures across a wider span with identical buffer composition.

## ΔH versus temperature is curved or nonmonotonic, or the c-value drifts across the series
id: dx-temperature-series-complications
kind: diagnostic
topics: temperature series, ΔCp, c-value
basis: mixed
status: draft
matches: curved ΔH versus T, temperature dependent ΔCp, nonmonotonic affinity, c-value changes with temperature, unfolding near high temperature, temperature series complications, linear ΔCp assumption
cite: datta_licata_taq_2003, lundback_sso7d_1998

Situation: ΔH(T) deviates from a straight line, Kd is nonmonotonic with temperature, or the isotherm shape changes across temperatures.
Explanations: temperature-dependent ΔCp over wide ranges; a coupled transition (partial unfolding, conformational change) entering at one end; buffer pKa change with temperature (Tris more than phosphate) changing proton-linked heat; the c-value shifting because Kd changes with temperature, so high c at cold and low c at warm (or the reverse).
Discriminating evidence: curvature beyond uncertainties; buffer ionization contributions; fitted c at each temperature; stability checks.
Bearing on conclusions: a single linear ΔCp or van't Hoff extrapolation should be interpreted locally, and extrapolated parameters over the full range may not describe the system.

## Protonation (buffer-series) analysis with few buffers or a narrow ionization-enthalpy span
id: dx-protonation-regression-limits
kind: diagnostic
topics: protonation, buffer series, regression
basis: mixed
status: draft
matches: proton uptake from buffer series, two buffers only, narrow range of buffer ionization enthalpy, protonation regression uncertainty, buffer dependence of ΔH weak, protons number uncertain
cite: nguyen_cgp_2006, paketuryte_intrinsic_thermo_2019

Situation: the buffer dependence of ΔH is estimated from two or three buffers, or from buffers with similar ionization enthalpies.
Explanations of weak constraint: two points define a line without testing it; a narrow ionization-enthalpy span makes the slope uncertain; non-matched pH, ionic strength, temperature or buffer-specific binding add scatter.
Discriminating evidence: slope interval, linearity with at least three or four buffers, buffers spanning low (phosphate), medium (HEPES, MOPS) and high (Tris, imidazole) ionization enthalpy.
Bearing on conclusions: a result with an interval including zero says the proton number is not distinguished from zero; a regression estimates net protons and is not an assignment to specific residues or a mechanism.

## Observed ΔH depends on buffer: the heat includes proton transfer to or from the buffer
id: dx-buffer-dependent-enthalpy
kind: diagnostic
topics: proton linkage, buffer dependence
basis: mixed
status: draft
matches: ΔH differs in Tris and phosphate, buffer dependent enthalpy, protons released on binding, protons taken up on binding, HEPES versus Tris, pH dependence of binding, buffer artifact or proton linkage, enthalpy changed when the buffer was swapped, different buffer gives different ΔH, switched buffer shifts the heat
cite: nguyen_cgp_2006

Situation: the same interaction gives different ΔH in different buffers at the same pH, or ΔH and Kd vary with pH.
Explanations: binding-linked proton uptake or release exchanged with the buffer; pKa shifts of ligand or protein groups upon binding; buffer binding to the macromolecule or a metal.
Discriminating evidence: the sign and size of ΔH change against the buffers' ionization enthalpies (a net uptake makes ΔH more positive in a high-ΔH_ion buffer such as Tris than in phosphate, release the reverse); pH dependence of K and ΔH; ionic strength matched.
Bearing on conclusions: the observed ΔH is not intrinsic binding energetics; comparisons across buffers are invalid without correcting for proton linkage, and a buffer with low ionization enthalpy (phosphate) gives results closest to the intrinsic ΔH only when other buffer interactions are absent.

## Fitted Kd changes with macromolecule concentration across titrations
id: dx-concentration-dependent-kd
kind: diagnostic
topics: self-association, aggregation, concentration series
basis: mixed
status: draft
matches: Kd depends on concentration, concentration series, apparent affinity changes with protein concentration, self association, nonideality, aggregation, dimerization of macromolecule, concentration dependent n
cite: wiseman_1989, cpn10_self_association_2005

Situation: titrations at different cell concentrations give systematically different Kd (or n, or ΔH).
Explanations: self-association or aggregation of one partner; linked equilibria; changing c shifting the identifiable range of Kd so different parameter regions are sampled; nonideal behavior at high concentration; concentration measurement error.
Discriminating evidence: SEC, AUC, DLS or mass photometry at the working concentrations; monotonic versus random trend; global fit with and without self-association; dilution-ITC of the macromolecule.
Bearing on conclusions: a single intrinsic Kd should not be assumed; the pattern supports reporting conditions and, if justified, a model including the association.

## Heats much smaller than ΔH × injected amount predicts
id: dx-heats-smaller-than-expected
kind: diagnostic
topics: signal budget, activity
basis: mixed
status: draft
matches: heats smaller than expected, early heats low, signal lower than predicted, less heat than expected, lower than ΔH times moles, partial saturation first injection, low active fraction
cite: tellinghuisen_2011

Situation: the first plateau heats are much lower than ΔH × moles of ligand per injection (when ΔH is known from literature or a previous run).
Explanations: low c so only a fraction of each injection binds; inactive or partly bound macromolecule; ligand concentration lower than assumed; a smaller true |ΔH| under these conditions (different buffer, pH or temperature); compensation by linked protonation.
Discriminating evidence: comparison with the expected ceiling from the heat-signal estimate; fitted c; fitted n; repeating in another buffer or temperature.
Bearing on conclusions: if early heats are close to the ceiling, binding is nearly stoichiometric and ΔH can be read; if far below, the system is in a weak-binding or low-activity regime and Kd or n rather than ΔH carry the reading.

## Few injections define the transition: sparse sampling of the informative region
id: dx-sparse-transition
kind: diagnostic
topics: sampling, injection design
basis: general
status: draft
matches: sparse transition sampling, too few points in transition, coarse injections, large injection volume near equivalence, resolution of curve midpoint, Kd uncertain few points

Situation: one to three injections fall between roughly 20 and 80 % saturation.
Explanations: injection volume too large for the c-value; titration range too wide (many plateau points); high c (sharp transition) or unfavorable molar-ratio range.
Discriminating evidence: number of points in the transition; Kd interval shape; effect of leaving out one of those points.
Bearing on conclusions: Kd and n are sensitive to individual transition points; leave-one-out sensitivity is large; the fit may be driven by two or three heats.
Next step: smaller injections near equivalence, or a shorter titration with the final ratio just beyond saturation.
