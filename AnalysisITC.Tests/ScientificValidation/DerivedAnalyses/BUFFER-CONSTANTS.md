# Buffer constants source review

Reviewed 12 September 2026 against the page images in Goldberg, Kishore and
Lennen (2002), *Thermodynamic Quantities for the Ionization Reactions of
Buffers*, [NIST full text](https://www.nist.gov/document/jpcrd615pdf).
Values refer to 298.15 K and zero ionic strength unless stated otherwise.

| Entry | Source and independently transcribed values | Application consequence |
| --- | --- | --- |
| TAPSO | Table 7.62, printed p.346: pKa 7.635; ionization enthalpy 39.09 kJ/mol; heat capacity −16 J/(mol K); reaction HL± ⇌ H+ + L− | Retain the enthalpy and heat capacity. Use neutral protonated charge, not +1 |
| TAPSO temperature trend | Same table: pKa 7.7479 at 20 °C and 7.5244 at 30 °C | Local central slope −0.02235/K, stored as −0.0224/K. The linear approximation is tested over 20–30 °C with 0.002 pKa tolerance |
| Imidazole | Table 7.41, printed p.302: pKa 6.993; ionization enthalpy 36.64 kJ/mol; heat capacity −9 J/(mol K) | Correct the stored slope from −0.009 to −9 J/(mol K); ΔH at 35 °C is 36.550 kJ/mol |

Visual verification matters: the PDF's extracted text can render “= −16” as
“5216” and “−9” as “29”. Neither ±216 for TAPSO nor +29 for imidazole is the
selected value visible in these tables. The tests use the visually verified
numbers, rather than those extraction artifacts.

The registry's historical name `ProtonationEnthalpy` carries the buffer
ionization enthalpy used in the linkage relation. Its linear-fit slope is in
J/(mol K), and its reference temperature is 25 °C. The field does not store
kJ/(mol K) or a pKa derivative.

This is a targeted source/units review, not certification of the entire
registry. Some entries have zero/unknown temperature coefficients or
thermodynamic quantities and should not be treated as measured zero values.
Listed pKa values can depend on reaction choice, ionic strength and
concentration scale; validate the conditions for the chosen buffer before
making a quantitative proton-linkage or ionic-strength claim. The local TAPSO
slope is not an exact thermodynamic model over an arbitrary temperature range.
