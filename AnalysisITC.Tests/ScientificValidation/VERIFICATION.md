# A06 verification — 12 September 2026

**13 September supplement:** the [external integrated-heat study](ExternalIntegratedHeats/RESULTS.md)
records 11 forward comparisons and 44 fits of upstream pytc-generated heats,
including eight strict native-protocol parameter-recovery misses. Its ordinary
Core regression run reports 1,140 passed, 0 failed and one explicitly skipped
diagnostic theory. The dated 12 September verification below is retained;
the supplement does not rerun or extend the earlier platform acceptance scope.

The scoped scientific evidence matrix is verified in the local working tree
based on commit `111438cccc69f7271f4fec70b21ee1729b65d59a`. This is an
uncommitted development checkout containing pre-existing interpretation/UI
changes as well as the A06 work; it is not an immutable release candidate.
The accompanying [manifest](verification-manifest.json) records source hashes,
the tracked diff hash, restored dependency-asset hashes, test results and
local log/TRX hashes. The verification documents themselves are excluded
from their own source-hash inventory to avoid circular hashes.

Environment: macOS 15.7.4, arm64; .NET SDK 10.0.302, MSBuild 18.6.11,
.NET runtime 10.0.10. Tests use Release configuration and existing restored
dependencies (`--no-restore`). The test runner required local-socket permission.

| Verification | Result |
| --- | --- |
| Full Core suite | 1,093 passed, 0 failed |
| Full Avalonia suite | 202 passed, 0 failed |
| Full Web suite | 240 passed, 0 failed |
| Native Preferences layout fixture | PASS |
| Native interpretation actions/package-footer layout fixture | PASS |
| Independent generator reproducibility | All three frozen outputs reproduced byte-for-byte |
| Dissociation CSV/reference consistency | All 36 frozen heats match their test arrays |
| Whitespace and local Markdown links in the new evidence material/audit | Checked |

The .NET total is **1,535**. The **53 new A06 cases** are included in the Core
count, not additional tests:

| Test class | Passing cases |
| --- | ---: |
| ScientificReferencePipelineTests | 6 |
| ScientificImportCultureTests | 3 |
| PublishedOneSiteMultiStartTests | 4 (12 optimizer runs) |
| IndependentBindingReferenceTests | 8 |
| SequentialScientificReferenceTests | 9 |
| DissociationScientificReferenceTests | 14 |
| DerivedScientificReferenceTests | 9 |

## Commands

From the repository root after normal dependency restore:

```sh
dotnet test AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj --configuration Release --no-restore
dotnet test AnalysisITC.Web.Tests/AnalysisITC.Web.Tests.csproj --configuration Release --no-restore
dotnet test AnalysisITC.Avalonia.Tests/AnalysisITC.Avalonia.Tests.csproj --configuration Release --no-restore
python3 AnalysisITC.Tests/ScientificValidation/RawPipeline/generate_reference.py
python3 AnalysisITC.Tests/ScientificValidation/Sequential/generate_reference.py
xcrun swift -module-cache-path /private/tmp/ftitc-a06-validation/swift-preferences-cache AnalysisITC.MacOS.Tests/PreferencesLayoutTests.swift
xcrun swift -module-cache-path /private/tmp/ftitc-a06-validation/swift-interpretation-cache AnalysisITC.MacOS.Tests/InterpretationLayoutTests.swift
```

Each .NET command also used `--logger 'trx;LogFileName=<suite>.trx'` and
`--results-directory /private/tmp/ftitc-a06-validation`. Final files are
`core.log/.trx`, `web.log/.trx` and `avalonia.log/.trx`. Earlier focused
failure records in the same directory document the import-locale error,
weak-association cancellation, spline drift bias, approximate log conversion,
TAPSO settings, protonation scaling and degenerate-regression checks.
These local temporary artifacts should be archived with the eventual
publication candidate; their hashes alone do not preserve their contents.

## Interpretation and limits

See the [matrix](README.md) and each component README for independent
expectations, starts, exclusions, locks, units, numerical settings and
tolerances. Recovery acceptance was not loosened after failed fits. Higher-step
and dissociation references required explicitly tighter stopping settings;
uniform SD scaling changes numerical conditioning without changing the
least-squares optimum. The synthetic examples do not establish noisy-data
identifiability, model adequacy, empirical interval coverage or numerical
superiority over another application.

The buffer review used source page images because text extraction misread
mathematical signs. The NIST PDF is an external reference, not a redistributed
dataset; exact source URL and hash are recorded in the manifest.

Avalonia emitted NU1900 when NuGet vulnerability metadata was unavailable.
No fresh dependency-vulnerability clearance or clean-cache build is claimed.
The Preferences fixture emitted non-fatal macOS XPC diagnostics and passed its
assertions. Native layout fixtures do not launch the complete application or
qualify scientific behavior on Mono. No Windows/Linux execution, browser
interaction suite, production-service verification or installer acceptance
was performed. The opt-in live interpretation test was not enabled.

No packaging, signing, notarization, deployment, commit, push, version or
citation-metadata operation was performed. A04, A07, A08 and A10 retain their
separate acceptance work; these results do not establish full publication
or public-release readiness.
