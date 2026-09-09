# MIST deployment notes

Deploy the Web project and its dependencies using the existing MIST procedure.
Stage the new service beside the current service, preserving the protected
credential environment, retrieval configuration, and hosted viewer files.
Keep a complete rollback copy of the prior deployment until production health
and a synthetic request have passed.

Before activation, run the Web tests and verify that a synthetic package using
the 3.0 envelope returns a response with the evidence schema, guidance
revision, instruction fingerprints, retrieval provenance, and effective-input
fingerprint. Use invented evidence only; never upload real experimental data
for deployment testing. Verify health and the interpretation status endpoint
after activation. Confirm that malformed JSON, missing envelope fields,
unsupported evidence schemas, and oversized bodies are rejected before a
model call.

The service keeps its existing model, retrieval, quota, and context-size
fallback behavior. The configured provider timeout covers the whole generation,
including response-body reads and fallback attempts. Before activation, verify
with a controlled provider that a client disconnect propagates through the
deployment's reverse proxy and cancels the provider request without starting a
fallback. In-process cancellation tests do not verify that proxy behavior.
Do not add a formatting registry or
negotiation layer. Scientific instruction resources are retained by revision
in server source. Their fingerprints identify the exact text used but cannot
reconstruct it. Roll back by restoring the saved service directory and
configuration, then repeat health checks.

## MIST administration

Trusted administrators can open the interactive console from any directory:

```bash
sudo ftitc-admintool
```

The launcher changes to `/opt/ftitc-web` and starts the web executable in
administration mode. It does not start Kestrel and does not source
`/etc/ftitc-web/interpretation.env`. Administration uses the configured paths,
whose deployment defaults are `/etc/ftitc-web/operator-codes.json` and
`/var/lib/ftitc-web/interpretation-usage.db`.

The console provides:

- **Status:** systemd state, local and public interpretation status, build and
  schema versions, operator-account totals, and usage-database statistics.
- **Operator accounts:** create, revoke, or list capability codes. A new secret
  is printed once. Listings and logs never contain the secret or its hash.
- **Logs:** list requests, show one request and its provider attempts, summarize
  a period with optional model/operator filters, or export metadata to CSV.

An operator account is revoked rather than deleted so historical usage remains
attributable to its non-secret record ID. The registry directory is owned by
`root:ftitc-web` with set-group-ID mode `2750`; the registry is `0640`. Atomic
registry replacements preserve owner read/write and group read permissions.

The original non-interactive commands remain available for automation:

```bash
cd /opt/ftitc-web
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name"
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --expires-days 7
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --no-expiry
sudo dotnet AnalysisITC.Web.dll operator-code list
sudo dotnet AnalysisITC.Web.dll operator-code revoke ID
sudo dotnet AnalysisITC.Web.dll usage-log status
sudo dotnet AnalysisITC.Web.dll usage-log list --since 24h --limit 100
sudo dotnet AnalysisITC.Web.dll usage-log show REQUEST_ID
sudo dotnet AnalysisITC.Web.dll usage-log summary --since 7d
sudo dotnet AnalysisITC.Web.dll usage-log summary --since 7d --model MODEL --operator ID
sudo dotnet AnalysisITC.Web.dll usage-log export --since 2026-09-01 --output /absolute/path/usage.csv
```

These commands also use the application configuration in `/opt/ftitc-web` and
do not require the protected provider environment file for the default paths.
