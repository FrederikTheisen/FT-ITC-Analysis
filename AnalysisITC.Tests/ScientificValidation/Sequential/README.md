# Sequential binding reference

`reference.json` is a deterministic synthetic reference, not experimental
data. The original repository material is under the MIT license.
`generate_reference.py` uses Python Decimal arithmetic at 55-digit precision
and bisects free ligand without calling production code.

State weights are `w0=1`, `wi=(K1...Ki)*Lfree^i`, and fractions are `Fi=wi/sum(w)`.
Mass balance is `Ltotal=Lfree+M*sum(i*Fi)`. Heat content is
`Q=V*M*sum(Fi*sum(H1...Hi))`. The injection heat is
`Qafter−Qbefore+v*(Qafter+Qbefore)/(2V)`, plus the declared zero offset.
The enthalpies are step values; the state heat includes their cumulative sum.
Macroscopic sequential constants are ordered and are not interchangeable site
labels or microscopic affinities. See the [model definition](../../../Documentation/UserManual/pages/06-fitting-models.md).

The cell contains 20 µM macromolecule in 200 µL; the syringe contains 2 mM
ligand. There are 48 injections of 2 µL, all included. MicroCal dilution uses
`u=cumulative_v/V`, `M=M0*(1−u/2)/(1+u/2)`, `L=Cs*u*(1−u/2)`.
Final u is 0.48; the final ligand/macromolecule ratio is **59.52**, not 0.48.
The affinity/enthalpy arrays, truncated for two/three steps, are:

- log10(Ka/M^-1): [6.4, 5.7, 5.0, 4.3].
- Step enthalpies J/mol: [−30000, +20000, −18000, +14000].

Both solvers fit all 2n affinity/enthalpy coordinates. Only offset is locked
at zero. For every case, odd-step logKa starts +0.08 from truth and its
enthalpy starts at 92% of truth; even-step logKa starts −0.08 and its enthalpy
starts at 108% of truth. The initial coordinates are outside the acceptance
window. No reference heat is recalculated by the production model.

Forward heat tolerance is 2e-12 J. Recovery tolerances remain 0.01 absolute
log10 Ka (about 2.3% Ka) and 1% relative enthalpy. Tests restore preference
defaults, select optimizer tolerance 1, allow 20,000 evaluations/iterations,
and perform no uncertainty estimation. Every reference heat is assigned the
same 1e-10 J SD for objective scaling; weighted and unweighted least squares
therefore have the same optimum. Nelder–Mead additionally uses
`SolverToleranceModifier=1e-4`; LM uses modifier 1.

Early unscaled/default-stopping experiments converged to insufficiently
accurate higher-step coordinates. Qualification tightens numerical controls
and preserves the original starts and tolerances. It does not use starts
already inside the acceptance window. The reference establishes noiseless
local recovery under these settings, not global uniqueness, broad-start
robustness, noisy identifiability or experimental validation of four steps.
Those require additional study and must not be claimed from these tests.
