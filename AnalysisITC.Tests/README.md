# Test and integration data

`AnalysisITC.Tests` is the canonical source location for test data. Test
projects link or copy files from here into their build output under `Fixtures/`;
the copied files in `bin/.../Fixtures/` are derived and must not be edited.

## Changing a data file

Treat every project, raw-data and reference file in this directory as controlled
test input. It may be changed only deliberately, together with the test(s) that
describe its expected contents and behavior. Do not overwrite a fixture merely
to make a test pass.

- `PublishedBenchmarks/` and `ScientificValidation/` contain external or
  independently generated reference data. Preserve their provenance and update
  their local README when replacing a source.
- Folders such as `OneSetOfSites/`, `TemperatureSeries/`, and `Tandem/` contain
  FT-ITC sample projects used by integration and viewer tests. They may evolve
  with the intended saved-project behavior, but tests must be updated in the
  same change.
- Binary project formats (`.ftxtc`) and raw instrument files should be changed
  through their producing application or a documented fixture-generation step,
  not by manual binary editing.

When adding a new integration fixture, place its source here, name the consuming
test(s) in the relevant test project, and add a short provenance note if the
data is external or scientifically validated.
