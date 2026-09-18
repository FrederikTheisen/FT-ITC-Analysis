# MIST interpretation relay contract

The desktop client and MIST server use the relay request and response contract
`ft-itc-relay-{request,response}-6.0`. The evidence package remains schema
`2.0`. A request has the request ID, `taskType`, required Boolean
`omitScientificGuidance`, generation profile,
`outputInstructions` (the exact text used by the app renderer),
`outputFormatVersion`, and the evidence `package`.

MIST validates the JSON envelope, content type, bounded body size, required
fields, and the supported evidence schema. It preserves unknown scientific
properties and enum strings, including nested values, and does not reject a
package because a scientific field is incomplete or unusual. MIST combines the
supplied presentation instructions with its server-selected, embedded scientific
guidance resource. Presentation instructions control formatting; server
guidance controls evidence assessment. MIST must not substitute a server
formatting specification.

Version 6 retains the version 5 generation controls and adds Administrator-only
scientific-guidance omission. With `omitScientificGuidance: true`, MIST excludes
the versioned scientific file and conditional model/scientific rules, while retaining
a minimal boundary declaring package and retrieved text to be evidence rather than
instructions. Retrieval behavior is unchanged. Summary rejects omission. Older relay
versions behave as `false`.

Version 5 retains the version 4 generation controls and adds the server-owned
`summary` task. Summary requests use the dedicated summary guidance, disable
retrieval, participate in the caller's shared allowance, and return a compact factual report with the fixed
headings `Overview`, `Main results`, `Data and fit quality`, and `Limitations`.
The `summary` task is available in the options response alongside the normal
interpretation presets. Summary and Fast usage participates in the caller's
normal shared allowance; task type does not exempt a request from accounting.
Version 5 also retains version 4 generation controls:
`generationProfile` has the server-defined values `instant`,
`fast`, `standard`, `in-depth`, or administrator-only `custom`. Public,
Standard, and Advanced access receive fixed subsets of the named presets;
Administrator access uses explicit allowlisted model and reasoning headers.
The `/api/interpretation/options` response supplies the permitted controls. Each
preset includes a server-managed `description` suitable for display beneath the
preset selector; clients must treat it as informational text and not infer model
or quota details from it.
MIST resolves presets through `/etc/ftitc-web/generation-presets.json` and
returns the effective preset and configuration revision with the response.
The same response includes `maximumRequestBytes` for the effective access tier.
Initial complete-envelope limits are 128 KiB for Public, 512 KiB for Registered,
1 MiB for Advanced, and 2 MiB for Administrator access. MIST accepts an envelope
at the exact tier limit and returns `interpretation_tier_size_exceeded` above it;
the absolute 2 MiB transport ceiling remains `interpretation_request_too_large`.
Invalid, expired, and revoked codes are rejected instead of receiving Public limits.
Public clients obtain a persistent installation bearer from
`POST /api/interpretation/public-access`; both options and generation require it.
The raw bearer is stored only in private desktop settings, while MIST stores its
hash. Revocation is sticky: a client retains the rejected bearer and does not
automatically enrol a replacement.

During the desktop transition MIST also accepts versions 5, 4 and 3.
Each receives its corresponding response version. Anonymous v3
requests map to Instant and receive a v3 response. Existing Administrator v3
requests retain their explicit model/reasoning override support.

Thermograms use `encoding: "uniform-minmax-v1"`. `powerMinMax` contains
nullable `[min, max]` pairs in median-centered µW, with `anchorTimeSeconds`
and `binWidthSeconds` (15) supplied once per trace. Position i covers the
half-open interval starting at anchor + i × width; the last interval may be
partial. Empty intervals remain `[null, null]`. Optional `baselineMinMax`
contains independently calculated bounds on the same grid and with the same
power offset; it is omitted when no finite baseline is available. No exact
extrema timestamps, ordering, endpoints or source indices are transmitted.
The source/finite sample counts and reversible power offset remain. Oversized
time spans are omitted before dense allocation, with a per-experiment reason.
This encoding does not change the evidence or relay version. MIST selects the
server default from its explicit, embedded guidance-version registry (initially
`3.7.0`); retained revisions remain available for controlled administrator
comparison. Updating a source file does not change an already deployed service.

Administrator requests using relay 6.0 may select an explicit guidance version
advertised by the options endpoint with `X-FTITC-Guidance-Variant`. MIST accepts
only those embedded version IDs; logical aliases such as `standard` and
`structured` are not accepted. Ordinary accounts cannot override guidance.
Responses and usage metadata identify the effective version, revision and
instruction fingerprint. Summary requests continue to use their separate summary
guidance and reject this header.

Responses contain the generated interpretation and existing retrieval and
omission provenance, together with the scientific guidance revision,
`outputFormatVersion`, and SHA-256 fingerprints of the exact scientific and
output instruction strings. The effective-input fingerprint identifies the
final request after transport fallbacks. A nonempty interpretation is accepted
regardless of word count, headings, or Markdown shape. Network failures, empty
responses, and malformed service responses are generation errors.

The app evidence fingerprint is a SHA-256 hash of the full-precision UTF-8 canonical
evidence JSON (`sha256:utf8:canonical-package-json-v1`). It is used for
freshness and includes the selected context and report choices represented in
the package. Changing server guidance does not make unchanged evidence stale.
Instruction fingerprints identify text but cannot reconstruct it.

### Advanced-analysis evidence

`results[].advancedAnalyses` carries completed Spolar–Record, electrostatics and
protonation outputs, including saved values, units, uncertainty bounds and
completion metadata. These records remain present independently of optional
injection tables, processing information and thermograms. No advanced analysis
is run by package construction.

Spolar–Record adds `completedFoldedMode` (`globular`, `intermediate` or
`intrinsically-disordered`) and `completedTemperatureMode` (`isoentropic-point`,
`mean-temperature` or `reference-temperature`). These describe the saved
completed analysis, not currently edited controls. Unavailable completed
settings remain null; no mode is inferred from the numerical output. The
existing reference-temperature value and bounds remain in kelvin.

Electrostatics adds `usesCurvature`, `ionicStrengthAvailable` and
`counterIonReleaseAvailable`, with `ionicStrengthCompletedIterations` and
`counterIonReleaseCompletedIterations` scoped to their respective components.
An unavailable component has no inferred estimate, sampling count or failure
reason. The enclosing completion status does not certify both components.

Protonation adds `xAxis: buffer-protonation-enthalpy`,
`yAxis: observed-binding-enthalpy`, `slopeParameter: protonation-change` and
`interceptParameter: binding-enthalpy`. Both enthalpy axes use J/mol. These
identify FT-ITC's existing regression convention without recalculating inputs.

`uncertaintyPropagation: random-input-sampling` describes propagation of input
uncertainties by repeated random sampling. The reported point estimate still
uses the original inputs. `uncertaintyMethod` is retained as historical
metadata; its null value does not mean that supplied uncertainty is absent,
and a saved bootstrap label does not identify the advanced-analysis sampling
algorithm. Sampling counts are not independent experimental replicates. Zero
or unavailable counts do not certify successful sampling or exact certainty.

Temperature-dependence entries also include stored SDs and lower/upper 95%
bounds for their intercept and slope. These support assessment of the trends
underlying SR without new regressions. Intercept groups use ordinary model
precision and slope groups use the existing slope precision, with the same
interval-collapse safeguards as other uncertain quantities. Historical
advanced-analysis input datasets are not reconstructed from current metadata.

These are additive evidence-schema 2.0 fields. Canonical evidence fingerprints
include them, while a scientific-guidance revision alone does not change local
freshness. Existing project files and saved interpretation text remain readable.
Guidance 3.6 assesses advanced results by scientific relevance, without requiring
an output section or a comment on each analysis. Offline debug exports identify
the pre-transport evidence; they do not prove the guidance revision used by an
earlier hosted generation.

### Compact model evidence

The desktop derives a separate model payload from the full local evidence. The
representation keeps evidence schema `2.0` and the relay contract unchanged.
The payload identifies its representation with `modelInputEncoding`: ordinary
packages use `compact-tables-v1`; when complete source evidence is duplicated
between result members, the smaller candidate may use
`compact-tables-shared-evidence-v1`. MIST forwards either form as opaque JSON;
its scientific guidance and trace-omission paths apply to both.

The application omits thermogram traces by default to keep ordinary requests
small. The report-builder option to include compressed traces is shown only for
locally verified Advanced or Administrator capability access; the writer and
transport fallback continue to support the option when explicitly selected.

Injection records are carried in acquisition, integration, heat-observation,
fit and baseline tables, for both result members and supporting experiments.
The integration schema `integration-v2` uses `nonIntegratedTimeFraction`,
defined as `1 - integrationLengthSeconds / injectionDelaySeconds`; older
packages may contain the complementary integration fraction instead.
The package declares the column schemas once. Every table identifies its
schema and owning report or evidence reference, and every row identifies its
injection.
Array positions correspond to the declared columns; `null` remains unavailable,
not zero. Excluded injections and the original record order are retained.
Scientific quantities, experiment identities, blank relationships and source
fingerprints remain available. Internal evidence catalogs/IDs are omitted;
correlation scope links use report references instead. Results are not merged.
In the shared form, each result member retains its report reference, experiment
identity and fitted evidence, and adds `experimentEvidenceRef`. The matching
root `experimentEvidence` record contains one complete source/processing state,
its `evidenceReference`, and the `reportReferences` that reuse it. Its source
metadata, thermogram, tandem/baseline evidence and the four non-fit injection
tables are owned by that record; the fit table remains on every member. A
record may be used once so the layout stays uniform. Reuse states that the
exported source evidence is identical; it is not independent replication, a
model equivalence claim, or proof that a historical fit used current processing.
Different processing states are separate records, and references are assigned
in deterministic first-use order. The representation is selected only when
the complete serialized payload is smaller than the inline form.

Baseline diagnostics remain attached to their experiment source record.
`baseline.landmarks`, `baseline.spline.controlPoints`, and
`baseline.segmented.segments` are table objects with `schema`, an owner
(`reportReference` for a member-local table or `evidenceReference` for a shared
record), and `rows`. Their shared schemas are
`baseline-landmarks-v1` (`timeSeconds`, `powerMicrowatts`),
`baseline-spline-controls-v1` (`timeSeconds`, `powerMicrowatts`,
`slopeMicrowattsPerSecond`, `userDefined`), and `baseline-segments-v1`
(`scope`, `injectionId`, `startTimeSeconds`, `endTimeSeconds`,
`centerTimeSeconds`, `coefficientsSi`). Segment coefficients retain the full
centred-time polynomial in SI units, including higher-order terms. Unknown
point properties are retained in an explicit table `extensions` map keyed by
row index. Spline `locked`, `slopeLocked`, and `linear` point flags are
intentionally omitted from the compact model payload; omission does not mean
false, and the control table is not sufficient to reconstruct the exact
interpolated baseline. These values describe a fitted baseline rather than raw
signal observations.

Ordinary scientific values use five significant digits; timing, baseline, power,
slopes and drift values use nine, while thermogram `powerMinMax` and
`baselineMinMax` values use six. Thermogram anchor, bin width and offset,
information criteria, likelihood values and parameter bounds retain full
precision. Narrow interval groups retain full precision if rounding would merge
or reorder distinct values; imperfect correlations are not rounded to exactly
+1 or -1. Strings, context, identifiers and integers remain unchanged. This is
representation-only rounding, not a new calculation of fits or diagnostics.

The model payload has its own hash. Its rounded values do not replace the
full-precision freshness fingerprint: a source change below the transmitted
precision still changes freshness. The server effective-input fingerprint
continues to identify the instructions and model evidence actually used after
fallbacks. The transport limit applies to the compact request envelope;
trace omission rebuilds that payload from a copy without changing local evidence.

Offline exports retain `canonical-package.json` (exact full evidence used for
freshness) and `package.json` (its readable copy). `model-package.json` contains
the exact compact model payload before transport fallbacks. The manifest
distinguishes full/model byte sizes and hashes, encoding and precision policy.
Use the model file for model-input evaluations; keep the full files for source
auditing. These files do not include server guidance or subsequent retrieval
and fallback changes. Routine logs report full/model sizes and reduction
without logging experimental content.

The authenticated `/api/interpretation/options` response may include an
`accessDetails` object containing the operator label as `name` and the UTC
expiration as `expiresAtUtc`. A supplied `expiresAtUtc` of `null` explicitly
means that the code does not expire. If `accessDetails` is absent, the client
must show that metadata is unavailable; it must not infer non-expiration.
Unauthenticated responses omit code metadata and return `accessDetails: null`.
Generation option entries may include an optional server-supplied `description`. Clients
use it only as explanatory presentation text and must remain compatible with servers that
omit it.

The authenticated `/api/interpretation/account` endpoint returns only the
current account's verified status, label, optional name and email, access tier,
expiration, effective request-size allowance, current quota usage, all-time recorded request count, and the most
recent recorded request outcome. It never returns the access code, its hash, or
another account's metadata. Quota reads require working accounting: an unavailable
ledger is a service error, not invalid credentials or a full allowance. Ancillary
request totals and the most recent request may be unknown rather than fabricated.

Costs from Summary, Fast, Default, Advanced, and Comprehensive attempts share
the account's single balance. Public requests and costs likewise share the
installation allowance regardless of task or preset.

Routine prompt-builder logs contain a single readable size/timing summary, without request IDs or fingerprints. Failures retain a request ID and exception type for troubleshooting. Full fingerprints remain in provenance and offline debug exports.

Other diagnostic logs may include request IDs, revisions, fingerprints, sizes,
omissions, timings, and failure stages. They must not include experimental
content, user context, generated text, full instructions, or credentials.

Accounting is required before every hosted provider dispatch, including every
preset, task, and Public request. A fresh server execution ID identifies each endpoint
invocation; the client request ID and HTTP trace ID are separate correlation fields.
The execution ID is server-owned and is not part of the desktop request envelope.
Successful responses continue to echo the submitted client request ID.

An authenticated submission claims `(operatorCodeId, clientRequestId)` atomically
with quota admission. The account comes from authentication. Claims span task types
and presets, survive all admitted outcomes and restarts, and are not removed at a
monthly reset. A valid repeat, including one with changed content, returns HTTP 409
`interpretation_duplicate_request` before provider work. There is no cached-result
replay. Rejections before admission do not consume an unused key. Other accounts may
reuse a client ID; anonymous client IDs remain correlation data only. Clients do not
automatically substitute IDs or retry ambiguous failures.

Quota accounting uses durable attempt charges and labelled historical balances,
not mutable request summaries. A transaction checks duplicates and quota and claims
a durable hold allowing one active quota-limited execution per account across service
instances. Provider attempts are recorded before dispatch; their receipts distinguish
known cost, confirmed zero cost and unresolved cost. A partial known total is not a
complete balance. Identical repeated receipts are harmless; conflicting receipts
cannot replace existing charges. Network calls never run inside the admission
transaction. Existing pricing, exemptions and admission below the recorded allowance
remain unchanged; this is not predicted-cost reservation.

Disabled or unavailable accounting returns HTTP 503
`interpretation_accounting_unavailable`. Unresolved prior accounting that blocks
admission returns HTTP 503 `interpretation_accounting_unresolved`; known quota
exhaustion and active quota-limited work retain their HTTP 429 responses. Admission
or attempt-start persistence failures make no provider call. A fallback remains within
the same execution and requires the preceding attempt's durable, resolved accounting.
An HTTP error alone does not demonstrate that a provider attempt was free.

Usage database schema 6 retains immutable execution identity and original provider
receipts. Surviving historical totals are migrated without double counting;
inconsistent ownership or charges block activation pending administrative review.
Audited historical settlements can establish an execution total without inventing
a per-attempt breakdown. Waived totals remain unknown in administrative reporting.
Account history counts admitted executions even when final reporting metadata could
not be written. None of these changes alter the relay or evidence schema versions.

Once valid interpretation text exists, later receipt or finalization failures do not
discard it: it may be delivered while accounting remains unresolved. Cancellation
still prevents publishing a late draft. Report and result IDs are optional metadata;
unavailable or malformed IDs are skipped without modifying evidence or discarding
generated text. Missing usage or pricing remains unknown. An incomplete response
remains a generation error even when its usage is recorded.

Cancellation must cover receiving the complete response, and a cancelled
generation must not publish a late draft or replace approved text. The relay
response is bounded to 2 MiB on the desktop, with its request timeout also
covering the body read. The relay forwards request cancellation to the provider.
The configured provider timeout
covers the complete generation, including response bodies and fallback
attempts. Cancellation and timeout are distinct outcomes; neither starts a
fallback attempt. When the final provider usage is unavailable, token counts
and cost remain unknown. Aborting the HTTP operation does not provide a
confirmation of the model's final remote state.
Unresolved accounting holds survive disposal, restart, elapsed time and month changes.
They require audited settlement or an explicit administrative waiver while generation
is paused and drained. A waiver permits progress without pretending unknown provider
cost was zero. See the deployment notes for inspection and recovery procedures.
