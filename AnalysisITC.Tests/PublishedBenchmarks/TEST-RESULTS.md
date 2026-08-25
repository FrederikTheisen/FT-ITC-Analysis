# FT-ITC published-data test results

Earlier strict one-site run: **32 passed, 0 failed**.  New two-site focused run:
**14 passed, 0 failed** (12 eLife source cases plus 2 SEDPHAT cases).

The strict tests use direct integrated injection heats. They do not reintegrate
thermograms or apply additional data processing. The eLife fixtures preserve
the source worksheets' effective active cell volume of `203.9 µL` (nominally
200 µL). The five Nramp fixtures use fixed `N = 1` and zero heat offset.

## Passing fixtures

| Fixture | Source/model | Published or source fit | FT-ITC result | Verdict |
| --- | --- | --- | --- | --- |
| `pytc-ca-edta-tris-01.DH` | pytc; maximum-likelihood one-site | `N=0.973948`, `Ka=4.05476e7 M⁻¹`, `ΔH=-11566.9 cal/mol` | Reproduced within 2% using both dilution conventions | PASS |
| `elife2023-g223w-mn-onesite-first-run.DH` | eLife 84006; Origin one-site, fixed `N=1` | `Ka=2.17e3 M⁻¹`, `ΔH=+5847 cal/mol` | MicroCal: `2.166e3`, `+5849`; Exponential: `2.177e3`, `+5829` | PASS |
| `elife2023-a47w-d296a-mn-onesite-first-run.DH` | eLife 84006; Origin one-site, fixed `N=1` | `Ka=3.340e3`, `ΔH=+6164` | `Ka=3.341e3`, `ΔH=+6167` | PASS |
| `elife2023-a47w-d369a-mn-onesite-first-run.DH` | eLife 84006; Origin one-site, fixed `N=1` | `Ka=4.690e3`, `ΔH=+7563` | `Ka=4.691e3`, `ΔH=+7564` | PASS |
| `elife2023-d369a-mn-onesite-first-run.DH` | eLife 84006; Origin one-site, fixed `N=1` | `Ka=2.590e3`, `ΔH=+9478` | `Ka=2.592e3`, `ΔH=+9481` | PASS |
| `elife2023-m230a-d296a-mn-onesite-first-run.DH` | eLife 84006; Origin one-site, fixed `N=1` | `Ka=3.110e3`, `ΔH=+7063` | `Ka=3.107e3`, `ΔH=+7064` | PASS |
| `elife2023-m230a-cd-onesite-first-run.DH` | eLife 84006; Origin one-site, fixed `N=1` | `Ka=7.030e3`, `ΔH=-6177` | `Ka=7.026e3`, `ΔH=-6177` | PASS |

The five new Nramp fixtures were each run with both FT-ITC dilution methods
(`MicroCal` and `Exponential`) and both supported optimizers. All 20 cases passed
the 0.5% agreement criterion.

## Deferred or rejected candidates

## Two-site diagnostic runs

The six eLife WT source fixtures and the SEDPHAT tutorial fixture all load as
direct integrated-heat data and converge with `N1 = N2 = 1`, zero offset, and
unweighted least squares.  The two dilution conventions were both exercised.
These are **diagnostic only**: eLife and SEDPHAT used sequential-site models,
while FT-ITC's `TwoSetsOfSites` implementation is independent-site, and the
SEDPHAT example also has the dimer in the syringe rather than the usual
cell-macromolecule orientation.

Representative fixed-`N` LM results (cal/mol) are:

| Fixture/method | Ka1 | ΔH1 | Ka2 | ΔH2 |
| --- | ---: | ---: | ---: | ---: |
| eLife WT--Mn first, MicroCal | 4.116e3 | +4,940 | 5.015e2 | −439 |
| eLife WT--Mn first, Exponential | 3.734e3 | +6,263 | 17.36 | −239,006 |
| eLife WT--Cd first, MicroCal | 1.006e4 | −7,598 | 296.9 | +8,838 |
| eLife WT--Cd first, Exponential | 8.279e3 | −7,725 | 19.85 | +239,006 |
| SEDPHAT tutorial, MicroCal | 3.519e5 | −177,424 | 1.596e5 | +239,006 |
| SEDPHAT tutorial, Exponential | 3.513e5 | −177,561 | 1.595e5 | +239,006 |

The SEDPHAT source fit is approximately `Ka1 = 4.13e3`, `Ka2 = 1.04e3`,
`ΔH1 = ΔH2 = −18,430 cal/mol`; the large difference is expected from the
orientation/model mismatch and is not a passing validation result.

| Candidate | Reason not in strict passing set |
| --- | --- |
| eLife G223W–Mn²⁺ second run | Direct `DH` fit gives `Ka≈3.03e3 M⁻¹` versus the Origin fit recorded for the worksheet (`2.31e3`). |
| eLife D296A/D369A–Cd²⁺ | Published one-site source fits were screened, but FT-ITC did not reproduce them closely enough. |
| eLife D56A–Cd²⁺ | The paper selected a two-site sequential model; the one-site alternative is not a matching published assumption. |
| eLife YB1/P1 | The supplied table and integrated data did not reproduce the published stoichiometry. |
| BBR M-equivalent, RNase, and FEOTF54 | Deferred at the user’s request; metadata/model-convention issues prevent treating them as strict evidence. |
| PLOS MCP2201 dissociation workbook | Direct normalized enthalpies are present, but the source file does not provide injection volumes or active cell volume; no metadata-invented `.DH` was created. |
| SEDPHAT tutorial | Titration orientation/model convention did not match FT-ITC’s independent-site implementation. |

Primary eLife source: [Ray et al., eLife 84006](https://elifesciences.org/articles/84006),
[Mn²⁺ Origin archive](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table1-data1-v2.zip),
and [Cd²⁺ Origin archive](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table2-data1-v2.zip).
