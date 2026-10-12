# Summary precision family for macOS and Avalonia

## Summary

Add a complete uncertainty family that describes **precision of the reported estimate**, alongside the existing **combined spread** presentation. Support automatic, ± error, interval, combined error and interval, and no uncertainty in both desktop applications.

Keep combined spread as the default. Preserve fitted central values, current summary weighting, and saved scientific results.

## Scientific calculation

Implement the new family in shared Core, building on [SummaryUncertainty.cs](/Users/frederiktheisen/Projects/FT-ITC-Analysis/AnalysisITC.Core/Presentation/SummaryUncertainty.cs).

- **Local means:** include observed scatter and individual fitting uncertainty. With `n` = experiment count, `s²` = between-experiment sample variance, and `uᵢ` = each fitted parameter’s uncertainty:
  
  `SE² = s²/n + Σuᵢ²/n²`

  This equals current Combined SD divided by √n for ordinary local means. For one experiment, retain its fitting uncertainty.

- **Local temperature trends:** calculate uncertainty at the evaluation temperature. With `r²` = residual variance, `Sxx = Σ(Tᵢ − T̄)²`, and `wᵢ(T) = 1/n + (T − T̄)(Tᵢ − T̄)/Sxx`:
  
  `SE(T)² = r²[1/n + (T − T̄)²/Sxx] + Σwᵢ(T)²uᵢ²`

  Precision therefore depends on both experiment count and temperature coverage. The residual contribution follows standard regression mean-response uncertainty; adding individual fitting uncertainty is the chosen application approximation. [NIST reference](https://itl.nist.gov/div898/handbook/pmd/section5/pmd511.htm)

- **Slopes, including ΔCp:** reuse the existing propagated slope uncertainty:
  
  `SE(slope)² = r²/Sxx + Σ[(Tᵢ − T̄)/Sxx]²uᵢ²`

  Do not divide this result by √n again. Exactly two distinct-temperature observations propagate their fitting errors, with zero residual-scatter contribution.

- **Shared and constrained parameters:** retain their saved model-estimated uncertainty. Bootstrap dispersion already estimates uncertainty of the fitted quantity; it requires no additional SEM conversion. Profile-derived equivalent SD remains an approximation. Bootstrap iteration count never enters `n`. [Standard-error explanation](https://www.stat.cmu.edu/~cshalizi/dst/18/lectures/18/lecture-18.html)
- Preserve existing asymmetric propagated intervals and thermodynamic transformations. Derive Kd from summarized Gibbs energy using the selected uncertainty, rather than averaging Kd.
- Use zero for an error contribution that cannot be estimated, as requested. Preserve the reason internally and never fabricate confidence bounds or replace a non-finite central value with zero.

## Core interfaces and output behavior

- Add `SummaryUncertaintyFamily` with `CombinedSpread` and `EstimatePrecision`, independently of the existing formatting enum. Preserve existing enum values and overload behavior.
- Pass the family explicitly through summary evaluation, report options, table export, clipboard output, and temperature envelopes. Existing callers default to spread; desktop entry points pass their selected family.
- Include family in summary cache keys and family plus display style in envelope cache keys. Switching presentation must not reuse stale errors or bands.
- Support the full formatting suite. Automatic retains the current asymmetry-based choice; precision labels use **Combined SE**, **Slope SE**, or **Model-estimated SE** as appropriate.
- Apply the family to all desktop summary text, tables, reports, preview, printing, PDF, CSV/TSV, and clipboard paths. Retain individual fitted values and their numerical uncertainty.
- Precision-family local trend bands use ±1 SE for error-only display, the existing propagated interval for interval/both, and no band for None. Automatic follows its resolved style. Shared-model bands use saved model uncertainty. Preserve legacy spread-family behavior and individual-observation whiskers.
- Precision exports identify standard-error columns with `_se`; retain legacy spread column names and existing interval-column conventions. Preserve output eligibility and suppression rules without recombining a subset.

## Both desktop applications and persistence

- Use a family selector beside the existing error-style control in Preferences, Results, report windows, and result exporters. This is the minimal implementation layout; later visual refinement does not affect the calculation API.
- Define shared labels, option mappings, and tooltips in Core. Use each application’s existing controls and renderer.
- Persist the application preference through existing settings stores. Report/export windows initialize from that preference and carry an explicit selection through generation.
- Save report family selection as an optional presentation-setting field. Missing fields restore spread; omit the field for legacy spread output. Update report option copying, equality, dirty-state checks, reader/writer mapping, and the current schema without changing package or application versions.
- Keep the web viewer’s existing default behavior; adding web controls is outside this feature.
- Update concise manual/help explanations and [ANALYSIS_REPORT_MAP.md](/Users/frederiktheisen/Projects/FT-ITC-Analysis/Documentation/ANALYSIS_REPORT_MAP.md), including combined parameters and temperature-band behavior.

## Validation and interpretation

- Use independently calculated expectations. For values `10 ± 1` and `12 ± 3`, verify centre `11`, Combined SD `√7`, and combined SE `√3.5`, with unchanged propagated interval bounds.
- Cover single experiments, unequal fitting errors, scatter-only and fitting-error-only contributions, asymmetric intervals, repeated temperatures, two-point trends, extrapolation, slope uncertainty, and nonlinear Kd propagation.
- Verify shared parameters are never divided by experiment count; changing bootstrap count metadata alone cannot change SE.
- Verify zero fallbacks, duplicate experiment references, fixed parameters, mixed constraints, and existing output suppression.
- Check cache switching, every formatting style, preview/export agreement, `_se` headers, saved-report round trips, and older files lacking the new field.
- Run `AnalysisITC.Core.Tests`, relevant Avalonia tests, and native macOS build/regression/render checks. Exercise both desktop controls and renderers without packaging or release work.

**Interpretation:** This is approximate combined precision, not a calibrated prediction interval or a guarantee of how well the true value is known. Observed scatter may already include fitting noise, and cross-experiment covariance remains omitted. Keep that explanation in tooltips, scientific help, and optional expanded report explanations. Zero fallback means no estimated contribution, not demonstrated exactness.
