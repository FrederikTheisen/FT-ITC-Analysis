# Structuring methods — research plan

Status: research and planning (created 2026-10-12; evidence and scientific recommendation updated 2026-10-16; settled scope unchanged). Related issues: [ITC-068](ISSUES.md#itc-068---structuring-evaluation-temperature-options-need-scientific-review) and [ITC-072](ISSUES.md#itc-072---structuring-with-an-explicit-polar-hydration-entropy-term). Creating this document does not approve a scientific method change or application implementation.

This is the shared plan for iterative work across agents. Record research, decisions, and handoff state here; keep the tracker entries concise. The [2026-10-07 investigation](../Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/README.md) initially prioritized **Option A**, a common reference temperature with matched calibration. Following the user’s 2026-10-15 direction, **A and Option C (explicit polar hydration) now have equal research priority**. The new evidence supports continued comparison; it does not select either method for implementation.

**Current recommendation (section 5, for user review).** A common 25 °C convention is admissible and useful for comparing interactions. Its benefit is a common convention, not precision: with paired fitted inputs, all candidate estimators have practically the same fitted-input uncertainty. Option C remains the conditional research preference for one convention covering both interaction classes. D'Aquino's primary basis for the effective ≈ 335 K parameter has now been recovered; its independent uncertainty and transfer to the 2021 coefficient/calibration bundle remain unestablished. The recovered coefficient covariance also requires revising the earlier uncertainty argument (section 4.8). Option A at 25 °C is practically equivalent for globular interactions, but its polar-heat-capacity transfer assumption fails under the ID ratio. No option has been shown to be more accurate.

## 1. Problem and scope

Structuring analysis estimates how much conformational ordering accompanies binding by subtracting model-estimated hydration and rotational/translational entropy contributions from the binding entropy. The remaining entropy is converted into an estimated number of residues folding upon binding using a calibrated per-residue constant. These are conditional model estimates, not direct structural measurements.

**Agreed objective (2026-10-15): produce a scientific recommendation for estimating residues folding upon binding at a declared common temperature, with 25 °C as the preferred default.** The purpose is comparison between interactions and assessment of whether this avoids uncertainty associated with an interaction-specific isoentropic temperature, `T_S`. Both globular and intrinsically disordered interaction cases remain in scope. Keep the residue-count abstraction as the main interpretation; it remains an approximate thermodynamic estimate.

The evaluation temperature and calibration must describe the same convention. Otherwise a change of temperature can change the estimated residue count without a corresponding change in structural ordering. The recommendation must distinguish a common comparison temperature, uncertainty in the final estimate, and physical accuracy. A fixed temperature is known by convention; the thermodynamics evaluated there remain uncertain. Datasets that do not include or bracket 25 °C are included, with extrapolation and its uncertainty assessed explicitly.

The plan covers:

- ITC-068: the isoentropic, mean-temperature, and reference-temperature conventions, including a reference-temperature convention with matched calibration.
- ITC-072: explicit polar hydration as a distinct alternative, including its additional assumptions and calibration.
- The evidence, unresolved scientific decisions, and validation requirements needed to decide whether either proposal should proceed.

Application implementation is a later stage requiring an agreed method and scope. Selecting or rebuilding the calibration dataset is deferred at the user's request; checking that an exploratory calculation uses a compatible temperature/conversion convention remains necessary. Neither a multi-method product nor a claim of improved structural accuracy is the agreed deliverable. Do not turn adjacent research observations into additional implementation tasks without agreement.

## 2. Current application baseline

- `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs` contains `FTSRMethod`, `SRTempMode`, and `SROutput`.
- Core `SRTempMode` retains Isoentropic point, Mean temperature, and Reference temperature for research and saved-output compatibility. Since the interim restriction agreed on 2026-10-13, both desktop apps expose only a fixed Isoentropic point row and explicitly use that mode for new runs. `FTSRMethod` still uses one `PerResidueEntropyLoss` value, approximately −23.96662 J/(mol·K) per residue, across those modes; its equations and calibration are unchanged.
- ITC-068 records that the published Spolar–Record convention and its Theisen et al. (JACS 2021) adaptation evaluate at the isoentropic temperature, where binding entropy is zero. Mean and reference temperatures are FT-ITC additions. Whether and how those additions are justified remains the subject of this review.
- ITC-069 and ITC-071 are resolved: completed outputs use their saved evaluation temperature and share temperature formatting across desktop views and reports. Retain those contracts.
- ITC-070 is closed as not an issue: the central evaluation temperature used to scale entropy contributions is treated as fixed. This plan does not reopen that decision.
- Report presentation is mapped under X02 in `Documentation/ANALYSIS_REPORT_MAP.md`. The manual describes the current structuring workflow in `Documentation/UserManual/pages/08-results-advanced-analysis.md`.

These baseline observations come from the current source and tracker. No runtime verification was performed when creating this plan.

## 3. Methods and what the data identify

Temperature-series ITC constrains total binding thermodynamics. Separating hydration from conformational entropy requires additional assumptions or independent observations. A good fit, exact entropy closure, or a temperature-invariant answer does not identify that separation. Under constant heat capacity, `ΔS(T) = ΔS(T₀) + ΔCp·ln(T/T₀)`; the resulting `T_S` is an entropy zero of the fitted curve, not a structural transition temperature. Its extrapolation is unstable when `ΔCp` is small.

Symbols: `R` = effective residues ordering upon binding; `c` = signed multiplier assigning `c·ΔCp` to nonpolar hydration; `P = (1−c)·ΔCp` = assigned polar heat capacity; `ΔS_rt` = rotational/translational entropy loss; `s(T)` = calibration remainder per residue. Temperatures in logarithms are in kelvin. Because polar and nonpolar heat capacities have opposite signs, `c` can exceed one: it is not a fraction constrained to 0–1.

### Published isoentropic comparison

The revised isoentropic baseline is

`R_iso = [−c·ΔCp·ln(T_S/386 K) − ΔS_rt] / s_iso`.

The current FT-ITC/Theisen adaptation uses `s_iso = −23.96662 J/(mol·K)` per residue and `ΔS_rt = −110 J/(mol·K)` per bimolecular association. Original 1994 SR and the 2021 revision have different coefficient/calibration bundles; do not describe current-constant recalculations as reproductions of the original paper's numerical estimates. The original method's interpretation already depends on folding dominating the residual entropy. [Spolar and Record 1994](https://doi.org/10.1126/science.8303294); [Theisen et al. 2021](https://doi.org/10.1021/jacs.1c04214).

### Option A — common temperature with matched calibration

`R_A(T) = [ΔS(T) − c·ΔCp·ln(T/386 K) − ΔS_rt] / s(T)`.

The source-workbook audit gives the following exploratory relationships in J/(mol·K) per residue:

- 12-protein set: `s(T) = −22.615976301193605 + 20.958481771939795·ln(T/298.15)`.
- 46-row set: `s(T) = −22.042594171476615 + 18.30026995736949·ln(T/298.15)`.

These digits make the research reproducible; they are not scientific precision or an approved implementation specification. A supplies a declared common comparison temperature. Interpreting its remainder as a structural count assumes that the assigned polar heat capacity per ordering residue transfers from the folding calibration to binding. Matching temperatures alone does not establish that assumption or justify a Mean temperature mode.

### Option C — explicit polar hydration

`ΔS_polar(T) = P·ln(T/T_p*)`, with an exploratory effective polar reference `T_p* ≈ 335 K`.

`R_C = [−ΔCp·ln(T_S/T_eff) − ΔS_rt] / s(T_p*)`, where `ln(T_eff) = c·ln(386) + (1−c)·ln(T_p*)`.

At 335 K the 12-protein denominator is −20.17360443737866 J/(mol·K) per residue. `T_eff` is about 405 K for the globular ratio and 424 K for the ID ratio; it is an algebraic reference, not a physical transition. C assigns all measured `ΔCp` to the two hydration terms and takes conformational and rotational/translational entropy as temperature-independent. Those assumptions produce its temperature invariance. Contributions from linked protonation, retained water, changing conformational populations or other processes require separate treatment before making this assignment.

### A and C are related conventions

For `s(T) = s(T_p*) + p̄·ln(T/T_p*)`, direct algebra gives

`R_A(T_p*) = R_C`

`R_A(T) − R_C = [P − p̄·R_C]·ln(T/T_p*) / s(T)`

`dR_A/dln(T) = [P − p̄·R_A] / s(T)`.

Thus A and C coincide at the chosen polar reference when calibration is matched. Agreement between them is not independent corroboration. Their difference away from that reference diagnoses the assumed polar heat capacity per residue. These identities were independently calculated without importing application code; that verifies the equations, not the physical model.

### One family: where polar hydration entropy is assumed to vanish

The three estimators form one family. With `P = (1−c)·ΔCp` and matched calibration `s(T)`,

`R_m·s_m = R_C·s(T_p*) + P·ln(T_m/T_p*)`,

where `T_m = T_S` for the isoentropic estimator, `T_ref` for A and `T_p*` for C. Thus:

- **A at `T_ref`** is C with its polar reference set to `T_ref`.
- **The isoentropic estimator** is C with the polar reference set to each system's own `T_S`. Its constant `s_iso` equals the matched `s(T)` at about 279.5 K. Its calibration likewise uses each folding protein's own `T_S`.

All three use the same fitted information. They differ in one physical assumption: the temperature at which the polar share of the hydration entropy vanishes, or is cancelled by the folding calibration. That identity was checked to 10⁻¹⁴ residues on every dataset in section 4.6.

Each estimator is also a scaled binding entropy extrapolated along the fitted curve:

- `R_iso = [c·ΔS(386 K) − ΔS_rt]/s_iso`.
- `R_A25 = [ΔS(T*) − ΔS_rt]/s(298.15 K)`, with `T* = 298.15·(386/298.15)^c` = 421 K (globular ratio) or 457 K (ID ratio).
- `R_C = [ΔS(T_eff) − ΔS_rt]/s(T_p*)`, with `T_eff` = 405/424 K.

A 25 °C reference therefore does not remove extrapolation. Every member evaluates the fitted entropy curve roughly 80–160 K above typical measurements, so every member is dominated by `ΔCp`. These virtual temperatures are algebraic, not physical transitions.

### Fixed temperature and the uncertainty in `T_S`

**A common-temperature calculation is within the thermodynamic framework.** The original paper gives hydration entropy as a function of temperature before specializing its residue estimator to `T_S`. The present estimate already uses `T_S`; the missing common comparison temperature is a different issue. `T_S` is a fitted entropy zero, not an arbitrarily selected reference temperature. Applying the decomposition at 25 °C retains the nonzero fitted binding entropy there and requires a declared, compatible residue conversion. It is an extension of the published isoentropic estimation convention, not merely a new temperature label. [Spolar and Record 1994, Eqs. 2–6 and note 40](https://doi.org/10.1126/science.8303294).

A fresh primary precedent explicitly applies the SR hydration/residual decomposition at 298 K. That paper interprets the remainder mainly through water uptake; it supports fixed-temperature decomposition, not a validated fixed-25 °C residue calibration. Its relevant primary passage was accessible in online indexed article text; direct archive access failed. [Xavier et al. 1997, Eqs. 9–10](https://doi.org/10.1016/S0006-3495(97)78242-0).

For the constant-`ΔCp` model, write `S₀ = ΔS(T₀)` and `T₀ = 298.15 K`. Then

`ln(T_S/T₀) = −S₀/ΔCp`

`R_iso = [c·S₀ − c·ΔCp·ln(T₀/386 K) − ΔS_rt] / s_iso`.

The second expression is exactly the same isoentropic estimator for nonzero `ΔCp`, without computing an entropy-zero temperature. A very uncertain `T_S` can therefore coexist with a reasonably constrained residue estimate: the fitted entropy and heat capacity vary together. Its apparent singularity as `ΔCp` approaches zero cancels in this expression. At exactly zero heat capacity the entropy zero is absent when `S₀ ≠ 0` and nonunique when `S₀ = 0`. The finite algebraic extension does not establish physical validity when heat capacity or the hydration assumptions are poorly supported.

At the same reference, A uses coefficient **1** on `S₀`, rather than **c**, and the matched `s(T₀)` denominator. Consequently A changes the estimation convention as well as the reference temperature. C can likewise be calculated directly from `S₀` and `ΔCp`, subtracting both hydration terms at 25 °C; its approximately 335 K polar reference is a model parameter, not the temperature at which binding must be evaluated. C's residue estimate is temperature-independent under its assumptions. Neither its invariance nor A's temperature dependence independently establishes the actual temperature dependence of structural ordering.

Compare uncertainty using joint fitted parameter samples or their covariance (how fitted quantities vary together), with the same underlying input information for every method. Do not treat `T_S` and `ΔCp` as independent measurements or interpret the removal of a marginal `T_S` error bar as increased physical certainty. The present `FTSRMethod` draws separate marginal values for temperature, entropy and heat capacity during its uncertainty calculation; linked `T_S` was initially derived from joint replicate curves, but their pairing is not retained in those draws. This is a static source observation to account for in the research comparison, not an authorized uncertainty implementation change. It is separate from the closed ITC-070 decision about scaling saved entropies by a fixed central temperature.

The joint comparison in section 4.6 has since tested precision on the **final residue estimate**; section 5 gives the resulting recommendation. A broad recalibration project was not needed to settle this feasibility question. The existing matched 25 °C calibration is an exploratory input, not an approved replacement. Extrapolation to 25 °C requires checking the temperature model and fitted uncertainty; a chosen reference alone cannot make an extrapolated estimate well determined.

## 4. Evidence reviewed through 2026-10-16

The [2026-10-07 investigation](../Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/README.md) remains a dated exploratory record. This section supersedes its source attribution and interpretation where specified below. Reproducible inputs, calculations, source locations and access limits are in the [2026-10-15 research evidence](../Artifacts/Investigations/Structuring-Methods-2026-10-15/README.md).

### 4.1 Calibration and archived comparison audit

The first pass reproduced the archived calculations and checked the workbooks and 2021 supporting information. The follow-up searched online for additional primary material; it did not restrict the evidence to those files.

| Calibration | Own-`T_S` mean | Mean at 298.15 K | Mean at 335 K | Minimum-spread temperature |
|---|---:|---:|---:|---:|
| Source 12 proteins | −23.96662 | −22.61598 | −20.17360 | 342.04 K |
| Source 46 rows | −23.18046 | −22.04259 | −19.90999 | 332.75 K |

Entropy units are J/(mol·K) per residue. Small differences from the archive arise from rounded heat capacities and Papain nonpolar area 12760 versus the workbook's 12755 Å². The 12-protein workbook uses 4.186 J/cal; the independent binding comparisons below use 4.184 J/cal. The mismatch is preserved and disclosed for reproduction, not proposed as a future units convention.

All 46 surface-area/heat-capacity entries overlap the 2021 coefficient-fitting dataset; 46 rows contain 43 protein labels and share families with the 12-protein set. Their entropy-at-60 °C source provenance remains incomplete. These are calibration/sensitivity sets, not two independent validations. The workbook's `dS_total(60)` cells contain `TΔS`; its `T_S` formula divides by temperature correctly.

For individual calibration curves `s_i(T)=a_i+p_i·ln(T/T₀)`, the variance minimum satisfies `ln(T_min/T₀)=−Cov(a,p)/Var(p)`. Calling that minimum a physical polar entropy zero requires a further assumption about the covariance of true conformational entropy with polar heat capacity. Bootstrap ranges describe sampling under the calibration data; they do not test this assumption. The correlation with each protein's own `T_S` also shares `T_S` between the axes.

The archived 15-row RMS log errors reproduce as 0.3641 (baseline), 0.3433 (A25) and 0.3350 (C335). Paired bootstrap differences versus baseline include zero: A −0.0208 [−0.0674, +0.0775], C −0.0290 [−0.0715, +0.0264]. Two ambiguous targets, S-peptide and melittin, contribute about 90% of baseline squared log error; the set also mixes interaction classes, related systems and lower-bound targets. It establishes neither superiority nor formal non-inferiority. The earlier 19.5% versus 12.8% method-uncertainty comparison also reproduces, but omits fitted-input uncertainty and does not demonstrate coverage.

Two archived interpretations need correction. First, at a fixed numerator the unmatched isoentropic denominator gives about **5.6% fewer**, not 6% more, residues than the matched 25 °C denominator. Second, a larger calibration spread after substituting 176 K into this coefficient bundle does not disprove the distinct hydration model using that reference.

### 4.2 What supports the 335 K polar reference

**Primary effective parameter recovered on 2026-10-16; uncertainty and transfer remain open.** Freire's author-written 2001 chapter, pp. 47–48, Eq. 7, describes approximately 335.15 K as an effective peptide parameter and attributes it to D'Aquino et al. (1996). The supplied full text now confirms that attribution: D'Aquino Fig. 6A, p. 153, explicitly uses `−0.26·ΔASA_pol·ln(T/335.15)` in cal/(mol·K). Its preceding entropy balance and binding examples constrain the polar/backbone decomposition but do not report a fitted confidence interval for the zero. Section 4.8 gives the recovered evidence and coefficient-bundle limits. This supersedes the previous abstract-only access and unrecovered-origin statements; the extra decimals do not establish precision. [Freire 2001](https://doi.org/10.1385/1-59259-193-0:037); [D'Aquino et al. 1996](https://pubmed.ncbi.nlm.nih.gov/8811731/).

**Correct the citation chain.** Full-text review of Bakk et al. (2002) found hydration heat-capacity modeling, not a 335 K entropy-zero determination or the cited D'Aquino/Freire attribution. Heat capacity determines an entropy derivative; an integration constant is also needed to locate its zero. The archive's attribution through this paper should not be repeated. [Bakk et al. 2002](https://doi.org/10.1016/S0006-3495(02)75433-7).

**Compare complete parameter sets.** A later application pairs 385.15/335.15 K with nonpolar/polar coefficients +0.45/−0.26 cal/(mol·K·Å²). Caro et al. use 385/176 K with a different fitted coefficient bundle. Neither validates inserting its polar reference into the current Theisen coefficients and calibration. Surface definitions, transfer conventions, signed area changes, hydroxyl treatment and conformational calibration must be matched. With signed heat-capacity components, hydration entropies are added; polar burial below 335 K gives a negative polar entropy contribution. [Bhatt et al. 2016, Eq. 6](https://doi.org/10.1016/j.bpj.2016.05.008); [Caro et al. 2017](https://doi.org/10.1073/pnas.1621154114).

**Newer use is not independent validation.** Streit et al. (2024) fix 385/335 K in their surface-area model and separately estimate total hydration entropy with explicit-solvent simulations. That comparison supports studying hydration, but does not isolate or fit the polar zero. Its printed Eqs. S25/S28 also require a sign clarification: S25 defines a signed polar heat capacity, while S28 subtracts it. Taken literally, their derivative would not equal the total heat capacity in S25. Do not copy that expression without resolving the convention. [Streit et al. 2024, Methods Eqs. S25–S30](https://doi.org/10.1038/s41586-024-07784-4).

The current conclusion is to retain ~335 K as a literature-motivated **model choice** for C, with its transferability and uncertainty open. Neither minimum calibration spread nor temperature invariance supplies the missing validation.

### 4.3 Independent observations and A/C comparisons

All calculations below fix the current nonpolar/polar coefficients at +1.3721067124117/−0.590884521921104 J/(mol·K·Å²), use `c=1.3406224` for the globular-ratio scenario or `c=1.6558442` for the ID-ratio scenario, and use the source-12 calibration in section 3. A25/A37 mean 298.15/310.15 K; C335/C335.15 change both the polar reference and its matched denominator. No reference temperature or class was selected by minimizing these benchmark errors. Values are conditional residue equivalents, not precise structural counts.

| System and input convention | Baseline | A25 | A37 | C335 | C335.15 |
|---|---:|---:|---:|---:|---:|
| RPP WT, reported corrected inputs; globular ratio | 49.72 | 54.80 | 54.22 | 52.97 | 52.96 |
| RPP A14V, reported corrected inputs; globular ratio | 42.64 | 46.22 | 45.79 | 44.86 | 44.85 |
| RPP WT, same inputs; ID ratio | 62.49 | 70.28 | 67.84 | 62.49 | 62.46 |
| RPP A14V, same inputs; ID ratio | 53.74 | 58.94 | 56.98 | 52.69 | 52.66 |
| POSH–Rac1, published rounded summary; ID ratio | 39.65 | 44.41 | 42.77 | 39.18 | 39.16 |
| POSH–Rac1, rounded SI diagnostic refit; ID ratio | 39.47 | 44.21 | 42.58 | 39.00 | 38.98 |
| POSH–Rac1b (new 2026 study), rounded SI diagnostic refit; ID ratio | 12.21 | 14.66 | 13.83 | 12.00 | 11.99 |

**RPP21–RPP29:** Xu et al. report corrected `ΔCp/T_S` of −1028 cal/(mol·K)/308 K for WT and −845/304 for A14V, compared with NMR ordering proxies of 50 and 40 residues. Their original SR estimates were 47/40; the table above deliberately recalculates with current constants. Correction assumptions include proton linkage and retained water; mutant corrections reuse WT assumptions. Do not combine a corrected heat capacity with uncorrected entropy. Both class scenarios are retained: choosing the better count agreement would be circular. Within a fixed class C is closer to the NMR proxies than A, but the revised baseline is also competitive, and class choice changes results more than A versus C. [Xu et al. 2012](https://doi.org/10.1021/bi201674d).

**POSH–Rac1:** fresh publisher Source Data and SI were inspected. The summary inputs are approximately −2800 J/(mol·K), 307.1 K. SI Table 1 supplies paired, rounded `ΔH/Kd` values at 5, 10, 15 and 35 °C; no unrounded temperature-fit parameter table was located in the workbook. Its approximately 40-residue result and entropy-decomposition bars are themselves method-derived and cannot be independent targets. [Kjaer et al. 2025, Methods and SI Table 1](https://doi.org/10.1038/s41467-025-66420-5).

**New POSH–Rac1b comparison:** the fresh 2026 SI has duplicate values at only 5/35 °C and does not report `T_S`. The diagnostic refit gives −1171.52 J/(mol·K), 313.635 K; the latter is our calculated value. NMR supports partial POSH ordering, with one recognition element remaining dynamic, and receptor regions also change. This is a useful related perturbation, not a second independent protein family. [Kjaer et al. 2026, SI Table S1](https://doi.org/10.1021/jacs.6c04072).

A separate count of deposited, positive-occupancy Cα positions gives **44** modeled POSH residues in the Rac1 fusion and **26** in the Rac1b fusion. A25 is close to the former; all methods give only 12–15 for the latter. However, fused crystallographic constructs, partial solution ordering, initial-state structure and ordering in the receptor prevent equating these counts with whole-complex entropy. The two structures are useful checks on interpretation, not exact targets for an accuracy score. [PDB 9RFF](https://www.rcsb.org/structure/9RFF); [PDB 9TJY](https://www.rcsb.org/structure/9TJY).

The diagnostic refits first fit `H(T)` by equal-weight least squares, then fit entropy at 298.15 K to `G=RT·ln(Kd/1 M)` while holding that enthalpy/heat-capacity curve fixed. This is a declared sensitivity analysis, not a joint fit with experimental covariance or a reanalysis of raw isotherms. For Rac1 it gives −2788.16 J/(mol·K), 307.086 K. Omitting 35 °C changes the predictions to about 24–29 residues; the observation remains in the main analysis. Enumerating the paired technical duplicates gives A25 extrema 41.98–46.45 and C335 36.93–41.08, **not confidence intervals**. Under the fitted constant-`ΔCp` model and comparable experimental conditions, Rac1b's enthalpy curve predicts `Kd(35)/Kd(5)≈0.840`, versus the measured geometric-mean ratio ≈0.449. Investigate model assumptions, experimental conditions and covariance before scoring accuracy; this does not establish inconsistent underlying thermodynamics. Changing 335 to 335.15 K affects these estimates by only 0.01–0.02 residues.

### 4.4 Additional fresh material and benchmark eligibility

Higbee et al. (2024) provide a new p53/MDM2/MDMX family with temperature-series ITC and an NMR relaxation-based estimate of a **relative** conformational entropy change. For P27A minus WT p53, the NMR-derived `−TΔΔS` at 298.15 K is −1.43 kcal/mol. Total ITC differences for the 17–35 peptides are −2.22 kcal/mol with MDM2(25–109) and −1.86 kcal/mol with MDMX(23–111), using the same subtraction order. The NMR conversion assumes similar bound-state order parameters and uses a longer unbound fragment. Their SR-derived residue counts are not independent targets. The main text was retrieved afresh; its detailed heat-capacity supplement was not obtained in this pass, so no unsupported A/C numerical score is added. This family is a priority for testing relative ordering and receptor compensation rather than assigning peptide length as the truth. [Higbee et al. 2024, Table 1 and conformational-entropy analysis](https://doi.org/10.1016/j.jmb.2024.168626).

The source ledger also records newly checked nonadditivity work and modern explicit-solvent entropy approaches. These test assumptions or suggest independent observables; they are not added as residue-count benchmarks without compatible thermodynamic and structural endpoints.

### 4.5 Interim conclusion before the joint comparison

This was the conclusion before section 4.6; section 5 now gives the recommendation. Neither A nor C was selected for implementation. A offered a reproducible common-temperature convention but retained a polar-heat-capacity transfer assumption. C made that contribution explicit and is algebraically invariant, but its effective polar reference and complete calibration require further justification. RPP class sensitivity, the POSH/Rac1b contrast and the p53 relative-entropy endpoint showed why selecting a winner from mixed structural counts would be premature.

### 4.6 Joint fitted-input comparison at 25 °C

Reproducible inputs, scripts, data notes and limits are in the [joint comparison evidence](../Artifacts/Investigations/Structuring-Methods-2026-10-15/joint_25C/README.md). The examined revision was HEAD `2a24852d`; `SpolarRecordAnalysis.cs` is unchanged since `f1b67957`.

**Inputs.** There are 50 per-temperature datasets. Each is fitted jointly to `ΔH` and `ΔG` with constant `ΔCp`, giving `S₀` at 298.15 K.

- **Nominally ID:**
  - Four DREB2A–RCD1 fragments, from the user's per-experiment files: 283–308 K, 17–23 experiments each. These files reproduce the 2021 summaries.
  - POSH–Rac1 and POSH–Rac1b.
  - Twelve p53-peptide complexes with MDM2(17–125) or MDMX(23–111), from the per-run tables in Higbee et al. 2024 SI Appendix A: 283–308 K, 21–24 runs each.
  - Seventeen further multi-temperature ID sets from published tables:
    - RPP21–RPP29, 283–328 K ([Xu et al. 2012, Table 1](https://doi.org/10.1021/bi201674d); per-temperature `ΔH` not proton-corrected).
    - Six CID–NCBD variants at pH 6.5 and 7.5, 288–308 K ([Jemth et al. 2018, Table S1](https://doi.org/10.1126/sciadv.aau4130)).
    - Three DREB2A–MED25 constructs ([Theisen et al. 2024, Source Data](https://doi.org/10.1038/s41467-024-44859-2)).
    - CSL–KyoT2 ([Collins et al. 2014](https://doi.org/10.1016/j.str.2013.10.010)).
    - Three Taz2–phospho-p53 sets ([Feng et al. 2009](https://doi.org/10.1016/j.str.2008.12.009)).
    - Two Sos–Drk SH3 sets ([Thole et al. 2023](https://doi.org/10.1016/j.jbc.2023.102984)).
    - Warm-only CaM–calcineurin, 303–308 K, no printed uncertainties ([Majava and Kursula 2009](https://doi.org/10.1371/journal.pone.0005402)).
    - The declared inclusion rule was every system with at least two temperatures not already represented. Three single-temperature p27 sets were not fittable.
- **Nominally globular or ambiguous:**
  - Three HyHEL-5 Fab–lysozyme sets ([Wibbenmeyer et al. 1999, Table I](https://doi.org/10.1074/jbc.274.38.26838)).
  - Eleven TEM-1–BLIP sets ([Wang et al. 2007, Table 1](https://doi.org/10.1074/jbc.M611548200)). Three of these (D49A, W150A, R160A) were measured only at 6–15 °C.
  - TN1 Fab–hTPO ([Arai et al. 2016, Table 1](https://doi.org/10.1002/pro.2985)), whose class is ambiguous.

Every dataset is evaluated under both class ratios. Central values are original-data fits. Fitted-input uncertainty comes from the fit covariance: residual-based for replicate-level data, and reported SDs with `χ²/dof` scaling for per-temperature summary tables.

Two data notes:

- One DREB2A 255–272 row has `ΔG` inconsistent with `ΔH − TΔS` by 5.9 kJ/mol. Both readings were analyzed; the effect is ≤ 0.3 residues.
- Four Higbee A4 runs are printed at 288.3 K inside the 283.15 K block. Their `ΔG/Kd` pairs imply 283.1–283.4 K, so they are assigned 283.15 K.

Literature rows were extracted by research subagents from full-text tables or SI and spot-checked by the parent.

**Fitted-input precision is essentially method-independent.** The table gives residues per J/(mol·K) of `ΔCp` error when the entropy at the data centre `T_c` is well determined:

| Data centre | Globular: iso | A25 | C | ID: iso | A25 | C |
|---|---:|---:|---:|---:|---:|---:|
| 278 K | 0.0183 | 0.0184 | 0.0186 | 0.0226 | 0.0220 | 0.0209 |
| 298 K | 0.0144 | 0.0153 | 0.0152 | 0.0178 | 0.0189 | 0.0174 |
| 318 K | 0.0108 | 0.0124 | 0.0120 | 0.0134 | 0.0160 | 0.0142 |

Across the 50 datasets, the ratio of the largest to the smallest method SD is ≤ 1.11. The exception is TEM-1/BLIP wild type under the ID ratio (1.21), which has only one affinity point; there the isoentropic estimator's larger `S₀` weight (`c` rather than 1) matters.

An independent verifier reproduced the fitted values and SDs to displayed precision. It found the same ≤ 1.08 ratio under four degrees-of-freedom conventions, and under `ΔH/ΔG` error correlations of ±0.5. Those correlations moved central values by about ±6%.

| Dataset (proxy) | T range, n | `ΔCp`, J/(mol·K) | `T_S`, K | Iso | A25 | C |
|---|---|---:|---:|---:|---:|---:|
| *ID ratio* | | | | | | |
| DREB2A 255–272 (18) | 286–303 K, 19 | −1305 ± 229 | 295.7 ± 1.0 | 19.4 ± 4.3 | 20.3 ± 4.5 | 17.8 ± 4.2 |
| DREB2A 250–287 (23) | 286–303 K, 17 | −1441 ± 119 | 297.3 ± 0.6 | 21.4 ± 2.2 | 22.6 ± 2.3 | 19.8 ± 2.2 |
| DREB2A 243–272 (30) | 286–308 K, 20 | −1793 ± 132 | 286.8 ± 0.8 | 32.2 ± 2.4 | 32.1 ± 2.5 | 29.2 ± 2.3 |
| DREB2A 234–287 (30) | 283–306 K, 23 | −1755 ± 99 | 287.4 ± 0.5 | 31.2 ± 1.9 | 31.2 ± 2.0 | 28.3 ± 1.8 |
| POSH–Rac1 | 278–308 K, 8 | −2795 ± 105 | 307.3 ± 0.8 | 39.4 ± 2.1 | 44.2 ± 2.1 | 39.0 ± 2.0 |
| POSH–Rac1b | 278/308 K, 4 | −1172 ± 48 | 313.7 ± 1.2 | 12.2 ± 0.9 | 14.7 ± 0.9 | 12.0 ± 0.9 |
| p53 16–29 / MDM2 | 283–308 K, 23 | −2039 ± 47 | 285.0 ± 0.3 | 38.1 ± 0.9 | 37.7 ± 0.9 | 34.6 ± 0.8 |
| p53 17–35 / MDMX | 283–308 K, 24 | −2000 ± 43 | 292.7 ± 0.2 | 33.6 ± 0.8 | 34.6 ± 0.8 | 31.2 ± 0.8 |
| *Globular ratio* | | | | | | |
| HyHEL-5 E50HQ / HEL | 284–303 K, 4 | −1075 ± 155 | 269 ± 3 | 17.1 ± 2.4 | 16.5 ± 2.5 | 16.4 ± 2.5 |
| TEM-1 / BLIP K74A | 279–303 K, 3 | −2135 ± 31 | 305.0 ± 0.3 | 23.6 ± 0.5 | 25.7 ± 0.5 | 24.6 ± 0.5 |
| TEM-1 / BLIP D49A (6–15 °C only) | 279–288 K, 3 | −1132 ± 207 | 356 ± 15 | 0.6 ± 3.6 | 3.6 ± 3.6 | 1.9 ± 3.7 |

Values are residue equivalents ± fitted-input SD. Notes on the table:

- **POSH SDs are uncertain.** The 4–8 rounded points make them depend on the resampling scheme: 0.8–4.1 residues for Rac1, against the residual-based value shown.
- **Large globular counts.** The globular complexes TEM-1–BLIP and HyHEL-5 receive 12–35 residue equivalents, although TEM-1–BLIP is a rigid interface with modest loop rearrangement. Their measured `ΔCp` exceeds surface-area expectations; the authors report −667 versus −336 cal/(mol·K) for TEM-1–BLIP. Every member of the family converts that unexplained heat capacity into residues.
- **Additional ID sets.** These follow the same pattern.
  - **RPP21–RPP29.** Fitted from the uncorrected per-temperature table (`ΔCp` −4655 ± 76 J/(mol·K), matching the authors' −1115 cal/(mol·K)), it gives 68/77/68 residues (iso/A25/C, ID ratio). With the authors' proton- and water-corrected summary it gives 62/70/62. The NMR proxy is 50. The proton correction therefore matters as much as the convention.
  - **CID–NCBD.** The six sets give `ΔCp` −3000 to −3900 J/(mol·K) and 50–72 residue equivalents. The pH 6.5 sets fit poorly (`χ²/dof` 4–14), consistent with the linked protonation the authors report.
  - **CaM–calcineurin.** This warm-only set spans 5 K, gives `ΔCp` ± 25% and residue SDs of 13–15, and is uninformative. It is not evidence about extrapolation as such.

**Precision does not require data that bracket 25 °C.** A synthetic design study used 6 temperatures × 2 replicates, DREB2A-like noise and the DREB2A 243–272 parameters, with the globular ratio:

| Window | SD `ΔCp` | SD `T_S` | Iso | A25 | C |
|---|---:|---:|---:|---:|---:|
| 5–35 °C | 105 | 0.7 K | 1.6 | 1.7 | 1.7 |
| 5–20 °C (cold; extrapolated to 25 °C) | 209 | 0.7 K | 3.5 | 3.6 | 3.6 |
| 30–45 °C (warm; extrapolated to 25 °C) | 209 | 2.7 K | 2.6 | 2.8 | 2.8 |
| 20–30 °C (narrow; brackets 25 °C) | 314 | 2.1 K | 4.5 | 4.8 | 4.8 |

- Temperature span sets `ΔCp` precision.
- Warm data lie closer to the 386–457 K virtual temperatures and therefore give the smaller residue SDs.
- In a small-`ΔCp` illustration (−400 J/(mol·K), `T_S` 263 K), `T_S` SDs of 8–26 K coexist with residue SDs of 1.6–4.8. Propagating `T_S` and `ΔCp` without their pairing gave 1.4–1.9× larger isoentropic SDs in that case.

**Model inadequacy, not extrapolation as such, is the main extrapolation risk.**

- **Declared subsets.** Cold (≤ 20 °C) and warm (≥ 27.5 °C) subsets were fitted for replicate-level datasets. DREB2A subsets agree with the full fits (|z| ≤ 1.1); warm subsets are nearly uninformative because their spans are only 2.5–7.5 K.
- **POSH–Rac1 cold subset.** The 5–15 °C subset gives `ΔCp` −2074 ± 159 versus −2795 ± 105 J/(mol·K). Every method moves 15 residues lower (z ≈ −5.7), and the verifier's linear-`ΔCp(T)` test finds significant curvature (p = 0.015).
- **TEM-1–BLIP cold-only sets.** The three 6–15 °C sets have smaller magnitude `ΔCp` (−1100 to −1800) and poor `χ²/dof` (6–10) compared with siblings that bracket 25 °C (−2100 to −2900). Mutation and curvature cannot be separated.
- **Imposed curvature.** With curvature of 0.5–1% of `ΔCp` per K, the verifier found window-dependent biases of −12% to +14%, identical across methods.
- **Concentration error.** A shared 5% concentration error shifts all three methods by 0.4–1.2 fitted SDs.

**Propagation, not the reference temperature, explains the apparent `T_S` problem.** Removing the pairing between `T_S` and `ΔCp`, or between `S₀` and `ΔCp`, changes isoentropic SDs by ×0.73–3.15 across these datasets. It overstates them when `T_S` lies below the data (×3.15 for the narrow 288–298 K DREB2A splice-variant window, `T_S` 276 K) and understates them when `T_S` lies above (×0.73–0.77). FT-ITC's linked bootstrap derives `T_S` from paired replicate roots. Its structuring calculation then draws `T_S` and `ΔCp` independently (`SpolarRecordAnalysis.cs` `Evaluate()`), because `ΔCp` comes from a separate summary build that does not keep replicate pairing. The unlinked `T_S` interval (`FitWithError.cs` `GetXAxisIntersect`) also samples slope and intercept independently. These are static observations at HEAD; no code was changed, and ITC-070 remains closed.

**Uncertainty budget.** Medians over datasets with `R > 5`, as a percentage of the estimate:

| Component | Globular: iso | A25 | C | ID: iso | A25 | C |
|---|---:|---:|---:|---:|---:|---:|
| Fitted input (paired) | 4.2 | 4.3 | 4.3 | 4.1 | 4.8 | 4.3 |
| Coefficients and calibration, application SDs | 11 | 10 | 7 | 20 | 19 | 13 |
| Same, 2021 fit standard errors | 6 | 6 | 4 | 9 | 9 | 6 |
| Single-protein calibration scatter | 8.7 | 5.4 | 3.5 | 8.7 | 5.4 | 3.5 |
| Convention: `T_p*` 320–350 K (half-range) | — | — | 1.1 | — | — | 4.9 |
| Convention: `T_p*` 309–359 K (half-range; section 4.7) | — | — | 1.9 | — | — | 8.1 |
| Convention: 37 °C instead of 25 °C | — | −0.8 | — | — | −3.2 | — |

- **Class assignment.** Changing from the globular to the ID ratio raises iso and A25 by a median 27–28% and C by 17%.
- **Coefficient draws.** Coefficient uncertainty is dominated by `a_p`, which drives `c`. The application's `FloatWithError` SDs (0.06/0.12) are about twice the standard errors printed by the user's 2021 Maple coefficient fit (0.0303/0.0588). The coefficient covariance was not recovered, and a refit of the 46 workbook rows did not reproduce the 2021 coefficients.
- **Overall.** For well-measured data, convention and model uncertainty exceed fitted-input uncertainty.

**Comparisons between interactions.**

- **Within the DREB2A family,** shared model uncertainty almost cancels in ratios (SD ≤ 0.02), while fitted-input SD dominates differences (2.4–5.0 residues).
- **Rank agreement** across the 50 joint datasets (Spearman): ID ratio iso–A25 0.97, iso–C 0.99, A25–C 1.00; globular ratio 0.99/0.99/1.00. Across 18 summary-only systems it is 0.975–0.998. With the first 33 datasets it was lower (iso–A25 0.93 under the ID ratio).
- **Per-system ratios (summary corrected 2026-10-16):** over all 50 datasets A25/C is 1.00–2.74 under the globular ratio and 1.07–1.96 under the ID ratio; iso/C is 0.26–1.10 and 0.39–1.28. The largest ratios occur near small C estimates, so relative differences alone can mislead. In the explicitly restricted `R_C > 5` subset, A25/C is 1.00–1.13 (globular) and 1.07–1.25 (ID), and iso/C is 0.88–1.10 and 0.90–1.28. The earlier summary mixed ranges without declaring the restriction. Inputs and fits are unchanged; [`check_summary.py`](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/check_summary.py) records the full and restricted ranges. Method choice can reorder systems whose `T_S` or `ΔCp` differ, even though it rarely does within a family.

**Transfer-assumption check.** A treats the polar heat capacity per ordering residue in binding (`P/R`) as equal to the folding calibration's `p̄` = 21 J/(mol·K).

- Across the 37 datasets with |`ΔCp`| ≥ 1500 J/(mol·K), `P/R_C` is 22–47 J/(mol·K) under the globular ratio and 38–69 under the ID ratio.
- In the large-`ΔCp` limit the class ratio alone fixes `P/R ≈ (1−c)·s/w_C`: about 22 (globular) and 38 (ID). The excess above these limits comes from the `ΔS_rt` term and from `S₀`.
- A's assumption therefore holds roughly for the globular ratio and fails about two- to threefold for the ID ratio.
- Correspondingly, in those 37 datasets A25 exceeds C by 1–13% under the globular ratio and by 9–25% under the ID ratio.
- This is an internal-consistency result, not an accuracy comparison.

**Relative endpoint: p53 P27A minus wild type.** Higbee et al. report an NMR-derived relative conformational entropy for the free peptides of −1.43 kcal/mol (`−TΔΔS`, 298 K).

- **17–35 with MDM2(17–125):** `ΔΔCp` = −330 ± 44 J/(mol·K). All methods give +5.4 to +6.0 residues, with remainder `−TΔΔS` of +7.8 to +9.6 kcal/mol, the opposite sign to the NMR estimate. The total `−TΔΔS` is −0.43 ± 0.11 kcal/mol. The authors attribute such MDM2 heat-capacity differences to MDM2's disordered tails and lid.
- **17–35 with MDMX(23–111):** `ΔΔCp` ≈ 0, and all methods give about zero residues (remainder −0.1 to −0.7 kcal/mol).
- **Size of method differences.** For 17–35 these are ≤ 1.8 kcal/mol, small next to the MDM2 discrepancy. Across all six P27A pairs they are ≤ 2.8 kcal/mol, and no method changes the sign of the remainder difference.
- **Raw versus fitted totals.** For MDMX 17–35 the fitted total (−0.91 ± 0.16) differs from the raw 298 K mean (−1.87; paper −1.86).

The endpoint therefore does not discriminate A from C. It exposes a family-wide limitation: heat capacity changes from sources other than folding are converted to residues at about 0.015–0.019 residues per J/(mol·K), whatever the reference temperature.

### 4.7 Literature follow-up (second 2026-10-15 pass)

Research subagents searched online and read accessible full texts. Access levels are recorded per source in the [source ledger](../Artifacts/Investigations/Structuring-Methods-2026-10-15/source_ledger.json) under `second_pass_sources`. "Deduction" marks the agent's or parent's inference rather than printed content.

**Dated evidence boundary:** the access and non-recovery statements below describe the 2026-10-15 search. Section 4.8 supersedes them where the supplied full texts and original Maple worksheet now resolve a gap. In particular, the primary effective 335.15 K parameter is recovered, and the original coefficient covariance is recoverable; neither establishes physical accuracy.

**No accessible primary source gives an uncertainty-qualified polar hydration entropy zero.**

- **Direct group data.** The only direct data found are [Privalov and Makhatadze 1993](https://doi.org/10.1006/jmbi.1993.1417), gas → water on a molar scale (abstract only). They report polar hydration entropy negative over 5–125 °C and growing in magnitude with temperature, so it has no zero in liquid water. Polar hydration `ΔCp` is negative and shrinks in magnitude with temperature ([1992](https://doi.org/10.1016/0022-2836(92)90555-x), abstract).
- **335/335.15 K.** Freire's chapter, [Bhatt et al. 2016](https://doi.org/10.1016/j.bpj.2016.05.008), COREX/BEST ([Hilser and Whitten 2014](https://doi.org/10.1007/978-1-62703-658-0_14)), Streit et al. 2024 and [Marlow et al. 2010](https://doi.org/10.1038/nchembio.347) all trace the value only to D'Aquino et al. 1996. That paper's abstract concerns backbone entropy, and its fit remains unrecovered.
  - In COREX/BEST the value is paired with the Freire conformational-entropy scales, the 0.45/−0.26 cal/(mol·K·Å²) coefficients and an entropy weighting factor of 0.95–1.05. It belongs to a coupled bundle with a condensed-phase reference.
- **176 K.** [Caro et al. 2017](https://doi.org/10.1073/pnas.1621154114) and Wand & Sharp 2018 cite this value to Makhatadze and Privalov 1993, Part I. Caro fixed it and fitted only the polar heat capacity. It matches a constant-`ΔCp` extrapolation of gas → water data about 100 K below the measurements (deduction).
- **Sign disagreement.** At 298 K the bundles disagree in sign. Per Å² of polar surface exposed, the Freire form gives +0.127 J/(mol·K) and Caro's −0.027. Much of the difference is reference state: condensed phase → water versus gas → water (partly deduction).
- **No support for A or the isoentropic choice.** No source supports a polar zero at 298 K, the choice implicit in A25, or at each system's `T_S`, the isoentropic choice.
- **Streit sign.** Streit's printed Eq. S28 sign remains unresolved.

**Consequence for C.** `T_p*` is an effective parameter of a coefficient bundle, not a measured property.

- Within the current coefficient set, the only internal evidence is the calibration-spread minimum: 333–342 K, with bootstrap range 309–359 K (section 4.1). That is a weak identifiability argument.
- Across 309–359 K, C changes by a median ±2% (globular) and ±9% (ID; range 5–18%).
- Inserting 176 K into this bundle would raise C by a median 12% (globular) and 45% (ID). That mixes reference states and is shown only to illustrate that the convention is not a small perturbation.

**Fixed-temperature precedents.**

- **Entropy meter.** Work with the NMR entropy meter evaluates at the experimental temperature, not a common one: [Frederick et al. 2007](https://doi.org/10.1038/nature05959) and Marlow et al. 2010 at 35 °C, and Caro et al. at each complex's 298–308 K. It calibrates against methyl order parameters and does not count residues.
- **Bhatt et al. 2016.** They decomposed at 298 K and report that the result does not close against NMR backbone entropy.
- **No residue-count validation.** Besides [Xavier et al. 1997](https://doi.org/10.1016/S0006-3495(97)78242-0), no precedent validates a residue count at 25 °C against structure.
- **Common-temperature comparison.** [Privalov et al. 2007](https://doi.org/10.1016/j.jmb.2006.09.059) compared many DNA-binding-domain complexes at a common 20 °C, as a precedent for comparing thermodynamics at one temperature (author manuscript). That was not a residue estimate.
- **No temperature test of residue estimates.** No study was found that tests SR-type residue estimates at several temperatures against NMR or structure. The search was mainly Europe PMC.

**Is folding upon binding itself temperature dependent?** Modestly for typical IDPs, strongly when a partner undergoes a transition.

- **Unbound IDPs.** ACTR's transient helix 1 falls from about 18% to 12% helix between 5 and 45 °C, referenced to urea at the same temperature ([Kjaergaard et al. 2010](https://doi.org/10.1002/pro.435), full text). The agent's deduction is ≲1 residue over 40 K.
  - Unbound IDPs compact on heating: Rg falls by about 2% per 10 K ([Wuttke et al. 2014](https://doi.org/10.1073/pnas.1313006111), full text).
  - NCBD loses helicity gradually and non-cooperatively (full text).
- **Bound state.** Order parameters fall by about 0.02–0.03 per 10 K for ubiquitin backbone and methyls ([Song et al. 2007](https://doi.org/10.1529/biophysj.106.102160), full text).
- **Partner transitions.** Large temperature effects arise when a partner melts or passes through a molten-globule transition near the measured range. Examples are TPR, DNA-binding domains and an affibody, where `ΔCp` changed from −1.7 to −4.3 kJ/(mol·K) across the transition (full text). Correcting for partner refolding can change `ΔCp` in magnitude and sign (Privalov et al. 2007).
- **Implication (parent deduction).** For typical IDP complexes, the physical change in ordering between the measured range and 25 °C is probably about one residue or less. A25's convention-driven temperature dependence (median −3.4% from 25 to 37 °C under the ID ratio, about one residue for a 30-residue estimate) is of similar or larger size. C's temperature invariance is therefore a reasonable approximation for typical IDPs, but not where a partner undergoes a transition.

**Non-constant `ΔCp` and precision.**

- **Coupled equilibria.** These are the main source of non-constant or non-hydration `ΔCp`, of either sign. They include protonation, unstacking of an unbound partner and partner folding transitions, and can amount to roughly half to double the intrinsic value. Sources: [Eftink et al. 1983](https://doi.org/10.1021/bi00285a025), [Baker and Murphy 1996](https://doi.org/10.1016/S0006-3495(96)79403-1), Kozlov and Lohman 1999/2000 (abstracts).
- **Residual-helix melting.** By the agent's deduction it adds only a few percent to `ΔCp` (about one residue of bias) with negligible curvature.
- **RPP21–RPP29.** The agent refitted Xu et al.'s 10–55 °C table with a quadratic term: d`ΔCp`/dT = 0.6 ± 3.4 cal/(mol·K²). This clean coupled-folding complex shows no detectable curvature, in contrast to POSH–Rac1 (section 4.6).
- **Concentration error dominates.** Within-fit `ΔCp` precision is typically 2–12%. The dominant ITC error is common-mode concentration error of 2–10%, which scales `ΔH` and `ΔCp` together and cannot be removed by global fitting of single-site models.
  - Sources: [Chodera and Mobley 2013](https://doi.org/10.1146/annurev-biophys-083012-130318); [Houtman et al. 2007](https://doi.org/10.1110/ps.062558507); [Myszka et al. 2003](https://pmc.ncbi.nlm.nih.gov/articles/PMC2279960/). In Myszka's interlaboratory study the spread in `ΔH` was 24% SD, against reported fit errors of 0.2–7%.
  - This method-independent component is comparable to or larger than the fitted-input SDs in section 4.6.
- **Global multi-temperature fitting.** It improves relative errors about two- to threefold over individual fits ([Freiburger et al. 2012](https://doi.org/10.1016/j.tca.2011.10.018), author manuscript).
- **Isoenthalpic temperatures.** Titrations near the isoenthalpic temperature carry little information, and POSH–Rac1 could not be measured at 25 °C. This is a practical reason why 25 °C estimates often require extrapolation.
- **SR 1994.** Spolar and Record's own residue uncertainties were typically about ±25%, driven by `ΔCp` (Tables 4–5, full text).

**Counter-case.** FCP1–Rap74 forms a 14-residue helix on binding, yet the measured non-rotational/translational entropy is small ([Lawrence et al. 2014](https://doi.org/10.1021/jz402729x), full text). The agent's recomputation gives about 1 (globular ratio) or 7 (ID ratio) residues. This is a reminder that residue equivalents can understate as well as overstate structural ordering.


### 4.8 Primary-source and covariance recovery (2026-10-16)

Three research subagents examined the polar reference, independent relative endpoints and the original coefficient fit. The parent reproduced the 50-dataset calculation, checked primary pages and table rows, independently verified the recovered regression, and integrated the evidence. The [follow-up package](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/README.md) records source hashes, access levels, scripts and limits. No application implementation is part of this update.

**Polar reference: primary effective construction recovered.** D'Aquino et al. (1996), Fig. 6A, p. 153, prints `ΔS_pol = −0.26·ΔASA_pol·ln(T/335.15)` alongside the apolar coefficient +0.45 and reference 385.15 K (cal/(mol·K·Å²), positive area for exposure). On pp. 151–153 the construction combines a computational backbone entropy scale, relative Ala/Gly measurements, protein unfolding and two binding examples. Eq. 9 explicitly links the polar entropy at 112 °C to the Gly backbone entropy; unfolding data alone do not separate them. The binding examples constrain that trade-off under their structural and entropy assumptions. No independent measured polar zero or statistical interval for 335.15 K is supplied. [D'Aquino et al. 1996](https://pubmed.ncbi.nlm.nih.gov/8811731/).

This is stronger primary provenance than the earlier citation chain, but not validation of the current bundle. D'Aquino's coefficients convert to +1.8828/−1.08784 J/(mol·K·Å²), versus +1.3721/−0.5909 in the 2021-based calculation; its amino-acid entropy scales differ, and equivalence of the detailed area conventions remains unestablished. A heat-capacity fit fixes entropy derivatives, not integration constants. The paper describes its conformational quantity in solution, whereas Makhatadze and Privalov (1996) explicitly define configurational entropy in vacuum. Thus a complete hydration/conformational decomposition must be compared, rather than exchanging a zero alone. The [primary-reference report](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/polar_reference/README.md) documents the matched and unmatched components.

The supplied Privalov–Makhatadze 1993 Part II is now read in full. Its Table 4 and Eqs. 8–15 derive gas-to-water group entropies using an entropy integration constant and temperature-dependent heat capacity. The peptide and four aggregate-protein polar entropies stay negative over 5–125 °C; some individual groups differ. Neither a universal 335 K nor 176 K zero is printed there. Part I and the 1995 review full texts remain unrecovered; the latter is metadata-only in this pass. The 309–359 K calibration-spread scenario remains a sensitivity analysis, not a primary confidence interval.

**Independent relative endpoints: protocol fixed, no new scoreable match recovered.** The [eligibility and family protocol](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/relative_endpoints/PROTOCOL.md) was written before this follow-up scored any endpoint; no score was subsequently generated. It requires matched constructs and conditions, recoverable endpoint uncertainty, and accounting for both partners or independently supported cancellation. Family variants stay together. Classes, temperatures and exclusions were not adjusted to improve agreement.

Fresh inspection confirms that p53's −1.43 kcal/mol is a conditional relative backbone estimate from free 1–73 fragments, with bound-state cancellation assumed when applied to 17–35; it is not a measured whole-complex entropy. CID–NCBD CPMG dynamics and phospho-p53 structures do not supply that missing endpoint. Two concrete leads are TAZ1/HIF-1α–STAT2–RelA receptor entropy at 303 K, and CrkII/cAbl–NS1 simulation-derived entropy at 300 K. Neither has the matched calorimetric series and temperature transfer recovered here. They remain conditional/ensemble leads, not additional validation families or accuracy scores. See the [endpoint report and primary-source ledger](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/relative_endpoints/README.md). This bounded search does not establish that eligible data do not exist.

**2021 coefficient covariance recovered from the original fit.** The hash-pinned Maple worksheet and its Prism figure recover all 99 normalized rows: 80 proteins, 13 hydrocarbons and 6 cyclic dipeptides. Each group's areas and heat capacities are divided by its maximum heat capacity. Repeating the original two-coefficient regression without an intercept reproduces the stored coefficients and residual SD; this is not the previously unsuccessful 46-row replacement fit or a new calibration.

For the **signed** pair `(a_np, a_p)`, in J/(mol·K·Å²), the recovered formal covariance, in `(J/(mol·K·Å²))²`, is

`[[0.0009184605204, −0.001484407251], [−0.001484407251, 0.003456814865]]`.

Its correlation is `−0.8330763`; SEs are `0.0303061/0.0587947`. The worksheet explicitly prints 95% confidence half-widths `0.0601492/0.1166912`, about 1.985 SEs. These round to the application's `0.06/0.12` SD inputs. That correspondence is established; authorial intent behind using them as SDs is not. No code or tracker entry was changed. Covariance changes sign if the polar coefficient is represented as a positive magnitude.

The [recomputed budget](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/coefficient_covariance/README.md) uses 40,000 shared model draws, with calibration denominators recomputed consistently. Central estimates remain the original-data fits and existing central constants. Median conditional coefficient/calibration/model SDs, as a percentage of each method's estimate where that estimate exceeds 5 residues, are:

| Coefficient treatment | Globular: iso | A25 | C | ID: iso | A25 | C |
|---|---:|---:|---:|---:|---:|---:|
| Application SDs, independent (reproduced scenario) | 11.10 | 10.30 | 6.99 | 19.61 | 18.68 | 12.71 |
| Recovered SEs, independent | 6.06 | 5.63 | 4.07 | 9.52 | 8.97 | 6.50 |
| Recovered SEs and covariance | 3.94 | 3.46 | 3.00 | 6.22 | 5.90 | 4.64 |

There are 47 qualifying datasets per cell except ID A25 (49); the selection rule is declared rather than silently equating the subsets. The full output also includes coefficient-only, fixed-coefficient and application-SD/correlated sensitivity scenarios, and conditional combined fitted/model SDs. On the common subset where both A25 and C exceed 5 residues (47 datasets per class), the paired ratio of absolute model SDs, C/A25, has median 0.86 (globular) and 0.69 (ID), versus 0.64/0.61 with application-sized independent draws. C's advantage narrows and is not universal: in three globular-ratio scenarios its SD is 3–5% larger than A's. Fitted-input uncertainty and central estimates do not change. Ratios of the separate percentage medians above must not be mistaken for paired SD ratios.

These are formal regression and conditional model budgets. Correlations between the coefficient fit and overlapping calibration proteins, class ratios, entropy references and unmodeled physical contributions remain unquantified. Polar-reference sensitivity, single-protein transfer scatter, concentration errors and lack of fit remain separate. These percentages are not complete uncertainty intervals or demonstrated coverage.

**Checks and corrections.** `python3 joint_compare.py` reproduces all 50 datasets byte for byte, with no missing inputs and maximum identity error `1.8474×10⁻¹³` residues. Independent NumPy least squares agrees with the recovered coefficient covariance within `8.3×10⁻¹⁸` of the saved result; it shares the source-row extractor, so it is an independent regression check, not an independent transcription. The new budget reproduces central values and fitted SDs within `1.5×10⁻¹⁴` and `3.6×10⁻¹⁵` residues. The parent re-transcribed Xu Table 1 (8 rows, 88 numeric cells); an endpoint subagent checked Feng Table 2 (6 rows, 36 numeric values plus temperatures). All checked numbers match. Xu's compact source note overgeneralizes the replicate-error type and an equation reference differs; both are recorded without altering archived numeric inputs. Rechecking summary extrema also corrected the previously undeclared ratio subsets in section 4.6. No application tests were run.

## 5. Scientific recommendation (updated 2026-10-16)

This is a research recommendation for user review. It does not approve an application method, calibration, UI or uncertainty change. Evidence is in sections 3 and 4.6–4.8. The 2026-10-16 follow-up strengthens the primary provenance of C's effective reference, recovers the original coefficient covariance, and narrows the claims supported by independent endpoints.

**In plain terms.** A residue estimate from temperature-series ITC is mainly a rescaled heat-capacity change (`ΔCp`).

- The isoentropic temperature `T_S` is one way of writing it down. Its large uncertainty does not carry over to the residue estimate when the fitted entropy and heat capacity are kept paired.
- Moving to 25 °C therefore does not make the number more certain.
- What a common temperature can provide is a single, declared convention for how the polar part of the hydration entropy is handled. That convention is what makes estimates comparable between interactions.

### 5.1 What can be recommended now

1. **Use a declared common-condition convention for comparisons, justified by comparability rather than precision.**
   - In 49 of 50 datasets, and in every design window, the fitted-input SDs of the isoentropic, A25 and C estimators are within about 11% of each other. The exception, with a single affinity point, is within 21%.
   - The 25 °C reference therefore adds no precision over a jointly propagated isoentropic estimate, and does not lose any.
   - Whatever convention is used, uncertainty should come from paired `(S₀, ΔCp)` samples or their covariance. Marginal `T_S`/`ΔCp` draws misstated isoentropic SDs by ×0.73–3.15 here.
2. **Treat the choice between conventions as a choice of polar-hydration assumption.**
   - The isoentropic estimator assumes the polar share vanishes at each interaction's own `T_S`. Interactions with different `T_S` therefore carry different polar contributions. Full and explicitly restricted iso/C ranges are given in section 4.6; ratios become unstable as residue estimates approach zero.
   - A25 assumes it vanishes at 25 °C, or cancels against the 25 °C folding calibration.
   - C assumes a single effective reference near 335 K.
3. **For substantial estimates under the globular ratio, A25 and C are close.**
   - A's transfer assumption roughly holds: `P/R` is near the calibration's 21 J/(mol·K).
   - In the 37 datasets with `|ΔCp| ≥ 1500 J/(mol·K)`, the two differ by 1–13%, typically ≤ 5%. This is conditional agreement within the estimator family, not evidence of physical interchangeability. Ratios can be much larger for small estimates.
4. **For the ID class, A at 25 °C is not recommended as the general convention.**
   - The ID surface-area ratio itself implies about 38–69 J/(mol·K) of polar heat capacity per estimated residue, roughly 2–3 times what A's globular-folding calibration absorbs.
   - In the 37 datasets with `|ΔCp| ≥ 1500 J/(mol·K)`, A25 exceeds C by 9–25%; the unrestricted range is wider (section 4.6). Its value also depends on the chosen reference (median −3.2% at 37 °C).
   - An ID-matched calibration could change this. Building one is deferred.
5. **C remains the conditional research preference for one convention covering both classes, provided `T_p*` ≈ 335 K is declared as an effective model parameter.** C now has a recovered primary construction as well as internal calibration support. Its transfer to this coefficient/remainder bundle remains unestablished. The narrower uncertainty advantage is supporting sensitivity evidence, not a reason to infer accuracy.
   - Its count is temperature-independent, so "at 25 °C" fixes the reported entropy components without changing the count.
   - It needs no transfer of polar heat capacity per residue.
   - It is less sensitive to class assignment (median +17% versus +27–28%). With recovered coefficient covariance, the paired C/A25 model-SD ratio has median 0.86 under the globular ratio and 0.69 under the ID ratio on the common `R > 5` subset. C does not have a smaller SD in every case (section 4.8).
   - It has the smallest single-protein calibration scatter in the 12-protein set (3.5% versus 5.4% and 8.7%). The 46-row set shows no such advantage (14.6% versus 14.2% and 17.6%).
   - Its cost is the unverified transfer and uncertainty of `T_p*`. D'Aquino's primary parameter belongs to a different coupled coefficient/entropy bundle. Over the calibration-internal sensitivity range 309–359 K, ID estimates change by a median ±8% (5–18%) and globular estimates by ±2%. Report that as convention sensitivity, not a confidence interval for the polar zero.
   - This recommendation rests on internal consistency and robustness. **No evidence shows C, A or the isoentropic baseline to be more accurate.** No benchmark here discriminates them: the relative p53 endpoint moves all three alike.
6. **Fallback.** If `T_p*` cannot be accepted even provisionally, A25 offers a common comparison convention, provided its ID-class transfer-assumption limitation is stated. Calling the A–C difference an established physical bias would require an independent endpoint, which is still missing.

### 5.2 What "residues folding upon binding at 25 °C" can mean

**Under C,** the estimate is an approximate residue equivalent of the binding entropy not assigned to nonpolar hydration, polar hydration or rotational/translational loss. It is calibrated against globular folding.

- The count is temperature-independent by assumption.
- "At 25 °C" states where the hydration and conformational `−TΔS` components are reported.

**Under A25,** it is the same remainder with the polar hydration share left in at 25 °C. That share is assumed to cancel against the 25 °C folding calibration, so the count is specific to the reference.

**Under either convention, it is not a measured number of residues ordered at 25 °C.**

- Heat capacity from sources other than folding is converted to residues at about 0.015–0.019 residues per J/(mol·K). Such sources include interfacial water, coupled protonation and partner tails. Examples are the 24–35 residue equivalents for the rigid TEM-1–BLIP interface and the p53 P27A/MDM2 sign reversal.
- Ordering itself may change with temperature. For typical IDPs the available evidence suggests about one residue or less over 5–45 °C, but partner transitions can change it substantially (section 4.7).
- Spolar and Record's original estimates carried about ±25% uncertainty, and in one counter-case a 14-residue helix formed with little measured folding entropy (FCP1–Rap74).

**Supported comparisons:**

- Rankings and ratios within a class and family are internally robust under the examined model: shared model uncertainty largely cancels (ratio SD ≤ 0.02 within DREB2A in the prior simulation), and methods agree on ranks (Spearman ≥ 0.97 across 50 datasets). This is not independent verification of those rankings.
- With recovered coefficient covariance, the median conditional model SDs are about 3.0–4.6% for C and 3.5–6.2% for A/isoentropic, in addition to fitted uncertainty. These replace the earlier independent-coefficient budgets as the formal-fit scenario, not as a complete error bar. Transfer scatter, C's polar-reference sensitivity, common concentration error and physical model inadequacy remain separate.
- Comparisons across classes depend on class assignment (+17–28%).

### 5.3 Extrapolation

Data need not bracket 25 °C.

- Precision follows `ΔCp` precision, set by temperature span and replication, and the distance from the data to the 386–457 K virtual temperatures. Warm-only windows were more precise than cold-only ones, and a narrow window bracketing 25 °C was the least precise.
- The material risk is that constant `ΔCp` does not describe the data. That affects every convention equally: POSH–Rac1 loses 15 residues when only 5–15 °C data are used (z ≈ −5.7), and imposed curvature of 1% per K biases estimates by up to ±14% depending on the window.
- A future method should therefore check constant-`ΔCp` adequacy (lack of fit or curvature across at least three temperatures) and report the measured range. Estimates that depend on an untested `ΔCp` model should be treated as conditional.

### 5.4 Assumptions

| Assumption | Needed by | Status |
|---|---|---|
| Constant `ΔCp`, integrated to 386–457 K virtual temperatures | All | Necessary; questionable where curvature is detected |
| All `ΔCp` is hydration (no protonation, interfacial water, coupled equilibria or partner disorder) | All | Necessary; contradicted in some examined systems; coupled equilibria can be about half to double the intrinsic `ΔCp` |
| Conformational and rotational/translational entropy independent of temperature | All; invariance of iso and C | Necessary; a reasonable approximation for typical IDPs, not where a partner undergoes a transition (section 4.7) |
| Per-residue entropy from globular folding transfers to binding | All | Necessary; untested |
| 2021 coefficient bundle (`a_np`, `a_p`, class ratios, 386 K) | All | Chosen; formal original-fit coefficient covariance recovered (section 4.8), remaining model correlations unquantified |
| Class assignment (globular versus ID ratio) | All | Chosen; largest single sensitivity |
| Polar hydration entropy zero at `T_p*` ≈ 335 K | C | Primary effective construction recovered; independent uncertainty and transfer between coefficient/remainder bundles unestablished (section 4.8) |
| Polar `ΔCp` per ordering residue equals folding's 21 J/(mol·K) | A | Questionable for ID; roughly satisfied for globular |
| Polar contribution vanishes at each system's `T_S` | Isoentropic | Convention; no supporting source found |
| Polar contribution vanishes at 25 °C, unless cancelled by calibration | A25 | Convention; no supporting source found |

### 5.5 Evidence that could change the recommendation

- **The polar reference.** An independent determination with uncertainty, compatible reference state, area definitions and conformational remainder would test C directly. D'Aquino's effective construction is now recovered, but does not establish its transfer to the 2021 bundle. The gas → water peptide and aggregate-protein polar entropies remain negative over 5–125 °C in the recovered 1993 tables; individual groups need not all behave alike. If a compatible zero lies far from 335 K or the form is not logarithmic, C weakens. If it lies near 298 K, A25 and C coincide.
- **An ID-specific calibration** of per-residue entropy and polar heat capacity per ordered residue. This could make an ID-matched A viable.
- **Independent relative endpoints** where A and C diverge most: ID systems with different polar burial or `T_S`. Candidate endpoints are NMR-derived relative conformational entropy and structural ensembles of free and bound states.
- **Remaining model covariance and transfer error.** The formal 2021 coefficient covariance is now recovered. Correlation with overlapping calibration proteins, class ratios, entropy references and model inadequacy remains unquantified. A formal regression covariance alone cannot settle these components.
- **Temperature-dependent ordering.** Evidence that ordering upon binding varies materially between 25 °C and typical measurement ranges would make any temperature-invariant count explicitly conventional.

## 6. Agreed decisions and remaining questions

The user clarified the following scope on 2026-10-15; scientific conditions inferred from that purpose are distinguished below:

| Topic | Decision |
|---|---|
| Deliverable | A scientific recommendation; no application implementation is authorized. |
| Purpose | Estimate at a declared common temperature for comparison between interactions and investigate greater certainty than relying on an uncertain interaction-specific `T_S`. |
| Interactions | Both globular and intrinsically disordered cases. |
| Main interpretation | Retain estimated number of residues folding upon binding as the approximate, comparative abstraction. |
| Temperature | A common reference is useful; 25 °C is the preferred default. Whether a future interface fixes it or permits another common reference is not yet decided. |
| Extrapolation | Include datasets that do not include or bracket 25 °C; assess extrapolation and its uncertainty explicitly. |
| Calibration | Defer selecting an authoritative dataset or undertaking a broader recalibration. Existing calculations remain exploratory and must state their convention. |
| A and C | Continue equal scientific consideration. This does not commit the application to exposing both methods. |
| Evidence claim | No requirement or claim of improved absolute structural accuracy has been agreed. The user's motivation is a better established comparison temperature and assessment of certainty. |

The recommendation in section 5 answers three scientific questions. Question 1 is answered in 5.1, 5.2 and 5.4; question 2 in 4.6 and 5.3; question 3 in 4.6 and 4.7:

1. Which assumptions let A or C support an interpretable common-temperature residue estimate in each interaction class? Keep the conversion to residues explicit without promoting it to a direct structural measurement.
2. How does uncertainty in the final estimate compare with the existing convention **after preserving fitted-parameter relationships**, including when 25 °C is outside the measured range? Distinguish fitted-input uncertainty from calibration/model sensitivity.
3. Do independent observations reveal a material contradiction or limitation for the intended use? Absolute residue proxies, relative conformational entropy and hydration observations remain different endpoints; any scored validation must define eligibility and hold out protein families. Such comparisons can constrain interpretation without requiring a claim of superiority.

An authoritative calibration choice, a separately approved application method, temperature controls, default selection, uncertainty implementation, and saved-result representation remain later decisions. The interim desktop restriction and existing saved-temperature contracts remain in force. **The section 5 recommendation awaits user review**; it is not an agreed decision.

## 7. Research stages and validation

Completed research checks include the source-workbook audit, reproduction of the archived comparisons, a new online polar-reference literature search, fresh POSH/Rac1 and Rac1b source retrieval, independent equation/refit calculations, and deposited-coordinate counts. The focused [fixed-temperature arithmetic check](../Artifacts/Investigations/Structuring-Methods-2026-10-15/fixed_temperature/README.md) verifies the equivalent expressions to within 5.7×10⁻¹⁴ residues and gives synthetic examples of both narrower and wider input uncertainty. These examples are not experimental validation or typical-precision estimates. Two subagents reviewed the new derivation and scope record. No application tests were run for this documentation update.

The [joint comparison](../Artifacts/Investigations/Structuring-Methods-2026-10-15/joint_25C/README.md) (section 4.6) completed the earlier steps 1–4:

- It fitted 50 per-temperature datasets jointly and compared the isoentropic, A25 and C estimators on the same paired inputs, for both class ratios.
- It separated fitted-input from coefficient/calibration and convention sensitivity, and tested extrapolation by declared subsets and synthetic designs.
- It checked the p53 relative endpoint.

An independent verifier reproduced the algebra, the fits and the design study, and added error-correlation, curvature, concentration-error and resampling checks. Its script and printed output are preserved in [`joint_25C/verification`](../Artifacts/Investigations/Structuring-Methods-2026-10-15/joint_25C/verification/README.md). It stopped on an API rate limit before writing its final report, and the parent integrated the reproduced numbers. The 2026-10-16 rerun reproduced the saved output byte for byte and measured a maximum identity error of 1.8474×10⁻¹³ residues; this corrects the earlier summary bound of 1.5×10⁻¹³. No application tests were run, because no application code changed.

The user authorized the next research pass on 2026-10-16. It recovered D'Aquino's primary effective construction and the original 99-row coefficient covariance, froze endpoint eligibility/family blocks, found no new eligible whole-complex relative endpoint in a bounded search, and checked a sample of the literature tables (section 4.8). The recommendation still awaits user review; authorization to continue research is not approval of a method.

Remaining research steps, in priority order:

1. **Test transfer of the recovered polar-reference construction.** The question is now whether D'Aquino's reference state, area definitions and conformational remainder can support the 2021 coefficient/folding-calibration bundle, with an independent uncertainty estimate. Part I/1995 full texts and the original Eq. 9/binding-comparison inputs remain specific source gaps. Do not repeat the obsolete claim that the 335.15 K primary origin is unavailable, or substitute a gas-reference zero into another bundle.
2. **Pursue a matched independent relative endpoint under the frozen protocol.** Prioritize the existing high-divergence RPP, CID–NCBD, POSH and phospho-p53 families. The TAZ1 and CrkII leads identify concrete missing calorimetric/ensemble/temperature-transfer evidence. Account for both partners; retain conditional diagnostics without turning them into accuracy scores. No class, temperature or exclusion may be selected for agreement.
3. **Keep the recovered covariance in the research budget; distinguish remaining model uncertainty.** Formal SE/covariance recovery is complete. Its dependence on shared calibration proteins, class ratio and physical model assumptions remains unquantified. Do not interpret the reconstructed confidence half-widths as SDs or treat the conditional budget as complete coverage. Any application sampling change remains separately unapproved.
4. **Extend transcription checks only as needed for a subsequent comparison.** Xu Table 1 and Feng Table 2 samples match numerically; most literature rows remain singly extracted. Their provenance notes and the corrected summary subsets are recorded. A full table audit is not claimed.

Constant-`ΔCp` adequacy remains necessary for interpreting extrapolated estimates; the existing curvature/design checks are retained. The 46-row entropy provenance and complete alternative bundles remain open supporting work. Authoritative calibration selection/rebuilding remains deferred, and new experiments or product work are not implied by the missing evidence.

If implementation is subsequently authorized:

- Verify changed equations and units using independently specified, high-precision expectations. Do not derive expected values from `FTSRMethod` or its constants. Structural benchmarks assess the method; they do not prove numerical correctness, solver recovery, or external forward-model validation.
- Preserve original-data central estimates; resampling determines uncertainty rather than replacing those estimates with a resampling mean or median.
- Keep old saved outputs tied to their saved method, inputs, and evaluation temperature. Read `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md` before designing serialization changes.
- Put shared scientific presentation in Core; keep native macOS and Avalonia aligned while retaining their renderers. Check reports and interpretation evidence where affected. Update report map X02, user-facing manual guidance, and shared help only for agreed behavior that helps users interpret results.
- Run `AnalysisITC.Core.Tests` and relevant desktop tests. Existing coverage includes `SpolarRecordAnalyticTests`, `SpolarRecordLinkedWorkflowTests`, `SpolarRecordAnalysisSamplingTests`, `StructuringPresentationTests`, and both desktops' `SpolarSavedTemperatureViewTests`. Existing passing tests do not validate a newly chosen scientific method.

## 8. Handoff and decision record

- **2026-10-07, existing research direction:** the investigation selected Option A for reference-temperature research and recorded Option C separately as ITC-072. This is a research direction, not application implementation approval.
- **2026-10-12, plan creation:** consolidated ITC-068 and ITC-072 into this shared document. No method, calibration, uncertainty, defaults, or saved-result behavior was changed. Other proposed plans were not created.
- **2026-10-13, interim desktop restriction approved:** hide Mean temperature and Reference temperature in macOS and Avalonia and fix new structuring runs to Isoentropic point. Preserve saved outputs and the Core research modes. This limits application exposure while calibration and interpretation remain unresolved; it does not decide the preferred scientific method. ITC-068 remains open.
- **2026-10-15, broad research direction:** the user requested equal consideration of A and C, followed by new online research into the 335 K basis and independent benchmarks. Three subagent tracks covered source provenance, calibration/benchmark extraction, and identifiability; the parent integrated and checked the calculations. This supersedes the earlier A-first research preference.
- **2026-10-15, evidence update:** examined revision `f1b67957527ccbee09b587527b71e40c20094b45`; the first-pass audit began at `3f9283b8e74f5d1c5b161aff729f1ac6a02ac0af`. The scientific method source was unchanged between those revisions. Unrelated working-tree changes were present and preserved. The [evidence directory](../Artifacts/Investigations/Structuring-Methods-2026-10-15/README.md) records the scripts, numeric inputs, source access and limits. Independent arithmetic reproduces the calibration and benchmark tables; it is not runtime or scientific validation of FT-ITC.
- **2026-10-15, scope clarification:** the user selected a scientific recommendation, both interaction classes, the residue-count interpretation, and a common temperature with 25 °C as preferred default. The purpose is to avoid reliance on an uncertain interaction-specific entropy zero for comparison and assess certainty; a superior-accuracy claim or multi-method product was not selected. Calibration selection/rebuilding is premature. The user explicitly included extrapolated 25 °C estimates with extrapolation and uncertainty assessed. Section 6 records these decisions without selecting A or C.
- **2026-10-15, focused scientific clarification:** two subagent reviews and the parent examined general-temperature SR equations and the constant-`ΔCp` transformation. A fixed reference is admissible in the decomposition, but does not itself improve precision. Static inspection at revision `f1b67957527ccbee09b587527b71e40c20094b45` identified marginal sampling in the current structuring calculation; no code or uncertainty policy was changed. ITC-070 remains closed.
- **2026-10-15, joint comparison and recommendation (second pass):**
  - **Revision.** Examined HEAD `2a24852d7861f1383f59aaabdddecd0a7e749942`; `SpolarRecordAnalysis.cs` is unchanged since `f1b67957`. The only Core change between them is event plumbing in `AdvancedAnalysis.cs`. At completion HEAD was `06602ff3` (manual/help and macOS title-bar commits). The structuring source and its uncertainty dependencies are unchanged, and there are no uncommitted Core changes. Unrelated dirty-tree changes were preserved.
  - **Source revalidation** (read-only subagent, parent spot-checked):
    - Linked bootstrap replicates carry paired `(S₀, ΔCp)`.
    - `T_S` is built from paired replicate roots, but `ΔCp` comes from a separate summary build, and `Evaluate()` draws `T_S`, `ΔCp` and the coefficients independently (split-normal).
    - The unlinked `T_S` interval samples slope and intercept independently.
    - No test asserts joint versus marginal sampling.
    - No code was changed. ITC-070 remains closed.
  - **Evidence.** [`joint_25C/`](../Artifacts/Investigations/Structuring-Methods-2026-10-15/joint_25C/README.md) contains:
    - The joint fits of 50 datasets, assembled from the user's DREB2A files, POSH and literature tables extracted by subagents.
    - The model, convention, extrapolation, design and p53 analyses.
    - The independent verification.
    - Literature follow-up by two subagents is recorded in section 4.7 and the source ledger.
  - **Checks run:** `joint_compare.py` (identities below 1.5×10⁻¹³ residues) and the independent verifier's NumPy script. No application tests were run, because no application code changed.
  - **Conclusions** (section 5, for user review):
    - A common 25 °C convention is admissible and useful for comparison but does not improve precision.
    - All three estimators share the same `ΔCp`-dominated fitted-input uncertainty.
    - C is the better-supported of the three for one convention covering both classes. This is conditional on declaring its effective polar reference (no primary determination found).
    - A25 is practically equivalent for globular interactions and inconsistent for the ID ratio.
    - No convention is shown to be more accurate.
  - **Limits:**
    - Literature rows were extracted by subagents and spot-checked, not fully re-transcribed.
    - POSH SDs depend on the resampling scheme.
    - The 2021 coefficient covariance is unknown.
    - The verifier stopped on an API rate limit before its written report; its printed output is preserved.
    - The ID-data subagent stopped on a rate limit after writing its table builder. The parent ran it on the agent's extracted text and added the 17 resulting multi-temperature sets; these were not individually re-transcribed.
    - Only one non-bracketing ID set (warm-only CaM–calcineurin, 5 K span) was found, and it is uninformative: `ΔCp` ± 25%.
  - **Observations for the user** (not acted on):
    - The application's coefficient SDs (0.06/0.12) are about twice the 2021 fit standard errors.
    - `ISSUES.md` ITC-068/ITC-072 still link to the old plan file name `STRUCTURING_METHODS_PLAN.md`.
- **Next step recorded at the 2026-10-15 handoff:** user review of section 5 and further research under section 7. The user subsequently authorized the research follow-up below, without approving either method for implementation.
- **2026-10-16, primary-source, endpoint and covariance follow-up:**
  - **Revision and scope.** HEAD `06602ff3a235a13a7cd2f5559d49adf2bad214b8`. The parent compared `SpolarRecordAnalysis.cs`, `FitWithError.cs`, `AnalysisResultParameterEvaluation.cs` and `SummaryUncertainty.cs` byte for byte with `f1b67957`; all are unchanged. This research's edits are confined to this plan and the gitignored evidence directory. Initial dirty-file hashes and final scope checks are in the follow-up package. Unrelated project/report files changed or appeared during the session; their latest contents were left untouched. This research took no application, UI, serialization, manuscript, version/release, tracker, commit or push action.
  - **Inputs.** Nine supplied PDFs were screened across the subagents. D'Aquino 1996, Privalov–Makhatadze 1993 Part II and Makhatadze–Privalov 1996 were examined for primary provenance and differing entropy definitions. The original Maple worksheet, Prism figure, author SI and proof were read in place and hash-checked. Existing 50-dataset inputs and the user's four DREB2A files remain unchanged; the baseline output SHA-256 is `beb646d4d0e83addd99e871412f4b0fa70fce142159544b0e2fb55e0faf0ea1a`.
  - **Evidence.** [`followup_2026-10-16/`](../Artifacts/Investigations/Structuring-Methods-2026-10-15/followup_2026-10-16/README.md) contains the three research reports, source ledgers/manifests, frozen endpoint protocol, regression reconstruction, revised model budgets, arithmetic/summary/transcription checks and parent verification. Full papers and extracted full texts are not redistributed in the evidence package.
  - **Checks run.** `python3 joint_compare.py`: 50 datasets, no missing inputs, byte-identical output, maximum identity error `1.8474×10⁻¹³` residues. `recover_and_propagate.py`: original 99-row regression recovered and 40,000 shared model draws; central/fit-SD differences below `1.5×10⁻¹⁴`/`3.6×10⁻¹⁵`. Parent NumPy regression check: covariance difference below `8.3×10⁻¹⁸` from the saved result, sharing the source extractor. Primary-equation arithmetic, method-divergence screening, summary extrema and Xu transcription scripts passed; Feng Table 2 was checked by the endpoint subagent. JSON and local evidence links were checked. No application tests were run.
  - **Conclusions for user review.** Primary provenance of the effective 335.15 K construction is recovered, but transfer/uncertainty remains unestablished. Original coefficient correlation is −0.8331; formal joint-coefficient budgets are appreciably smaller than the earlier independent scenarios, and C's SD advantage is narrower and not universal. Central estimates and fitted-input precision are unchanged. No new independent relative endpoint was eligible for an accuracy score. C remains a conditional research preference, with A/C considered on the same evidence and neither approved for implementation.
  - **Corrections.** Earlier primary-non-recovery statements are superseded in sections 4.2/4.8. Section 4.6 now labels full versus `R_C > 5` ratio ranges. The current identity-error bound is corrected. Sampled literature numbers match; two Xu metadata notes remain explicitly recorded, not silently changed.
  - **Limits.** D'Aquino supplies an effective construction rather than a statistically identified zero; Part I and the 1995 review remain full-text gaps. Original regression covariance is formal and does not model dependence on overlapping calibration data or physical transfer error. Endpoint search and table checking were bounded; the old POSH resampling and non-bracketing-data limitations remain. No runtime validation, calibration approval or accuracy claim follows.
  - **Observations still awaiting the user, not acted on.** The coefficient `0.06/0.12` values now trace numerically to rounded 95% confidence half-widths rather than SEs; intent is unconfirmed. ITC-068/ITC-072 still reference the old plan filename. No tracker issue was posted and ITC-070 remains closed.
  - **Next concrete step.** Review the revised conditional recommendation and, for further research, address bundle compatibility and a protocol-eligible relative endpoint (section 7). Preserve the isoentropic desktop baseline and original-data central estimates.
- **Pending:** the scientific conclusions and later product decisions distinguished in section 6. Record new decisions with their date, rationale, evidence, and explicit approval where required.

For each later handoff, record the code revision examined, evidence inputs and scripts used, checks actually run, conclusions and their limits, unresolved questions, and the next concrete step. Keep archived exploratory claims distinct from reproduced evidence and accepted decisions.
