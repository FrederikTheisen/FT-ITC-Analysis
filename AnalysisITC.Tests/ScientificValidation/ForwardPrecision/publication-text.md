# Forward-model publication text

## Manuscript: Quality control

Automated tests cover data processing, concentration calculations, binding models, fitting, uncertainty calculations, project persistence, and reporting. In a separate forward-model comparison, FT-ITC Analysis reproduced integrated injection heats generated with pytc [4] for all 52 cases at floating-point precision, using prescribed parameters and matched finite-injection assumptions. The comparisons covered one-site, two-independent-site, competitive, and two- to four-step sequential binding (Supporting Methods S11; Table S1; Figure S6).

## Supporting Methods S11: External forward-model comparison

Synthetic integrated injection heats for 52 cases were generated with pytc 1.1.5 and compared with FT-ITC Analysis predictions at prescribed parameters using matched finite-injection assumptions, without fitting. The cases covered one-site, two-independent-site, competitive, and two- to four-step sequential binding, including unequal fractional site capacities and varied concentrations and injection protocols. Two-independent-site references used exact binding-polynomial parameterizations.

References were evaluated with default and converged arithmetic. The precision comparisons used a binding-polynomial xtol of 2 × 10⁻²⁴ M and 80-digit arithmetic for the competitive analytical expression. All 52 cases passed the floating-point criterion:

|Δqᵢ| ≤ 512ε(qₘₐₓ + |qᵢ|)

Here, Δqᵢ is the difference between FT-ITC and reference injection heats, qᵢ is the reference heat, qₘₐₓ is the largest absolute reference heat in that case, and ε = 2.220446049250313 × 10⁻¹⁶.

**Supporting Table S1:** Forward-model validation. PASS indicates that every injection in every case met the floating-point criterion.

| Model | Cases | Result |
|---|---:|:---:|
| One set of sites | 11 | PASS |
| Two independent sets of sites | 32 | PASS |
| Competitive binding | 3 | PASS |
| Sequential binding with two to four steps | 6 | PASS |

Full case parameters and reference calculations are provided in the repository.

**Supporting Figure S6:** Forward-model comparisons for one-site, two-independent-site, competitive, and sequential binding. Lines show converged pytc reference heats and circles show FT-ITC predictions at the prescribed parameters. Sequential curves show two, three, and four steps. Residuals are FT-ITC minus pytc injection heats.
