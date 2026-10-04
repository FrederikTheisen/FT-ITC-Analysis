<!--
Experimental design and sample preparation knowledge: what experienced analysts know
about how an experiment was probably set up and what that implies for the data.
-->

## Choosing cell and syringe concentrations from an expected Kd and ΔH
id: ds-sizing-concentrations
kind: design
topics: concentration design, titration planning
basis: mixed
status: draft
matches: how to choose concentrations, cell concentration, syringe concentration, final molar ratio, 10x syringe, titration planning, c between 10 and 100, concentration ratio
cite: tellinghuisen_2007_variable, wiseman_1989

Approach: Pick the cell concentration so that c = n[M]/Kd is about 10 to 100 when Kd is known, but not lower than the level at which the first injections give a comfortable signal (several microcalories on small-cell instruments; see the heat-signal estimate). Pick the syringe concentration so that the final molar ratio is at least about 2 times the expected stoichiometry.
Rule of thumb: on a 200 µL cell with a 40 µL syringe, 19 injections of 2 µL deliver about 0.19 cell volumes, so a syringe concentration of about 10 times the cell concentration reaches a molar ratio of about 2; on a 1.4 mL cell with 28 × 10 µL it is the same ratio (about 0.20 cell volumes). Use 20 to 25 times for 2:1 or sequential multisite systems, and more when the transition lies at a high molar ratio.
Typical starting points for small-cell instruments: 10 to 50 µM macromolecule in the cell and 100 to 500 µM ligand in the syringe; weaker interactions need higher values.
Trade-off: higher concentrations raise signal but raise dilution heat and the risk of aggregation, and move c upward. Simulating the design across the plausible range of Kd and ΔH, not only the best guess, is the safest check.

## Designing for weak binding (Kd from tens of µM to mM): low c is unavoidable but manageable
id: ds-low-affinity
kind: design
topics: weak binding, low c, fragments
basis: mixed
status: draft
matches: weak binding ITC, low affinity, millimolar Kd, fragment binding, low c design, fixed n, sugar binding, high ligand concentration, no plateau
cite: wiseman_1989, tellinghuisen_2007_variable
reading: Turnbull & Daranas 2003 J Am Chem Soc 125:14859

Approach: Use the highest soluble concentrations, aim for a final ligand concentration of several times Kd (about 9 times Kd for 90 % of sites occupied), and reach a final molar ratio of at least 5 to 10. With n known from an independent source and substantial saturation reached, Kd and ΔH can be obtained at c below 1; without a fixed n, n and ΔH are strongly correlated.
Pitfalls: with a concentrated syringe, dilution heat becomes large and, because the plateau is never reached, the fitted offset is strongly correlated with ΔH; an accurate ligand-into-buffer control (or blank subtraction) is more important than at high c. Ligand self-association, DMSO or pH mismatch and solubility limits show up first here.
Bearing: Kd from such data is reliable only to the extent that n, concentrations and the blank are right; report it as an estimate with its dependence on the fixed n stated. A larger |ΔH| (different temperature) improves the signal.

## Designing for tight binding (Kd below about 10 nM): lower concentrations, displacement or an orthogonal method
id: ds-tight-binding
kind: design
topics: tight binding, high c, displacement
basis: mixed
status: draft
matches: tight binding ITC, nanomolar Kd, picomolar, step isotherm design, high c, upper limit of ITC, how to measure tight binder, lower concentration, displacement alternative
cite: sigurskjold_2000, zubriene_nanomolar_tsa_itc_2009

Approach: Direct titrations keep Kd resolvable only up to c of about 500 to 1000. With a cell concentration of 10 µM, c exceeds 1000 for Kd below about 10 nM, so the isotherm is a step. Lowering the cell concentration to about 1 to 2 µM extends the reach to low nM only when |ΔH| is large enough that the heats stay measurable.
Alternatives: displacement ITC (titrate the tight ligand into macromolecule pre-bound to a weaker, characterized competitor) lowers the apparent affinity into the measurable range; kinetic methods (SPR, BLI, stopped flow) give Kd = koff/kon; thermal-shift or competition assays can bound the affinity.
Bearing: when only a step is available, ΔH and n are still good, but Kd should be stated as an upper bound of roughly one hundredth to one thousandth of the cell concentration, not as a precise number.

## Competitive (displacement) ITC design: competitor choice, concentrations and what must be known
id: ds-displacement-design
kind: design
topics: competition, displacement, high-affinity or low-ΔH ligands
basis: mixed
status: draft
matches: competitive ITC, displacement titration, competitor, apparent Kd, weak competitor, prebound ligand, measuring tight binders, enthalpy silent ligands, Sigurskjold
cite: sigurskjold_2000

Approach: Titrate the target ligand A into macromolecule pre-equilibrated with a competitor B for the same site. The apparent affinity of A is lowered to about Ka/(1 + K_B[B]); choose [B] so that the apparent c falls into 10 to 100. The same experiment gives the affinity of A when B's thermodynamics are known, or of B when A's are known (including ligands too weak or enthalpy-silent for direct titration).
Requirements: A and B bind the same site mutually exclusively; B's Kd, ΔH and total concentration are known in the same buffer and temperature (errors in them propagate); A and B must differ enough in ΔH, otherwise the displacement produces little heat; enough pre-equilibration; a direct titration of A without B as reference.
Bearing: the fitted target affinity depends on the supplied competitor properties, which are inputs rather than results of the displacement fit, so their uncertainty belongs in the final interval.

## Global analysis of several titrations: what to share, what to vary, and what the design adds
id: ds-global-fitting
kind: design
topics: global fitting, multiple experiments
basis: mixed
status: draft
matches: global fit, joint analysis, shared parameters, complementary titrations, different concentrations, shared Kd, shared ΔH, improve identifiability, multi experiment
cite: brautigam_2016

Approach: Fit titrations together when physics justifies shared parameters. Complementary designs add the most: different concentrations (shifting c), different orientations, different ligand-to-macromolecule ratios, or different temperatures with linked thermodynamics. Typically share Kd and ΔH among replicates at the same conditions and let n or the offset vary per experiment to absorb concentration and dilution differences.
Key points: Global fitting can use partial or suboptimal isotherms productively and often resolves n–ΔH correlation. Shared parameters are an assumption: equal values after sharing are imposed, not evidence of agreement. Fitting individually first and comparing parameters and AICc is the simple test of whether sharing is warranted.
Bearing: pooling improves precision only for the parameters that are actually shared and informed; it does not remove systematic errors common to all experiments (shared concentration or buffer errors).

## Injection volume, number and spacing: practical defaults and trade-offs
id: ds-injection-schedule
kind: design
topics: injection schedule, timing
basis: general
status: draft
matches: injection volume, number of injections, spacing between injections, delay time, 2 µL injections, 10 µL injections, resolution of transition, return to baseline, long spacing
reading: instrument manuals for default schedules

Approach: Typical small-cell defaults are a small initial injection (0.2 to 0.5 µL, usually discarded) followed by about 18 to 19 injections of 2 µL with spacing of roughly 120 to 150 s; large-cell instruments use about 28 injections of 10 µL with 200 to 300 s spacing. These reach a molar ratio of about 2 with a syringe about 10 times the cell concentration.
Trade-offs: larger injections give more heat per injection but coarser sampling of the transition; smaller injections near the transition improve resolution of Kd; spacing must allow the signal to return to baseline (a few times the response or reaction time), and slow processes need longer spacing or a longer integration window.
Rule: injections after the plateau add little information; points on the transition (about 20 to 80 % saturation) carry the Kd information, and more than the number of fitted parameters is needed there.

## The first injection: why it is small and usually excluded
id: ds-first-injection
kind: design
topics: first injection, artifact
basis: general
status: draft
matches: first injection smaller, initial injection artifact, 0.4 µL first injection, diffusion from syringe tip, exclude first peak, first point outlier

Key points: During thermal equilibration and stirring, ligand diffuses from the syringe tip into the cell, so the first injection delivers less ligand than nominal and its heat is lower than expected. This is why a small initial injection (about 0.2 to 0.5 µL) is programmed and normally excluded from the fit.
Discriminating: a reduced first peak with ordinary later peaks is expected; an unusually large first peak, or unusual shape, suggests a bubble, a leak, or a ligand-in-syringe stability problem.
Bearing: including the first injection biases ΔH and n slightly; excluding it is routine and normally intentional. A fit with the first injection retained should be compared before concluding that its inclusion matters.

## Reverse (inverted) titrations: when to put the macromolecule in the syringe
id: ds-reverse-titration
kind: design
topics: reverse titration, orientation
basis: mixed
status: draft
matches: reverse titration, inverse titration, macromolecule in syringe, ligand in cell, swap orientation, multivalent order of addition, ligand solubility limits, precipitation avoidance
cite: dam_2016

Approach: Reverse titrations are used when the ligand has limited solubility in the syringe, when the ligand is scarce or very precious, when ligand self-association makes dilution heats large, when a multivalent partner would crosslink and precipitate if titrated into excess ligand sites, and to test stoichiometry (the apparent n becomes the reciprocal).
Cautions: macromolecule dilution heats and viscosity can be larger; the macromolecule must be soluble at the higher syringe concentration; models written for the macromolecule in the cell (for example sequential binding) may not apply.
Bearing: a reverse titration gives complementary, not automatically complete, information about complex systems; it is a useful test of stoichiometry and multivalency but does not by itself fix all microscopic parameters.

## Buffer matching, dialysis and pH control: why small mismatches generate heats
id: ds-buffer-matching
kind: design
topics: buffer matching, pH, DMSO, mixing heats
basis: mixed
status: draft
matches: buffer mismatch, dialysis, matched buffer, DMSO mismatch, pH mismatch, mixing heat, dilution heat large, ionizable ligand pH, unbuffered ligand, large offset, ligand dissolved in water instead of buffer, protein dialyzed but ligand not
cite: sun_panD_2020, nature_primer_2023

Approach: Place both partners in the same final buffer (final dialysate or size-exclusion buffer), dissolve ligands in that buffer, and match DMSO or other cosolvents to within about 0.1 % (v/v). Check the pH of syringe and cell solutions after preparation and adjust ligand stocks, especially acids or bases.
Why it matters: mixing a syringe and cell solution that differ in cosolvent, salt, pH, glycerol, reductant or detergent produces a heat of mixing that is not constant per injection; for low-ΔH interactions it can equal or exceed the binding heat. Mismatch shows as a large or drifting offset, large first injections and plateau heats that do not match a ligand-into-buffer control.
Ionizable ligands: dissolving an acid or base in weak buffer shifts pH on injection, and the buffer's neutralization heat contaminates the signal; use at least 20 to 50 mM buffer and match pH.
Bearing: mismatch heats bias ΔH, Kd and n and cannot be diagnosed from residual structure alone; a matched control titration is the direct test.

## Reductants, glycerol, detergents, imidazole and other additives: the heats they add
id: ds-additives
kind: design
topics: additives, TCEP, DTT, glycerol, detergents, imidazole
basis: general
status: draft
matches: TCEP versus DTT, reducing agent ITC, glycerol viscosity, detergent micelles heat, imidazole carryover, EDTA, high salt dilution heat, additives in ITC buffer

Key points: TCEP is generally preferred over DTT or β-mercaptoethanol, whose oxidation and interactions with metals can add heat and baseline drift; TCEP is acidic and must be pH-neutralized in a buffer of adequate capacity, and it binds some metals.
Glycerol and sucrose raise viscosity and mixing heats; matching concentration in cell and syringe is important, and low percentages are better.
Detergents are safest below their CMC; above it, micelle dilution heats appear and must be matched in both solutions.
Imidazole carried over from nickel affinity elution has a large ionization enthalpy (about +37 kJ/mol) and coordinates metals, so dialysis is needed.
High salt concentrations and chelators (EDTA) produce large dilution or binding heats if not identical in both solutions.

## Sample quality and concentration accuracy: the dominant source of systematic error in n and Kd
id: ds-concentration-accuracy
kind: design
topics: concentration, purity, active fraction
basis: mixed
status: draft
matches: protein concentration accuracy, extinction coefficient error, active fraction, purity, concentration determination, n deviates, inactive protein, aggregated sample, nucleic acid contamination
cite: tellinghuisen_2011, gruner_itc_impurities_2014

Key points: Fitted n scales with the ratio of the true to assumed concentrations: n_fit ≈ n_true × (M_true/M_assumed) × (L_assumed/L_true). ΔH is determined mainly by the syringe concentration (heat per mole injected); Kd shifts more modestly with either. None of these errors worsen the fit residuals.
Sources of error: computed extinction coefficients are often off by 5 to 10 %; light scattering and nucleic acid contamination inflate A280; tags and chromophores change ε; protein can be partially inactive, aggregated, or pre-bound to a co-purified ligand, metal or nucleotide; small-molecule stocks can be hydrated, impure or partly degraded; compounds in DMSO can absorb water.
Practice: measure concentration by two methods when n matters, centrifuge or filter samples, check oligomeric state (SEC, DLS), and test activity independently. Changing n with stable Kd and ΔH is a pattern that favors effective-concentration or activity explanations.

## Peptide ligands: net peptide content, TFA counterions, and concentration determination
id: ds-peptide-concentration
kind: design
topics: peptides, concentration, TFA
basis: general
status: draft
matches: peptide concentration, net peptide content, TFA salt, peptide purity, gravimetric concentration error, n too high peptide, amino acid analysis, A205 peptide, peptide solubility

Key points: Lyophilized peptides contain counterions (often trifluoroacetate) and water, so net peptide content may be only about 50 to 80 % of the weighed mass; concentrations from weight alone can be 20 to 50 % too high, which inflates the apparent n and distorts ΔH.
Better routes: quantitative amino acid analysis, absorbance of aromatic residues or peptide-bond absorbance calibrated against a standard, or quantitative NMR.
Other issues: TFA is acidic and can shift pH in weakly buffered peptide solutions; matched dialysis is impossible for short peptides, so dissolve them in the final dialysate and adjust pH; charged or hydrophobic peptides can adsorb to surfaces or aggregate at syringe concentrations; phosphopeptides are prone to dephosphorylation by contaminating phosphatases.

## Small-molecule ligands: solubility, aggregation, DMSO and nonspecific heats
id: ds-small-molecule-handling
kind: design
topics: small molecules, aggregation, DMSO
basis: general
status: draft
matches: small molecule solubility, colloidal aggregation, DMSO stock, compound precipitation, promiscuous aggregator, ligand self association, n much less than one, cloudy syringe

Key points: Stock solutions in DMSO are diluted into buffer; the final DMSO content must be identical in syringe and cell. Compounds can precipitate or form colloidal aggregates in aqueous buffer at tens to hundreds of µM, producing unusual stoichiometry (n far below 1), irreproducible heats, or heat that scales with concentration instead of saturating.
Checks: visual inspection and light scattering of the syringe solution, a ligand-into-buffer control, and (in orthogonal assays) sensitivity to detergent.
Syringe concentrations used in ITC are often near or above aqueous solubility; the apparent syringe concentration may therefore be lower than the nominal one, which inflates apparent n.

## Control titrations: ligand into buffer, buffer into macromolecule, macromolecule into buffer
id: ds-controls-blanks
kind: design
topics: controls, blank subtraction, dilution heats
basis: mixed
status: draft
matches: control titration, blank titration, ligand into buffer, heat of dilution control, buffer into protein, subtract blank, constant offset versus blank, baseline heat
cite: tellinghuisen_2011

Key points: Ligand into buffer measures ligand dilution and mixing heat and is the standard control. Buffer into macromolecule measures the effect of diluting the cell contents (usually small). Macromolecule into buffer measures macromolecule dilution heat and is needed in reverse titrations or for dissociating oligomers.
When to use a blank: a constant offset parameter is adequate if dilution heat is small and constant. If control heats are large, change with injection number or concentration (self-association, DMSO, micelle dilution), or if the titration does not reach a plateau (low c), a measured blank with identical injection scheme and concentrations is more reliable than a fitted constant.
Cautions: subtracting a noisy blank adds its noise to the data; the blank must match the buffer and concentrations of the real titration, and an over-subtracted blank turns late heats opposite in sign.

## Choosing the temperature: signal, stability, relevance and ΔCp
id: ds-temperature-selection
kind: design
topics: temperature, ΔCp, signal
basis: mixed
status: draft
matches: which temperature, 25 versus 37 °C, ΔH near zero temperature change, ΔCp series design, temperature stability of sample, temperature effects on c, multi temperature design, how many temperatures are needed for a reliable heat capacity
cite: hierarchical_idp_2025

Approach: 25 °C is the usual default. A temperature with larger |ΔH| improves signal; with negative ΔCp, ΔH becomes more negative on warming, so warming can rescue a weak signal, but Kd, c-value, sample stability and linked-buffer behavior change with temperature.
For ΔCp, use at least three to four temperatures spanning at least 10 to 15 K, with identical buffer composition at each (buffers such as Tris shift pH noticeably with temperature; phosphate shifts little). Slope uncertainty is approximately σ(ΔH)/(0.75 × span) for four evenly spaced points, so a span of 15 K with σ(ΔH) of 1 kJ/mol gives about ±0.09 kJ/(mol K).
Avoid temperatures near protein unfolding or lipid phase transitions, and degas samples at higher temperatures to avoid bubbles.

## Stirring speed, reference power and feedback mode: effects on baseline, peak shape and fragile samples
id: ds-stirring-feedback
kind: design
topics: instrument settings, stirring, feedback
basis: mixed
status: draft
matches: stirring speed rpm, reference power, feedback mode, high feedback low feedback, peak shape instrument, clipped peaks, paddle shear, fragile protein stirring
cite: maruno_stirring_itc_2020

Key points: Typical stirring is about 750 rpm on small-cell instruments and about 300 rpm on large-cell ones. Faster stirring improves mixing but adds noise and shear that can unfold or aggregate fragile proteins and perturb vesicles; reported enthalpies can depend on stirring in such systems.
Reference power sets the baseline power and the dynamic range; with strongly exothermic or large injections a reference power that is too low lets the signal run to its limit and clips peaks.
Feedback mode changes the instrument response and therefore peak width, not the integrated heat, provided the signal returns to baseline before the next injection; peak-width differences between feedback modes are instrument-response effects, not binding kinetics.

## Instrument calibration and validation: electrical calibration, standard reactions and cell volume
id: ds-calibration
kind: design
topics: calibration, cell volume, standard reaction
basis: mixed
status: draft
matches: calibration of ITC, standard reaction, test reaction, cell volume calibration, instrument performance check, systematic n bias, electrical calibration, heat calibration
cite: tellinghuisen_2004_volume, tellinghuisen_2007_calibration
reading: standard reactions include BaCl2 with 18-crown-6, CaCl2 with EDTA, and RNase A with 2'CMP; check values in instrument documentation

Key points: Instruments are electrically calibrated for heat; their effective cell volume and syringe delivery volume carry additional calibration uncertainty. Volume errors shift n and K systematically in a titration without worsening the fit. Heat of dilution of NaCl has been proposed as a practical check of heat and cell volume.
A standard reaction measured periodically (for example barium chloride into 18-crown-6, calcium chloride into EDTA, or the RNase A and 2'CMP pair) detects instrument drift and operator problems before a precious sample is used.
Consistent n deviation across otherwise well-behaved 1:1 systems on the same instrument points to a volume or calibration contribution.

## Replicates: independent preparations, how many are needed, and how to combine them
id: ds-replicates
kind: design
topics: replicates, reproducibility
basis: general
status: draft
matches: how many replicates, independent preparations, replicate ITC, mean and standard deviation, combine replicates, technical versus biological replicate, reproducibility

Key points: Replicates are most informative when they use independently prepared dilutions (and ideally independent protein batches); repeated fits of one thermogram or titrations from one pipetted stock mainly test instrument and fitting reproducibility.
At least duplicates, preferably triplicates, are used for reported values. Combine by averaging fitted parameters with their SD, or by global fitting with shared Kd and ΔH and free n or offsets; each treatment answers a different question.
In a temperature series, replicates at least at one temperature give an empirical estimate of scatter that propagates to ΔCp.

## Lipid vesicle and membrane titrations: vesicle size, outer-leaflet accessibility, partition models and controls
id: ds-membrane-vesicles
kind: design
topics: membranes, liposomes, partitioning
basis: mixed
status: draft
matches: liposome titration, vesicle ITC, lipid into peptide, peptide into lipid, partition coefficient, LUV SUV, outer leaflet, membrane binding design, Gouy Chapman
cite: rajarathnam_rosgen_membrane_2014, moreno_sds_popc_2010, henriksen_mastoparan_2011

Approach: Peptide or amphiphile into vesicles, or vesicles into peptide, are both used; the vesicle concentration series gives a partition coefficient Kp (often per lipid) rather than a stoichiometric binding constant. With large unilamellar vesicles (about 100 nm) roughly half of the lipid is on the accessible outer leaflet, a larger fraction for small ones, so effective lipid concentration must be corrected unless the peptide permeates.
Electrostatics: for charged peptides on anionic membranes the surface potential makes the apparent Kp concentration-dependent; models separate the intrinsic partition from the electrostatic term.
Controls: lipid-into-buffer for vesicle dilution (including fusion or leakage), and buffer-into-peptide. Heats can combine partitioning, pore formation, aggregation and micellization, so sign changes or non-monotonic heats mean a single binding model is not appropriate without separate evidence.

## Membrane protein and detergent ITC: matching detergent, CMC and micelle heats
id: ds-membrane-proteins-detergent
kind: design
topics: membrane proteins, detergents, nanodiscs
basis: mixed
status: draft
matches: membrane protein ITC, detergent matched, CMC dilution heat, micelle, GPCR in detergent, nanodisc ITC, low protein concentration, hydrophobic ligand partition into micelle
cite: rajarathnam_rosgen_membrane_2014, vu_membrane_itc_2021, sugiki_cert_nanodisc_nmr_itc_2022

Approach: Keep the detergent concentration (and lipid or additive composition) identical in syringe and cell; above the CMC micelles dilute and demix, giving heats that look like binding. Low-CMC detergents reduce this. Protein concentrations are usually limited (a few to tens of µM), so high-ΔH interactions are favored.
Pitfalls: hydrophobic ligands partition into micelles, lowering the free ligand and shifting apparent Kd; membrane protein samples contain bound lipid and detergent; protein stability over the experiment is limited.
Controls: ligand into detergent buffer, and detergent-matched macromolecule dilution.

## Kinetic ITC and enzyme assays by calorimetry: single- and multiple-injection designs
id: ds-kinetic-itc
kind: design
topics: kinetics, enzymes, kinetic ITC
basis: mixed
status: draft
matches: enzyme kinetics ITC, kinetic ITC, single injection assay, thermal power turnover, Michaelis Menten calorimetry, inhibitor kinetics ITC, kon koff ITC, rate from heat flow
cite: hsEH_single_injection_2019, ditrani_inhibitor_kinetics_2018, vandermeulen_association_kinetics_2016

Approach: The thermal power after substrate injection is proportional to reaction rate times apparent enthalpy: P(t) = V · ΔH_app · v(t). A single injection can yield a progress curve from which kcat and Km are obtained, and multiple injections can scan substrate concentration. The apparent enthalpy is obtained from a complete conversion experiment under identical conditions.
Requirements: the instrument response time (seconds) must be small relative to the process or be deconvolved; product inhibition, buffer protonation heats and substrate dilution heats must be controlled.
Reading a thermogram that does not return to baseline after substrate injection: the experiment reports catalytic turnover and requires kinetic models, not an equilibrium binding isotherm. Time-resolved association and inhibitor kinetics can also be extracted from the response curve.

## Dilution (dissociation) ITC of oligomers and complexes: design, baseline and model
id: ds-dissociation-itc
kind: design
topics: self-association, dissociation, dilution
basis: mixed
status: draft
matches: dilution ITC, dimer dissociation, oligomer dilution, self association constant, injecting concentrated oligomer into buffer, demonstrating monomer dimer equilibrium, heptamer, dissociation constant by dilution
cite: cpn10_self_association_2005

Approach: A concentrated, associated solution is injected into buffer; heat arises as the dilution shifts the equilibrium toward monomers, and heats decrease as the cell concentration builds up. The syringe concentration should be well above the dissociation midpoint (several times) so the first injections start from a mostly associated state; the cell holds the same buffer.
Requirements: an oligomerization model (monomer-dimer, monomer-heptamer, isodesmic) and an independently justified dilution baseline; the heat per injection includes both dissociation and ordinary dilution of the monomer.
Bearing: tight oligomers (nM Kd) give very small heats at attainable concentrations; the fitted ΔH is for dissociation and has the opposite sign of the assembly enthalpy; slow dissociation or dissociation kinetics longer than the injection spacing can distort apparent values.

## Multivalent samples and avidity: design to avoid crosslinking and precipitation
id: ds-multivalent-design
kind: design
topics: multivalency, avidity, crosslinking
basis: mixed
status: draft
matches: multivalent binding design, avidity, crosslinking precipitation, bivalent ligand, polymer ligand, IgG bivalent, valence, functional valence, effective stoichiometry
cite: dam_2016, survivin_2021

Approach: Multivalent reactants can bridge and crosslink, giving heterogeneous complexes, precipitation or slow assembly. Titration orientation, concentration and ionic strength control this. Using monovalent fragments (Fab versus IgG, single domains) simplifies interpretation; the divalent form then provides the avidity comparison.
Reading n: the fitted stoichiometry reflects the number of functionally engaged sites, which can be lower than the structural valence (steric hindrance, overlapping sites) or non-integer; changes in n with ligand length or pH are not automatic proof of creating or destroying sites.
Warning signs: slow baselines, noise from particulates, irreproducible heats, and a cloudy cell after the run.
