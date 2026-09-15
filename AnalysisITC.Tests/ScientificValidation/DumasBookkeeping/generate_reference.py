"""Independent ideal-mixing reference. Python standard library only; writes JSON to stdout.

Equilibria are solved from mass balances, without importing FT-ITC or pytc.
The continuous displacement integral uses composite eight-point Gauss-Legendre
quadrature, independently of the production single-panel Simpson calculation.
"""
import json
import math

NODES = (0.1834346424956498, 0.5255324099163290, 0.7966664774136267, 0.9602898564975363)
WEIGHTS = (0.3626837833783620, 0.3137066458778873, 0.2223810344533745, 0.1012285362903763)


def root(function, upper):
    lower = 0.0
    for _ in range(64):
        middle = (lower + upper) / 2
        if function(middle) > 0:
            upper = middle
        else:
            lower = middle
    return (lower + upper) / 2


def dimer(total, ka):
    monomer = 2 * total / (1 + math.sqrt(1 + 8 * ka * total))
    return ka * monomer * monomer


def heat_density(case, macro, ligand):
    p = case['parameters']
    ka = [10 ** k for k in p['logka']]
    hs = p['enthalpy']
    kind = case['model']
    if kind == 'dissociation':
        return hs[0] * dimer(ligand, ka[0])
    if kind == 'one-site':
        sites = macro * p['n'][0]
        total = sites + ligand + 1 / ka[0]
        bound = 2 * sites * ligand / (total + math.sqrt(total * total - 4 * sites * ligand))
        return hs[0] * bound
    if kind == 'two-site':
        sites = [macro * n for n in p['n']]
        free = root(lambda x: x + sum(s * k * x / (1 + k * x) for s, k in zip(sites, ka)) - ligand, ligand)
        return sum(h * s * k * free / (1 + k * free) for h, s, k in zip(hs, sites, ka))
    if kind == 'competitive':
        competitor = case['competitor']
        other = competitor['concentration'] * macro / case['cell_molar']
        kb = 10 ** competitor['logka']
        free_sites = root(lambda r: r + ligand * ka[0] * r / (1 + ka[0] * r)
                          + other * kb * r / (1 + kb * r) - macro * p['n'][0], macro * p['n'][0])
        return (hs[0] * ligand * ka[0] * free_sites / (1 + ka[0] * free_sites)
                + competitor['enthalpy'] * other * kb * free_sites / (1 + kb * free_sites))

    def distribution(free):
        weights = [1.0]
        for k in ka:
            weights.append(weights[-1] * k * free)
        total = sum(weights)
        return [w / total for w in weights]

    free = root(lambda x: x + macro * sum(i * f for i, f in enumerate(distribution(x))) - ligand, ligand)
    cumulative_h = [0.0]
    for h in hs:
        cumulative_h.append(cumulative_h[-1] + h)
    return macro * sum(h * f for h, f in zip(cumulative_h, distribution(free)))


def injection_heat(case, before, volume, panels):
    cell_volume, syringe = case['cell_liters'], case['syringe_molar']
    u = volume / cell_volume

    def content(x):
        r = math.exp(-x)
        return cell_volume * heat_density(case, before[0] * r, before[1] * r + syringe * (-math.expm1(-x)))

    integral = 0.0
    half_width = u / (2 * panels)
    for panel in range(panels):
        center = (2 * panel + 1) * half_width
        integral += half_width * sum(w * (content(center - n * half_width) + content(center + n * half_width))
                                     for n, w in zip(NODES, WEIGHTS))
    incoming = heat_density(case, 0, syringe) if case['model'] == 'dissociation' else 0.0
    heat = content(u) - content(0) + integral - volume * incoming
    return heat + volume * syringe * case['parameters']['offset']


def generate(case):
    before = (case['cell_molar'], 0.0)
    segments = {s['first_injection']: (s['cell_molar'], s['titrant_molar']) for s in case.get('segments', [])}
    fine, coarse = [], []
    for i, volume in enumerate(case['injection_liters']):
        before = segments.get(i, before)
        coarse.append(injection_heat(case, before, volume, 32))
        fine.append(injection_heat(case, before, volume, 64))
        r = math.exp(-volume / case['cell_liters'])
        before = (before[0] * r, before[1] * r + case['syringe_molar'] * (1 - r))
    peak = max(map(abs, fine))
    case['reference_refinement_fraction_of_peak'] = max(abs(a - b) for a, b in zip(fine, coarse)) / peak
    assert case['reference_refinement_fraction_of_peak'] < 1e-8, case['id']
    case['heats_joules'] = fine
    return case


def make_case(name, model, logs, enthalpies, ns, fractions, cell=20e-6, syringe=400e-6, offset=0, **extra):
    return dict(id=name, model=model, cell_liters=200e-6, cell_molar=cell, syringe_molar=syringe,
                parameters=dict(logka=logs, enthalpy=enthalpies, n=ns, offset=offset),
                injection_liters=[f * 200e-6 for f in fractions], **extra)


def main():
    cases = []
    for c in (1, 100, 10000):
        for fraction in (0.005, 0.01, 0.02):
            cases.append(make_case(f'one-c{c}-v{fraction}', 'one-site', [math.log10(c / 20e-6)], [-25000], [1],
                                   [fraction] * round(0.3 / fraction)))
    fractions = [0.004, 0.009, 0.012, 0.015] * 15
    cases.extend([
        make_case('one-endothermic', 'one-site', [6], [18000], [0.8], fractions, offset=120),
        make_case('two-site', 'two-site', [7, 5.2], [-20000, 15000], [0.65, 1.4], fractions),
        make_case('competitive', 'competitive', [6.7], [-35000], [1.1], fractions,
                  competitor=dict(concentration=100e-6, logka=6, enthalpy=-12000)),
        make_case('dissociation', 'dissociation', [4.3], [-42000], [], fractions,
                  cell=0, syringe=1e-3, offset=50),
        make_case('tandem-one', 'one-site', [6.4], [-25000], [1], fractions[:32],
                  segments=[dict(first_injection=16, cell_molar=16e-6, titrant_molar=42e-6)]),
        make_case('tandem-dissociation', 'dissociation', [4.3], [-42000], [], fractions[:32],
                  cell=0, syringe=1e-3,
                  segments=[dict(first_injection=16, cell_molar=0, titrant_molar=100e-6)]),
        make_case('stress-large-shots', 'one-site', [8.7], [-25000], [1], [0.1] * 6,
                  stress_only=True),
    ])
    for count in (2, 3, 4):
        cases.append(make_case(f'sequential-{count}', 'sequential', [6.4, 5.4, 4.6, 3.8][:count],
                               [-30000, 20000, -15000, 10000][:count], [], [0.005] * 72, syringe=2e-3))
    print(json.dumps(dict(reference='Independent mass balances; composite 8-point Gauss-Legendre, 32/64 panels',
                          cases=[generate(case) for case in cases]), indent=2, allow_nan=False))


if __name__ == '__main__':
    main()
