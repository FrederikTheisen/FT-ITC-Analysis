# Dumas ideal-mixing bookkeeping validation

This study tests the finite-injection implementation, not the empirical superiority of a mixing model. The scientific inspiration is Philippe Dumas, *Isothermal titration calorimetry in the single-injection mode with imperfect mixing*, Eur Biophys J 51, 77–84 (2022), doi:10.1007/s00249-021-01588-4. FT-ITC uses ideal mixing only; the fixed Simpson rule is its numerical approximation, not a claimed reproduction of the paper's imperfect-mixing analysis.

`generate_reference.py` uses Python's standard library, independent mass-balance equations, and composite eight-point Gauss–Legendre integration. It does not import FT-ITC or pytc. Run it to stdout to reproduce `reference.json`. Reference refinement from 32 to 64 panels changes heat by at most 7.42e-11 of peak injection heat.

Run the executable checks with:

```sh
dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --filter FullyQualifiedName~DumasInjectionHeatTests
```

## Results (14 September 2026)

- 18 ordinary forward cases cover one-site, independent two-site, competitive, sequential 2–4-step and monomer–dimer models, including nonzero tandem segment starts and variable shot volumes.
- One-site checks span Ka × initial cell concentration of 1, 100 and 10,000; injection fractions of 0.005, 0.01 and 0.02; and saturation. The largest Simpson error is 0.00042578 of peak heat (0.04258%), below the declared 0.1% acceptance limit.
- A separate, non-acceptance stress case uses shots of 10% of cell volume and tight binding. Its maximum discrepancy is 0.7527% of peak heat. No adaptive fallback hides this limitation.
- Six identifiable synthetic fitting cases (one-site exothermic/endothermic, two-site, competitive, dissociation and two-step sequential) recover all free binding parameters within 2% using Levenberg–Marquardt from perturbed starts. Offsets are held at their generating values for this identifiability check. Forward accuracy and parameter recovery are separate criteria.
- Reverse-order and excluded-shot evaluations are unchanged. A cubic heat-content path independently verifies Simpson weights and exactly three equilibrium-content evaluations; a steady-flow case verifies incoming/displaced heat cancellation.

Representative local median objective timings (7 batches of 100 evaluations, after warmup):

| Model | Historical exponential/endpoint heat | Dumas | Ratio |
| --- | ---: | ---: | ---: |
| One-site | 0.0141 ms | 0.0195 ms | 1.38× |
| Two-site | 0.454 ms | 0.644 ms | 1.42× |
| Four-step sequential | 0.940 ms | 1.43 ms | 1.52× |

These timings are diagnostic, not a CI performance threshold or a prediction of total fitting time. Existing MicroCal/exponential historical references and the frozen `ExternalIntegratedHeats` pytc study remain separate and retain their original heat conventions and tolerances.

End-to-end fitting timings (median of five fits after warmup, without uncertainty runs) were 1.387 ms historical versus 2.015 ms Dumas for one-site (1.45×), and 97.42 ms versus 165.02 ms for two-site (1.69×). Both methods fit the same independent continuous-mixing observations from the same perturbed starts, with exponential endpoints and fixed true offsets. The heat laws differ, so optimizer trajectories and fitted values can differ; this is an actual-workload comparison, not a claim of a universal fitting slowdown.
