<!--
Core concepts. Numbers are computed (R = 8.314 J/(mol K)) or standard textbook values;
status stays "draft" until reviewed by a domain expert.
-->

## Converting Kd to ΔG: 5.7 kJ/mol per tenfold change in Kd at 25 °C, with reference values
id: cn-gibbs-kd-conversion
kind: concept
topics: free energy, affinity, unit conversion
basis: general
status: draft
matches: Kd to ΔG, free energy of binding, kJ/mol per decade, ΔG table, how tight is a Kd, ΔΔG from fold change, kcal/mol conversion, log Kd

Key points: ΔG° = RT ln(Kd / c°) with c° = 1 M, so ΔG° = -RT ln Ka. At 298.15 K, RT = 2.479 kJ/mol and a tenfold change in Kd is 5.71 kJ/mol (1.36 kcal/mol). At 310.15 K it is 5.94 kJ/mol.
Anchors at 25 °C (kJ/mol, rounded): Kd 1 mM -17.1; 100 µM -22.8; 10 µM -28.5; 1 µM -34.2; 100 nM -40.0; 10 nM -45.7; 1 nM -51.4; 100 pM -57.1; 1 pM -68.5.
Fold changes in Kd as ΔΔG at 25 °C: 2-fold 1.7 kJ/mol; 3-fold 2.7; 10-fold 5.7; 1 kJ/mol is about 1.5-fold.
Practical window: direct ITC usually resolves Kd from roughly 10 nM to 100 µM (ΔG about -46 to -23 kJ/mol); outside it c-value limits or displacement designs apply. Typical ITC precision of ΔG is about 0.3 to 1 kJ/mol, i.e. 1.1- to 1.5-fold in Kd.

## Sign and unit conventions: ΔH, −TΔS, ΔG, thermogram direction, cal versus J
id: cn-sign-conventions
kind: concept
topics: conventions, units, signs
basis: general
status: draft
matches: sign of ΔH, exothermic endothermic, −TΔS meaning, units kcal versus kJ, µcal/s to µW, thermogram peak direction, association versus dissociation, per mole of injectant

Key points: ΔH < 0 is exothermic. ΔG = ΔH - TΔS, so the entropy contribution −TΔS = ΔG - ΔH; a negative −TΔS is favorable. Binding is spontaneous when ΔG < 0 whatever the sign of ΔH; endothermic binding (ΔH > 0) must be entropy-driven.
Units: 1 cal = 4.184 J; 1 kcal/mol = 4.184 kJ/mol; 1 µcal/s = 4.184 µW; R = 8.314 J/(mol K) = 1.987 cal/(mol K). Heats are reported per mole of injectant (or per mole of binding sites, depending on software).
Direction: raw-signal sign conventions differ between instrument vendors and software (some plot exothermic as downward peaks, others upward), so the sign should be taken from the reported ΔH and the physical context.
Reversal: a dissociation constant is the reciprocal of an association constant, and the enthalpy of dissociation (for example in dilution experiments) has the opposite sign of the association enthalpy.

## ΔS and ΔG depend on the standard state; ΔH does not
id: cn-standard-state
kind: concept
topics: standard state, entropy
basis: general
status: draft
matches: standard state 1 M, entropy depends on concentration unit, cratic entropy, translational rotational entropy, comparing ΔS across papers, ΔS sign meaning

Key points: Reported ΔG° and ΔS° refer to a 1 M standard state. Using another concentration unit shifts ΔG by RT ln of the ratio and ΔS by the opposite amount, but leaves ΔH unchanged. Values from different papers are comparable only if they used the same convention (almost always 1 M).
Binding two molecules into one costs translational and rotational entropy. A positive net ΔS therefore means that solvent release, ion release and conformational gains outweigh that loss; it is not a measure of "hydrophobicity" by itself.
Magnitude of the shift: moving from a 1 M to a 1 µM standard state makes ΔG° about 34 kJ/mol less negative and ΔS° about 115 J/(mol K) lower at 25 °C, so an unstated convention can change the apparent entropy by far more than the experimental error.

## What ΔH and −TΔS include: net solvent, conformational and interaction terms, not isolated bonds
id: cn-enthalpy-entropy-meaning
kind: concept
topics: enthalpy, entropy, interpretation
basis: mixed
status: draft
matches: meaning of enthalpy, meaning of entropy, ΔH contributions, hydrogen bond enthalpy, desolvation, hydrophobic effect, solvent reorganization, thermodynamic signature
cite: nature_primer_2023, galectin_jacsau_2021

Key points: Observed ΔH is the sum of bonds formed and broken between protein, ligand and water, conformational changes, and linked protonation or ion exchange. Observed −TΔS combines solvent release, protein and ligand conformational entropy change, translational and rotational loss, and ion release.
Large favorable and unfavorable terms in water largely offset (breaking water contacts costs enthalpy, forming protein contacts returns it), so ΔH and −TΔS individually are poor, partly offsetting readouts of any single interaction type.
A comparison is most informative within a series measured under identical conditions, using ΔΔH and Δ(−TΔS) with propagated uncertainties.

## Enthalpy-driven versus entropy-driven binding: definitions, temperature dependence and misuse
id: cn-enthalpy-vs-entropy-driven
kind: concept
topics: thermodynamic signature, drug design
basis: general
status: draft
matches: enthalpy driven binding, entropy driven binding, enthalpic optimization, thermodynamic signature of ligands, ΔH versus −TΔS dominance, first in class best in class
reading: Freire 2008 Drug Discov Today 13:869; Reynolds & Holloway 2011 ACS Med Chem Lett

Key points: Binding is called enthalpy-driven when ΔH is the larger favorable term (ΔH < -TΔS, i.e. ΔH below ΔG/2) and entropy-driven when −TΔS is larger; endothermic binding is necessarily entropy-driven.
The label is conditional: ΔH changes with temperature by ΔCp, and −TΔS changes with it too, so a ligand that is enthalpy-driven at 25 °C can be entropy-driven at another temperature. Buffer ionization, standard state and linked processes also move the partition.
In drug design, enthalpy-driven binders have often been favored, but the partition is not a quality guarantee; high-affinity binders exist at every point of the enthalpy-entropy range and potency, selectivity and properties matter more than the partition.

## Enthalpy–entropy compensation: physical effect versus statistical and experimental correlation
id: cn-compensation
kind: concept
topics: compensation, ligand series
basis: mixed
status: draft
matches: enthalpy entropy compensation, ΔH versus TΔS plot, compensation slope, narrow ΔG range, correlated errors, phantom compensation, ligand series compensation
cite: olsson_compensation_2011
reading: Cornish-Bowden 2002 J Biosci 27:121; Sharp 2001 Protein Sci 10:661; Chodera & Mobley 2013 Annu Rev Biophys 42:121

Key points: Across ligand series ΔH and −TΔS are often negatively correlated, so large changes in ΔH produce small changes in ΔG. Two sources are not physical: (a) ΔG is determined far more precisely than ΔH or −TΔS individually, and −TΔS = ΔG - ΔH, so errors in ΔH reappear with opposite sign in −TΔS; (b) when ΔG varies little while ΔH varies a lot, a plot of ΔH versus −TΔS has slope near 1 by construction.
Physical compensation exists (looser contacts lower enthalpy cost and raise entropy; solvent reorganization offsets) but is usually partial.
Tests: use pairwise ΔΔH and Δ(−TΔS) with propagated error, compare the ΔH range with the ΔG range, and prefer independent estimates of ΔH and ΔG (calorimetric ΔH versus van't Hoff) when claiming compensation.

## Heat capacity change from ΔH versus temperature: sign, typical magnitudes and what ΔCp does and does not report
id: cn-heat-capacity-basics
kind: concept
topics: ΔCp, temperature dependence
basis: mixed
status: draft
matches: heat capacity change, ΔCp, temperature dependence of enthalpy, slope of ΔH versus T, negative ΔCp, surface burial, coupled folding, multi temperature ITC
cite: spolar_record_science_1994, ladbury_trp_repressor_1994

Key points: ΔCp = d(ΔH)/dT, measured as the slope of ΔH over at least three or four temperatures spanning 10 to 40 K with identical buffer composition. A negative ΔCp is typical when nonpolar surface is buried or when folding is coupled to binding; polar burial pushes it positive.
Typical magnitudes: near zero to about -0.5 kJ/(mol K) for small ligands, about -0.5 to -2 kJ/(mol K) for protein-protein interfaces of 1000 to 2000 Å², and more negative for protein-DNA or coupled-folding interfaces (about -1 to -5 kJ/(mol K), sometimes beyond).
Caveats: linked equilibria (buffer ionization, protonation, ion binding, folding) add their own ΔCp; ΔH(T) can curve over wide ranges; buffer pH changes with temperature (Tris more than phosphate); the molecular origin is not unique and ΔCp is not a direct measure of affinity or hydrophobic burial.

## Empirical link between ΔCp and buried surface area: about 0.32 cal/(mol K Å²) for apolar and -0.14 for polar
id: cn-cp-asa-relationship
kind: concept
topics: ΔCp, buried surface, structure-thermodynamics
basis: mixed
status: draft
matches: ΔCp ASA, heat capacity buried surface area, accessible surface area, Spolar Record hydration, Murphy Freire, estimating burial from ΔCp, structure based ΔCp
cite: spolar_record_science_1994, harmon_solvation_itc_2024
reading: Spolar, Livingstone & Record 1992 Biochemistry 31:3947; Murphy & Freire 1992 Adv Protein Chem 43:313; Myers, Pace & Scholtz 1995 Protein Sci 4:2138

Key points: An empirical relation from protein folding and model-compound data is ΔCp ≈ 0.32 ΔASA_apolar - 0.14 ΔASA_polar in cal/(mol K) per Å², with ΔASA negative on burial. Burying 1000 Å² of purely apolar surface predicts about -320 cal/(mol K), i.e. about -1.3 kJ/(mol K); mixed surfaces give less.
Use: compare a measured ΔCp with the value predicted from a structure. A measured ΔCp more negative than predicted suggests additional ordering, hydration changes or coupled folding; a less negative value suggests polar burial or compensating positive contributions.
Caveats: parameter sets differ between laboratories; coupled processes (folding, ionization, ion binding) contaminate ΔCp; ΔCp from a short temperature span has a large uncertainty; the numbers are estimates, not measurements of surface.

## Isoentropic temperature and the Spolar–Record decomposition of entropy into hydration, rigid-body and conformational terms
id: cn-spolar-record-decomposition
kind: concept
topics: Spolar-Record, coupled folding, entropy decomposition
basis: mixed
status: draft
matches: Spolar Record analysis, isoentropic temperature, hydration entropy, conformational entropy, residues folding upon binding, rigid body entropy, ΔCp ln(T/T*), coupled folding and binding estimate
cite: spolar_record_science_1994, spolar_record_protocol_2009, theisen_jacs_2021

Key points: The method splits the entropy of binding into a hydration term tied to ΔCp (ΔS_hyd = ΔCp ln(T/T_S*), with T_S* near 385 K where apolar hydration entropy vanishes), a rigid-body term for lost translation and rotation, and a conformational term. Given ΔCp from the temperature series, the remaining observed entropy is attributed to conformational change, and dividing by a per-residue entropy gives a number of residues that fold.
Assumptions that drive the answer: that ΔCp arises only from surface burial, the value assigned to the rigid-body term, the per-residue entropy calibration, and the chosen evaluation temperature (isoentropic point, mean or reference temperature).
Caveats: uncertainties compound from ΔCp and ΔS; the original globular-protein calibration can underestimate ordering for disordered regions, so IDR-adapted calibrations give larger estimates; results are conditional model estimates, not counts of residues, and should be reported with the calibration used.

## van't Hoff versus calorimetric enthalpy: agreement requires one two-state process
id: cn-vant-hoff-vs-calorimetric
kind: concept
topics: van't Hoff, calorimetric enthalpy, linked equilibria
basis: general
status: draft
matches: van't Hoff enthalpy, calorimetric versus van't Hoff, ln K versus 1/T, enthalpy discrepancy, ΔH from temperature dependence of K, linked equilibria enthalpy
reading: Naghibi, Tamura & Sturtevant 1995 PNAS 92:5597; Horn et al. 2001 Biochemistry 40:1774

Key points: Calorimetric ΔH is measured directly from heat. van't Hoff ΔH comes from d ln K/d(1/T) and needs K over a temperature range; with ΔCp ≠ 0 the plot is curved. For a simple one-step binding event they agree within error.
Discrepancy points to linked equilibria (protonation, conformational or oligomeric pre-equilibria), a non-two-state process, a temperature-dependent c-value that degrades K precision, or errors in K at the extreme temperatures. Because K from ITC has limited precision, calorimetric ΔH is normally the more reliable of the two; large disagreement is informative and should be explained instead of averaged.
A van't Hoff estimate over a narrow temperature range has a large error because d ln K is small.

## Salt dependence of affinity: slope of log Kd versus log [salt] reflects ion release but is not a charge count
id: cn-salt-dependence
kind: concept
topics: electrostatics, counterion release, ionic strength
basis: mixed
status: draft
matches: salt dependence of Kd, counterion release, ionic strength dependence, Debye Hückel, log Kd versus log salt, electrostatic contribution, Record analysis, polyelectrolyte binding, binding weakens at higher NaCl or KCl, affinity decreases with salt, high salt weakens binding
cite: lundback_sso7d_salt_1996, vandermeulen_ihf_water_2008, milev_tn916_osmolytes_2005
reading: Record, Lohman & de Haseth 1976 J Mol Biol 107:145; Record, Anderson & Courtenay 1998 Q Rev Biophys 31:257

Key points: For highly charged partners (DNA, RNA, polyanions, cationic peptides) binding releases counterions, which contributes favorable entropy; log Kobs falls roughly linearly with log [salt], and the slope is related to the number of ion pairs formed and ions released. Specific protein-DNA complexes often have slopes of roughly -5 to -12; nonspecific or weakly charged interfaces have smaller slopes.
Limits: the slope is not a simple count of charges (ion association, anion binding and accumulation effects enter), curvature appears at low and high salt, and different salts at equal concentration give different results because anions differ (chloride binds weakly; glutamate and fluoride are excluded), so Hofmeister, hydration and osmotic effects are mixed in with electrostatics.
Practice: use at least three or four salt concentrations over a decade, keep other composition constant, include buffer ions in ionic strength, and test salt identity separately from concentration.

## Proton linkage: observed ΔH includes the buffer's ionization heat, and the buffer series gives net protons transferred
id: cn-proton-linkage
kind: concept
topics: protonation, buffer, linked equilibria
basis: mixed
status: draft
matches: proton uptake, proton release, buffer dependence of ΔH, ΔnH, buffer ionization enthalpy regression, linked protonation, pKa shift on binding, Tris versus phosphate, intrinsic binding enthalpy
cite: nguyen_cgp_2006, paketuryte_intrinsic_thermo_2019

Key points: ΔH_obs = ΔH_int + n·ΔH_ion(buffer), where n is the net number of protons taken up by the complex from the buffer when ΔH_ion is the buffer's ionization (deprotonation) enthalpy. Plotting ΔH_obs against ΔH_ion over several buffers at the same pH, temperature and ionic strength gives n as the slope (positive for net uptake) and ΔH_int as the intercept. When the x-axis is the buffer protonation enthalpy (= -ΔH_ion), the slope has the opposite sign; the sign convention of the analysis in hand determines how the result is read.
Practical points: at least three, preferably four buffers spanning very different ΔH_ion (for example phosphate about +4, HEPES about +21, Tris about +47 kJ/mol) are needed to show linearity; two buffers define a line without testing it. Non-integer n arises when binding shifts a pKa near the working pH and n then depends on pH.
Limits: ΔH_int still contains the ionization heat of the affected group, buffers may bind the macromolecule or metals, ionic strength must match, and ΔH_ion itself depends on temperature. A regression is not a mechanism or residue assignment.

## Any coupled equilibrium (protonation, ion binding, conformation, oligomerization, competition) is folded into apparent Kd and ΔH
id: cn-linked-equilibria
kind: concept
topics: linked equilibria, apparent constants
basis: mixed
status: draft
matches: linked equilibria, coupled processes, apparent binding constant, binding polynomial, competing ligand, conformational pre-equilibrium, intrinsic parameters, thermodynamic linkage
cite: paketuryte_intrinsic_thermo_2019, brautigam_2016

Key points: If binding is coupled to another equilibrium, the apparent affinity is the intrinsic affinity weighted by the population of the binding-competent state, and the apparent ΔH adds the fraction-weighted heat of the coupled process. Typical couplings: protonation of ligand or protein groups, metal or nucleotide competition, conformational selection, ligand tautomers or ionization, self-association of either partner.
Evidence of coupling: parameters that change systematically with pH, buffer, salt, temperature or concentration; non-integer proton or ion numbers; ΔCp that disagrees with structure.
Consequence: apparent values describe the stated conditions; "intrinsic" values require explicit modeling of the linked process and measurements that vary the linked variable.

## Cooperativity in ITC: macroscopic versus intrinsic constants, statistical factors and the Hill coefficient
id: cn-cooperativity-definitions
kind: concept
topics: cooperativity, multisite binding
basis: mixed
status: draft
matches: positive cooperativity, negative cooperativity, statistical factor, macroscopic constants K1 K2, Hill coefficient, sequential binding cooperativity, K2/K1 ratio, intrinsic site constant
cite: tochtrop_ibabp_nmr_itc_2002, potassium_jacs_2022

Key points: For n identical independent sites with intrinsic constant k, the macroscopic stepwise constants are K_i = k (n - i + 1)/i. For two sites K1 = 2k and K2 = k/2, so K1/K2 = 4 with no cooperativity; for three sites K1 : K2 : K3 = 3k : k : k/3. Observed ratios K2/K1 below 1/4 indicate negative cooperativity and above 1/4 positive cooperativity (for two sites).
The Hill coefficient is above 1 for positive and below 1 for negative cooperativity and cannot exceed the number of sites. Strong positive cooperativity makes a sharper-than-1:1 transition; negative cooperativity makes a shallower, sometimes biphasic curve.
ITC alone may not separate intrinsic affinity from a very large cooperativity factor; site-resolved methods help. A step with more favorable ΔH is not positive cooperativity unless its affinity is also higher.

## Conformational selection versus induced fit cannot be distinguished by equilibrium ITC
id: cn-selection-vs-induced-fit
kind: concept
topics: mechanism, kinetics, conformational exchange
basis: mixed
status: draft
matches: conformational selection, induced fit, pre-existing conformation, binding mechanism, slow conformational exchange, proline isomerization, apparent affinity population, kinetic mechanism
cite: chakrabarti_recoverin_rk_2016, theisen_jacs_2025

Key points: ITC reports the net equilibrium thermodynamics (Kd, ΔH) and is blind to the pathway. Conformational selection and induced fit predict different dependences of the observed rate on ligand concentration; relaxation-dispersion NMR, stopped-flow fluorescence and related kinetic methods discriminate them.
When partner conformers interconvert slowly (for example proline cis/trans), the fitted Kd is an apparent, population- and timescale-dependent value, and thermogram peaks can broaden or show slow components.
Reporting: state the Kd as apparent under the experimental equilibration conditions; do not assign a mechanism from ITC alone.

## Thermodynamic cycles and double-mutant analysis from ITC: error propagation and additivity
id: cn-thermodynamic-cycles
kind: concept
topics: ΔΔG, mutants, coupling energy
basis: mixed
status: draft
matches: double mutant cycle, coupling energy, ΔΔΔG, additivity of mutations, mutational thermodynamics, hot spot, alanine scanning, error propagation
cite: koch_aldose_reductase_mutants_2011

Key points: ΔΔG = RT ln(Kd,mut/Kd,wt). A double-mutant coupling term uses four measurements, so its uncertainty is about twice that of a single ΔG; with typical ITC precision of 0.3 to 0.7 kJ/mol per ΔG, coupling energies below about 1.5 to 2 kJ/mol are rarely distinguishable from zero.
Additivity cannot be assumed in water: structural rearrangement, altered solvation and changed binding mode can make effects non-additive, and closely related mutants can differ in binding mode and thermodynamic signature even when Kd is similar.
A mutant that is partially unfolded or differently oligomeric changes the meaning of ΔΔG, so folding checks (CD, thermal shift, SEC) support interpretation.

## The c-value: definition, workable range and what it implies for identifiability
id: cn-c-value
kind: concept
topics: c-value, identifiability, design
basis: mixed
status: draft
matches: c-value, Wiseman c, c = n [M]/Kd, steepness of isotherm, optimal c, low c high c, identifiability of Kd, sigmoidicity, c window, cell concentration too high for the affinity, concentration too high or too low relative to Kd, adjusting concentration to the affinity
cite: wiseman_1989, tellinghuisen_2007_variable, brautigam_2016
reading: Turnbull & Daranas 2003 J Am Chem Soc 125:14859

Key points: c = n·[M]t/Kd (or Ka·n·[M]t), with [M]t the total cell concentration and n the sites per macromolecule. It controls the isotherm shape: c ≪ 1 gives a shallow, nearly linear curve; roughly 5 to 100 gives the best-defined sigmoid (about 10 to 100 is often cited as ideal); above about 500 to 1000 the transition is a step.
Information: Kd, n and ΔH are all well determined at intermediate c. At low c, n and ΔH become strongly correlated and n cannot be fitted independently, although Kd and ΔH can still be obtained if n is known and the titration reaches substantial saturation (low-c experiments with fixed n are well established). At very high c, n and ΔH are well determined but Kd is not.
Practice: estimate c from the fitted Kd and the actual cell concentration after the experiment; for a different c, change [M] (and the titrant concentration to keep the final molar ratio near 2 or higher).

## Heat per injection: how enthalpy, concentration and injection volume set the signal, with a worked estimate
id: cn-heat-signal-estimate
kind: concept
topics: signal, sensitivity, design
basis: general
status: draft
matches: heat per injection, expected signal size, how much heat, µcal per injection, detectable heat, signal to noise, injection volume and concentration, can ITC see this binding

Key points: In the saturating regime early in a titration nearly all injected ligand binds, so the heat per injection is q ≈ ΔH × [L]syringe × V_inj. Example: 2 µL of 200 µM ligand is 0.4 nmol; with ΔH = -40 kJ/mol that is about 16 µJ (3.8 µcal); with ΔH = -5 kJ/mol only 2 µJ (0.5 µcal).
Past the transition, heats fall to the dilution baseline. For weak binding the fraction bound per injection is small, so heats are smaller than this ceiling.
Rough sensitivity guide on small-cell (about 200 µL) instruments: peaks of a few tenths of a microcalorie are hard to distinguish from noise and dilution heat, whereas several microcalories are comfortable; detectability is instrument-dependent and set by baseline noise and blank heats.
Consequences: low |ΔH| needs more material per injection or a temperature with larger |ΔH|; the same ligand dissolved at higher concentration raises both signal and dilution heat.

## Fraction bound and occupancy: how many Kd's of ligand are needed for a given saturation
id: cn-fraction-bound
kind: concept
topics: occupancy, saturation
basis: general
status: draft
matches: fraction bound, percent saturation, ligand concentration relative to Kd, how much ligand to saturate, occupancy, titration range, 90 percent bound

Key points: For ligand in large excess over macromolecule, fraction bound = [L]/(Kd + [L]): 50 % at [L] = Kd, 75 % at 3 × Kd, 90 % at 9 × Kd, 95 % at 19 × Kd, 99 % at 99 × Kd.
In a titration the free ligand is lower than the total, especially when [M] is comparable to or above Kd (tight-binding regime), where occupancy follows stoichiometry and the quadratic binding equation; this is why the final molar ratio, not only [L]/Kd, sets saturation.
Design use: to reach about 90 % saturation of a weak interaction the final free ligand must be about nine times Kd; for Kd in the mM range this is why ITC of weak binders needs very high ligand concentrations and often a fixed n.

## Ligand efficiency and thermodynamic efficiency metrics
id: cn-ligand-efficiency
kind: concept
topics: drug design, ligand efficiency
basis: general
status: draft
matches: ligand efficiency, LE, heavy atom, binding efficiency, enthalpic efficiency, fragment quality, ΔG per atom, potency per size

Key points: Ligand efficiency LE = -ΔG / N_heavy (kcal/mol or kJ/mol per heavy atom); about 0.3 kcal/mol (1.25 kJ/mol) per heavy atom is a common benchmark for lead-like compounds, and fragments often show higher values. Enthalpic efficiency (-ΔH per heavy atom) and the ΔH/ΔG ratio are sometimes used to describe the signature.
Caveats: LE is size-dependent and weakly informative for very small ligands; ΔG from weak fragment Kd values needs accurate high-concentration ITC (c ≪ 1, offset-sensitive); enthalpy-based metrics inherit buffer and linked-protonation artifacts.

## Interpreting a Kd against concentration and biology
id: cn-kd-and-biology
kind: concept
topics: biological relevance, Kd
basis: general
status: draft
matches: biological relevance of Kd, physiological concentration, weak interaction relevance, millimolar Kd, avidity effective concentration, titration regime tight binding

Key points: Occupancy depends on the free partner concentration relative to Kd; typical intracellular concentrations of individual proteins are nM to µM, so Kd in the nM to µM range is usually functionally engaged, while mM Kd values matter only with high local concentration, avidity or abundant ligands (metabolites, ions, sugars at mM).
When [M] ≫ Kd (tight binding) the fraction bound is set by stoichiometry, and Kd matters only through the free concentration of the limiting partner.
In vitro Kd depends on buffer, pH, salt, temperature and construct, so quoting biological relevance should be tied to the conditions and constructs used.
