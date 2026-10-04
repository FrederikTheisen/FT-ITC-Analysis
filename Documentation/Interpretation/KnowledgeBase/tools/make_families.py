#!/usr/bin/env python3
"""Generate entries/80-study-families.md: one overview entry per set of related studies.

Why: a precedent entry describes one case, and nothing links related studies from different papers.
A family entry says what the related studies collectively show and lists each member study with its
full citation and one finding, so a single retrieval returns the set and the model can follow up on
a specific system. Membership and member citations come from the legacy case data (no retyping); the
overview text is hand-written (it synthesizes the members' findings, which stay in the precedent entries) and may be edited in the generated file afterwards.

Re-running overwrites entries/80-study-families.md; do not re-run after hand edits. Precedent entries
get a 'related study sets' line from tools/migrate_legacy.py, which imports FAMILIES from here.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# id suffix, title, short name, member case indices (legacy case_studies order), keywords, overview
FAMILIES = [
 ("14-3-3-phosphopeptides", "Related studies: 14-3-3 complexes with phosphopeptides and small-molecule stabilizers", "14-3-3 phosphopeptide complexes",
  [82, 83, 84, 85, 86, 87, 88, 89, 39],
  "14-3-3 binding, phosphopeptide, hDMX, hDM2, MDM2, ERα, fusicoccin, molecular glue, isoform differences, bivalent phosphopeptide, related 14-3-3 studies",
  "A single phosphosite gives measurable, enthalpically favorable 14-3-3 binding without a bivalent model, and site identity and local sequence context can move affinity by more than tenfold. Multisite peptides can return a subunit-normalized stoichiometry near 0.5 without bridging both grooves, so geometry needs structural or orthogonal data. Closely related isoforms differ in affinity for the same peptide, so an isoform-specific Kd should not be generalized to the family. For stabilizer (molecular-glue) experiments the Kd is conditional on the stabilizer concentration, and label-free ITC is a useful reference for labeled-peptide assays. Slowly interconverting peptide conformers (proline cis/trans) can make a one-site Kd apparent."),
 ("idp-coupled-folding", "Related studies: intrinsically disordered regions binding by coupled folding, context and dynamics", "disordered-region binding",
  [8, 32, 33, 38, 39, 40, 81, 109],
  "intrinsically disordered, IDP IDR binding, coupled folding and binding, fuzzy complex, flanking region, bivalent motif, proline isomerization, splice variant, near zero enthalpy, related IDP studies",
  "A good one-site fit can coexist with persistent dynamics (fuzzy or partially folded complexes), so it does not prove a rigid complex. Flanking disordered sequence and second motifs can add affinity while raising the conformational entropy cost, and truncations can change affinity a lot while a calorimetric transition remains. A large negative ΔCp supports coupled folding and burial, but folding-residue estimates depend on the calibration and need structural or spectroscopic support. Near-zero ΔH at some temperatures makes ITC uninformative there; temperature variation can restore signal, and extrapolated Kd values are derived, not measured. Slow conformer exchange gives apparent Kd values, and a splice variant can change affinity and kinetics."),
 ("protein-dna-thermodynamics", "Related studies: protein–DNA thermodynamics (heat capacity, salt, lattice binding, temperature)", "protein–DNA thermodynamics",
  [41, 45, 48, 49, 52, 53, 95],
  "protein DNA binding thermodynamics, heat capacity protein DNA, trp repressor, Sso7d, Taq polymerase, integrase, IHF, salt dependence, lattice binding, temperature series, related protein DNA studies",
  "A large negative ΔCp is not a proxy for high affinity and not by itself proof of hydrophobic burial: it can reflect coupled local folding, a weak stereospecific interface can show it, and temperature-dependent apparent ΔCp can arise from component heat capacities and restricted motions. Nonspecific binding is better described by lattice models than 1:1. Salt effects need more than a counterion-release slope (anion identity and hydration matter), and strongly favorable enthalpy can coexist with large solvent terms. Thermophilic origin does not imply weak binding at low temperature, a constant ΔCp should not be extrapolated when affinity is nonmonotonic, and weak secondary modes can leave late heats above baseline."),
 ("rna-binding", "Related studies: RNA–ligand and RNA–protein ITC (folding state, metals, protonation, kinetics)", "RNA binding",
  [50, 76, 80, 103, 16, 17, 78],
  "RNA ITC, riboswitch, aptamer, aminoglycoside, kissing complex, Z-RNA ADAR1, RNA folding state, magnesium, related RNA studies",
  "For RNA, preparation and folding state are part of the measurement: a clean ligand curve does not show that the RNA is uniformly folded, and loss of signal after mutation can mean loss of a binding-competent fold rather than a direct contact. Metal ions seen in structures belong in the interpretation. pH-dependent ΔH and affinity (aminoglycosides) can reflect ligand protonation and counterion release together, and a higher-affinity analogue need not be better in every thermodynamic component. Intermediate stability can be set by koff rather than kon. NMR can locate the ligand and conformational change that make a ΔCp difference interpretable. Multisite RNA binding can be consistent with complexity without resolved site constants, and very large uncertainties argue against precise mechanistic claims."),
 ("dna-duplex-and-ligands", "Related studies: nucleic-acid duplex formation, small ligands and metal ions binding DNA", "DNA duplexes and DNA ligands",
  [37, 51, 54, 77, 96],
  "DNA duplex formation, hybridization, DNA RNA hybrid, groove binder, metal ion DNA, aluminum DNA, aptamer lysozyme, single strand stacking, related DNA ligand studies",
  "Duplex thermodynamics depend strongly on temperature and salt even for short defined duplexes, and ΔCp is not transferable between DNA/DNA and DNA/RNA; an observed ΔCp can report coupled single-strand stacking instead of the final interface, and a constant per-base-pair ΔCp fails across sequences and ionic strengths. Buffer-dependent ΔH in DNA–ligand binding indicates linked protonation, and strong primary binding can coexist with weaker nonspecific binding. Endothermic signals can represent strongly favorable, entropy-driven binding, particularly for metal ions on polyelectrolytes where speciation and electrostatics dominate. Electrostatic nucleic-acid interactions benefit from an orthogonal method that does not rely on heat."),
 ("peptide-membrane", "Related studies: peptide and amphiphile interactions with lipid membranes", "peptide–membrane interactions",
  [35, 57, 58, 59, 60, 62],
  "peptide membrane ITC, antimicrobial peptide, magainin, melittin, mastoparan, penetratin, Tat, partition coefficient, pore formation, vesicle size, surface electrostatics, related membrane studies",
  "Membrane thermograms are often composites of partitioning, remodeling, pore formation, micellation, aggregation and dilution, so a single binding model can be inappropriate; thresholds or sign changes can mark permeabilization or translocation rather than saturation. Apparent affinity of charged peptides is dominated by surface electrostatics, so effective charge differs from formal charge and a feature near charge neutralization can reflect vesicle aggregation. Vesicle size and curvature change the enthalpy-entropy split without much change in ΔG, partitioning can show strong compensation, and translocation time is an experimental variable. Multistage traces need controls and orthogonal morphology data before one Kd is assigned."),
 ("detergents-and-membrane-proteins", "Related studies: detergent partitioning and membrane-protein lipid recognition", "detergents and membrane proteins",
  [61, 66, 104],
  "detergent membrane partitioning, SDS POPC, solubilization, nanodisc, PI4P recognition, membrane protein lipid specificity, related detergent studies",
  "A concentration-dependent apparent partition coefficient can indicate ligand-induced membrane remodeling instead of several independent affinity classes, so intrinsic partitioning should be separated from high-coverage, perturbed-membrane behavior. Charged-detergent solubilization combines partitioning with electrostatic surface effects and phase coexistence, and kinetic equilibration decides whether an experiment reports thermodynamic phase behavior or history-dependent half-sided binding. For membrane proteins, calorimetry needs a structural method that distinguishes specific lipid recognition from bulk-surface association."),
 ("metal-ions", "Related studies: metal-ion binding to proteins and clusters (multisite, competition, enthalpy versus affinity)", "metal-ion binding",
  [28, 70, 71, 110, 54],
  "metal ion binding ITC, calcium protein, metallothionein, zinc copper cluster, cobalt albumin, potassium sequential, competitive displacement chelator, related metal studies",
  "Global multisite fits can conceal sign changes and coupling between metal sites, and one ion can change another's apparent cooperativity through preloading and competition. A later step with more favorable ΔH is not positive cooperativity unless its affinity is also higher. Very tight or chemically unstable metal binding needs competitive displacement with explicit chelator and buffer equilibria, and clustered metals should not be reduced to one average K and ΔH. In overlapping sequential fits typically only the first step lies in the optimal c range; later steps are model-dependent, and published versus refitted magnitudes should be disclosed. Metal binding to polyelectrolytes is dominated by speciation and electrostatics."),
 ("carbohydrate-recognition", "Related studies: carbohydrate recognition by lectins, binding modules and glycoside hydrolases", "carbohydrate recognition",
  [22, 23, 24, 42, 44, 63, 64, 65, 74],
  "lectin carbohydrate ITC, galectin, glycoside hydrolase subsites, carbohydrate binding module, oligosaccharide length, alginate, LecB mannose, multivalent lectin, related carbohydrate studies",
  "Ligand length and linkage change the contact architecture instead of adding independent contacts, and ligand-length series can reveal extended sites even when isotherms look one-site. Multivalent oligomers can show a simple-looking stoichiometry that differs from the subunit count, and for polymers n can describe an effective coverage that changes with pH or chain length. ΔCp helps subsite assignment only with mutational or orthogonal evidence, and favorable entropy in specific carbohydrate recognition should not be equated with nonspecific hydrophobic association."),
 ("method-comparison-surface", "Related studies: ITC compared with SPR, mass spectrometry and fluorescence methods", "ITC versus SPR, MS and fluorescence",
  [90, 91, 92, 93, 94, 98, 99],
  "ITC SPR comparison, method comparison, interlaboratory, carbonic anhydrase sulfonamide, mass spectrometry binding, fluorescence polarization, anisotropy, orthogonal validation, related method comparison studies",
  "Agreement between solution and surface methods supports a robust affinity only after mass transport and immobilization artifacts are tested, and agreement is better judged across laboratories and across the assay dynamic range than from one matched Kd. Agreement in trends can validate mutation effects while SPR separates association from dissociation contributions. Fluorescent readouts need probe-specific controls because quenching can invalidate affinities even when ITC is clean, and when fluorescence and ITC support different site models the discrepancy should be kept as evidence. An orthogonal assay supports a non-unit ITC stoichiometry before architecture is assigned."),
 ("kinetics-and-pathway", "Related studies: ITC with stopped-flow, SPR and NMR exchange (thermodynamics versus pathway)", "ITC with kinetics and exchange methods",
  [101, 102, 105, 106, 107, 108],
  "ITC stopped flow, NMR exchange, conformational selection versus induced fit, closure check, kon koff, tight inhibitor kinetics, two site agreement, related kinetics studies",
  "Equilibrium calorimetry combined with NMR exchange or stopped-flow kinetics separates thermodynamic stabilization from kinetic accessibility. A kinetics-derived equilibrium constant close to the ITC value is a closure check for a proposed bimolecular model, and agreement for both sites of a two-site system supports the model far more than agreement for a composite affinity. Tight inhibitors need kinetic methods in addition to an equilibrium fit. Agreement of an NMR exchange rate with a stopped-flow limiting rate can distinguish conformational selection from induced fit, while a failed or range-limited kinetic assay is informative and not confirmation."),
 ("site-resolved-methods", "Related studies: ITC with site-resolved methods (NMR, anisotropy, ultracentrifugation) for sites, cooperativity and dynamics", "ITC with site-resolved methods",
  [95, 96, 97, 100, 103, 104, 109],
  "ITC NMR combined, site specific thermodynamics, anisotropy, AUC, cooperativity resolved by NMR, nanodisc, IDR dynamics, orthogonal structural validation, related site-resolved studies",
  "A good ITC curve can remain non-identifying for strongly cooperative multisite systems, so site-resolved data may be needed to separate intrinsic affinity from cooperativity. ΔCp differences between nucleic-acid constructs gain mechanistic value when NMR locates the ligand and the conformational transition; for membrane proteins a structural method distinguishes specific lipid recognition from surface association. A stable disordered-region complex can retain fast internal motion, modest Kd differences between methods are interpretable when assays also show shared site usage and competition, and fluorescence-derived temperature trends should be checked against calorimetry."),
 ("multisite-and-stoichiometry", "Related studies: stoichiometry puzzles, multisite binding and valence", "multisite binding and stoichiometry",
  [12, 13, 16, 17, 19, 30, 31, 43, 44],
  "fitted n unusual, multisite binding, multivalent, dimeric receptor stoichiometry, calmodulin, survivin, ubiquitin NEMO, ADAR1, non-integer stoichiometry, related stoichiometry studies",
  "A large or non-integer fitted n can be physically meaningful in multivalent surface binding, with dimeric partners (n near 2 for one dimer per ligand) or with several occupied sites; it should be read against architecture and oligomeric state before being called anomalous. Changes in n alongside designed changes in architecture can be informative, similar n does not mean similar energetics, and fold-change comparisons can be robust without a central thermodynamic decomposition. Two-transition isotherms encode sequential or multisite binding, which a single average Kd loses. A more complex model does not make site parameters reliable when uncertainties are enormous, and orthogonal stoichiometry data support interpretation."),
 ("itc-methodology", "Related studies: ITC methodology (concentration effects, uncertainty, compensation, artifacts, solvation, purity)", "ITC methodology",
  [29, 46, 47, 67, 72, 73, 34],
  "ITC methodology, concentration dependence, Bayesian uncertainty, compensation dataset, stirring artifact, ligand impurity, solvation model, blank subtraction, related methods studies",
  "Reproducibility at one concentration does not exclude concentration-dependent bias or coupled equilibria. Optimizer covariance intervals understate real uncertainty and should include concentration, baseline, replicate and control variation. ΔH versus −TΔS correlations carry mathematical coupling, and compensation is not universal. Stirring is a measurement variable that can create aggregation or unfolding heats. A good classical fit does not prove complete state variables (solvation, pre-equilibria), unexpected n should trigger purity and concentration checks (ligand purity can matter more than protein purity for enthalpy), and composite or non-monotonic titrations need blank subtraction."),
 ("detection-limits", "Related studies: detection limits (very high c, near-zero ΔH, weak secondary sites, no binding detected, ITC-silent fragments)", "ITC detection limits",
  [6, 7, 8, 9, 10, 11, 40, 79, 81],
  "detection limit ITC, no binding detected, high c, near zero enthalpy, weak secondary site, ITC silent fragment, tight binding, upper bound Kd, related detection limit studies",
  "Evidence that an interaction occurs is separate from confidence in fitted parameters: a pathological isotherm lowers quantitative confidence while the qualitative interaction may stand. High-c data fix stoichiometry and enthalpy but not Kd, and a converged nanomolar Kd is not automatically credible. Near-zero ΔH makes ITC uninformative at some temperatures, although temperature variation can restore signal and an extrapolated Kd is derived. A weak secondary site missed by ITC can be seen by another method, 'no binding detected' is a detection statement and not proof of absence, and an ITC-silent fragment can still make weak contacts."),
 ("series-and-mutants", "Related studies: ligand series and mutants (reading changes in thermodynamic signature)", "ligand series and mutants",
  [0, 1, 2, 3, 4, 5, 22, 23, 24, 25, 26, 27, 75, 69],
  "ligand series, mutant thermodynamics, structure activity thermodynamics, trimethyllysine readers, aldose reductase, PanD resistance, MITF fragments, antibody scaffold, related series studies",
  "A trend across a chemical series is stronger mechanistic evidence than the sign of ΔH from one ligand, and counterexamples should be kept. A mutation-induced affinity change is stronger evidence when consistent with kinetic or structural change, and a small Kd change can accompany a large kinetic change. Similar poses can have different thermodynamics through small geometry or water-network changes, identical binding-site sequences can behave differently in another scaffold or oligomeric context, and an unfavorable entropy term does not imply weak binding when enthalpy compensates. Ionizable ligands need buffer and pH control, and labeled and unlabeled ligands are not automatically interchangeable."),
 ("self-assembly-and-supramolecular", "Related studies: self-assembly, dilution experiments and supramolecular systems", "self-assembly and supramolecular systems",
  [18, 20, 21, 36, 55],
  "self assembly ITC, dilution experiment, dimer dissociation, heptamer, host guest, entropy driven endothermic, critical aggregation concentration, related self assembly studies",
  "Dilution ITC reports self-assembly thermodynamics, but the sign refers to disassembly, an oligomerization model is required, and the lack of a clear endpoint can prevent a critical-aggregation-concentration estimate. Endothermic assembly can be entropy-driven through water release, and endothermic binding can be strongly favorable. Per-contact energy decompositions in host–guest systems are model-dependent estimates, and the fitted stoichiometry and enthalpy of self-association refer to the association model, not ordinary ligand occupancy."),
 ("kinetic-itc", "Related studies: kinetic ITC (enzyme rates and inhibitor kinetics from heat flow)", "kinetic ITC",
  [56, 68],
  "kinetic ITC, enzyme kinetics by calorimetry, single injection, inhibitor kinetics, reversible covalent, time dependent heat flow, related kinetic ITC studies",
  "A time-dependent ITC trace can be an enzyme-rate experiment, so fitting it with a binding isotherm is inappropriate; kinetic ITC must account for calorimeter response time, substrate depletion and reaction-associated control heats. A high-affinity inhibitor can be characterized kinetically even when an equilibrium isotherm would be too steep, and reversible covalent inhibition can give biphasic heat-flow kinetics that a single exponential or equilibrium fit obscures."),
]

_legacy = json.loads((ROOT / "legacy" / "itc_knowledge_base_expanded.json").read_text(encoding="utf-8"))
_cases = _legacy["case_studies"]
_sources = {s["id"]: s for s in _legacy["sources"]}

# case id -> short names of the families it belongs to (used by migrate_legacy.py for cross-links)
FAMILIES_BY_CASE: dict[str, list[str]] = {}
for _fid, _title, _short, _members, _kw, _text in FAMILIES:
    for _i in _members:
        FAMILIES_BY_CASE.setdefault(_cases[_i]["id"], []).append(_short)


def _normalize(text: str) -> str:
    text = re.sub(r"\bDelta(?=[CGHS])", "Δ", text)
    return re.sub(r"(?<=\d) ?uM\b", " µM", text)


def _clip(text: str, limit: int) -> str:
    text = _normalize(text.strip())
    if len(text) <= limit:
        return text if text.endswith((".", "?", "!")) else text + "."
    cut = text[:limit].rsplit(" ", 1)[0].rstrip(",;:")
    return cut + "…"


def member_lines(indices: list[int]) -> tuple[list[str], list[str]]:
    by_paper: dict[str, list[dict]] = {}
    for i in indices:
        by_paper.setdefault(_cases[i]["source_id"], []).append(_cases[i])
    lines, cites = [], []
    for sid, cases in by_paper.items():
        src = _sources[sid]
        extra = f" [{len(cases)} cases]" if len(cases) > 1 else ""
        lines.append(f"- \"{src['title'].rstrip('. ')}\" ({src['journal']}, {src['year']}){extra}")
        cites.append(sid)
    return lines, cites


def family_entry(fid, title, short, members, keywords, text) -> str:
    lines, cites = member_lines(members)
    title = title.removeprefix("Related studies: ")  # the emitted label already says "Related studies"
    title = title[:1].upper() + title[1:]
    header = [f"## {title}", f"id: sf-{fid}", "kind: family", f"topics: related studies; {short}", "basis: literature",
              "status: draft", f"matches: {keywords}", f"cite: {', '.join(dict.fromkeys(cites))}", ""]
    body = [f"Overview: {text}",
            f"Studies in this set ({len(lines)}):",
            *lines,
            "Each study's individual cases, reported values and findings are in separate precedent entries (search by system name)."]
    return "\n".join(header + body) + "\n"


def main() -> None:
    intro = ("<!-- Generated by tools/make_families.py from legacy case data; the overview paragraphs are hand-written.\n"
             "     This file is now the source of truth; edit it directly. -->\n\n")
    out = ROOT / "entries" / "80-study-families.md"
    out.write_text(intro + "\n".join(family_entry(*f) for f in FAMILIES), encoding="utf-8")
    print(f"wrote {len(FAMILIES)} family entries to {out.name}")


if __name__ == "__main__":
    main()
