# eLife 2023 G223W–Mn2+ one-site benchmark

This fixture contains the integrated injection heats from the published Origin
worksheet `G223W_Mn_onesite_first_run.OPJ` in the source-data archive for Ray
et al., *eLife* 12:e84006 (2023), Appendix 1—table 1—source data 1:

<https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table1-data1-v2.zip>

The source worksheet's `DH` column was copied as-is (20 integrated injection
heats, in microcalories); only the surrounding five-line legacy `.DH` metadata
header was added so FT-ITC can load it. The header preserves the source
worksheet's effective active cell volume of 203.9 µL. No thermogram integration, baseline
correction, displacement correction, normalization, or fitting was performed
when making this fixture. The source worksheet also contains the corresponding
`INJV`, `Xt`, `Mt`, `XMt`, and `NDH` columns; the test recomputes concentration
states using FT-ITC's selected dilution convention so MicroCal and exponential
conventions can be compared explicitly.

Source archive SHA-256: `1da01a323b9a449f2dad86acf29276b62791e6f6b41ca1b9c980d29aaf2bf87d`.
Source OPJ SHA-256: `438c33fe8c8bbd896ebf3d968133c02db64c6d6adc900048f400472b1e3054a4`.

The paper reports that G223W–Mn2+ fits a one-site model with fixed `n = 1` and
`Kd = 440 ± 15 µM` (approximately `Ka = 2.273 × 10^3 M^-1`). The Origin
worksheet itself records the first-run fit as `K = 2.17 × 10^3 M^-1` and
`ΔH = +5847 cal/mol`; the paper's reported affinity is the average of two
measurements, so the per-run value is the more direct source-fit comparison.
