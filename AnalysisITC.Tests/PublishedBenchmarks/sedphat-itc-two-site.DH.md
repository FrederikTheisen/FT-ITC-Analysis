# SEDPHAT tutorial two-site fixture

Source: [SEDPHAT ITC tutorial](https://sedfitsedphat.github.io/sedphat/isothermal_titration_calorimetry.htm),
downloadable data link `ITCdhTable.DAT`.

The source table contains 21 direct integrated injections.  FT-ITC's legacy
`.DH` format stores total heat per injection, while the source's `NDH` column
stores normalized heat in cal/mol.  The fixture converts only units using the
source metadata (50 uM syringe):

`q[uCal] = NDH[cal/mol] × injection volume[uL] × 50e-6`.

The first source row has no `NDH`; its direct `DH` value is retained unchanged,
and the reader excludes that first injection as the source analysis does.  Its
unit is therefore immaterial to the fit.  Source
metadata are 4.5 uM cell, 50 uM syringe, 1414.1 uL cell volume.  The tutorial
does not state a temperature in the data table; 25 °C is a metadata placeholder
and does not enter this binding calculation.

The tutorial's final SEDPHAT fit shown in `isothe22.jpg` is approximately:

- `log10(Ka1/M^-1) = 6.616` (`Kd1 ≈ 0.242 mM`)
- `log10(Ka2/Ka1) = -0.600` (`Kd2 ≈ 0.964 mM`)
- `ΔH1 = ΔH2 ≈ -18.43 kcal/mol`

This is a **sequential symmetric-dimer** model (`A+B+B ↔ AB+B ↔ ABB`), not
FT-ITC's independent `TwoSetsOfSites` model.  It is therefore a diagnostic
two-site fixture, not an exact same-model truth test.
