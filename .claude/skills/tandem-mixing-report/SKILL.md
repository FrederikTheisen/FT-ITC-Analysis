---
name: tandem-mixing-report
description: Generate or rerun the PDF report of the model-free tandem mixing criterion (synthetic tandems at Kd 25-500 µM with known mixing fractions, plus real tandem .ftxtc projects, each with a full titration and per-transition zooms). Use when asked for the tandem mixing report, the model-free mixing report, or to check how a change to TandemMixingContinuity.cs / MixingFractionBias affects the chosen mixing fractions.
---

# Tandem mixing report

The tool lives in `tools/TandemMixingReport` (details in its `README.md`). It is a console
program, not a test.

## Steps

1. From the repository root, run:

   ```bash
   dotnet run --project tools/TandemMixingReport -- "/Users/frederiktheisen/Mit drev/Academia/Postdoc_2024_IBS/FT-ITC Analysis Publication/Concat/rawfiles"
   ```

   - Always included: the 16 synthetic cases (15 standard plus one short-run design) and the repository fixture `280-430-D2mut`.
   - The folder argument adds the user's real tandem projects. If the folder is not reachable
     (another machine, a cloud session), run without it and say that the real projects were left out.
   - Add other `.ftxtc` files or folders if the user names them. `--out <path>` overrides the
     default output `output/tandem-mixing-report.pdf` (git-ignored).
2. The console prints one line per case: chosen fractions, the true fractions for synthetic
   cases, or the reason a project was skipped.
3. Send the PDF to the user. In your reply, summarise the synthetic errors (chosen minus true, in
   percentage points) per Kd. Name the worst cases and any real project skipped or landing at an
   extreme fraction (near 0% or above 50%).
4. If you are comparing a code change, run the tool before and after on the same inputs, write
   the two PDFs to different `--out` paths, and report which fractions moved.

Do not commit the PDF. Do not change the synthetic designs or the scanner just to improve the
report; ask first.
