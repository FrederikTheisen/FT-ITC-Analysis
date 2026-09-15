# itcsimlib-assisted numerical regression

This suite uses the external `itcsimlib` 0.6.1 source at commit
`654d89372f86d833f2b5fb1b3bd0d73780a80027` for one-site and independent-site
equilibrium calculations. The adapter in `generate_reference.py` deliberately does **not**
use itcsimlib's `ITCExperimentBase` concentration bookkeeping, because that
bookkeeping does not match FT-ITC's post-injection MicroCal convention.

Instead, the adapter supplies the external `OneMode` model with states produced
by the declared MicroCal protocol:

- cumulative volume `u = V_injected / V_cell`;
- retained macromolecule `M = M0 * (1-u/2)/(1+u/2)`;
- titrant `L = Ls * u / (1+u/2)`;
- injection heat from the mean displaced pre-/post-injection cell heat.

The suite covers every FT-ITC analysis model. `itcsimlib` supplies the
one-site and independent-site equilibrium calculations. Its public model set
does not directly express the FT-ITC competitive, macroscopic sequential, or
dimerization models, so those cases use standalone physical mass balances in
the adapter. They remain independent implementations and do not call FT-ITC.

These locally adapted fixtures are numerical regression checks, not independent
external forward-model validation. The external package does not supply the
complete concentration/heat protocol or every binding model.

The associated Core test checks predicted integrated heats at the
declared parameters for all five model families. Sequential binding is covered
at each supported site count (2, 3, and 4). It is not a parameter-recovery or
model-selection study.

`itcsimlib` identifies itself as GPLv2.  Its source is not copied into this
repository and this adapter is validation-only; it must not become a bundled
or runtime FT-ITC dependency without a separate licence review.

Run it with a clean checkout of the pinned external source:

```sh
python3 generate_reference.py --itcsimlib-source /path/to/itcsimlib
```

The generator verifies the external commit and source-file hashes, and writes a
`DH` integrated-heat fixture plus a manifest that records the exact external
source and protocol boundary.
