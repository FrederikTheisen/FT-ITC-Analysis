<!-- Migrated from legacy/itc_knowledge_base_expanded.json by tools/migrate_legacy.py.
     These files are now the source of truth; edit them directly. -->

## M. tuberculosis PanD with pyrazinoic acid (POA) (protein–small molecule)
id: pr-pand-poa
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: ligand series and mutants
matches: protein–small molecule, literature case study, M. tuberculosis PanD, pyrazinoic acid (POA), single site, binding enthalpy
cite: sun_panD_2020
legacy_id: case_panD_POA

System: cell: M. tuberculosis PanD; syringe: pyrazinoic acid (POA).
Conditions: model reported: single site; T = 293.15 K (20.0 °C).
Reported: Kd = 710 ± 30 µM; ΔH = -17.57 ± 0.84 kJ/mol; ΔS = 0.4 ± 2.9 J/(mol K); pKd = 3.15.
Observations:
- Affinity agreed closely with the independently measured competitive inhibition constant.
- Binding was exothermic with a small entropy term.
- An orthogonal BLI-derived Kd also agreed with ITC.
Authors' interpretation:
- Binding was described as primarily enthalpy-driven and consistent with electrostatic and hydrogen-bonding interactions.
- Unbuffered acidic measurements were considered potentially physiologically misleading because POA itself changes pH.
Takeaway for interpretation:
- Agreement between ITC affinity and an orthogonal equilibrium/kinetic method materially increases confidence.
- For ionizable ligands, buffer and pH control can dominate whether a measured heat and affinity are interpretable.

## M. tuberculosis PanD with 6-Cl-POA (protein–small molecule)
id: pr-pand-6clpoa
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: ligand series and mutants
matches: protein–small molecule, literature case study, M. tuberculosis PanD, 6-Cl-POA, single site, binding enthalpy
cite: sun_panD_2020
legacy_id: case_panD_6ClPOA

System: cell: M. tuberculosis PanD; syringe: 6-Cl-POA.
Conditions: model reported: single site.
Reported: Kd = 1.090 ± 0.040 mM; ΔH = -13.81 ± 0.42 kJ/mol; ΔS = 9.2 ± 2.1 J/(mol K); pKd = 2.96.
Comparison with M. tuberculosis PanD with pyrazinoic acid (POA) (protein–small molecule): affinity change = weaker; interpretation = Small chemical modification weakens affinity modestly while preserving an enthalpy-favorable signature..

## PanD H21R resistance mutant with POA (mutation effect)
id: pr-pand-h21r
kind: precedent
basis: literature
status: draft
topics: mutation effect; related study sets: ligand series and mutants
matches: mutation effect, literature case study, PanD H21R resistance mutant, POA
cite: sun_panD_2020
legacy_id: case_panD_H21R

System: cell: PanD H21R resistance mutant; syringe: POA.
Reported: Kd = 2.840 ± 0.060 mM; pKd = 2.55.
Comparison with M. tuberculosis PanD with pyrazinoic acid (POA) (protein–small molecule): fold Kd increase = 4; author interpretation = Resistance is associated with weakened affinity and markedly faster ligand dissociation..
Takeaway for interpretation: A mutation-induced affinity change is stronger mechanistic evidence when consistent with orthogonal kinetic or structural changes.

## PanD M117I resistance mutant with POA (mutation effect)
id: pr-pand-m117i
kind: precedent
basis: literature
status: draft
topics: mutation effect; related study sets: ligand series and mutants
matches: mutation effect, literature case study, PanD M117I resistance mutant, POA
cite: sun_panD_2020
legacy_id: case_panD_M117I

System: cell: PanD M117I resistance mutant; syringe: POA.
Reported: Kd = 900 ± 90 µM; pKd = 3.05.
Comparison with M. tuberculosis PanD with pyrazinoic acid (POA) (protein–small molecule): fold Kd increase = 1.27; author interpretation = A relatively small equilibrium-affinity loss accompanied a much faster off-rate..
Takeaway for interpretation: Small Kd changes can coexist with large kinetic changes; ITC equilibrium data alone do not define residence time.

## MITF with compound 8 (protein–small molecule)
id: pr-mitf-compound8
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: ligand series and mutants
matches: protein–small molecule, literature case study, MITF, compound 8, binding enthalpy
cite: mitf_2025
legacy_id: case_mitf_compound8

System: cell: MITF; syringe: compound 8.
Reported: Kd = 470 ± 170 nM; ΔH = -49.4 ± 6.5 kJ/mol; −TΔS = 12.4 ± 8.7 kJ/mol; ΔG = -36.10 ± 0.88 kJ/mol; pKd = 6.33.
Authors' interpretation: Enthalpically driven binding with an unfavorable entropic contribution.
Takeaway for interpretation: An unfavorable entropy contribution does not imply weak binding when enthalpy more than compensates.

## MITF with compound 9 (protein–small molecule)
id: pr-mitf-compound9
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: ligand series and mutants
matches: protein–small molecule, literature case study, MITF, compound 9, binding enthalpy
cite: mitf_2025
legacy_id: case_mitf_compound9

System: cell: MITF; syringe: compound 9.
Reported: Kd = 390 ± 200 nM; ΔH = -42.1 ± 6.1 kJ/mol; −TΔS = 7.6 ± 3.9 kJ/mol; ΔG = -36.8 ± 1.4 kJ/mol; pKd = 6.41.
Comparison with MITF with compound 8 (protein–small molecule): interpretation = Similar free energies arise from somewhat different enthalpy/entropy partitions..

## CDIN1 with Codanin-1 C-terminal fragment (protein–protein)
id: pr-cdin1-codanin
kind: precedent
basis: literature
status: draft
topics: protein–protein; related study sets: ITC detection limits
matches: protein–protein, literature case study, CDIN1, Codanin-1 C-terminal fragment
cite: cdin1_2026
legacy_id: case_cdin1_codanin

System: cell: CDIN1; syringe: Codanin-1 C-terminal fragment.
Reported: Kd = 1.66 ± 0.54 µM; pKd = 5.78.
Observations:
- Exothermic event with approximate 1:1 stoichiometry.
- The isotherm did not show the expected sigmoidal approach to baseline.
Authors' interpretation:
- The abnormal isotherm could reflect buffer mismatch or another pathology.
- The data were still taken as evidence of direct complex formation.
Takeaway for interpretation:
- Separate 'evidence that an interaction occurs' from 'confidence in fitted thermodynamic parameters'.
- A visibly pathological isotherm should downgrade quantitative confidence even if the qualitative interaction is supported.

## FAM118B head mutant with FAM118B tail mutant (protein–protein)
id: pr-fam118-high-c
kind: precedent
basis: literature
status: draft
topics: protein–protein; related study sets: ITC detection limits
matches: protein–protein, literature case study, FAM118B head mutant, FAM118B tail mutant
cite: fam118_2025
legacy_id: case_fam118_high_c

System: cell: FAM118B head mutant; syringe: FAM118B tail mutant.
Observations:
- One-site fit produced a very steep transition characteristic of a high-c interaction.
- The Kd appeared to be in the low-to-high nanomolar range but could not be precisely determined.
Takeaway for interpretation:
- Do not report a precise Kd merely because an optimizer returns one when the transition is too steep to identify affinity.
- High-c data can strongly determine stoichiometry and enthalpy while leaving Kd weakly identified.

## GMPPNP-loaded human Rac1 (residues 1-177) with human POSH intrinsically disordered region (residues 315-380) (protein–protein IDR) (part 1 of 2)
id: pr-idp-zero-enthalpy
kind: precedent
basis: literature
status: draft
topics: protein–protein IDR; related study sets: disordered-region binding, ITC detection limits
matches: protein–protein IDR, literature case study, GMPPNP-loaded human Rac1 (residues 1-177), human POSH intrinsically disordered region (residues 315-380), one set of sites ITC with temperature series, heat capacity change, binding enthalpy
cite: hierarchical_idp_2025
legacy_id: case_idp_zero_enthalpy

System: cell: GMPPNP-loaded human Rac1 (residues 1-177); syringe: human POSH intrinsically disordered region (residues 315-380).
Conditions: model reported: one set of sites ITC with temperature series.
Reported: Kd range = 21–46 µM; Kd at 288.15 K = 21.6 µM; ΔH at 288.15 K = -7.1 kcal/mol; ΔCp = -2800 J/(mol K); Kd extrapolated at 298.15 K = 24 µM; temperature range = 278–308 K; replicates = 2; buffer = 50 mM HEPES pH 6.9, 150 mM NaCl, 5 mM MgCl2, 2 mM TCEP.
Observations:
- Across 5-35 °C, Kd ranged from 21 to 46 µM; the 15 °C measurement gave Kd = 21.6 µM and ΔH = -7.1 kcal/mol.
- The fitted heat-capacity change was ΔCp = -2.8 kJ/(mol K).
- Kd could not be determined directly at 25 °C because the binding enthalpy was close to zero; the authors estimated Kd = 24 µM by linear extrapolation of Gibbs free energies measured at other temperatures.
- NMR and crystallography showed that approximately 50 POSH residues, arranged as two molecular-recognition elements, interact with Rac1; the POSH-Rac1 complex buries about 1830 A^2 of Rac1 surface area.

## GMPPNP-loaded human Rac1 (residues 1-177) with human POSH intrinsically disordered region (residues 315-380) (protein–protein IDR) (part 2 of 2)
id: pr-idp-zero-enthalpy-2
kind: precedent
basis: literature
status: draft
topics: protein–protein IDR; related study sets: disordered-region binding, ITC detection limits
matches: protein–protein IDR, literature case study, GMPPNP-loaded human Rac1 (residues 1-177), human POSH intrinsically disordered region (residues 315-380), one set of sites ITC with temperature series, heat capacity change, binding enthalpy
cite: hierarchical_idp_2025
legacy_id: case_idp_zero_enthalpy

Authors' interpretation:
- The large negative ΔCp supports substantial hydrophobic-surface burial during binding and, together with entropy analysis, extensive coupled folding of POSH.
- Spolar-Record analysis estimated approximately 40 POSH residues fold upon binding; the unfavorable conformational entropy is compensated by favorable protein-surface desolvation.
- POSH follows a hierarchical pathway through structurally distinct intermediates: partial CRIB anchoring, MRE1 folding, then MRE2 folding.
Takeaway for interpretation:
- Lack of measurable heat is not evidence of no binding.
- If ΔH is near zero, ITC may become uninformative at that temperature; temperature variation can restore signal, but an extrapolated Kd should be labeled as derived rather than directly measured.
- For extended IDP interfaces, ΔCp and entropy decomposition can support coupled folding, but the number and order of folding elements require orthogonal structural or spectroscopic evidence.
Flags: near zero binding enthalpy.

## OEP21 with ATP (membrane protein–metabolite)
id: pr-oep21-atp
kind: precedent
basis: literature
status: draft
topics: membrane protein–metabolite; related study sets: ITC detection limits
matches: membrane protein–metabolite, literature case study, OEP21, ATP
cite: oep21_2023
legacy_id: case_oep21_ATP

System: cell: OEP21; syringe: ATP.
Reported: Kd: (approximate: yes; range description = low micromolar); stoichiometry = approximately 1:1.
Observations:
- ITC detected the high-affinity internal ATP site.
- A second peripheral site with Kd around 500 µM was detected by NMR but not by ITC.
Authors' interpretation: Binding is dominated by negative charge density and bulk electrostatics.
Takeaway for interpretation:
- Failure to resolve a weak secondary site by ITC does not exclude its existence.
- Orthogonal methods operating in different concentration/sensitivity regimes can reveal additional binding modes.

## OEP21 with GAP (membrane protein–metabolite)
id: pr-oep21-gap
kind: precedent
basis: literature
status: draft
topics: membrane protein–metabolite; related study sets: ITC detection limits
matches: membrane protein–metabolite, literature case study, OEP21, GAP
cite: oep21_2023
legacy_id: case_oep21_GAP

System: cell: OEP21; syringe: GAP.
Reported: Kd = 150 µM; method for value = NMR; pKd = 3.82.
Observations:
- GAP was substantially weaker than ATP.
- The flexible L5 region increased GAP affinity by approximately fourfold.
Takeaway for interpretation: Context-dependent affinity changes from accessory regions should not automatically be interpreted as a new discrete binding site.

## OEP21 with AMP (negative or below detection)
id: pr-oep21-amp
kind: precedent
basis: literature
status: draft
topics: negative or below detection; related study sets: ITC detection limits
matches: negative or below detection, literature case study, OEP21, AMP
cite: oep21_2023
legacy_id: case_oep21_AMP

System: cell: OEP21; syringe: AMP.
Observations: No binding could be detected by ITC.
Takeaway for interpretation: 'No binding detected by ITC' should be rendered as a detection statement, not as proof that Kd is infinite or binding is absent.

## molecular tweezer TW with Survivin120 (protein–small molecule multisite)
id: pr-survivin-tw
kind: precedent
basis: literature
status: draft
topics: protein–small molecule multisite; related study sets: multisite binding and stoichiometry
matches: protein–small molecule multisite, literature case study, molecular tweezer TW, Survivin120, one set of sites
cite: survivin_2021
legacy_id: case_survivin_TW

System: cell: molecular tweezer TW; syringe: Survivin120.
Conditions: model reported: one set of sites.
Reported: Kd = 38 µM; stoichiometry ligand per protein = 20; pKd = 4.42.
Authors' interpretation: High apparent stoichiometry was considered broadly compatible with many accessible lysine/arginine residues.
Takeaway for interpretation:
- A large fitted n can be physically meaningful in a multivalent surface-binding system; it is not automatically a concentration error.
- Interpret n in the structural/chemical context before labeling it anomalous.

## TW-ELTL with Survivin120 (protein–small molecule multisite)
id: pr-survivin-tw-eltl
kind: precedent
basis: literature
status: draft
topics: protein–small molecule multisite; related study sets: multisite binding and stoichiometry
matches: protein–small molecule multisite, literature case study, TW-ELTL, Survivin120
cite: survivin_2021
legacy_id: case_survivin_TW_ELTL

System: cell: TW-ELTL; syringe: Survivin120.
Reported: Kd = 24 µM; stoichiometry ligand per protein = 2; pKd = 4.62.
Comparison with molecular tweezer TW with Survivin120 (protein–small molecule multisite): interpretation = Peptide modification modestly improved affinity but dramatically changed apparent stoichiometry..
Takeaway for interpretation: Changes in fitted stoichiometry can be mechanistically informative when accompanied by a designed change in molecular architecture.

## TW-ELTLGEFL with Survivin120 (protein–small molecule multisite)
id: pr-survivin-tw-eltlgefl
kind: precedent
basis: literature
status: draft
topics: protein–small molecule multisite
matches: protein–small molecule multisite, literature case study, TW-ELTLGEFL, Survivin120
cite: survivin_2021
legacy_id: case_survivin_TW_ELTLGEFL

System: cell: TW-ELTLGEFL; syringe: Survivin120.
Reported: Kd = 19 µM; stoichiometry ligand per protein = 2; pKd = 4.72.

## TW-ELTL with Survivin120 K90/103T (mutation effect)
id: pr-survivin-mutant-tw-eltl
kind: precedent
basis: literature
status: draft
topics: mutation effect
matches: mutation effect, literature case study, TW-ELTL, Survivin120 K90/103T
cite: survivin_2021
legacy_id: case_survivin_mutant_TW_ELTL

System: cell: TW-ELTL; syringe: Survivin120 K90/103T.
Reported: Kd = 50 ± 10 µM; stoichiometry ligand per protein = 1; pKd = 4.3.
Comparison with TW-ELTL with Survivin120 (protein–small molecule multisite): interpretation = Mutation of proposed anchor residues weakened binding and altered apparent stoichiometry..

## Zα domain with AluSx1Jo RNA (protein–RNA multisite)
id: pr-adar1-alu-two-site
kind: precedent
basis: literature
status: draft
topics: protein–RNA multisite; related study sets: RNA binding, multisite binding and stoichiometry
matches: protein–RNA multisite, literature case study, Zα domain, AluSx1Jo RNA, two site
cite: adar1_zrna_2021
legacy_id: case_adar1_alu_two_site

System: cell: Zα domain; syringe: AluSx1Jo RNA.
Conditions: model reported: two site.
Reported: site 1 Kd = 1.1 ± 8.8 µM; site 2 Kd = 40 ± 100 nM.
Observations:
- Fit errors were larger than or comparable to parameter magnitudes.
- Authors explicitly noted that other ITC parameters indicated more complex binding behavior.
Takeaway for interpretation:
- A numerically more complex model does not make site-specific parameters reliable when uncertainties are enormous.
- Large relative uncertainty should trigger language such as 'consistent with complex/multisite binding' rather than precise mechanistic claims.

## h43 RNA with Zα domain (protein–RNA multisite)
id: pr-adar1-h43-multisite
kind: precedent
basis: literature
status: draft
topics: protein–RNA multisite; related study sets: RNA binding, multisite binding and stoichiometry
matches: protein–RNA multisite, literature case study, h43 RNA, Zα domain
cite: adar1_zrna_2021
legacy_id: case_adar1_h43_multisite

System: cell: h43 RNA; syringe: Zα domain.
Reported: site 1 Kd = 540 ± 310 nM; site 2 Kd = 500 ± 1700 nM.
Observations:
- Multiple exothermic phases were observed.
- Up to four Zα domains were inferred to bind in the final state using ITC together with AUC.
Takeaway for interpretation: Use orthogonal stoichiometry information to support interpretation of complex ITC isotherms.

## trim21 dimer dissociation (self association)
id: pr-trim21-dimer-dissociation
kind: precedent
basis: literature
status: draft
topics: self association; related study sets: self-assembly and supramolecular systems
matches: self association, literature case study, TRIM21 coiled-coil construct, dimer dissociation, binding enthalpy
cite: trim21_2021
legacy_id: case_trim21_dimer_dissociation

System: species: TRIM21 coiled-coil construct.
Conditions: model reported: dimer dissociation.
Reported: Kd = 7 µM; ΔH = 272 kJ/mol; pKd = 5.15.
Takeaway for interpretation: For self-association, fitted stoichiometry and enthalpy refer to the association/dissociation model and should not be narrated as ordinary ligand occupancy.

## Spindlin1 or Spindlin1/C11orf84 with H3K4me3K9me3 peptide (protein–peptide bivalent)
id: pr-spindlin-bivalent
kind: precedent
basis: literature
status: draft
topics: protein–peptide bivalent; related study sets: multisite binding and stoichiometry
matches: protein–peptide bivalent, literature case study, Spindlin1 or Spindlin1/C11orf84, H3K4me3K9me3 peptide
cite: spindlin_2021
legacy_id: case_spindlin_bivalent

System: cell: Spindlin1 or Spindlin1/C11orf84; syringe: H3K4me3K9me3 peptide.
Observations: The Spindlin1/C11orf84 heterodimer bound the bivalent histone peptide about twofold more strongly than Spindlin1 alone.
Takeaway for interpretation: Fold-change comparisons can be robust and biologically useful even when absolute thermodynamic decomposition is not central to the paper.

## HPMC-C12 polymer with polystyrene nanoparticles (polymer–nanoparticle)
id: pr-entropy-driven-endothermic
kind: precedent
basis: literature
status: draft
topics: polymer–nanoparticle; related study sets: self-assembly and supramolecular systems
matches: polymer–nanoparticle, literature case study, HPMC-C12 polymer, polystyrene nanoparticles, 1:1 effective binding scheme
cite: physical_networks_2021
legacy_id: case_entropy_driven_endothermic

System: cell: HPMC-C12 polymer; syringe: polystyrene nanoparticles.
Conditions: model reported: 1:1 effective binding scheme.
Observations:
- Binding/adsorption was endothermic.
- The positive entropy contribution was much larger in magnitude than the enthalpy term.
- A single inflection point and similar effective affinities/enthalpies supported an effective 1:1 description.
Authors' interpretation: Association was favorable solely because of a large positive entropy contribution.
Takeaway for interpretation:
- Endothermic binding can be strongly favorable.
- Do not equate exothermicity with favorable binding or endothermicity with weak binding.

## bowl tube (supramolecular host guest)
id: pr-bowl-tube
kind: precedent
basis: literature
status: draft
topics: supramolecular host guest; related study sets: self-assembly and supramolecular systems
matches: supramolecular host guest, literature case study, [4]CC tube, COR bowl, binding enthalpy
cite: bowl_tube_2018
legacy_id: case_bowl_tube

System: host: [4]CC tube; guest: COR bowl.
Conditions: T = 298 K (24.9 °C).
Reported: Ka = 2940 ± 240 M^-1; Kd = 340 µM; ΔG = -19.8 kJ/mol; ΔH = -28.3 kJ/mol; −TΔS = 8.49 kJ/mol; pKd = 3.47.
Authors' interpretation:
- Complexation was enthalpy-driven with an unfavorable entropy term.
- The measured enthalpy was used, with structural assumptions, to estimate the contribution per CH–π contact.
Takeaway for interpretation: Per-contact energetic decompositions are model-dependent and should be described as estimates, not directly measured microscopic energies.

## galectin M (protein–small molecule)
id: pr-galectin-m
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: carbohydrate recognition, ligand series and mutants
matches: protein–small molecule, literature case study, Galectin-3C, M, binding enthalpy
cite: galectin_jacsau_2021
legacy_id: case_galectin_M

System: protein: Galectin-3C; ligand: M.
Reported: Kd = 2.00 ± 0.29 µM; ΔG = -32.9 kJ/mol; ΔH = -50.4 kJ/mol; −TΔS = 17.5 kJ/mol; pKd = 5.7.

## galectin P (protein–small molecule)
id: pr-galectin-p
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: carbohydrate recognition, ligand series and mutants
matches: protein–small molecule, literature case study, Galectin-3C, P, binding enthalpy
cite: galectin_jacsau_2021
legacy_id: case_galectin_P

System: protein: Galectin-3C; ligand: P.
Reported: Kd = 2.46 ± 0.24 µM; ΔG = -32.3 kJ/mol; ΔH = -49.1 kJ/mol; −TΔS = 16.7 kJ/mol; pKd = 5.61.
Comparison with galectin M (protein–small molecule): interpretation = Near-identical affinities and thermodynamic partitions..

## galectin O (protein–small molecule)
id: pr-galectin-o
kind: precedent
basis: literature
status: draft
topics: protein–small molecule; related study sets: carbohydrate recognition, ligand series and mutants
matches: protein–small molecule, literature case study, Galectin-3C, O, binding enthalpy
cite: galectin_jacsau_2021
legacy_id: case_galectin_O

System: protein: Galectin-3C; ligand: O.
Reported: Kd = 7.2 ± 1.3 µM; ΔG = -29.6 kJ/mol; ΔH = -45.5 kJ/mol; −TΔS = 15.8 kJ/mol; pKd = 5.14.
Comparison with galectin M (protein–small molecule): interpretation = Weaker affinity accompanies both less favorable enthalpy and a somewhat smaller entropic penalty..

## trimethyllysine comparison (comparative thermodynamics)
id: pr-trimethyllysine-comparison
kind: precedent
basis: literature
status: draft
topics: comparative thermodynamics; related study sets: ligand series and mutants
matches: comparative thermodynamics, literature case study
cite: trimethyllysine_2015
legacy_id: case_trimethyllysine_comparison

Observations:
- H3K4me3 bound 2–33-fold more strongly than the neutral H3C4me3 analog for four of five Trp-containing reader proteins.
- Kme3 association was on average about 4.3 kcal/mol more favorable in enthalpy but about 3.1 kcal/mol less favorable in entropy.
- The SGF29 reader was an exception with nearly indistinguishable thermodynamics.
Authors' interpretation: The comparison supported a favorable cation–π contribution for several readers while demonstrating that the interpretation is reader-dependent.
Takeaway for interpretation:
- A trend across a chemical series is stronger evidence for an interaction mechanism than the sign of ΔH from a single ligand.
- Explicit counterexamples should be retained; they prevent overgeneralization.

## JARID1A PHD3 with H3G4 peptide (protein–peptide)
id: pr-jarid1a-h3g4
kind: precedent
basis: literature
status: draft
topics: protein–peptide; related study sets: ligand series and mutants
matches: protein–peptide, literature case study, JARID1A PHD3, H3G4 peptide, binding enthalpy
cite: trimethyllysine_2015
legacy_id: case_JARID1A_H3G4

System: cell: JARID1A PHD3; syringe: H3G4 peptide.
Reported: Kd = 88 µM; ΔG = -23 kJ/mol; ΔH = -8.79 kJ/mol; −TΔS = -14.2 kJ/mol; pKd = 4.06.
Observations: Removing the entire Kme3 side chain reduced binding by more than 500-fold relative to H3K4me3 across the reader set.

## TAF3 PHD with H3G4 peptide (protein–peptide)
id: pr-taf3-h3g4
kind: precedent
basis: literature
status: draft
topics: protein–peptide; related study sets: ligand series and mutants
matches: protein–peptide, literature case study, TAF3 PHD, H3G4 peptide, binding enthalpy
cite: trimethyllysine_2015
legacy_id: case_TAF3_H3G4

System: cell: TAF3 PHD; syringe: H3G4 peptide.
Reported: Kd = 36 µM; ΔG = -25.5 kJ/mol; ΔH = -10.5 kJ/mol; −TΔS = -15.1 kJ/mol; pKd = 4.44.

## potassium sequential (sequential two site)
id: pr-potassium-sequential
kind: precedent
basis: literature
status: draft
topics: sequential two site; related study sets: metal-ion binding
matches: sequential two site, literature case study, bis(18-crown-6) Tröger's base analogue, K+
cite: potassium_jacs_2022
legacy_id: case_potassium_sequential

System: host: bis(18-crown-6) Tröger's base analogue; guest: K+.
Observations:
- The second K+ binding step had a more favorable (more negative) enthalpy than the first.
- Despite this, the second binding free energy was less favorable.
- For KCl, the second step had an additional entropic penalty of 3.78 kcal/mol relative to the first; about 0.8 kcal/mol was attributed to the statistical factor.
Takeaway for interpretation:
- More favorable ΔH for a later binding step does not imply positive cooperativity in affinity.
- Cooperativity should be assessed from free energies/equilibrium constants, not enthalpy alone.

## RNase A with 2'CMP (concentration series)
id: pr-wiseman-rnase-concentration-dependence
kind: precedent
basis: literature
status: draft
topics: concentration series; related study sets: ITC methodology
matches: concentration series, literature case study, RNase A, 2'CMP, binding enthalpy
cite: wiseman_1989
legacy_id: case_wiseman_RNase_concentration_dependence

System: cell: RNase A; syringe: 2'CMP.
Conditions: T = 301.15 K (28.0 °C).
Series: (RNase = 634 µM, Ka = 80500 M^-1, n = 0.95, ΔH = -51.9 kJ/mol); (RNase = 175 µM, Ka = 100000 M^-1, n = 0.98, ΔH = -50.2 kJ/mol); (RNase = 44.6 µM, Ka = 115000 M^-1, n = 0.99, ΔH = -48.1 kJ/mol); (RNase = 14.5 µM, Ka = 135000 M^-1, n = 1, ΔH = -53.6 kJ/mol).
Authors' interpretation: The systematic concentration dependence of fitted affinity, despite good repeatability at a fixed concentration, suggested a concentration-dependent process such as RNase association/aggregation.
Takeaway for interpretation:
- Reproducibility at one concentration does not rule out systematic concentration-dependent bias or coupled equilibria.
- A concentration series is informative when fitted affinity shifts systematically beyond within-condition uncertainty.

## NEMO CC2-LZ with tandem diubiquitin (protein–protein)
id: pr-nemo-tandem-diub
kind: precedent
basis: literature
status: draft
topics: protein–protein; related study sets: multisite binding and stoichiometry
matches: protein–protein, literature case study, NEMO CC2-LZ, tandem diubiquitin, binding enthalpy
cite: nemo_2009
legacy_id: case_nemo_tandem_diub

System: cell: NEMO CC2-LZ; syringe: tandem diubiquitin.
Reported: Kd = 1.4 µM; stoichiometry N = 2.1; stoichiometry error = 0.02; ΔH = -11.72 ± 0.17 kJ/mol; −TΔS = -21.3 kJ/mol.
Observations:
- The fitted stoichiometry was approximately two NEMO monomers per diubiquitin, consistent with one NEMO dimer per diubiquitin.
- Binding was exothermic, with an additional favorable entropy contribution.
Takeaway for interpretation:
- For a dimeric receptor, a fitted N near 2 can represent one receptor dimer per ligand rather than two independent receptor complexes.
- Stoichiometry should be interpreted against the oligomeric state of the binding partner.

## NEMO CC2-LZ with K63-linked diubiquitin (protein–protein)
id: pr-nemo-k63-diub
kind: precedent
basis: literature
status: draft
topics: protein–protein; related study sets: multisite binding and stoichiometry
matches: protein–protein, literature case study, NEMO CC2-LZ, K63-linked diubiquitin, binding enthalpy
cite: nemo_2009
legacy_id: case_nemo_k63_diub

System: cell: NEMO CC2-LZ; syringe: K63-linked diubiquitin.
Reported: Kd = 131 µM; stoichiometry N = 2; stoichiometry error = 0.14; ΔH = 15.1 ± 1.5 kJ/mol; −TΔS = -37.2 kJ/mol.
Comparison with NEMO CC2-LZ with tandem diubiquitin (protein–protein): interpretation = The same approximate 2:1 stoichiometry masks a roughly 100-fold affinity difference and a reversal in the sign of ΔH..
Takeaway for interpretation:
- Similar stoichiometry does not imply similar binding energetics or affinity.
- A favorable free energy can arise despite an endothermic enthalpy when the entropy contribution is sufficiently favorable.

## V. cholerae HigA2 with 45-bp operator DNA (Opr45) (protein–DNA IDR)
id: pr-higa2-operator-full
kind: precedent
basis: literature
status: draft
topics: protein–DNA IDR; related study sets: disordered-region binding
matches: protein–DNA IDR, literature case study, V. cholerae HigA2, 45-bp operator DNA (Opr45), single site
cite: higa2_2024
legacy_id: case_higa2_operator_full

System: cell: V. cholerae HigA2; syringe: 45-bp operator DNA (Opr45).
Conditions: model reported: single site.
Reported: Kd = 25.1 ± 5.2 nM.
Observations:
- The disordered N-terminal region remained involved in a specific, transient interaction with the operator DNA.
- NMR, SAXS, ITC, and in vivo experiments converged on a fuzzy recognition mechanism.
Comparison with V. cholerae HigA2 ΔIDR with 45-bp operator DNA (Opr45) (protein–DNA IDR truncation): fold Kd change = 15.9; interpretation = The full-length antitoxin bound the operator about 16-fold more tightly than the folded-domain truncation..
Takeaway for interpretation:
- A high-affinity interaction need not imply a single rigid bound conformation.
- A fitted one-site curve can summarize a dominant effective transition while structural methods reveal dynamic or fuzzy contacts.

## V. cholerae HigA2 ΔIDR with 45-bp operator DNA (Opr45) (protein–DNA IDR truncation)
id: pr-higa2-operator-deltaidr
kind: precedent
basis: literature
status: draft
topics: protein–DNA IDR truncation; related study sets: disordered-region binding
matches: protein–DNA IDR truncation, literature case study, V. cholerae HigA2 ΔIDR, 45-bp operator DNA (Opr45), single site
cite: higa2_2024
legacy_id: case_higa2_operator_deltaIDR

System: cell: V. cholerae HigA2 ΔIDR; syringe: 45-bp operator DNA (Opr45).
Conditions: model reported: single site.
Reported: Kd = 400 ± 50 nM.
Comparison with V. cholerae HigA2 with 45-bp operator DNA (Opr45) (protein–DNA IDR): fold Kd change = 15.9; affinity change = weaker; interpretation = Removing the disordered region weakened operator binding without eliminating the interaction..
Takeaway for interpretation: Deletion of a disordered segment can change affinity substantially while preserving a measurable calorimetric transition.

## heparin with keratinocyte growth factor 2 (KGF-2) (protein–polyanion)
id: pr-kgf2-heparin-biphasic
kind: precedent
basis: literature
status: draft
topics: protein–polyanion; related study sets: ITC methodology
matches: protein–polyanion, literature case study, heparin, keratinocyte growth factor 2 (KGF-2), two independent modes, binding enthalpy
cite: kgf2_heparin_2014
legacy_id: case_kgf2_heparin_biphasic

System: cell: heparin; syringe: keratinocyte growth factor 2 (KGF-2).
Conditions: model reported: two independent modes.
Reported: exothermic mode: (stoichiometry N = 3.8; association constant = 4.8e+08 M^-1; ΔH = -62.8 kJ/mol; ΔS = -51.9 J/(mol K)); endothermic mode: (stoichiometry N = 1; association constant = 3.2e+06 M^-1; ΔH = 50.2 kJ/mol; ΔS = 287 J/(mol K)).
Observations: The data were resolved into exothermic and endothermic components after subtraction of dilution controls.
Takeaway for interpretation:
- A non-monotonic or composite titration can reflect multiple interaction modes rather than a poor single-site fit alone.
- Blank subtraction is essential when polyelectrolyte/protein dilution contributes appreciable heat.

## MPX antimicrobial peptide with POPC/POPG (3:1) large unilamellar vesicles (peptide–membrane)
id: pr-mpx-popc-popg-membrane
kind: precedent
basis: literature
status: draft
topics: peptide–membrane; related study sets: peptide–membrane interactions
matches: peptide–membrane, literature case study, MPX antimicrobial peptide, POPC/POPG (3:1) large unilamellar vesicles
cite: peptide_membrane_2011
legacy_id: case_mpx_POPC_POPG_membrane

System: cell: MPX antimicrobial peptide; syringe: POPC/POPG (3:1) large unilamellar vesicles.
Observations:
- Some titrations showed both exothermic and endothermic spikes and did not resemble a monotonic binding isotherm.
- The heat trace was treated as a superposition of peptide partitioning, a reversible process such as pore formation or micellation, and dilution heat.
Takeaway for interpretation:
- For membrane systems, a standard binding model may be physically inappropriate because partitioning, membrane remodeling, pore formation, micellation, ion exchange, and dilution can overlap.
- A complex thermogram should be decomposed with controls and orthogonal evidence before reporting a single Kd.

## water with concentrated NBI 1 amphiphilic dye aggregate (small molecule self assembly)
id: pr-nbi1-selfassembly
kind: precedent
basis: literature
status: draft
topics: small molecule self assembly; related study sets: self-assembly and supramolecular systems
matches: small molecule self assembly, literature case study, water, concentrated NBI 1 amphiphilic dye aggregate, isodesmic dilution
cite: nbi_selfassembly_2019
legacy_id: case_nbi1_selfassembly

System: cell: water; syringe: concentrated NBI 1 amphiphilic dye aggregate.
Conditions: model reported: isodesmic dilution; T = 298.15 K (25.0 °C).
Reported: standard disassembly enthalpy = -13.8 kJ/mol; log10 K ass = 3.8; critical aggregation concentration = 210 µM.
Observations:
- ITC dilution was fit with an isodesmic model and agreed with independent UV-vis estimates.
- Related NBI structures changed the aggregation mechanism from isodesmic to weakly anti-cooperative.
Takeaway for interpretation:
- ITC dilution can report self-assembly thermodynamics, but the sign refers to disassembly versus assembly and must be labeled explicitly.
- The absence of a clear endpoint or saturation in dilution data can prevent reliable critical-aggregation-concentration estimation.

## AATT DNA hairpin or poly(dAdT)·poly(dAdT) with CGP 40215A (small molecule–DNA)
id: pr-cgp40215a-dna-proton-linkage
kind: precedent
basis: literature
status: draft
topics: small molecule–DNA; related study sets: DNA duplexes and DNA ligands
matches: small molecule–DNA, literature case study, AATT DNA hairpin or poly(dAdT)·poly(dAdT), CGP 40215A, sequential strong plus weak sites
cite: nguyen_cgp_2006
legacy_id: case_cgp40215a_DNA_proton_linkage

System: cell: AATT DNA hairpin or poly(dAdT)·poly(dAdT); syringe: CGP 40215A.
Conditions: model reported: sequential strong plus weak sites; T = 298.15 K (25.0 °C).
Reported: free ligand pKa = 6.3; bound ligand pKa = 9; bound pKa shift = 2.7; proton uptake at pH 6 25 = 0.49; proton uptake at pH 7 45 = 0.87; primary site ratio ligand per DNA basepair = 0.125.
Observations:
- Binding enthalpy differed between buffers at the same pH because the buffers had different ionization enthalpies.
- A weaker nonspecific interaction appeared at higher ligand/DNA ratios in addition to the strong primary site.
- Affinity varied with pH and salt concentration, and ITC proton-uptake estimates agreed with spectroscopic titrations.
Takeaway for interpretation:
- Buffer-dependent ΔH is evidence that observed binding enthalpy may include linked protonation; it should not be treated as an intrinsic interaction enthalpy without linkage analysis.
- Strong primary binding and weaker nonspecific binding can coexist in nucleic-acid titrations, making a single-site model incomplete.

## DREB2A fragments with RCD1-RST(499–572) (protein–protein IDR coupled folding)
id: pr-theisen2021-dreb2a-rcd1-context
kind: precedent
basis: literature
status: draft
topics: protein–protein IDR coupled folding; related study sets: disordered-region binding
matches: protein–protein IDR coupled folding, literature case study, DREB2A fragments, RCD1-RST(499–572), single site with temperature series, heat capacity change
cite: theisen_jacs_2021
legacy_id: case_theisen2021_DREB2A_RCD1_context

System: cell: DREB2A fragments; syringe: RCD1-RST(499–572).
Conditions: model reported: single site with temperature series.
Observations:
- DREB2A243–272 and DREB2A234–287 bound in the low-nanomolar regime, whereas the shorter or differently truncated fragments were weaker.
- The N-terminally extended fragments gained affinity mainly through favorable enthalpy but also incurred a large unfavorable entropy contribution, producing enthalpy–entropy compensation.
- Temperature-dependent ITC gave ΔCp values of −1.3 ± 0.2, −1.4 ± 0.1, −1.8 ± 0.1, and −1.8 ± 0.1 kJ mol−1 K−1 for DREB2A255–272, 250–287, 243–272, and 234–287, respectively.
- The ID-adapted Spolar–Record analysis estimated 20 ± 5, 22 ± 2, 32 ± 3, and 31 ± 2 residues folding upon binding for those fragments, respectively.
Takeaway for interpretation:
- Disordered flanking sequence can strengthen binding while increasing the conformational entropy penalty; affinity alone does not reveal whether added context is structurally important.
- For IDR interactions, heat-capacity-derived folding estimates depend on the calibration model and should be presented as model-dependent inferences, not direct residue counts.

## 14–3–3ζ with phosphorylated human PRLR peptide (protein–peptide IDR proline isomerization)
id: pr-theisen2025-prlr-1433-isomer
kind: precedent
basis: literature
status: draft
topics: protein–peptide IDR proline isomerization; related study sets: 14-3-3 phosphopeptide complexes, disordered-region binding
matches: protein–peptide IDR proline isomerization, literature case study, 14–3–3ζ, phosphorylated human PRLR peptide, apparent one site plus kinetic model
cite: theisen_jacs_2025
legacy_id: case_theisen2025_PRLR_1433_isomer

System: cell: 14–3–3ζ; syringe: phosphorylated human PRLR peptide.
Conditions: model reported: apparent one site plus kinetic model.
Reported: WT apparent Kd = 230 ± 30 nM; WT Kd cis = 130 ± 40 nM; WT Kd trans = 80 ± 20 µM; W392Y Kd cis = 430 ± 60 nM; W392Y Kd trans = 8.0 ± 2.0 µM.
Observations:
- The WT ITC curve showed a slow post-injection kinetic phase and a fitted one-site Kd that was only an apparent affinity.
- The cis proline isomer bound about 600-fold more tightly than the trans isomer in the WT peptide based on the modeled affinities.
- A W392Y variant reduced cis selectivity: both isomers bound measurably, with an affinity ratio of about 19 between trans and cis.
Takeaway for interpretation:
- A one-site ITC fit can return an apparent Kd when slowly interconverting conformers have different affinities.
- Injection spacing and equilibration time are part of the measurement model when conformational exchange is slow; apparent affinity and kinetic accessibility should be separated.

## Med25-ACID with Arabidopsis DREB2A disordered fragments (protein–protein IDR bivalent)
id: pr-theisen2024-dreb2a-med25-switch
kind: precedent
basis: literature
status: draft
topics: protein–protein IDR bivalent; related study sets: disordered-region binding, ITC detection limits
matches: protein–protein IDR bivalent, literature case study, Med25-ACID, Arabidopsis DREB2A disordered fragments, single site with fragment series, binding enthalpy
cite: theisen_natcomm_2024
legacy_id: case_theisen2024_DREB2A_Med25_switch

System: cell: Med25-ACID; syringe: Arabidopsis DREB2A disordered fragments.
Conditions: model reported: single site with fragment series.
Reported: DREB2A151 335 Kd = 540 ± 40 nM; DREB2A234 272 Kd = 1.80 ± 0.40 µM; DREB2A234 256 Kd = 6.00 ± 0.20 µM; DREB2A195 335 delta244 276 Kd = 5.40 ± 0.60 µM; Med25 R568A Kd = 3.10 ± 0.70 µM; Med25 R568A delta H = -46 kJ/mol; Med25 R568A minus T delta S = 14 kJ/mol.
Observations:
- The DREB2A region contains two motifs, ABS and RIM, that bind separate Med25-ACID surfaces in a bivalent configuration.
- Removing or shortening flanking sequence weakened affinity, while the isolated RIM did not produce detectable ITC binding despite NMR perturbations.
- NMR showed structural heterogeneity caused by cis/trans proline isomerization; the trans isomer can introduce energetic frustration that facilitates regulator exchange.
Takeaway for interpretation:
- A fragment that is ITC-silent can still make weak or transient contacts detectable by another method; no ITC heat is not proof of no molecular contact.
- Bivalent IDR binding can be strongly context-dependent, so a fitted affinity for one fragment should not be generalized to the isolated motif or full biological interaction.

## site-specific protein–DNA complexes and model protein–ligand associations with binding partner (protein–DNA thermodynamic framework)
id: pr-spolar-record-1994-heat-capacity-signature
kind: precedent
basis: literature
status: draft
topics: protein–DNA thermodynamic framework; related study sets: protein–DNA thermodynamics
matches: protein–DNA thermodynamic framework, literature case study, binding partner, heat capacity change
cite: spolar_record_science_1994
legacy_id: case_spolar_record_1994_heat_capacity_signature

System: cell: site-specific protein–DNA complexes and model protein–ligand associations; syringe: binding partner.
Observations:
- The paper used binding thermodynamics, including large negative ΔCp and entropy decomposition, to identify local folding coupled to site-specific DNA binding.
- The estimated number of residues folding on binding agreed with structural data, supporting a thermodynamic signature for induced-fit-like local ordering.
Takeaway for interpretation:
- A large negative ΔCp is not by itself proof of hydrophobic burial; in protein–DNA recognition it can also indicate coupled local folding and interface formation.
- Temperature series can add mechanistic information beyond a single Kd/ΔH measurement, but residue-folding estimates are derived quantities tied to the chosen thermodynamic decomposition.

## P. aeruginosa LecB with dimethoxycinnamide 7b (lectin–small molecule)
id: pr-prestel-lecb-cinnamide-7b
kind: precedent
basis: literature
status: draft
topics: lectin–small molecule; related study sets: carbohydrate recognition
matches: lectin–small molecule, literature case study, P. aeruginosa LecB, dimethoxycinnamide 7b, single site, binding enthalpy
cite: prestel_lecb_2016
legacy_id: case_prestel_lecb_cinnamide_7b

System: cell: P. aeruginosa LecB; syringe: dimethoxycinnamide 7b.
Conditions: model reported: single site; T = 298.15 K (25.0 °C).
Reported: Kd = 10.9 ± 1.8 µM; stoichiometry N = 1.03; stoichiometry error = 0.09; ΔG = -28.49 ± 0.67 kJ/mol; ΔH = -23.55 ± 0.88 kJ/mol; −TΔS = -4.9 ± 1.5 kJ/mol.
Observations:
- Three independent ITC titrations supported a 1:1 interaction.
- SPR gave Kd = 7.7 µM, close to the ITC value, and a slow on-rate with a complex half-life of about 8.8 minutes.
Takeaway for interpretation:
- Agreement between ITC equilibrium affinity and an orthogonal method can coexist with slow association kinetics; Kd does not specify how rapidly equilibrium is reached.
- A favorable entropy contribution can accompany specific carbohydrate-recognition binding and should not be equated with nonspecific hydrophobic association.

## calmodulin with NHE1 cytoplasmic peptides H1, H2, H1H2, and H1H2-pS648 (protein–peptide multisite) (part 1 of 2)
id: pr-prestel-nhe1-calmodulin-multisite
kind: precedent
basis: literature
status: draft
topics: protein–peptide multisite; related study sets: multisite binding and stoichiometry
matches: protein–peptide multisite, literature case study, calmodulin, NHE1 cytoplasmic peptides H1, H2, H1H2, and H1H2-pS648, one site and two site models
cite: prestel_nhe1_cam_2021
legacy_id: case_prestel_nhe1_calmodulin_multisite

System: cell: calmodulin; syringe: NHE1 cytoplasmic peptides H1, H2, H1H2, and H1H2-pS648.
Conditions: model reported: one site and two site models; T = 298.15 K (25.0 °C).
Reported: H1 two site: (Kd1 = 27.0 ± 8.0 nM; Kd2 = 42.0 ± 6.0 nM; n total = 1.920 ± 0.060); H2 one site: (Kd = 8.90 ± 0.60 µM; n = 1.150 ± 0.080); H1H2 two site: (Kd1 = 270 ± 70 pM; Kd2 = 560 ± 15 nM; n1 = 1.04; n2 = 1.22); H1H2 pS648 two site: (Kd1 = 5.0 ± 4.0 nM; Kd2 = 2.70 ± 0.70 µM).
Observations:
- One CaM molecule bound two H1 peptides with similar high affinities, while H2 bound almost three orders of magnitude more weakly in a 1:1 transition.
- The tandem H1H2 peptide produced two transitions and an overall 1:2 CaM:H1H2 stoichiometry, with affinities differing by more than three orders of magnitude.
- The phosphorylated H1H2 variant shifted both site affinities, showing that a modification can alter multiple binding events in a composite interaction.

## calmodulin with NHE1 cytoplasmic peptides H1, H2, H1H2, and H1H2-pS648 (protein–peptide multisite) (part 2 of 2)
id: pr-prestel-nhe1-calmodulin-multisite-2
kind: precedent
basis: literature
status: draft
topics: protein–peptide multisite; related study sets: multisite binding and stoichiometry
matches: protein–peptide multisite, literature case study, calmodulin, NHE1 cytoplasmic peptides H1, H2, H1H2, and H1H2-pS648, one site and two site models
cite: prestel_nhe1_cam_2021
legacy_id: case_prestel_nhe1_calmodulin_multisite

Takeaway for interpretation:
- A non-integer total n can reflect multiple occupied sites and should be interpreted with the oligomeric and peptide architecture in view.
- A two-transition isotherm can encode sequential or multisite binding; collapsing it to one average Kd loses biologically relevant heterogeneity.

## β-lactoglobulin A with defined alginate oligosaccharides M4–M6, G4–G6, and MG4–MG6 (protein–polysaccharide multivalent)
id: pr-prestel-alginate-blg-multivalency
kind: precedent
basis: literature
status: draft
topics: protein–polysaccharide multivalent; related study sets: carbohydrate recognition, multisite binding and stoichiometry
matches: protein–polysaccharide multivalent, literature case study, β-lactoglobulin A, defined alginate oligosaccharides M4–M6, G4–G6, and MG4–MG6, independent one site per effective site
cite: prestel_alginate_blg_2023
legacy_id: case_prestel_alginate_blg_multivalency

System: cell: β-lactoglobulin A; syringe: defined alginate oligosaccharides M4–M6, G4–G6, and MG4–MG6.
Conditions: model reported: independent one site per effective site; T = 310.15 K (37.0 °C).
Reported: M4 Kd = 520 µM; M5 Kd = 228 µM; M6 Kd = 147 µM; M4 M5 M6 global Kd errors = 33–30–40 µM.
Observations:
- β-lactoglobulin A was highly multivalent: the number of bound oligosaccharides decreased from about five to two as oligosaccharide degree of polymerization increased.
- Longer oligosaccharides occupied overlapping or merged binding sites and showed similar mid-micromolar affinities.
- At pH 2.65 versus 4.00, binding enthalpy and apparent stoichiometry changed; the authors interpreted the lower-pH interaction as more entropy-driven and the higher-pH interaction as more enthalpy-driven.
Takeaway for interpretation:
- For multivalent polymers, fitted n can describe effective molecular coverage rather than a fixed number of independent sites.
- pH-dependent charge states can change both apparent stoichiometry and the enthalpy/entropy partition without requiring a new protein fold.

## trp repressor with operator DNA (protein–DNA multimode)
id: pr-ladbury-trp-repressor-operator
kind: precedent
basis: literature
status: draft
topics: protein–DNA multimode; related study sets: protein–DNA thermodynamics
matches: protein–DNA multimode, literature case study, trp repressor, operator DNA, primary strong plus secondary weak mode, heat capacity change
cite: ladbury_trp_repressor_1994
legacy_id: case_ladbury_trp_repressor_operator

System: cell: trp repressor; syringe: operator DNA.
Conditions: model reported: primary strong plus secondary weak mode.
Observations:
- Temperature-dependent direct titration calorimetry detected a primary strong binding mode and a weaker half-site mode.
- Both modes displayed unusually large negative heat-capacity changes, even though the secondary mode was weak.
- The primary interaction remained enthalpically driven across the physiological temperature range.
Takeaway for interpretation:
- A large negative ΔCp is not a proxy for high affinity; a weak stereospecific interface can show a similarly large ΔCp.
- Saturation of a principal DNA-binding mode does not guarantee that later injections are pure baseline: weaker secondary modes may remain.

## 32 protein systems with 674 ligand modifications (protein–ligand meta analysis)
id: pr-olsson-itc-compensation-dataset
kind: precedent
basis: literature
status: draft
topics: protein–ligand meta analysis; related study sets: ITC methodology
matches: protein–ligand meta analysis, literature case study, 32 protein systems, 674 ligand modifications, paired delta thermodynamics analysis
cite: olsson_compensation_2011
legacy_id: case_olsson_itc_compensation_dataset

System: cell: 32 protein systems; syringe: 674 ligand modifications.
Conditions: model reported: paired delta thermodynamics analysis.
Reported: strong compensation fraction = 0.22; reinforcement fraction = 0.15; compensation better than 10 percent fraction = 0.68.
Observations:
- The study modeled experimental and analytical constraints that can create apparent ΔH/−TΔS correlation.
- Pairwise ΔΔH versus ΔΔG analysis found widespread but imperfect compensation; approximately 22% of modifications showed strong compensation and approximately 15% showed reinforcement.
Takeaway for interpretation:
- Do not infer a physical compensation mechanism from a simple ΔH versus −TΔS correlation across related ligands without accounting for mathematical coupling and measurement constraints.
- Enthalpy–entropy compensation is not universal: ligand modifications can produce compensation, weak compensation, or reinforcement.

## Mg2+:EDTA and protein:ligand benchmark systems with binding partner (itc uncertainty analysis)
id: pr-nguyen-bayesian-itc-uncertainty
kind: precedent
basis: literature
status: draft
topics: itc uncertainty analysis; related study sets: ITC methodology
matches: itc uncertainty analysis, literature case study, Mg2+:EDTA and protein:ligand benchmark systems, binding partner, Bayesian posterior sampling
cite: nguyen_bayesian_itc_2018
legacy_id: case_nguyen_bayesian_itc_uncertainty

System: cell: Mg2+:EDTA and protein:ligand benchmark systems; syringe: binding partner.
Conditions: model reported: Bayesian posterior sampling.
Observations:
- The Bayesian framework incorporated multiple ITC experiments, blank controls, and prior information into posterior parameter distributions.
- The authors showed that conventional nonlinear-regression standard errors can be unrealistically small because they omit dominant sources of experiment-to-experiment variation.
- A multi-laboratory carbonic-anhydrase benchmark had binding constants and enthalpies varying far more than individual least-squares error estimates suggested.
Takeaway for interpretation:
- A narrow optimizer covariance interval is not necessarily a realistic uncertainty interval for ITC parameters.
- Uncertainty should include concentration, baseline, replicate, and control-related variation when those sources are material.

## Sso7d protein with poly(dGdC) and poly(dAdT) DNA (nonspecific protein–DNA binding)
id: pr-lundback-sso7d-condition-dependence
kind: precedent
basis: literature
status: draft
topics: nonspecific protein–DNA binding; related study sets: protein–DNA thermodynamics
matches: nonspecific protein–DNA binding, literature case study, Sso7d protein, poly(dGdC) and poly(dAdT) DNA, McGhee von Hippel noncooperative, heat capacity change, binding enthalpy
cite: lundback_sso7d_1998
legacy_id: case_lundback_sso7d_condition_dependence

System: cell: Sso7d protein; syringe: poly(dGdC) and poly(dAdT) DNA.
Conditions: model reported: McGhee von Hippel noncooperative.
Reported: binding site size base pairs = 4-5; ΔG range = approximately -7 to -10 kcal/mol; ΔH at 25 °C = approximately +10 kcal/mol; ΔCp = -0.25 kcal/(mol K).
Observations:
- ITC was measured across buffer composition, temperature 15-45 °C, pH 7.1-8.0, osmotic stress, H2O/D2O solvent, and two alternating DNA polymers.
- Similar thermodynamics for poly(dGdC) and poly(dAdT) supported non-sequence-specific binding.
- Binding was endothermic at 25 °C, while extrapolation predicted exothermic binding near the organism's growth temperature.
- Osmotic stress and H2O/D2O substitution had small effects, arguing against a large net hydration change as the sole origin of the heat-capacity change.
Takeaway for interpretation:
- A noncooperative lattice model can be more appropriate than a 1:1 model for nonspecific DNA binding.
- Large negative ΔCp need not imply large net dehydration or high affinity; solvent and structural mechanisms must be compared directly.

## Taq DNA polymerase and Klentaq large fragment with primed-template DNA (thermophilic protein–DNA binding)
id: pr-datta-licata-taq-temperature-series
kind: precedent
basis: literature
status: draft
topics: thermophilic protein–DNA binding; related study sets: protein–DNA thermodynamics
matches: thermophilic protein–DNA binding, literature case study, Taq DNA polymerase and Klentaq large fragment, primed-template DNA, single equilibrium site with temperature series, heat capacity change
cite: datta_licata_taq_2003
legacy_id: case_datta_licata_taq_temperature_series

System: cell: Taq DNA polymerase and Klentaq large fragment; syringe: primed-template DNA.
Conditions: model reported: single equilibrium site with temperature series.
Reported: itc temperature range = 10-60 °C; fluorescence temperature range = 5-70 °C; affinity maximum temperature range = 40-50 °C; ΔCp = -0.7 to -0.8 kcal/(mol K).
Observations:
- Fluorescence anisotropy and ITC were combined to measure equilibrium binding over a broad temperature range.
- The thermophilic proteins retained high DNA affinity at temperatures as low as 5 °C, with maximal affinity around 40-50 °C.
- ΔH and ΔS were strongly temperature dependent; calorimetric and van't Hoff ΔCp estimates agreed.
Takeaway for interpretation:
- Thermophilic origin does not justify assuming weak binding at low temperature.
- When affinity is nonmonotonic with temperature, a local constant-ΔCp approximation should not be extrapolated across the full series.

## adenine-binding purine riboswitch RNA with 2,6-diaminopurine (structured RNA–small molecule binding)
id: pr-gilbert-batey-purine-riboswitch-protocol
kind: precedent
basis: literature
status: draft
topics: structured RNA–small molecule binding; related study sets: RNA binding
matches: structured RNA–small molecule binding, literature case study, adenine-binding purine riboswitch RNA, 2,6-diaminopurine, single ligand binding protocol
cite: gilbert_batey_rna_2009
legacy_id: case_gilbert_batey_purine_riboswitch_protocol

System: cell: adenine-binding purine riboswitch RNA; syringe: 2,6-diaminopurine.
Conditions: model reported: single ligand binding protocol.
Observations:
- The protocol uses adenine-binding RNA titrated with 2,6-diaminopurine as a reproducible practical example.
- The authors emphasize matching RNA and ligand preparation and exploiting ITC's label-free operation across pH, temperature, and ionic conditions.
- The workflow is intended for structured RNA systems where folding state and sample preparation can affect the observed interaction.
Takeaway for interpretation:
- For RNA ITC, sample preparation and folding state are part of the measurement definition, not merely procedural details.
- A clean ligand-binding curve does not by itself establish that the RNA population is uniformly folded or that no coupled folding contributes to ΔH.

## 5′-ATGCTGATGC-3′ oligonucleotide with complementary DNA or RNA strand (nucleic acid hybridization)
id: pr-lang-schwarz-duplex-hybridization
kind: precedent
basis: literature
status: draft
topics: nucleic acid hybridization; related study sets: DNA duplexes and DNA ligands
matches: nucleic acid hybridization, literature case study, 5′-ATGCTGATGC-3′ oligonucleotide, complementary DNA or RNA strand, 1 to 1 duplex binding, heat capacity change
cite: lang_schwarz_hybridization_2007
legacy_id: case_lang_schwarz_duplex_hybridization

System: cell: 5′-ATGCTGATGC-3′ oligonucleotide; syringe: complementary DNA or RNA strand.
Conditions: model reported: 1 to 1 duplex binding.
Reported: ΔG change 20 to 37C = approximately +6 kJ/mol; ΔCp DNA DNA = -1.42 kJ/(mol K); ΔCp DNA RNA = -0.87 kJ/(mol K); ΔG salt effect 25C = approximately -3.5 kJ/mol; ΔG salt effect 37C = approximately -6.0 kJ/mol.
Observations:
- ITC measured DNA/DNA and DNA/RNA duplex formation under varied temperature and sodium chloride concentration.
- The salt dependence was nonlinear in log[NaCl] but became approximately linear with the activity coefficient of water.
- ITC thermodynamics combined with DSC stacking/unstacking terms agreed with free-energy extrapolation from a 56 °C melting transition.
Takeaway for interpretation:
- Nucleic-acid ITC thermodynamics can be strongly temperature- and salt-dependent even for a defined short duplex.
- Comparing DNA/DNA and DNA/RNA requires preserving the same sequence and solution conditions; ΔCp is not transferable between backbones.

## Tn916 integrase DNA-binding domain with 13-bp cognate duplex DNA (sequence specific protein–DNA binding)
id: pr-milev-tn916-integrase-temperature-cp
kind: precedent
basis: literature
status: draft
topics: sequence specific protein–DNA binding; related study sets: protein–DNA thermodynamics
matches: sequence specific protein–DNA binding, literature case study, Tn916 integrase DNA-binding domain, 13-bp cognate duplex DNA, single site with temperature series, heat capacity change
cite: milev_tn916_integrase_2003
legacy_id: case_milev_tn916_integrase_temperature_cp

System: cell: Tn916 integrase DNA-binding domain; syringe: 13-bp cognate duplex DNA.
Conditions: model reported: single site with temperature series.
Reported: ΔCp 4C = -1.4 kJ/(mol K); ΔCp 30C = -2.9 kJ/(mol K); corrected delta Cp = -1.8 kJ/(mol K); structure based delta Cp = -1.2 kJ/(mol K).
Observations:
- ITC was combined with DSC, thermal melting/CD, and fluorescence measurements.
- The apparent ΔCp changed substantially with temperature, but correction for nonparallel heat capacities of free and bound components produced an approximately temperature-independent value.
- The corrected ΔCp remained more negative than a surface-dehydration estimate, which the authors attributed partly to incomplete dehydration of polar groups.
Takeaway for interpretation:
- A temperature-dependent apparent ΔCp can arise from the temperature dependence of component heat capacities and restricted motions, not only from changing interface burial.
- Structure-based solvent-area estimates should be treated as hypotheses, not as constraints that override measured calorimetry.

## E. coli integration host factor with 34-bp H′ DNA (wrapped protein–DNA binding)
id: pr-vandermeulen-ihf-anion-water
kind: precedent
basis: literature
status: draft
topics: wrapped protein–DNA binding; related study sets: protein–DNA thermodynamics
matches: wrapped protein–DNA binding, literature case study, E. coli integration host factor, 34-bp H′ DNA, salt and osmolyte dependence, binding enthalpy
cite: vandermeulen_ihf_water_2008
legacy_id: case_vandermeulen_ihf_anion_water

System: cell: E. coli integration host factor; syringe: 34-bp H′ DNA.
Conditions: model reported: salt and osmolyte dependence.
Reported: ΔH 004M KCl = -20.2 kcal/mol; ΔH salt slope KCl per = 38 M; ΔH salt slope KF or KGlu per = 11 M; K obs KGlu or KF vs KCl at 033M fold = 30; salt derivative KCl = -8.8; salt derivative KF or KGlu = -4.7; estimated hydration water displaced = approximately 1000; estimated net water release from anionic surface = approximately 150.
Observations:
- Both affinity and enthalpy depended strongly on salt concentration and anion identity.
- KCl, KF, and potassium glutamate produced different thermodynamic responses despite the same cation.
- Glycine betaine was used to quantify osmotic effects and separate ionic, Hofmeister, and water-release contributions.
Takeaway for interpretation:
- Salt effects in protein-DNA ITC are not necessarily captured by a single counterion-release slope; anion identity and hydration can matter.
- A strongly favorable enthalpy can coexist with large unfavorable or favorable solvent terms, so mechanistic claims should use the full perturbation series.

## calf-thymus DNA with Al(III) ions (metal DNA binding)
id: pr-aluminum-ctdna-entropy-driven
kind: precedent
basis: literature
status: draft
topics: metal DNA binding; related study sets: DNA duplexes and DNA ligands, metal-ion binding
matches: metal DNA binding, literature case study, calf-thymus DNA, Al(III) ions, pH and temperature series, heat capacity change
cite: aluminum_ctdna_itc_2005
legacy_id: case_aluminum_ctdna_entropy_driven

System: cell: calf-thymus DNA; syringe: Al(III) ions.
Conditions: model reported: pH and temperature series.
Reported: pH range = 3.5-5.5; ΔCp = 1.57 kcal/(mol K).
Observations:
- ITC, fluorescence, and UV spectroscopy showed strong, pH-dependent Al(III) binding to DNA.
- The binding was endothermic but driven by a large favorable entropy increase across the reported pH and temperature conditions.
- The positive heat-capacity change was interpreted as consistent with groove binding and polar-surface burial.
Takeaway for interpretation:
- Endothermic ITC signals can represent strong favorable binding when entropy dominates.
- Metal-ion binding to polyelectrolytes should not be interpreted using neutral-ligand intuition; pH-dependent speciation and electrostatics are central.

## heptameric co-chaperonin protein 10 with same protein for dilution-induced dissociation (protein self association)
id: pr-cpn10-dilution-heptamer
kind: precedent
basis: literature
status: draft
topics: protein self association; related study sets: self-assembly and supramolecular systems
matches: protein self association, literature case study, heptameric co-chaperonin protein 10, same protein for dilution-induced dissociation, heptamer monomer dissociation dilution
cite: cpn10_self_association_2005
legacy_id: case_cpn10_dilution_heptamer

System: cell: heptameric co-chaperonin protein 10; syringe: same protein for dilution-induced dissociation.
Conditions: model reported: heptamer monomer dissociation dilution.
Reported: heptamer monomer midpoint Aacpn10 del25 µM total monomer = 0.51; heptamer monomer midpoint human cpn10 µM total monomer = 3.5; temperature = 25 °C.
Observations:
- ITC dilution experiments recovered complete thermodynamic descriptions of a homo-heptamer self-association equilibrium.
- The midpoint concentrations differed substantially between thermophilic Aacpn10-del25 and human mitochondrial cpn10.
- Association was endothermic and entropy-driven, consistent with release of ordered water and, for the thermophilic protein, monomer relaxation.
Takeaway for interpretation:
- ITC dilution is a valid route for self-association, but it requires an equilibrium model for oligomer dissociation rather than a conventional heterobinding fit.
- An endothermic assembly signal can reflect favorable water-release entropy and does not imply that oligomerization is unfavorable.

## human soluble epoxide hydrolase with natural epoxy-fatty-acid substrates and inhibitors (enzyme kinetics and inhibition)
id: pr-hseh-single-injection-kinetics
kind: precedent
basis: literature
status: draft
topics: enzyme kinetics and inhibition; related study sets: kinetic ITC
matches: enzyme kinetics and inhibition, literature case study, human soluble epoxide hydrolase, natural epoxy-fatty-acid substrates and inhibitors, single injection kinetic ITC
cite: hsEH_single_injection_2019
legacy_id: case_hseh_single_injection_kinetics

System: cell: human soluble epoxide hydrolase; syringe: natural epoxy-fatty-acid substrates and inhibitors.
Conditions: model reported: single injection kinetic ITC.
Observations:
- A single-injection method tracked thermal power while substrate was depleted, avoiding synthetic chromogenic substrates and extensive postreaction processing.
- The method was applied to several physiological epoxy-fatty-acid substrates and to inhibitor characterization.
- The study describes ITC as providing rapid, reproducible kinetic characterization in a system where the heat signal reports catalysis rather than a static binding isotherm.
Takeaway for interpretation:
- A time-dependent ITC trace can be an enzyme-rate experiment rather than an equilibrium binding experiment; fitting it with a binding isotherm is inappropriate.
- Kinetic ITC requires accounting for calorimeter response time, substrate depletion, and reaction-associated control heats.

## lipid vesicles of varying composition and size with melittin (peptide–membrane partitioning)
id: pr-fernandezvidal-melittin-partitioning
kind: precedent
basis: literature
status: draft
topics: peptide–membrane partitioning; related study sets: peptide–membrane interactions
matches: peptide–membrane partitioning, literature case study, lipid vesicles of varying composition and size, melittin, temperature and lipid composition series, heat capacity change
cite: fernandezvidal_melittin_2011
legacy_id: case_fernandezvidal_melittin_partitioning

System: cell: lipid vesicles of varying composition and size; syringe: melittin.
Conditions: model reported: temperature and lipid composition series.
Reported: ΔG POPC 25C = -6.2 kcal/mol; ΔG POPC50 POPG50 25C = -7.5 kcal/mol; ΔCp = -0.5 kcal/(mol K).
Observations:
- ITC and CD showed substantial variation of ΔH and −TΔS with temperature, lipid composition, and vesicle size but only modest variation in ΔG.
- The large negative ΔCp was largely independent of lipid composition and supported an ordinary hydrophobic-effect interpretation of partitioning.
Takeaway for interpretation:
- Membrane partitioning can show strong enthalpy–entropy compensation; a favorable enthalpy alone does not establish a special hydrophobic mechanism.
- ΔCp and composition comparisons can materially change the mechanistic interpretation of a membrane heat signal.

## anionic lipid vesicles with variable POPG fraction with HIV-1 and SIV TAT transduction domains (charged peptide–membrane binding)
id: pr-ziegler-tat-electrostatic-partition
kind: precedent
basis: literature
status: draft
topics: charged peptide–membrane binding; related study sets: peptide–membrane interactions
matches: charged peptide–membrane binding, literature case study, anionic lipid vesicles with variable POPG fraction, HIV-1 and SIV TAT transduction domains, electrostatic attraction chemical partition, heat capacity change, binding enthalpy
cite: ziegler_tat_membrane_2003
legacy_id: case_ziegler_tat_electrostatic_partition

System: cell: anionic lipid vesicles with variable POPG fraction; syringe: HIV-1 and SIV TAT transduction domains.
Conditions: model reported: electrostatic attraction chemical partition.
Reported: ΔH = -1.5 kcal/mol; ΔCp = 0 kcal/(mol K); K app = approximately 10^3-10^4 M^-1; K intrinsic = approximately 1-10 M^-1; electrostatic fraction of binding energy = 0.8; stoichiometry = approximately 1:8.
Observations:
- ITC, monolayer, light-scattering, NMR, and dye-release data supported electrostatic adsorption to intact bilayers under dense-packing conditions.
- The apparent affinity was much larger than the intrinsic partition constant after correcting for surface electrostatics; vesicle aggregation occurred near charge neutralization.
Takeaway for interpretation:
- For charged peptide–membrane systems, apparent affinity can be dominated by surface electrostatics.
- A stoichiometric feature near charge neutralization may reflect vesicle aggregation or charge compensation rather than discrete molecular sites.

## mastoparan-X peptide with POPC/POPG 3:1 vesicles (peptide–membrane pore and micelle formation)
id: pr-henriksen-mastoparan-pore-transition
kind: precedent
basis: literature
status: draft
topics: peptide–membrane pore and micelle formation; related study sets: peptide–membrane interactions
matches: peptide–membrane pore and micelle formation, literature case study, mastoparan-X peptide, POPC/POPG 3:1 vesicles, multistage partition pore micellation
cite: henriksen_mastoparan_2011
legacy_id: case_henriksen_mastoparan_pore_transition

System: cell: mastoparan-X peptide; syringe: POPC/POPG 3:1 vesicles.
Conditions: model reported: multistage partition pore micellation.
Observations:
- ITC resolved peptide/lipid-ratio-dependent transitions that were cross-checked by cryo-TEM and DLS.
- The data distinguished initial partitioning from pore formation and later membrane solubilization into mixed peptide–lipid micelles; a Tzero condition isolated the pore-formation contribution.
Takeaway for interpretation:
- A multistage membrane thermogram should not be forced into one equilibrium binding event when morphology changes across the titration.
- Orthogonal structural measurements help assign late heats to pores, micellation, aggregation, or saturation.

## anionic small and large unilamellar vesicles with magainin 2 amide (antimicrobial peptide–membrane binding)
id: pr-wieprecht-magainin-vesicle-size
kind: precedent
basis: literature
status: draft
topics: antimicrobial peptide–membrane binding; related study sets: peptide–membrane interactions
matches: antimicrobial peptide–membrane binding, literature case study, anionic small and large unilamellar vesicles, magainin 2 amide, surface partition equilibrium, binding enthalpy
cite: wieprecht_magainin_2000
legacy_id: case_wieprecht_magainin_vesicle_size

System: cell: anionic small and large unilamellar vesicles; syringe: magainin 2 amide.
Conditions: model reported: surface partition equilibrium.
Reported: ΔG = -22 kJ/mol; ΔH LUV = -15.1 kJ/mol; ΔS LUV = 24.7 J/(mol K); ΔH SUV = -38.5 kJ/mol; ΔS SUV = -55.3 J/(mol K); temperature = 45 °C.
Observations:
- SUV and LUV binding isotherms were fit with a surface partition model and gave nearly identical ΔG but markedly different ΔH and ΔS.
- The authors linked the compensation to differences in lipid packing and flexibility; translocation to the inner monolayer was fast at 45 °C.
Takeaway for interpretation:
- Vesicle size and curvature can change the enthalpy/entropy decomposition without strongly changing ΔG.
- Translocation timescale is an experimental variable in membrane ITC, not merely a biological afterthought.

## POPC lipid bilayer with sodium dodecyl sulfate (detergent membrane partitioning)
id: pr-moreno-sds-nonideal-partition
kind: precedent
basis: literature
status: draft
topics: detergent membrane partitioning; related study sets: detergents and membrane proteins
matches: detergent membrane partitioning, literature case study, POPC lipid bilayer, sodium dodecyl sulfate, concentration dependent partition with electrostatic correction
cite: moreno_sds_popc_2010
legacy_id: case_moreno_sds_nonideal_partition

System: cell: POPC lipid bilayer; syringe: sodium dodecyl sulfate.
Conditions: model reported: concentration dependent partition with electrostatic correction.
Reported: nonideal local SDS fraction mol = 5 %.
Observations:
- The apparent partition coefficient decreased with total SDS concentration even after electrostatic correction.
- Nonideal behavior became significant above approximately 5 mol% local SDS, where the bilayer itself was strongly perturbed.
Takeaway for interpretation:
- A concentration-dependent apparent partition coefficient may indicate ligand-induced membrane remodeling rather than multiple independent affinity classes.
- Intrinsic partition parameters should be separated from high-coverage, perturbed-membrane behavior.

## DOPC/DOPG unilamellar vesicles with penetratin (cell penetrating peptide–membrane translocation)
id: pr-binder-penetratin-charge-threshold
kind: precedent
basis: literature
status: draft
topics: cell penetrating peptide–membrane translocation; related study sets: peptide–membrane interactions
matches: cell penetrating peptide–membrane translocation, literature case study, DOPC/DOPG unilamellar vesicles, penetratin, surface partition with permeabilization threshold
cite: binder_penetratin_2003
legacy_id: case_binder_penetratin_charge_threshold

System: cell: DOPC/DOPG unilamellar vesicles; syringe: penetratin.
Conditions: model reported: surface partition with permeabilization threshold.
Reported: effective peptide charge = 5.1; PG fraction threshold = 0.5; bound peptide to lipid at permeabilization = approximately 1:20.
Observations:
- ITC described initial outer-surface binding followed by inner-monolayer binding after a charge- and coverage-dependent permeability transition.
- The proposed mechanism involved asymmetric peptide distribution, a transmembrane electrical field, and membrane stress.
Takeaway for interpretation:
- A threshold in a membrane ITC series may mark translocation or permeabilization rather than single-site saturation.
- Effective charge from a surface model is not necessarily the formal molecular charge.

## family-10 xylanases with xylosaccharides of different lengths (enzyme carbohydrate subsite binding)
id: pr-xylanase-subsite-mapping
kind: precedent
basis: literature
status: draft
topics: enzyme carbohydrate subsite binding; related study sets: carbohydrate recognition
matches: enzyme carbohydrate subsite binding, literature case study, family-10 xylanases, xylosaccharides of different lengths, temperature series and mutational validation, heat capacity change
cite: lyx_xylanase_subsites_2004
legacy_id: case_xylanase_subsite_mapping

System: cell: family-10 xylanases; syringe: xylosaccharides of different lengths.
Conditions: model reported: temperature series and mutational validation.
Observations:
- ITC measured binding thermodynamics and ΔCp for xylosaccharides differing by one sugar unit and used temperature dependence to map subsite preferences.
- The structural assignment was supported by aromatic-residue mutants and tryptophan fluorescence.
Takeaway for interpretation:
- ΔCp can aid structural subsite assignment when paired with mutational and orthogonal evidence.
- Carbohydrate binding involves several interaction types; enthalpy should not be equated with one mechanism without validation.

## tetrameric Momordica charantia lectin with mono- and disaccharides (lectin carbohydrate binding)
id: pr-sultan-mcl-saccharide-series
kind: precedent
basis: literature
status: draft
topics: lectin carbohydrate binding; related study sets: carbohydrate recognition
matches: lectin carbohydrate binding, literature case study, tetrameric Momordica charantia lectin, mono- and disaccharides, saccharide series with temperature and pH controls, heat capacity change, binding enthalpy
cite: sultan_mcl_2005
legacy_id: case_sultan_mcl_saccharide_series

System: cell: tetrameric Momordica charantia lectin; syringe: mono- and disaccharides.
Conditions: model reported: saccharide series with temperature and pH controls.
Reported: sites per tetramer = 2; Kb range = 7.3×10^3 to 1.52×10^4 M^-1; ΔH range = -50.99 to -43.39 kJ/mol; temperature = 288 K; pH activity range = 7.4-11.0.
Observations:
- The tetramer bound two sugar molecules and showed different ΔCp magnitudes for disaccharides versus monosaccharides.
- Binding was enthalpy-driven with unfavorable entropy and clear enthalpy–entropy compensation; CD and fluorescence indicated little global structural rearrangement.
Takeaway for interpretation:
- A multivalent oligomer can show an apparently simple sugar-binding stoichiometry that is not equal to the number of protein subunits.
- Small ΔCp differences across ligand size can support surface-area trends but should not be treated as proof of a unique binding geometry.

## homotetrameric artocarpin lectin with mannose-containing saccharides (lectin oligosaccharide binding)
id: pr-rani-artocarpin-mannotriose-extended-contact
kind: precedent
basis: literature
status: draft
topics: lectin oligosaccharide binding; related study sets: carbohydrate recognition
matches: lectin oligosaccharide binding, literature case study, homotetrameric artocarpin lectin, mannose-containing saccharides, temperature series and saccharide comparison, binding enthalpy
cite: rani_artocarpin_1999
legacy_id: case_rani_artocarpin_mannotriose_extended_contact

System: cell: homotetrameric artocarpin lectin; syringe: mannose-containing saccharides.
Conditions: model reported: temperature series and saccharide comparison.
Reported: temperature = 280 and 293 K; ΔH range = -10.94 to -47.11 kJ/mol; mannotriose vs mannose affinity fold = 7.
Observations:
- ITC showed enthalpy-driven binding with enthalpy–entropy compensation across a saccharide panel.
- Mannotriose bound more strongly than shorter or differently linked analogues, and its interaction heat indicated simultaneous engagement of all three mannopyranosyl residues rather than only the nonreducing end.
Takeaway for interpretation:
- Oligosaccharide length and linkage can change the contact architecture, not merely add independent sugar contacts.
- A one-site-per-subunit structural annotation does not guarantee that every ligand uses only one monosaccharide contact.

## POPC vesicles in phosphate/NaCl buffer with SDS (charged detergent membrane solubilization)
id: pr-keller-sds-temperature-solubilization
kind: precedent
basis: literature
status: draft
topics: charged detergent membrane solubilization; related study sets: detergents and membrane proteins
matches: charged detergent membrane solubilization, literature case study, POPC vesicles in phosphate/NaCl buffer, SDS, partition phase diagram with light scattering
cite: keller_sds_popc_2006
legacy_id: case_keller_sds_temperature_solubilization

System: cell: POPC vesicles in phosphate/NaCl buffer; syringe: SDS.
Conditions: model reported: partition phase diagram with light scattering.
Reported: temperature C slow translocation = 25; temperature C fast translocation = 65; buffer = 10 mM phosphate, 154 mM NaCl, pH 7.4.
Observations:
- ITC and right-angle light scattering characterized membrane partitioning, vesicle solubilization/reconstitution, micelle formation, and phase ranges.
- Transbilayer migration and dissolution were slow at 25 °C but much faster at 65 °C; at the higher temperature a partition/equilibrium model with Gouy–Chapman electrostatics described the phases quantitatively.
Takeaway for interpretation:
- Kinetic equilibration can determine whether a membrane ITC experiment reflects thermodynamic phase behavior or history-dependent half-sided binding.
- Charged-detergent solubilization requires combining partitioning with electrostatic surface effects and phase coexistence.

## PPARγ protein with protein ligand (itc instrumental artifact and unfolding)
id: pr-maruno-stirring-aggregation-artifact
kind: precedent
basis: literature
status: draft
topics: itc instrumental artifact and unfolding; related study sets: ITC methodology
matches: itc instrumental artifact and unfolding, literature case study, PPARγ protein, protein ligand, paddle shape and stirring rate comparison
cite: maruno_stirring_itc_2020
legacy_id: case_maruno_stirring_aggregation_artifact

System: cell: PPARγ protein; syringe: protein ligand.
Conditions: model reported: paddle shape and stirring rate comparison.
Reported: complete reaction rpm = 500 or 750 with small-pitched corkscrew; soluble fraction at 1500rpm = 94 %; unfolded fraction at 1500rpm = 6 %.
Observations:
- Paddle geometry and stirring rate altered the apparent reaction heat and fitted thermodynamic parameters.
- Only the small-pitched corkscrew at 500 or 750 rpm produced complete reaction without incompetent fractions; 1500 rpm increased micron aggregates and measurable unfolding.
Takeaway for interpretation:
- Stirring is a measurement variable: excessive mixing can create aggregation/unfolding heats that masquerade as binding heterogeneity or altered ΔH.
- A robust ITC protocol should document paddle geometry and rpm, especially when comparing instruments or laboratories.

## prolyl oligopeptidase with substrate with reversible covalent and noncovalent inhibitors (inhibitor association dissociation kinetics)
id: pr-ditrani-pop-inhibitor-kinetics
kind: precedent
basis: literature
status: draft
topics: inhibitor association dissociation kinetics; related study sets: kinetic ITC
matches: inhibitor association dissociation kinetics, literature case study, prolyl oligopeptidase with substrate, reversible covalent and noncovalent inhibitors, kinetic inhibition and initiation ITC
cite: ditrani_inhibitor_kinetics_2018
legacy_id: case_ditrani_pop_inhibitor_kinetics

System: cell: prolyl oligopeptidase with substrate; syringe: reversible covalent and noncovalent inhibitors.
Conditions: model reported: kinetic inhibition and initiation ITC.
Reported: kinetic span orders of magnitude = 3; subnanomolar affinities measured: yes; time resolution = sufficient to distinguish monophasic and biphasic inhibition.
Observations:
- Complementary ITC experiments measured inhibitor association and dissociation through changes in catalytic heat flow.
- The approach resolved rapid kinetics and sub-nanomolar affinities, and could distinguish an initial noncovalent complex followed by slower covalent bond formation from one-step-like behavior.
Takeaway for interpretation:
- A high-affinity inhibitor can be characterized kinetically by ITC even when a conventional equilibrium isotherm would be too steep.
- Reversible covalent inhibition may produce biphasic heat-flow kinetics; a single exponential or equilibrium fit can obscure the mechanism.

## four variable-region-identical monoclonal antibodies with 12-mer peptide mimetic of Cryptococcus neoformans polysaccharide (antibody peptide binding)
id: pr-dam-antibody-constant-region-modulation
kind: precedent
basis: literature
status: draft
topics: antibody peptide binding; related study sets: ligand series and mutants
matches: antibody peptide binding, literature case study, four variable-region-identical monoclonal antibodies, 12-mer peptide mimetic of Cryptococcus neoformans polysaccharide, univalent ligand comparison across isotypes
cite: dam_antibody_constant_region_2008
legacy_id: case_dam_antibody_constant_region_modulation

System: cell: four variable-region-identical monoclonal antibodies; syringe: 12-mer peptide mimetic of Cryptococcus neoformans polysaccharide.
Conditions: model reported: univalent ligand comparison across isotypes.
Reported: stoichiometry range = 1.9-2.0; isotypes = IgG1, IgG2a, IgG2b, IgG3.
Observations:
- Antibodies with identical variable regions but different constant regions showed significant differences in ITC thermodynamic parameters.
- Binding was entropy-dominated, and biotinylation of the peptide increased the interaction enthalpy relative to native peptide.
- The result provided evidence that the constant region can modulate antigen binding beyond a simple avidity explanation.
Takeaway for interpretation:
- Identical nominal binding-site sequences do not guarantee identical thermodynamics when the surrounding scaffold or oligomeric context changes.
- A ligand label can change the species or complex geometry being measured; labeled and unlabeled ITC values are not automatically interchangeable.

## Entamoeba histolytica calcium-binding protein with Ca2+ or Mg2+ (metal–protein multisite cooperativity)
id: pr-sundaralingam-calcium-protein-multisite
kind: precedent
basis: literature
status: draft
topics: metal–protein multisite cooperativity; related study sets: metal-ion binding
matches: metal–protein multisite cooperativity, literature case study, Entamoeba histolytica calcium-binding protein, Ca2+ or Mg2+, four Ca sites competitive Mg binding
cite: sundaralingam_calcium_binding_1997
legacy_id: case_sundaralingam_calcium_protein_multisite

System: cell: Entamoeba histolytica calcium-binding protein; syringe: Ca2+ or Mg2+.
Conditions: model reported: four Ca sites competitive Mg binding.
Reported: calcium sites = 4; buffer = 20 mM MOPS pH 7.0; temperature = 20 °C; KCl range = 0.25-1.
Observations:
- Ca2+ titration resolved four sites: two low-affinity exothermic sites and two high-affinity sites with opposite enthalpy signs; both pairs showed positive cooperativity.
- Mg2+ alone appeared single-site and endothermic, while Mg2+ presaturation changed the high-affinity Ca-site interaction to negative cooperativity.
- Competitive binding assays, intrinsic fluorescence, peptide cleavage, and denaturant titrations were used to validate site assignments and coupling.
Takeaway for interpretation:
- A global multisite fit can conceal sign changes and cooperative coupling between individual metal-binding sites.
- The same ligand can change the apparent cooperativity of another ligand depending on preloading state and competition.

## Zn7 metallothionein-3 and isolated α/β domains with Cu+ or chelator for Zn2+ displacement (metal thiolate cluster thermodynamics)
id: pr-mehlenbacher-mt3-cu-zn-cluster-switch
kind: precedent
basis: literature
status: draft
topics: metal thiolate cluster thermodynamics; related study sets: metal-ion binding
matches: metal thiolate cluster thermodynamics, literature case study, Zn7 metallothionein-3 and isolated α/β domains, Cu+ or chelator for Zn2+ displacement, buffer series and competitive displacement
cite: mehlenbacher_mt3_cu_zn_2022
legacy_id: case_mehlenbacher_mt3_cu_zn_cluster_switch

System: cell: Zn7 metallothionein-3 and isolated α/β domains; syringe: Cu+ or chelator for Zn2+ displacement.
Conditions: model reported: buffer series and competitive displacement.
Reported: pH = 7.4; zinc sites = 7; copper cluster count = 2; cluster size = Cu4+.
Observations:
- ITC quantified Zn2+ binding by chelation and Cu+ binding by Zn2+ displacement with glutathione competition, using multiple buffers to obtain condition-independent thermodynamics.
- The seven Zn2+ ions formed two thermodynamically distinct populations, while Cu+ displacement assembled two Cu4+-thiolate clusters.
- Cu+ binding was enthalpically favored relative to the entropically favored Zn2+ binding and similar thermodynamics across MT isoforms implicated conserved cysteine ligation.
Takeaway for interpretation:
- Competitive displacement ITC can quantify metal binding that is too strong or chemically unstable for direct titration, but requires explicit treatment of chelator and buffer equilibria.
- Sequential or clustered metal binding should not be reduced to one average K and ΔH when populations have distinct coordination thermodynamics.

## 9- and 10-bp DNA duplex-forming oligonucleotides; modeled protein-ligand examples with complementary strands or ligand (solvation aware itc modeling)
id: pr-harmon-water-solvation-model-itc
kind: precedent
basis: literature
status: draft
topics: solvation aware itc modeling; related study sets: ITC methodology
matches: solvation aware itc modeling, literature case study, complementary strands or ligand, classical equilibrium vs bulk water extended model
cite: harmon_solvation_itc_2024
legacy_id: case_harmon_water_solvation_model_itc

System: cell: 9- and 10-bp DNA duplex-forming oligonucleotides; modeled protein-ligand examples; syringe: complementary strands or ligand.
Conditions: model reported: classical equilibrium vs bulk water extended model.
Reported: temperature = 25 °C.
Observations:
- The authors measured concentration-dependent ITC for two short DNA duplexes and modeled an additional bulk-water solvation term in the governing equilibrium.
- The extended model showed that a classical equilibrium equation can fit the data well while obscuring a large unfavorable solvation contribution.
- Temperature analysis was complicated by changing intramolecular base stacking in the single strands.
Takeaway for interpretation:
- A visually good classical ITC fit does not prove that the assumed thermodynamic state variables are complete.
- For nucleic acids and other strongly solvated systems, concentration-dependent solvation and intramolecular pre-equilibria can bias mechanistic interpretation even when K and residuals look plausible.

## trypsin and tRNA-guanine transglycosylase with high-affinity ligands of varying purity (itc sample purity artifact)
id: pr-gruner-ligand-impurity-enthalpy
kind: precedent
basis: literature
status: draft
topics: itc sample purity artifact; related study sets: ITC methodology
matches: itc sample purity artifact, literature case study, trypsin and tRNA-guanine transglycosylase, high-affinity ligands of varying purity, purity and concentration comparison
cite: gruner_itc_impurities_2014
legacy_id: case_gruner_ligand_impurity_enthalpy

System: cell: trypsin and tRNA-guanine transglycosylase; syringe: high-affinity ligands of varying purity.
Conditions: model reported: purity and concentration comparison.
Reported: residual enthalpy difference after correction = 4 kJ/mol.
Observations:
- Protein concentration errors had little effect in the tested systems, whereas ligand impurities caused pronounced changes in apparent enthalpy.
- Even after correcting ligand concentration under a nonbinding-impurity assumption, differences of about 4 kJ/mol remained relative to pure-ligand controls.
- Manually changing fitting parameters to obtain an expected stoichiometry produced inaccurate thermodynamic signatures.
Takeaway for interpretation:
- Unexpected n should trigger analytical purity and concentration checks before manual parameter adjustment.
- Ligand purity can be more consequential than protein purity for enthalpy in high-affinity assays.

## family-6 carbohydrate-binding module of Clostridium thermocellum XynA with xylooligosaccharides DP 2-8 (carbohydrate binding module ligand series)
id: pr-sakka-cbm-dp-site-length
kind: precedent
basis: literature
status: draft
topics: carbohydrate binding module ligand series; related study sets: carbohydrate recognition
matches: carbohydrate binding module ligand series, literature case study, family-6 carbohydrate-binding module of Clostridium thermocellum XynA, xylooligosaccharides DP 2-8, DP dependent binding series
cite: sakka_cbm_xyna_2003
legacy_id: case_sakka_cbm_dp_site_length

System: cell: family-6 carbohydrate-binding module of Clostridium thermocellum XynA; syringe: xylooligosaccharides DP 2-8.
Conditions: model reported: DP dependent binding series.
Reported: Ka DP2 at 20 °C = 5000 M^-1; Ka DP5 to 8 at 20 °C = approximately 5×10^5 M^-1; Ka 60C relative to 20C = approximately 0.1; estimated site length xylose units = 5.
Observations:
- The association constant increased from DP2 to a plateau around DP5-8 at 20 °C.
- Binding was enthalpy-driven and roughly tenfold weaker at 60 °C than at 20 °C.
- The DP dependence supported an approximately five-xylose-unit binding site.
Takeaway for interpretation:
- A ligand-length series can reveal an extended binding site even when individual isotherms look one-site-like.
- Temperature can alter affinity substantially without changing the inferred physical site length.

## human aldose reductase wild type and single-site mutants with two closely related inhibitors (mutational protein–ligand thermodynamics)
id: pr-koch-aldose-reductase-mutation-signature
kind: precedent
basis: literature
status: draft
topics: mutational protein–ligand thermodynamics; related study sets: ligand series and mutants
matches: mutational protein–ligand thermodynamics, literature case study, human aldose reductase wild type and single-site mutants, two closely related inhibitors, ITC plus crystallography mutant series
cite: koch_aldose_reductase_mutants_2011
legacy_id: case_koch_aldose_reductase_mutation_signature

System: cell: human aldose reductase wild type and single-site mutants; syringe: two closely related inhibitors.
Conditions: model reported: ITC plus crystallography mutant series.
Observations:
- ITC and high-resolution X-ray structures were used to compare inhibitor binding across single-site mutants.
- The gross binding mode was conserved, but small geometric adaptations followed spatial and electronic changes at mutation sites.
- Mutations altered thermodynamic signatures even when the overall structural pose remained similar.
Takeaway for interpretation:
- Similar poses do not imply similar thermodynamics; small geometry or water-network changes can shift ΔH and ΔS.
- Mutation-series ITC is strongest when interpreted jointly with structural evidence.

## glutamine-II riboswitch ligand-binding domain with L-glutamine (riboswitch ligand binding and mutation)
id: pr-ren-glutamine-riboswitch-metal-and-mutation
kind: precedent
basis: literature
status: draft
topics: riboswitch ligand binding and mutation; related study sets: RNA binding
matches: riboswitch ligand binding and mutation, literature case study, glutamine-II riboswitch ligand-binding domain, L-glutamine, structure guided mutational ITC
cite: ren_glutamine_riboswitch_2019
legacy_id: case_ren_glutamine_riboswitch_metal_and_mutation

System: cell: glutamine-II riboswitch ligand-binding domain; syringe: L-glutamine.
Conditions: model reported: structure guided mutational ITC.
Observations:
- X-ray structure, atomic mutagenesis, and ITC showed that mutations at the RNA ligand-binding site eliminate or weaken binding.
- A metal ion directly coordinated the glutamine carboxylate and interacted with RNA through inner-sphere water molecules in a pseudoknot/partial-triplex architecture.
Takeaway for interpretation:
- Loss of a riboswitch ITC signal after mutation can indicate loss of a binding-competent folded state, not only loss of a direct ligand contact.
- Metal-mediated contacts should be included when structural data identify coordinated ions.

## five designed DNA duplex systems with complementary DNA strands (DNA duplex formation heat capacity)
id: pr-mikulecky-dna-cp-single-strand-stacking
kind: precedent
basis: literature
status: draft
topics: DNA duplex formation heat capacity; related study sets: DNA duplexes and DNA ligands
matches: DNA duplex formation heat capacity, literature case study, five designed DNA duplex systems, complementary DNA strands, ITC DSC CD optical melting global interpretation, heat capacity change
cite: mikulecky_dna_cp_2006
legacy_id: case_mikulecky_dna_cp_single_strand_stacking

System: cell: five designed DNA duplex systems; syringe: complementary DNA strands.
Conditions: model reported: ITC DSC CD optical melting global interpretation.
Reported: ITC temperature range = 15-45 °C; NaCl range = 0.1-1.0; per base pair delta Cp range at 1M = 60-120 cal/(mol K).
Observations:
- Despite identical GC content and similar duplex stability, designed duplexes showed roughly twofold variation in per-base-pair ΔCp at high salt.
- ITC-observed ΔCp values were accounted for by progressive melting of residual single-strand stacking, supported by DSC, CD, and optical melting.
Takeaway for interpretation:
- Observed ΔCp for nucleic-acid association can report coupled strand pre-equilibria rather than only the final interface.
- Constant per-base-pair ΔCp approximations can fail across sequences and ionic strengths.

## 16S rRNA A-site model oligonucleotide with neomycin, paromomycin, and ribostamycin (aminoglycoside RNA binding)
id: pr-kaul-aminoglycoside-rrna-proton-linkage
kind: precedent
basis: literature
status: draft
topics: aminoglycoside RNA binding; related study sets: RNA binding
matches: aminoglycoside RNA binding, literature case study, 16S rRNA A-site model oligonucleotide, neomycin, paromomycin, and ribostamycin, buffer pH salt temperature series, heat capacity change
cite: kaul_aminoglycoside_rRNA_2002
legacy_id: case_kaul_aminoglycoside_rRNA_proton_linkage

System: cell: 16S rRNA A-site model oligonucleotide; syringe: neomycin, paromomycin, and ribostamycin.
Conditions: model reported: buffer pH salt temperature series.
Reported: affinity rank = neomycin > paromomycin > ribostamycin; minimum drug NH3 groups implicated = 3.
Observations:
- Binding became more enthalpically favorable with increasing pH while entropy became less favorable, consistent with binding-linked drug protonation.
- Buffer-dependent ITC quantified pH-dependent proton uptake; affinity decreased with increasing pH and Na+ concentration.
- Negative ΔCp increased in magnitude with pH, and neomycin's affinity advantage was primarily enthalpic.
Takeaway for interpretation:
- pH-dependent ΔH and affinity in RNA–aminoglycoside systems can reflect ligand protonation and counterion release simultaneously.
- A higher-affinity analogue need not be more favorable in every thermodynamic component.

## Hsp90αN or human carbonic anhydrase II with radicicol or ethoxzolamide (tight binding itc and thermal shift)
id: pr-zubriene-tight-binding-displacement-tsa
kind: precedent
basis: literature
status: draft
topics: tight binding itc and thermal shift; related study sets: ITC detection limits
matches: tight binding itc and thermal shift, literature case study, Hsp90αN or human carbonic anhydrase II, radicicol or ethoxzolamide, displacement ITC plus thermal shift
cite: zubriene_nanomolar_tsa_itc_2009
legacy_id: case_zubriene_tight_binding_displacement_tsa

System: cell: Hsp90αN or human carbonic anhydrase II; syringe: radicicol or ethoxzolamide.
Conditions: model reported: displacement ITC plus thermal shift.
Reported: Kd Hsp90 radicicol = 1 nM; Kd CAII ethoxzolamide = 2 nM; ligand induced Tm increase = >10 °C.
Observations:
- Direct ITC was too steep for accurate measurement; displacement ITC and ligand-dependent thermal transitions were used instead.
- Ligand-free and ligand-bound protein fractions melted separately, producing two transitions when ligand concentration approached half the protein concentration.
- Thermal-shift concentration dependence yielded nanomolar dissociation constants consistent with the displacement strategy.
Takeaway for interpretation:
- A converged direct-fit Kd in the single-digit nanomolar regime is not automatically credible.
- Two melting transitions can reflect mixed ligand occupancy rather than two protein unfolding states.

## two complementary RNA hairpins with partner hairpin (RNA kissing interaction and strand displacement)
id: pr-salim-rna-kissing-duplex-resolution
kind: precedent
basis: literature
status: draft
topics: RNA kissing interaction and strand displacement; related study sets: RNA binding
matches: RNA kissing interaction and strand displacement, literature case study, two complementary RNA hairpins, partner hairpin, ITC SPR smFRET kinetic thermodynamic comparison
cite: salim_rna_kissing_2012
legacy_id: case_salim_rna_kissing_duplex_resolution

System: cell: two complementary RNA hairpins; syringe: partner hairpin.
Conditions: model reported: ITC SPR smFRET kinetic thermodynamic comparison.
Observations:
- ITC, SPR, and single-molecule fluorescence characterized kissing-complex formation and subsequent resolution into an extended duplex.
- Stable and labile kissing complexes differed primarily in dissociation behavior; stable complexes persisted longer even when association rates were similar.
- A stable kissing complex was not always required for strand displacement in the model system.
Takeaway for interpretation:
- Intermediate stability may be controlled more by koff than kon; equilibrium affinity alone does not specify pathway behavior.
- A transient ITC intermediate can still be functionally important if it gates a structural rearrangement.

## GMPPNP-loaded human Rac1b (residues 1-196) with human POSH intrinsically disordered region (residues 315-380) (part 1 of 2)
id: pr-rac1b-posh-partial-folding-2026
kind: precedent
basis: literature
status: draft
topics: alternative splicing modulates IDP folding upon binding; related study sets: disordered-region binding, ITC detection limits
matches: alternative splicing modulates IDP folding upon binding, literature case study, GMPPNP-loaded human Rac1b (residues 1-196), human POSH intrinsically disordered region (residues 315-380), one set of sites ITC with temperature series, heat capacity change
cite: kjaer_rac1b_posh_2026
legacy_id: case_rac1b_posh_partial_folding_2026

System: cell: GMPPNP-loaded human Rac1b (residues 1-196); syringe: human POSH intrinsically disordered region (residues 315-380).
Conditions: model reported: one set of sites ITC with temperature series; T = 308.15 K (35.0 °C).
Reported: Kd = 265 µM; Kd reference Rac1 = 22 µM; fold Kd increase vs Rac1 = 12; ΔCp = -1200 J/(mol K); ΔCp reference Rac1 = -2800 J/(mol K); replicates = 2; buffer = 50 mM HEPES pH 6.9, 150 mM NaCl, 5 mM MgCl2, 2 mM TCEP.
Observations:
- At 35 °C, POSH(315-380) bound Rac1b with Kd = 265 µM, approximately 12-fold weaker than the reported Rac1-POSH complex (Kd = 22 µM at the same temperature).
- The fitted binding enthalpy approached zero near 25 °C, producing a weak ITC signal at intermediate temperatures; the authors therefore used measurements at 5 and 35 °C to estimate ΔCp.
- ΔCp was -1.2 kJ/(mol K), less than half the previously reported Rac1-POSH value of -2.8 kJ/(mol K).
- ITC-derived affinity was used to constrain the bound-state population in independent NMR CEST analysis, which resolved a folding intermediate and showed slower association and faster dissociation than for Rac1.

## GMPPNP-loaded human Rac1b (residues 1-196) with human POSH intrinsically disordered region (residues 315-380) (part 2 of 2)
id: pr-rac1b-posh-partial-folding-2026-2
kind: precedent
basis: literature
status: draft
topics: alternative splicing modulates IDP folding upon binding; related study sets: disordered-region binding, ITC detection limits
matches: alternative splicing modulates IDP folding upon binding, literature case study, GMPPNP-loaded human Rac1b (residues 1-196), human POSH intrinsically disordered region (residues 315-380), one set of sites ITC with temperature series, heat capacity change
cite: kjaer_rac1b_posh_2026
legacy_id: case_rac1b_posh_partial_folding_2026

Authors' interpretation:
- The reduced magnitude of ΔCp indicates substantially less hydrophobic surface burial during POSH binding to Rac1b.
- Rac1b's 19-residue splice insertion truncates the POSH folding-upon-binding trajectory: MRE1 folds on Rac1b whereas MRE2 remains dynamic.
- Enhanced Rac1b dynamics impose entropic and kinetic penalties that destabilize the effector-bound state.
Takeaway for interpretation:
- A near-zero binding enthalpy can make a mid-temperature ITC isotherm uninformative even when the interaction is measurable at other temperatures; report the temperature dependence rather than treating the weak signal as absence of binding.
- For coupled folding and binding, a smaller-magnitude negative ΔCp supports reduced burial or ordering but does not by itself assign which structural element failed to fold; orthogonal structural or spectroscopic evidence is needed.
- When a splice variant changes both Kd and kinetic rates, affinity loss should not be simplified to a static interface defect: altered conformational sampling and a truncated folding pathway can be causal.
Flags: near zero binding enthalpy.

## human 14-3-3eta with hDMX(335-349) phospho-Ser342 peptide (phosphopeptide 14-3-3 binding)
id: pr-hdmx-ps342-1433eta-2022
kind: precedent
basis: literature
status: draft
topics: phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: phosphopeptide 14-3-3 binding, literature case study, human 14-3-3eta, hDMX(335-349) phospho-Ser342 peptide, one set of sites, binding enthalpy
cite: srdanovic_hdmx_hdm2_1433_2022
legacy_id: case_hdmx_pS342_1433eta_2022

System: cell: human 14-3-3eta; syringe: hDMX(335-349) phospho-Ser342 peptide.
Conditions: model reported: one set of sites; T = 298.15 K (25.0 °C).
Reported: Kd = 21.0 ± 3.0 µM; ΔG = -26.7 kJ/mol; ΔH = -21.4 ± 1.0 kJ/mol; −TΔS = -5.3 kJ/mol; stoichiometry = 0.72.
Observations: The singly phosphorylated hDMX peptide bound 14-3-3eta with micromolar affinity in ITC.
Takeaway for interpretation: A single phosphosite can give a measurable, enthalpically favorable 14-3-3 interaction without requiring a bivalent model.

## human 14-3-3eta with hDMX(361-374) phospho-Ser367 peptide (phosphopeptide 14-3-3 binding)
id: pr-hdmx-ps367-1433eta-2022
kind: precedent
basis: literature
status: draft
topics: phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: phosphopeptide 14-3-3 binding, literature case study, human 14-3-3eta, hDMX(361-374) phospho-Ser367 peptide, one set of sites, binding enthalpy
cite: srdanovic_hdmx_hdm2_1433_2022
legacy_id: case_hdmx_pS367_1433eta_2022

System: cell: human 14-3-3eta; syringe: hDMX(361-374) phospho-Ser367 peptide.
Conditions: model reported: one set of sites; T = 298.15 K (25.0 °C).
Reported: Kd = 870 ± 95 nM; ΔG = -34.6 kJ/mol; ΔH = -16.10 ± 0.20 kJ/mol; −TΔS = -18.5 kJ/mol; stoichiometry = 1.28.
Comparison with human 14-3-3eta with hDMX(335-349) phospho-Ser342 peptide (phosphopeptide 14-3-3 binding): affinity change = stronger; fold Kd decrease = 24.1.
Takeaway for interpretation: Phosphosite identity and local sequence context can change 14-3-3 affinity by more than an order of magnitude while preserving favorable enthalpy.

## human 14-3-3eta with hDMX(335-373) phospho-Ser342/phospho-Ser367 peptide (multisite phosphopeptide 14-3-3 binding)
id: pr-hdmx-dual-phospho-1433eta-2022
kind: precedent
basis: literature
status: draft
topics: multisite phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: multisite phosphopeptide 14-3-3 binding, literature case study, human 14-3-3eta, hDMX(335-373) phospho-Ser342/phospho-Ser367 peptide, one set of sites, binding enthalpy
cite: srdanovic_hdmx_hdm2_1433_2022
legacy_id: case_hdmx_dual_phospho_1433eta_2022

System: cell: human 14-3-3eta; syringe: hDMX(335-373) phospho-Ser342/phospho-Ser367 peptide.
Conditions: model reported: one set of sites; T = 298.15 K (25.0 °C).
Reported: Kd = 14 ± 10 nM; ΔG = -44.7 kJ/mol; ΔH = -22.10 ± 0.70 kJ/mol; −TΔS = -22.6 kJ/mol; stoichiometry = 0.94.
Authors' interpretation: Proximal phosphosites cooperate to facilitate high-affinity 14-3-3 binding.
Comparison with human 14-3-3eta with hDMX(361-374) phospho-Ser367 peptide (phosphopeptide 14-3-3 binding): affinity change = stronger; fold Kd decrease = 62.1.
Takeaway for interpretation: A low fitted stoichiometry is not, on its own, proof that both phosphosites bridge the dimer; structural and orthogonal binding data are needed to establish geometry.

## human 14-3-3eta with hDM2(160-192) phospho-Ser166/phospho-Ser186 peptide (multisite phosphopeptide 14-3-3 binding)
id: pr-hdm2-dual-phospho-1433eta-2022
kind: precedent
basis: literature
status: draft
topics: multisite phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: multisite phosphopeptide 14-3-3 binding, literature case study, human 14-3-3eta, hDM2(160-192) phospho-Ser166/phospho-Ser186 peptide, one set of sites, binding enthalpy
cite: srdanovic_hdmx_hdm2_1433_2022
legacy_id: case_hdm2_dual_phospho_1433eta_2022

System: cell: human 14-3-3eta; syringe: hDM2(160-192) phospho-Ser166/phospho-Ser186 peptide.
Conditions: model reported: one set of sites; T = 298.15 K (25.0 °C).
Reported: Kd = 151 ± 57 nM; ΔG = -38.9 kJ/mol; ΔH = -12.30 ± 0.60 kJ/mol; −TΔS = -26.6 kJ/mol; stoichiometry = 0.94.
Authors' interpretation: The two proximal hDM2 phosphosites cooperate to support high-affinity 14-3-3 recognition.
Takeaway for interpretation: Comparison of related multisite clients can separate affinity changes driven mainly by enthalpy from those driven mainly by entropy.

## human 14-3-3sigma with ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp) (C terminal phosphopeptide 14-3-3 binding)
id: pr-era-ctp-1433sigma-binary-2020
kind: precedent
basis: literature
status: draft
topics: C terminal phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: C terminal phosphopeptide 14-3-3 binding, literature case study, human 14-3-3sigma, ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp), one set of sites ITC
cite: sengupta_fusicoccin_1433_2020
legacy_id: case_era_ctp_1433sigma_binary_2020

System: cell: human 14-3-3sigma; syringe: ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp).
Conditions: model reported: one set of sites ITC.
Reported: Kd = 750 ± 140 nM.
Observations: ITC measured the binary ERalpha-ctp/14-3-3sigma affinity; fluorescence polarization gave a comparable apparent affinity for a labeled peptide.
Takeaway for interpretation: Use label-free ITC as a reference when interpreting affinities from labeled phosphopeptide assays.

## human 14-3-3sigma with 200 µM fusicoccin A with ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp)
id: pr-era-ctp-1433sigma-fusicoccin-2020
kind: precedent
basis: literature
status: draft
topics: small molecule stabilized 14-3-3 phosphopeptide complex; related study sets: 14-3-3 phosphopeptide complexes
matches: small molecule stabilized 14-3-3 phosphopeptide complex, literature case study, human 14-3-3sigma with 200 µM fusicoccin A, ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp), one set of sites ITC
cite: sengupta_fusicoccin_1433_2020
legacy_id: case_era_ctp_1433sigma_fusicoccin_2020

System: cell: human 14-3-3sigma with 200 µM fusicoccin A; syringe: ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp).
Conditions: model reported: one set of sites ITC.
Reported: Kd = 20 ± 50 nM; stabilizer concentration = 200 µM.
Authors' interpretation: Fusicoccin A stabilized the ERalpha-ctp/14-3-3sigma complex by approximately 40-fold.
Comparison with human 14-3-3sigma with ERalpha C-terminal phosphothreonine peptide (ERalpha-ctp) (C terminal phosphopeptide 14-3-3 binding): affinity change = stronger; fold Kd decrease = 37.5.
Takeaway for interpretation: For molecular-glue experiments, record the stabilizer concentration and describe the Kd as conditional on that concentration; it is not an intrinsic binary affinity.

## human 14-3-3sigma with MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide (multisite phosphopeptide 14-3-3 binding)
id: pr-hdm2-dual-phospho-1433sigma-2024
kind: precedent
basis: literature
status: draft
topics: multisite phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: multisite phosphopeptide 14-3-3 binding, literature case study, human 14-3-3sigma, MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide, one set of sites ITC, binding enthalpy
cite: ward_mdm2_1433_2024
legacy_id: case_hdm2_dual_phospho_1433sigma_2024

System: cell: human 14-3-3sigma; syringe: MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide.
Conditions: model reported: one set of sites ITC.
Reported: Kd = 1.50 ± 0.40 µM; ΔG = -33.3 kJ/mol; ΔH = -4.40 ± 0.20 kJ/mol; −TΔS = -28.9 kJ/mol; stoichiometry = 0.6.
Authors' interpretation: One doubly phosphorylated MDM2 peptide binds a 14-3-3sigma dimer, but the phosphosites do not simultaneously bridge both grooves; the motifs rock between them.
Takeaway for interpretation: Multisite peptide binding can give a subunit-normalized stoichiometry near 0.5 without simultaneous bivalent engagement of both grooves.

## human 14-3-3zeta with MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide (multisite phosphopeptide 14-3-3 binding)
id: pr-hdm2-dual-phospho-1433zeta-2024
kind: precedent
basis: literature
status: draft
topics: multisite phosphopeptide 14-3-3 binding; related study sets: 14-3-3 phosphopeptide complexes
matches: multisite phosphopeptide 14-3-3 binding, literature case study, human 14-3-3zeta, MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide, one set of sites ITC, binding enthalpy
cite: ward_mdm2_1433_2024
legacy_id: case_hdm2_dual_phospho_1433zeta_2024

System: cell: human 14-3-3zeta; syringe: MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide.
Conditions: model reported: one set of sites ITC.
Reported: Kd = 380 ± 53 nM; ΔG = -36.7 kJ/mol; ΔH = -7.20 ± 0.10 kJ/mol; −TΔS = -29.4 kJ/mol; stoichiometry = 0.55.
Comparison with human 14-3-3sigma with MDM2(161-191) phospho-Ser166/phospho-Ser186 peptide (multisite phosphopeptide 14-3-3 binding): affinity change = stronger; fold Kd decrease = 3.9.
Takeaway for interpretation: Closely related 14-3-3 isoforms can differ substantially in affinity for the same multisite phosphopeptide; do not generalize an isoform-specific Kd to the family.

## human carbonic anhydrase II with CBS or DNSA arylsulfonamide inhibitors (protein–small molecule orthogonal validation)
id: pr-caii-sulfonamide-itc-spr-stoppedflow-2002
kind: precedent
basis: literature
status: draft
topics: protein–small molecule orthogonal validation; related study sets: ITC versus SPR, MS and fluorescence
matches: protein–small molecule orthogonal validation, literature case study, human carbonic anhydrase II, CBS or DNSA arylsulfonamide inhibitors, one to one with SPR mass transport for DNSA, ITC, SPR, stopped flow fluorescence
cite: day_caii_multimethod_2002
legacy_id: case_caii_sulfonamide_itc_spr_stoppedflow_2002

System: cell: human carbonic anhydrase II; syringe: CBS or DNSA arylsulfonamide inhibitors.
Conditions: model reported: one to one with SPR mass transport for DNSA.
Reported: Kd CBS ITC = 730 nM; Kd CBS SPR = 760 nM; Kd DNSA ITC = 360 nM; Kd DNSA SPR = 340 nM; Kd DNSA stopped flow = 420 nM.
Observations:
- Solution ITC, surface SPR, and stopped-flow fluorescence produced closely agreeing affinities when sample preparation and kinetic modeling accounted for method-specific artifacts.
- CBS had lower affinity but a fourfold slower dissociation rate than DNSA.
Orthogonal methods: ITC, SPR, stopped flow fluorescence.
Takeaway for interpretation: Agreement across solution and surface methods is evidence for a robust affinity only after mass transport and immobilization artifacts have been tested.

## human complement C3d with Staphylococcus aureus Efb-C and interface mutants (protein–protein electrostatic interface)
id: pr-efbc-c3d-itc-spr-2008
kind: precedent
basis: literature
status: draft
topics: protein–protein electrostatic interface; related study sets: ITC versus SPR, MS and fluorescence
matches: protein–protein electrostatic interface, literature case study, human complement C3d, Staphylococcus aureus Efb-C and interface mutants, single site with salt and mutant series, ITC, SPR
cite: haspel_efbc_c3d_2008
legacy_id: case_efbc_c3d_itc_spr_2008

System: cell: human complement C3d; syringe: Staphylococcus aureus Efb-C and interface mutants.
Conditions: model reported: single site with salt and mutant series.
Observations: ITC and SPR were used with salt and interface mutants to quantify the Efb-C:C3d interaction and its electrostatic contributions.
Orthogonal methods: ITC, SPR.
Takeaway for interpretation: Agreement in affinity trends across ITC and SPR can validate mutation effects while SPR separately exposes association and dissociation contributions.

## CdiA-CT toxin fragment with CysK or CdiI proteins (protein–protein multimeric complex)
id: pr-cdia-cysk-itc-spr-2016
kind: precedent
basis: literature
status: draft
topics: protein–protein multimeric complex; related study sets: ITC versus SPR, MS and fluorescence
matches: protein–protein multimeric complex, literature case study, CdiA-CT toxin fragment, CysK or CdiI proteins, ITC stoichiometry plus SPR affinity, ITC, SPR
cite: kaundal_cysk_cdia_2016
legacy_id: case_cdia_cysk_itc_spr_2016

System: cell: CdiA-CT toxin fragment; syringe: CysK or CdiI proteins.
Conditions: model reported: ITC stoichiometry plus SPR affinity.
Reported: ITC stoichiometry CdiA CT to CysK = 2.
Observations: ITC indicated a 2:1 CdiA-CT:CysK assembly, while SPR independently supported high-affinity complex formation.
Orthogonal methods: ITC, SPR.
Takeaway for interpretation: Use an orthogonal assay to support a non-unit ITC stoichiometry before assigning oligomeric architecture.

## carbonic anhydrase II with 4-carboxybenzenesulfonamide (CBS) (interlaboratory method comparison)
id: pr-caii-cbs-multilab-itc-spr-2004
kind: precedent
basis: literature
status: draft
topics: interlaboratory method comparison; related study sets: ITC versus SPR, MS and fluorescence
matches: interlaboratory method comparison, literature case study, carbonic anhydrase II, 4-carboxybenzenesulfonamide (CBS), one to one benchmark, ITC, SPR, analytical ultracentrifugation
cite: papalia_abrf_mirg_2004
legacy_id: case_caii_cbs_multilab_itc_spr_2004

System: cell: carbonic anhydrase II; syringe: 4-carboxybenzenesulfonamide (CBS).
Conditions: model reported: one to one benchmark.
Observations: A multi-laboratory benchmark compared assembly state, thermodynamics, and kinetics for the same enzyme-inhibitor system.
Orthogonal methods: ITC, SPR, analytical ultracentrifugation.
Takeaway for interpretation: Method agreement should be assessed across laboratories as well as across instruments, particularly for benchmark interactions.

## human carbonic anhydrase I with eight sulfonamide inhibitors (protein–ligand method comparison)
id: pr-cai-sulfonamides-ms-itc-spr-2009
kind: precedent
basis: literature
status: draft
topics: protein–ligand method comparison; related study sets: ITC versus SPR, MS and fluorescence
matches: protein–ligand method comparison, literature case study, human carbonic anhydrase I, eight sulfonamide inhibitors, one to one ligand series, ITC, SPR, native ESI mass spectrometry
cite: jecklin_cai_sulfonamide_2009
legacy_id: case_cai_sulfonamides_ms_itc_spr_2009

System: cell: human carbonic anhydrase I; syringe: eight sulfonamide inhibitors.
Conditions: model reported: one to one ligand series.
Observations: Binding constants spanning more than four orders of magnitude were compared by native mass spectrometry and validated against ITC and SPR.
Orthogonal methods: ITC, SPR, native ESI mass spectrometry.
Takeaway for interpretation: A method-comparison series is more informative than a single matched Kd because it tests agreement across the assay dynamic range.

## Klenow DNA polymerase with primed-template DNA (protein–DNA temperature series)
id: pr-klenow-primed-dna-itc-fa-2006
kind: precedent
basis: literature
status: draft
topics: protein–DNA temperature series; related study sets: protein–DNA thermodynamics, ITC with site-resolved methods
matches: protein–DNA temperature series, literature case study, Klenow DNA polymerase, primed-template DNA, one to one temperature series, ITC, fluorescence anisotropy, heat capacity change
cite: datta_klenow_dna_2006
legacy_id: case_klenow_primed_dna_itc_fa_2006

System: cell: Klenow DNA polymerase; syringe: primed-template DNA.
Conditions: model reported: one to one temperature series.
Reported: ΔCp range = -870 to -1220 cal/(mol K).
Observations: ITC and fluorescence anisotropy followed primed-template binding across 5-37 °C and supported a temperature-dependent affinity maximum near 25-30 °C.
Orthogonal methods: ITC, fluorescence anisotropy.
Takeaway for interpretation: Temperature-dependent affinity inferred from fluorescence should be checked against calorimetric enthalpy and heat-capacity trends.

## hen egg lysozyme with DNA or RNA aptamer (protein nucleic acid orthogonal validation)
id: pr-aptamer-lysozyme-itc-fa-auc-2011
kind: precedent
basis: literature
status: draft
topics: protein nucleic acid orthogonal validation; related study sets: DNA duplexes and DNA ligands, ITC with site-resolved methods
matches: protein nucleic acid orthogonal validation, literature case study, hen egg lysozyme, DNA or RNA aptamer, salt dependent binding, ITC, fluorescence anisotropy, analytical ultracentrifugation
cite: vogele_aptamer_lysozyme_2011
legacy_id: case_aptamer_lysozyme_itc_fa_auc_2011

System: cell: hen egg lysozyme; syringe: DNA or RNA aptamer.
Conditions: model reported: salt dependent binding.
Observations: Fluorescence anisotropy, ITC, and analytical ultracentrifugation characterized aptamer binding and its salt dependence.
Orthogonal methods: ITC, fluorescence anisotropy, analytical ultracentrifugation.
Takeaway for interpretation: Electrostatic nucleic-acid interactions benefit from an orthogonal method that can detect complex formation without relying only on binding heat.

## Mcm10 internal domain with DNA polymerase alpha p180 subunit (protein–protein with competing DNA)
id: pr-mcm10-pola-itc-fa-nmr-2009
kind: precedent
basis: literature
status: draft
topics: protein–protein with competing DNA; related study sets: ITC with site-resolved methods
matches: protein–protein with competing DNA, literature case study, Mcm10 internal domain, DNA polymerase alpha p180 subunit, one to one with competition, ITC, fluorescence anisotropy, NMR
cite: warren_mcm10_dna_pola_2009
legacy_id: case_mcm10_pola_itc_fa_nmr_2009

System: cell: Mcm10 internal domain; syringe: DNA polymerase alpha p180 subunit.
Conditions: model reported: one to one with competition.
Reported: Kd fluorescence anisotropy = 12 µM; Kd ITC = 30 µM.
Observations: Fluorescence anisotropy and ITC gave micromolar Mcm10-p180 affinity; NMR mapped the protein interface and showed competition by ssDNA.
Orthogonal methods: ITC, fluorescence anisotropy, NMR.
Takeaway for interpretation: A modest method-to-method Kd difference can be interpretable when the assays also establish shared binding-site usage and competition.

## streptavidin with biotin and fluorescent biotin analogues (protein–small molecule fluorescent probe validation)
id: pr-streptavidin-biotin-itc-fp-fret-2016
kind: precedent
basis: literature
status: draft
topics: protein–small molecule fluorescent probe validation; related study sets: ITC versus SPR, MS and fluorescence
matches: protein–small molecule fluorescent probe validation, literature case study, streptavidin, biotin and fluorescent biotin analogues, one site, ITC, fluorescence polarization, FRET
cite: lee_peterson_fret_fp_itc_2016
legacy_id: case_streptavidin_biotin_itc_fp_fret_2016

System: cell: streptavidin; syringe: biotin and fluorescent biotin analogues.
Conditions: model reported: one site.
Observations: ITC validated fluorescence-based binding measurements; fluorescence polarization failed for some probes because fluorophore quenching distorted the readout.
Orthogonal methods: ITC, fluorescence polarization, FRET.
Takeaway for interpretation: A fluorescent readout requires a probe-specific control: quenching can invalidate FP affinity estimates even when ITC is well behaved.

## bovine serum albumin with ferulic acid (protein–small molecule method discrepancy)
id: pr-ferulic-bsa-itc-anisotropy-cd-2012
kind: precedent
basis: literature
status: draft
topics: protein–small molecule method discrepancy; related study sets: ITC versus SPR, MS and fluorescence
matches: protein–small molecule method discrepancy, literature case study, bovine serum albumin, ferulic acid, spectroscopy and ITC comparison, ITC, fluorescence anisotropy, fluorescence lifetime, circular dichroism
cite: mishra_ferulic_bsa_2012
legacy_id: case_ferulic_bsa_itc_anisotropy_cd_2012

System: cell: bovine serum albumin; syringe: ferulic acid.
Conditions: model reported: spectroscopy and ITC comparison.
Observations: Fluorescence suggested one binding-site class whereas ITC suggested two; anisotropy, lifetime, and CD confirmed a binding-linked change in the protein environment and secondary structure.
Orthogonal methods: ITC, fluorescence anisotropy, fluorescence lifetime, circular dichroism.
Takeaway for interpretation: When fluorescence and ITC support different site models, retain the discrepancy as evidence rather than collapsing it to a single preferred model.

## human ileal bile-acid binding protein with glycocholate (two site positive cooperativity)
id: pr-ibabp-glycocholate-itc-nmr-2002
kind: precedent
basis: literature
status: draft
topics: two site positive cooperativity; related study sets: ITC with site-resolved methods
matches: two site positive cooperativity, literature case study, human ileal bile-acid binding protein, glycocholate, two step ITC plus site specific NMR, ITC, NMR
cite: tochtrop_ibabp_nmr_itc_2002
legacy_id: case_ibabp_glycocholate_itc_nmr_2002

System: cell: human ileal bile-acid binding protein; syringe: glycocholate.
Conditions: model reported: two step ITC plus site specific NMR.
Reported: intrinsic Kd site1 = 1.5 mM; intrinsic Kd site2 = 2.1 mM; step2 Kd = 1.5 µM; microscopic cooperativity factor = >1000; hill coefficient = 1.94.
Observations: ITC alone could not uniquely deconvolute the extreme cooperativity; isotope-resolved NMR measured individual-site occupancies and enabled site-specific thermodynamics.
Orthogonal methods: ITC, NMR.
Takeaway for interpretation: For highly cooperative multisite systems, a good ITC curve can still be non-identifying; site-resolved data may be essential to distinguish intrinsic affinity from cooperativity.

## Fyn SH3 domain with 12-residue proline-rich peptide (transient protein–peptide binding)
id: pr-fyn-sh3-prm-itc-nmr-2009
kind: precedent
basis: literature
status: draft
topics: transient protein–peptide binding; related study sets: ITC with kinetics and exchange methods
matches: transient protein–peptide binding, literature case study, Fyn SH3 domain, 12-residue proline-rich peptide, two state thermodynamics with NMR exchange, ITC, NMR, heat capacity change, binding enthalpy
cite: demers_fyn_sh3_nmr_itc_2009
legacy_id: case_fyn_sh3_prm_itc_nmr_2009

System: cell: Fyn SH3 domain; syringe: 12-residue proline-rich peptide.
Conditions: model reported: two state thermodynamics with NMR exchange.
Reported: ΔH at 303.15 K = 15.4 kcal/mol; ΔS at 303.15 K = 20 cal/(mol K); ΔCp = 352 cal/(mol K).
Observations: ITC established thermodynamics and heat capacity, while NMR exchange measured fast association/dissociation across temperature.
Orthogonal methods: ITC, NMR.
Takeaway for interpretation: Combining equilibrium calorimetry with NMR exchange separates thermodynamic stabilization from kinetic accessibility.

## Fyn SH3 domain with RR, SR, or SS proline-rich peptides (transient protein–peptide electrostatics)
id: pr-fyn-sh3-electrostatic-pathway-itc-nmr-2014
kind: precedent
basis: literature
status: draft
topics: transient protein–peptide electrostatics; related study sets: ITC with kinetics and exchange methods
matches: transient protein–peptide electrostatics, literature case study, Fyn SH3 domain, RR, SR, or SS proline-rich peptides, salt dependent affinity and exchange kinetics, ITC, NMR, stopped flow fluorescence
cite: meneses_fyn_sh3_nmr_itc_2014
legacy_id: case_fyn_sh3_electrostatic_pathway_itc_nmr_2014

System: cell: Fyn SH3 domain; syringe: RR, SR, or SS proline-rich peptides.
Conditions: model reported: salt dependent affinity and exchange kinetics.
Observations: NMR titrations agreed with ITC affinity trends across ionic strength and peptide charge variants; stopped-flow required cooling because room-temperature kinetics exceeded its range.
Orthogonal methods: ITC, NMR, stopped flow fluorescence.
Takeaway for interpretation: A failed or range-limited orthogonal kinetic assay is informative: do not treat it as independent confirmation of the faster rate regime.

## DNA cocaine-binding aptamer variants with cocaine (nucleic acid small molecule folding linkage)
id: pr-cocaine-aptamer-itc-nmr-2010
kind: precedent
basis: literature
status: draft
topics: nucleic acid small molecule folding linkage; related study sets: RNA binding, ITC with site-resolved methods
matches: nucleic acid small molecule folding linkage, literature case study, DNA cocaine-binding aptamer variants, cocaine, stem length temperature series, ITC, NMR, heat capacity change
cite: neves_cocaine_aptamer_nmr_itc_2010
legacy_id: case_cocaine_aptamer_itc_nmr_2010

System: cell: DNA cocaine-binding aptamer variants; syringe: cocaine.
Conditions: model reported: stem length temperature series.
Reported: ΔCp long stem = -557 cal/(mol K); ΔCp short stem = -922 cal/(mol K).
Observations: ITC reported stem-length-dependent thermodynamics and heat capacities, while NMR chemical shifts and intermolecular NOEs located cocaine and characterized the folding transition.
Orthogonal methods: ITC, NMR.
Takeaway for interpretation: A ΔCp difference among nucleic-acid constructs gains mechanistic value when NMR independently locates the ligand and conformational transition.

## CERT pleckstrin-homology domain with PI4P-containing nanodiscs (protein membrane specificity)
id: pr-cert-ph-pi4p-nanodisc-itc-nmr-2022
kind: precedent
basis: literature
status: draft
topics: protein membrane specificity; related study sets: detergents and membrane proteins, ITC with site-resolved methods
matches: protein membrane specificity, literature case study, CERT pleckstrin-homology domain, PI4P-containing nanodiscs, specific lipid binding vs nonspecific nanodisc association, ITC, solution NMR
cite: sugiki_cert_nanodisc_nmr_itc_2022
legacy_id: case_cert_ph_pi4p_nanodisc_itc_nmr_2022

System: cell: CERT pleckstrin-homology domain; syringe: PI4P-containing nanodiscs.
Conditions: model reported: specific lipid binding vs nonspecific nanodisc association.
Observations: ITC differentiated specific PI4P engagement from nonspecific nanodisc interactions, and HSQC/cross-saturation NMR mapped the interaction at atomic resolution.
Orthogonal methods: ITC, solution NMR.
Takeaway for interpretation: For membrane-protein systems, calorimetry should be interpreted with a structural method that distinguishes target-lipid recognition from bulk-surface association.

## protein L immunoglobulin-binding domain with human kappa light chain (two site protein–protein binding)
id: pr-proteinl-lightchain-itc-stoppedflow-2004
kind: precedent
basis: literature
status: draft
topics: two site protein–protein binding; related study sets: ITC with kinetics and exchange methods
matches: two site protein–protein binding, literature case study, protein L immunoglobulin-binding domain, human kappa light chain, two site binding, ITC, stopped flow fluorescence, X ray crystallography, site directed mutagenesis
cite: housden_proteinl_lightchain_2004
legacy_id: case_proteinl_lightchain_itc_stoppedflow_2004

System: cell: protein L immunoglobulin-binding domain; syringe: human kappa light chain.
Conditions: model reported: two site binding.
Reported: Kd site1 ITC = 37.5 nM; Kd site1 stopped flow = 48 nM; Kd site2 ITC = 4.6 µM; Kd site2 stopped flow = 3.4 µM.
Observations: Stopped-flow and ITC independently resolved a nanomolar and a micromolar light-chain-binding site with agreeing Kd values.
Orthogonal methods: ITC, stopped flow fluorescence, X ray crystallography, site directed mutagenesis.
Takeaway for interpretation: Agreement for both sites in a two-site system is much stronger model support than agreement only for a composite affinity.

## human alpha-thrombin with melagatran, inogatran, or CH-248 (tight inhibitor binding orthogonal validation)
id: pr-thrombin-inhibitors-itc-spr-stoppedflow-2002
kind: precedent
basis: literature
status: draft
topics: tight inhibitor binding orthogonal validation; related study sets: ITC with kinetics and exchange methods
matches: tight inhibitor binding orthogonal validation, literature case study, human alpha-thrombin, melagatran, inogatran, or CH-248, one to one temperature series, ITC, SPR, stopped flow spectrophotometry, chromogenic inhibition assay, binding enthalpy
cite: deinum_thrombin_inhibitors_2002
legacy_id: case_thrombin_inhibitors_itc_spr_stoppedflow_2002

System: cell: human alpha-thrombin; syringe: melagatran, inogatran, or CH-248.
Conditions: model reported: one to one temperature series.
Reported: SPR Kd range = 1.1-4.8 nM; ITC delta H range = -90.4 to -115.3 kJ/mol.
Observations: ITC, biosensor kinetics, and stopped-flow were combined to characterize inhibitor thermodynamics and temperature-dependent dissociation.
Orthogonal methods: ITC, SPR, stopped flow spectrophotometry, chromogenic inhibition assay.
Takeaway for interpretation: For tight inhibition, combine a solution thermodynamic assay with kinetic methods rather than interpreting a single equilibrium fit as a complete binding description.

## Tyr485Trp GroEL with MgATP2- (multisite nucleotide binding)
id: pr-groel-mgatp-itc-stoppedflow-2011
kind: precedent
basis: literature
status: draft
topics: multisite nucleotide binding; related study sets: ITC with kinetics and exchange methods
matches: multisite nucleotide binding, literature case study, Tyr485Trp GroEL, MgATP2-, bimolecular kinetic and equilibrium comparison, ITC, stopped flow fluorescence, mutagenesis
cite: aumuller_groel_mgatp_2011
legacy_id: case_groel_mgatp_itc_stoppedflow_2011

System: cell: Tyr485Trp GroEL; syringe: MgATP2-.
Conditions: model reported: bimolecular kinetic and equilibrium comparison.
Reported: stopped flow kon = 91400 M^-1 s^-1; stopped flow koff = 14.2 s^-1; stopped flow Kb = 6400 M^-1; ITC Kb = 9500 M^-1; ITC stoichiometry = 6.6.
Observations: Stopped-flow and ITC gave similar equilibrium association constants while providing complementary kinetic and occupancy information.
Orthogonal methods: ITC, stopped flow fluorescence, mutagenesis.
Takeaway for interpretation: A close kinetic-derived versus ITC equilibrium constant is a valuable closure check for a proposed bimolecular model.

## Ca2+-loaded recoverin with rhodopsin-kinase N-terminal construct (protein–protein conformational selection)
id: pr-recoverin-rk-itc-nmr-stoppedflow-2016
kind: precedent
basis: literature
status: draft
topics: protein–protein conformational selection; related study sets: ITC with kinetics and exchange methods
matches: protein–protein conformational selection, literature case study, Ca2+-loaded recoverin, rhodopsin-kinase N-terminal construct, conformational selection flux model, ITC, NMR relaxation dispersion, stopped flow fluorescence
cite: chakrabarti_recoverin_rk_2016
legacy_id: case_recoverin_rk_itc_nmr_stoppedflow_2016

System: cell: Ca2+-loaded recoverin; syringe: rhodopsin-kinase N-terminal construct.
Conditions: model reported: conformational selection flux model.
Reported: binding competent recoverin population = 3 %.
Observations: ITC supplied equilibrium thermodynamics, NMR identified a pre-existing binding-competent recoverin state, and stopped-flow showed that its interconversion limits binding.
Orthogonal methods: ITC, NMR relaxation dispersion, stopped flow fluorescence.
Takeaway for interpretation: Agreement between an NMR exchange rate and the high-ligand stopped-flow limiting rate can discriminate conformational selection from induced fit.

## CBP TAZ1 domain with STAT2 transactivation domain (IDR protein–protein partial folding)
id: pr-taz1-stat2-itc-nmr-stoppedflow-2018
kind: precedent
basis: literature
status: draft
topics: IDR protein–protein partial folding; related study sets: disordered-region binding, ITC with site-resolved methods
matches: IDR protein–protein partial folding, literature case study, CBP TAZ1 domain, STAT2 transactivation domain, coupled folding binding with association and displacement kinetics, ITC, NMR relaxation, stopped flow association and displacement
cite: lindstrom_taz1_stat2_2018
legacy_id: case_taz1_stat2_itc_nmr_stoppedflow_2018

System: cell: CBP TAZ1 domain; syringe: STAT2 transactivation domain.
Conditions: model reported: coupled folding binding with association and displacement kinetics.
Observations: ITC, stopped-flow association/displacement kinetics, and NMR dynamics showed that the complex retains substantial internal dynamics and only partial STAT2 disorder-to-order folding.
Orthogonal methods: ITC, NMR relaxation, stopped flow association and displacement.
Takeaway for interpretation: A thermodynamically stable IDR complex can retain fast internal motion; a single equilibrium model should not be interpreted as proof of a rigid final complex.

## plasma-purified human serum albumin (HSA), 50 µM with CoCl2, 2 mM (metal–protein multisite sequential binding) (part 1 of 5)
id: pr-hsa-cobalt-three-sequential-fit-stability-2023
kind: precedent
basis: literature
status: draft
topics: metal–protein multisite sequential binding; related study sets: metal-ion binding
matches: metal–protein multisite sequential binding, literature case study, plasma-purified human serum albumin (HSA), 50 µM, CoCl2, 2 mM, 50 mM Tris, 50 mM NaCl, pH 7.4, 35 injections; first 2 uL then 8 uL injections; 210 s interval, ITC, X ray crystallography, site directed mutagenesis, 1H NMR, circular dichroism, binding enthalpy
cite: wu_albumin_cobalt_multisite_2023
legacy_id: case_hsa_cobalt_three_sequential_fit_stability_2023

System: cell: plasma-purified human serum albumin (HSA), 50 µM; syringe: CoCl2, 2 mM; buffer: 50 mM Tris, 50 mM NaCl, pH 7.4; temperature (K): 298.15; injection protocol: 35 injections; first 2 uL then 8 uL injections; 210 s interval.
Conditions: model reported: three sequential binding sites in MicroCal Origin 7 (Fit 1-2); alternative three-sets-of-sites models in AFFINImeter.
Reported: published Origin three sequential sites: (site 1: (Kd = 2.24 µM; ΔH = -20.2 kJ/mol; log Ka = 5.65); site 2: (Kd = 13.8 µM; ΔH = -16.5 kJ/mol; log Ka = 4.86); site 3: (Kd = 324 µM; ΔH = -61.5 kJ/mol; log Ka = 3.49); table S3 chi square per degree = 671).

## plasma-purified human serum albumin (HSA), 50 µM with CoCl2, 2 mM (metal–protein multisite sequential binding) (part 2 of 5)
id: pr-hsa-cobalt-three-sequential-fit-stability-2023-2
kind: precedent
basis: literature
status: draft
topics: metal–protein multisite sequential binding; related study sets: metal-ion binding
matches: metal–protein multisite sequential binding, literature case study, plasma-purified human serum albumin (HSA), 50 µM, CoCl2, 2 mM, 50 mM Tris, 50 mM NaCl, pH 7.4, 35 injections; first 2 uL then 8 uL injections; 210 s interval, ITC, X ray crystallography, site directed mutagenesis, 1H NMR, circular dichroism, binding enthalpy
cite: wu_albumin_cobalt_multisite_2023
legacy_id: case_hsa_cobalt_three_sequential_fit_stability_2023

Observations:
- The authors state that binding-site properties with overlapping equilibria are often unlikely to be fully discerned by fitting, and describe their assessment as semi-quantitative.
- The paper selected the three-sequential-site model as the simplest model yielding a reasonable fit for HSA, while alternative AFFINImeter three-sets-of-sites fits accommodated two or three weak third-site equivalents.
- In supplementary Table S1, affinities and enthalpies for the AFFINImeter three-sets-of-sites analysis were derived from the Origin sequential fit or fixed; these alternative models are not independent unconstrained confirmations of all six thermodynamic parameters.
- For HSA, Table S3 reports a 671.1 chi-square-per-degree value for the Origin three-sequential-sites fit. This is not directly comparable without knowing the table's scaling/definition to the paper's general stated 0.7 < chi-square < 3 goodness criterion.
- Crystallography, mutation, NMR folding checks, and CD support the presence and structural assignment of strong and secondary cobalt-binding regions, but do not independently establish a unique three-step ITC enthalpy decomposition.
Orthogonal methods: ITC, X ray crystallography, site directed mutagenesis, 1H NMR, circular dichroism.

## plasma-purified human serum albumin (HSA), 50 µM with CoCl2, 2 mM (metal–protein multisite sequential binding) (part 3 of 5)
id: pr-hsa-cobalt-three-sequential-fit-stability-2023-3
kind: precedent
basis: literature
status: draft
topics: metal–protein multisite sequential binding; related study sets: metal-ion binding
matches: metal–protein multisite sequential binding, literature case study, plasma-purified human serum albumin (HSA), 50 µM, CoCl2, 2 mM, 50 mM Tris, 50 mM NaCl, pH 7.4, 35 injections; first 2 uL then 8 uL injections; 210 s interval, ITC, X ray crystallography, site directed mutagenesis, 1H NMR, circular dichroism, binding enthalpy
cite: wu_albumin_cobalt_multisite_2023
legacy_id: case_hsa_cobalt_three_sequential_fit_stability_2023

Comparison: FT ITC reanalysis user reported: (provenance = user_corrected; site 1: (Kd = 5 µM; Kd uncertainty = 2 µM; ΔH = -21 kJ/mol; ΔH uncertainty = 1 kJ/mol); site 2: (Kd = 19 µM; Kd uncertainty = 9 µM; ΔH = -16 kJ/mol; ΔH uncertainty = 2 kJ/mol); site 3: (Kd = 190 µM; Kd uncertainty = 50 µM; ΔH = -34 kJ/mol; ΔH uncertainty = 2 kJ/mol); notes = The original supplied table omitted the minus sign for the FT-ITC third-site enthalpy; it was corrected by the user. The supplied Kd,2 unit was not explicit in the comparison table; it is stored as µM by consistency with the Kd column and the other Kd values.).
Identifiability context (compiler-derived): Only the first transition is in the cited optimal c range of 20-100. The second and especially the third transition are weakly informative for affinity under these concentrations. Basis: c = n*P_t/Kd using 50 µM HSA, n=1 for each sequential event, and the published Kd values.
Modeling note: The third-site Kd changes from published 324 µM to the user-reported FT-ITC 190 +/- 50 µM; this magnitude is compatible with the low-c, overlapping-transition regime and should not by itself be read as a mechanistic contradiction.

## plasma-purified human serum albumin (HSA), 50 µM with CoCl2, 2 mM (metal–protein multisite sequential binding) (part 4 of 5)
id: pr-hsa-cobalt-three-sequential-fit-stability-2023-4
kind: precedent
basis: literature
status: draft
topics: metal–protein multisite sequential binding; related study sets: metal-ion binding
matches: metal–protein multisite sequential binding, literature case study, plasma-purified human serum albumin (HSA), 50 µM, CoCl2, 2 mM, 50 mM Tris, 50 mM NaCl, pH 7.4, 35 injections; first 2 uL then 8 uL injections; 210 s interval, ITC, X ray crystallography, site directed mutagenesis, 1H NMR, circular dichroism, binding enthalpy
cite: wu_albumin_cobalt_multisite_2023
legacy_id: case_hsa_cobalt_three_sequential_fit_stability_2023

Modeling note: The corrected third-site enthalpies have the same sign (published -61.5 kJ/mol; user-reported -34 +/- 2 kJ/mol), but differ substantially in magnitude. Do not assign a precise site-specific mechanistic meaning without examining residuals, baselines, parameter profiles, and competing models.
Modeling note: The paper does not report a covariance matrix or parameter-correlation coefficients, so correlation is not demonstrated quantitatively. Nevertheless, correlation among Kd and delta_H of the overlapping weak transitions is expected from the design and is supported qualitatively by the authors' model-dependence discussion and constrained alternative fits.
Modeling note: A small conditional uncertainty from one selected model does not establish global identifiability when different plausible models or constraints produce materially different parameters.

## plasma-purified human serum albumin (HSA), 50 µM with CoCl2, 2 mM (metal–protein multisite sequential binding) (part 5 of 5)
id: pr-hsa-cobalt-three-sequential-fit-stability-2023-5
kind: precedent
basis: literature
status: draft
topics: metal–protein multisite sequential binding; related study sets: metal-ion binding
matches: metal–protein multisite sequential binding, literature case study, plasma-purified human serum albumin (HSA), 50 µM, CoCl2, 2 mM, 50 mM Tris, 50 mM NaCl, pH 7.4, 35 injections; first 2 uL then 8 uL injections; 210 s interval, ITC, X ray crystallography, site directed mutagenesis, 1H NMR, circular dichroism, binding enthalpy
cite: wu_albumin_cobalt_multisite_2023
legacy_id: case_hsa_cobalt_three_sequential_fit_stability_2023

Takeaway for interpretation:
- Treat Kd,1/delta_H1 as the most reliable component; the reported and FT-ITC values agree within the supplied uncertainty. Kd,2/delta_H2 are directionally consistent but arise below the optimal c range.
- The third-site enthalpy is negative in both fits but remains model-dependent; disclose the published-versus-refit magnitude difference rather than claiming a sign disagreement.
- For a reproducibility comparison, refit the same baseline-corrected heats with two- and three-step sequential models and a sets-of-sites model; inspect residuals, profile likelihoods or bootstrap distributions, and the Kd--delta_H covariance/correlation structure before selecting a third transition.
- Where possible, fit related wild-type, mutant, and fatty-acid datasets globally with justified shared parameters, but retain model-comparison diagnostics rather than using a fixed sequential fit as independent validation.
Flags: weak secondary site below ITC sensitivity, model complexity not independently supported.
