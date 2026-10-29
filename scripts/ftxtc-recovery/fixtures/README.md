# Frozen recovery compatibility corpus

These files are permanent regression inputs. Do not open and re-save them with a
new application version, replace them with current writer output, or regenerate
them during tests. `compatibility.json` pins their SHA-256 checksums and recovered
outputs. Additional cases can be appended without changing these cases.

| Schema | Origin | Main coverage |
| --- | --- | --- |
| 1.2 | Exact copy of `AnalysisITC.Tests/SeArgT_Pu1210_TwoSetsOfSites.ftxtc` | Historical project schema 2, two-site fits, experiments and results |
| 1.3 | Exact copy of `AnalysisITC.Tests/Tandem/280-430-D2mut-1p6mM-JNK-200uM-1_process2.ftxtc` | Historical project schema 2, tandem metadata, traces and injections |
| 1.4 | Exact copy of `AnalysisITC.Tests/SolutionIdentityTracingTest/saved_and_loaded.ftxtc` | Historical project schema 2 and saved solution/result relationships |
| 1.5 | Hand-authored structural/numerical fixture | Project schema 3, mixed content order, no reports, profile bounds, locks, constraints, errors, missing values and lab-unit conversions |
| 1.6 | Exact copy of `AnalysisITC.Tests/JORS Example Project.ftxtc` | Project schema 4, local/global fits, embedded Null fits and intentional report/refit exclusion |

There was no original schema 1.5 writer archive in the repository when this corpus
was created. Its fixture is explicitly synthetic, shaped to the documented 1.5
root contract. It is not evidence of interoperability with a historical writer.
Add an original 1.5 archive if one becomes available; retain the synthetic case.

Expected outputs were captured once from the initially tested extractor. The
independent numerical tests in `test_recover_ftxtc.py` establish the conversion
expectations; the frozen outputs detect subsequent regressions. In the hand-authored
case, the first raw power is 1 W and the baseline is 0.99999998 W, which rounds to
1 W for Float32 subtraction: adjusted power is exactly zero. The second sample
is 4 W minus 1.25 W, giving 2,750,000 µW. Its injection volume is 2 µL and raw heat
is −4 µJ with SD 1 µJ. An affinity coordinate of 6 gives Kd 1 µM; coordinate
endpoints 5 and 7 become Kd endpoints 0.1 and 10 µM. Enthalpy −30,000 J/mol with
SD 1,000 J/mol becomes −30 kJ/mol with SD 1 kJ/mol. These expectations do not depend
on the extractor's implementation.

The catalog records every recovered object's identity, references and status;
each CSV's original column names, row count and fingerprint of cell values; and
the recovered original-metadata field paths/values. CSV quoting, BOM, line endings,
column reordering and additional columns do not invalidate existing values.
Changes to existing cells, lost rows/columns/objects, or lost metadata do.

For intentional scientific corrections, investigate and document the old behavior
and independent expected correction before reviewing a catalog change. Never
refresh the whole catalog to silence a compatibility failure. Tests do not offer
an automatic baseline-update switch.
