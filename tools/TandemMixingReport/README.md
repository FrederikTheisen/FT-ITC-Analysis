# Tandem mixing report

Console tool that writes a PDF report of the model-free tandem mixing criterion
(`TandemContinuityScanner` in `AnalysisITC.Core/TandemMixingContinuity.cs`). It is a
diagnostic, not a test: `dotnet test` never runs it.

## Run

From the repository root:

```bash
dotnet run --project tools/TandemMixingReport -- "/Users/frederiktheisen/Mit drev/Academia/Postdoc_2024_IBS/FT-ITC Analysis Publication/Concat/rawfiles"
```

- Positional arguments are `.ftxtc` projects or folders (a folder adds its `.ftxtc` files, not recursively).
- `--out path.pdf` sets the output. The default is `output/tandem-mixing-report.pdf` (git-ignored).
- The console prints one line per case with the chosen fractions; the full run takes seconds.

## What the report contains

- **Page 1:** the method and settings (points per side, scan steps, the `MixingFractionBias`
  centre and strength, back-mixing settings, dilution method), the commit, the date, and a summary
  table of every case: runs, true and chosen fractions, and the error in percentage points.
- **One page per case:** the full titration at the chosen fractions (runs alternate filled and
  hollow markers, fit windows shaded) and one zoom per transition. Each zoom shows the run before
  (blue) and the run after at the chosen fraction (orange) with their quadratic. On synthetic
  pages it also shows the window at the true fractions (aqua rings, dashed quadratic). Each zoom
  lists the biased score at the chosen and, where known, the true fraction. When the true
  fraction scores worse than the chosen one, the error comes from the criterion, not the search.

## Cases

**Synthetic** (always included). One-site heats on the real back-mixing bookkeeping, the same
recipe as `TandemMixingContinuityTests.CreateSyntheticTandem`: 30 µM cell in 200 µL, n = 1,
ΔH = −40 kJ/mol, 19 injections per run (an excluded 0.4 µL injection, then 2 µL), and noise of
0.2% of the largest heat with a fixed seed per case. At Kd 25, 50, 100, 200 and 500 µM:

| Runs | Syringe | True fractions |
|------|---------|----------------|
| 2 | 150 µM | 15% |
| 3 | 100 µM | 10% / 25% |
| 4 | 75 µM | 5% / 15% / 30% |

**Real.** The repository fixture `AnalysisITC.Tests/Tandem/280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc`
(always included), plus every project given on the command line. A project's non-tandem
experiments, in project order, are the runs. Each is processed with its saved settings, as
`TandemRealDataTests` does. A project with fewer than 2 or more than 5 runs, or a failed scan,
is listed in the table with the reason and gets no page.

Back-mixing settings for every case: dead volume 80 µL, titrated overflow removed, default
dilution method.

## Changing it

- Synthetic designs and Kd values: `ReportData.KdMicromolar` and `ReportData.SyntheticDesigns`.
- The scanner, window and bias are read from Core (`TandemContinuityScanner.PointsPerSide`,
  `TransitionWindow`, `Bias`). The report always reflects the current criterion; there is no
  separate copy to update.
