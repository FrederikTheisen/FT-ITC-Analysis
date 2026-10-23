# ITC-216 investigation — 2026-10-23

Recommendation: leave runtime bootstrap storage and restoration unchanged for this issue. The bootstrap graph is expensive, but its calibrated retained cost is materially below the previous reported figure; the viewer releases that graph. The small initialization prototype saves allocation traffic, with no retained-memory improvement and only a small peak-memory benefit. A representation change would buy more memory at a substantially wider scientific/ownership boundary. These results do not establish that any particular server has sufficient headroom.

Measurements, harnesses and prototypes were produced under `/private/tmp/itc216-20261023`. The investigation left the checkout unchanged; all 227 copied Core files matched the measured source snapshot at final verification. This report and its summaries in [ITC-216](../ISSUES.md#itc-216---restored-bootstrap-replicates-dominate-project-load-memory) and the [storage plan](../BOOTSTRAP_STORAGE_PLAN.md) were subsequently saved at the maintainer’s request. No production code, versions, commits, packages or release artifacts were changed.

All artifact paths below, unless absolute or explicitly identified as repository paths, are relative to `/private/tmp/itc216-20261023`. Raw measurements, frozen binaries and disposable prototypes remain there; they have not been copied into the repository. The source/build state below identifies the measured snapshot, not subsequent checkout changes.

## Source, fixture and method

- Measured checkout: `master`, HEAD `5f6af81f00393e595c5e71631ffe6b830438d3e3`. The unrelated dirty files are recorded in `initial-status.txt` and `initial-diff.patch`. Core itself was clean. Source was copied before profiling, excluding bin/obj.
- Host: macOS 15.7, arm64; .NET SDK 10.0.302, runtime 10.0.10; workstation GC. Both Debug and Release were built from the copied source. `environment.json`, `dotnet-info.txt`, `source-manifest.json`, and `binary-sha256.json` identify the inputs and frozen binaries.
- Fixture: repository `AnalysisITC.Tests/JORS Table1.ftxtc`, SHA-256 `adba84c1bd1bb598c75ce537d96863e494c0587d4b607a635802e4c619d6ea16`; 1,682,315 compressed bytes, 38,499,498 expanded bytes. Thirteen primary fits, 13,000 local replicates, 305,000 bootstrap injection records, eight results, 8,000 global replicate wrappers and two reports. All open without recovery issues.
- Five fresh processes per case/configuration, reversing case order on alternate rounds. File-system caches were not flushed. No timing includes build time. The previous scratch directory recorded in ITC-216 is absent, so this is a fresh reproduction, not a rerun of that unavailable harness.
- Core: `FTXTCReader.ReadWithRecovery`, recovery mode, non-interactive, no caller limits. Viewer: full `ViewerDocumentReader.ReadAsync`, with every default `ViewerUploadOptions` read limit copied verbatim into the harness. It includes buffering, Core restoration, all DTO construction and the viewer-array budget check. It excludes HTTP multipart handling, response serialization and network transfer.
- Allocated = `GC.GetTotalAllocatedBytes(true)` delta around the call, across all threads; cumulative churn, not live memory. Time = wall time around that call, excluding the forced collections. Live heap = `GC.GetGCMemoryInfo().HeapSizeBytes - FragmentedBytes` after two blocking, compacting full collections with finalizers drained and the result held by a static root. This includes process caches, not just the document. Peak RSS = macOS `/usr/bin/time -l` maximum resident set for the complete fresh process; it includes managed/native memory and JIT/runtime overhead. `Process.PeakWorkingSet64` returns zero here and is not used.

## Important measurement qualification

`GC.GetTotalMemory`, including its `true` overload, substantially overreports on this host/runtime. A standalone calibration allocates 100,000 byte[1024] arrays plus their reference array: expected array storage 105,600,024 bytes, allocation counter 105,600,328, post-GC live heap 105,652,464, but `GetTotalMemory(true)` 210,502,024. See `Calibration/Program.cs` and `calibration.txt`. This reproduces the discrepancy without FT-ITC. The cause inside the runtime was not investigated.

The Core runs report approximately 290 MiB through that API, but approximately 150 MiB in the calibrated live heap. EventPipe GC dumps independently total 149.5 MiB, with no sampling (count/size multipliers 1). Therefore the earlier 306 MiB observation must not be treated as verified object retention. Its exact cause cannot be established without its original harness/runtime. The previous approximately 959 MiB allocation observation does reproduce.

## Measurements

MiB = 1,048,576 bytes. Medians of five runs; ranges are given below where needed to judge the prototype.

| Configuration and case | Allocated MiB | Post-GC live MiB | Peak RSS MiB | Seconds |
|---|---:|---:|---:|---:|
| Debug Core, original | 963.46 | 149.67 | 289.30 | 5.292 |
| Debug Core, bootstrap removed | 135.37 | 14.86 | 102.28 | 1.063 |
| Debug Core, compact JSON | 953.98 | 149.69 | 298.16 | 5.323 |
| Debug complete viewer, original | 990.08 | 15.82 | 302.09 | 5.530 |
| Debug complete viewer, bootstrap removed | 149.66 | 15.69 | 113.33 | 1.230 |
| Release Core, original | 952.36 | 149.67 | 310.14 | 5.078 |
| Release Core, bootstrap removed | 133.28 | 14.86 | 104.00 | 0.966 |
| Release Core, compact JSON | 942.51 | 149.68 | 305.16 | 4.952 |
| Release complete viewer, original | 979.11 | 18.32 | 311.13 | 5.203 |
| Release complete viewer, bootstrap removed | 147.55 | 16.96 | 114.34 | 1.104 |

The bootstrap-removed package is solely an attribution control, not a proposed optimization. Its references/global replicate metadata and manifest were rebuilt consistently. The compact control rewrites bootstrap/result JSON and rebuilds manifest checksums; binary matrices are unchanged. Neither control produced recovery warnings. The retained-heap difference between full/empty Core loads is 134.8 MiB, approximately 10.6 KiB per local replicate including the associated global graphs; cumulative allocation difference is approximately 64.5 KiB per replicate. Compact JSON does not address retention.

## Retained attribution and ownership

GC dumps were taken separately from performance runs. `Heap/Program.cs` sums actual graph node sizes; it does not multiply the histogram's representative array size by count. It also cuts a named object type from the root graph and measures objects that become unreachable. Cuts are ownership evidence, not allocation samples. Cuts overlap when one type owns another and must not be added together indiscriminately.

| Retained bootstrap contribution, full minus no-bootstrap dump | MiB | Evidence |
|---|---:|---|
| Injection objects | 53.52 | 305,000 additional `InjectionData`, 184 bytes each |
| Their interval arrays | 11.63 | 305,000 additional double[2], 40 bytes each; RawPeakArea and PeakArea share that replicate's zero interval |
| Injection list backing arrays | 4.08 | 13,000 extra arrays; capacity exceeds logical count |
| Experiment infrastructure beyond injections/list backing | about 10.51 | ExperimentData, concentration intervals, empty collections, processor and synchronization objects; experiment-subgraph cut totals 79.75 MiB including injections |
| Model options and exclusively owned children | 10.37 | 38,000 ExperimentAttribute objects plus strings/interval arrays |
| Parameter objects and bounds | 5.29 | 63,000 Parameter objects and their bounds arrays |
| Solution parameter dictionaries: entries, dictionary objects, numeric intervals | about 8.34 | Values duplicated intentionally into saved solution state, independently of model parameter tables |
| Global temperature-dependence fits and their numeric children | 7.74 | 36,000 LinearFitWithError objects with FloatWithError[] and interval arrays |
| Model, global and solution wrappers/tables/other collections | remaining graph | 13,000 models/solutions, 8,000 global model/solution pairs, parameter collections, clone options, identities, etc. |

Across all owners, the additional double arrays total 21.42 MiB. `FloatWithError` is a struct; its intervals are separate arrays, so treating all numeric/error memory as one object class is misleading. DataProcessor and its exclusively owned infrastructure account for only 3.07 MiB of the bootstrap delta. Model/solution shells themselves are comparatively small; global temperature fits, dictionaries and their arrays matter more than the GlobalSolution shell (0.67 MiB).

The document-root cut gives 136.47 MiB for the loaded full Core document, versus 4.07 MiB without bootstrap: 132.40 MiB document-owned difference. The remaining difference in whole-process live heaps comes from reusable pools, metadata and other runtime state. Typical ownership is result/experiment → primary model/solution → BootstrapSolutions → replicate solution → model → experiment → injections/processor; global replicate wrappers refer to the same member models rather than copying injections a second time.

The complete viewer dump contains **zero concrete ExperimentData, InjectionData, ModelSolution, GlobalSolution or model instances**. Some empty arrays and type-related delegates with those names remain; they do not retain Core instances. The ViewerDocument root owns only 2.15 MiB. Independent instrumentation kept weak references to all 13,000 replicate solutions, all 13 primary solutions and eight global roots: all 13,021 were dead after viewer construction and collection. Thus the 16–18 MiB post-GC viewer process heap is mostly reusable runtime/serializer/pool state, not a persistent bootstrap graph.

## Transient work and peak memory

One instrumented Release run (diagnostic, not a benchmark sample) attributed approximately 566 MiB allocations to solution restoration and 148 MiB to result restoration. Within the 13,000 snapshot restores: experiment/injections 109 MiB; normal factory initialization 181 MiB; saved-option installation/ApplyModelOptions 44.6 MiB; saved parameters/solution creation 20.8 MiB. These are cumulative allocations and do not describe retained ownership.

Restoring reports is small, but checking the two saved interpretations' freshness allocates another approximately 206 MiB and takes about 1.85 s. This calls AnalysisInterpretationPackageBuilder, which computes bands and correlations; all 13 primary bootstrap-band caches already exist by the end of Core loading. Consequently final viewer DTO building adds only about 16.3 MiB allocations and 0.080 s in this fixture. Do not generalize that incremental viewer cost to documents without those report-driven calculations.

Sampled working set grows from approximately 244 MiB after solutions, to 253 MiB after results, 301 MiB after report freshness, and 305 MiB after viewer DTO construction. These are phase-end snapshots, not separate phase maxima; the uninstrumented peak RSS results above are the peak measurements. The reader spools validated expanded entries to temporary disk and materializes entries on demand, so the entire expanded ZIP is not retained as one in-memory archive. Descriptors, decoded matrices, setup temporaries, global summaries and report/DTO generation still overlap the live graph, while GC segment capacity and native/JIT state also contribute to RSS. Releasing the graph does not imply immediate return of all committed/resident process memory to the OS.

## What consumers require

- Confidence bands evaluate every saved replicate with its own model/data and the existing equations. Model.EvaluateBootstrap scopes each replicate's whole curve and stores aggregate bands on the primary. Included flags, predecessor concentration states, segment starts, temperature and sampled options must survive. The primary prediction remains the reported central estimate.
- Correlations and distributions require fitted coordinates and lock/global constraints; global views need shared coordinates, selected-member coordinates and complete matched replicate rows. Derived Kd, ΔG, −TΔS and related quantities use each replicate's saved parameters and temperature, including thermodynamic linkage. Merely retaining primary parameters plus deltas is not sufficient.
- Global restoration uses explicit replicate index and member identities, including LOO subsets; the legacy path intersects indices rather than shifting remaining list positions. Global temperature fits and shared-coordinate uncertainty require the matched ensemble.
- Saving calls BootstrapModelSnapshot.Capture on every solution and writes sampled options, concentrations, injection states/inclusion, segment boundaries, locks and indices. Global metadata captures matching memberships/coordinates. It must work without refitting or reading current preferences.
- Mutation remains observable: InjectionData contains an owning Experiment reference, and InjectionMass reads that owner's sampled syringe concentration. Parameters, options, inclusion and solution validity are mutable; import remaps identities through the graph. Parameter.Copy and FloatWithError copies can share internal arrays, so these existing types are not safe immutable containers.
- Cache identity currently includes solution/list identity, replicate count, heat method and processing revision. It does not fingerprint arbitrary in-place replicate edits. Rebinding one evaluator across saved records would need explicit invalidation of both per-curve and model evaluation caches. Sequential/two-site models store temporary objective workspaces and an owner thread ID; that is not a guarantee of safety while another thread mutates/rebinds the model. Any shared evaluator must be scoped to one operation/worker with complete state replacement and no escaping references. No such change was prototyped.

## Alternatives

| Option | Benefit | Cost/constraints | Assessment |
|---|---|---|---|
| Presize injection lists / remove a redundant Parameter.Copy | Estimated approximately 1.46 MiB retained list-capacity saving; one redundant copy accounts for about 2.9 MiB transient allocation | Low CPU/correctness risk; no equations or ownership change | Too small to resolve ITC-216; no prototype needed |
| Avoid normal fit-setup initialization for complete restored snapshots | Measured below | Must preserve defaults for old/incomplete records, current factory-option behavior, sequential shape and two-site ApplyModelOptions ordering | Useful allocation reduction, weak memory-capacity improvement |
| Avoid eager processors on restored replicate experiments | Maximum observed removable subtree about 3.07 MiB | Needs a special/lazy initialization contract for ExperimentData; later mutation/processing paths must remain valid | Small benefit for a broader object invariant change |
| Compact immutable injection records | Estimated roughly 55–58 MiB saving for this fixture if a 40-byte record replaces 184-byte objects, zero-heat interval arrays and list references | Changes model input access/ownership, mutation, snapshot saving and possibly fallback initialization; still needs separate mutable per-replicate options/parameters | Material potential, not measured; not a small restoration fix |
| Share exact immutable layouts | Current fixture has only 13 distinct injection+inclusion rows, verified byte-for-byte | Existing InjectionData cannot be shared because of its owner and mutation. Concentration variance/LOO can make layouts unique; requires new immutable representation or copy-on-write | Fixture-specific potential, not a general saving and not measured |
| Lazy restoration / batching | No demonstrated saving in this path | Viewer bands, global summaries, report freshness and subsequent saving consume the ensemble. Delaying full restoration generally only moves the cost; evicting models requires a different retained representation and cache policy | Not justified as an independent simple fix |

## Disposable initialization prototype

`prototype.patch` changes only the copied BootstrapModelSnapshot.Restore. Complete parameter shapes construct a concrete model, install catalog defaults and saved parameters, then continue through existing saved-option application and final saved-parameter replacement. Incomplete shapes use the original initialization. Sequential structural ApplyModelOptions can still initialize its dynamic table. The ordinary valid-peak guard remains. No scientific equation, persisted schema, limit or number of retained replicates changes.

Five fresh Release runs:

| Case | Allocated MiB | Live MiB | Peak RSS MiB | Seconds |
|---|---:|---:|---:|---:|
| Core baseline | 952.36 | 149.67 | 310.14 | 5.078 |
| Core prototype | 817.42 | 149.67 | 305.20 | 4.877 |
| Viewer baseline | 979.11 | 18.32 | 311.13 | 5.203 |
| Viewer prototype | 843.29 | 18.32 | 304.98 | 4.953 |

Allocation reduction is stable at approximately 135 MiB (14%). Peak medians improve by only 4.9–6.1 MiB (about 2%). Core times overlap: baseline 4.972–5.220 s, prototype 4.633–5.105 s; viewer 5.121–5.600 s versus 4.722–5.183 s. The approximately 4–5% median time improvement is suggestive, not a robust latency guarantee. Retention does not improve. The prototype is not proposed as production-ready: current factory memory and partial legacy option defaults need more specific scrutiny before adopting a separate initialization path.

Validation actually run:

- JORS Table1 strict load, all 13,000 replicate states, 610,000 replicate enthalpy predictions (with and without offset), primary bands, primary/replicate reported and derived parameters, full correlation rows/matrices, global parameter/temperature summaries and matched member indices compared by IEEE-754 bits. All equal; `scientific.bin` SHA-256 `AF9083923099149A45A0C9EF471AF90012FF11B8B65664E2C042D0B9F5C927EE` (35,288,406 bytes).
- Complete viewer JSON equal byte-for-byte (4,007,690 bytes).
- Both paths saved and reopened strictly with zero issues. All bootstrap descriptors/matrices are byte-identical. Across the rest of the saved packages the only payload differences are 26 freshly restored embedded Null-solution GUIDs, plus the corresponding manifest checksums; every other value matches (`save-comparison.json`). These are not bootstrap replicate identities.
- Representative sibling/primary independence checks for parameter mutation, injection inclusion and option-object identity passed for every primary ensemble.
- 282 existing tests passed, zero failed/skipped, against the disposable prototype: FTXTCFormatTests, FTITCBootstrapSnapshotTests, SequentialPersistenceTests, BootstrapPredictionCacheTests, BootstrapCorrelationAnalyzerTests and ViewerDocumentReaderTests. No test cases were added to the checkout. The first VSTest attempt was blocked by sandbox socket permissions before execution; the unsandboxed rerun passed. Full Core, desktop, Web HTTP and release suites were not run.

## Decision and remaining uncertainties

Keep the existing representation/restoration for ITC-216. Do not describe compact persistence as a runtime-memory fix, and do not use the previous 306 MiB number as calibrated retained object memory. The tested narrow optimization is insufficient to solve retained memory and offers too little demonstrated peak benefit to justify an extra restoration initialization contract at present.

This is one substantial real fixture on one local runtime with workstation GC. It is not a worst-case bound at 20,000 replicates/500,000 injection records; model/site count, sampled options, global memberships, report content and other documents can change the cost. The HTTP handler uses the same reader and returns DTOs; its default active-upload limit is one, but request buffering, response serialization, Linux/server GC, other services and existing heap pools were not measured. Do not multiply 150 MiB by stored viewer documents or extrapolate concurrency from these runs. GC-dump construction also has a separate collection point and can trim pools, explaining a small difference from the immediate post-call heap.

Before authorizing an implementation, establish the actual host/process memory target and demonstrate that the candidate changes peak or retained memory enough to meet it. For a restoration-only candidate, minimum validation is exact saved-state/observable-output comparison across all six models, all bookkeeping methods, concentration variance, sampled competitors, tandem segments, locked/unlocked parameters and local/global LOO; malformed/legacy and partial-option fallback under nonempty factory defaults; reordered/missing global indices; strict save/read and ownership independence; then full Core tests and affected Web/desktop checks. If sharing/reusable evaluators are proposed, add explicit concurrent evaluation and cache invalidation tests. Keep original-fit estimates and every saved sample unchanged. No production implementation is authorized by this investigation.

## Reproduction

Run commands from any directory. Frozen binaries avoid differences from subsequent checkout changes. Logs and raw measurements are in `runs/baseline`, `runs/restore-init`, `instrumented`, `Check`, `test-results`, and the `heap-*.gcdump` / `heap-*-attribution.txt` files.

```sh
# One uninstrumented load; /usr/bin/time may need unsandboxed execution on macOS.
/usr/bin/time -l dotnet /private/tmp/itc216-20261023/baseline/output-release/Probe.dll core '/private/tmp/itc216-20261023/fixtures/JORS Table1.ftxtc'
/usr/bin/time -l dotnet /private/tmp/itc216-20261023/baseline/output-release/Probe.dll viewer '/private/tmp/itc216-20261023/fixtures/JORS Table1.ftxtc' --limits

# Source builds (all outputs remain under the disposable root).
dotnet build /private/tmp/itc216-20261023/Probe/Probe.csproj -c Release -p:CoreRoot=/private/tmp/itc216-20261023/baseline -p:NuGetAudit=false -o /private/tmp/itc216-20261023/baseline/rebuilt-release
dotnet build /private/tmp/itc216-20261023/Probe/Probe.csproj -c Release -p:CoreRoot=/private/tmp/itc216-20261023/restore-init -p:NuGetAudit=false -o /private/tmp/itc216-20261023/restore-init/output

# Five-process matrix per case; reruns create a timestamped directory and preserve earlier logs.
python3 /private/tmp/itc216-20261023/run_matrix.py baseline 5
python3 /private/tmp/itc216-20261023/run_matrix.py restore-init 5
python3 /private/tmp/itc216-20261023/make_variants.py

# Heap captures require diagnostic IPC access; uses the frozen Release binary.
python3 /private/tmp/itc216-20261023/collect_heaps.py
dotnet /private/tmp/itc216-20261023/Heap/bin/Debug/net10.0/Heap.dll '/private/tmp/itc216-20261023/heap-core-JORS Table1.gcdump'

# Observable scientific comparison and strict save/read.
dotnet /private/tmp/itc216-20261023/Check/baseline/Check.dll '/private/tmp/itc216-20261023/fixtures/JORS Table1.ftxtc' /private/tmp/itc216-20261023/check-results/baseline
dotnet /private/tmp/itc216-20261023/Check/restore-init/Check.dll '/private/tmp/itc216-20261023/fixtures/JORS Table1.ftxtc' /private/tmp/itc216-20261023/check-results/restore-init
cmp /private/tmp/itc216-20261023/check-results/baseline/scientific.bin /private/tmp/itc216-20261023/check-results/restore-init/scientific.bin
cmp /private/tmp/itc216-20261023/check-results/baseline/viewer.json /private/tmp/itc216-20261023/check-results/restore-init/viewer.json

dotnet test /private/tmp/itc216-20261023/restore-init/AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~FTITCBootstrapSnapshotTests|FullyQualifiedName~FTXTCFormatTests|FullyQualifiedName~BootstrapPredictionCacheTests|FullyQualifiedName~BootstrapCorrelationAnalyzerTests|FullyQualifiedName~SequentialPersistenceTests|FullyQualifiedName~ViewerDocumentReaderTests' --logger 'trx;LogFileName=focused.trx' --results-directory /private/tmp/itc216-20261023/test-results --nologo
```
