#!/usr/bin/env python3
"""Independent high-precision binding-polynomial reference, no FT-ITC calls."""
from decimal import Decimal as D, localcontext
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def state(cell, ligand, constants, enthalpies):
    def populations(free):
        terms = [D(1)]
        for ka in constants:
            terms.append(terms[-1] * ka * free)
        total = sum(terms)
        return [value / total for value in terms]

    low, high = D(0), ligand
    for _ in range(180):
        free = (low + high) / 2
        p = populations(free)
        bound = cell * sum(D(index) * fraction for index, fraction in enumerate(p))
        if free + bound > ligand:
            high = free
        else:
            low = free
    p = populations((low + high) / 2)
    cumulative_h = [D(0)]
    for h in enthalpies:
        cumulative_h.append(cumulative_h[-1] + h)
    return cell * sum(fraction * h for fraction, h in zip(p, cumulative_h))


def generate():
    with localcontext() as context:
        context.prec = 55
        cases = []
        for count in (2, 3, 4):
            logs = [D('6.4'), D('5.7'), D('5.0'), D('4.3')][:count]
            ka = [D(10) ** x for x in logs]
            h = [D(-30000), D(20000), D(-18000), D(14000)][:count]
            cell, syringe, volume, shot = D('0.00002'), D('0.002'), D('0.0002'), D('0.000002')
            previous = D(0)
            rows = []
            for i in range(48):
                ratio = D(i + 1) * shot / volume
                mt = cell * (1 - ratio / 2) / (1 + ratio / 2)
                lt = syringe * ratio * (1 - ratio / 2)
                energy_density = state(mt, lt, ka, h)
                # Enthalpy balance with the declared endpoint-mean correction
                # for solution displaced from the fixed active cell volume.
                heat = volume * (energy_density - previous) + shot * (energy_density + previous) / 2
                rows.append(dict(id=i, volume_liters=float(shot), cell_molar=float(mt),
                                 ligand_molar=float(lt), heat_joules=float(heat)))
                previous = energy_density
            cases.append(dict(step_count=count, cell_molar=float(cell), syringe_molar=float(syringe),
                              cell_liters=float(volume), dilution='MicroCal',
                              log10_ka=list(map(float, logs)), enthalpy_joules_per_mole=list(map(float, h)),
                              offset_joules_per_mole=0.0, injections=rows))
    (ROOT / 'reference.json').write_text(json.dumps(dict(
        description='Independent synthetic reference; not experimental observations',
        precision_decimal_digits=55, license='MIT (repository license)', cases=cases), indent=2) + '\n')


if __name__ == '__main__':
    generate()
