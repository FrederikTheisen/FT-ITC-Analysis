# Can WT two-step sequential benchmark investigation

These three `.dh` files contain **predicted**, not measured, integrated heats
for the WT *Carnobacterium antarcticus* preQ1 riboswitch experiments associated
with Schroeder et al., *Nature Communications* 13, 199 (2022):

- <https://www.nature.com/articles/s41467-021-27790-8>
- <https://github.com/chapincavender/itc_two_site_fit>

The attached archive was verified byte-for-byte against upstream Git commit
`49569a7f62d01b67f213b28d7c820818831c5b5c`. The pinned implementation is
`fit_itc_model.py` (SHA-256
`5fd16d46cdadd971354a00087788c2644bead51fc34ce3d3f2c6f01fa57ea15c`).
The source data are `can_wt_preq1_1_peaq_data`,
`can_wt_preq1_2_peaq_data`, and `can_wt_preq1_3_peaq_data`; the parameter
source is `fits/can_wt_preq1_123_log10_16_fit_0.875`.

## Source conditions and units

All runs use 3.0 µM recorded RNA, a 1420.6 µL active cell volume, and 29
injections (3 µL followed by 28 × 10 µL). Runs 1 and 2 use 60.0 µM ligand in
the syringe; run 3 uses 55.0 µM. The first injection participates in the
concentration and heat history but is excluded from fitting (`-s 1`). Source
injection volumes are µL and observed/predicted normalized heats are cal/mol
in the input/output report (internally kcal/mol). The `.dh` files store volumes
in µL, concentrations in mM, cell volume in mL, and absolute heat in µcal.

The reference uses the exponential trajectory
`RT = R0 exp(-V/V0)` and `LT = L0(1-exp(-V/V0))`, individual injection
volumes through the cumulative `V`, one fitted offset per run, and a shared
`eta = 1.0065750263310016`. Despite the wording “syringe factor” in the
benchmark prompt, the paper and code define eta as the effective active RNA
concentration relative to recorded cell RNA. The fixtures therefore store
`eta × 3.0 µM = 3.0197250789930048 µM` as cell concentration; the syringe
concentrations remain 60, 60, and 55 µM. Offsets (kcal/mol) are
`0.34611151995817996`, `-0.033651140984190059`, and
`-0.56611565226946092` and are included in every predicted heat, including
the excluded first injection.

## Microscopic-to-macroscopic conversion

For microscopic `KD_A1`, `KD_A2`, and `KD_B2`, detailed balance gives
`KD_B1 = KD_A1 KD_B2 / KD_A2`. The equivalent sequential binding polynomial
has:

```
Kd,1 = KD_A1 KD_B2 / (KD_A2 + KD_B2)
Kd,2 = KD_A2 + KD_B2
```

The singly-bound macroscopic enthalpy is the population-weighted value of the
two microscopic routes, and the second step completes the cycle:

```
DH1 = (DH_A1 KD_B2 + DH_B1 KD_A2) / (KD_A2 + KD_B2)
DH2 = DH_A1 + DH_B2 - DH1
```

Using the regenerated binary64 fit vector gives `Kd,1 =
0.89291414632308064 µM`, `Kd,2 = 0.46090982623566212 µM`, `DH1 =
-34.104283382675469 kcal/mol`, and `DH2 = -43.106958398384485 kcal/mol`.
The test checks the microscopic and macroscopic state heat at several total
ligand concentrations rather than assuming this enthalpy mapping.

## Why there is no strict injection-heat acceptance test

The equilibrium/state functions are equivalent, but the finite-injection
operators are not. If `h_i` is the equilibrium molar heat content per active
RNA after injection `i`, the reference analytic interval average is

```
q_ref / (L0 dv) = M_i (h_i - h_(i-1)) / (L0 (1-exp(-dv/V0)))
```

whereas FT-ITC currently applies its common trapezoidal correction

```
q_FT = Q_i + (dv/V0)(Q_i + Q_(i-1))/2 - Q_(i-1),  Q_i = V0 M_i h_i.
```

At these parameters the maximum included-injection discrepancy is about
`0.1267 cal/mol` (`~4.1e-6` relative), while binary64 evaluation noise is near
`1e-11 cal/mol`. Thus a tight forward-model comparison correctly fails for a
model-convention reason. No production model was changed and no loose
tolerance was introduced. `Nature2022CanSequentialBenchmarkTests` preserves
the strict equilibrium mapping and explicitly detects this known
finite-injection mismatch; it is not an optimizer-recovery test.

## Regeneration

The generator verifies SHA-256 hashes for the implementation, all three data
files, and the named fit report, then evaluates the original `TwoSites` class
at the fixed full-precision parameter vector. It also regenerates the upstream
fit only as an environment guard; optimizer recovery is not used as fixture
truth.

From the repository root (NumPy 2.0.2, SciPy 1.13.1):

```
python3 AnalysisITC.Tests/PublishedBenchmarks/nature2022-can-wt-sequential/generate_reference_fixtures.py /path/to/itc_two_site_fit-main
```
