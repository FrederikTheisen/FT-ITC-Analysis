#!/usr/bin/env python3
"""One-time migration of legacy/itc_knowledge_base_expanded.json to entry files.

Produces entries/90-literature-rules.md and entries/91-literature-precedents.md.
After migration the Markdown files are the source of truth; edit them directly.
Re-running overwrites both files, so do not re-run after hand edits.

Design decisions (see README.md for the reasoning):
  * every entry is self-contained: citations are expanded inline from sources.json
    instead of pointing at opaque ids in another chunk;
  * rule 'trigger' becomes the Situation, 'conclusion' the Interpretive implication,
    'avoid_language' a 'Common over-claim', 'recommended_language' a 'Defensible
    framing' (a calibrated claim strength, not boilerplate to copy);
  * the legacy language_patterns block is not migrated: the scientific-guidance
    prompt already governs phrasing and fixed sentences invite templated output;
  * metadata, design_principles and quality_flags lists are not retrievable content;
    case-level quality flags are kept as a 'Flags' line.
"""
from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_families import FAMILIES_BY_CASE  # noqa: E402  case id -> related study-set names
legacy = json.loads((ROOT / "legacy" / "itc_knowledge_base_expanded.json").read_text(encoding="utf-8"))

# ---------------------------------------------------------------- rules
# rule id -> (situation-style title, retrieval keywords)
RULES = {
 "rule_c_value_high": ("Very high c-value with a near-vertical transition: Kd is poorly identifiable even when n and ΔH are well defined",
   "high c, c above 1000, steep isotherm, step-like, tight binding, nanomolar Kd, Kd upper bound, fit converged but Kd imprecise"),
 "rule_c_value_low": ("Low c-value (below about 1–10): n and ΔH strongly correlated, stoichiometry weakly constrained",
   "low c, shallow isotherm, weak binding, no plateau, n-ΔH correlation, fixed stoichiometry, Wiseman c"),
 "rule_zero_enthalpy": ("Binding enthalpy near zero: ITC is insensitive although a real interaction may exist",
   "ΔH close to zero, flat thermogram, silent ITC, no heat, small heats, change temperature, heat capacity"),
 "rule_endothermic_binding": ("Endothermic binding (ΔH > 0) with ΔG < 0 is entropy-driven, not weak or unfavorable",
   "positive enthalpy, endothermic, entropy-driven, favorable -TΔS, desolvation, hydrophobic"),
 "rule_exothermic_not_mechanism": ("Exothermic binding (ΔH < 0): enthalpy alone does not identify hydrogen bonds, electrostatics or any specific mechanism",
   "negative enthalpy, enthalpy-driven, hydrogen bonding, van der Waals, mechanism from ΔH, molecular origin"),
 "rule_entropy_not_hydrophobic_proof": ("Favorable entropy term is not by itself proof of hydrophobic binding",
   "positive TΔS, entropy-driven, hydrophobic effect, solvent release, ion release, conformational entropy"),
 "rule_concentration_error_titrant": ("Uncertain syringe (titrant) concentration biases Kd and ΔH without worsening the fit",
   "syringe concentration error, titrant concentration, systematic error, biased Kd, concentration accuracy"),
 "rule_concentration_error_cell": ("Fitted n deviates from the expected stoichiometry in a simple 1:1 system: suspect cell concentration, inactive fraction or cell volume first",
   "n not 1, n 0.7, n 1.3, concentration error, inactive protein, active fraction, cell volume"),
 "rule_n_context": ("Non-integer or unexpected fitted n: interpret by model and system (concentration error, functional valence, heterogeneity, effective stoichiometry)",
   "fractional stoichiometry, unexpected n, n 0.5, n 2, apparent stoichiometry, valence"),
 "rule_baseline_small_offset": ("A small baseline or heat offset relative to early injection heats can shift K and ΔH materially",
   "baseline offset, constant heat, dilution offset, small offset, K bias, ΔH bias, 1.4 percent offset"),
 "rule_dilution_not_always_constant": ("Late-injection or blank heats that vary systematically: a constant offset is inadequate",
   "heat of dilution, blank subtraction, concentration-dependent dilution, late injections not zero, drifting late heats"),
 "rule_residual_systematic": ("Structured residuals (runs, curvature, phase-specific structure): model, concentrations or baseline treatment are inadequate",
   "non-random residuals, systematic residuals, residual pattern, runs test, poor fit, misfit"),
 "rule_good_residual_not_accuracy": ("Excellent fit statistics and random residuals support internal consistency, not accuracy",
   "good fit, small RMSD, random residuals, systematic error, concentration scaling, precision versus accuracy"),
 "rule_multisite_underidentification": ("Two-site or multisite fit with very large uncertainties and correlations: heterogeneity supported, microscopic constants not identified",
   "two-site model, multisite, large confidence intervals, correlated parameters, unidentifiable, overfitting"),
 "rule_model_fit_not_validation": ("A more complex model fits better: statistical fit alone does not establish the physical mechanism",
   "model selection, AICc, extra parameters, better fit, overfitting, mechanism validation"),
 "rule_global_analysis": ("Multiple titrations sharing physical parameters: global analysis improves precision and identifiability",
   "global fit, joint fit, shared parameters, multiple experiments, suboptimal isotherms, SEDPHAT"),
 "rule_reverse_orientation": ("Reverse titration for complex or multivalent binding gives complementary information but does not guarantee identification of all parameters",
   "reverse titration, swapped orientation, ligand in cell, protein in syringe, multivalent"),
 "rule_multivalent_precipitation": ("Multivalent reactants can crosslink and precipitate: baseline and heats are corrupted and parameters unreliable",
   "precipitation, crosslinking, multivalent, aggregate, cloudy cell, avidity"),
 "rule_high_functional_valence": ("Large or fractional n in a multivalent system: structural valence versus functional valence",
   "multivalent, functional valence, number of motifs, fractional n, avidity, steric hindrance"),
 "rule_displacement_high_affinity": ("Direct titration too steep to measure tight binding: competitive displacement ITC brings the affinity into range",
   "displacement ITC, competition, tight binder, picomolar, nanomolar, weak competitor, Sigurskjold"),
 "rule_displacement_enthalpy_contrast": ("Competitive/displacement ITC needs sufficiently different enthalpy signatures for target and competitor",
   "displacement ITC, competitor, enthalpy difference, no heat displacement, competition design"),
 "rule_buffer_proton_linkage": ("ΔH changes substantially across buffers with different ionization enthalpies: proton uptake or release is plausible and quantifiable",
   "buffer dependence, Tris versus phosphate, HEPES, proton linkage, ionization enthalpy, protonation"),
 "rule_ionizable_ligand_pH": ("Ionizable ligand or a pH difference between syringe and cell: protonation heats and buffer neutralization contaminate the signal",
   "pH mismatch, acidic ligand, carboxylic acid, unbuffered, protonation heat, buffer neutralization"),
 "rule_temperature_deltaCp": ("ΔH measured at several temperatures: its slope gives ΔCp if the same binding process applies throughout",
   "heat capacity, ΔCp, temperature series, ΔH versus T, multi-temperature ITC"),
 "rule_temperature_rescue_signal": ("Binding heat too small at one temperature: changing temperature can restore an informative signal",
   "small heats, near-zero ΔH, change temperature, increase |ΔH|, heat capacity, sample stability"),
 "rule_affinity_vs_enthalpy_cooperativity": ("A later sequential step has a more favorable ΔH: not positive cooperativity unless the affinity is also higher",
   "cooperativity, sequential binding, enthalpy versus affinity, positive cooperativity, two-site"),
 "rule_orthogonal_confirmation": ("ITC parameters agree with BLI, SPR, NMR, functional inhibition or structural evidence: confidence in the interpretation increases",
   "orthogonal method, SPR, BLI, NMR, agreement, validation, independent confirmation"),
 "rule_weak_secondary_site": ("ITC fits one site but another technique detects a much weaker second site",
   "weak second site, secondary site, one-site model, NMR detects, millimolar, below ITC sensitivity"),
 "rule_no_binding_detected": ("Flat isotherm or no binding detected: no heat-associated binding was seen, which is not proof of no interaction",
   "no binding, flat isotherm, no heat, negative result, null result, no interaction"),
 "rule_pathological_shape": ("Isotherm lacks the expected approach to saturation or has a visibly implausible shape: downgrade confidence in quantitative parameters",
   "non-sigmoidal, no saturation, weird isotherm, baseline pathology, buffer mismatch, aggregation, bad titration"),
 "rule_concentration_series": ("Fitted Kd changes systematically with macromolecule concentration: self-association, aggregation or linked equilibria",
   "concentration dependence of Kd, Kd varies with concentration, self-association, aggregation, nonideality"),
 "rule_cell_volume_systematic": ("Consistent stoichiometry bias across otherwise well-behaved systems: effective cell volume calibration",
   "cell volume, n shifts, calibration, systematic n bias, active volume"),
 "rule_integration_uncertainty": ("Baseline noise differs strongly between injections: per-injection integration uncertainty should weight the fit",
   "peak integration error, injection SD, weighted fit, heteroscedastic, noisy baseline, NITPIC"),
 "rule_manual_baseline_bias": ("Baseline manually adjusted after viewing the desired fit: circular analysis and user-induced bias",
   "manual baseline, baseline editing, integration bias, circular analysis, operator bias"),
 "rule_single_injection_mixing": ("Continuous or single-injection ITC: imperfect mixing and injection-rate limits bias fitted thermodynamics",
   "single injection ITC, continuous injection, mixing, injection rate, cell volume as correction"),
 "rule_fuzzy_binding": ("Strong ITC transition but NMR or structure show persistent disorder or multiple dynamic contacts: not necessarily a rigid lock-and-key complex",
   "fuzzy complex, disordered complex, IDP binding, dynamic complex, one-site fit, lock and key"),
 "rule_composite_membrane_heat": ("Membrane–peptide thermogram with sign changes, non-monotonic injections or concentration-dependent background is a composite of processes",
   "lipid vesicle, peptide membrane, partitioning, pore formation, micelle, sign change, non-monotonic"),
 "rule_multiple_interaction_modes": ("Separable exothermic and endothermic components or structured one-site residuals: more than one calorimetric process",
   "biphasic, exothermic and endothermic, two processes, multiple binding modes, composite heat"),
 "rule_dilution_selfassembly_sign": ("ITC dilution used for aggregate or supramolecular disassembly: the measured enthalpy is for disassembly, assembly has the opposite sign",
   "dilution experiment, disassembly, self-assembly, aggregate, micelle, supramolecular, sign of enthalpy"),
 "rule_buffer_linked_protonation": ("Buffer- or pH-dependent ΔH and K: observed values include linked proton uptake or release and need a linkage analysis",
   "pH dependence, proton-coupled binding, protonation, ΔnH, buffer ionization enthalpy, pKa shift"),
 "rule_idr_spolar_record_model_dependence": ("Residues folding upon binding estimated from temperature-dependent ITC for an intrinsically disordered protein: calibration-dependent",
   "Spolar Record, ΔCp, residues folding, IDP, IDR, coupled folding and binding, conformational entropy"),
 "rule_apparent_kd_conformational_exchange": ("Slow conformer interconversion or incomplete post-injection equilibration: Kd is an apparent, population- and timescale-dependent value",
   "proline isomerization, conformational selection, slow exchange, apparent Kd, incomplete equilibration"),
 "rule_disordered_context_bivalency": ("Truncation or motif isolation changes affinity: disordered context contributes through coupled folding and bivalent avidity",
   "truncation, fragment, flanking region, IDR, bivalent, motif, context dependence"),
 "rule_equilibrium_affinity_vs_kinetics": ("ITC Kd agrees with another equilibrium measurement but kinetics show slow association or long residence time",
   "kinetics, residence time, kon koff, slow binding, equilibrium versus kinetics, SPR"),
 "rule_multisite_architecture": ("Receptor and ligand architecture supports several contacts and the isotherm has two transitions or n near two: use a multisite model guided by architecture",
   "two transitions, n equals 2, multisite, sequential, calmodulin, ubiquitin chains, avidity"),
 "rule_multivalent_polymer_coverage": ("Polymer or oligosaccharide titration where n changes with ligand length or pH: effective molecular coverage",
   "polymer binding, polysaccharide, alginate, coverage, effective n, ligand length, overlapping sites"),
 "rule_delta_cp_not_affinity": ("Large negative ΔCp in a protein–DNA or protein–ligand interaction: not a unique report of affinity or hydrophobic burial",
   "large negative heat capacity, ΔCp, protein-DNA, hydrophobic burial, coupled folding"),
 "rule_compensation_correlation_bias": ("Strong negative ΔH versus −TΔS correlation across a ligand series: partly mathematical, test with ΔΔ analysis",
   "enthalpy-entropy compensation, ΔH-TΔS correlation, ligand series, compensation plot, ΔΔH ΔΔG"),
 "rule_realistic_itc_uncertainty": ("Confidence based only on a single regression fit or optimizer covariance: real uncertainty is larger",
   "confidence interval, standard error, covariance, bootstrap, Bayesian, replicate variability, realistic uncertainty"),
 "rule_nonspecific_dna_condition_dependence": ("Nonspecific protein–DNA binding under changed salt, pH, buffer, osmolyte or temperature: condition-dependent lattice binding",
   "nonspecific DNA binding, lattice binding, site size, salt dependence, Sso7d, 1:1 model inadequate"),
 "rule_broad_temperature_series": ("Broad temperature series with nonmonotonic affinity: ΔCp and van't Hoff analysis are not constant over the range",
   "temperature dependence, nonmonotonic affinity, curved van't Hoff, temperature-dependent ΔCp, wide temperature range"),
 "rule_nucleic_acid_ionic_condition": ("DNA, RNA or hybrid duplex ITC compared across salt concentration or backbone: condition- and backbone-specific thermodynamics",
   "duplex formation, hybridization, DNA RNA hybrid, PNA, salt dependence, ionic strength"),
 "rule_anion_specific_protein_dna": ("Protein–DNA affinity or enthalpy changes with salt identity, not only concentration: Hofmeister, hydration and osmotic effects",
   "anion effect, chloride versus glutamate, Hofmeister, osmotic, hydration, counterion release"),
 "rule_kinetic_itc_not_binding_isotherm": ("Thermal power keeps changing after injection because an enzymatic reaction proceeds: kinetic calorimetry, not a binding isotherm",
   "enzyme kinetics ITC, turnover, substrate injection, steady-state power, Michaelis-Menten by ITC"),
 "rule_membrane_thermogram_transition": ("Successive membrane injections change heat sign, slope or morphology threshold: a sequence of membrane transitions",
   "vesicle titration, permeabilization, reorientation, micellation, lipid-peptide, threshold"),
 "rule_membrane_apparent_partition_nonideal": ("Apparent membrane partition coefficient changes with ligand concentration or vesicle size: not automatically intrinsic",
   "partition coefficient, lipid binding, vesicle size, LUV, SUV, surface charge, nonideal partitioning"),
 "rule_stirring_artifact": ("Fitted enthalpy or curve shape changes with stirring rate or paddle geometry: mixing-dependent artifact",
   "stirring speed, rpm, mixing artifact, protein unfolding at high stirring, paddle"),
 "rule_self_association_dilution_model": ("Heat from dilution of an oligomer into lower concentration: needs an oligomerization model and a justified dilution baseline",
   "dimer dissociation, oligomer, dilution ITC, self-association, heptamer, monomer-oligomer"),
 "rule_covariant_carbohydrate_contacts": ("Ligand length, linkage or subsite occupancy changes carbohydrate-binding enthalpy: contacts are not independently additive",
   "lectin, carbohydrate, oligosaccharide, subsite mapping, glycoside hydrolase, additivity"),
 "rule_ligand_label_changes_heat": ("A labeled ligand gives a different enthalpy or curve from the native ligand: the label perturbs binding",
   "fluorescent label, tag, modified ligand, labeled versus native, antibody glycan"),
 "rule_metal_speciation_posthoc": ("Metal-ion ITC with buffers, chelators, redox-active ions, hydrolysis or precipitation: apparent parameters depend on speciation",
   "metal binding, zinc, copper, calcium, chelator, buffer competition, metal speciation, hydrolysis"),
 "rule_classical_fit_incomplete_state": ("A classical binding equation fits well but large solvation, stacking, folding or pre-equilibria are present: parameters lack unique mechanistic meaning",
   "good fit but linked equilibria, pre-equilibrium, stacking, solvation, nucleic acid folding, apparent parameters"),
 "rule_ligand_purity_before_n_adjustment": ("Implausible fitted n with uncertain ligand purity: verify purity and concentration before constraining n",
   "impurity, ligand purity, n too low, n too high, fixing n, concentration check"),
 "rule_nucleic_acid_cp_coupled_strands": ("Nucleic-acid duplex ΔCp varies with sequence, salt or temperature: coupled single-strand stacking can dominate",
   "DNA duplex ΔCp, single-strand stacking, strand melting, heat capacity of hybridization"),
 "rule_tight_binding_alternative_method": ("Direct isotherm nearly vertical because Kd is in the single-digit nanomolar range: use displacement ITC, lower concentration or an orthogonal method",
   "nanomolar Kd, tight binding, c above 1000, upper limit of ITC, thermal shift, displacement"),
}

# ------------------------------------------------------------ helpers
sources = {s["id"]: s for s in legacy["sources"]}


def cite_ids(ids):
    return ", ".join(ids)


def sentence(text: str) -> str:
    text = text.strip()
    return text if text.endswith((".", "?", "!")) else text + "."


def upper_first(text: str) -> str:
    # keep lowercase scientific tokens (c-value, n, pH, pKa) as written
    if not text or re.match(r"^(c-value|[b-z](?=[\s,;:.)\-])|pH|pKa|pI)", text):
        return text
    return text[:1].upper() + text[1:]


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def rule_entry(rule: dict) -> str:
    title, matches = RULES[rule["id"]]
    rid = "lr-" + slug(rule["id"].replace("rule_", "", 1))
    lines = [f"## {title}", f"id: {rid}", "kind: literature", "basis: literature", "status: draft",
             f"matches: {matches}", f"cite: {cite_ids(rule['source_ids'])}", f"legacy_id: {rule['id']}", ""]
    lines.append("Situation: " + upper_first(sentence(rule["trigger"])))
    lines.append("Interpretive implication: " + sentence(rule["conclusion"]))
    if rule.get("evidence_note"):
        lines.append("Evidence note: " + sentence(rule["evidence_note"]))
    if rule.get("recommended_language"):
        lines.append("Defensible framing: " + sentence(rule["recommended_language"]))
    if rule.get("avoid_language"):
        lines.append("Common over-claim: " + sentence(rule["avoid_language"]))
    if rule.get("confidence") and rule["confidence"] != "high":
        lines.append(f"Strength of support: {rule['confidence']}.")
    return "\n".join(lines) + "\n"


# ----------------------------------------------------- case studies
GREEK = [(r"\bdelta_Cp", "ΔCp"), (r"\bdelta_H", "ΔH"), (r"\bdelta_S", "ΔS"), (r"\bdelta_G", "ΔG"), (r"\bminus_T_delta_S", "−TΔS")]
UNIT_PHRASES = [  # order matters: longest first
    (r"_M_inverse_s_inverse", " (M^-1 s^-1)"), (r"_M_inv(?:erse)?_s_inv(?:erse)?", " (M^-1 s^-1)"),
    (r"_kJ_per_mol_K", " (kJ/(mol K))"), (r"_kJ_mol_K", " (kJ/(mol K))"),
    (r"_kcal_per_mol_per_M", " (kcal/mol per M)"),
    (r"_kcal_per_mol_K", " (kcal/(mol K))"), (r"_kcal_mol_K", " (kcal/(mol K))"),
    (r"_cal_per_mol_K", " (cal/(mol K))"), (r"_cal_mol_K", " (cal/(mol K))"),
    (r"_J_per_mol_K", " (J/(mol K))"), (r"_J_mol_K", " (J/(mol K))"),
    (r"_kJ_per_mol", " (kJ/mol)"), (r"_kJ_mol", " (kJ/mol)"),
    (r"_kcal_per_mol", " (kcal/mol)"), (r"_kcal_mol", " (kcal/mol)"),
    (r"_M_inverse", " (M^-1)"), (r"_M_inv\b", " (M^-1)"), (r"_s_inverse", " (s^-1)"), (r"_s_inv\b", " (s^-1)"),
    (r"_per_M$", " (per M)"), (r"_K$", " (K)"), (r"_C$", " (°C)"), (r"_uM$", " (µM)"), (r"_nM$", " (nM)"), (r"_percent$", " (%)"),
]
MOLAR_TOKEN = re.compile(r"(?:^|_)M(?=_|$)")


def fmt_num(x) -> str:
    if isinstance(x, bool):
        return str(x)
    if isinstance(x, int):
        return str(x)
    if x == 0:
        return "0"
    return f"{x:.3g}"


def fmt_with_error(value: float, error: float) -> str:
    """'value ± error' with both rounded to two significant figures of the error."""
    if not error:
        return fmt_num(value)
    decimals = max(0, 1 - math.floor(math.log10(abs(error))))
    if decimals == 0 and abs(error) >= 100:
        digits = 1 - math.floor(math.log10(abs(error)))  # negative: round to tens/hundreds
        return f"{round(value, digits):g} ± {round(error, digits):g}"
    return f"{value:.{decimals}f} ± {error:.{decimals}f}"


def molar_unit(values) -> tuple[float, str]:
    """Pick one SI prefix for a set of molar values (largest magnitude decides)."""
    a = max(abs(v) for v in values) if values else 0
    for scale, unit in ((1.0, "M"), (1e3, "mM"), (1e6, "µM"), (1e9, "nM"), (1e12, "pM")):
        if a * scale >= 1 or unit == "pM":
            return scale, unit
    return 1.0, "M"


def humanize(key: str) -> str:
    k = key
    k = re.sub(r"_at_(\d+)_(\d+)_K", r" at \1.\2 K", k)
    k = re.sub(r"_at_(\d+)C(?=_|$)", r" at \1 °C", k)
    k = re.sub(r"_(\d+)_(\d+)_K\b", r" \1.\2 K", k)
    for pat, rep in UNIT_PHRASES:
        k = re.sub(pat, rep, k)
    for pat, rep in GREEK:
        k = re.sub(pat, rep, k)
    return re.sub(r"\s+", " ", k.replace("_", " ")).strip()


def move_unit_to_end(label: str) -> str:
    """'ΔH (kcal/mol) at 288.15 K' -> 'ΔH at 288.15 K' + unit, returned as (label, unit)."""
    m = re.match(r"^(.*?) \(([^()]*(?:\([^()]*\))?[^()]*)\)(.*)$", label)
    return ((m.group(1) + m.group(3)).strip(), m.group(2)) if m else (label, "")


def is_num(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def render_params(params: dict) -> list[str]:
    """Readable 'label = value unit' strings; pairs X and X_error into 'value ± error'."""
    consumed = set()
    out = []
    for key, value in params.items():
        if key in consumed:
            continue
        if re.search(r"_errors?(_|$)", key) and re.sub(r"_errors?(?=_|$)", "", key, count=1) in params:
            continue  # rendered together with its value
        err_key = next((c for c in (re.sub(r"(?=(?:_(?:M|kJ_mol|J_mol_K|M_inv|kcal_mol|M_inverse|kJ_per_mol)$))", "_error", key, count=1), key + "_error")
                        if c != key and c in params and is_num(params[c])), None)
        molar = bool(MOLAR_TOKEN.search(key))
        label_src = MOLAR_TOKEN.sub("", key, count=1) if molar and not re.search(r"_M_inv", key) else key
        if molar and re.search(r"_M_inv", key):
            molar = False
        label, unit = move_unit_to_end(humanize(label_src))
        if molar and is_num(value):
            scale, unit = molar_unit([value])
            if err_key:
                text = fmt_with_error(value * scale, params[err_key] * scale)
                consumed.add(err_key)
            else:
                text = fmt_num(value * scale)
            out.append(f"{label} = {text} {unit}")
        elif molar and isinstance(value, list) and value and all(is_num(v) for v in value):
            scale, unit = molar_unit(value)
            out.append(f"{label} = " + "–".join(fmt_num(v * scale) for v in value) + f" {unit}")
        elif is_num(value):
            if err_key:
                text = fmt_with_error(value, params[err_key])
                consumed.add(err_key)
            else:
                text = fmt_num(value)
            out.append(f"{label} = {text}" + (f" {unit}" if unit else ""))
        elif isinstance(value, bool):
            out.append(f"{label}: {'yes' if value else 'no'}")
        elif isinstance(value, dict):
            inner = "; ".join(render_params(value))
            out.append(f"{label}: ({inner})")
        elif isinstance(value, list):
            sep = "–" if len(value) == 2 and all(is_num(v) for v in value) else ", "
            out.append(f"{label} = " + sep.join(fmt_num(v) if is_num(v) else str(v) for v in value) + (f" {unit}" if unit else ""))
        else:
            out.append(f"{label} = {value}" + (f" {unit}" if unit else ""))
    return out


def render_series(series: list[dict]) -> list[str]:
    rows = ["(" + ", ".join(render_params(row)) + ")" for row in series]
    return ["Series: " + "; ".join(rows) + "."]


CLASS_FIXES = [(r"\bprotein protein\b", "protein–protein"), (r"\bprotein small molecule\b", "protein–small molecule"),
               (r"\bprotein DNA\b", "protein–DNA"), (r"\bprotein RNA\b", "protein–RNA"), (r"\bprotein peptide\b", "protein–peptide"),
               (r"\bprotein polyanion\b", "protein–polyanion"), (r"\bprotein polysaccharide\b", "protein–polysaccharide"),
               (r"\bprotein ligand\b", "protein–ligand"), (r"\bmetal protein\b", "metal–protein"), (r"\bpeptide membrane\b", "peptide–membrane"),
               (r"\bsmall molecule DNA\b", "small molecule–DNA"), (r"\blectin small molecule\b", "lectin–small molecule"),
               (r"\bstructured RNA small molecule\b", "structured RNA–small molecule"), (r"\bpolymer nanoparticle\b", "polymer–nanoparticle"),
               (r"\b14 3 3\b", "14-3-3"), (r"\bmembrane protein metabolite\b", "membrane protein–metabolite"),
               (r"\bIDR\b", "IDR")]


def class_label(c: str) -> str:
    c = c.replace("_", " ")
    for pat, rep in CLASS_FIXES:
        c = re.sub(pat, rep, c)
    return c


def normalize(text: str) -> str:
    text = re.sub(r"\bDelta(?=[CGHS]|Delta)", "Δ", text)
    text = text.replace("ΔDelta", "ΔΔ")
    text = re.sub(r"(?<=\d) ?uM\b", " µM", text)
    text = re.sub(r"\buM\b", "µM", text)
    text = re.sub(r"(?<=\d) C(?![-\w])", " °C", text)
    return text


def case_title(case: dict) -> str:
    s = case.get("system")
    if isinstance(s, dict) and s.get("cell"):
        base = f"{s['cell']} with {s['syringe']}" if s.get("syringe") else s["cell"]
    else:
        base = case["id"].replace("case_", "").replace("_", " ")
    full = f"{base} ({class_label(case['interaction_class'])})"
    return normalize(full if len(full) <= 150 else base)


def bullets(label: str, items) -> str:
    if isinstance(items, str):
        items = [items]
    if not items:
        return ""
    if len(items) == 1:
        return f"{label}: {sentence(str(items[0]))}"
    return f"{label}:\n" + "\n".join(f"- {sentence(str(i))}" for i in items)


def est_tokens(text: str) -> int:
    pieces = re.findall(r"[A-Za-z]+|\d+|[^\sA-Za-z\d]", text)
    return int(max(sum(-(-len(p) // 4.5) if p[0].isalpha() else 1 for p in pieces), len(text) / 3.8))


SPLIT_TARGET = 430  # tokens per part, leaving room for header lines and the Sources line


# Values whose magnitude or unit looks implausible and could not be checked against the paper.
# They are left out of the emitted entry rather than propagated as fact; verify, then re-add by hand.
SUSPECT_VALUES = {
    "case_harmon_water_solvation_model_itc": {"reported_delta_G_s_oligo10_kcal_per_mol_per_base_pair"},
}


def case_blocks(case: dict, titles: dict) -> list[str]:
    s = case.get("system") or {}
    skip = SUSPECT_VALUES.get(case["id"], set())
    if skip and case.get("reported_parameters"):
        case = {**case, "reported_parameters": {k: v for k, v in case["reported_parameters"].items() if k not in skip}}
    blocks = []
    sysline = []
    if s.get("cell"):
        sysline.append(f"cell: {s['cell']}")
    if s.get("syringe"):
        sysline.append(f"syringe: {s['syringe']}")
    for k, v in s.items():
        if k not in ("cell", "syringe"):
            sysline.append(f"{humanize(k)}: {v}")
    if sysline:
        blocks.append("System: " + "; ".join(sysline) + ".")
    meta = []
    if case.get("model_reported"):
        meta.append(f"model reported: {str(case['model_reported']).replace('_', ' ')}")
    if case.get("temperature_K"):
        t = case["temperature_K"]
        meta.append(f"T = {t:g} K ({t - 273.15:.2f} °C)".replace(".00 °C", " °C").replace("0 °C)", " °C)") if False else f"T = {t:g} K ({t - 273.15:.1f} °C)")
    if meta:
        blocks.append("Conditions: " + "; ".join(meta) + ".")
    if case.get("reported_parameters"):
        blocks.append("Reported: " + "; ".join(render_params(case["reported_parameters"])) + ".")
    if case.get("reported_series"):
        blocks += render_series(case["reported_series"])
    blocks.append(bullets("Observations", case.get("reported_observations")))
    blocks.append(bullets("Authors' interpretation", case.get("author_interpretation")))
    if case.get("orthogonal_methods"):
        blocks.append("Orthogonal methods: " + ", ".join(str(m).replace("_", " ") for m in case["orthogonal_methods"]) + ".")
    cmp_ = case.get("comparison")
    if cmp_:
        ref = cmp_.get("reference_case_id")
        rest = {k: v for k, v in cmp_.items() if k != "reference_case_id"}
        parts = render_params(rest)
        head = "Comparison" + (f" with {titles.get(ref, ref)}" if ref else "")
        blocks.append(head + (": " + "; ".join(parts) + "." if parts else "."))
    ctx = case.get("derived_identifiability_context")
    if ctx:
        blocks.append("Identifiability context (compiler-derived): " + sentence(ctx.get("interpretation", ""))
                      + (" Basis: " + sentence(ctx["basis"]) if ctx.get("basis") else ""))
    for note in case.get("modeling_notes", []):
        blocks.append("Modeling note: " + sentence(note))
    blocks.append(bullets("Takeaway for interpretation", case.get("interpreter_takeaways")))
    if case.get("quality_flags"):
        blocks.append("Flags: " + ", ".join(f.replace("_", " ") for f in case["quality_flags"]) + ".")
    return [normalize(b) for b in blocks if b]


def split_blocks(blocks: list[str]) -> list[list[str]]:
    parts, cur, size = [], [], 0
    for b in blocks:
        t = est_tokens(b)
        if cur and size + t > SPLIT_TARGET:
            parts.append(cur)
            cur, size = [], 0
        cur.append(b)
        size += t
    if cur:
        parts.append(cur)
    return parts


def case_entries(case: dict, titles: dict) -> list[str]:
    title = case_title(case)
    base_id = "pr-" + slug(case["id"].replace("case_", "", 1))
    s = case.get("system") or {}
    keywords = [class_label(case["interaction_class"]), "literature case study"]
    keywords += [str(v) for v in s.values() if isinstance(v, str) and len(v) <= 70]
    if case.get("model_reported") and len(str(case["model_reported"])) <= 70:
        keywords.append(str(case["model_reported"]).replace("_", " "))
    keywords += [str(m).replace("_", " ") for m in case.get("orthogonal_methods", [])]
    text_all = json.dumps(case, ensure_ascii=False)
    if "Cp" in text_all:
        keywords.append("heat capacity change")
    if "delta_H" in text_all or "DeltaH" in text_all:
        keywords.append("binding enthalpy")
    matches = normalize(", ".join(dict.fromkeys(keywords)))
    parts = split_blocks(case_blocks(case, titles))
    out = []
    for i, blocks in enumerate(parts, start=1):
        suffix = "" if len(parts) == 1 else f" (part {i} of {len(parts)})"
        pid = base_id if i == 1 else f"{base_id}-{i}"
        related = FAMILIES_BY_CASE.get(case["id"], [])
        topics = class_label(case["interaction_class"]) + ("; related study sets: " + ", ".join(related) if related else "")
        header = [f"## {title}{suffix}", f"id: {pid}", "kind: precedent", "basis: literature", "status: draft",
                  "topics: " + topics, f"matches: {matches}",
                  f"cite: {case['source_id']}", f"legacy_id: {case['id']}", ""]
        out.append("\n".join(header + blocks) + "\n")
    return out


def main() -> None:
    entries_dir = ROOT / "entries"
    entries_dir.mkdir(exist_ok=True)
    missing = [r["id"] for r in legacy["interpretation_rules"] if r["id"] not in RULES]
    assert not missing, f"no title/keywords mapped for rules: {missing}"
    intro = ("<!-- Migrated from legacy/itc_knowledge_base_expanded.json by tools/migrate_legacy.py.\n"
             "     These files are now the source of truth; edit them directly. -->\n\n")
    (entries_dir / "90-literature-rules.md").write_text(
        intro + "\n".join(rule_entry(r) for r in legacy["interpretation_rules"]), encoding="utf-8")
    titles = {c["id"]: case_title(c) for c in legacy["case_studies"]}
    (entries_dir / "91-literature-precedents.md").write_text(
        intro + "\n".join(e for c in legacy["case_studies"] for e in case_entries(c, titles)), encoding="utf-8")
    print(f"migrated {len(legacy['interpretation_rules'])} rules and {len(legacy['case_studies'])} case studies")


if __name__ == "__main__":
    main()
