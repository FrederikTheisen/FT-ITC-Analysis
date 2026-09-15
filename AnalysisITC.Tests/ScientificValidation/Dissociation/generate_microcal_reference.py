"""High-precision numerical regression fixture, not external model validation.

Uses only Decimal arithmetic and the declared monomer/dimer mass balance;
does not import FT-ITC. Frozen C# expectations mirror the resulting CSV.
"""
from decimal import Decimal as D, localcontext
from pathlib import Path


def generate():
    with localcontext() as context:
        context.prec = 55
        volume, syringe, ka = D('0.0014'), D('0.0012'), D(31250)
        enthalpy, offset = D(-18000), D(1250)
        shots = [D('0.00000025')] + [D('0.0000025')] * 11

        def dimer(total):
            monomer = ((1 + 8 * ka * total).sqrt() - 1) / (4 * ka)
            return ka * monomer * monomer

        cumulative, previous = D(0), D(0)
        lines = [
            '# Independent high-precision MicroCal rational-protocol regression; no FT-ITC calls.',
            '# Columns: injection, volume_L, post_titrant_M, heat_J, molar_heat_J_per_mol.',
            'injection,volume_L,post_titrant_M,heat_J,molar_heat_J_per_mol',
        ]
        for index, shot in enumerate(shots):
            cumulative += shot
            # Direct solution of delivered = active + mean displaced ligand.
            concentration = syringe * cumulative / (volume + cumulative / 2)
            before = shot * dimer(syringe) + (volume - shot) * dimer(previous)
            after = volume * dimer(concentration)
            heat = enthalpy * (after - before) + offset * syringe * shot
            values = (shot, concentration, heat, heat / (syringe * shot))
            lines.append(str(index) + ',' + ','.join(format(float(x), '.17g') for x in values))
            previous = concentration
        Path(__file__).with_name('microcal-reference.csv').write_text('\n'.join(lines) + '\n')


if __name__ == '__main__':
    generate()
