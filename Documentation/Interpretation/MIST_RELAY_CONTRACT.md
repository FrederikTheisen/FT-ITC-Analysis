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

Responses contain the generated interpretation and existing retrieval and
omission provenance, together with the scientific guidance revision,
`outputFormatVersion`, and SHA-256 fingerprints of the exact scientific and
output instruction strings. The effective-input fingerprint identifies the
final request after transport fallbacks. A nonempty interpretation is accepted
regardless of word count, headings, or Markdown shape. Network failures, empty
responses, and malformed service responses are generation errors.

The app evidence fingerprint is a SHA-256 hash of the compact UTF-8 canonical
evidence JSON (`sha256:utf8:canonical-package-json-v1`). It is used for
freshness and includes the selected context and report choices represented in
the package. Changing server guidance does not make unchanged evidence stale.
Instruction fingerprints identify text but cannot reconstruct it.

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
