# Wu 2023 HSA-Co2+ two-step sequential benchmark

This directory investigates the H9A and H67A HSA-Co2+ titrations from Wu
*et al.*, *Chemical Science* 14, 6244-6258 (2023):

- paper: <https://doi.org/10.1039/D3SC01723K>
- deposited data: <https://doi.org/10.17630/34d5ba8e-569d-4630-aac7-8c61a64928a0>
- Supplementary Table S3: Origin 7.0 `Fit 1-1`, two sequential sites

No model code was changed and no alternative binding model was examined.

## Why the fixtures are digitized

The deposited ITC directory contains raw MicroCal `.itc` thermograms only. It
does not contain Origin projects, `.DH`/`NDH` tables, or another numerical
integrated-heat export. The paper says that an averaged Co2+-into-buffer heat
of dilution was subtracted before fitting, but the deposited directory has
only one raw blank thermogram and no author integration regions. Reintegrating
those traces would therefore invent both integration and average-blank details.

The open-access paper's Figure 6 is the available author-integrated source. Its
embedded 886 x 1119 pixel image contains 34 included points for each titration.
The first 2.0005 uL injection is omitted from the plot, consistent with the
usual Origin exclusion, but is retained in each `.DH` fixture with a zero heat
because it still contributes to the concentration history.

The extraction is deterministic:

- x-axis ticks: pixel 177 = ratio 0 and pixel 874 = ratio 9;
- y-axis ticks: pixel 19 = 0 and pixel 999 = -5 kcal/mol injectant, or 196
  pixels per kcal/mol;
- expected x positions use the MicroCal ratio trajectory from the raw metadata;
- cyan H9A and magenta H67A pixels are followed in a bounded vertical window,
  with a color-score-weighted median used for each marker;
- normalized heat is converted to absolute microcalories with
  `Q = q * 1000 cal/kcal * Cs * Vinj * 1e6 ucal/cal`; for every included 8 uL,
  2 mM injection this is `Q_ucal = 16 * q_kcal/mol`.

The source image has SHA-256
`353bd37595fce4f517a45c5045f2761f999f1fee19796523dc080cd40c00e508`.
`extract_figure6_integrated_heats.py` verifies that image and regenerates or
checks both fixtures from a local copy of the article PDF.

Deposited raw-file SHA-256 values are:

| File | SHA-256 |
| --- | --- |
| `H9A-Co.itc` | `2ff947e66f1f0a79e484809f7d33f31ad9960b0f168692e9a99f2d0855f8f913` |
| `H67A-Co.itc` | `f6f86182ff02f7842af9a01fd03647e14c003a221aaf8e7721b0b22bb3692019` |
| `buffercobaltBlank.itc` | `52797ff134b557e41259b6bdd5c9295be2fba2c6fef3f3d5005371367ccfebb4` |

The Figure 6 points are already blank-subtracted, so the deposited raw blank
must not be subtracted again.

## Imported experiment and fit

Both fixtures preserve the raw header and paper conditions: 25 C, 50 uM HSA
in a 1.4314 mL VP-ITC cell, 2 mM CoCl2 in the syringe, one 2.0005 uL injection
and 34 x 8 uL injections. FT-ITC uses its existing two-step
`SequentialBindingSites` model, MicroCal dilution trajectory, excluded first
injection, unweighted residuals, and a locked zero offset. Both affinities and
both enthalpies are free, initialized at the Table S3 values. LM and
Nelder-Mead converge to effectively identical results.

| Data | Parameter | Table S3 | FT-ITC MicroCal/LM |
| --- | --- | ---: | ---: |
| H67A | log10 Ka1 | 4.85 | 4.8337 |
| H67A | DH1 (cal/mol) | -5568 | -5663 |
| H67A | log10 Ka2 | 3.33 | 3.3473 |
| H67A | DH2 (cal/mol) | -18250 | -17185 |
| H9A | log10 Ka1 | 4.85 | 4.3283 |
| H9A | DH1 (cal/mol) | -4958 | -7866 |
| H9A | log10 Ka2 | 3.45 | 3.2820 |
| H9A | DH2 (cal/mol) | -15820 | -14089 |

H67A is accepted as a parameter-recovery benchmark: affinity errors are below
0.02 log units and enthalpy errors are 1.7% and 5.8%. H9A is retained as an
explicit non-recovery diagnostic. The Table S3 H9A vector already predicts the
digitized curve with 28.6 cal/mol RMSD, while the all-free refit reduces this
only to 19.1 cal/mol but moves to a very different parameter vector. Both
optimizers select that same point. This is a shallow, correlated
affinity/enthalpy valley, consistent with the paper's warnings about suboptimal
`c` values and overlapping equilibria. The absence of the original numerical
integrated table and averaged blank prevents distinguishing digitization noise
from unpublished processing details more finely.

The automated assertions are in
`AnalysisITC.Core.Tests/Wu2023HsaCoSequentialBenchmarkTests.cs`.

## Reproduction

With `pypdf`, Pillow, and NumPy available:

```text
python3 extract_figure6_integrated_heats.py /path/to/D3SC01723K.pdf --check
python3 extract_figure6_integrated_heats.py /path/to/D3SC01723K.pdf --write
```
