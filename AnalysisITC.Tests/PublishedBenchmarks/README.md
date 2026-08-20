# Published model-reproduction benchmark

`pytc-ca-edta-tris-01.DH` is the `ca-edta/tris-01.DH` integrated-heat
fixture from the public-domain
[`harmslab/pytc-demos`](https://github.com/harmslab/pytc-demos) repository,
pinned at commit `7a40435ee5e24d5958a518104cae8034b2419f08`.

The output embedded in `00_fit-single-site.ipynb` reports the maximum-likelihood
one-site fit (cal/mol):

- competent fraction / stoichiometry: `0.973948`
- association constant: `4.05476e7 M^-1`
- binding enthalpy: `-11566.9 cal/mol`

The notebook constructs the experiment with `shot_start=2`, so injection indices
0 and 1 are excluded. The fit also includes a linear dilution term. FT-ITC
Analysis uses its one-set-of-sites model with a constant heat offset, so the
regression criterion is agreement within 2% for the three scientific parameters
rather than equality with the source optimizer's internal standard errors.

The regression runs the same integrated heats through both FT-ITC dilution
conventions (`MicroCal` and `Exponential`). This intentionally verifies fitting
from an imported integrated-heat fixture, not reconstruction of the raw-signal
processing that produced it. Both conventions reproduce the published N, Ka,
and ΔH within the stated tolerance with the supported optimizers.

The pyITC software article is: Duvvuri H, Wheeler LC, Harms MJ. *pytc: Open-Source
Python Software for Global Analyses of Isothermal Titration Calorimetry Data*.
Biochemistry. 2018;57:2578-2583. DOI:
[`10.1021/acs.biochem.7b01264`](https://doi.org/10.1021/acs.biochem.7b01264).

## Two independent sites benchmark

`feotf54-independent-sites.ndh` is the integrated-heat table from the
`Figure_2/MN_Independent/NDH/Analytical/Data_NDH` file in the downloadable
supporting archive for Krishnamoorthy *et al.* (2020),
[`PMC6926116`](https://pmc.ncbi.nlm.nih.gov/articles/PMC6926116/). It is the
published independent-sites simulation based on MicroCal's FEOTF54
ovotransferrin/ferric-ion example. Its source code supplies the experimental
setup (31.4 µM cell, 1.56 mM syringe, 1.411 mL cell, 17 × 5 µL injections) and
the reference parameters (each site has N = 1; Ka1 = 1.18e10 M^-1, ΔH1 =
767.3 cal/mol; Ka2 = 3.46e7 M^-1, ΔH2 = -12030 cal/mol).

The table is a published forward-model output rather than the original
instrument export. It is therefore used as a deterministic two-sites
forward-model regression. The source labels normalized heat as `Kcal/mol`, but
the values and the accompanying parameters establish that the numeric values
are cal/mol. FT-ITC maps the normalized integrated heats to injection heats and fits them with both
supported optimizers and requires 15% agreement for all six scientific
parameters. The tolerance accommodates the source's constant-volume
calculation versus FT-ITC's injection-volume correction.
