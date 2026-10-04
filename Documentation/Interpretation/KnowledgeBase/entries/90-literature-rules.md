<!-- Migrated from legacy/itc_knowledge_base_expanded.json by tools/migrate_legacy.py.
     These files are now the source of truth; edit them directly. -->

## Very high c-value with a near-vertical transition: Kd is poorly identifiable even when n and ΔH are well defined
id: lr-c-value-high
kind: literature
basis: literature
status: draft
matches: high c, c above 1000, steep isotherm, step-like, tight binding, nanomolar Kd, Kd upper bound, fit converged but Kd imprecise
cite: wiseman_1989, fam118_2025, brautigam_2016
legacy_id: rule_c_value_high

Situation: c-value very high and isotherm transition is nearly vertical.
Interpretive implication: Affinity may be weakly identifiable even if n and ΔH are well constrained.
Defensible framing: The transition is consistent with high-affinity binding, but the curve is too steep to determine Kd precisely under these concentrations.
Common over-claim: The fitted nanomolar Kd is precise because the fit converged.

## Low c-value (below about 1–10): n and ΔH strongly correlated, stoichiometry weakly constrained
id: lr-c-value-low
kind: literature
basis: literature
status: draft
matches: low c, shallow isotherm, weak binding, no plateau, n-ΔH correlation, fixed stoichiometry, Wiseman c
cite: wiseman_1989, tellinghuisen_2007_variable, brautigam_2016
legacy_id: rule_c_value_low

Situation: c-value low, especially below about 1–10.
Interpretive implication: Stoichiometry and enthalpy can become strongly correlated; Kd may still be estimable with appropriate titration range, but n should not be overinterpreted.
Defensible framing: The affinity may be estimable, but stoichiometry is weakly constrained under this low-c design.

## Binding enthalpy near zero: ITC is insensitive although a real interaction may exist
id: lr-zero-enthalpy
kind: literature
basis: literature
status: draft
matches: ΔH close to zero, flat thermogram, silent ITC, no heat, small heats, change temperature, heat capacity
cite: hierarchical_idp_2025, nature_primer_2023
legacy_id: rule_zero_enthalpy

Situation: Binding enthalpy is close to zero relative to experimental heat uncertainty.
Interpretive implication: ITC can become insensitive even for a real interaction.
Defensible framing: The absence of a clear heat signal at this temperature does not exclude binding; the affinity is not reliably recoverable from these ITC data.

## Endothermic binding (ΔH > 0) with ΔG < 0 is entropy-driven, not weak or unfavorable
id: lr-endothermic-binding
kind: literature
basis: literature
status: draft
matches: positive enthalpy, endothermic, entropy-driven, favorable -TΔS, desolvation, hydrophobic
cite: physical_networks_2021, nature_primer_2023
legacy_id: rule_endothermic_binding

Situation: ΔH > 0 but ΔG < 0.
Interpretive implication: Binding is thermodynamically favorable because entropy compensates for the endothermic enthalpy.
Defensible framing: Binding is entropy-driven under these conditions.
Common over-claim: The positive enthalpy indicates unfavorable or weak binding.

## Exothermic binding (ΔH < 0): enthalpy alone does not identify hydrogen bonds, electrostatics or any specific mechanism
id: lr-exothermic-not-mechanism
kind: literature
basis: literature
status: draft
matches: negative enthalpy, enthalpy-driven, hydrogen bonding, van der Waals, mechanism from ΔH, molecular origin
cite: nature_primer_2023, sun_panD_2020, trimethyllysine_2015
legacy_id: rule_exothermic_not_mechanism

Situation: ΔH < 0.
Interpretive implication: Exothermicity alone does not uniquely identify hydrogen bonding, electrostatics, van der Waals interactions, conformational changes, proton linkage, or desolvation.
Defensible framing: The interaction is enthalpically favorable; assigning a specific molecular origin requires comparison or orthogonal evidence.

## Favorable entropy term is not by itself proof of hydrophobic binding
id: lr-entropy-not-hydrophobic-proof
kind: literature
basis: literature
status: draft
matches: positive TΔS, entropy-driven, hydrophobic effect, solvent release, ion release, conformational entropy
cite: nature_primer_2023, galectin_jacsau_2021
legacy_id: rule_entropy_not_hydrophobic_proof

Situation: Favorable entropy contribution.
Interpretive implication: A favorable entropy term can arise from solvent release, ion release, conformational effects, or other changes; it is not by itself proof of hydrophobic binding.

## Uncertain syringe (titrant) concentration biases Kd and ΔH without worsening the fit
id: lr-concentration-error-titrant
kind: literature
basis: literature
status: draft
matches: syringe concentration error, titrant concentration, systematic error, biased Kd, concentration accuracy
cite: tellinghuisen_2011
legacy_id: rule_concentration_error_titrant

Situation: Uncertain syringe/titrant concentration.
Interpretive implication: For standard 1:1 analysis, titrant concentration error can bias K and ΔH without worsening least-squares residuals.
Defensible framing: The statistical fit cannot diagnose this concentration error; parameter accuracy depends on the concentration accuracy.

## Fitted n deviates from the expected stoichiometry in a simple 1:1 system: suspect cell concentration, inactive fraction or cell volume first
id: lr-concentration-error-cell
kind: literature
basis: literature
status: draft
matches: n not 1, n 0.7, n 1.3, concentration error, inactive protein, active fraction, cell volume
cite: tellinghuisen_2011, tellinghuisen_2004_volume, brautigam_2016
legacy_id: rule_concentration_error_cell

Situation: Fitted n deviates from expected stoichiometry in a simple validated 1:1 system.
Interpretive implication: Cell concentration error, inactive fraction, or effective cell-volume error are plausible explanations before invoking unusual stoichiometry.

## Non-integer or unexpected fitted n: interpret by model and system (concentration error, functional valence, heterogeneity, effective stoichiometry)
id: lr-n-context
kind: literature
basis: literature
status: draft
matches: fractional stoichiometry, unexpected n, n 0.5, n 2, apparent stoichiometry, valence
cite: brautigam_2016, dam_2016, survivin_2021
legacy_id: rule_n_context

Situation: Non-integer or unexpected fitted n.
Interpretive implication: Interpret n according to the model and system. It may reflect concentration error/inactive material, genuine functional valence, heterogeneous accessibility, or an effective stoichiometry.

## A small baseline or heat offset relative to early injection heats can shift K and ΔH materially
id: lr-baseline-small-offset
kind: literature
basis: literature
status: draft
matches: baseline offset, constant heat, dilution offset, small offset, K bias, ΔH bias, 1.4 percent offset
cite: tellinghuisen_2011
legacy_id: rule_baseline_small_offset

Situation: Small baseline/heat offset relative to early injection heat.
Interpretive implication: Even a small baseline offset can materially shift K and ΔH.
Evidence note: Tellinghuisen reported cases where a 1.4% offset changed K by ~11%, and an ~11% offset changed K by ~50%.

## Late-injection or blank heats that vary systematically: a constant offset is inadequate
id: lr-dilution-not-always-constant
kind: literature
basis: literature
status: draft
matches: heat of dilution, blank subtraction, concentration-dependent dilution, late injections not zero, drifting late heats
cite: tellinghuisen_2011, sigurskjold_2000
legacy_id: rule_dilution_not_always_constant

Situation: Late heats or blank heats vary systematically with injection/concentration.
Interpretive implication: A constant heat offset may be inadequate; explicit blank subtraction or a concentration-dependent dilution model may be needed.

## Structured residuals (runs, curvature, phase-specific structure): model, concentrations or baseline treatment are inadequate
id: lr-residual-systematic
kind: literature
basis: literature
status: draft
matches: non-random residuals, systematic residuals, residual pattern, runs test, poor fit, misfit
cite: brautigam_2016
legacy_id: rule_residual_systematic

Situation: Residuals show sustained runs, curvature, phase-specific structure, or other systematic deviation.
Interpretive implication: The chosen model, concentrations, baseline treatment, or experimental assumptions may be inadequate.
Defensible framing: The residuals are not randomly distributed around zero, so the fitted model should not be treated as a complete description of the data.

## Excellent fit statistics and random residuals support internal consistency, not accuracy
id: lr-good-residual-not-accuracy
kind: literature
basis: literature
status: draft
matches: good fit, small RMSD, random residuals, systematic error, concentration scaling, precision versus accuracy
cite: tellinghuisen_2011
legacy_id: rule_good_residual_not_accuracy

Situation: Excellent fit statistics and random residuals.
Interpretive implication: This supports internal consistency but does not rule out systematic errors such as concentration scaling.

## Two-site or multisite fit with very large uncertainties and correlations: heterogeneity supported, microscopic constants not identified
id: lr-multisite-underidentification
kind: literature
basis: literature
status: draft
matches: two-site model, multisite, large confidence intervals, correlated parameters, unidentifiable, overfitting
cite: brautigam_2016, adar1_zrna_2021
legacy_id: rule_multisite_underidentification

Situation: Two-site or multisite fit has very large parameter uncertainties/correlations.
Interpretive implication: The data may support heterogeneous/complex binding without uniquely identifying microscopic site constants.

## A more complex model fits better: statistical fit alone does not establish the physical mechanism
id: lr-model-fit-not-validation
kind: literature
basis: literature
status: draft
matches: model selection, AICc, extra parameters, better fit, overfitting, mechanism validation
cite: brautigam_2016, adar1_zrna_2021
legacy_id: rule_model_fit_not_validation

Situation: A more complex binding model fits.
Interpretive implication: Statistical fit alone does not establish the physical binding mechanism; independent structural, stoichiometric, or orthogonal evidence is needed.

## Multiple titrations sharing physical parameters: global analysis improves precision and identifiability
id: lr-global-analysis
kind: literature
basis: literature
status: draft
matches: global fit, joint fit, shared parameters, multiple experiments, suboptimal isotherms, SEDPHAT
cite: brautigam_2016
legacy_id: rule_global_analysis

Situation: Multiple titrations share physical parameters.
Interpretive implication: Global analysis can improve precision and identifiability and can use partial/suboptimal isotherms productively.

## Reverse titration for complex or multivalent binding gives complementary information but does not guarantee identification of all parameters
id: lr-reverse-orientation
kind: literature
basis: literature
status: draft
matches: reverse titration, swapped orientation, ligand in cell, protein in syringe, multivalent
cite: dam_2016
legacy_id: rule_reverse_orientation

Situation: Complex or multivalent binding where orientation may change observable macroscopic behavior.
Interpretive implication: Reverse titration can provide complementary information but is not guaranteed to identify all microscopic parameters.
Strength of support: medium.

## Multivalent reactants can crosslink and precipitate: baseline and heats are corrupted and parameters unreliable
id: lr-multivalent-precipitation
kind: literature
basis: literature
status: draft
matches: precipitation, crosslinking, multivalent, aggregate, cloudy cell, avidity
cite: dam_2016
legacy_id: rule_multivalent_precipitation

Situation: Multivalent reactants can crosslink and precipitate.
Interpretive implication: Precipitation can corrupt baseline and heat signals; thermodynamic parameters should be considered unreliable unless precipitation is prevented or demonstrably negligible.

## Large or fractional n in a multivalent system: structural valence versus functional valence
id: lr-high-functional-valence
kind: literature
basis: literature
status: draft
matches: multivalent, functional valence, number of motifs, fractional n, avidity, steric hindrance
cite: dam_2016, survivin_2021
legacy_id: rule_high_functional_valence

Situation: Large or fractional stoichiometry in multivalent system.
Interpretive implication: Structural valence and functional valence may differ; do not force n to the nominal number of motifs without evidence.
Strength of support: medium.

## Direct titration too steep to measure tight binding: competitive displacement ITC brings the affinity into range
id: lr-displacement-high-affinity
kind: literature
basis: literature
status: draft
matches: displacement ITC, competition, tight binder, picomolar, nanomolar, weak competitor, Sigurskjold
cite: sigurskjold_2000
legacy_id: rule_displacement_high_affinity

Situation: Direct titration is too steep to identify high affinity.
Interpretive implication: Competitive/displacement ITC can move the apparent affinity into a measurable range if the competitor thermodynamics are known.

## Competitive/displacement ITC needs sufficiently different enthalpy signatures for target and competitor
id: lr-displacement-enthalpy-contrast
kind: literature
basis: literature
status: draft
matches: displacement ITC, competitor, enthalpy difference, no heat displacement, competition design
cite: sigurskjold_2000
legacy_id: rule_displacement_enthalpy_contrast

Situation: Competitive/displacement ITC.
Interpretive implication: The competing reactions need sufficiently different enthalpy signatures for a strong calorimetric signal.

## ΔH changes substantially across buffers with different ionization enthalpies: proton uptake or release is plausible and quantifiable
id: lr-buffer-proton-linkage
kind: literature
basis: literature
status: draft
matches: buffer dependence, Tris versus phosphate, HEPES, proton linkage, ionization enthalpy, protonation
cite: brautigam_2016, nature_primer_2023
legacy_id: rule_buffer_proton_linkage

Situation: Binding enthalpy changes substantially across buffers with different ionization enthalpies.
Interpretive implication: Binding-linked proton uptake/release is plausible and can be quantified with global buffer-ionization analysis.

## Ionizable ligand or a pH difference between syringe and cell: protonation heats and buffer neutralization contaminate the signal
id: lr-ionizable-ligand-ph
kind: literature
basis: literature
status: draft
matches: pH mismatch, acidic ligand, carboxylic acid, unbuffered, protonation heat, buffer neutralization
cite: sun_panD_2020, nature_primer_2023
legacy_id: rule_ionizable_ligand_pH

Situation: Ligand is ionizable or sample pH differs between syringe and cell.
Interpretive implication: Observed heat may include protonation/deprotonation and buffer neutralization; pH matching is essential.

## ΔH measured at several temperatures: its slope gives ΔCp if the same binding process applies throughout
id: lr-temperature-deltacp
kind: literature
basis: literature
status: draft
matches: heat capacity, ΔCp, temperature series, ΔH versus T, multi-temperature ITC
cite: nature_primer_2023, brautigam_2016
legacy_id: rule_temperature_deltaCp

Situation: ΔH measured at multiple temperatures.
Interpretive implication: The slope of ΔH versus temperature can provide ΔCp if the same binding process/model applies across temperatures.

## Binding heat too small at one temperature: changing temperature can restore an informative signal
id: lr-temperature-rescue-signal
kind: literature
basis: literature
status: draft
matches: small heats, near-zero ΔH, change temperature, increase |ΔH|, heat capacity, sample stability
cite: hierarchical_idp_2025
legacy_id: rule_temperature_rescue_signal

Situation: Binding heat is too small at one temperature.
Interpretive implication: Changing temperature may increase |ΔH| and make ITC informative, provided sample stability and model assumptions remain valid.

## A later sequential step has a more favorable ΔH: not positive cooperativity unless the affinity is also higher
id: lr-affinity-vs-enthalpy-cooperativity
kind: literature
basis: literature
status: draft
matches: cooperativity, sequential binding, enthalpy versus affinity, positive cooperativity, two-site
cite: potassium_jacs_2022
legacy_id: rule_affinity_vs_enthalpy_cooperativity

Situation: Later sequential binding step has more favorable ΔH.
Interpretive implication: Do not call this positive cooperativity unless the corresponding binding free energy/association constant is more favorable.

## ITC parameters agree with BLI, SPR, NMR, functional inhibition or structural evidence: confidence in the interpretation increases
id: lr-orthogonal-confirmation
kind: literature
basis: literature
status: draft
matches: orthogonal method, SPR, BLI, NMR, agreement, validation, independent confirmation
cite: sun_panD_2020, oep21_2023, adar1_zrna_2021
legacy_id: rule_orthogonal_confirmation

Situation: ITC parameters agree with BLI/SPR/NMR/functional inhibition or structural evidence.
Interpretive implication: Confidence in the biological/chemical interpretation increases, especially when the methods have different systematic limitations.

## ITC fits one site but another technique detects a much weaker second site
id: lr-weak-secondary-site
kind: literature
basis: literature
status: draft
matches: weak second site, secondary site, one-site model, NMR detects, millimolar, below ITC sensitivity
cite: oep21_2023
legacy_id: rule_weak_secondary_site

Situation: ITC fits one site but an orthogonal technique detects a substantially weaker second site.
Interpretive implication: ITC's effective model may describe the dominant calorimetric transition without excluding weaker binding modes.

## Flat isotherm or no binding detected: no heat-associated binding was seen, which is not proof of no interaction
id: lr-no-binding-detected
kind: literature
basis: literature
status: draft
matches: no binding, flat isotherm, no heat, negative result, null result, no interaction
cite: oep21_2023, hierarchical_idp_2025
legacy_id: rule_no_binding_detected

Situation: Flat isotherm or no binding detected.
Interpretive implication: Report that no heat-associated binding was detected under the tested conditions; do not infer absolute absence of interaction.

## Isotherm lacks the expected approach to saturation or has a visibly implausible shape: downgrade confidence in quantitative parameters
id: lr-pathological-shape
kind: literature
basis: literature
status: draft
matches: non-sigmoidal, no saturation, weird isotherm, baseline pathology, buffer mismatch, aggregation, bad titration
cite: cdin1_2026, tellinghuisen_2011, brautigam_2016
legacy_id: rule_pathological_shape

Situation: Isotherm lacks expected approach to saturation/baseline or otherwise has visibly implausible shape.
Interpretive implication: Downgrade confidence in quantitative parameters and consider buffer mismatch, dilution heat, aggregation, precipitation, baseline errors, or model mismatch.

## Fitted Kd changes systematically with macromolecule concentration: self-association, aggregation or linked equilibria
id: lr-concentration-series
kind: literature
basis: literature
status: draft
matches: concentration dependence of Kd, Kd varies with concentration, self-association, aggregation, nonideality
cite: wiseman_1989
legacy_id: rule_concentration_series

Situation: Fitted Kd changes systematically with macromolecule concentration.
Interpretive implication: Consider self-association, aggregation, linked equilibria, or concentration-dependent nonideality before interpreting the values as a single intrinsic Kd.
Strength of support: medium.

## Consistent stoichiometry bias across otherwise well-behaved systems: effective cell volume calibration
id: lr-cell-volume-systematic
kind: literature
basis: literature
status: draft
matches: cell volume, n shifts, calibration, systematic n bias, active volume
cite: tellinghuisen_2004_volume, tellinghuisen_2007_calibration
legacy_id: rule_cell_volume_systematic

Situation: Consistent stoichiometry bias across otherwise well-behaved simple systems.
Interpretive implication: Effective cell-volume calibration can produce systematic n and K shifts and should be considered in precision work.
Strength of support: medium.

## Baseline noise differs strongly between injections: per-injection integration uncertainty should weight the fit
id: lr-integration-uncertainty
kind: literature
basis: literature
status: draft
matches: peak integration error, injection SD, weighted fit, heteroscedastic, noisy baseline, NITPIC
cite: keller_2012, brautigam_2016
legacy_id: rule_integration_uncertainty

Situation: Baseline noise differs strongly between injections.
Interpretive implication: Per-injection integration uncertainty should influence weighting and final parameter confidence.

## Baseline manually adjusted after viewing the desired fit: circular analysis and user-induced bias
id: lr-manual-baseline-bias
kind: literature
basis: literature
status: draft
matches: manual baseline, baseline editing, integration bias, circular analysis, operator bias
cite: keller_2012, brautigam_2016
legacy_id: rule_manual_baseline_bias

Situation: Baseline is manually adjusted after viewing the desired fit.
Interpretive implication: There is a risk of circular analysis and user-induced bias.

## Continuous or single-injection ITC: imperfect mixing and injection-rate limits bias fitted thermodynamics
id: lr-single-injection-mixing
kind: literature
basis: literature
status: draft
matches: single injection ITC, continuous injection, mixing, injection rate, cell volume as correction
cite: dumas_2022
legacy_id: rule_single_injection_mixing

Situation: Continuous/single-injection ITC.
Interpretive implication: Imperfect mixing and injection-rate limitations can bias fitted thermodynamics; an apparently adjusted cell volume may act as an empirical mixing correction in some models.
Strength of support: medium.

## Strong ITC transition but NMR or structure show persistent disorder or multiple dynamic contacts: not necessarily a rigid lock-and-key complex
id: lr-fuzzy-binding
kind: literature
basis: literature
status: draft
matches: fuzzy complex, disordered complex, IDP binding, dynamic complex, one-site fit, lock and key
cite: higa2_2024
legacy_id: rule_fuzzy_binding

Situation: Strong ITC transition but structural or spectroscopic evidence indicates persistent disorder or multiple dynamic contacts.
Interpretive implication: A good apparent affinity and one-site fit do not establish a rigid lock-and-key complex.
Defensible framing: The calorimetry supports a dominant binding transition; orthogonal data indicate that the complex may remain dynamically heterogeneous or fuzzy.

## Membrane–peptide thermogram with sign changes, non-monotonic injections or concentration-dependent background is a composite of processes
id: lr-composite-membrane-heat
kind: literature
basis: literature
status: draft
matches: lipid vesicle, peptide membrane, partitioning, pore formation, micelle, sign change, non-monotonic
cite: peptide_membrane_2011
legacy_id: rule_composite_membrane_heat

Situation: Membrane/peptide thermogram contains sign changes, non-monotonic injections, or strong concentration-dependent background.
Interpretive implication: Do not force a single equilibrium binding model when partitioning, pore formation, micellation, ion exchange, or dilution may overlap.
Defensible framing: The heat trace is composite and requires process-specific controls or orthogonal measurements before a single Kd or stoichiometry is assigned.

## Separable exothermic and endothermic components or structured one-site residuals: more than one calorimetric process
id: lr-multiple-interaction-modes
kind: literature
basis: literature
status: draft
matches: biphasic, exothermic and endothermic, two processes, multiple binding modes, composite heat
cite: kgf2_heparin_2014
legacy_id: rule_multiple_interaction_modes

Situation: One titration shows separable exothermic and endothermic components or a single-site residual pattern is systematically structured.
Interpretive implication: Distinct interaction modes may coexist; apparent n and ΔH from a one-site model can be effective averages.
Defensible framing: The data are consistent with more than one calorimetric process; component-specific parameters should be reported only if the composite model is independently justified.

## ITC dilution used for aggregate or supramolecular disassembly: the measured enthalpy is for disassembly, assembly has the opposite sign
id: lr-dilution-selfassembly-sign
kind: literature
basis: literature
status: draft
matches: dilution experiment, disassembly, self-assembly, aggregate, micelle, supramolecular, sign of enthalpy
cite: nbi_selfassembly_2019
legacy_id: rule_dilution_selfassembly_sign

Situation: ITC dilution is used to study aggregate or supramolecular disassembly.
Interpretive implication: The measured enthalpy is for the direction of the observed dilution/disassembly process; the corresponding assembly enthalpy has the opposite sign.
Defensible framing: This is an enthalpy of disassembly measured by dilution; the assembly enthalpy is equal in magnitude with reversed sign under the same convention.

## Buffer- or pH-dependent ΔH and K: observed values include linked proton uptake or release and need a linkage analysis
id: lr-buffer-linked-protonation
kind: literature
basis: literature
status: draft
matches: pH dependence, proton-coupled binding, protonation, ΔnH, buffer ionization enthalpy, pKa shift
cite: nguyen_cgp_2006, tellinghuisen_2011
legacy_id: rule_buffer_linked_protonation

Situation: The same-pH binding enthalpy changes across buffers with different ionization enthalpies, or affinity changes with pH.
Interpretive implication: Observed ΔH and K may include linked proton uptake/release and cannot be interpreted as intrinsic binding energetics without a linkage analysis.
Defensible framing: The buffer and pH dependence is consistent with proton-linked binding; compare buffers at matched pH and, where possible, fit proton uptake globally.

## Residues folding upon binding estimated from temperature-dependent ITC for an intrinsically disordered protein: calibration-dependent
id: lr-idr-spolar-record-model-dependence
kind: literature
basis: literature
status: draft
matches: Spolar Record, ΔCp, residues folding, IDP, IDR, coupled folding and binding, conformational entropy
cite: spolar_record_science_1994, theisen_jacs_2021
legacy_id: rule_idr_spolar_record_model_dependence

Situation: Using temperature-dependent ITC to estimate residues folding upon binding for an intrinsically disordered interaction.
Interpretive implication: The original globular-protein calibration can underestimate conformational entropy loss for IDR interactions; an IDR-adapted calibration may give larger estimates.
Defensible framing: The temperature series is consistent with coupled ordering, but the inferred number of residues is model-dependent and should be reported with the calibration used.

## Slow conformer interconversion or incomplete post-injection equilibration: Kd is an apparent, population- and timescale-dependent value
id: lr-apparent-kd-conformational-exchange
kind: literature
basis: literature
status: draft
matches: proline isomerization, conformational selection, slow exchange, apparent Kd, incomplete equilibration
cite: theisen_jacs_2025
legacy_id: rule_apparent_kd_conformational_exchange

Situation: Binding partners interconvert slowly between conformers with different affinities, or post-injection equilibration is incomplete.
Interpretive implication: A standard one-site Kd can be an apparent population- and timescale-dependent value rather than the affinity of one molecular species.
Defensible framing: The reported Kd is an apparent affinity under the experimental equilibration conditions; conformer-specific affinities require a kinetic or population-aware model.

## Truncation or motif isolation changes affinity: disordered context contributes through coupled folding and bivalent avidity
id: lr-disordered-context-bivalency
kind: literature
basis: literature
status: draft
matches: truncation, fragment, flanking region, IDR, bivalent, motif, context dependence
cite: theisen_jacs_2021, theisen_natcomm_2024
legacy_id: rule_disordered_context_bivalency

Situation: Truncation or motif-isolation changes affinity substantially, especially when multiple disordered motifs contact separate surfaces.
Interpretive implication: The thermodynamic contribution of an IDR context can be distributed across motif stabilization, coupled folding, and bivalent avidity; isolated-motif results may not predict the full interaction.
Defensible framing: Affinity is context-dependent in this fragment series; the isolated motif and extended construct should be treated as distinct thermodynamic systems.

## ITC Kd agrees with another equilibrium measurement but kinetics show slow association or long residence time
id: lr-equilibrium-affinity-vs-kinetics
kind: literature
basis: literature
status: draft
matches: kinetics, residence time, kon koff, slow binding, equilibrium versus kinetics, SPR
cite: prestel_lecb_2016, theisen_jacs_2025
legacy_id: rule_equilibrium_affinity_vs_kinetics

Situation: ITC Kd agrees with an orthogonal equilibrium measurement but the orthogonal kinetics show slow association or long residence time.
Interpretive implication: Equilibrium affinity and kinetic accessibility are independent dimensions of interaction behavior.
Defensible framing: The equilibrium affinity is supported, but the rate of approach to equilibrium and residence time require kinetic measurements.

## Receptor and ligand architecture supports several contacts and the isotherm has two transitions or n near two: use a multisite model guided by architecture
id: lr-multisite-architecture
kind: literature
basis: literature
status: draft
matches: two transitions, n equals 2, multisite, sequential, calmodulin, ubiquitin chains, avidity
cite: prestel_nhe1_cam_2021, nemo_2009
legacy_id: rule_multisite_architecture

Situation: A receptor/ligand architecture supports multiple contacts and the isotherm has two transitions or a fitted n near two.
Interpretive implication: Use multisite or sequential models and interpret n against molecular architecture; a single averaged Kd can conceal distinct binding events.
Defensible framing: The titration is consistent with multiple binding events whose affinities and enthalpies should be modeled separately where the data support that resolution.

## Polymer or oligosaccharide titration where n changes with ligand length or pH: effective molecular coverage
id: lr-multivalent-polymer-coverage
kind: literature
basis: literature
status: draft
matches: polymer binding, polysaccharide, alginate, coverage, effective n, ligand length, overlapping sites
cite: prestel_alginate_blg_2023
legacy_id: rule_multivalent_polymer_coverage

Situation: A polymer or oligosaccharide titration gives n values that change with ligand length or pH.
Interpretive implication: The fitted stoichiometry may represent effective molecular coverage across overlapping/dynamic sites rather than a fixed count of independent sites.
Defensible framing: The fitted n is an effective coverage under these conditions; changes with polymer length or pH should not be interpreted as direct creation or loss of discrete sites without structural support.

## Large negative ΔCp in a protein–DNA or protein–ligand interaction: not a unique report of affinity or hydrophobic burial
id: lr-delta-cp-not-affinity
kind: literature
basis: literature
status: draft
matches: large negative heat capacity, ΔCp, protein-DNA, hydrophobic burial, coupled folding
cite: spolar_record_science_1994, ladbury_trp_repressor_1994
legacy_id: rule_delta_cp_not_affinity

Situation: Large negative ΔCp is observed in a protein–DNA or protein–ligand interaction.
Interpretive implication: ΔCp magnitude does not uniquely report affinity or hydrophobic burial; weak interfaces and coupled conformational changes can also produce large negative values.
Defensible framing: The temperature dependence indicates a substantial heat-capacity change; its molecular origin and relation to affinity require structural or comparative evidence.

## Strong negative ΔH versus −TΔS correlation across a ligand series: partly mathematical, test with ΔΔ analysis
id: lr-compensation-correlation-bias
kind: literature
basis: literature
status: draft
matches: enthalpy-entropy compensation, ΔH-TΔS correlation, ligand series, compensation plot, ΔΔH ΔΔG
cite: olsson_compensation_2011
legacy_id: rule_compensation_correlation_bias

Situation: A ligand series shows a strong negative correlation between ΔH and −TΔS.
Interpretive implication: The correlation may be partly mathematical or experimental; use pairwise ΔΔ analysis and uncertainty-aware tests before calling it physical compensation.
Defensible framing: The series shows enthalpy–entropy compensation-like behavior, but the extent of physical compensation cannot be established from the raw ΔH versus −TΔS correlation alone.

## Confidence based only on a single regression fit or optimizer covariance: real uncertainty is larger
id: lr-realistic-itc-uncertainty
kind: literature
basis: literature
status: draft
matches: confidence interval, standard error, covariance, bootstrap, Bayesian, replicate variability, realistic uncertainty
cite: nguyen_bayesian_itc_2018, keller_2012
legacy_id: rule_realistic_itc_uncertainty

Situation: Parameter confidence is based only on a single nonlinear-regression fit or optimizer covariance.
Interpretive implication: Reported uncertainty may be understated when concentration, baseline, replicate, or control variability is not modeled.
Defensible framing: The fit-derived interval reflects the assumed statistical model; broader uncertainty from replicates, concentrations, and controls should be considered where available.

## Nonspecific protein–DNA binding under changed salt, pH, buffer, osmolyte or temperature: condition-dependent lattice binding
id: lr-nonspecific-dna-condition-dependence
kind: literature
basis: literature
status: draft
matches: nonspecific DNA binding, lattice binding, site size, salt dependence, Sso7d, 1:1 model inadequate
cite: lundback_sso7d_1998, lundback_sso7d_salt_1996, spolar_record_science_1994
legacy_id: rule_nonspecific_dna_condition_dependence

Situation: Nonspecific protein-DNA binding is measured under changed salt, pH, buffer, solvent, osmotic, or temperature conditions.
Interpretive implication: The observed K, ΔH, ΔS, and apparent site size can be condition-dependent and may require a lattice-binding model rather than a 1:1 model.
Defensible framing: The thermodynamics are conditional on the DNA lattice, ionic environment, and model; comparisons across conditions should not assume an invariant 1:1 site.

## Broad temperature series with nonmonotonic affinity: ΔCp and van't Hoff analysis are not constant over the range
id: lr-broad-temperature-series
kind: literature
basis: literature
status: draft
matches: temperature dependence, nonmonotonic affinity, curved van't Hoff, temperature-dependent ΔCp, wide temperature range
cite: datta_licata_taq_2003, lundback_sso7d_1998
legacy_id: rule_broad_temperature_series

Situation: ITC or orthogonal binding measurements span a broad temperature range and affinity is nonmonotonic.
Interpretive implication: A single local ΔCp or linear van't Hoff approximation may not describe the complete temperature series; coupled transitions or temperature-dependent ΔCp should be considered.
Defensible framing: The temperature series shows condition-dependent thermodynamics; ΔCp and extrapolated parameters should be interpreted locally and not assumed constant over the full range.

## DNA, RNA or hybrid duplex ITC compared across salt concentration or backbone: condition- and backbone-specific thermodynamics
id: lr-nucleic-acid-ionic-condition
kind: literature
basis: literature
status: draft
matches: duplex formation, hybridization, DNA RNA hybrid, PNA, salt dependence, ionic strength
cite: lang_schwarz_hybridization_2007, peyr_et_al_pna_dna_1999
legacy_id: rule_nucleic_acid_ionic_condition

Situation: DNA, RNA, or hybrid duplex ITC data are compared across salt concentrations or polymer backbones.
Interpretive implication: The free energy and heat-capacity change are condition- and backbone-specific; salt effects may be nonlinear in concentration variables.
Defensible framing: The measured duplex thermodynamics apply to the stated ionic activity and nucleic-acid composition; cross-condition comparisons require explicit salt and backbone corrections.

## Protein–DNA affinity or enthalpy changes with salt identity, not only concentration: Hofmeister, hydration and osmotic effects
id: lr-anion-specific-protein-dna
kind: literature
basis: literature
status: draft
matches: anion effect, chloride versus glutamate, Hofmeister, osmotic, hydration, counterion release
cite: vandermeulen_ihf_water_2008, milev_tn916_osmolytes_2005
legacy_id: rule_anion_specific_protein_dna

Situation: Protein-DNA affinity or enthalpy changes with salt identity, not only salt concentration.
Interpretive implication: Cation-independent anion effects can reflect Hofmeister partitioning, hydration, and osmotic contributions in addition to electrostatic counterion release.
Defensible framing: The perturbation is anion-specific; a simple salt-concentration or counterion-release interpretation is insufficient without considering hydration and osmotic effects.

## Thermal power keeps changing after injection because an enzymatic reaction proceeds: kinetic calorimetry, not a binding isotherm
id: lr-kinetic-itc-not-binding-isotherm
kind: literature
basis: literature
status: draft
matches: enzyme kinetics ITC, turnover, substrate injection, steady-state power, Michaelis-Menten by ITC
cite: hsEH_single_injection_2019, nguyen_bayesian_itc_2018
legacy_id: rule_kinetic_itc_not_binding_isotherm

Situation: Thermal power changes continuously after a substrate injection because an enzymatic reaction proceeds.
Interpretive implication: The experiment is a kinetic calorimetry measurement; equilibrium binding parameters should not be inferred by applying a standard integrated binding-isotherm fit.
Defensible framing: The heat trace reports reaction rate and catalytic turnover under the stated conditions; kinetic and calorimetric response models are required before deriving activity or inhibition parameters.

## Successive membrane injections change heat sign, slope or morphology threshold: a sequence of membrane transitions
id: lr-membrane-thermogram-transition
kind: literature
basis: literature
status: draft
matches: vesicle titration, permeabilization, reorientation, micellation, lipid-peptide, threshold
cite: henriksen_mastoparan_2011, binder_penetratin_2003, rajarathnam_rosgen_membrane_2014
legacy_id: rule_membrane_thermogram_transition

Situation: Successive membrane injections show changes in heat sign, slope, or morphology-dependent thresholds.
Interpretive implication: The trace may contain partitioning, permeabilization, reorientation, aggregation, or micellation rather than one equilibrium binding process.
Defensible framing: The thermogram is consistent with a sequence of membrane transitions; separate process models and orthogonal morphology measurements are needed before reporting one affinity or stoichiometry.

## Apparent membrane partition coefficient changes with ligand concentration or vesicle size: not automatically intrinsic
id: lr-membrane-apparent-partition-nonideal
kind: literature
basis: literature
status: draft
matches: partition coefficient, lipid binding, vesicle size, LUV, SUV, surface charge, nonideal partitioning
cite: moreno_sds_popc_2010, wieprecht_magainin_2000, fernandezvidal_melittin_2011
legacy_id: rule_membrane_apparent_partition_nonideal

Situation: Apparent membrane partition coefficient changes with ligand concentration or vesicle size.
Interpretive implication: The membrane may be perturbed or the partition process may depend on curvature, packing, charge, or transbilayer equilibration; the apparent parameter is not automatically intrinsic.
Defensible framing: The reported partition parameter is conditional on membrane composition, curvature, coverage, and equilibration timescale; intrinsic values require an explicit model and concentration-series validation.

## Fitted enthalpy or curve shape changes with stirring rate or paddle geometry: mixing-dependent artifact
id: lr-stirring-artifact
kind: literature
basis: literature
status: draft
matches: stirring speed, rpm, mixing artifact, protein unfolding at high stirring, paddle
cite: maruno_stirring_itc_2020
legacy_id: rule_stirring_artifact

Situation: Fitted enthalpy or curve shape changes with stirring rate, paddle geometry, or mixing intensity.
Interpretive implication: Mixing-dependent aggregation, unfolding, incompetent fractions, or incomplete reaction may be contaminating the apparent binding heat.
Defensible framing: The thermodynamic parameters are mixing-condition dependent until paddle geometry, stirring rate, sample integrity, and equilibration are controlled.

## Heat from dilution of an oligomer into lower concentration: needs an oligomerization model and a justified dilution baseline
id: lr-self-association-dilution-model
kind: literature
basis: literature
status: draft
matches: dimer dissociation, oligomer, dilution ITC, self-association, heptamer, monomer-oligomer
cite: cpn10_self_association_2005
legacy_id: rule_self_association_dilution_model

Situation: Heat is generated by dilution of an oligomer into lower concentration.
Interpretive implication: The experiment can report self-association/dissociation thermodynamics, but only with an oligomerization model and an independently justified dilution baseline.
Defensible framing: The dilution heats are consistent with an oligomer–monomer equilibrium under the stated self-association model; a conventional heterobinding fit is not appropriate.

## Ligand length, linkage or subsite occupancy changes carbohydrate-binding enthalpy: contacts are not independently additive
id: lr-covariant-carbohydrate-contacts
kind: literature
basis: literature
status: draft
matches: lectin, carbohydrate, oligosaccharide, subsite mapping, glycoside hydrolase, additivity
cite: rani_artocarpin_1999, lyx_xylanase_subsites_2004, sultan_mcl_2005
legacy_id: rule_covariant_carbohydrate_contacts

Situation: Ligand length, linkage, or subsite occupancy changes carbohydrate-binding enthalpy.
Interpretive implication: The ligand may change contact architecture or engage multiple subsites; affinity and enthalpy differences should not be interpreted as independent additive contacts without structural support.
Defensible framing: The saccharide series indicates ligand-dependent contact architecture; subsite assignments require structural, mutational, or orthogonal validation.

## A labeled ligand gives a different enthalpy or curve from the native ligand: the label perturbs binding
id: lr-ligand-label-changes-heat
kind: literature
basis: literature
status: draft
matches: fluorescent label, tag, modified ligand, labeled versus native, antibody glycan
cite: dam_antibody_constant_region_2008
legacy_id: rule_ligand_label_changes_heat

Situation: A labeled ligand gives a different enthalpy or curve from the native ligand.
Interpretive implication: The label may alter complex geometry, binding mode, local solvation, or effective species concentration; the discrepancy is not necessarily instrument noise.
Defensible framing: Label-dependent thermodynamics indicate that the modified ligand may form a different ensemble or complex; native and labeled parameters should be compared as distinct experiments.

## Metal-ion ITC with buffers, chelators, redox-active ions, hydrolysis or precipitation: apparent parameters depend on speciation
id: lr-metal-speciation-posthoc
kind: literature
basis: literature
status: draft
matches: metal binding, zinc, copper, calcium, chelator, buffer competition, metal speciation, hydrolysis
cite: quinn_metal_protein_itc_2016, quinn_bioinorganic_itc_2010, sundaralingam_calcium_binding_1997, mehlenbacher_mt3_cu_zn_2022
legacy_id: rule_metal_speciation_posthoc

Situation: Metal-ion ITC involves buffers, chelators, redox-active ions, hydrolysis, or precipitation.
Interpretive implication: The measured K_ITC and ΔH_ITC may include metal-buffer/chelator equilibria and proton competition; condition-independent parameters require explicit speciation analysis.
Defensible framing: The apparent metal-binding thermodynamics are conditional on metal speciation and buffer competition; post hoc correction or a validated metal-delivery complex is required for intrinsic parameters.

## A classical binding equation fits well but large solvation, stacking, folding or pre-equilibria are present: parameters lack unique mechanistic meaning
id: lr-classical-fit-incomplete-state
kind: literature
basis: literature
status: draft
matches: good fit but linked equilibria, pre-equilibrium, stacking, solvation, nucleic acid folding, apparent parameters
cite: harmon_solvation_itc_2024, lang_schwarz_hybridization_2007
legacy_id: rule_classical_fit_incomplete_state

Situation: A classical binding equation fits ITC data well but the system has large solvation, stacking, folding, or concentration-dependent pre-equilibria.
Interpretive implication: Good residuals do not establish that the classical model captures all thermodynamic state variables or that the fitted parameters have a unique mechanistic meaning.
Defensible framing: The classical model describes the measured curve under the chosen conditions, but additional solvation or conformational equilibria may be folded into the apparent parameters.

## Implausible fitted n with uncertain ligand purity: verify purity and concentration before constraining n
id: lr-ligand-purity-before-n-adjustment
kind: literature
basis: literature
status: draft
matches: impurity, ligand purity, n too low, n too high, fixing n, concentration check
cite: gruner_itc_impurities_2014
legacy_id: rule_ligand_purity_before_n_adjustment

Situation: Fitted stoichiometry is implausible and ligand purity is uncertain.
Interpretive implication: Impurities can distort apparent enthalpy and stoichiometry; forcing n to an expected value can produce inaccurate thermodynamic parameters.
Defensible framing: Verify ligand identity and purity and test the concentration model before constraining or manually changing stoichiometry.

## Nucleic-acid duplex ΔCp varies with sequence, salt or temperature: coupled single-strand stacking can dominate
id: lr-nucleic-acid-cp-coupled-strands
kind: literature
basis: literature
status: draft
matches: DNA duplex ΔCp, single-strand stacking, strand melting, heat capacity of hybridization
cite: mikulecky_dna_cp_2006, holbrook_dna_duplex_coupled_1999, wu_dna_rna_duplex_2002
legacy_id: rule_nucleic_acid_cp_coupled_strands

Situation: ΔCp from nucleic-acid duplex formation varies with sequence, salt, or temperature.
Interpretive implication: Residual single-strand stacking and other coupled pre-equilibria may dominate the observed ΔCp; it should not automatically be assigned to duplex interface hydration.
Defensible framing: The observed ΔCp includes coupled strand-structure contributions under these conditions; single-strand melting or folding controls are needed for an intrinsic duplex interpretation.

## Direct isotherm nearly vertical because Kd is in the single-digit nanomolar range: use displacement ITC, lower concentration or an orthogonal method
id: lr-tight-binding-alternative-method
kind: literature
basis: literature
status: draft
matches: nanomolar Kd, tight binding, c above 1000, upper limit of ITC, thermal shift, displacement
cite: zubriene_nanomolar_tsa_itc_2009, fam118_2025
legacy_id: rule_tight_binding_alternative_method

Situation: Direct ITC isotherm is nearly vertical because Kd is in the single-digit nanomolar range.
Interpretive implication: The direct titration may constrain ΔH but not Kd accurately; displacement ITC, concentration reduction, or an orthogonal equilibrium method is preferable.
Defensible framing: The interaction is tighter than the direct ITC design can resolve reliably; the affinity estimate should come from an alternative or displacement design.
