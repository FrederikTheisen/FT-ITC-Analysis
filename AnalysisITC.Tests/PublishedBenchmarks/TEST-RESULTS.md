# FT-ITC published-data test results

Earlier strict one-site run: **32 passed, 0 failed**. Sequential-source focused
run: **17 passed, 0 failed**.

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

## Sequential two-step diagnostics

All six eLife WT fixtures use `SequentialBindingSites`, fixed count 2, the
first injection excluded, unweighted residuals, and zero locked offset. The
focused result comprises:

- 12 fixture/dilution cases with the two published worksheet enthalpies locked;
  each case runs both LM and Nelder-Mead and requires convergence, finite
  interior affinity coordinates, `Kd1 < Kd2`, and ≤1% inter-optimizer
  disagreement.
- One all-four-coordinate diagnostic covering all six fixtures and both
  optimizers. It confirms that the direct-DH data do not meet the requested
  all-free acceptance: at least four LM fits contact an enthalpy bound and at
  least one fixture selects materially different LM and Nelder-Mead affinity
  basins. Nelder-Mead boundary contact is not itself required because it can
  terminate just inside the broad enthalpy limit.
- One triplicate-mean comparison, two affinity-shared global-family fits (Mn
  and Cd, each with both optimizers), and one SEDPHAT orientation/import check.

MicroCal/LM affinity-only results with the published enthalpy steps locked are:

| Fixture | Published `Kd1 / Kd2` (µM) | FT-ITC `Kd1 / Kd2` (µM) |
| --- | ---: | ---: |
| WT--Mn first | `220 / 961` | `136.71 / 3424.26` |
| WT--Mn second | `125 / 2700` | `115.22 / 4326.00` |
| WT--Mn third | `220 / 2250` | `213.32 / 4444.36` |
| WT--Cd first | `85 / 260` | `97.75 / 226.87` |
| WT--Cd second | `50 / 220` | `51.49 / 204.12` |
| WT--Cd third | `30 / 180` | `29.54 / 182.49` |

| Comparison | Published range (µM) | FT-ITC locked-ΔH result (µM) | Verdict |
| --- | ---: | ---: | --- |
| Mn triplicate mean `Kd1 / Kd2` | `160–220 / 1450–2490` | `155.08 / 4064.88` | Outside both ranges |
| Cd triplicate mean `Kd1 / Kd2` | `40–70 / 200–240` | `59.59 / 204.49` | Within both ranges |
| Mn affinity-shared global `Kd1 / Kd2` | `160–220 / 1450–2490` | `166.51 / 4386.44` | `Kd1` within; `Kd2` outside |
| Cd affinity-shared global `Kd1 / Kd2` | `40–70 / 200–240` | `59.66 / 192.72` | `Kd1` within; `Kd2` outside |

The global fits use one `SameForAll` affinity coordinate per sequential step,
the `None` enthalpy-family style, the published per-run enthalpies locked on
each member, and locked zero member offsets. LM and Nelder-Mead agree within
1% for both metals. These are useful real-data route checks, but the locked
enthalpies and target misses mean they are not all-free parameter-truth passes.

The SEDPHAT tutorial remains diagnostic-only. Its sequential symmetric-dimer
setup has the macromolecule in the syringe, outside the selected
cell-macromolecule orientation, so the test checks import metadata and
orientation without fitting it.

No CBS supplementary dataset exists in the current repository after a
case-insensitive filename/content audit. If restored, its fractional
stoichiometry and independent two-event interpretation would classify it as an
independent-site diagnostic rather than a fixed-integral sequential benchmark.

## Deferred or rejected candidates

| Candidate | Reason not in strict passing set |
| --- | --- |
| eLife G223W–Mn²⁺ second run | Direct `DH` fit gives `Ka≈3.03e3 M⁻¹` versus the Origin fit recorded for the worksheet (`2.31e3`). |
| eLife D296A/D369A–Cd²⁺ | Published one-site source fits were screened, but FT-ITC did not reproduce them closely enough. |
| eLife D56A–Cd²⁺ | The paper selected a two-site sequential model; the one-site alternative is not a matching published assumption. |
| eLife YB1/P1 | The supplied table and integrated data did not reproduce the published stoichiometry. |
| BBR M-equivalent, RNase, and FEOTF54 | Deferred at the user’s request; metadata/model-convention issues prevent treating them as strict evidence. |
| PLOS MCP2201 dissociation workbook | Direct normalized enthalpies are present, but the source file does not provide injection volumes or active cell volume; no metadata-invented `.DH` was created. |
| SEDPHAT tutorial | The macromolecular dimer is in the syringe, outside FT-ITC's chosen sequential cell-macromolecule orientation. |

Primary eLife source: [Ray et al., eLife 84006](https://elifesciences.org/articles/84006),
[Mn²⁺ Origin archive](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table1-data1-v2.zip),
and [Cd²⁺ Origin archive](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table2-data1-v2.zip).
