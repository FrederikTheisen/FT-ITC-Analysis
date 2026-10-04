<!--
System-class playbooks: typical behavior, complications and cautions per interaction type.
Typical ranges are broad orders of magnitude, not rules.
-->

## Protein–small-molecule (drug-like) binding: typical ranges, signatures and pitfalls
id: sy-protein-small-molecule
kind: system
topics: ligand binding, drug discovery
basis: mixed
status: draft
matches: small molecule binding, drug like ligand, inhibitor binding ITC, enzyme inhibitor, ligand series, protein ligand thermodynamics, hit validation, optimized ligand
cite: paketuryte_intrinsic_thermo_2019, koch_aldose_reductase_mutants_2011, sun_panD_2020

Typical behavior: optimized ligands bind with Kd from nM to tens of µM, fragments in the tens of µM to mM range. ΔH is commonly -10 to -80 kJ/mol with an entropy term that often partly offsets it; ΔCp is usually modest (near zero to about -0.5 kJ/(mol K)). n is expected near one per site.
Complications: DMSO and pH mismatch; ligand solubility or aggregation; ligand ionization and proton linkage that make ΔH buffer-dependent; co-purified cofactor or ligand occupying the site (n low); competition with substrate, metal or nucleotide; slow binding (broad peaks); protein stability.
Interpretation cautions: ΔH and −TΔS partitions are not additive contacts; series comparisons should use ΔΔ values with uncertainties; Kd and IC50 or Ki differ in definition and assay conditions; a good agreement between ITC Kd and independently measured inhibition constant raises confidence.
Useful partner methods: SPR/BLI for kinetics, thermal shift for screening, NMR and crystallography for binding mode.

## Fragment-sized and low-affinity ligands (Kd tens of µM to mM)
id: sy-fragments
kind: system
topics: fragments, weak binding
basis: mixed
status: draft
matches: fragment binding, weak ligand, millimolar affinity, fragment based discovery, ligand efficiency fragment, low c fragment, high concentration ITC, solubility limit
cite: mitf_2025

Typical behavior: small heats, shallow curves, no plateau within accessible concentrations, strong correlation of n, ΔH and offset. Ligand efficiency is often high even though Kd is weak.
Complications: solubility limits cap the syringe concentration, DMSO and pH mismatch heats compete with the binding heat, ligands often have multiple weak sites and nonspecific binding, aggregation can mimic binding.
Interpretation cautions: Kd is conditional on a fixed n and a reliable blank; a clear curvature supports binding even if numbers are imprecise; absence of heat is not absence of binding (ΔH may be near zero).
Useful partner methods: NMR (ligand- and protein-observed), SPR with solvent correction, thermal shift, crystallography; fragment-based discovery in the literature set combined biophysical methods.

## Protein–protein interactions: typical ranges, complications and cautions
id: sy-protein-protein
kind: system
topics: protein-protein, complexes
basis: mixed
status: draft
matches: protein protein interaction ITC, complex formation, heterodimer, interface thermodynamics, ΔCp interface, tag dimerization GST, self associating partner, transient complex
cite: upadhyay_itc_spr_2024, nemo_2009

Typical behavior: Kd from pM to about 100 µM; ΔH is often large (tens to over a hundred kJ/mol, either sign) with large opposing entropy; ΔCp typically negative, about -0.5 to -2 kJ/(mol K) for interfaces of 1000 to 2000 Å².
Complications: self-association of either partner (GST tags, coiled coils, oligomeric proteins); multiple domains and sites (n may exceed one); precipitation at the high syringe concentrations needed; ionic-strength sensitivity; linked folding or conformational change; partner heterogeneity, glycosylation or incomplete tag cleavage; concentration uncertainty because extinction coefficients of large proteins are imprecise.
Interpretation cautions: the stoichiometry should be compared with known oligomeric states; weak transient interactions need very high concentrations and are sensitive to blank accuracy; ITC and SPR combined give thermodynamics and kinetics.

## Short linear motifs and peptide–domain interactions (Kd about 1 to 500 µM)
id: sy-slim-peptide
kind: system
topics: peptides, linear motifs, phosphopeptides
basis: mixed
status: draft
matches: peptide domain binding, short linear motif, SLiM, phosphopeptide binding, 14-3-3 peptide, SH3 domain peptide, peptide Kd micromolar, peptide in syringe, phosphopeptide titrated into a 14-3-3 protein, peptide into domain
cite: srdanovic_hdmx_hdm2_1433_2022, sengupta_fusicoccin_1433_2020, ward_mdm2_1433_2024

Typical behavior: micromolar to sub-micromolar affinities, ΔH moderate to large exothermic, n near one per binding groove; multi-site phosphopeptides can show higher affinity from cooperating motifs.
Complications: peptide concentration and net content (see peptide handling), pH shifts from TFA salts, cysteine oxidation, phosphatase contamination, peptide aggregation at syringe concentration, adsorption; the macromolecule needs to be in the cell for sequential models.
Interpretation cautions: isolated motif versus extended construct are different systems; weak peptides need high concentration and a fixed n for Kd; comparisons across phosphosite variants should use matched buffers and ΔΔG with uncertainty.
Examples in the literature set include 14-3-3 complexes with phosphopeptides and stabilizing small molecules.

## Intrinsically disordered proteins and coupled folding-and-binding: signatures and how to read them
id: sy-idp-coupled-folding
kind: system
topics: IDP, coupled folding, fuzzy complexes
basis: mixed
status: draft
matches: intrinsically disordered protein binding, coupled folding and binding, IDR ITC, fuzzy complex, entropy penalty, strongly negative ΔCp, folding upon binding, disordered flanking region, motif context
cite: theisen_jacs_2021, theisen_natcomm_2024, hierarchical_idp_2025, higa2_2024

Typical behavior: moderate affinity (nM to tens of µM) despite large interface area because of the entropic cost of ordering; large negative ΔH and strongly negative ΔCp (for example about -2 to -3 kJ/(mol K) for extended interfaces) are typical of coupled folding; ΔH can be near zero at intermediate temperatures, making ITC uninformative there.
Complications: multiple motifs binding separate surfaces (bivalent contribution), flanking regions contributing to affinity, proline cis/trans isomerization giving apparent affinities, partial folding trajectories, fuzzy complexes that remain dynamic despite a good one-site fit.
Interpretation cautions: Spolar–Record residue counts are model-dependent and calibration-dependent; truncation changes the thermodynamic system; an ITC-silent fragment can still contact its partner weakly. Orthogonal NMR, crystallography or kinetics are needed for folding pathways.

## Specific protein–DNA binding: stoichiometry, salt dependence, ΔCp and coupled folding
id: sy-protein-dna-specific
kind: system
topics: protein-DNA, transcription factors
basis: mixed
status: draft
matches: protein DNA ITC, transcription factor binding, DNA binding protein, operator, specific DNA binding thermodynamics, sequence specific DNA, salt dependent DNA binding, palindromic site dimer
cite: spolar_record_science_1994, spolar_record_protocol_2009, hadzi_lah_protein_dna_2022, ladbury_trp_repressor_1994, milev_tn916_integrase_2003

Typical behavior: very high affinity in physiological salt (sub-nM to pM) that weakens sharply with salt; large negative ΔCp (about -1 to -5 kJ/(mol K) or beyond) interpreted through coupled folding and desolvation; ΔH can be large with either sign.
Complications: very high c at accessible concentrations (low concentrations or high salt are needed); DNA concentration and duplex annealing quality (A260 with a sequence-based extinction coefficient; check by gel); dimeric proteins on palindromic sites (n = 0.5 per monomer or 2 per site); nonspecific binding at excess protein producing extra heats; protein sticking to surfaces; nonspecific binding on long DNA requires lattice models.
Interpretation cautions: ΔCp and Spolar–Record decompositions are conditional and calibration-dependent; salt identity changes results; compare specific and nonspecific sites under identical conditions.

## Nonspecific protein–DNA binding on long DNA: lattice models, site size and condition dependence
id: sy-nonspecific-dna-lattice
kind: system
topics: nonspecific DNA binding, lattice binding
basis: mixed
status: draft
matches: nonspecific DNA binding, lattice binding, McGhee von Hippel, binding site size, SSB ssDNA binding modes, poly(dA) poly(dT), cooperative lattice, site size base pairs
cite: lundback_sso7d_1998, lundback_sso7d_salt_1996, holbrook_ihf_2001, kozlov_lohman_ssb_2012

Typical behavior: many overlapping binding positions on a polymer give a site size of several base pairs; affinity is moderate (µM to mM), strongly salt-dependent, and thermodynamics depend on salt, solvent and temperature; binding can be endothermic at one temperature and exothermic at another because of a large negative ΔCp.
Models: a 1:1 model is inadequate; lattice models (such as McGhee–von Hippel, with or without cooperativity) with an explicit site size are typical, and different oligomeric binding modes (for example for ssDNA-binding proteins) depend on salt.
Cautions: apparent n depends on DNA length and end effects; observed thermodynamics are conditional on the lattice, ionic environment and model; large negative ΔCp does not by itself imply large net dehydration.

## RNA–protein and RNA–small-molecule ITC: folding, Mg²⁺ and conformational heterogeneity
id: sy-rna-and-riboswitch
kind: system
topics: RNA, riboswitch, aptamer
basis: mixed
status: draft
matches: RNA binding ITC, riboswitch ligand binding, aptamer, RNA folding before ITC, magnesium dependence, structured RNA, RNase contamination, RNA heterogeneity, kissing complex
cite: salim_feig_rna_2009, feig_rna_2007, gilbert_batey_rna_2009, jones_riboswitch_itc_2019, kaul_aminoglycoside_rRNA_2002

Typical behavior: binding often couples to folding or tertiary structure formation and to Mg²⁺ and monovalent ions; ΔH is frequently large, ΔCp negative, and stoichiometry dependent on how much RNA is correctly folded.
Practical points from the RNA ITC methods literature: refold RNA in the final buffer (heat and cool in the presence of the right ions) and verify homogeneity (native gel); control RNase contamination; keep Mg²⁺ and monovalent salt matched; use appropriate purification to avoid failure sequences and mixed conformers; ligand and RNA concentrations determined by absorbance with sequence-based extinction coefficients.
Interpretation cautions: heterogeneous folding reduces apparent active fraction (low n); ligand-induced folding is part of the measured enthalpy; temperature and Mg²⁺ series reveal coupled processes; NMR complements ITC for stem-length or structural dependence.

## DNA and RNA duplex formation and hybridization: salt, strand stacking and ΔCp
id: sy-nucleic-acid-duplex
kind: system
topics: duplex formation, hybridization
basis: mixed
status: draft
matches: duplex formation ITC, DNA hybridization, DNA RNA hybrid, PNA DNA, nearest neighbor enthalpy, single strand stacking, ΔCp of duplex formation, salt dependence of hybridization
cite: lang_schwarz_hybridization_2007, peyr_et_al_pna_dna_1999, wu_dna_rna_duplex_2002, mikulecky_dna_cp_2006, holbrook_dna_duplex_coupled_1999, harmon_solvation_itc_2024

Typical behavior: duplex formation is strongly exothermic, on the order of -30 kJ/mol per base-pair step; ΔCp is small and negative with strong sequence and salt dependence; affinity depends on ionic strength.
Complications: single-strand base stacking and other pre-equilibria contribute to the observed ΔH and ΔCp, so these are not purely duplex-interface quantities; strand concentration accuracy (extinction coefficients from nearest-neighbor values), strand ratio and self-complementarity; temperature dependence reflects strand structure changes.
Interpretation cautions: values apply to the stated salt activity and backbone; cross-condition comparison requires explicit salt and backbone corrections; a good classical fit does not guarantee that solvation or stacking pre-equilibria are absent.

## Small-molecule ligands binding nucleic acids: multiple sites, neighbor exclusion and protonation
id: sy-small-molecule-nucleic-acid
kind: system
topics: groove binders, intercalators, aminoglycosides
basis: mixed
status: draft
matches: DNA groove binder, intercalator, ligand DNA binding sites per base pair, neighbor exclusion, aminoglycoside RNA binding, minor groove agent protonation, metal ion DNA binding, cationic ligand nucleic acid
cite: nguyen_cgp_2006, kaul_aminoglycoside_rRNA_2002, aluminum_ctdna_itc_2005

Typical behavior: several binding modes (intercalation, groove binding, electrostatic) can coexist with different ΔH; sites per base pair and neighbor exclusion limit saturation; affinity depends on sequence and ionic strength.
Complications: ligand ionization or pKa shifts on binding make observed ΔH buffer-dependent; cationic ligands bring counterion release; polymer DNA gives a lattice problem; multiple sites with similar affinity give shallow or biphasic curves.
Interpretation cautions: n per base pair is an effective ratio; binding-linked protonation requires a buffer series for intrinsic ΔH; sequence-specific comparisons need identical conditions.

## Lipid vesicle and membrane interactions (peptides, amphiphiles, detergents): composite heats and partition models
id: sy-lipid-membrane
kind: system
topics: membranes, partitioning, antimicrobial peptides
basis: mixed
status: draft
matches: peptide lipid interaction, membrane partitioning, antimicrobial peptide ITC, detergent solubilization, liposome binding, pore formation heat, charged lipid, membrane composite thermogram
cite: peptide_membrane_2011, moreno_sds_popc_2010, wieprecht_magainin_2000, binder_penetratin_2003, fernandezvidal_melittin_2011

Typical behavior: interactions are partitioning-like and reported as a partition coefficient per lipid; charged peptides binding to anionic vesicles have an electrostatic component that makes apparent affinity depend on surface coverage; heats can change sign with temperature; permeabilization, reorientation, aggregation and micellization can be superimposed.
Complications: vesicle size and lamellarity (outer-leaflet fraction), vesicle stability and fusion, peptide aggregation, detergent effects on membranes above subsolubilizing concentrations; ligand concentration dependence of the apparent partition coefficient.
Interpretation cautions: sign changes, non-monotonic injections or concentration-dependent background indicate a composite process and call for separate process models or orthogonal morphology and permeabilization assays before assigning a single Kp or stoichiometry.

## Metal-ion binding: speciation, buffer competition, redox, hydrolysis and apo-protein preparation
id: sy-metal-ion
kind: system
topics: metal binding, speciation, bioinorganic
basis: mixed
status: draft
matches: metal ion ITC, zinc binding, copper binding, calcium binding, metal buffer complexation, apo protein preparation, metal hydrolysis, metallothionein, EDTA competition, chelator displacement
cite: quinn_metal_protein_itc_2016, quinn_bioinorganic_itc_2010, sundaralingam_calcium_binding_1997, mehlenbacher_mt3_cu_zn_2022

Typical behavior: metal binding releases protons from coordinating groups (His, Cys, Asp/Glu), so observed ΔH depends on buffer; affinities for tight metal sites often lie beyond direct ITC range and need competition with a chelator.
Complications: buffer and chelator complexation (Tris and some Good's buffers bind Cu²⁺, Zn²⁺, Ni²⁺; phosphate precipitates several metals; citrate and EDTA chelate strongly); metal hydrolysis and precipitation at higher pH; redox of Fe²⁺ and Cu⁺; reductants such as DTT chelating metals and TCEP interacting; trace metal contamination and incomplete apo protein; heat of dilution of the metal salt itself.
Interpretation cautions: measured K_ITC and ΔH_ITC are apparent and conditional on buffer and speciation; condition-independent values require explicit speciation analysis or a validated metal-delivery complex; multisite metal binding often has overlapping transitions and fits are model-dependent.

## Carbohydrate–protein binding (lectins, binding modules, glycoside hydrolases): weak, enthalpy-driven and multivalent
id: sy-carbohydrate-lectin
kind: system
topics: lectins, glycans, carbohydrate binding
basis: mixed
status: draft
matches: lectin carbohydrate binding, sugar binding ITC, galectin, carbohydrate binding module, glycoside hydrolase subsites, oligosaccharide binding, millimolar sugar affinity, enthalpy driven sugar binding, anomer mutarotation
cite: rani_artocarpin_1999, sultan_mcl_2005, gupta_lectin_comparison_1996, lyx_xylanase_subsites_2004, galectin_jacsau_2021

Typical behavior: monosaccharide Kd from about 10 µM to mM with moderate to large exothermic ΔH offset by an unfavorable entropy (strong compensation); oligosaccharides often bind more tightly, engaging more subsites; ΔCp small.
Complications: very weak binding (low c) requiring high ligand concentration; mutarotation and anomeric mixtures in sugar stocks; multivalency in lectins (oligomers with several sites and cross-linking); water-mediated contacts that make contact contributions non-additive; ligand length and linkage changing the binding mode.
Interpretation cautions: subsite assignments from ligand series require structural or mutational support; entropy contributions include solvent and protein/ligand flexibility and are not hydrophobic proof; affinity and enthalpy changes with ligand length may reflect a change in contact architecture.

## Antibody–antigen and Fab binding: high affinity, bivalency and format effects
id: sy-antibody-antigen
kind: system
topics: antibodies, high affinity
basis: mixed
status: draft
matches: antibody antigen ITC, Fab binding, IgG bivalent stoichiometry, high affinity binders, constant region effects, nanobody, affinity maturation thermodynamics
cite: jelesarov_antigen_antibody_1996, dam_antibody_constant_region_2008

Typical behavior: Kd from pM to tens of nM; large exothermic ΔH common; intact IgG binds two antigens per molecule (n near 2 per IgG) while Fab or single-domain formats are 1:1.
Complications: very high c (step isotherms) requiring low concentrations or displacement; bivalent crosslinking at excess antigen for multivalent antigens; heterogeneity (glycosylation, partial fragments); concentration errors from large molecular weights; format-dependent thermodynamics (variable-region-identical antibodies of different isotypes can differ in binding thermodynamics).
Interpretation cautions: report whether the antibody is intact or fragmented; n near 2 for IgG is not an unusual stoichiometry; apparent affinity from IgG includes avidity when the antigen is multivalent.

## Enzyme–ligand ITC: turnover heat, cofactors and catalytic mutants
id: sy-enzyme-ligand
kind: system
topics: enzymes, inhibitors, substrates
basis: mixed
status: draft
matches: enzyme substrate ITC, catalytic turnover heat, inhibitor binding to enzyme, inactive mutant, non hydrolyzable analog, slow tight binding inhibitor, cofactor dependent binding, product inhibition
cite: hsEH_single_injection_2019, ditrani_inhibitor_kinetics_2018, koch_aldose_reductase_mutants_2011

Typical behavior: inhibitor or analogue binding behaves like ordinary ligand binding; a true substrate adds continuous heat from turnover that does not return to baseline.
Complications: substrate turnover during the titration (use inactive mutants, non-hydrolyzable analogues, chelators or low temperature); co-purified cofactor or product occupying the site; product inhibition; slow-binding inhibitors giving broad or time-dependent peaks; pH-dependent activity; buffer protonation heats from catalysis or binding.
Interpretation cautions: a thermogram with changing steady-state power reports kinetics and needs kinetic models; ITC Kd for inhibitors should be compared with Ki under matched conditions; mutations can change binding mode as well as strength.

## Nucleotide and cofactor binding (ATP, ADP, GTP, NAD): Mg²⁺ coupling, hydrolysis and apo preparation
id: sy-nucleotide-cofactor
kind: system
topics: nucleotides, cofactors
basis: mixed
status: draft
matches: ATP binding ITC, nucleotide binding, MgATP, GTPase nucleotide, cofactor binding, hydrolysis during titration, co purified nucleotide, apo protein, NAD processing
cite: aumuller_groel_mgatp_2011, fam118_2025

Typical behavior: affinity and ΔH depend on Mg²⁺ (free versus complexed nucleotide), pH and ionic strength; binding can be coupled to conformational change and proton linkage.
Complications: nucleotide hydrolysis by the protein during the run (use non-hydrolyzable analogues, low temperature or inactive mutants); co-purified nucleotide or metal giving low n; contaminating nucleotides in stocks; Mg²⁺ binding to nucleotide and to protein as separate equilibria; concentration determination of nucleotide stocks by absorbance.
Interpretation cautions: the measured process may be binding of the Mg-nucleotide complex, free nucleotide, and metal exchange together; keep Mg²⁺ and nucleotide ratios explicit.

## Self-association and oligomers: how oligomerization changes ITC data and models
id: sy-self-association
kind: system
topics: oligomerization, dilution
basis: mixed
status: draft
matches: self association, oligomer, dimer monomer equilibrium, concentration dependent oligomerization, dissociation model, heptamer, isodesmic assembly, ligand induced oligomerization
cite: cpn10_self_association_2005, nbi_selfassembly_2019

Typical behavior: dilution of an oligomer into buffer gives heat from dissociation that decreases as the cell concentration builds; association is often entropy-driven (water release) and endothermic; ligand binding to an oligomer can be coupled to assembly state.
Complications: apparent Kd and n depend on the macromolecule's oligomeric state at the working concentration; reverse or concentration series give different parameters; concentration units (monomer versus oligomer) can double or halve apparent n.
Interpretation cautions: a conventional heterobinding fit is inappropriate for dilution of an oligomer; the model must include the oligomer-monomer equilibrium and an independently justified dilution baseline; the measured enthalpy is for disassembly and has the opposite sign of the assembly enthalpy.

## Ternary complexes and cooperativity (PROTACs, molecular glues): binary versus ternary affinities
id: sy-ternary-cooperativity
kind: system
topics: ternary complexes, cooperativity, PROTAC, stabilizers
basis: mixed
status: draft
matches: ternary complex ITC, PROTAC cooperativity, molecular glue, alpha cooperativity factor, binary versus ternary Kd, protein protein interaction stabilizer, fusicoccin 14-3-3, ligand induced dimerization
cite: sengupta_fusicoccin_1433_2020, ward_mdm2_1433_2024

Key points: Cooperativity is commonly expressed as α = Kd(binary)/Kd(ternary), with α above 1 meaning that a partner enhances the binding of the second ligand. In ITC, binary affinities of the bifunctional compound for each protein are measured first, then the ternary affinity is measured by titrating one protein into the compound pre-saturated with the other (or into the pre-formed binary complex).
Complications: compound solubility and DMSO; very tight binary or ternary steps (high c); possibly small ΔH; protein–protein affinity in the absence of the compound must be known; high concentrations can induce hook-like behavior in activity assays but not in a properly designed ITC.
Interpretation cautions: cooperativity values depend on the order and conditions of the titrations; the thermodynamic coupling should be checked by closing the cycle with all measured legs.

## Covalent and slow tight-binding ligands: heat from bond formation and time dependence
id: sy-covalent-slow-binding
kind: system
topics: covalent inhibitors, slow binding
basis: general
status: draft
matches: covalent inhibitor ITC, irreversible binding, slow tight binding, time dependent inhibition, reaction heat not equilibrium, kinact KI, long residence time inhibitor

Key points: A covalent reaction is not an equilibrium binding event; ITC measures the total heat of bond formation (often large and exothermic) and an equilibrium Kd is not defined. A reversible-covalent or slow-tight inhibitor gives peaks that return to baseline slowly and whose amplitude depends on spacing.
Complications: incomplete reaction within the injection spacing; subsequent hydrolysis or side reactions; heats from ligand reactivity with buffer components or thiols (reductants).
Interpretation cautions: fitting a standard binding isotherm to such data returns a parameter set without equilibrium meaning; kinetic ITC or orthogonal kinetic assays are the appropriate route to rate constants.

## Host–guest and supramolecular systems (cyclodextrins, cucurbiturils, calixarenes, amphiphilic assemblies)
id: sy-host-guest
kind: system
topics: supramolecular chemistry, host-guest
basis: mixed
status: draft
matches: host guest ITC, cyclodextrin, cucurbituril, calixarene, supramolecular complex, entropy driven self assembly, amphiphilic dye assembly, solvent effect on binding, guest dimerization
cite: bowl_tube_2018, nbi_selfassembly_2019, physical_networks_2021

Typical behavior: Kd spans mM to below nM; binding can be driven by enthalpy (cavity dehydration, CH–π contacts) or entropy (solvent release), and endothermic entropy-driven association is well documented; ΔH and −TΔS vary strongly with solvent, salt and temperature.
Complications: guest or host self-association; very high affinity (high c); solvent mixtures and cosolvent mismatches; competing ions; slow exchange.
Interpretation cautions: displacement against a weaker guest brings very tight pairs into range; a favorable entropy term can drive endothermic binding strongly; do not equate exothermicity with favorable binding.

## Multivalent binding and avidity: effective valence, steric limits and precipitation
id: sy-multivalent
kind: system
topics: multivalency, avidity, polymers
basis: mixed
status: draft
matches: multivalent ITC, avidity, effective valence, polymer binding, polysaccharide binding stoichiometry, functional valence, crosslinking precipitate, bivalent peptide, calmodulin complex stoichiometry
cite: dam_2016, survivin_2021, prestel_alginate_blg_2023, prestel_nhe1_cam_2021

Typical behavior: fitted n reflects effective coverage rather than a count of independent sites; avidity makes apparent Kd much lower than the monovalent affinity; n can vary with ligand length, pH or concentration.
Complications: structural versus functional valence (steric hindrance, overlapping sites); crosslinking and precipitation; heterogeneous complexes of varying stoichiometry; slow reorganization.
Interpretation cautions: changes in n with polymer length or pH should not be read as creation or loss of discrete sites without structural support; reverse titrations give complementary but not guaranteed complete information; precipitation invalidates parameters.

## Detergent and amphiphile micellization by ITC: CMC and demicellization heats
id: sy-detergent-micelle
kind: system
topics: detergents, CMC, micelles
basis: mixed
status: draft
matches: critical micelle concentration ITC, demicellization, detergent dilution, CMC determination, micelle formation enthalpy, amphiphile aggregation, surfactant titration
cite: keller_sds_popc_2006, nbi_selfassembly_2019

Typical behavior: injecting a micellar solution into buffer gives heats that fall sigmoidally as the cell concentration crosses the CMC; the midpoint gives the CMC and the step height the enthalpy of demicellization. The same pattern appears for self-assembling dyes and amphiphilic peptides.
Complications: CMC depends on salt, temperature and buffer; ligands and proteins can partition into micelles; the heats of dilution of monomers below the CMC.
Interpretation cautions: such a curve is a dilution or disassembly process, not binding; it should not be fitted with a binding model to extract Kd or stoichiometry; the measured enthalpy of disassembly has opposite sign to assembly.
