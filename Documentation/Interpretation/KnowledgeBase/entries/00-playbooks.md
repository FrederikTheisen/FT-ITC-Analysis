<!--
Playbooks: how an experienced ITC analyst organizes judgment. They are written as
knowledge, not as instructions to the reader, because retrieved text is evidence.
Entry grammar: see ../README.md.
-->

## Reading an ITC dataset: the order experienced analysts inspect it, and what each step can overturn
id: pb-triage-order
kind: playbook
topics: interpretation workflow, triage
basis: general
status: draft
matches: how to interpret ITC data, where to start, workflow, checklist, order of inspection, data quality first, interpretation strategy, what matters most

Sequence: (1) Raw thermogram and baseline: settled start, no spikes or bubbles, peaks return to baseline, plausible noise. (2) Injection heats: signal well above noise and blank, sensible sign, curvature versus a constant offset, plateau level at the end. (3) Design sanity: c-value, final molar ratio, concentrations plausible for the system, buffer matching documented, control titration available. (4) Fit: residual pattern, parameters at bounds, interval widths and correlations, n against expectation. (5) Plausibility: Kd, ΔH and n against what is known about the system and any orthogonal data. (6) Consequences: which numbers can be quoted, with what qualifier, and which single follow-up would remove the largest remaining doubt.

Earlier steps can overturn later ones. A buffer mismatch or drifting baseline invalidates fit statistics; a perfect fit does not validate concentrations; a good one-site fit does not exclude a weak second site or a fuzzy complex.

Materiality: effort belongs where a problem would change a conclusion. Slight drift with a long settled baseline, a noisy first injection that is excluded, or a modest deviation of n from an integer rarely changes anything. An unexplained offset comparable to the binding heat, a missing plateau, or structured residuals usually does.

## What an ITC dataset can support: a ladder of claims from "heat observed" to "mechanism"
id: pb-claim-strength-ladder
kind: playbook
topics: claim strength, overinterpretation, reporting
basis: general
status: draft
matches: how strong a claim, overinterpretation, supported conclusions, what can be concluded, precise Kd, mechanism claim, evidence needed, confidence level, qualified language

Ladder: weakest to strongest claim, with the evidence each rung needs.
1. Heat above the control: sample injections differ from the matched blank, reproducibly. Supports "a heat-producing interaction or process occurs under these conditions".
2. Binding-like isotherm: saturable curvature with approach to a plateau, not just an offset. Supports "consistent with binding".
3. Affinity range: acceptable c-value or a displacement design; intervals on Kd that close. Supports "Kd between X and Y".
4. Precise Kd and ΔH: well-conditioned curve, accurate concentrations, matched buffer, replicates from independent preparations.
5. Stoichiometry: accurate active concentrations of both partners and a plausible structural expectation.
6. Thermodynamic signature comparisons between ligands or constructs: same conditions, replicated, uncertainties propagated into ΔΔG, ΔΔH and −TΔS.
7. Mechanistic attribution (hydrophobic, hydrogen bonding, ordering, proton linkage): needs variation experiments (buffer, temperature, salt, mutants) or structural and orthogonal evidence.

The most common error is reporting one rung above what the data support, for example a three-significant-figure Kd from a step-like isotherm, or a molecular mechanism from the sign of ΔH.

## ITC measures the total heat of everything that happens in the cell, so fitted parameters are apparent and condition-specific
id: pb-itc-measures-heat-not-binding
kind: playbook
topics: apparent parameters, linked equilibria, conditions
basis: mixed
status: draft
matches: apparent Kd, apparent enthalpy, intrinsic versus observed, buffer dependence, conditions matter, what ITC measures, linked processes, condition dependent parameters
cite: nature_primer_2023, paketuryte_intrinsic_thermo_2019

Key points: The signal is the sum of all heat from mixing: binding, protonation and buffer ionization, ion exchange, conformational change, oligomerization or aggregation, dilution and mixing of solvents, and any chemistry such as hydrolysis. A binding model assigns all of it to the binding equilibrium, so the fitted ΔH is a composite and the fitted Kd is an apparent affinity that includes any linked equilibrium (protonation, metal or nucleotide competition, conformational pre-equilibria, self-association).

Consequences: parameters depend on buffer identity, pH, ionic strength, temperature, additives and construct. Values from different conditions are not directly comparable, and "intrinsic" quantities need a linkage analysis (see buffer-series and salt-series entries). Reports should state conditions next to every number.

## Kinds of uncertainty in an ITC result: fit-derived, resampling, replicate and systematic
id: pb-uncertainty-types
kind: playbook
topics: uncertainty, error analysis, replicates
basis: mixed
status: draft
matches: confidence interval, standard error, bootstrap, profile likelihood, replicate variability, realistic uncertainty, systematic error, how reliable are the errors, error bars
cite: tellinghuisen_2011, nguyen_bayesian_itc_2018, brautigam_2016

Fit-derived: asymptotic standard errors assume a locally linear, correct model with correct weights; they usually understate the true uncertainty and can be symmetric when the real interval is not.
Resampling and profile methods: bootstrap, leave-one-out and profile likelihood handle nonlinearity and asymmetry better, but remain conditional on the model, concentrations and baseline treatment, and on small numbers of injections (about 15 to 30) being representative.
Replicates: independent preparations capture concentration, activity and handling variation. For well-behaved systems, replicate Kd values commonly agree within roughly 1.2- to 2-fold and ΔH within about 5 to 10 percent; larger spread points to sample or design problems, not just noise.
Systematic: concentration errors (extinction coefficients are often off by 5 to 10 percent, peptides by more), cell-volume calibration, baseline and integration choices, and buffer mismatch. These shift parameters without worsening residuals.
Not independent evidence: re-analysis of one thermogram, imposed parameter sharing, and repeated fits to the same injection data.

Bearing: fit-derived intervals are best read as lower bounds on real uncertainty, most of all for a single experiment.

## No detected heat: what range of interactions can still hide behind a flat isotherm
id: pb-no-binding-hidden-range
kind: playbook
topics: negative result, sensitivity limits
basis: mixed
status: draft
matches: no binding detected, flat thermogram, negative ITC result, cannot detect, silent ITC, null result sensitivity, absence of heat, no signal
cite: hierarchical_idp_2025, oep21_2023

Situation: injection heats are indistinguishable from the matched control.

What can still be present: (a) binding with ΔH close to zero at this temperature, since ΔH(T) crosses zero where ΔCp is nonzero; (b) binding too weak for the concentrations used (Kd well above the cell concentration and final ligand concentration, so the fraction bound per injection is tiny); (c) inactive, aggregated or already liganded protein; (d) a missing cofactor, ion or pH requirement; (e) cancellation between binding heat and protonation or other linked heat in the chosen buffer.

What it does establish: under the tested conditions no heat-associated binding was detected at the sensitivity of the experiment.

Most informative follow-ups: a different temperature (a 10 K change shifts ΔH by about 10 × ΔCp), a buffer with a different ionization enthalpy, higher concentrations, or an orthogonal method that does not rely on heat (NMR, SPR/BLI, thermal shift).

## Choosing follow-up experiments: matching each proposal to the specific uncertainty it would resolve
id: pb-followup-map
kind: playbook
topics: experiment planning, next steps
basis: general
status: draft
matches: what experiment next, recommendations, follow-up, how to improve the experiment, improve the titration, resolve uncertainty, redesign, repeat experiment

Uncertainty and the experiment that addresses it:
- Kd undetermined at very high c: lower the cell concentration, or use displacement against a weaker competitor, or an orthogonal kinetic method.
- n and ΔH inseparable at low c: raise concentrations and final molar ratio, or fix n from an independent concentration and activity check, or fit jointly with a complementary titration.
- Suspected buffer ionization or proton linkage: repeat in a buffer with very different ionization enthalpy at the same pH.
- Offset or late heats unexplained: ligand-into-buffer (and, if relevant, buffer-into-macromolecule) control with the same injection scheme.
- n unexpectedly low or high: confirm concentrations by a second method, check activity and aggregation, verify ligand purity.
- Weak heats: raise reacting material per injection or change temperature.
- ΔCp or temperature-derived quantities: add temperatures across a wider span with identical buffer composition.
- Mechanistic claim: mutants, salt or pH series, or structural and spectroscopic evidence.

Replication is a reasonable early step when variability is unknown, but a proposal that cannot name the doubt it would remove is usually not worth adding.

## Comparing two datasets (mutant versus wild type, ligand series, conditions): what difference is meaningful
id: pb-comparing-conditions
kind: playbook
topics: comparisons, ΔΔG, ligand series, mutants
basis: mixed
status: draft
matches: compare mutants, wild type versus mutant, ligand series comparison, fold change in Kd, ΔΔG, significance of difference, SAR, structure activity thermodynamics, comparing affinities
cite: olsson_compensation_2011, koch_aldose_reductase_mutants_2011

Key points: Compare ΔG (or log Kd) first; it is the best-determined quantity. At 25 °C a tenfold change in Kd is 5.7 kJ/mol, a factor of two is 1.7 kJ/mol, and 1 kJ/mol is a factor of about 1.5. Typical independent replicates scatter by 1.2- to 2-fold in Kd, so differences below about two-fold are within ordinary variation unless replicates are tight.

Same buffer, pH, temperature and instrument, ideally run in the same session, make a comparison meaningful; otherwise differences mix intrinsic change with condition change.

ΔH and −TΔS are anticorrelated by construction (−TΔS = ΔG − ΔH), so an error in ΔH appears as an opposite error in −TΔS. Report ΔΔH with its own uncertainty and treat apparent compensation cautiously.

Errors propagate in quadrature: σ(ΔΔG) ≈ √(σ₁² + σ₂²), and a double-mutant coupling term built from four measurements has roughly twice the single-measurement error.

Mutations and modifications can change folding, oligomeric state or binding mode, so a change in Kd is interpretable mechanistically only with evidence that the construct is comparably folded.

## Reconciling ITC with SPR, BLI, MST, fluorescence anisotropy or NMR affinities
id: pb-orthogonal-agreement
kind: playbook
topics: orthogonal methods, method comparison
basis: mixed
status: draft
matches: ITC versus SPR, discrepancy between methods, orthogonal validation, Kd disagrees, BLI MST comparison, cross validation, method comparison, agreement between techniques
cite: day_caii_multimethod_2002, papalia_abrf_mirg_2004, upadhyay_itc_spr_2024

Key points: Agreement within about 2- to 3-fold between methods is generally good. ITC is solution-phase, label-free and equilibrium-based, so it is exposed to concentration and activity errors and to c-window limits; SPR and BLI immobilize one partner and are exposed to avidity, mass transport, rebinding and surface heterogeneity; MST and fluorescence methods may need labels or intrinsic fluorophores and are exposed to label and buffer effects; NMR chemical-shift titrations report site-specific exchange.

Differences larger than about 5- to 10-fold usually trace to condition mismatches (temperature, buffer, pH, salt, additives, tags or constructs), immobilization, labeling, active fractions, or an extreme c-value in one method.

Each method is strongest for different quantities: ITC for ΔH, stoichiometry and thermodynamic signature; SPR/BLI for kinetics (kon, koff, residence time); NMR and structure for site assignment and dynamics.

Agreement from methods with different systematic limitations raises confidence more than agreement between similar methods.

## What thermodynamic signatures can and cannot say about binding mechanism
id: pb-mechanistic-claims-limits
kind: playbook
topics: mechanism, interpretation limits
basis: mixed
status: draft
matches: hydrophobic interaction claim, hydrogen bond from enthalpy, mechanism from ΔH, entropy driven means, signature interpretation, structural interpretation of thermodynamics, overinterpreting ΔH ΔS
cite: nature_primer_2023, galectin_jacsau_2021

Key points: ΔH, −TΔS and ΔCp are net quantities that include protein, ligand and solvent reorganization, linked protonation and ion exchange, and conformational changes. Large opposing contributions routinely cancel in water, so each term alone is a weak readout of any single interaction.

What can be said with reasonable support: sign and size of ΔH and −TΔS describe the net energetic partition under the stated conditions; a negative ΔCp is consistent with net burial of nonpolar surface or coupled ordering; salt dependence supports an electrostatic contribution; buffer dependence supports proton linkage.

What needs more evidence: assigning enthalpy to specific hydrogen bonds, assigning entropy to the hydrophobic effect, or reading residue counts from ΔCp. Series of related ligands or mutants, variation experiments, and structures are the usual supporting evidence.
