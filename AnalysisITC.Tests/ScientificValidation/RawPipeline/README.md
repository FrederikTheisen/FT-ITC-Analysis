# Analytic raw thermogram reference

`analytic-one-site.itc` is synthetic, not a laboratory acquisition. The
stdlib-only `generate_reference.py` defines the truth before processing or
fitting and writes `reference.json`, including every absolute injection heat
and the raw-file SHA-256. Both are original material under the repository's
MIT license. There is no digitization or third-party dataset transformation.

## Physical and acquisition definition

The cell contains 40 µM macromolecule in 200 µL; the syringe contains 500 µM
ligand. There are 32 injections of 2 µL, starting at 40 s and separated by
80 s. Temperature is 25 °C. MicroCal cumulative displacement is specified by
`u = cumulative volume / cell volume`, `M = M0(1−u/2)/(1+u/2)` and
`Ltotal = Cs u/(1+u/2)`.

Free ligand is independently bisected from
`Ltotal = Lfree + N M Ka Lfree/(1+Ka Lfree)`. If `B` is bound concentration,
the injection heat is `ΔH[V(Bafter−Bbefore) + v(Bafter+Bbefore)/2] + offset Cs v`.
This declares the endpoint-mean displaced-heat convention explicitly. The
per-injection `ligand_molar` and `heat_joules` fields describe this independent
raw-signal generator. The separate `implementation_ligand_molar` field records
the concentration trajectory from the current MicroCal approximate ligand
expression, `Cs u(1−u/2)`, and is used only as an application regression check.

The baseline is `3e-6 + 1e-9 t` watts. Each heat multiplies a triangle of
width 20 s and height 0.1/s, starting at the injection time. Its analytic
area and its 1 s right-endpoint sum are exactly one. Decimal-point raw power
is written in µcal/s using 4.184 J/cal. Thus the expected absolute heats do
not come from the application's integration or binding implementation.

## Reproducible application workflow

1. Open the bundled `.itc` file. Check 32 injections, 200 µL cell volume,
   40 µM cell concentration, 500 µM syringe concentration and 25 °C.
2. Set every integration window to start at injection time and end 22 s
   later. The extra 2 s is a baseline guard after the known 20 s pulse.
   Exclude the first injection from fitting; retain its integration.
3. Exclude integration regions from baseline fitting. Use a degree-1
   polynomial (outlier Z limit 3), degree-1 segmented baseline, or rigid
   spline with automatic mean handles at the default point density. The
   regions between pulses contain only the prescribed linear drift.
4. Process, then select one set of sites with syringe correction off.
   Start N at 0.85, Ka at `10^5.7 M^-1` (Kd about 2 µM), ΔH at −24 kJ/mol
   and offset at zero. Leave all four parameters free. Use unweighted
   fitting, no uncertainty estimation, and either optimizer with a 20,000
   iteration/evaluation cap and default preferences.
5. Compare the heats and parameters with `reference.json`; save `.ftxtc`
   and reopen it. `ScientificReferencePipelineTests` automates these steps
   for all six baseline/solver combinations, including round-trip values.

The independent generator parameters are N 1.1; Ka 1,584,893.192 M^-1
(Kd about 0.631 µM); ΔH −32 kJ/mol; offset +350 J/mol. Since the current app
uses MicroCal's approximate ligand concentration expression, the fitted
parameters differ slightly. `implementation_fit_regression` records the
current pipeline's N 1.0993303, log10(Ka) 6.206575, ΔH −32,008.74 J/mol and
offset 346.10 J/mol as regression expectations. They are not independent
parameter truth. Acceptance is ≤0.2 nJ absolute heat error; the fit regression
uses tight tolerances around those recorded values. The first three absolute heats
are approximately −31.13543039, −30.97446892 and −30.73168766 µJ; the JSON
contains full precision and all remaining injections.

The fixture validates a known drift, separated peaks and controlled windows.
It does not establish instrument kinetics, experimental baseline selection,
blank subtraction, correlated noise, tail truncation or uncertainty coverage.
Manual interaction, installation, a published figure, and independent user
acceptance remain the separate A10 tutorial/release work.

The thermogram heat source uses the rational cumulative ligand balance, while
the application uses the documented approximate MicroCal ligand expression.
The independently generated raw heat expectations remain external to the app;
the implementation concentration expectations and fitting recovery are
regression checks against the current implementation. This is not independent
external forward-model validation.
