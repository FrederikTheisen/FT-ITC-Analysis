# eLife 2023 G223W–Mn2+ one-site benchmark — second run

This fixture contains the integrated injection heats from the published Origin
worksheet `G223W_Mn_onesite_second_run.OPJ` in the source-data archive for Ray
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
Source OPJ SHA-256: `5e21bc1832836ee757519d3a1ce168882b14fb2e8e0aa749bd728c4a11c35a7b`.

The Origin worksheet records the second-run one-site fit as `K = 2.31 × 10^3
M^-1` and `ΔH = +3048 cal/mol`, with `N = 1`. Its second injection has no
finite `NDH` value, but its integrated `DH` value is retained here because the
fixture is an unprocessed copy of the authors' integrated-heats column. The
direct integrated-heat regression therefore reports the result obtained from
all retained DH values, without introducing an error-weighting scheme that is
not recoverable from the source file.
