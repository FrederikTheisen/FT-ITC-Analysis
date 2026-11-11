# Split tandem fit plots at back-mixed joins

Implementation deferred. See [ITC-233](ISSUES.md#itc-233---tandem-fit-lines-and-confidence-bands-bridge-reloads).

## Display behavior

Draw a separate fit path on each side of a reload that changes the cell’s starting concentrations. Simple tandems and zero-mixing joins stay continuous. Use the same line style throughout, with no reload markers or extra legend entries.

Draw the confidence ribbons as **one lightly shaded shape**. Use the existing per-injection limits within each branch, round the join-facing corners slightly inward, and fill the combined shape once. Overlap keeps the same tint as the rest of the band, and empty space between the ribbons stays clear.

## Implementation

- Share the join rule in Core. At each recorded `FirstInjectionID`, compare the new segment’s starting cell and titrant concentrations with the preceding injection’s final concentrations. Break when either finite pair differs by more than floating-point tolerance. Assign branches before filtering or sorting so an excluded first injection or overlapping x range cannot hide the join.
- Draw the primary fit with the selected interpolation applied separately to each branch. For split ribbons, use straight edges between paired finite limits and round only the join-facing corners, by up to two drawing points. Use consistently wound paths and a single fill in each renderer. A branch needs two distinct usable x positions to contribute shading.
- Apply the shared rule to macOS and Avalonia Analysis, Results, Final Figure, Supporting Figure, and report previews and exports. Keep each app’s renderer. Use the saved tandem metadata and existing fit and uncertainty values. Update ITC-233, the report map, and the relevant help text. Track the web viewer separately.

## Verification

- Use the saved 16% / 21% back-mixed tandem from ITC-233. Verify three fit branches, no connectors across reloads, and uniform shading where ribbons overlap.
- Cover equal x across branches, a back-mixed join with disjoint x ranges, an excluded first injection, and a branch with too few valid limits. Verify continuous output for simple tandems, zero-mixing joins, and ordinary fits.
- Run Core and both desktop test suites. Inspect the join at normal figure size in both desktop previews and PDF exports for square corners, dark overlaps, and visible seams.
