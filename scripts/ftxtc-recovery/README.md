# Recover data from an FTXTC project

`recover_ftxtc.py` extracts experiments and saved analysis results when FT-ITC
Analysis and the web viewer are unavailable. It works offline and does not change
the project file. **Keep the original `.ftxtc` file.**

You need **Python 3.9 or newer**. There are no packages to install with `pip`, and
the script does not need FT-ITC, .NET, or any other file from this repository.
Copy or download `recover_ftxtc.py` and keep it somewhere you can find again.

## Run it

Open Terminal on macOS/Linux or PowerShell on Windows. Use paths to the script
and your project; put paths containing spaces in quotes.

**macOS/Linux:**

```sh
python3 "/path/to/recover_ftxtc.py" "/path/to/My project.ftxtc"
```

**Windows:**

```powershell
py -3 "C:\path\to\recover_ftxtc.py" "C:\path\to\My project.ftxtc"
```

If your Windows Python installation provides `python` instead of `py`, use
`python` in place of `py -3`. If neither command is found, install Python 3.9 or
newer first. Python is the only required installation; after that, recovery works
without an internet connection.

You can omit the project path and enter it at a terminal prompt:

```sh
python3 "/path/to/recover_ftxtc.py"
```

Paste the project path and press Enter. Submitting an empty path cancels recovery.
The script runs entirely in the terminal and has no graphical dependencies.

The script prints progress and creates **`My project_recovered` beside your
project**. If that name already exists, it uses `_1`, `_2`, etc. Nothing is
overwritten. To choose a different location, for example when the source folder
is read-only:

```sh
python3 "/path/to/recover_ftxtc.py" "/path/to/My project.ftxtc" --output "/path/to/Recovered data"
```

On Windows, use the same `--output` option with a Windows path. It names the new
output directory itself, not a parent folder. Existing directories are left alone
and a numbered sibling is created. An interrupted or failed run may leave partial
output; rerunning creates a fresh directory.

## Find your recovered data

Start with **`index.csv`** and **`recovery_log.txt`**. The index lists every recovered
experiment, fit, and result, its saved ID, source entry, output folder, relationships,
and recovery status. Numbered folder names distinguish objects with similar names.

Each experiment folder contains:

- **`thermogram.csv`**, when the raw table is readable: time, raw power, cell
  temperature, saved baseline and baseline errors, and baseline-adjusted power
  where reconstruction is possible. An empty table can be normal for an experiment
  imported as integrated heats.
- **`injections.csv`**: all saved injections, including excluded and unintegrated
  injections, with timing, volume, concentration, ratio, integration settings,
  raw/corrected peak areas, and available errors.
- **`metadata.txt`**: all readable saved experiment metadata, including concentrations,
  comments, instrument information, attributes, processing settings, tandem
  segments, and saved buffer-reference information.
- **`baseline.csv`**, when a baseline is readable but cannot be aligned with a raw
  trace: its saved samples and errors, indexed without invented time values.

Fit and result folders contain:

- **`parameters.csv`**: original fitted coordinates, saved reported estimates and
  uncertainties, recorded locks, and converted Kd rows. Sources remain separate.
- **`constraints.csv`**, where recorded: global sharing/temperature relationship
  rules. Ordinary optimizer bounds are not generally stored in a project.
- **`profile_bounds.csv`**, where recorded: saved profile bounds, endpoints, and
  side outcomes. Read the outcomes alongside the endpoints; a failed search is
  not a completed confidence interval.
- **`advanced_parameters.csv`**, where recorded: saved completed advanced-analysis
  estimates, without rerunning or updating their scientific methods.
- **`metadata.txt`**: model/options, uncertainty method, fit status, diagnostics,
  saved validity, constraints, historical fit inputs, and other saved fields.

Embedded Null fits are recovered into their own folders. Incomplete results and
fits whose experiments are unavailable are retained, with unresolved references
identified. The script never substitutes a different experiment or recomputes a
result from the surviving members.

## Units and uncertainties

CSV tables use seconds, µW, µJ, µL, µM, °C, kJ/mol, and kJ/(mol K), with units in
column headings or a separate unit column. Values retain full floating-point
precision. Unknown quantities keep their saved scale and are labelled accordingly.
The detailed text metadata **always preserves original saved values and scales**;
it does not apply the CSV unit conversions.

The CSV files are UTF-8 with a BOM and use commas and decimal points. If your
spreadsheet does not separate the columns automatically, import them as UTF-8,
comma-separated text with a decimal point. Text that could execute as a spreadsheet
formula is prefixed with an apostrophe; the original text survives in metadata.

A **blank numeric cell means unavailable**. A saved zero stays zero. A value marked
`is_missing=true` is not exported as a real zero. `raw_peak_area` means the saved
integrated heat before buffer subtraction; `corrected_peak_area` is the separately
saved corrected heat. Neither is recalculated.

Baseline-adjusted power is reconstructed only when the saved baseline is marked
complete and has the same number of samples as the raw trace. The subtraction
reproduces FT-ITC's Float32 arithmetic. Baseline errors describe the baseline; they
are not new error estimates for adjusted power. The script does not fit a baseline
or reintegrate peaks.

Fitted and reported parameter centers are exported exactly as saved, apart from
unit conversion. If they disagree, both are retained and the discrepancy is logged.
Uncertainties come from saved summaries, not newly calculated bootstrap statistics.
Profile-likelihood SDs may be equivalent scales rather than Gaussian sampling SDs;
the saved method, calibration, and outcomes remain in the metadata.

For an affinity coordinate `x = log10(Ka / (1/M))`, the additional Kd row contains
`10^(-x)` in mol/L, converted to µM. Available interval endpoints transform in
reverse order. The script does **not** infer a symmetric Kd SD from the coordinate's
SD. The coordinate and its original errors remain available in separate rows.

## Recovery limits

- Supported native package versions are **1.2 through 1.6**. `.ftitc` is not supported.
  Explicitly unsupported versions stop with a diagnostic.
- The script validates the entries it reads against the manifest when possible.
  Readable content with a checksum/length mismatch is retained as **unverified**.
  Unreadable entries and structurally invalid binary tables are skipped.
- When the manifest or project root is damaged, recognizable experiment/solution/
  result paths are scanned. Version, integrity, or relationships that cannot be
  verified are explicitly identified. Unknown metadata survives as readable field
  paths; it is not interpreted using guessed meanings or units.
- If experiment metadata is unreadable, independently readable raw and baseline
  tables are still exported. No injection data or completed-baseline state is
  inferred. Any ID recovered from the project root is identified as such.
- A recovery status of `recovered` means the extraction succeeded. It does not mean
  a saved fit was scientifically valid. Saved validity and fit diagnostics are
  retained without recalculation. Review any `unverified/partial` outputs before use.
- **Reports and individual bootstrap/leave-one-out refits are excluded**, including
  their integrity checks. Saved uncertainty summaries are included.
- No new fitted curves, confidence bands, derived thermodynamics, or combined-result
  summaries are calculated. Advanced outputs retain the meaning of their saved
  method/version, including historical methods.
- Broken ZIP-directory reconstruction and truncated-matrix repair are not attempted.
  Limits are 10,000 ZIP entries, 512 MiB expanded per entry, and 2 GiB expanded per
  package. Large binary reads can use temporary disk space. Large JSON metadata
  still needs memory; an exhausted resource can prevent recovery.
- Unsafe, duplicate, or symlink ZIP entries are skipped. Files are written only
  under generated output names, never using embedded archive paths.

The command returns **0** for complete recovery, **1** for partial/unverified recovery,
and **2** for failure. Cancellation before selecting a file returns 0 and writes
nothing. Expected file errors produce a short explanation; for output permission
or disk-space errors, choose a writable directory with enough space using `--output`.

## Tests

From the repository root:

```sh
python3 -m unittest discover -s scripts/ftxtc-recovery -v
```

Or, with only this folder available:

```sh
python3 -m unittest discover -s . -v
```

Tests use Python's standard library and temporary directories. They cover
independently specified numeric examples and damaged-file cases. When repository
fixtures are present, they also recover the existing `.ftxtc` test projects,
compare saved counts and values, and check that the input hashes are unchanged.
Fixture tests are skipped when only the standalone folder is available.

## Maintaining compatibility

Run the full test command above whenever the recovery script or native persistence
format changes, including optional fields added without a package version bump.
`test_compatibility.py` adds two safeguards:

- A repository check compares the actual C# writer version and published manifest
  schema with `SUPPORTED_SCHEMAS` and the frozen fixture catalog. A new writer
  version fails this check until recovery support and a corresponding fixture exist.
- Frozen archives for schemas 1.2–1.6 are recovered against fixed expected table
  values, row counts, metadata, identities, relationships, and exit status. Input
  checksums detect accidental replacement or re-saving of the archives. The tests
  never generate these archives or their expectations at runtime.

For a new schema, inspect the changed storage meanings, add explicit handling and
the version to `SUPPORTED_SCHEMAS`, then add a new frozen archive and independently
reviewed expected output to `fixtures/compatibility.json`. Add focused tests with
independent numerical expectations for any new conversions. Keep the old versions
and their fixtures, and run the entire suite. Do not automatically accept future
versions or infer support from the application's current version.

Do not regenerate old archives or expected outputs just to make a failing test
pass. Investigate the changed recovery first. A demonstrated correction to an old
expectation needs a separately documented explanation and independent expected
values; retain the original input archive. More cases may be added for an existing
schema without replacing its earlier cases.

The frozen corpus is regression evidence, not proof that every possible file is
recoverable. Its provenance and coverage are described in `fixtures/README.md`.
These guards run with the test command; no CI service is configured by this tool.
The single recovery script remains independent of all fixtures and repository files.
