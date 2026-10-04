<!--
Complementary and adjacent techniques: what each measures, how it compares with ITC,
and why values can disagree. Written so an analyst can reason across methods.
-->

## Surface plasmon resonance (SPR): kinetics and affinity, and how it differs from ITC
id: me-spr
kind: method
topics: SPR, kinetics, immobilization
basis: mixed
status: draft
matches: surface plasmon resonance, SPR versus ITC, kon koff, mass transport, immobilization effects, avidity SPR, steady state affinity SPR, solvent correction DMSO SPR, sensorgram
cite: day_caii_multimethod_2002, papalia_abrf_mirg_2004, deinum_thrombin_inhibitors_2002, upadhyay_itc_spr_2024

Measures: real-time mass change at a sensor surface, giving association and dissociation rate constants and, from them or from steady-state responses, Kd; works from pM to mM depending on design and uses small amounts of material.
Compared with ITC: SPR needs one partner immobilized (heterogeneous orientation, partial activity, avidity with multivalent analytes, rebinding, mass transport limits), is sensitive to bulk refractive index (DMSO calibration) and yields kinetics that ITC cannot; ITC is label-free in solution and provides ΔH and stoichiometry that SPR does not.
Joint use: Kd = koff/kon from SPR should match ITC Kd for simple 1:1 binding under matched conditions; agreement raises confidence, and differences usually trace to immobilization, avidity or active fraction. Multi-method comparisons of the same enzyme–inhibitor pair in the literature illustrate typical agreement and discrepancies.

## Biolayer interferometry (BLI): dip-and-read kinetics with lower sensitivity for small molecules
id: me-bli
kind: method
topics: BLI, kinetics
basis: general
status: draft
matches: biolayer interferometry, BLI versus ITC, octet, dip and read, small molecule sensitivity BLI, avidity BLI, biosensor nonspecific binding

Measures: interference shift at a biosensor tip, giving binding kinetics and steady-state affinity.
Compared with ITC: lower sensitivity for small molecules (low mass change), sensitivity to viscosity, nonspecific biosensor binding, and avidity with dimeric capture; throughput and low sample volume are advantages; no ΔH.
Joint use: BLI is often used as an orthogonal check of ITC Kd and for off-rate information. Agreement within 2- to 3-fold is typical for a clean 1:1; larger gaps point to avidity or baseline drift.

## Microscale thermophoresis (MST) and related labeled-diffusion methods
id: me-mst
kind: method
topics: MST, fluorescence, low sample
basis: general
status: draft
matches: microscale thermophoresis, MST versus ITC, fluorescently labeled protein, thermophoresis artifact, capillary adsorption, low sample binding assay, aggregation artifact MST

Measures: change in thermophoretic movement of a fluorescent partner upon binding; works with small sample amounts over nM to mM.
Compared with ITC: requires a fluorophore (label or intrinsic Trp); signal is sensitive to buffer, DMSO, detergent, aggregation and capillary adsorption, and to labeling effects on binding; gives no ΔH or direct stoichiometry.
Joint use: useful for low-material or very weak interactions; discrepancy with ITC should prompt checks of labeling, aggregation (capillary scans), and matched buffer.

## Fluorescence polarization/anisotropy and competition assays; Kd versus IC50
id: me-fp-fa
kind: method
topics: fluorescence anisotropy, competition
basis: mixed
status: draft
matches: fluorescence anisotropy, FP assay, tracer, competition assay IC50, labeled peptide, anisotropy versus ITC, inner filter, Cheng Prusoff competition binding
cite: lee_peterson_fret_fp_itc_2016, kaundal_cysk_cdia_2016

Measures: rotational diffusion of a fluorescent tracer; direct titration gives the tracer's Kd; displacement of the tracer by an unlabeled ligand gives IC50, converted to Ki with the tracer concentration and Kd.
Compared with ITC: needs a labeled tracer whose label may perturb binding; tracer concentration should be below Kd for accurate direct binding; unlabeled ligand affinity is inferred; fluorescence artifacts (inner filter, autofluorescence, light scattering) can matter. Literature comparisons of ITC with anisotropy for the same complexes show agreement within a factor of a few, and tag/label differences explain larger gaps.

## Proximity and homogeneous assays (TR-FRET, AlphaScreen/AlphaLISA, HTRF): IC50 depends on assay design
id: me-proximity-assays
kind: method
topics: TR-FRET, AlphaScreen, IC50
basis: general
status: draft
matches: TR-FRET, AlphaScreen, AlphaLISA, HTRF, IC50 depends on concentrations, hook effect, proximity assay affinity, high throughput binding assay

Key points: These assays report signal from proximity of two tagged partners; IC50 from inhibitor curves depends on the concentrations of the tagged partners and the tracer, so IC50 is not Kd or Ki without correction. At high analyte concentration signal can fall (hook effect). Tag and bead effects can change apparent affinity.
Relation to ITC: ITC is label-free and gives Kd directly; disagreement with proximity-assay values commonly reflects assay design (IC50 versus Ki), tags, and different conditions.

## Thermal shift assays (DSF/TSA, nanoDSF): ligand stabilization is not a quantitative affinity
id: me-thermal-shift
kind: method
topics: thermal shift, stability, screening
basis: mixed
status: draft
matches: thermal shift assay, differential scanning fluorimetry, DSF, TSA, nanoDSF, ΔTm ligand, melting temperature shift, stabilization by ligand, screening hits, Tm shift affinity
cite: zubriene_nanomolar_tsa_itc_2009

Measures: shift in protein melting temperature on ligand binding, usually with a dye or intrinsic fluorescence; high throughput and low material.
Compared with ITC: ΔTm correlates qualitatively with affinity for related ligands but is not a Kd; it depends on the enthalpy of binding at Tm, unfolding thermodynamics and ligand concentration; ligands that bind the unfolded state, or compounds interfering with the dye, give misleading shifts. Quantitative Kd from shifts requires a full thermodynamic model, and in the literature set a thermal-shift approach was used together with ITC to bound nanomolar affinities beyond direct ITC.
Use: screening and stability, plus a check of whether a construct is folded.

## Differential scanning calorimetry (DSC): unfolding thermodynamics in the context of ITC
id: me-dsc
kind: method
topics: DSC, stability
basis: general
status: draft
matches: differential scanning calorimetry, DSC, protein stability, unfolding enthalpy, Tm and ΔH unfolding, ligand effect on unfolding, folded state check, construct stability

Measures: heat capacity versus temperature through thermal unfolding, giving Tm, ΔH and ΔCp of unfolding.
Relation to ITC: DSC characterizes stability and whether a construct or mutant is similarly folded; ligand-induced Tm shifts report on binding to the native state; ΔCp of unfolding provides context for ΔCp measured by ITC (coupled folding of a disordered region adds a ΔCp term).
Joint use: reasoning about protein variants (mutations changing stability) and temperature choices for ITC series.

## NMR titrations and relaxation: site-resolved binding, exchange regimes and dynamics
id: me-nmr
kind: method
topics: NMR, chemical shift perturbation, relaxation dispersion
basis: mixed
status: draft
matches: NMR titration, chemical shift perturbation, HSQC, fast exchange, slow exchange, relaxation dispersion, CEST, site resolved affinity, ligand observed NMR, STD NMR, NMR and ITC combined
cite: tochtrop_ibabp_nmr_itc_2002, demers_fyn_sh3_nmr_itc_2009, meneses_fyn_sh3_nmr_itc_2014, neves_cocaine_aptamer_nmr_itc_2010, chakrabarti_recoverin_rk_2016

Measures: chemical shift changes and line shapes give binding site location, population and exchange rates; titrations in fast exchange yield Kd for weaker interactions (roughly µM to mM), slow exchange appears for tight binding, and relaxation dispersion or CEST quantify minor-state populations and interconversion kinetics.
Compared with ITC: NMR resolves individual sites and detects weak contacts that ITC (because of ΔH near zero) may miss; Kd from shift fitting is generally less accurate than ITC but site-specific; ITC provides ΔH and n.
Joint use: NMR plus ITC can separate intrinsic affinity from cooperativity, detect conformational selection, reveal multiple modes, and show that an ITC-silent fragment still interacts.

## Native mass spectrometry: stoichiometry and heterogeneity, with limits for quantitative Kd
id: me-native-ms
kind: method
topics: native MS, stoichiometry
basis: mixed
status: draft
matches: native mass spectrometry, ESI binding assay, complex stoichiometry by MS, gas phase artifacts, ammonium acetate, nonspecific adducts, MS affinity, mass spectrometry binding constants
cite: jecklin_cai_sulfonamide_2009

Measures: masses of intact noncovalent complexes, giving stoichiometry, heterogeneity (oligomers, ligand occupancy distributions) and, with careful calibration, binding constants.
Compared with ITC: very low sample need and direct stoichiometry; but conditions differ (volatile buffers such as ammonium acetate, low ionic strength), gas-phase dissociation and nonspecific clustering can bias Kd, and no ΔH. Studies comparing MS-derived constants with SPR show agreement for well-behaved systems.
Joint use: confirms stoichiometry and heterogeneity suspected from ITC n.

## Oligomeric state and assembly: SEC-MALS, analytical ultracentrifugation, mass photometry, DLS
id: me-oligomer-state
kind: method
topics: oligomeric state, aggregation, size
basis: general
status: draft
matches: oligomeric state, SEC MALS, analytical ultracentrifugation, AUC, mass photometry, DLS, aggregation check, monomer dimer, dimer concentration units, size distribution

Measures: molecular mass and distribution of species (SEC-MALS, mass photometry), sedimentation behavior and self-association constants (AUC), hydrodynamic size and aggregation (DLS).
Relation to ITC: ITC concentrations and stoichiometry depend on oligomeric state at the working concentration; AUC gives self-association Kd directly; DLS or SEC before and after ITC detects aggregation; mass photometry works at nM and quantifies oligomers.
Use: resolve whether n and concentration units refer to monomers or oligomers, and whether a concentration-dependent Kd reflects self-association.

## Optical titrations (intrinsic fluorescence, UV and CD): affinity from a spectroscopic signal
id: me-spectroscopy
kind: method
topics: fluorescence, CD, spectroscopy
basis: mixed
status: draft
matches: tryptophan fluorescence quenching, intrinsic fluorescence binding, CD titration, UV difference, inner filter effect, Stern Volmer, spectroscopic Kd, optical binding assay
cite: mishra_ferulic_bsa_2012

Measures: ligand-induced change in fluorescence, absorbance or circular dichroism of the protein, ligand or probe; secondary structure change by CD.
Compared with ITC: very low material and high sensitivity, but the signal change must be linked to binding (not to inner-filter or dynamic quenching), stoichiometry is inferred from model fits, and spectroscopic Kd values at low protein concentration can differ from ITC concentrations by conditions; no ΔH. CD distinguishes folding upon binding.
Joint use: check folding and conformational change linked to ITC ΔH and ΔCp.

## Structures (X-ray, cryo-EM) and ITC: stoichiometry, site assignment and limits of structure–thermodynamics mapping
id: me-structure
kind: method
topics: structural biology, crystallography, cryo-EM
basis: mixed
status: draft
matches: crystal structure and ITC, binding mode, structure thermodynamics relationship, crystal contacts, stoichiometry from structure, mutants and binding site, cryo EM stoichiometry, thermodynamic signature and structure
cite: koch_aldose_reductase_mutants_2011, oep21_2023

Key points: A structure fixes the complex composition, site architecture and contacts, which anchors expected stoichiometry (n) and provides burial areas for ΔCp predictions. It does not predict ΔH, −TΔS or Kd; crystal packing, cryogenic conditions and ligand soaking can show a binding mode that differs from solution.
Reasoning: ITC differences across closely related ligands or mutants can reflect changes in binding mode as well as strength, so thermodynamic signatures should be examined alongside structures of each complex. Mutational or biophysical assignment is needed for site-specific attributions.

## Computation (docking, MD, free-energy methods) versus ITC
id: me-computation
kind: method
topics: simulations, free energy calculation
basis: general
status: draft
matches: docking versus ITC, MD simulation binding free energy, FEP accuracy, free energy perturbation, computed ΔH, MM/PBSA, computational prediction affinity, benchmark with ITC

Key points: Docking scores are not affinities. Alchemical free-energy methods can reach about 1 kcal/mol (4 kJ/mol) RMS error in favorable congeneric series, so predictions closer than this are not meaningful differences; absolute enthalpies and entropies are harder than ΔG.
ITC role: ITC supplies experimental ΔG (and ΔH) benchmarks with typical precision of 0.3 to 1 kJ/mol; disagreements can reflect force-field errors, unmodeled protonation or linked equilibria, and experimental concentration errors. Computed ΔH should be compared with the buffer-corrected intrinsic ΔH.

## HDX-MS, SAXS and other structural-dynamics probes: coupled folding and conformational change
id: me-hdx-saxs
kind: method
topics: HDX-MS, SAXS, dynamics
basis: general
status: draft
matches: hydrogen deuterium exchange, HDX MS, SAXS, conformational change on binding, protection upon binding, compaction, flexible complex, dynamics and thermodynamics

Measures: HDX-MS reports local backbone protection and dynamics; SAXS reports overall shape, size and compaction in solution.
Relation to ITC: large negative ΔCp, big opposing entropy or unusual n can be rationalized by observed folding, protection or compaction; changes in flexibility link to entropy terms; neither technique gives ΔH or Kd directly.
Use: test hypotheses suggested by ITC (coupled folding, partially disordered complex).

## Equilibrium dialysis, ultrafiltration and other direct binding measurements
id: me-direct-binding
kind: method
topics: equilibrium dialysis, ultrafiltration
basis: general
status: draft
matches: equilibrium dialysis, ultrafiltration binding, direct binding measurement, radioligand binding, free ligand measurement, gold standard weak binding

Measures: the concentration of free and bound ligand at equilibrium by separation (dialysis membrane, filtration) with a quantitation method.
Relation to ITC: model-free Kd in solution, with no label if the ligand can be quantified; works for very weak binding at high concentrations and for tight binding in titration regime; slower and material-intensive; no ΔH.
Use: independent check of Kd and stoichiometry for key complexes.

## Kinetics versus equilibrium: Kd = koff/kon, residence time and what ITC cannot see
id: me-kinetics-vs-equilibrium
kind: method
topics: kinetics, residence time
basis: mixed
status: draft
matches: kinetics versus equilibrium, residence time, kon koff, slow association, fast dissociation, same Kd different kinetics, drug residence time, ITC cannot give rates
cite: prestel_lecb_2016, theisen_jacs_2025, chakrabarti_recoverin_rk_2016, sun_panD_2020

Key points: Kd = koff/kon, so very different kinetic profiles can share one Kd (a mutation can change koff many-fold with a small change in Kd). ITC, as an equilibrium method, gives Kd, ΔH and stoichiometry only; residence time requires SPR/BLI, stopped flow, NMR exchange or kinetic ITC.
Functional consequences often depend on kinetics (resistance mutations with faster off-rates, drug residence time); agreement of ITC Kd with an orthogonal equilibrium measurement does not imply similar kinetics.

## IC50, Ki, Kd and EC50 are different quantities: Cheng–Prusoff and assay dependence
id: me-ic50-ki-kd
kind: method
topics: potency, inhibitors
basis: mixed
status: draft
matches: IC50 versus Kd, Ki versus Kd, Cheng Prusoff, EC50, enzyme inhibition constant, competitive inhibition Ki, tight binding Morrison, potency versus affinity, conversion IC50 to Ki
cite: sun_panD_2020

Key points: IC50 depends on assay conditions; for a competitive inhibitor Ki = IC50 / (1 + [S]/Km) (and for a displaced tracer Ki = IC50 / (1 + [L]/Kd_L)). For tight inhibitors comparable to enzyme concentration, IC50 approaches half the enzyme concentration and Morrison-type analysis is needed. EC50 values in cells combine target engagement, permeability, occupancy and downstream amplification.
Relation to ITC: ITC Kd should match Ki for a simple competitive inhibitor under the same conditions; a close match between ITC affinity and a separately measured competitive inhibition constant is supportive.

## Cell-based potency and ITC affinity: why they diverge
id: me-cell-assays
kind: method
topics: cellular potency, translation
basis: general
status: draft
matches: cellular potency versus ITC, cell assay EC50, in vitro affinity versus cell activity, target engagement, permeability, competition with endogenous ligands, translation of biophysical data

Key points: Purified-system Kd reflects intrinsic affinity under assay conditions; cell potency is shaped by permeability, efflux, intracellular competition (ATP, substrates, endogenous partners), occupancy requirements, protein state and amplification. A weaker ITC Kd with good cell activity, or the reverse, is common and not necessarily a measurement problem.
Use: ITC supports mechanism and structure–activity reasoning but does not by itself predict cellular activity.
