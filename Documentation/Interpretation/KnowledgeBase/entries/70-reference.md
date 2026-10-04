<!--
Reference data. Values are approximate textbook numbers (about +/-10 %, buffer enthalpies about
+/-2 kJ/mol) and are labelled as such in the entries. Verify against primary tables before use as
quantitative inputs. Original compilations are listed under 'reading:' (source-only, not emitted).
-->

## Constants, unit conversions and quick formulas for ITC
id: rf-units-and-formulas
kind: reference
topics: units, conversion, formulas
basis: general
status: draft
matches: unit conversion, kcal to kJ, µcal to µJ, gas constant R, RT value, ΔG from Kd, error propagation ΔG, c-value formula, ΔS from ΔG ΔH, Gibbs Helmholtz, temperature conversion

Constants: R = 8.314 J/(mol K) = 1.987 cal/(mol K); RT = 2.479 kJ/mol (0.592 kcal/mol) at 298.15 K and 2.579 kJ/mol at 310.15 K; ln 10 = 2.303; 0 °C = 273.15 K.
Energy: 1 cal = 4.184 J; 1 kcal/mol = 4.184 kJ/mol; 1 µcal = 4.184 µJ; 1 µcal/s = 4.184 µW.
Relations: ΔG = RT ln(Kd) = -RT ln(Ka); −TΔS = ΔG − ΔH; ΔS = (ΔH − ΔG)/T; ΔH(T) = ΔH(T0) + ΔCp (T − T0); a 10 % error in K corresponds to RT × 0.1 = 0.25 kJ/mol in ΔG.
Design: c = n[M]t/Kd; fraction bound at excess ligand = [L]/(Kd + [L]); heat in the saturating regime q ≈ ΔH × [L]syringe × V_inj.
Molar quantities: nmol = µM × µL / 1000; for example 2 µL of 200 µM ligand is 0.4 nmol.

## Buffer ionization enthalpies at 25 °C (kJ/mol): which buffers add the most proton-transfer heat
id: rf-buffer-ionization-enthalpy
kind: reference
topics: buffers, proton linkage
basis: general
status: draft
matches: buffer ionization enthalpy table, Tris ionization enthalpy, phosphate ionization enthalpy, HEPES ΔH ion, buffer choice for proton linkage, buffer protonation enthalpy values, which buffer for ITC
reading: Goldberg, Kishore & Lennen 2002 J Phys Chem Ref Data 31:231; Fukada & Takahashi 1998 Proteins 33:159

Approximate ionization (deprotonation) enthalpy ΔH_ion, kJ/mol, near 25 °C and low ionic strength, with the working pKa: acetate +0.4 (4.8); phosphate (H₂PO₄⁻/HPO₄²⁻) about +4 to +5 (7.2); PIPES about +11 (6.8); MES about +15 (6.1); MOPS about +21 (7.2); HEPES about +21 (7.5); Bicine about +27 (8.3); Bis-Tris about +28 (6.5); ACES about +31 (6.8); Tricine about +31 to +32 (8.1); TES about +32 (7.4); imidazole about +37 (7.0); glycine (amino group) about +44 (9.6); Tris about +47 (8.1); ammonium about +52 (9.25).
Use: when binding transfers protons, observed ΔH shifts by about n × ΔH_ion. Low-ΔH_ion buffers (phosphate, acetate, PIPES, MES) minimize this contribution; high-ΔH_ion buffers (Tris, imidazole, glycine) amplify it. Values depend on ionic strength and temperature (ΔCp of ionization is nonzero), and the application's buffer registry holds approximations with some unknown quantities.
Reference sets: the buffer series for a regression typically combines one low, one medium and one high ΔH_ion buffer at the same pH.

## Buffer pKa temperature coefficients: how pH shifts when temperature changes
id: rf-buffer-ph-temperature
kind: reference
topics: buffers, temperature, pH
basis: general
status: draft
matches: pKa temperature coefficient, Tris pH changes with temperature, pH shift between 25 and 37 °C, dpKa/dT, buffer for temperature series, phosphate temperature stability, HEPES temperature dependence

Approximate dpKa/dT per K near 25 °C: acetate about 0; phosphate −0.003; PIPES −0.009; MES −0.011; HEPES −0.014; MOPS −0.015; Bis-Tris −0.017; Bicine −0.018; ACES −0.020; imidazole −0.020; TES −0.020; Tricine −0.021; Tris −0.028. The temperature coefficient follows the ionization enthalpy: dpKa/dT ≈ −ΔH_ion / (2.303 R T²), roughly −0.0006 per kJ/mol at 25 °C.
Consequences: a Tris buffer set to pH 7.5 at 25 °C is about pH 7.8 at 15 °C and about 7.2 at 37 °C; HEPES shifts about 0.17 units between 25 and 37 °C; phosphate only about 0.03.
Practice: for temperature series (ΔCp) use a low-coefficient buffer or adjust pH at each temperature; pH drift with temperature also changes protonation of ligands and proteins and therefore ΔH.

## Buffers, chelators and metals: which common buffers coordinate or precipitate metal ions
id: rf-buffer-metal-binding
kind: reference
topics: buffers, metals
basis: general
status: draft
matches: metal binding buffers, Tris copper zinc, phosphate precipitates metals, HEPES MOPS metal coordination, citrate chelator, buffer for metal ITC, Good's buffers metal affinity

Key points: Tris binds Cu²⁺, Ni²⁺, Zn²⁺ and Co²⁺ moderately; imidazole, glycine, Tricine and Bicine coordinate transition metals; citrate, EDTA/EGTA, phosphate (precipitation of Ca²⁺, Zn²⁺, Mn²⁺, Fe³⁺) and carbonate remove or compete for metals; DTT chelates metals, and TCEP interacts with some.
Weakly coordinating choices: MES, MOPS, PIPES and HEPES bind metals only weakly (HEPES has minor Cu²⁺ binding).
Consequence: metal-binding ITC in a coordinating buffer measures a competition process; correcting for buffer complexation requires known buffer–metal affinities and protons released from the protein ligand, with pH well below hydrolysis onset of the metal.

## Typical small-cell and large-cell instrument parameters
id: rf-instrument-parameters
kind: reference
topics: instruments
basis: general
status: draft
matches: VP-ITC cell volume, iTC200, PEAQ-ITC, Nano ITC, Affinity ITC, instrument specs, cell volume syringe volume, default injection scheme, stirring speed rpm, reference power
reading: manufacturer manuals; confirm values for the specific instrument and software version

Approximate values (confirm with the instrument manual): MicroCal VP-ITC: cell about 1.4 mL, syringe about 290 µL, typical 28 injections of 10 µL, spacing about 240 s, stirring about 300 rpm. MicroCal iTC200 and Auto-iTC200: cell about 200 µL, syringe about 40 µL, typical small first injection (0.2 to 0.5 µL) then about 18 to 19 injections of 2 µL, spacing about 150 s, stirring about 750 rpm. MicroCal PEAQ-ITC: similar cell and syringe geometry to the iTC200 and similar default schedule. TA Instruments Nano ITC: standard cell about 1 mL and low-volume cell about 170 µL.
Typical reference power is about 5 to 10 µcal/s; default temperature 25 °C. Real sensitivity depends on the instrument, baseline noise and feedback mode.

## Common detergents: approximate critical micelle concentrations
id: rf-detergent-cmc
kind: reference
topics: detergents, CMC
basis: general
status: draft
matches: detergent CMC table, critical micelle concentration values, DDM CMC, SDS CMC, Triton X-100 CMC, CHAPS CMC, LMNG CMC, detergent concentration for membrane protein ITC

Approximate CMC in water at 20 to 25 °C (varies with salt, temperature and purity): LMNG about 0.01 mM; Tween-20 about 0.06 mM; DDM about 0.17 mM; Triton X-100 about 0.2 to 0.3 mM; digitonin about 0.5 mM; LDAO about 1 to 2 mM; decyl maltoside about 1.8 mM; CHAPS about 6 to 10 mM; SDS about 8 mM; octyl glucoside about 18 to 25 mM.
Use: detergent concentrations above the CMC contain micelles whose dilution gives heats; below the CMC amphiphiles are monomeric; adding salt lowers the CMC of ionic detergents.

## Hofmeister series and specific-ion effects
id: rf-hofmeister
kind: reference
topics: ions, Hofmeister
basis: general
status: draft
matches: Hofmeister series, kosmotropes chaotropes, specific ion effects, anion effect on binding, glutamate versus chloride, sulfate phosphate citrate salting out, thiocyanate perchlorate, ion specific salt effects

Anions, from strongly hydrated (kosmotropic, stabilizing, salting-out) to weakly hydrated (chaotropic, destabilizing, salting-in): citrate, sulfate, phosphate, acetate, chloride, bromide, nitrate, iodide, perchlorate, thiocyanate. Cations: ammonium, potassium and sodium are intermediate, guanidinium is chaotropic, magnesium and calcium are more strongly hydrated.
Consequences for binding: salt effects on affinity include electrostatic screening, ion binding and exclusion, and hydration or osmotic contributions; at equal concentration different anions can shift ΔG and ΔH differently, especially for protein–DNA interactions (chloride is weakly interacting, glutamate and fluoride are excluded).

## Typical affinity and enthalpy ranges by interaction class (orders of magnitude only)
id: rf-typical-ranges-by-system
kind: reference
topics: typical values, expectations
basis: general
status: draft
matches: typical Kd ranges, expected affinity by system, antibody antigen Kd, protein protein Kd, lectin sugar Kd, DNA binding Kd, enzyme inhibitor Kd, SLiM Kd, typical ΔH values

Approximate Kd ranges (order of magnitude): antibody–antigen pM to low nM; optimized enzyme inhibitors and drugs sub-nM to µM; fragments tens of µM to mM; protein–protein stable complexes pM to µM and transient ones µM to mM; short linear motif–domain 1 to 500 µM; specific protein–DNA sub-nM or lower at physiological salt; nonspecific protein–DNA µM to mM; lectin–monosaccharide about 10 µM to mM; Ca²⁺ with EF-hand proteins sub-µM to µM; tightly bound structural Zn²⁺ sites pM to fM.
Approximate ΔH: small-molecule binding −10 to −80 kJ/mol; protein–protein interfaces often −30 to −150 kJ/mol or endothermic with large entropy gain; duplex formation about −30 kJ/mol per base-pair step; lectin–sugar −20 to −60 kJ/mol.
Use: expectation checks for unusual Kd, ΔH or n; values outside these ranges are not wrong but deserve a second look at concentrations and conditions.

## Concentration determination methods and their typical accuracy
id: rf-concentration-methods
kind: reference
topics: concentration, extinction coefficient
basis: general
status: draft
matches: protein concentration measurement, extinction coefficient calculation, A280 accuracy, nucleic acid concentration A260, BCA Bradford accuracy, peptide quantification, amino acid analysis, concentration error sources

Protein by A280: ε280 ≈ 5500 × (Trp) + 1490 × (Tyr) + 125 × (disulfide) M⁻¹cm⁻¹ for native protein; typical accuracy 5 to 10 %, worse for proteins with few aromatics, bound cofactors or nucleic acid contamination (A260/A280 above about 0.7 suggests contamination, compared with about 0.57 for pure protein); colorimetric assays (BCA, Bradford) depend on composition and are accurate only to about 10 to 30 %.
Nucleic acids by A260: 1 A260 unit ≈ 50 µg/mL double-stranded DNA, ≈ 40 µg/mL RNA, ≈ 33 µg/mL single-stranded DNA/oligonucleotide; sequence-specific extinction coefficients from nearest-neighbor values are more accurate for oligonucleotides.
Peptides and small molecules: quantitative amino acid analysis, quantitative NMR or calibrated absorbance; gravimetric concentration needs correction for counterions and water.
Practice: when n matters, quantify by two independent methods; remember ITC concentration errors scale n, ΔH and (less) Kd without worsening the fit.
