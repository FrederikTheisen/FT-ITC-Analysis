---
title: Additional tools
summary: Design simulated titrations, subtract buffer controls, and merge standard or back-mixed tandem experiments.
slug: additional-tools
nav_order: 10
last_verified: 2026-08-22
_verification:
  product_version: "1.4.3"
  commit: "7a19b583468b4b087e130e4b27c8140cd428339a"
---

# Additional tools

The **Tools** menu contains workflows that prepare or combine experiments but are not ordinary model fitting. Their output becomes part of the open project, so save a checkpoint before applying changes to valuable work.

## Experiment Designer

Open **Experiment Designer...** to simulate a proposed titration using the same model concepts used for analysis.

### Configure the design

Choose or enter:

- instrument and available cell or syringe volume information;
- cell and syringe concentrations;
- injection count and injection volumes;
- an optional smaller first injection;
- model and model parameters;
- model-specific options;
- synthetic noise, when desired.

The simulated graph updates as the design changes. Use it to see whether the transition lies within the injection series, whether early or late points establish useful baselines, and whether the expected heat scale is measurable.

### Fit the simulated experiment

Run a fit against the synthetic data to see whether the proposed design can recover the known input under the chosen noise and fitting configuration. Repeat with plausible uncertainty in concentration and parameters rather than testing only an ideal curve.

> **Interpretation:** Recovering simulated parameters is a design diagnostic under the assumed model and noise. It does not guarantee sample stability, correct active concentration, absence of artifacts, or validity of the model for the real system.

### Design tandem experiments

Enable tandem design to repeat an injection series across syringe-load segments. The designer continues concentration bookkeeping between segments and applies the tandem back-mixing assumptions. A small first injection is applied at the beginning of each segment.

Use tandem design when a reload might extend the informative concentration range. Compare the gain in parameter recovery with the additional preparation and mixing assumptions.

## Buffer Subtraction

Open **Buffer Subtraction...** after loading a target experiment and a distinct processed reference experiment. The reference must contain usable integrated heats and must not itself be configured as a buffer-subtracted experiment.

### Choose targets and reference

1. Select the processed buffer or reference experiment.
2. Select one or more distinct target experiments.
3. Choose a subtraction method.
4. Inspect the preview and included reference injections.
5. Apply the subtraction and inspect the corrected target heats.

The reference is disabled from ordinary analysis when applied as the buffer source. Raw integrated peak areas remain available; subtraction supplies corrected areas used downstream. Original files are not modified.

### Matched subtraction

**Matched** evaluates a reference heat at the target injection number. When the exact reference injection is unavailable, the nearest valid reference injection is used.

Use it when injection-by-injection background is reproducible and schedules align. Inspect mismatched schedules carefully.

### Linear subtraction

**Linear** fits a straight background trend through valid reference heats and evaluates it for the target injections. Use it when background changes approximately linearly through the titration.

### Exponential-decay subtraction

**Exp. decay** fits an exponential-decay background when enough valid reference points are present. Use it only when the reference trend supports that shape.

> **Caution:** Subtraction can reduce an apparent background while adding uncertainty and model dependence. Compare reference and target preparation, concentrations, injection schedules, and volumes before applying it.

## Experiment Merger

Open **Experiment Merger...** to join consecutive titration segments collected on the same continuing cell contents after syringe reloads.

### Prepare segments

Load all segments and place them in chronological order. Confirm that they belong to the same cell sequence, use compatible timing and units, and have correct concentrations and injection metadata.

The merger appends thermograms and injection sequences, preserves segment boundaries, and stores calculated starting active cell and titrant concentrations for every segment. If an input has already been baseline processed, the merger can use its baseline-corrected trace for that segment.

### Standard merge

Use standard concatenation when the next segment continues concentration progression without a separate back-mixing correction. Inspect the time-shifted thermogram, injection sequence, segment boundary, and calculated concentration ratio after merging.

### Back-mixing merge

Use back-mixing when syringe reloads can mix dead or overflow volume with the active cell contents. Configure:

- syringe dead or overflow volume;
- whether displaced titrated solution was removed between segments;
- the mixing fraction between remaining dead volume and active cell;
- automatic back-mixing optimization when appropriate.

Automatic scanning can choose a mixing fraction for one or two reload transitions. Treat the selected fraction as a fitted experimental correction and check its plausibility.

> **Interpretation:** Back-mixing can improve continuity under its volume model, but the software cannot verify the physical mixing history. Report the correction and test sensitivity to plausible settings.

### Validate a merged experiment

After merging:

1. Inspect thermogram continuity and segment markers.
2. Confirm every injection and its inclusion state.
3. Review segment starting concentrations and concentration ratios.
4. Process the merged experiment consistently.
5. Fit and inspect residuals around segment transitions.
6. Save the project under a new name.

Do not count segments from one continuing titration as independent replicates.

