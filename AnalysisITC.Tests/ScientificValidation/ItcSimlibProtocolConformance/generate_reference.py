#!/usr/bin/env python3
"""Generate numerical regression fixtures using itcsimlib and local equations.

This adapter deliberately replaces itcsimlib's experiment bookkeeping.  The
equilibrium calculation is supplied by the pinned external itcsimlib OneMode
model, while the concentrations and injection heats are evaluated with the
MicroCal protocol stated in the FT-ITC manual and implementation.  It does not
import FT-ITC or call any FT-ITC code. This is not independent external
forward-model validation.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys


ITCSIMLIB_COMMIT = "654d89372f86d833f2b5fb1b3bd0d73780a80027"
CALORIE_JOULES = 4.184


def assert_pinned_source(source: Path) -> None:
    revision = subprocess.check_output(
        ["git", "-C", str(source), "rev-parse", "HEAD"], text=True
    ).strip()
    if revision != ITCSIMLIB_COMMIT:
        raise RuntimeError(f"Expected itcsimlib {ITCSIMLIB_COMMIT}; found {revision}")
    subprocess.run(["git", "-C", str(source), "diff", "--exit-code", "HEAD"], check=True)


def ft_microcal_states(cell_molar, syringe_molar, cell_liters, injection_liters):
    """Return post-injection states using the untruncated MicroCal mass balance.

    For cumulative relative volume u, cell material is retained by
    (1 - u/2)/(1 + u/2), and titrant concentration is u / (1 + u/2) times
    the syringe concentration.  These are intentionally evaluated directly
    from the cumulative volume rather than through itcsimlib's native driver.
    """
    cumulative_liters = 0.0
    states = []
    for volume_liters in injection_liters:
        cumulative_liters += volume_liters
        u = cumulative_liters / cell_liters
        if u >= 2.0:
            raise ValueError("MicroCal displacement is undefined at two cell volumes")
        half_u = u / 2.0
        states.append({
            "Macromolecule": cell_molar * (1.0 - half_u) / (1.0 + half_u),
            "Ligand": syringe_molar * u / (1.0 + half_u),
        })
    return states


def injection_heats(states, injection_liters, cell_liters, normalized_heat, initial_content=0.0):
    """Convert post-injection heat contents to integrated heats.

    This is the MicroCal mean-displacement relation: the observed injection
    heat is the new cell heat content, plus the displaced average content,
    minus the preceding cell heat content.
    """
    previous_content = initial_content
    heats = []
    for state, volume_liters, molar_heat in zip(states, injection_liters, normalized_heat):
        content = cell_liters * state["Macromolecule"] * molar_heat
        heat = content + (volume_liters / cell_liters) * ((content + previous_content) / 2.0) - previous_content
        heats.append(heat)
        previous_content = content
    return heats


def bisect_zero(function, lower, upper, iterations=256):
    """Solve a bracketed monotonic scalar mass balance without FT-ITC code."""
    lower_value, upper_value = function(lower), function(upper)
    if abs(lower_value) < 1e-22:
        return lower
    if abs(upper_value) < 1e-22:
        return upper
    if lower_value * upper_value > 0:
        raise RuntimeError("External mass balance was not bracketed")
    for _ in range(iterations):
        middle = (lower + upper) / 2.0
        value = function(middle)
        if abs(value) < 1e-22 or middle == lower or middle == upper:
            return middle
        if value * lower_value > 0:
            lower, lower_value = middle, value
        else:
            upper, upper_value = middle, value
    return (lower + upper) / 2.0


def sequential_molar_heat(states, constants, step_enthalpies):
    """Independent macroscopic binding-polynomial equilibrium calculation."""
    cumulative_enthalpies = []
    total = 0.0
    for enthalpy in step_enthalpies:
        total += enthalpy
        cumulative_enthalpies.append(total)

    heats = []
    for state in states:
        macromolecule, ligand = state["Macromolecule"], state["Ligand"]
        def residual(free_ligand):
            weight, partition, bound = 1.0, 1.0, 0.0
            for index, constant in enumerate(constants, start=1):
                weight *= constant * free_ligand
                partition += weight
                bound += index * weight
            return free_ligand + macromolecule * bound / partition - ligand

        free = bisect_zero(residual, 0.0, ligand)
        weights = [1.0]
        for constant in constants:
            weights.append(weights[-1] * constant * free)
        partition = sum(weights)
        heats.append(sum(weights[index] / partition * cumulative_enthalpies[index - 1]
                         for index in range(1, len(weights))))
    return heats


def competitive_molar_heat(states, initial_macromolecule, n, ka_a, h_a, competitor_ratio, ka_b, h_b):
    """Independent two-ligand, one-site mass balance for prebound competition."""
    def heat_for(macromolecule, ligand):
        sites = n * macromolecule
        competitor = competitor_ratio * sites
        def residual(free_sites):
            free_a = ligand / (1.0 + ka_a * free_sites)
            free_b = competitor / (1.0 + ka_b * free_sites)
            return free_sites + ka_a * free_sites * free_a + ka_b * free_sites * free_b - sites

        free_sites = bisect_zero(residual, 0.0, sites)
        free_a = ligand / (1.0 + ka_a * free_sites)
        free_b = competitor / (1.0 + ka_b * free_sites)
        fraction_a = ka_a * free_sites * free_a / sites
        fraction_b = ka_b * free_sites * free_b / sites
        return n * (h_a * fraction_a + h_b * fraction_b)

    initial_heat = heat_for(initial_macromolecule, 0.0)
    return [heat_for(state["Macromolecule"], state["Ligand"]) for state in states], initial_heat


def dimer_concentration(total_molar, association_constant):
    if total_molar <= 0.0:
        return 0.0
    monomer = 2.0 * total_molar / (1.0 + math.sqrt(1.0 + 8.0 * association_constant * total_molar))
    return association_constant * monomer * monomer


def dissociation_heats(syringe_molar, states, injection_liters, cell_liters, association_constant, enthalpy):
    """Independent dimerization/dilution calculation for FT-ITC's dissociation model."""
    syringe_dimer = dimer_concentration(syringe_molar, association_constant)
    previous_ligand = 0.0
    heats = []
    for state, volume in zip(states, injection_liters):
        post_ligand = state["Ligand"]
        pre_dimer = dimer_concentration(previous_ligand, association_constant)
        post_dimer = dimer_concentration(post_ligand, association_constant)
        moles_before = volume * syringe_dimer + (cell_liters - volume) * pre_dimer
        moles_after = cell_liters * post_dimer
        heats.append(enthalpy * (moles_after - moles_before))
        previous_ligand = post_ligand
    return heats


def generate(source: Path, output: Path) -> None:
    assert_pinned_source(source)
    sys.path.insert(0, str(source))
    from itcsimlib.model_independent import NModes, OneMode
    from itcsimlib.thermo import dG_from_Kd
    output.mkdir(parents=True, exist_ok=True)

    cases = [
        dict(id="one-site-exothermic", model="OneSetOfSites", generator="itcsimlib.OneMode",
             cell_molar=20e-6, syringe_molar=500e-6, count=40,
             n=[1.1], logka=[6.2], enthalpy_j_per_mol=[-32000.0]),
        dict(id="two-independent-sites", model="TwoSetsOfSites", generator="itcsimlib.NModes",
             cell_molar=20e-6, syringe_molar=2e-3, count=60,
             n=[1.0, 1.0], logka=[6.4, 5.0], enthalpy_j_per_mol=[-25000.0, 15000.0]),
        dict(id="competitive-binding", model="CompetitiveBinding", generator="independent-two-ligand-mass-balance",
             cell_molar=20e-6, syringe_molar=500e-6, count=40,
             n=[1.15], logka=[7.0], enthalpy_j_per_mol=[-18000.0],
             competitor=dict(concentration_molar=30e-6, logka=6.2, enthalpy_j_per_mol=-8000.0)),
        *(dict(id=f"sequential-{count}", model="SequentialBindingSites", generator="independent-macroscopic-binding-polynomial",
               cell_molar=20e-6, syringe_molar=2e-3, count=60, n=[],
               logka=[6.4, 5.7, 5.0, 4.3][:count],
               enthalpy_j_per_mol=[-30000.0, 20000.0, -18000.0, 14000.0][:count])
          for count in (2, 3, 4)),
        dict(id="dissociation", model="Dissociation", generator="independent-dimerization-mass-balance",
             cell_molar=0.0, syringe_molar=1e-3, count=50, n=[], logka=[5.6],
             enthalpy_j_per_mol=[-24000.0]),
    ]

    references = []
    for case in cases:
        case["temperature_kelvin"] = 298.15
        case["cell_liters"] = 200e-6
        case["injection_liters"] = [1e-6] * case["count"]
        states = ft_microcal_states(case["cell_molar"], case["syringe_molar"],
                                     case["cell_liters"], case["injection_liters"])
        constants = [10.0 ** value for value in case["logka"]]
        enthalpies = case["enthalpy_j_per_mol"]

        if case["generator"] == "itcsimlib.OneMode":
            model = OneMode(units="J")
            model.set_param("n", case["n"][0])
            model.set_param("dG", dG_from_Kd(1.0 / constants[0], case["temperature_kelvin"]))
            model.set_param("dH", enthalpies[0])
            model.set_param("dCp", 0.0)
            normalized_heat = model.Q(case["temperature_kelvin"], case["temperature_kelvin"], states)
            heats_joules = injection_heats(states, case["injection_liters"], case["cell_liters"], normalized_heat)
        elif case["generator"] == "itcsimlib.NModes":
            model = NModes(modes=len(constants), units="J")
            model.precision = 1e-24
            for index, (n, constant, enthalpy) in enumerate(zip(case["n"], constants, enthalpies), start=1):
                model.set_param(f"n{index}", n)
                model.set_param(f"dG{index}", dG_from_Kd(1.0 / constant, case["temperature_kelvin"]))
                model.set_param(f"dH{index}", enthalpy)
                model.set_param(f"dCp{index}", 0.0)
            normalized_heat = model.Q(case["temperature_kelvin"], case["temperature_kelvin"], states)
            heats_joules = injection_heats(states, case["injection_liters"], case["cell_liters"], normalized_heat)
        elif case["model"] == "CompetitiveBinding":
            competitor = case["competitor"]
            competitor_ratio = competitor["concentration_molar"] / (case["n"][0] * case["cell_molar"])
            normalized_heat, initial_normalized_heat = competitive_molar_heat(
                states, case["cell_molar"], case["n"][0], constants[0], enthalpies[0], competitor_ratio,
                10.0 ** competitor["logka"], competitor["enthalpy_j_per_mol"])
            initial_content = case["cell_liters"] * case["cell_molar"] * initial_normalized_heat
            heats_joules = injection_heats(states, case["injection_liters"], case["cell_liters"],
                                            normalized_heat, initial_content)
        elif case["model"] == "SequentialBindingSites":
            normalized_heat = sequential_molar_heat(states, constants, enthalpies)
            heats_joules = injection_heats(states, case["injection_liters"], case["cell_liters"], normalized_heat)
        elif case["model"] == "Dissociation":
            heats_joules = dissociation_heats(case["syringe_molar"], states, case["injection_liters"],
                                               case["cell_liters"], constants[0], enthalpies[0])
        else:
            raise RuntimeError(f"No generator for {case['id']}")

        if not all(math.isfinite(value) for value in heats_joules):
            raise RuntimeError(f"Non-finite heat for {case['id']}")
        heats_microcalories = [value / CALORIE_JOULES * 1e6 for value in heats_joules]
        dh = "\n".join([
            "10", f"0,{len(heats_joules)},0,0,0",
            f"25,{case['cell_molar'] * 1000:.17g},{case['syringe_molar'] * 1000:.17g},0.2,0",
            "0", "0", *(f"1,{heat:.17g}" for heat in heats_microcalories), "",
        ]).encode("utf-8")
        dh_path = output / f"{case['id']}.DH"
        dh_path.write_bytes(dh)
        references.append({
            "id": case["id"], "model": case["model"], "generator": case["generator"],
            "temperature_kelvin": case["temperature_kelvin"], "cell_liters": case["cell_liters"],
            "cell_molar": case["cell_molar"], "syringe_molar": case["syringe_molar"],
            "injection_count": case["count"], "injection_liters_each": 1e-6,
            "n": case["n"], "logka": case["logka"], "enthalpy_j_per_mol": enthalpies,
            "competitor": case.get("competitor"), "file": dh_path.name,
            "sha256": hashlib.sha256(dh).hexdigest(),
        })

    source_files = ("itcsimlib/model_independent.py", "itcsimlib/thermo.py")
    manifest = {
        "schema_version": 1,
        "generator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "source": {
            "name": "itcsimlib",
            "repository": "https://github.com/elihuihms/itcsimlib",
            "commit": ITCSIMLIB_COMMIT,
            "models": ["OneMode", "NModes"],
            "source_sha256": {
                relative: hashlib.sha256((source / relative).read_bytes()).hexdigest()
                for relative in source_files
            },
        },
        "protocol_adapter": {
            "concentration": "MicroCal cumulative-volume states: M=M0*(1-u/2)/(1+u/2), L=Ls*u/(1+u/2)",
            "heat": "Q_i + (v_i/V)*(Q_i+Q_(i-1))/2 - Q_(i-1)",
            "independence": "Uses itcsimlib for one-site and independent-site equilibrium heat. Other FT-ITC models use separately implemented physical mass balances; no FT-ITC assembly or model code is called.",
        },
        "cases": references,
    }
    (output / "reference.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(references)} protocol-conformant integrated-heat fixtures.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--itcsimlib-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    generate(args.itcsimlib_source.resolve(), args.output.resolve())
