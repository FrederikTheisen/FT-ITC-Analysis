# Structuring methods — research plan

Status: research and planning (created 2026-10-12). Related issues: [ITC-068](ISSUES.md#itc-068---structuring-evaluation-temperature-options-need-scientific-review) and [ITC-072](ISSUES.md#itc-072---structuring-with-an-explicit-polar-hydration-entropy-term). Creating this document does not approve a scientific method change or application implementation.

This is the shared plan for iterative work across agents. Record research, decisions, and handoff state here; keep the tracker entries concise. The existing [investigation](../Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/README.md) identifies **Option A**, a common reference temperature with a matched per-residue calibration, as the chosen research direction. **Option C**, explicit polar hydration, remains a separate research alternative. Neither direction is assumed to be ready for release.

## 1. Problem and scope

Structuring analysis estimates how much conformational ordering accompanies binding by subtracting model-estimated hydration and rotational/translational entropy contributions from the binding entropy. The remaining entropy is converted into an estimated number of residues folding upon binding using a calibrated per-residue constant. These are conditional model estimates, not direct structural measurements.

The evaluation temperature and calibration must describe the same convention. Otherwise a change of temperature can change the estimated residue count without a corresponding change in structural ordering. Review the scientific justification, practical interpretation, and evidence for the available conventions before choosing application behavior.

The plan covers:

- ITC-068: the isoentropic, mean-temperature, and reference-temperature conventions, with the existing reference-temperature research direction retained.
- ITC-072: explicit polar hydration as a distinct alternative, including its additional assumptions and calibration.
- The evidence, unresolved scientific decisions, and validation requirements needed to decide whether either proposal should proceed.

Application implementation is a later stage requiring an agreed method and scope. Do not turn adjacent research observations into additional implementation tasks without agreement.

## 2. Current application baseline

- `AnalysisITC.Core/Analysis2/AdvancedAnalysis/SpolarRecordAnalysis.cs` contains `FTSRMethod`, `SRTempMode`, and `SROutput`.
- Core `SRTempMode` retains Isoentropic point, Mean temperature, and Reference temperature for research and saved-output compatibility. Since the interim restriction agreed on 2026-10-13, both desktop apps expose only a fixed Isoentropic point row and explicitly use that mode for new runs. `FTSRMethod` still uses one `PerResidueEntropyLoss` value, approximately −23.96662 J/(mol·K) per residue, across those modes; its equations and calibration are unchanged.
- ITC-068 records that the published Spolar–Record convention and its Theisen et al. (JACS 2021) adaptation evaluate at the isoentropic temperature, where binding entropy is zero. Mean and reference temperatures are FT-ITC additions. Whether and how those additions are justified remains the subject of this review.
- ITC-069 and ITC-071 are resolved: completed outputs use their saved evaluation temperature and share temperature formatting across desktop views and reports. Retain those contracts.
- ITC-070 is closed as not an issue: the central evaluation temperature used to scale entropy contributions is treated as fixed. This plan does not reopen that decision.
- Report presentation is mapped under X02 in `Documentation/ANALYSIS_REPORT_MAP.md`. The manual describes the current structuring workflow in `Documentation/UserManual/pages/08-results-advanced-analysis.md`.

These baseline observations come from the current source and tracker. No runtime verification was performed when creating this plan.

## 3. Methods under consideration

Symbols used below: `R` = estimated residues folding upon binding; `ΔS(T)` = model-estimated binding entropy at temperature `T`; `ΔCp` = heat-capacity change; `T_S` = isoentropic temperature; `c` = share of `ΔCp` assigned to hydrophobic hydration; `ΔS_rt` = rotational/translational entropy loss; `s(T)` = calibrated conformational entropy loss per residue. Temperatures inside logarithms are in Kelvin.

### Published isoentropic convention

Retain the published convention as the comparison baseline. Verify its equations, coefficients, interaction classes, and calibration against the original sources before using it as independent validation evidence. The review must distinguish a method's published status from evidence that it is preferable for every intended comparison.

### Option A — reference temperature with matched calibration (ITC-068)

The existing investigation proposes:

`R_A = [ΔS(T_ref) − c·ΔCp·ln(T_ref/386 K) − ΔS_rt] / s(T_ref)`

Its 12-protein calibration gives the rounded exploratory relationship:

`s(T_ref) = −22.62 + 21.0·ln(T_ref/298.15 K)` J/(mol·K) per residue.

The 46-protein set gives a different relationship, approximately `−22.04 + 18.3·ln(T_ref/298.15 K)`. Neither rounded expression is an implementation specification. Decide the authoritative dataset, coefficient precision, provenance, applicable temperature range, and uncertainty first.

The investigation identifies an added assumption: the polar heat capacity per folded residue in binding matches that in the folding calibration. Treat this as a chosen, scientifically questionable assumption to assess, not a consequence of selecting a reference temperature. Decide separately whether a matched calibration also justifies Mean temperature mode.

### Option C — explicit polar-hydration term (ITC-072)

The original tracker entry records that the Spolar–Record split assigns `c·ΔCp` to hydrophobic hydration and leaves the polar share, `(1 − c)·ΔCp`, in the conformational remainder. The proposed alternative represents that polar contribution explicitly:

`ΔS_polar(T) = (1 − c)·ΔCp·ln(T/T_p*)`

Here `T_p*` is a proposed polar-hydration convergence temperature, approximately 335 K in the investigation. The resulting conformational term is:

`ΔS_conf = −ΔCp·ln(T_S/T_eff) − ΔS_rt`

with `T_eff = 386^c·T_p*^(1−c)` (approximately 405 K for folded–folded and 424 K for folded–disordered), and a calibration constant of about −20.2 J/(mol·K) per residue. Under the proposed assumptions, the residue count is independent of evaluation temperature; four entropy contributions can be converted into energy contributions that sum to `−TΔS`.

This introduces another method parameter and changes the interpretation of existing estimates. It remains a research alternative, not an approved replacement or an instruction to add a selector.

## 4. Existing evidence and its limits

The [2026-10-07 investigation](../Artifacts/Investigations/Structuring-Reference-Temperature-2026-10-07/README.md) and its scripts hold the detailed derivations and exploratory results. The following summaries are transferred from that investigation and ITC-072; they were not reproduced when this plan was created:

- Both folding calibration sets (12 and 46 proteins) show per-residue values evaluated at each protein's own `T_S` trending with `T_S` (`r = 0.79`). The smallest spread occurs near 333–342 K. Interpreting that minimum as polar-hydration convergence requires an additional assumption described in the investigation.
- On 15 structural benchmarks, the reported RMS log error is approximately 0.36 for the published convention and 0.34 for both alternatives. This small exploratory comparison does not establish superiority or formal non-inferiority; its paired intervals and benchmark limitations must inform the decision.
- Option C's reported method uncertainty is 12.8%, compared with 19.5% for the published convention, for DREB2A 243–272. Those calculations exclude fit uncertainty and are not uncertainty guarantees for application outputs.
- ITC-072 records reduced sensitivity to the polar/nonpolar surface ratio and an estimated DREB2A folded–disordered correction of 29.2 versus 32.2 published. Generality remains to be established.
- Open limitations include the small validation set, unconfirmed provenance of the 46-protein set, the proposed `T_p*` bootstrap range of 308–359 K, and changes of about 10% to published `R_th,ID` values.
- The investigation also discusses fit-coordinate correlations and calibration/coefficient correlations. Those are evidence to review, not approval to expand this plan into an uncertainty-engine rewrite.

Source spreadsheets, supporting documents, and paper files were read in place by the investigation rather than copied into its artifact directory. Its README identifies those inputs and the literature. A future agent must confirm access, provenance, and reproducibility; archived machine-specific paths alone are insufficient.

## 5. Decisions needed before implementation

1. Scientific applicability: which convention is justified for each interaction class, and which assumptions must a user understand?
2. Calibration: which dataset and coefficient relationship are authoritative, at what temperatures, and with what method uncertainty?
3. Evidence: which independent structural benchmarks and comparison criteria are sufficient? Specify criteria before interpreting new comparison results.
4. Temperature choices: retain, restrict, relabel, or remove Mean temperature and Reference temperature? Research preference alone does not decide the UI or default.
5. Polar hydration: keep Option C as research, or propose a separately approved application method? Resolve the additional convergence-temperature parameter and consequences for published comparisons.
6. Uncertainty: distinguish uncertainty from fitted inputs, calibration, and method assumptions. Decide which changes are necessary for the selected method and which remain separate work.
7. Saved results: if implementation proceeds, how are method identity, calibration, assumptions, and evaluation temperature captured so existing outputs remain interpretable without silent recalculation?

## 6. Research stages and validation

1. Recover the original inputs and sources; document provenance, units, signs, interaction classes, and any exclusions. Verify the archived scripts against them.
2. Reproduce the baseline and both exploratory alternatives, retaining counterexamples and uncertainty intervals. Resolve calibration provenance and assess temperature applicability.
3. Expand independent structural comparisons where possible; document limits when further evidence is unavailable. Keep method accuracy, numerical equation agreement, and uncertainty coverage as separate claims.
4. Record the scientific decision and remaining uncertainty here. Only then prepare the agreed application implementation scope.

If implementation is subsequently authorized:

- Verify changed equations and units using independently specified, high-precision expectations. Do not derive expected values from `FTSRMethod` or its constants. Structural benchmarks assess the method; they do not prove numerical correctness, solver recovery, or external forward-model validation.
- Preserve original-data central estimates; resampling determines uncertainty rather than replacing those estimates with a resampling mean or median.
- Keep old saved outputs tied to their saved method, inputs, and evaluation temperature. Read `Documentation/Schemas/FTXTC/FTXTC_FORMAT.md` before designing serialization changes.
- Put shared scientific presentation in Core; keep native macOS and Avalonia aligned while retaining their renderers. Check reports and interpretation evidence where affected. Update report map X02, user-facing manual guidance, and shared help only for agreed behavior that helps users interpret results.
- Run `AnalysisITC.Core.Tests` and relevant desktop tests. Existing coverage includes `SpolarRecordAnalyticTests`, `SpolarRecordLinkedWorkflowTests`, `SpolarRecordAnalysisSamplingTests`, `StructuringPresentationTests`, and both desktops' `SpolarSavedTemperatureViewTests`. Existing passing tests do not validate a newly chosen scientific method.

## 7. Handoff and decision record

- **2026-10-07, existing research direction:** the investigation selected Option A for reference-temperature research and recorded Option C separately as ITC-072. This is a research direction, not application implementation approval.
- **2026-10-12, plan creation:** consolidated ITC-068 and ITC-072 into this shared document. No method, calibration, uncertainty, defaults, or saved-result behavior was changed. Other proposed plans were not created.
- **Current next step:** recover and verify the research inputs, then resolve calibration provenance and the evidence needed for a scientific decision.
- **2026-10-13, interim desktop restriction approved:** hide Mean temperature and Reference temperature in macOS and Avalonia and fix new structuring runs to Isoentropic point. Preserve saved outputs and the Core research modes. This limits application exposure while calibration and interpretation remain unresolved; it does not decide the preferred scientific method. ITC-068 remains open.
- **Pending:** the scientific decisions and long-term product decisions in section 5. Record new decisions with their date, rationale, evidence, and explicit approval where required.

For each later handoff, record the code revision examined, evidence inputs and scripts used, checks actually run, conclusions and their limits, unresolved questions, and the next concrete step. Keep archived exploratory claims distinct from reproduced evidence and accepted decisions.
