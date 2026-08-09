# Native FTXTC project format 1.0

FTXTC is the native FT-ITC Analysis project format. An `.ftxtc` file is a ZIP package containing a normalized JSON object graph, typed binary matrices, and a checksum manifest. This document is maintainer documentation, not a third-party compatibility promise.

FTXTC 1.0 is native: it contains no `semanticGraph`, `payloadBase64`, or embedded FTITC text. Experimental payload-based packages are intentionally unsupported. Legacy `.ftitc` files are read-only imports; saving an imported document creates a new `.ftxtc` file.

## Package layout

```text
manifest.json
project.json

experiments/000000/experiment.json
experiments/000000/thermogram.ftxb
experiments/000000/baseline.ftxb
experiments/000000/corrected-trace.ftxb

solutions/000000/solution.json
solutions/000000/bootstrap.json
solutions/000000/bootstrap-parameters.ftxb
solutions/000000/bootstrap-parameter-locks.ftxb
solutions/000000/bootstrap-injections.ftxb
solutions/000000/bootstrap-injection-includes.ftxb

results/000000/result.json
```

Ordinal directory names make output deterministic; stable object IDs live inside JSON. `project.json` contains metadata references for experiments, normalized solutions, and results. A solution attached to an experiment and referenced by a result is stored once.

Paths are relative, use `/`, and cannot contain empty, `.` or `..` segments. ZIP timestamps are normalized. Dictionaries, parameters, options, references, and manifest entries are emitted in ordinal order. Determinism is assessed using normalized JSON and entry hashes rather than compressed ZIP bytes.

## Manifest and validation

`manifest.json` contains `format` (`"ftxtc"`), schema major/minor (`1.0`), writer version, root (`"project.json"`), and a sorted declaration for every payload with media type, uncompressed length, and lowercase SHA-256.

Reading first validates safe unique paths, entry count, expanded sizes, compression ratio, declarations, lengths, hashes, root schema, and root references. Domain objects are built as a detached graph and published only after restoration completes.

Root failures are fatal: unreadable ZIP, missing or malformed manifest/project, unsafe or duplicate paths, unsupported package schema, and ambiguous root IDs/references. Recovery mode can omit damaged components: missing thermograms retain integrated injections; an incomplete baseline/corrected pair is discarded; damaged solutions lose the fit; damaged bootstrap data loses confidence bands; and damaged results are skipped. A recovered desktop document is detached, dirty, and must use Save As.

## JSON wire rules

JSON is UTF-8, camel-case, SI-based, and uses invariant round-trip numbers and ISO-8601 dates. Models, parameters, experiment attributes, processors, instruments, algorithms, and enums use explicit stable wire strings. They are never derived from C# enum ordinals, `ToString()`, class names, or namespaces.

`FloatWithError` records contain `isMissing`, `value`, `standardDeviation`, `lower95`, and `upper95`. The explicit flag distinguishes a missing/NaN value from an ordinary numeric estimate.

Experiment metadata stores identity/source fields, concentrations and uncertainties, instrument settings, typed attributes, injections (including integration and actual-concentration state), tandem segments, processor configuration, and an optional attached-solution ID. Numeric raw and processed outputs are authoritative and installed directly; loading never runs interpolation or integration.

Processor state is a versioned tagged union: `none`, `spline`, `polynomial`, `segmented`, or `asl`. Spline points include all handle/lock/display fields. Segmented state contains exact bounds, centers, kinds, injection IDs, and polynomial coefficients.

Solution metadata stores the stable model ID and model schema, clone/model options, weighting/error method, fitted parameters and locks, reported `FloatWithError` estimates, and convergence. Restoration initializes the concrete model normally, directly installs captured options, restores fitted parameters, applies model options, creates the solution, restores bootstrap models, and finally reapplies reported estimates.

Result metadata stores the global-solution ID, ordered member-solution IDs, model, constraints, global parameters, clone options, convergence, and validity snapshot. Global bootstrap sets are reconstructed by joining explicit common replicate indices.

## Bootstrap representation

`bootstrap.json` declares explicit replicate indices, parameter columns, injection columns, sampled experiment values, complete sampled model options, tandem segments, and the four matrix paths. Every replicate must contain every declared column.

- parameter values: Float64, replicate × parameter;
- parameter locks: UInt8, replicate × parameter;
- injections: Float64, replicate × (`4 × injection`), ordered as volume, actual cell concentration, actual titrant concentration, ratio;
- injection inclusion: UInt8, replicate × injection.

Each replicate restores to an independent experiment/model/solution. Evaluated curves and confidence-band arrays are not stored; they are regenerated from the versioned native model and captured bootstrap state.

## FTXB matrices

FTXB is little-endian and row-major:

| Offset | Size | Meaning |
| --- | ---: | --- |
| 0 | 4 | ASCII `FTXB` |
| 4 | 1 | format version (`1`) |
| 5 | 1 | scalar type (`1` Float32, `2` Float64, `3` UInt8) |
| 6 | 1 | layout (`1` row-major) |
| 7 | 1 | reserved (`0`) |
| 8 | 4 | signed row count |
| 12 | 4 | signed column count |
| 16 | remaining | row-major scalar values |

Thermogram and corrected-trace arrays use seven Float32 columns: time, power, temperature, cell/reference temperature difference, shield temperature, ATP, and JFBI. Baselines use four Float64 columns: value, standard deviation, lower 95%, and upper 95%.

## Compatibility

- Writers emit only native schema 1.0.
- Future migrations operate on parsed storage DTOs before domain restoration.
- New/existing FTXTC projects and autosaves save natively.
- FTITC remains importable but is never overwritten; its first Save prompts for FTXTC.
- Save As no longer offers FTITC.
- Desktop, autosave recovery, and the web viewer use recovery mode; format tests use strict mode.
