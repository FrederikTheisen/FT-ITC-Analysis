# Derived analysis scientific validation

This record supports the A06 claims for the temperature, protonation and
electrostatics analyses. The tests in
`AnalysisITC.Core.Tests/DerivedScientificReferenceTests.cs` use source
equations and synthetic targets defined independently of the production
implementation. They check the reported point estimate from the original
fit separately from uncertainty sampling; bootstrap draws are not used as a
replacement for the point estimate.

## Claim-to-evidence matrix

| Derived analysis | Implemented quantity and equation | Units and convention | Evidence | Assumptions and limits |
| --- | --- | --- | --- | --- |
| Temperature / Spolar–Record | `ΔS_hydration = ΔCp · a_np · ln(T/386 K)`, `ΔS_conf = ΔS − ΔS_hydration − ΔS_rt`, and `R = ΔS_conf / ΔS_residue`; the application also supports mean and reference temperature evaluation. | Temperature is converted from °C to K for the logarithm; entropy and heat capacity are J mol⁻¹ K⁻¹. `ΔCp` and `ΔS` come from the original temperature dependence fit. | The exact central-value evaluation and sampled-input behavior are covered by `SpolarRecordAnalysisSamplingTests`; the source basis is Spolar and Record, *Science* 1994, DOI [10.1126/science.8303294](https://doi.org/10.1126/science.8303294). | Empirical surface-area partition coefficients, 386 K convergence temperature, and a fixed rotational/translational entropy are model assumptions. `R` is an empirical residue-equivalent interpretation, not a directly fitted structural count. The analysis is enabled only for one-set models with a sufficient temperature span. |
| Protonation | Linear relation `ΔH_observed = ΔH_binding + m · ΔH_buffer-ionization`; the intercept is the binding enthalpy and the stored `ProtonationChange` is slope m. The desktop **Protons** output displays −m, as documented in the manual. | Enthalpies are stored in J mol⁻¹; buffer enthalpy is evaluated at the experiment's measured temperature. | `DerivedScientificReferenceTests.ProtonationDependenceUsesBindingEnthalpyPlusProtonationChangeTimesBufferEnthalpy` fits an independent synthetic target. Buffer constants are recorded in `BUFFER-CONSTANTS.md`. The thermodynamic linkage basis is Baker and Murphy, *Biophysical Journal* 71 (1996), DOI [10.1016/S0006-3495(96)79403-1](https://doi.org/10.1016/S0006-3495(96)79403-1). | Requires multiple buffers with compatible protonation metadata. A microscopic uptake/release interpretation requires an explicit sign and reaction convention; buffer-specific interactions, pH differences and multiple linked groups can invalidate it. |
| Electrostatics / ionic strength | `ln(Kd) = ln(Kd0) + s·sqrt(I)`; the report plot receives `sqrt(I)` and displays `log10(Kd)`. Counter-ion release uses `ln(Kd)` versus `ln(a_ion)`. | Ionic strength `I` is mol L⁻¹; salt sensitivity `s` has units (mol L⁻¹)⁻¹/²; affinity is Kd in mol L⁻¹. Salt activity is the application’s concentration-power proxy. | `DerivedScientificReferenceTests.IonicStrengthDependenceRecoversKnownDebyeHuckelParametersAndUnits` checks independent recovery and display units. Existing `AnalysisReportBuilderTests.DebyeHuckelReportPlotsLogKdAgainstSquareRootOfIonicStrength` checks the rendered transform. The square-root trend is observed under limiting-law conditions by de los Rios and Plaxco, *Biochemistry* 44 (2005), DOI [10.1021/bi048444l](https://doi.org/10.1021/bi048444l). | The fit is a two-parameter empirical Debye–Hückel-style relation, currently without curvature. It needs at least three valid varying ionic strengths for the exposed analysis. The activity proxy does not establish a full activity-coefficient model, and Kd0 extrapolation can be weakly identified when the data do not approach low ionic strength. |

## Tested and untested regimes

The new reference tests exercise one-set-of-sites result objects with three
members for the temperature and electrostatic mappings, and a public
protonation-analysis run with three synthetic buffer-enthalpy points. The
temperature case uses 5, 25 and 45 °C, `ΔH(25 °C) = −9000 J mol⁻¹`,
`ΔCp = +100 J mol⁻¹ K⁻¹`, and constant `log10(Ka) = 6`; it checks the
stored member best fits, fitted dependences, Kd conversion and Spolar mode
selection. The protonation case uses 10, 20 and 30 kJ mol⁻¹ buffer enthalpies,
zero input SD, one calculation iteration and the exact target
`ΔH = 1000 + 0.3·ΔH_buffer`. The electrostatic case checks an exact synthetic
two-parameter fit, the optional display curvature transform and the
counter-ion log-space point mapping.

These tests do not establish optimizer recovery from raw thermograms,
alternative starting values, behavior for two-site/sequential/competitive
models, systematic buffer or salt activity errors, metal binding, pH drift,
or empirical uncertainty coverage. Existing persistence/rendering tests cover
saved advanced-analysis state and presentation; they are not independent
scientific recovery benchmarks.

## Buffer source and unit review

[TAPSO and imidazole](BUFFER-CONSTANTS.md) have independent, visually verified
reference values and focused regression tests. TAPSO's charge and local pKa
temperature trend are corrected; its existing heat capacity is preserved.
Imidazole's heat-capacity slope is corrected to the field's J/(mol K) units.
The review explicitly limits temperature extrapolation and broader registry
claims. It is not a complete salt-activity or buffer-calibration study.

The protonation regression uses the original 10, 20 and 30 kJ/mol independent
buffer-enthalpy targets. Production now uses direct linear least squares for
the stated straight-line relation, eliminating the former nonlinear solver's
iteration-limit failure at those normal physical magnitudes. A constant
buffer-enthalpy coordinate cannot identify the slope; the regression checks
that this case is rejected without replacing the previous valid result. The displayed
ionic-strength dependence now divides by ln(10), rather than a rounded 2.303,
when converting natural logarithms to log10 values.
