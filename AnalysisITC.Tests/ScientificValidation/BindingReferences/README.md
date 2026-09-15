# Independent binding references

These are deterministic, synthetic references for audit A06. They are not
published experimental datasets and must not be described as published-data
reproduction.

All synthetic references and their generators are original repository material under the MIT license.

The reference heats are derived in `AnalysisITC.Core.Tests/IndependentBindingReferenceTests.cs`
from conservation equations and a 240-step bisection calculation written
independently of the production model. The fit then uses the production model
and both supported single-experiment solvers. Integrated heat is in joules;
concentrations are mol/L; cell volume is 1.4 mL; injection volume is 1.5 uL;
the syringe contains 1.0 mM titrant. Every injection is included, and the
reported target is the original-data best fit. The input SD is `1e-10` J for
every injection, so the tests exercise the relative weighted objective without
downweighting the saturated tail while retaining a zero-noise reference.

The conditional checks use 32 injections and pre-lock `N1 = 0.75`, `N2 = 1.45`,
and offset 0; their recovery tolerances are 3e-4 in log10 association
constant, 3e-4 relative in enthalpy, and 1e-3 in offset. The full-fit check
uses 240 injections over 360 uL, starts at `N1 = 0.70`, `N2 = 1.50`,
`logK1 = 7.30`, `logK2 = 5.20`, `H1 = -24500`, `H2 = 13000`, and offset 1700,
and predeclares tolerances of 2e-3 in N, 3e-3 in log10 association constant,
3e-3 relative in enthalpy, and 2 J/mol-equivalent in offset. The assertion
accepts either the declared site labels or the mathematically equivalent
label-swapped pair; the distinct-affinity starts (`K1 > K2`) normally select
the declared ordering.

## Two independent site classes

The conditional reference starts at 20 uM macromolecule. Site classes have known stoichiometries
`n1 = 0.75` and `n2 = 1.45`; those two N values are locked in the fit. The
association constants are `K1 = 10^7.20 M^-1` and `K2 = 10^5.10 M^-1`, and the
binding enthalpies are `H1 = -26000 J/mol` and `H2 = +12000 J/mol`. The
displacement offset is fixed at zero because this reference has no additional
dilution heat term beyond the stated finite-injection correction. The
exponential dilution convention is used:

```
u = cumulative injected volume / cell volume
M = M0 exp(-u)
L = Cs (1 - exp(-u))
```

For each state, free ligand `l` is the nonnegative root of

```
l + M (n1 l/(Kd1+l) + n2 l/(Kd2+l)) = L
```

and the cumulative heat is `Q = M V0 (n1 H1 theta1 + n2 H2 theta2)`.
The injection heat is independently formed as
`Qi + (v/V0)(Qi + Qprevious)/2 - Qprevious`.

The two-site form is the standard independent-site extension of the one-site
ITC mass balance. A useful primary-source discussion of this model family and
its inability to be distinguished from sequential binding at one concentration
is [Raut et al., 2011, variable-concentration ITC](https://pubmed.ncbi.nlm.nih.gov/21505408/).

## Competitive binding

The cell starts at 20 uM macromolecule with target stoichiometry `n = 1.15`,
and is pre-equilibrated with 12 uM competitor. Target parameters are
`K_A = 10^7.00 M^-1`, `H_A = -18000 J/mol`; competitor parameters are fixed
configuration inputs `K_B = 10^6.20 M^-1`, `H_B = -8000 J/mol`. The target
stoichiometry and displacement offset are locked only in the 32-shot
conditional reference. The 240-shot saturated companion fits both.

The competitive conditional fit starts at `N = 1.15`, `logK = 6.70`,
`H = -15000`, and offset 0; the locked-reference tolerances are 4e-4 in N,
1e-3 in log10 association constant, 4e-4 relative in target enthalpy, and
1e-3 in offset. Its full-fit companion uses the same physical reference and
starts at `N = 0.95`, `logK = 6.70`, `H = -15000`, and offset 0; it fits N,
logK, H, and offset with the same tolerances as the two-site full fit.

The conditional competitive reference uses MicroCal dilution; its full-fit
companion uses exponential dilution (defined above):

```
u = cumulative injected volume / cell volume
M = M0 (1-u/2)/(1+u/2)
L = Cs u / (1+u/2)
```

At each state, `x` is the free-site fraction and is independently solved from

```
x + rA x/(1/(KA*n*M) + x) + rB x/(1/(KB*n*M) + x) = 1
```

where `rA = L/(n M)` in the standard (inactive syringe-factor) mode and
`rB = Btotal/(n M0)`. The cumulative heat is
`Q = V0 n M (H_A boundA + H_B boundB)`, followed by the same finite-injection
heat correction above. This is an end-to-end target-plus-competitor example:
the competitor contributes to the cumulative heat and depletes against the
cell sites before the target fit. The full-fit test also writes the solved
competitive experiment to an in-memory `.ftxtc` package and reads it back,
checking the restored model type and fitted target parameters.

The competitive setup follows the exact displacement treatment of
[Sigurskjold, 2000](https://doi.org/10.1006/abio.1999.4402), which derives a
two-ligand mass-balance fit for identical independent sites and requires the
competitor affinity and enthalpy as known inputs. The general binding-polynomial
workflow is summarized by [Brown, 2009](https://pmc.ncbi.nlm.nih.gov/articles/PMC2812830/).
The numerical values chosen here are synthetic and are used solely for
validation.

Known limitations: these references do not validate raw thermogram integration,
buffer subtraction, or experimental uncertainty coverage, and they do not
establish that the two independent classes are identifiable in every real
experiment. Those claims require separate evidence.

## Complete numerical run settings

Preference defaults are restored around every test. Both solvers use a
12,000 iteration/evaluation cap, no uncertainty estimation, uniform 1e-10 J
SD weights and the default optimizer-tolerance preference.

For the conditional two-site case, LM starts at logKa (6.85,5.45), enthalpies
(−22000,9000); NM starts at logKa (7.10,5.20), enthalpies (−25000,11000).
The Ns and zero offset are fixed as stated above. The full two-site truth
is N (0.65,1.55), logKa (7.35,5.15), enthalpy (−25000,13000) and offset 1800;
all seven values are free. The starts and tolerances appear above.

The full competitive truth is N 1.15, logKa 7.0, enthalpy −18000 and offset
1800; all four target coordinates are free. Competitor inputs remain
12 µM total, logKa 6.2 and enthalpy −8000. No syringe activity correction
is enabled in any of these references. The known-N and saturated examples
have distinct evidentiary roles; one should not be substituted for the other.
