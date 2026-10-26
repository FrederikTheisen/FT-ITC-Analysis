# MIST interpretation relay contract

The desktop client and MIST server use the relay request and response contract
`ft-itc-relay-{request,response}-6.0`. The evidence package is currently schema
`2.2`; MIST accepts package schemas `2.0`, `2.1`, and `2.2` during client transition. A request has the request ID, `taskType`, required Boolean
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

Schema 2.2 adds `nullModelId`, `nullModel`, and `nullParameters` to independent
member assessment and Null-member evidence. `nullModel` contains the actual model
name, such as `Offset`, or `Unknown` when identity is unavailable. Fit status and
comparison scope are separate fields. Named parameters use `InterpretationParameterEvidence`:
reported best-fit values, SI units, and constraints come from the matching saved Null
solution. Uncertainty is supplied only where computed; rebuilt native Null solutions
normally have none. The compatibility field `offsetJoulesPerMole` is populated only
for confirmed Offset Null evidence. Saved observations and Null predictions remain the
comparison evidence. Compact model input retains these fields.

Deployment dependency: the MIST server must accept 2.2 before a desktop client sending
2.2 is released. Updating these sources does not deploy the server.

Schema 2.1 may include per-experiment `traceability` identifiers entered by the
user and a `sourceDataFingerprint`. The fingerprint is SHA-256 over an explicit
ordered encoding: `rawThermogram` uses the raw sample count and time/power pairs;
`integratedHeats` uses injection ID, volume, integration availability, raw
integrated heat and reported uncertainty. Processing state is excluded. Compare
fingerprints only when both `sourceDataKind` and fingerprint match; equality
identifies the same supplied source observations, not independent replication.
Algorithm details are implementation and contract details and are not sent as
model instructions.

Experiment evidence may also contain the optional ordered `tandemSourceExperimentIds`
array, identifying the source experiments recorded when a tandem was merged. It
expresses the source relationship alongside buffer-reference provenance; it does
not assign a fitted result or control role to a source. Included sources remain
ordinary supporting experiments with unchanged observations. Recorded IDs can
refer to experiments absent from the package. The field is omitted when no IDs
were recorded, preserving canonical evidence fingerprints for older tandems and
single-file concatenated imports. This additive field changes neither the
evidence package schema version nor the relay version.

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
`3.7.3` for a new registry); retained revisions remain available for controlled administrator
comparison. Standard and Compact `3.7.3` are addressable variants, and Persona
`3.8.0` remains experimental. The effective deployed default is the persisted
registry value. Summary requests use independently
versioned guidance, currently `2.2`. Updating a source file does not change an
already deployed service.

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
final provider request after any knowledge-retrieval fallback. A nonempty interpretation is accepted
regardless of word count, headings, or Markdown shape. Network failures, empty
responses, and malformed service responses are generation errors.

The app evidence fingerprint uses `sha256:utf8:canonical-package-json-v2`: SHA-256
of the full-precision UTF-8 canonical evidence JSON with only the report name and
its report evidence-catalog label replaced by empty strings. The request retains
the report display name; renaming that label does not change scientific freshness.
The fingerprint includes the selected context, report choices, and result order
represented in the package. Changing server guidance does not make unchanged evidence stale.
Saved `sha256:utf8:canonical-package-json-v1` fingerprints remain verifiable with
their original report name. Optional `reportNameForFingerprint` provenance retains
that name when a legacy report is renamed; the original saved fingerprint is not rewritten.
Save Report As retains optional `reportIdForFingerprint` and `reportDateUtcForFingerprint`
in approved interpretation provenance. The latter is the exact UTC round-trip string
from the original package. Both legacy v1 and v2 comparison packages substitute these
source values; requests continue to use the current report identity and creation date.
Repeated copies preserve the original source, without rewriting stored fingerprints.
Optional `confirmedAtUtc` and `reviewedResultIds` track the builder's separate review
confirmation for manual and AI text. They do not enter evidence packages or change
fingerprint freshness. Confirm preserves text, approval dates, and AI provenance.
A user edit replaces the active record with manual origin, the current writing time,
and current reviewed result scope; previous AI fingerprints and generation metadata
are removed. Generated text is read-only in the generation dialog and can be edited
after Use in Report. Newly generated interpretations start with their own report context.
The builder also reviews both origins after any result content change, using the
optional native-project `lastContentChangedUtc` timestamp. That timestamp does not
enter the interpretation package or alter the relay contract version.

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
When the salt-dependence component is available, electrostatics also adds
`xAxis: ln-salt-concentration`, `yAxis: ln-dissociation-constant` and
`slopeParameter: salt-dependence-slope`. Both axes are natural logarithms of
values in mol/L: the recorded single-salt concentration and Kd. These axes
describe only that slope, not the ionic-strength values in the same record.

Protonation adds `xAxis: buffer-ionization-enthalpy`,
`yAxis: observed-binding-enthalpy`, `slopeParameter: protonation-change` and
`interceptParameter: binding-enthalpy`. Both enthalpy axes use J/mol. These
identify the regression without recalculating inputs. `protonation-change` is
net proton uptake per binding event: positive uptake by the binding system,
negative release. `binding-enthalpy` is the buffer-independent intercept at the
experimental pH and retains protonation contributions from the binding partners;
it is not fully intrinsic binding enthalpy. Earlier packages used the misleading
axis identifier `buffer-protonation-enthalpy` for the same ionization-enthalpy
coordinates. That historical label must not trigger sign conversion of saved
values or intervals.

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

### Assessment-aware member evidence

Every result member retains its available binding-model parameters and its saved
assessment in the interpretation package, including No binding detected and
Inconclusive members. Parameter presence does not establish binding. Guidance
uses the effective assessment, including optional manual overrides, to decide
which estimates may be interpreted as findings. Automatic outcomes, rule IDs,
and comparison evidence remain available alongside overrides. No-binding
estimates describe an attempted model and must not be presented as established
affinity or thermodynamic findings. Inconclusive estimates remain available
with the uncertainty in the binding assessment explicit; Not assessed is not
positive evidence. Independent members use their own assessments, while pooled
fits use the applicable result-level assessment. Pooled diagnostics for
independent fits do not replace member evidence.

For an independent assessment collection, `collectionOutcome` and
`effectiveOutcome` summarize the effective member outcomes: an empty collection
is `NotAssessed`, a uniform collection keeps its common outcome, and differing
outcomes use `Mixed`. `Mixed` describes the member assessments; it is not a
statistical verdict. `automaticOutcome` is aggregated separately from the
members' automatic outcomes, so manual overrides can make it differ from the
effective summary. Member assessments and the unchanged per-outcome counts
remain authoritative. Relay consumers must remain compatible with older
requests whose collection summary used precedence; for independent collections,
they should interpret the member fields rather than infer a uniform assessment
from a legacy collection label. Single and pooled result outcomes retain their
existing meaning.

When any member is effectively No binding detected, combined binding values and
dependent analyses are not findings for the full collection. Guidance must not
construct a new combined result from the remaining members. These presentation
and interpretation changes do not alter evidence or relay schema versions.

### Binding c-value evidence

Fitted result members may include `cValues`. Each entry has a stable evidence ID,
quantity ID, display name, model-specific kind, optional site or step index,
concentration basis, and the central dimensionless value. Independent one- and two-site values use
`N [cell]₀ / Kd`; syringe-correction fits use the fixed site count rather than
the fitted syringe active fraction. Sequential step values use `[cell]₀ / Kd_i`,
and competitive binding supplies only the apparent value based on `Kd_app`.
Dissociation and supporting experiments do not supply c-values.

The first segment concentration is used for tandem experiments. Values are
member-local fit evidence and are not moved into shared experiment-source
records. Compact input removes their internal evidence IDs and rounds their
central values with the ordinary five-significant-digit policy. The canonical
full-precision entries participate in the existing freshness fingerprint. These
are additive evidence-schema 2.0 fields and do not change the relay contract.

### Compact model evidence

The desktop derives a separate model payload from the full local evidence. The
representation keeps evidence schema `2.0` and the relay contract unchanged.
The payload identifies its representation with `modelInputEncoding`: ordinary
packages use `compact-tables-v1`; when complete source evidence is duplicated
between result members, the smaller candidate may use
`compact-tables-shared-evidence-v1`. MIST forwards either form as opaque JSON;
its scientific guidance applies to both.

Full local canonical capture includes all supported evidence, including injection
and Null points and compressed thermograms, independently of the selected model
input level. Canonical omission reasons describe unavailable captured evidence.
The canonical package remains the freshness authority. This changes unpublished
development fingerprints; saved text and its original fingerprint are retained,
with normal Stale status when the rebuilt full capture differs. The fingerprint
algorithm and evidence/relay versions are unchanged.

Both desktop dialogs select one cumulative **Evidence level**: Results,
Injection data, Processing details, Thermograms. Each includes all lower levels.
Injection data is the default where permitted. Public access permits Results;
Standard additionally permits Injection data; Advanced and Administrator permit
all four. A saved Thermograms selection reopens at Processing details, requiring
fresh opt-in. Capability responses, including failures, recompute effective
selection and visibility without erasing the dialog's requested level. An
explicit user choice replaces that requested level.

The enum and dialog state are not serialized. Transmitted
`requestedInterpretation` uses the existing fields, normalized together:

| Effective level | `injectionRows` | `includeProcessingInformation` | `includeThermograms` |
|---|---|---|---|
| Results | `none` | false | false |
| Injection data | `all` or preserved `includedOnly` | false | false |
| Processing details | `all` or preserved `includedOnly` | true | false |
| Thermograms | `all` or preserved `includedOnly` | true | true |

Noncumulative combinations map downward to their deepest complete level. In
particular, `injectionRows:none` with processing enabled becomes Results, with
both Boolean flags false in the model payload. This normalized metadata also
controls server SIGNAL, INJECTIONS and THERMOGRAM guidance. `includedOnly` filters
model injection tables and both pooled and independent-member Null points;
canonical capture retains all eligible rows. Within a dialog, changing levels
preserves the saved `includedOnly` preference.

The selected level applies fully to every fitted result member. A selected
supporting experiment is transmitted as a summary at every level: identity,
metadata, comments, conditions, provenance, relationships and processing
completion. At Processing details and Thermograms, its summary also includes
processing settings, scalar baseline statistics and baseline availability or
unavailability reasons. Supporting experiments never transmit injection rows,
thermograms, baseline landmarks or baseline control representations. Their
summary is unchanged at Results and Injection data. Shared source evidence
referenced by a fitted member follows that member's selected level; a
supporting-only record uses the summary rule.

`studyContext.experiments` annotations are filtered to experiment IDs retained
as selected result members or supporting experiments. Other saved report
context is retained. This filtering affects only the transmitted model payload;
the complete local capture and freshness fingerprint remain unchanged. The
payload keeps plural `bootstrapCorrelations`; a legacy singular-only
`bootstrapCorrelation` is normalized to that plural form.

#### Evidence ownership

Here `E` means `results[].experiments[]`, `supportingExperiments[]`, or shared
`experimentEvidence[]`. The table describes transmitted evidence; canonical
capture remains complete. Result members use the levels in the table. The table's
fit and detailed source-evidence rows apply only to result members and shared
records used by them; a supporting-only record retains only the summary metadata
rows and, at Processing details or Thermograms, processing settings, scalar
baseline statistics and availability/unavailability reasons. Applicable
scientific output restrictions still apply.

| JSON paths/evidence | First included level |
|---|---|
| `report`, `studyContext`; `results[].{reportReference,resultId,name,dateUtc,comments,health,validityStatus,validityReasons,matchedFitDiagnosticsUnavailableReason}` | Results |
| Result `model`, `solver`, `informationCriteria`, `bindingAssessment` except points, `bootstrapCorrelations`, `temperatureDependence`, `advancedAnalyses` | Results |
| `E.{reportReference,experimentId,name,sourceFileBasename,dateProvenance,dateUtc,comments,sourceStateFingerprint,traceability}` | Results |
| Result-member `E.{solver,informationCriteria,evidenceBasis,matchedFitDiagnosticsUnavailableReason,unavailableDerivedParameterReason}` | Results |
| `E.{concentrationMethod,heatMethod,fittedHeatMethod,analysisAxis,instrument}` | Results |
| `E.{targetTemperatureKelvin,measuredTemperatureKelvin,targetTemperatureCelsius,measuredTemperatureCelsius,cellConcentrationMolar,cellConcentrationSdMolar,syringeConcentrationMolar,syringeConcentrationSdMolar}` | Results |
| `E.{attributes,tandemSegments,tandemSourceExperimentIds,blankReferenceExperimentId,blankSubtractionMethod}`; all records' processing completion flags | Results |
| Result-member `E.{initialDelaySeconds,modelOptions,parameters,cValues,residualDiagnostics}` | Results |
| Result-member `E.injections.{acquisition,heatObservations,fit}`; `results[].bindingAssessment.{members,nullMembers}[].points` | Injection data |
| Result-member `E.injections.{integration,baseline}` | Processing details |
| Result-member `E.baseline`, including availability, drift/range/RMS statistics, landmarks, spline/segmented/polynomial representations | Processing details |
| Result-member `E.{baselineProcessor,processorLocked,discardsIntegratedPointsForBaseline,integrationLengthMode,integrationLengthFactor}` | Processing details |
| Entire `results[].historicalFitInputs` (saved metadata, injection data and processing snapshot); invalid-fit warning remains at Results | Processing details |
| Result-member `E.thermogram`, aligned baseline arrays, trace-specific omission/unavailability reasons | Thermograms |
| `packageSchemaVersion`, `modelInputEncoding`, `requestedInterpretation`, `omissions`, `dataBoundary`, `tableSchemas`, `modelInputDefinitions`; shared owner/reference links | Describe retained evidence at every level |

For supporting-only records, the model package includes omission disclosures
for the omitted injection rows, thermograms, landmarks and baseline controls.
Schemas are retained only for actual positional tables. Below Thermograms,
trace-specific unavailability reasons are replaced by the trace-selection
omission; unrelated omissions remain intact. Selected traces are retained even
when large: request-size or provider-context rejection fails visibly instead of
automatically reducing the selected level.

#### Evidence boundaries

`dataBoundary` describes actual retained model content, rather than capability
or requested availability. Full canonical flags describe full captured content.

| Flag | Results | Injection data | Processing details | Thermograms |
|---|---|---|---|---|
| `containsRawThermogramSamples` | false | false | false | retained compressed trace exists |
| `containsBaselineArrays` | false | false | false | retained `baselineMinMax` array exists |
| `containsBaselineSummary` | false | false | available retained baseline summary exists | same |
| `containsBaselineControlRepresentation` | false | false | nonempty landmark table or spline/segmented/polynomial representation exists | same |
| `containsBootstrapReplicateArrays` | false | false | false | false |
| `containsLocalPaths` | false | false | false | false |

The historical `containsRawThermogramSamples` wire name denotes the compressed
thermogram representation, subject to its explicit limitations.
`modelObservationRestriction` composes a trace-availability clause with the
applicable No-binding assessment clause. Removing traces or recomputing a
boundary never erases that assessment restriction or duplicates it.

Injection records are carried in acquisition, integration, heat-observation,
fit and baseline tables, for both result members and supporting experiments.
The integration schema `integration-v2` uses `nonIntegratedTimeFraction`,
defined as `1 - integrationLengthSeconds / injectionDelaySeconds`; older
packages may contain the complementary integration fraction instead.
The package declares the column schemas once. Every table identifies its
schema and owning report or evidence reference, and every row identifies its
injection.
Array positions correspond to the declared columns; `null` remains unavailable,
not zero. Excluded injections and the original record order are retained unless `includedOnly` explicitly filters excluded rows from the model payload.
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
any knowledge-retrieval fallback. The transport limit applies to the compact
request envelope. The desktop compares the serialized request with the current
tier limit before sending it.

Offline exports retain `canonical-package.json` (exact full evidence used for
freshness) and `package.json` (its readable copy). `model-package.json` contains
the exact compact model payload before transport. The manifest
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
not mutable request summaries. One immediate SQLite admission transaction checks
duplicates, migration activation blockers, the maintenance gate, installation
activity, lifetime unresolved installation liabilities, installation limits and
global recorded limits, in that order. Public quota checks therefore follow the
retired, availability and accounting-enabled endpoint checks. Provider attempts are
recorded before dispatch; their receipts distinguish known cost, confirmed zero cost
and unresolved cost. A partial known total is not a complete balance. Identical
repeated receipts are harmless; conflicting receipts cannot replace existing charges.
Network calls never run inside the admission transaction. Admission uses recorded
charges without predicted-cost reservation; concurrent work can finish above a
monetary ceiling.

Disabled or unavailable accounting returns HTTP 503
`interpretation_accounting_unavailable`. Unresolved prior accounting that blocks
registered-account admission returns HTTP 503 `interpretation_accounting_unresolved`;
unresolved public installation liabilities retain HTTP 429
`interpretation_public_accounting_unresolved`. Known quota exhaustion and active
quota-limited work retain their HTTP 429 responses. Admission or attempt-start
persistence failures make no provider call. A fallback remains within
the same execution and requires the preceding attempt's durable, resolved accounting.
An HTTP error alone does not demonstrate that a provider attempt was free.

Usage database schema 7 retains immutable execution identity, the access
classification and public request-count eligibility captured before provider
dispatch, and original provider receipts. Admitted, finalized and recovered
executions count once; rejected executions do not. Migrated rows use their stored
access tier. Unclassifiable historical rows remain non-public and appear in migration
preflight. Public unresolved liabilities remain tied to their installation across
month boundaries, including migrated unknown-cost issues; another installation can
continue against recorded global spending. Surviving historical totals are migrated without double counting.
Other migration issues marked as activation blockers still require administrative review.
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

## Relay version 7: provider selection

`ft-itc-relay-request-7.0` adds an optional `provider` string: `openai` (the
omitted-field default) or `mistral`. Versions 3–6 always resolve to OpenAI.
The options response adds `providers: [{ id, name }]`; administrator model
entries identify their `provider`. Presets, descriptions, tier access and
allowances remain shared. Every enabled provider requires a complete mapping
for Summary and all four interpretation presets.

Unknown providers return `invalid_interpretation_provider`; disabled or
unconfigured providers return `interpretation_provider_unavailable`, before
admission or dispatch. Generation never switches providers automatically.
The existing response `provider`, `model` and `reasoningEffort` fields capture
actual generation provenance. `default` reasoning denotes provider default
and omits the provider API's reasoning parameter; it does not denote `none`.
Mistral uses the existing prompt with retrieval disabled. Summary continues
to use its dedicated guidance with retrieval disabled for either provider.
