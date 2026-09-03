# FT-ITC model audit

**Updated:** 2026-09-01

**Baseline:** `9c7037d9e5173361074bcb8d01e82248af98e1b3`
**Previous file:** SHA-256 `adfe4438bbf3966903cf20d8f10b9e04ac4dba8fad9fb430af0f1504b24f4fd0`

## Verdict

No confirmed High or Stop-ship mathematical defect remains. The open items below are mostly methodological choices, workflow contracts, or risks that still need a decisive reproduction. Do not change those paths until the proposed test demonstrates the claimed behavior and the intended contract is clear.

The only consistently failing scientific regression is the published two-site recovery benchmark, P-005. Its forward-equation oracle passes, so changing the model equation is not yet justified.

## Closed issues

- F-001 — Integrated `DH/NDH` concentration inference [closed—fixed]
- F-002 — Two-site physical-root selection [closed—fixed]
- F-003 — Competitive equilibrium root selection [closed—fixed]
- F-004 — Uncertainty row admission and counts [closed—fixed]
- F-005 — Saved uncertainty re-filtered by current limits [closed—fixed]
- F-006 — Legacy affinity model mismatch [closed—removed]
- F-007 — Obsolete isomerization model [closed—removed]
- F-008 — Integration-end sample exclusion [closed—fixed]
- F-012 — Concentration resampling semantics [closed—fixed]
- F-014 — Zero-work concentration LOO [closed—fixed]
- F-015 — Limit-terminated uncertainty fits admitted [closed—fixed]
- F-017 — Competitive concentration label [closed—fixed]
- F-018 — Central apparent `Kd` used total competitor [closed—fixed]
- F-020 — Global value labelled RMSD [closed—fixed]
- F-021 — Restore Defaults selected Fast [closed—fixed]
- F-023 — Raw-molar reader scaling [closed—fixed]
- F-024 — PEAQ numeric power conversion [closed—false alarm; comment only]
- F-025 — Target temperature used for result evaluation [closed—intentional]
- F-026 — Solver cancellation-token ownership [closed—fixed]
- F-031 — Small-sample empirical percentile endpoints [closed—intentional]
- F-033 — Validation overwrote stored/custom limits [closed—targeted regression passes]
- P-004 — MicroCal/SEDPHAT export units and row alignment [closed—fixed]
- P-006 — Persistence of unsolved session configuration [closed—out of scope]

## Open issues

### F-009 — Irregular-time heat uncertainty [open—needs test]

- **Likely observation:** irregular or missing timestamps alter error bars and error-weighted fits incorrectly; integrated heat itself is unaffected.
- **Test:** compare `EstimateError2` with an explicit `w^T C w` calculation on the same uniform and irregular synthetic trace.
- **Close if:** supported files are effectively uniform or the absolute variance error is negligible.

### F-010 — LOO uncertainty meaning [open—contract decision]

- **Likely observation:** reported LOO SD/CI differs from textbook jackknife uncertainty because it is the raw spread of delete-one fits.
- **Test:** use an analytic sample mean and compare the application with both raw delete-one spread and the jackknife formula.
- **Close if:** the output is intentionally an influence/robustness spread and is labelled accordingly.

### F-011 — Covariance in derived uncertainty [open—method decision]

- **Likely observation:** central `-TΔS = ΔG-ΔH` is correct, but its uncertainty is too large or small.
- **Test:** create matched rows where `ΔG` and `ΔH` move identically; `ΔG-ΔH` must have zero spread.
- **Close if:** derived values are already recomputed per matched row or independent-marginal propagation is the explicit contract.

### F-013 — Unlocking an all-locked global fit [open—intent unclear]

- **Likely observation:** “unlock during uncertainty” gives no variance for shared values because the analysis dispatches as independent fits.
- **Test:** lock every shared coordinate in a two-experiment analysis, enable unlocking, and inspect dispatch and retained shared-coordinate variance.
- **Close if:** the intended operation is only control of a common fixed multi-fit value, not a joint global uncertainty refit.

### F-016 — Two-site label switching [open—deferred]

- **Likely observation:** site-specific intervals become broad or bimodal while predicted heat remains stable.
- **Test:** pass exact swapped parameter tuples through the summarizer, then measure switching frequency in realistic seeded bootstraps.
- **Close if:** supported initialization/constraints prevent switching in realistic cases or define a physical site order.

### F-019 — Tandem dissociation pre-state [open—accepted]

- **Likely observation:** the first predicted heat after a tandem boundary is wrong when that segment's stored pre-state differs from the previous propagated state.
- **Test:** construct a two-segment example with deliberately different states and compare the boundary heat with an independent reaction-extent calculation.
- **Close if:** the merger guarantees those two states are always identical. Otherwise use the stored segment state and add the regression.

### F-022 — Locale-dependent legacy export [open—low]

- **Likely observation:** decimal-comma locales produce extra comma-delimited fields.
- **Test:** export identical data under `da-DK` and `en-US`; compare field counts and numeric parsing.
- **Close if:** every supported export entry point guarantees invariant formatting.

### F-027 — Limit policy for later refits [open—contract decision]

- **Likely observation:** a result fitted under Extended limits behaves differently when reopened under Standard limits.
- **Test:** fit/save outside Standard but inside Extended, change the preference, reload, and inspect effective limits before solving.
- **Close if:** reruns are explicitly defined to use current preferences and the UI reports the changed fit domain.

### F-028 — Joint identity in independent-global LOO [open—unverified]

- **Likely observation:** joint covariance or intervals change with completion order or member-specific failures.
- **Test:** reverse completion order and force different failed omission IDs in two deterministic members; compare joint rows and covariance.
- **Close if:** no joint statistic uses ordinal pairing or only per-member marginals are reported.

### F-029 — Extreme weak-binding cancellation [open—low]

- **Likely observation:** relative numerical error grows at the weakest affinity bound, currently at an absolute heat scale near `10^-13 J`.
- **Test:** compare direct and high-precision/rationalized formulas over allowed bounds and judge error against instrument noise.
- **Close if:** no supported case produces NaN, wrong sign, or measurable absolute error.

### F-030 — Child solver settings [open—low]

- **Likely observation:** independent child fits use defaults instead of a programmatically configured parent tolerance or iteration budget.
- **Test:** assign distinctive parent settings and inspect each child solver.
- **Close if:** the API intentionally defines children as fresh fits using application defaults.

### F-032 — Member versus joint success counts [open—low]

- **Likely observation:** member success counts exceed the number of complete global uncertainty rows.
- **Test:** force different failed replicate IDs in two members and inspect all displayed counts.
- **Close if:** member and joint counts are separately and accurately labelled.

### P-001 — Tandem merge with different stocks [open—contract decision]

- **Likely observation:** the second segment's material balance is wrong when syringe stock concentrations differ.
- **Test:** merge 100 and 200 µM segments and inspect second-segment injected mass and concentration.
- **Close if:** mismatched stocks are rejected or same-stock use is an explicit requirement.

### P-002 — Buffer subtraction with mismatched protocols [open—contract decision]

- **Likely observation:** ordinal subtraction removes inappropriate heat when blank and sample injection volumes differ.
- **Test:** subtract a 2 µL blank schedule from a 10 µL sample schedule.
- **Close if:** mismatches are blocked or absolute subtraction is explicitly user-controlled.

### P-003 — Empirical interval coverage [open—validation gap]

- **Likely observation:** nominal 95% intervals may not have 95% coverage for realistic ITC designs.
- **Test:** simulate known parameters with realistic noise/concentration error and measure bias and coverage.
- **Close if:** coverage is adequate in the declared operating domain; otherwise document the observed coverage rather than assuming it.

### P-005 — Published two-site recovery [open—reproduced]

- **Observation:** both solvers converge near `dH1 = 2047 J/mol`; the benchmark expects `3210.3832 J/mol`.
- **Test:** compare objective values at published and fitted parameters, profile the loss, use broad multi-start, and reconstruct the publication's preprocessing conventions.
- **Close as a non-defect if:** fitted parameters have lower loss under the declared objective and published values require different preprocessing. Do not alter the forward equation without a forward counterexample.

## Priority

1. Resolve F-013 and F-027 as product contracts before implementation.
2. Add the accepted F-019 boundary test.
3. Run the analytic falsifiers for F-009, F-010, and F-011.
4. Diagnose P-005 without changing the forward model.
5. Measure F-016 and F-028 before implementing alignment or pairing changes.

Competitive apparent uncertainty and the latent `dHapp` path remain incomplete, but the central F-018 `Kdapp` defect is closed.
