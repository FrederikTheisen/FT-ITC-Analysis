# Native FTXTC project format 1.6

FTXTC is the native FT-ITC Analysis project format. An `.ftxtc` file is a ZIP package containing a normalized JSON object graph, typed binary matrices, and a checksum manifest. This document is maintainer documentation, not a third-party compatibility promise.

Legacy `.ftitc` files are read-only imports; saving an imported document creates a new `.ftxtc` file.

## Package layout

```text
manifest.json
project.json

experiments/000000/experiment.json
experiments/000000/thermogram.ftxb
experiments/000000/baseline.ftxb

solutions/000000/solution.json
solutions/000000/bootstrap.json
solutions/000000/bootstrap-parameters.ftxb
solutions/000000/bootstrap-parameter-locks.ftxb
solutions/000000/bootstrap-injections.ftxb
solutions/000000/bootstrap-injection-includes.ftxb

results/000000/result.json

reports/000000/report.json
```

Ordinal directory names make output deterministic; stable object IDs live inside JSON. `project.json` contains metadata references for experiments, normalized solutions, and results. A solution attached to an experiment and referenced by a result is stored once.

Paths are relative, use `/`, and cannot contain empty, `.` or `..` segments. ZIP timestamps are normalized. Dictionaries, parameters, options, references, and manifest entries are emitted in ordinal order. Determinism is assessed using normalized JSON and entry hashes rather than compressed ZIP bytes.

## Manifest and validation

`manifest.json` contains `format` (`"ftxtc"`), schema major/minor (`1.6`), writer version, root (`"project.json"`), and a sorted declaration for every payload with media type, uncompressed length, and lowercase SHA-256.

Reading first validates safe unique paths, entry count, expanded sizes, compression ratio, declarations, lengths, hashes, root schema, and root references. Domain objects are built as a detached graph and published only after restoration completes.

Root failures are fatal: unreadable ZIP, missing or malformed manifest/project, unsafe or duplicate paths, unsupported package schema, and empty or duplicate root IDs. Strict reads also reject a solution whose experiment is not declared by `project.json`. Recovery mode treats that missing component reference as recoverable: the orphaned solution and its bootstrap payloads are omitted, and every result containing an unavailable member solution is omitted as an atomic unit. Recovery can also omit other damaged components: missing thermograms retain integrated injections; an unavailable or shape-invalid baseline clears reconstructed processed output; missing buffer-reference experiments retain raw heats; damaged solutions lose the fit; damaged bootstrap data loses confidence bands; and damaged results are skipped. A recovered desktop document is detached, dirty, and must use Save As.

## JSON wire rules

JSON is UTF-8, camel-case, SI-based, and uses invariant round-trip numbers and ISO-8601 dates. Models, parameters, experiment attributes, processors, instruments, algorithms, and enums use explicit stable wire strings.

`FloatWithError` records contain `isMissing`, `value`, `standardDeviation`, `lower95`, and `upper95`. Profile-likelihood endpoints use the existing lower/upper fields; their symmetric display SD is an equivalent scale, not a Gaussian sample SD.

Schema 1.5 adds the required `contentOrder` array to `project.json`. Each entry contains a stable `type` (`experiment` or `result`) and root object `id`; every experiment and result must occur exactly once. This preserves the canonical mixed Data / Results list order independently of the normalized experiment and result payload collections. Recovery mode falls back to the historical experiment-then-result order when this non-scientific ordering metadata is malformed. Schemas 1.0–1.4 are migrated by synthesizing that historical order.

Schema 1.6 adds the root `reports` collection. Each report is stored as
`reports/{ordinal}/report.json` and contains its ordered result references,
structured study context, interpretation settings, and at most one approved
structured interpretation with provenance. Reports are deliberately outside
`contentOrder`. A dangling result reference is retained so report context and
approved text survive; the report remains unresolved until that result is
available. Schemas 1.0–1.5 migrate with an empty reports collection.

Experiment metadata stores identity/source fields, concentrations and uncertainties, instrument settings, typed attributes, injections (including integration and actual-concentration state), tandem segments, processor configuration, and an optional attached-solution ID. The raw thermogram, saved baseline, and raw injection heats are authoritative. Corrected thermogram points are reconstructed as raw power minus baseline. Corrected injection peak areas are persisted as a fallback for selected-project exports or unavailable buffer references; when references are available, current buffer-subtracted peak areas are recalculated after all experiment references are restored. Loading never reruns interpolation or peak integration.

Processor state is a versioned tagged union: `none`, `spline`, `polynomial`, or `segmented`. Spline points include all handle/lock/display fields. Segmented state contains exact bounds, centers, kinds, injection IDs, and polynomial coefficients.

Solution metadata stores the stable model ID and model schema, validity, a `parameterBoundaryHit` boolean, clone/model options, weighting/error method, fitted parameters and locks, reported `FloatWithError` estimates, and convergence. Restoration initializes the concrete model normally, directly installs captured options, restores fitted parameters, applies model options, creates the solution, directly restores the validated bootstrap set without applying current preference limits, and finally reapplies reported estimates, boundary state, and validity.

Convergence metadata may include `molarRmsdJoulesPerMole`, the optional display-only unweighted RMSD of per-injection molar heat residuals in SI J/mol. Its absence means the metric was not captured and must not be reconstructed from potentially changed experiment data. Convergence metadata may also include the optional structured residual-bootstrap counts `errorEstimationAttemptedRefits`, `errorEstimationSucceededRefits`, and `errorEstimationFailedRefits`. Their absence in older packages means the counts are unknown; readers may recover them only from the writer's recognized keyed convergence summary and otherwise must not infer a failure rate. These optional fields do not change the package schema version.

`sequential-binding-sites` is a genuine sequential solution only with model
schema version `2`. It must contain the explicit integer model option
`sequential-site-count` with value 2–4, exactly one fitted
`affinity-log10-i`/`enthalpy-i` pair for every active step, one `offset`, and no
stoichiometry parameter. Reported parameters contain the corresponding Kd,
enthalpy, Gibbs, and entropy-contribution values for every active step. Model
schema version `1` retains the historical dormant/fallback meaning and is not
silently interpreted as a sequential solution. A missing count or malformed
shape is rejected in strict mode; recovery mode omits the affected solution or
bootstrap component and reports the reason. The package schema is 1.6.

The appended stable parameter IDs are `affinity-log10-3`,
`affinity-log10-4`, `enthalpy-3`, `enthalpy-4`, `gibbs-3`, `gibbs-4`,
`heat-capacity-3`, `heat-capacity-4`, `entropy-3`, `entropy-4`,
`entropy-contribution-3`, and `entropy-contribution-4`. These strings and
`sequential-site-count` are storage API and are not derived from enum names or
ordinals.

Result metadata stores the global-solution ID, global validity, ordered member-solution IDs, model, constraints, global parameters, clone options, convergence, and a historical fit-input validity snapshot. The snapshot retains fit-time corrected heats so stale results can still be diagnosed. Global bootstrap sets are reconstructed by joining explicit common replicate indices.

Schema 1.2 optionally adds `advancedAnalyses` to result metadata. Completed Spolar Record, electrostatics, and protonation analyses are stored as independently versioned JSON objects using stable mode/method IDs and SI-valued `FloatWithError` estimates. Reconstructable input points and discarded Monte Carlo samples are not duplicated. A missing subtype means that analysis has not completed. Desktop and viewer readers restore saved outputs without rerunning calculations; recovery mode may discard one invalid advanced subtype while retaining the parent result. Schema 1.3 adds the per-solution boundary boolean; readers restore it as `false` when opening older packages. Schema 1.4 adds profile-likelihood run diagnostics and the `profile-likelihood` method ID.

The optional `profile` object records confidence level, calibration (`unweighted-f-calibrated-rss` or `weighted-chi-squared`), `n`, `p`, `q`, `df`, baseline objective, target increment, solver algorithm, weighting, tolerance modifier, the `optimizerToleranceSetting` snapshot, candidate iteration cap, expansion/refinement limits, attempted solver calls, elapsed time, and overall outcome (`none`, `not-run`, `completed`, `partial-failure`, `complete-failure`, or `cancelled`). Each coordinate records its stable parameter ID, scope (`local` or `shared`), local experiment identity when applicable, primary optimizer index, best value, effective lower/upper bounds, and shape warnings. Its lower and upper side records use stable outcomes (`endpoint-found`, `bound-reached-before-crossing`, `search-exhausted`, `optimizer-failure`, `non-finite-candidate`, `cancelled`, or `primary-minimum-improved`), endpoint/crossing values, evaluation counts, solver-call counts, and side warnings. Missing `profile` metadata in schemas 1.0–1.3 means no profile run is restored; reported endpoint values remain in the ordinary `FloatWithError` lower/upper fields.

## Bootstrap representation

`bootstrap.json` declares explicit replicate indices, parameter columns, injection columns, sampled experiment values, a `parameterBoundaryHit` boolean for each replicate, complete sampled model options, tandem segments, and the four matrix paths. Every replicate must contain every declared column.

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

Thermogram arrays use three Float32 columns: time, power, and temperature. Baselines use four Float64 columns: value, standard deviation, lower 95%, and upper 95%.

## Compatibility

Writers emit schema 1.6. Readers also accept native 1.0 through 1.5 packages. Schema 1.0 thermogram and corrected-trace matrices contain seven Float32 columns ordered as time, power, temperature, cell/reference temperature difference, shield temperature, ATP, and JFBI; the reader retains only the first three values, translates legacy enum ordinals, ignores redundant corrected traces, and reconstructs normalized state from raw values and baselines, using a persisted corrected peak area when a buffer reference is unavailable. Schema 1.1 normally uses three-column thermograms, but the reader also accepts the seven-column variant emitted by early 1.1 writers and retains its first three values. Schema 1.1 projects load with no persisted advanced-analysis state. Schema 1.2 packages have no persisted parameter-boundary warning state and therefore restore those flags as `false`. Schemas 1.0–1.4 have no persisted mixed content ordering and load experiments before results. Schema 1.5 has no reports collection. Other schema versions remain unsupported until an explicit migration becomes necessary.
