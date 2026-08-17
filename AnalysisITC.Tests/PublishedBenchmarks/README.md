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
Analysis uses its one-set-of-sites model with a constant heat offset and the
MicroCal dilution convention; therefore the regression criterion is agreement
within 2% for the three scientific parameters rather than equality with the
source optimizer's internal standard errors.

The pyITC software article is: Duvvuri H, Wheeler LC, Harms MJ. *pytc: Open-Source
Python Software for Global Analyses of Isothermal Titration Calorimetry Data*.
Biochemistry. 2018;57:2578-2583. DOI:
[`10.1021/acs.biochem.7b01264`](https://doi.org/10.1021/acs.biochem.7b01264).
