# itcsimlib-assisted numerical regression fixtures

The `.DH` files in this directory were generated with the external
`itcsimlib` 0.6.1 source at commit
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

Those DH files preserve the output of that earlier rational-titrant protocol.
They provide the instrument and model inputs for the Core tests; they are not
the expected heats for the current MicroCal implementation.

`current-implementation-predictions.json` contains the expected integrated heat
for every injection in each case, captured from the current FT-ITC
implementation. The Core regression test compares its predictions against
these full vectors. These values are implementation-derived regression
expectations, not independent external forward-model validation. Update the
sidecar only when intentionally changing current implementation behavior, and
record the new values from that implementation.

The suite covers every FT-ITC analysis model. `itcsimlib` supplies the
one-site and independent-site equilibrium calculations. Its public model set
does not directly express the FT-ITC competitive, macroscopic sequential, or
dimerization models, so those cases use standalone physical mass balances in
the adapter. They remain independent implementations and do not call FT-ITC.

The original locally adapted DH fixtures are numerical regression inputs, not
independent external forward-model validation. The external package does not
supply the complete concentration/heat protocol or every binding model.

The associated Core test checks current predicted integrated heats at the
declared parameters for all five model families against the sidecar. Sequential
binding is covered at each supported site count (2, 3, and 4). It is not a
parameter-recovery or model-selection study.

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
