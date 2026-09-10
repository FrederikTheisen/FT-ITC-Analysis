# MIST interpretation relay contract

The desktop client and MIST server use the relay request and response contract
`ft-itc-relay-{request,response}-4.0`. The evidence package remains schema
`2.0`. A request has
the request ID, generation profile, `outputInstructions` (the exact text used
by the app renderer), `outputFormatVersion`, and the evidence `package`.

MIST validates the JSON envelope, content type, bounded body size, required
fields, and the supported evidence schema. It preserves unknown scientific
properties and enum strings, including nested values, and does not reject a
package because a scientific field is incomplete or unusual. MIST combines the
supplied presentation instructions with its one active, versioned scientific
guidance resource. Presentation instructions control formatting; server
guidance controls evidence assessment. MIST must not substitute a server
formatting specification.

Version 4 gives `generationProfile` the server-defined values `instant`,
`fast`, `standard`, `in-depth`, or administrator-only `custom`. Public,
Standard, and Advanced access receive fixed subsets of the named presets;
Administrator access uses explicit allowlisted model and reasoning headers.
The `/api/interpretation/options` response supplies the permitted controls.
MIST resolves presets through `/etc/ftitc-web/generation-presets.json` and
returns the effective preset and configuration revision with the response.

During the desktop transition MIST also accepts version 3. Anonymous v3
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
This encoding does not change the evidence or relay version. Server guidance
revision `itc-scientific-guidance-3.2` describes the current bounds; earlier
instruction resources remain in source for provenance.

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

### Compact model evidence

The desktop derives a separate `compact-tables-v1` model payload from the full
local evidence. This representation keeps evidence schema `2.0` and the relay
contract unchanged. MIST forwards it as opaque JSON; its existing scientific
guidance and trace-omission paths continue to apply.

The application omits thermogram traces by default to keep ordinary requests
small. The report-builder option to include compressed traces is shown only for
locally verified Advanced or Administrator capability access; the writer and
transport fallback continue to support the option when explicitly selected.

Injection records are carried in acquisition, integration, heat-observation,
fit and baseline tables, for both result members and supporting experiments.
The package declares the column schemas once. Every table identifies its
schema and experiment report reference, and every row identifies its injection.
Array positions correspond to the declared columns; `null` remains unavailable,
not zero. Excluded injections and the original record order are retained.
Scientific quantities, experiment identities, blank relationships and source
fingerprints remain available. Internal evidence catalogs/IDs are omitted;
correlation scope links use report references instead. Results are not merged.

Ordinary scientific values use six significant digits; time, baseline/power,
slope, drift and thermogram-extrema values use nine. Thermogram anchor, bin
width and offset, information criteria, likelihood values and parameter bounds
retain full precision. Narrow parameter/interval groups retain full precision
when rounding would collapse distinct values or change their ordering, and
imperfect correlations must not become exactly +1 or -1. Strings, context,
identifiers and integers remain unchanged. This is representation-only
rounding, not a new calculation of fits or diagnostics.

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

Routine prompt-builder logs contain a single readable size/timing summary, without request IDs or fingerprints. Failures retain a request ID and exception type for troubleshooting. Full fingerprints remain in provenance and offline debug exports.

Other diagnostic logs may include request IDs, revisions, fingerprints, sizes,
omissions, timings, and failure stages. They must not include experimental
content, user context, generated text, full instructions, or credentials.

Usage bookkeeping is optional and must not change the generation outcome.
Report and result IDs are recorded only when present as strings; unavailable
or malformed IDs are skipped without modifying the evidence package. Each
provider attempt, including an incomplete response or an attempt followed by
a fallback, records the available usage and estimated cost. Missing or
malformed usage fields remain unknown rather than preventing delivery of a
valid interpretation. An incomplete response remains a generation error even
when its usage is recorded.

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
