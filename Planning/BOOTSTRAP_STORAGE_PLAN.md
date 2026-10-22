# Bootstrap storage within web viewer byte limits

Revision of the 2026-10-22 Codex plan "Compact bootstrap persistence with exact values", after review. Decisions below were confirmed by the maintainer; items under "Confirm before implementing" were not.

## Problem and target

The driver is the web viewer's JSON byte budgets (`ViewerUploadLimits`): 8 MiB per JSON entry and 32 MiB of JSON per package. Binary budgets cannot bind first: the 500,000 injection-record cap limits a bootstrap injection matrix to about 15 MiB, under the 32 MiB per-entry and 128 MiB cumulative binary limits.

Measured on existing projects (1,000 replicates per fit):

- `JORS Table1.ftxtc` (13 fits) already uses about 25 MiB of JSON, 24 MB of it in `bootstrap.json`.
- Whole-record deduplication does not help competitive fits: sampled competitor Kd/ΔH make every replicate record unique (about 1.6 kB compact per replicate). Concentration variance does the same through the sampled concentrations.
- At the web viewer's 20,000-replicate cap, one competitive fit exceeds the 8 MiB entry limit under every whole-record or per-field JSON scheme.

Target: every byte budget clears at the logical caps (20,000 replicates, 500,000 injection records) for the worst-case fits below, with default web limits unchanged.

Memory is out of scope. Restored replicate objects, not stored bytes, dominate load memory (306 MiB retained for JORS Table1 versus 18 MiB without bootstrap data; compact JSON changes this by under 1%). Tracked separately as ITC-216.

## Decisions

- Two independent steps: compact JSON first in the current format, then bootstrap descriptor schema 2.
- Schema 2 changes only the JSON descriptor. The four FTXB matrices (parameter values, parameter locks, injections, injection inclusion) keep their schema-1 paths, shapes and contents.
- Values that vary per replicate are stored in a binary Float64 matrix; everything else is stored once in shared template records.
- Global refit records in `result.json` get compact JSON only, no format change.
- Older builds are checked once by hand with the last released desktop builds; no copy of the old reader is kept in the test project.

## Step 1: compact JSON in the current format

- Write `bootstrap.json` and `result.json` without indentation. Other JSON entries stay indented.
- Add a compact variant of `FTXTCFormat.JsonBytes`; do not change the shared `JsonOptions`.
- No schema, version or reader change. Existing and older readers parse both forms.
- Update the JSON wire rules in `FTXTC_FORMAT.md` to say which entries are written compact.
- Expected effect on JORS Table1: bootstrap JSON about 24 → 15 MB, result JSON about 1.75 → 1.2 MB.

## Step 2: bootstrap descriptor schema 2

Package schema stays 1.6, project schema 4, FTXB version 1. Only the bootstrap descriptor's `schemaVersion` becomes 2.

### Representation

`bootstrap.json` (compact) keeps `replicateIndices`, `parameterIds`, `injectionIds` and the four matrix paths unchanged, and replaces `replicates` with:

- `templates`: distinct replicate records, in the schema-1 replicate shape including `parameterBoundaryHit`. A slot listed in `sampledColumns` is `null` in every template.
- `templateIndices`: one template index per replicate, in `replicateIndices` order.
- `sampledColumns`: ordered slot identifiers from the closed list below.
- `sampledValues`: path to `solutions/{n}/bootstrap-sampled-values.ftxb`, Float64, replicate × `sampledColumns`, rows in `replicateIndices` order. `null`, with no entry, when `sampledColumns` is empty (FTXB has no zero-column shape).

Slot identifiers (closed list, documented in the format specification):

- `cell-concentration/{value|sd|lower95|upper95}`, `syringe-concentration/{value|sd|lower95|upper95}`
- `cell-volume`, `measured-temperature`
- `option/{key}/double-value`, `option/{key}/parameter-{value|sd|lower95|upper95}`
- `segment/{position}/initial-cell-concentration`, `segment/{position}/initial-titrant-concentration`

Only double-valued slots can be sampled columns. Booleans, integers, strings and `isMissing` stay in templates; if they vary, they produce more templates. Correctness therefore never depends on the list being complete, only size does.

`parameterBoundaryHit` stays in the template. It can at most double the template count and needs no new entry.

### Writer

- Capture snapshots and build schema-1 replicate records with the existing code, then derive the schema-2 descriptor from those records.
- A slot becomes a sampled column exactly when its binary scalar value differs between at least two replicates of the same solution (signed zero and NaN payloads count as different). This is data-driven: never decided from concentration-variance, LOO or other settings.
- Null the sampled slots, then deduplicate records by exact canonical JSON bytes. Assign template indices and sampled columns in first-occurrence order for deterministic output.
- Never derive saved values from the experiment, the primary fit or scale factors.
- Keep helpers private to persistence. No public API, no duplicated schema-1 payload.

### Reader

- Dispatch on `schemaVersion` 1 or 2 in `FTXTCReader.RestoreBootstrap`; keep reading every currently readable package version.
- For schema 2, build one schema-1-shaped replicate record per replicate (template copy plus that row's sampled values) and pass it through the existing per-replicate restoration code, so both schemas share everything after the record.
- Validate before constructing models: `templateIndices` length equals `replicateIndices` length; every index is in range; column identifiers are known and unique; every referenced template has `null` at each sampled slot and a value at every other slot; the sampled matrix shape is replicates × columns; `sampledValues` is `null` exactly when there are no columns.
- Unreferenced or duplicate templates are harmless and need not be rejected.
- A malformed schema-2 descriptor keeps the existing behaviour: strict reads fail, recovery reads omit the bootstrap with the existing warning codes.
- Budgets and limits are unchanged. The new FTXB entry is charged once like other entries; replicate and injection-record budgets still count logical restoration work.

### Documentation

- `FTXTC_FORMAT.md`: bootstrap representation for both schemas, the slot list, and the older-build behaviour recorded in the release check below.
- `component.schema.json`: accept bootstrap schema 1 and schema 2.
- No manual, in-app help or UI changes.

## Older builds

Older readers reject bootstrap schema 2 and, through the default recovery open, omit the bootstrap with a warning. The document opens detached and needs Save As.

- Single fits keep reported estimates, ± and confidence intervals, which are stored in `solution.json`. Confidence bands, bootstrap distributions and correlations are lost.
- Global results recalculate temperature-dependence and shared-parameter uncertainty from member bootstrap replicates on load (`GlobalModel.cs`, `RestoreGlobalBootstrapSolutions` and the constructor fallback). Without replicates, older builds show a value computed without bootstrap: no error, or the spread between member experiments. This is accepted, but must be recorded from the release check, not assumed.
- Older builds describe the omitted bootstrap as damaged ("invalid schema or shape"). This cannot be changed retroactively.
- Saving through an older build permanently drops the omitted bootstrap. Accepted.

## Verification and acceptance

Focused tests (Core):

- **Exact equivalence:** for the same captured solution, schema-2 decoded replicate records equal schema-1 records, comparing doubles by bit pattern. Cover signed zero, NaN, ±∞, adjacent doubles in one slot, and missing `FloatWithError` values.
- **Slot selection:** a constant slot stays in the template; a slot differing by one ULP becomes a column; no variation writes no sampled entry.
- **Variation coverage:** competitive sampled options; concentration variance on and off; tandem segments with sampled segment concentrations; sequential site count; local and global LOO; parameter unlocking; mixed boundary hits; each bookkeeping method.
- **Reader validation:** unknown or duplicate column; slot missing or non-null in a template; value missing at a non-sampled slot; index out of range; length or matrix-shape mismatch; sampled path present with no columns or absent with columns. Strict fails; recovery omits.
- **Scientific preservation:** confidence bands, correlations, reported values and global results are identical after a schema-1 → schema-2 save–read–save cycle. Restored replicates stay independent when one is mutated.
- **Compatibility:** existing schema-1 fixtures still read; saving writes schema 2 and reopens strictly with no recovery issues.

Byte-budget acceptance (the target):

- Synthetic worst case 1: one competitive fit with concentration variance, 20,000 replicates and 24 injections (480,000 bootstrap injection records; the experiment's own injections also count toward the 500,000 cap). Size tests may construct replicate solutions directly; this is not a forward-model test.
- Synthetic worst case 2: a global temperature-dependent result of 4 members × 5,000 replicates with 24 injections each, to cover `result.json` global refits.
- Both open through the web viewer upload path with default limits, every JSON entry below 8 MiB and total JSON below 32 MiB, with the measured margin reported.
- Report before/after expanded and compressed sizes by entry kind for `JORS Table1.ftxtc`, `JORS Example Project.ftxtc`, `Me6PXDA_C1_CHDA_CompetitiveDisplacement_updated.ftxtc` and `SeArgT_Pu1210_TwoSetsOfSites_new.ftxtc`. Report load time and memory without CI assertions; no change is expected.

Release check (one-off, recorded in this plan):

- Open schema-2 files (single fit, competitive fit, global temperature-dependent result, tandem fit) with the last released macOS and Avalonia builds.
- Record warnings, retained fits and reported values, and what global results display for uncertainty.
- Also confirm the step-1 compact files open in those builds with no warnings.

Suites: full Core suite, schema validation, Web upload/viewer tests, and builds of both desktop applications.

## Out of scope

- Restored replicate memory and the logical budgets (ITC-216).
- FTXB matrix layout, including injection-layout deduplication.
- Format changes to `result.json` global refits.
- Web limit values.

## Confirm before implementing

1. No fingerprint, hash or test depends on the indented bytes of `bootstrap.json` or `result.json`. Interpretation fingerprints are expected to use canonical packages, not saved entries.
2. The slot list covers every double that varies between replicates of one fit across all models. A missed slot only costs size, but it should be named if found.
3. How a `null` sampled slot is represented in typed templates without making the shared `FtxtcFloatWithError` fields nullable.
4. Whether any reader outside `FTXTCReader` (web, viewer, scripts) reads `bootstrap.json` directly. None was found in review.
