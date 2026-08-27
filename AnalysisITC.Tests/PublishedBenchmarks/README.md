# Published model-reproduction benchmarks

For a compact result table, see [TEST-RESULTS.md](TEST-RESULTS.md).

The strict shortlist currently contains seven high-confidence fixtures:

1. `pytc-ca-edta-tris-01.DH`, a public integrated-heats example with a
   single-experiment maximum-likelihood one-site solution.
2. `elife2023-g223w-mn-onesite-first-run.DH`, an authors' source-data export
   with a conventional Origin one-site solution at fixed `N = 1`.
3. Five additional fixed-`N = 1` exports from the same published Nramp source
   archive: four Mn²⁺ experiments and one Cd²⁺ experiment listed below.

All are direct integrated-heats inputs. The files were not reintegrated,
baseline-corrected, normalized, or otherwise processed when added here.

`pytc-ca-edta-tris-01.DH` is the `ca-edta/tris-01.DH` integrated-heat
fixture from the public-domain
[`harmslab/pytc-demos`](https://github.com/harmslab/pytc-demos) repository,
pinned at commit `7a40435ee5e24d5958a518104cae8034b2419f08`.

The output embedded in `00_fit-single-site.ipynb` reports the maximum-likelihood
one-site fit (cal/mol):

- competent fraction / stoichiometry: `0.973948`
- association constant: `4.05476e7 M^-1`
- binding enthalpy: `-11566.9 cal/mol`

The notebook constructs the experiment with `shot_start=2`, so injection indices
0 and 1 are excluded. The fit also includes a linear dilution term. FT-ITC
Analysis uses its one-set-of-sites model with a constant heat offset, so the
regression criterion is agreement within 2% for the three scientific parameters
rather than equality with the source optimizer's internal standard errors.

The regression runs the same integrated heats through both FT-ITC dilution
conventions (`MicroCal` and `Exponential`). This intentionally verifies fitting
from an imported integrated-heat fixture, not reconstruction of the raw-signal
processing that produced it. Both conventions reproduce the published N, Ka,
and ΔH within the stated tolerance with the supported optimizers.

The pyITC software article is: Duvvuri H, Wheeler LC, Harms MJ. *pytc: Open-Source
Python Software for Global Analyses of Isothermal Titration Calorimetry Data*.
Biochemistry. 2018;57:2578-2583. DOI:
[`10.1021/acs.biochem.7b01264`](https://doi.org/10.1021/acs.biochem.7b01264).

## eLife Nramp Mn²⁺/Cd²⁺ one-site benchmarks

Ray *et al.* (eLife 2023, [eLife 84006](https://elifesciences.org/articles/84006))
published the complete Origin source files for the metal-binding ITC runs. The
paper states that the low-c-value isotherms were fit conventionally with either
a one-site model with fixed `n = 1` or a two-site sequential model with fixed
`n = 2`. The four Mn²⁺ fixtures below and the M230A–Cd²⁺ fixture are cases where
the published analysis selected the fixed-`n = 1` model. The source archives are
[Appendix 1 table 1 (Mn²⁺)](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table1-data1-v2.zip)
and [Appendix 1 table 2 (Cd²⁺)](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table2-data1-v2.zip).

Each `.DH` file is a direct copy of the authors' integrated `DH` and injection-
volume columns for one first-run worksheet. The legacy header preserves the
worksheet's effective active cell volume of `203.9 µL` (the nominal instrument
volume is 200 µL). No thermogram reintegration,
baseline correction, normalization, blank subtraction, or other processing was
performed here. The source values below are the fixed-`n = 1` Origin fit recorded
in the corresponding published worksheet; the paper reports replicate averages
in Appendix 1.

| Fixture | Metal | Origin worksheet fit (`Ka`, `ΔH` cal/mol) | FT-ITC MicroCal/LM |
| --- | --- | ---: | ---: |
| `elife2023-a47w-d296a-mn-onesite-first-run.DH` | Mn²⁺ | `3.340e3`, `+6164` | `3.341e3`, `+6167` |
| `elife2023-a47w-d369a-mn-onesite-first-run.DH` | Mn²⁺ | `4.690e3`, `+7563` | `4.691e3`, `+7564` |
| `elife2023-d369a-mn-onesite-first-run.DH` | Mn²⁺ | `2.590e3`, `+9478` | `2.592e3`, `+9481` |
| `elife2023-m230a-d296a-mn-onesite-first-run.DH` | Mn²⁺ | `3.110e3`, `+7063` | `3.107e3`, `+7064` |
| `elife2023-m230a-cd-onesite-first-run.DH` | Cd²⁺ | `7.030e3`, `−6177` | `7.026e3`, `−6177` |

The corresponding test runs all five fixtures with both FT-ITC dilution
conventions and both supported optimizers; all 20 combinations pass the 0.5%
parameter-agreement criterion with fixed `N = 1` and zero offset.

The archive also contains Cd²⁺ one-site-looking worksheets that were not added:
D296A and D369A do not reproduce their published per-run fits in FT-ITC, while
D56A was interpreted by the paper with the two-site sequential model rather than
the one-site alternative. They remain screened candidates, not passing fixtures.

## Sequential two-step source diagnostics

Six additional fixtures were extracted from Ray *et al.*'s eLife Origin source
worksheets: three WT--Mn²⁺ runs and three WT--Cd²⁺ runs.  The extraction script
[`scripts/extract-elife-opj-dh.csx`](../../scripts/extract-elife-opj-dh.csx)
copies the native direct `DH` and `INJV` columns and the worksheet metadata into
legacy `.DH` files. It does not use `NDH`, `Fit`, `Xt`, `Mt`, or raw power
columns. FT-ITC now evaluates these fixtures with the two-step
`SequentialBindingSites` model, fixed count 2, the first injection excluded,
unweighted residuals, and a zero locked offset.

The source paper reports WT--Mn²⁺ `Kd1 = 190 ± 30 µM` and `Kd2 = 1970 ± 520
µM`, and WT--Cd²⁺ `Kd1 = 55 ± 15 µM` and `Kd2 = 220 ± 20 µM`. The direct-DH
fixtures expose an important identifiability limitation: when both affinities
and both enthalpies are free, four of the six LM fits reach an enthalpy bound,
and LM and Nelder-Mead do not consistently select the same interior basin.
Those all-free fits therefore do **not** satisfy the positive-recovery
acceptance criterion and are retained as a passing diagnostic of that failure,
not relabeled as successful published-parameter recovery.

A transparent reduced fit locks each run's two enthalpies to the values in the
published worksheet and fits only the two affinities. With that stated
restriction, all six fixtures converge with LM and Nelder-Mead, keep
`Kd1 < Kd2`, remain interior in the fitted affinity coordinates, and agree
between optimizers within 1% under both dilution conventions. MicroCal/LM
gives the following values:

| Run | Published `Kd1 / Kd2` (µM) | FT-ITC locked-ΔH `Kd1 / Kd2` (µM) |
| --- | ---: | ---: |
| WT--Mn²⁺ 1 | `220 / 961` | `136.7 / 3424` |
| WT--Mn²⁺ 2 | `125 / 2700` | `115.2 / 4326` |
| WT--Mn²⁺ 3 | `220 / 2250` | `213.3 / 4444` |
| WT--Cd²⁺ 1 | `85 / 260` | `97.75 / 226.9` |
| WT--Cd²⁺ 2 | `50 / 220` | `51.49 / 204.1` |
| WT--Cd²⁺ 3 | `30 / 180` | `29.54 / 182.5` |

The Cd²⁺ triplicate means (`59.59 / 204.49 µM`) lie within both published
ranges. The Mn²⁺ means (`155.08 / 4064.88 µM`) do not; in particular the
second step is substantially weaker than the paper's reported range. An
affinity-shared global fit with the same published per-run enthalpies locked is
also optimizer-stable, but gives Mn²⁺ `166.51 / 4386.44 µM` and Cd²⁺
`59.66 / 192.72 µM`, missing one published range for each metal. These results
validate the sequential calculation and global constraint route on real data,
but they are not an all-four-coordinate source-truth recovery.

The [SEDPHAT ITC tutorial](https://sedfitsedphat.github.io/sedphat/isothermal_titration_calorimetry.htm)
provides a second direct two-site table,
[`sedphat-itc-two-site.DH`](sedphat-itc-two-site.DH), generated from the
published [`ITCdhTable.DAT`](https://sedfitsedphat.github.io/sedphat/images/ITCdhTable.DAT)
using the documented 4.5 µM cell, 50 µM syringe, and 1414.1 µL cell volume.
The conversion from the source `NDH` (cal/mol) to legacy total heat (µcal) is
only a unit conversion; the source's first direct `DH` is retained because
`NDH` is absent for that excluded injection.  The SEDPHAT fit shown in the
tutorial is approximately `Kd1 = 0.242 mM`, `Kd2 = 0.964 mM`, and equal
`ΔH ≈ −18.43 kcal/mol`.  It uses the sequential symmetric-dimer orientation
`A+B+B ↔ AB+B ↔ ABB` (dimer in the syringe), which is outside FT-ITC's chosen
cell-macromolecule sequential-model scope. The test verifies provenance,
orientation, and import only; it does not fit this fixture. See the
sidecar [`sedphat-itc-two-site.DH.md`](sedphat-itc-two-site.DH.md) and
[`scripts/create-sedphat-twosite-dh.rb`](../../scripts/create-sedphat-twosite-dh.rb)
for the exact provenance and conversion.

### CBS supplementary-data classification

A case-insensitive filename and content audit of the current repository found
no CBS supplementary dataset. No concrete repository fixture is therefore
claimed or tested. If that previously referenced dataset is restored, its
fractional stoichiometries and independent two-event interpretation classify it
as an independent-site/two-event diagnostic, not a sequential fixed-integral-
count benchmark.

## Other deferred diagnostic fixtures

The following fixtures are useful for diagnosing model or convention
differences, but are not part of the current result. In particular, the BBR/FEOTF54
fixtures are deferred and are not used as evidence for the current FT-ITC model
implementation.

### M-equivalent one-site benchmark

`bbr2020-m-equivalent.ndh` is the 20-point integrated-heat table from
`Figure_2/M_Equivalent/NDH/Analytical/Data_NDH` in the downloadable supporting
archive for Krishnamoorthy *et al.* (2020),
[`PMC6926116`](https://pmc.ncbi.nlm.nih.gov/articles/PMC6926116/). The adjacent
`M_equal_states.m` script makes this a transparent one-site fixture: it uses a
20 µM cell concentration, 200 µM syringe concentration, 230 µL cell volume,
20 × 4 µL injections, N = 1, Ka = 1e6 M^-1, binding ΔH = -10000 cal/mol, and
ligand-dilution ΔH = -100 cal/mol.

The script deliberately exports direct differences of heat content: its
injection-volume correction lines are commented out. FT-ITC includes that
correction, so this is not a valid exact-recovery assertion for the source Ka.
Instead the test locks N to the source value of one and tightly regresses the
FT-ITC fit to the archived integrated heats (Ka = 7.663e5 M^-1, ΔH = -9954.2
cal/mol). It runs both supported optimizers and both dilution settings. The
latter are equivalent here because the table supplies the per-injection
concentrations explicitly. As with the other BBR fixture, `Kcal/mol` in the
source header is a label error; the numerical data are cal/mol.

### RNASE archived integrated-heat regression

`bbr2020-rnase.ndh` is the processed integrated-heat table from
`Figure_3/Processed_data/RNASE/Data_NDH` in the same supporting archive. The
adjacent fit report, `Figure_3/RNASE/Fit.txt`, reports N = 1.02, Ka = 5.59e4
M^-1 (Kd = 17.9 µM), and ΔH = -13540 cal/mol. This is a real processed data
export, unlike the Figure 2 synthetic fixtures.

The archive contains a tenfold metadata mismatch: `Data_NDH` and its processing
script retain 61 µM protein and 2250 µM ligand, while the exact fitting model
(`Figure_3/RNASE/model_func.m`) fixes 651 µM protein and 21160 µM ligand.
The table is correspondingly normalized using the stale 2250 µM value. The
test preserves the integrated heat, restores that normalization, and uses the
fitting model's concentrations to generate the per-injection trace. The source
trace agrees with FT-ITC's MicroCal convention after the metadata scaling;
the exponential convention is deliberately retained as a separate numerical
check. The archived fit converges within 3% of `Fit.txt` for both optimizers
and dilution settings, without error weighting. It is deliberately not called
an exact source-truth recovery test: `Fit.txt` names `Data1_NDH` as its input,
but that table is not in the archive.

### Two independent sites benchmark

`feotf54-independent-sites.ndh` is the integrated-heat table from the
`Figure_2/MN_Independent/NDH/Analytical/Data_NDH` file in the downloadable
supporting archive for Krishnamoorthy *et al.* (2020),
[`PMC6926116`](https://pmc.ncbi.nlm.nih.gov/articles/PMC6926116/). It is the
published independent-sites simulation based on MicroCal's FEOTF54
ovotransferrin/ferric-ion example. Its source code supplies the experimental
setup (31.4 µM cell, 1.56 mM syringe, 1.411 mL cell, 17 × 5 µL injections) and
the reference parameters (each site has N = 1; Ka1 = 1.18e10 M^-1, ΔH1 =
767.3 cal/mol; Ka2 = 3.46e7 M^-1, ΔH2 = -12030 cal/mol).

Its `Mt` column is the auxiliary `P_Correct` dilution trace, not the `P_T`
state used by the supplied forward model. The model holds `P_T` at its initial
31.4 µM; the test therefore uses 31.4 µM as the cell concentration for every
injection and takes the `Xt` column as the ligand-total state.

The table is a published forward-model output rather than the original
instrument export. The source labels normalized heat as `Kcal/mol`, but the
values and accompanying parameters establish that the numeric values are
cal/mol. The source uses a constant-volume calculation while FT-ITC applies an
injection-volume correction; the current FT-ITC regression does not meet the
15% parameter-agreement target. Keep this as a diagnostic candidate, not as a
passing independent-sites benchmark.

## eLife G223W–Mn²⁺ one-site benchmark

`elife2023-g223w-mn-onesite-first-run.DH` contains the 20 integrated `DH`
values copied from the `G223W_Mn_onesite_first_run.OPJ` worksheet in Ray *et
al.* (2023), *eLife* 12:e84006, Appendix 1—table 1—source data 1. The source
archive is available at
[`elife-84006-app1-table1-data1-v2.zip`](https://cdn.elifesciences.org/articles/84006/elife-84006-app1-table1-data1-v2.zip),
and the paper explicitly uses a one-site model with fixed N = 1 for G223W–Mn²⁺.

The `.DH` file contains the integrated heats only; no raw thermogram was
reintegrated or otherwise processed. It uses the source metadata (25 µM cell,
6 mM syringe, 203.9 µL effective cell volume, 20 injections). Origin's per-run worksheet fit is
Ka = 2.17e3 M⁻¹ and ΔH = +5847 cal/mol (Kd ≈ 460 µM); the paper reports the
two-run average Kd = 440 ± 15 µM. With N and offset fixed, FT-ITC returns:

- MicroCal dilution: Ka = 2.166e3 M⁻¹, ΔH = +5849 cal/mol.
- Exponential dilution: Ka = 2.177e3 M⁻¹, ΔH = +5829 cal/mol.

Both are within 0.5% of the per-run Origin fit and use unweighted least squares,
since the published integrated heats contain no injection-error estimates.

`elife2023-g223w-mn-onesite-second-run.DH` is the companion 20-injection
worksheet `G223W_Mn_onesite_second_run.OPJ` from the same archive. It is also a
direct copy of the authors' `DH` column, with the same 25 µM cell, 6 mM syringe,
203.9 µL effective cell volume, and 0.4/2 µL injection sequence. Origin records K = 2.31e3 M⁻¹
and ΔH = +3048 cal/mol for this worksheet. Its second `NDH` cell is NaN, but
the corresponding integrated `DH` value is finite and is retained rather than
silently deleting a source heat.

With N and offset fixed and no error weighting, the direct-DH regressions are:

- MicroCal dilution: Ka = 3.027e3 M⁻¹, ΔH = +2906 cal/mol.
- Exponential dilution: Ka = 3.039e3 M⁻¹, ΔH = +2899 cal/mol.

The second run therefore does **not** reproduce the Origin K value from the
unprocessed DH column (about +28% in Ka). This is a documented diagnostic, not
a relaxed source-truth pass. The worksheet exposes both `NDH` and `Fit`, and
Origin's fit note is associated with that normalized-heats column; FT-ITC is
deliberately fitting the imported integrated `DH` values unweighted because no
injection-error model or equivalent normalization is available in the source
file. The two runs are now both covered, while the first run remains the strict
source-fit benchmark.

## Screening conclusions

The strict fixtures satisfy the important data constraint: the repository
contains integrated injection heats, not raw thermograms, and each has a
published/source-file fit under a conventional one-site model. The pytc source
is the public [`harmslab/pytc-demos`](https://github.com/harmslab/pytc-demos)
example paired with the single-site maximum-likelihood analysis described in
the pytc paper; the eLife fixtures come from the authors' published Origin
source-data archives. The BBR fixtures are deferred and are not part of the
strict result.

The official SEDPHAT tutorial was also screened because it publishes a complete
integrated-heat table and a conventional two-site fit
([tutorial](https://sedfitsedphat.github.io/sedphat/isothermal_titration_calorimetry.htm)).
It was not retained as an FT-ITC regression fixture: the tutorial's stable
symmetric-dimer example uses a titration orientation/model convention that did
not reproduce under FT-ITC's cell-macromolecule independent-site model. Keeping
it would create a failing benchmark and would not be evidence that FT-ITC had
implemented the same model.

Other screened sources were rejected for the same stated requirements: Bayesian
ITC datasets, multi-buffer/global analyses, raw-only datasets, and tutorial
sample names for which the actual integrated-heats file could not be retrieved.
