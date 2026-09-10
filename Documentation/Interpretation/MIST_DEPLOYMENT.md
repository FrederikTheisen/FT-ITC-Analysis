# MIST deployment notes

Deploy the Web project and its dependencies using the existing MIST procedure.
Stage the new service beside the current service, preserving the protected
credential environment, retrieval configuration, and hosted viewer files.
Keep a complete rollback copy of the prior deployment until production health
and a synthetic request have passed.

Before activation, run the Web tests and verify that a synthetic package using
the 5.0 envelope returns a response with the evidence schema, guidance
revision, instruction fingerprints, retrieval provenance, and effective-input
fingerprint. Use invented evidence only; never upload real experimental data
for deployment testing. Verify health and the interpretation status endpoint
after activation. Confirm that malformed JSON, missing envelope fields,
unsupported evidence schemas, invalid task types, and oversized bodies are
rejected before a model call. Exercise both the normal interpretation and
summary task with invented evidence; summary must not invoke retrieval or
consume capability-code quota.

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
`/var/lib/ftitc-web/interpretation-usage.db`. Preset mappings are stored in
`/etc/ftitc-web/generation-presets.json`.

The console provides:

In an interactive terminal, press a displayed number to select it immediately; Enter is
not required. Press Backspace to return from a submenu. Text-entry prompts still use Enter.

- **Status:** systemd state, local and public interpretation status, build and
  schema versions, operator-account totals, and usage-database statistics.
- **Operator accounts:** create accounts or show a compact `ID / Name or label / Email /
  Level` directory. Looking up an account asks directly for its exact ID and opens a
  details page with usage plus a settings menu for updating details, changing access level,
  changing quota, or revoking the account. The details lookup does not print every account.
  Opening the page immediately shows the account identity, access, quota, dates, status,
  lifetime interpretation count, and lifetime estimated cost. **All details** provides the
  period-filtered token, latency, outcome, preset, model, and recent-request breakdown.
  For a quota-limited account, the basic page also shows estimated spend subtracted from the
  effective monthly allowance, the resulting dollar and percentage balance, and the next
  UTC calendar-month reset. Billable failed attempts count when provider usage is available;
  validation failures and requests without provider usage do not consume the allowance.
  The page can inspect an
  account's request counts, token use, estimated cost, latency, outcomes, presets, models,
  and recent request metadata over a selected time period. A new secret
  is printed once. Listings and logs never contain the secret or its hash.
- **Logs:** list requests, show one request and its provider attempts, summarize
  a period with optional model/operator filters, or export metadata to CSV.
  Interactive exports default to `/home/logexports/`, with the UTC export time
  and selected horizon in the filename; an absolute custom path remains available.
- **Generation presets:** list or edit the allowlisted model/reasoning mapping for Fast,
  Default, Advanced, and Thorough, edit the tier quota defaults, and edit request-size
  limits for Public, Registered, Advanced, and Administrator access. Size values are whole
  KiB from 1 through 2048. Confirmed changes
  apply immediately. Registered accounts receive $1/month and Advanced-tier accounts
  receive $3/month unless an account override changes that limit. The allowance is shared
  across Default, Advanced, and Thorough requests made with that capability code. Fast
  requests do not reduce the monetary allowance.

The preset IDs on the wire remain `instant`, `fast`, `standard`, and `in-depth` for
client compatibility. The public options API reports quota usage only as a whole-number
percentage remaining and a UTC reset time. The authenticated account API additionally
returns the effective dollar allowance and spend, optional account contact details, and
the all-time recorded request summary for the supplied capability code.

An operator account is revoked rather than deleted so historical usage remains
attributable to its non-secret record ID. The registry directory is owned by
`root:ftitc-web` with set-group-ID mode `2750`; the registry is `0640`. Atomic
registry replacements preserve owner read/write and group read permissions.

The original non-interactive commands remain available for automation:

```bash
cd /opt/ftitc-web
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name"
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --tier standard
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --tier standard --name "Person" --email "person@example.org" --organization "Lab"
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --expires-days 7
sudo dotnet AnalysisITC.Web.dll operator-code create --label "Name" --no-expiry
sudo dotnet AnalysisITC.Web.dll operator-code set-quota ACCOUNT_ID 2.50
sudo dotnet AnalysisITC.Web.dll operator-code set-quota ACCOUNT_ID unlimited
sudo dotnet AnalysisITC.Web.dll operator-code list
sudo dotnet AnalysisITC.Web.dll operator-code revoke ID
sudo dotnet AnalysisITC.Web.dll operator-code set-tier ID advanced
sudo dotnet AnalysisITC.Web.dll generation-presets list
sudo dotnet AnalysisITC.Web.dll generation-presets set fast gpt-5.6-luna medium
sudo dotnet AnalysisITC.Web.dll generation-presets set-request-size public 128
sudo dotnet AnalysisITC.Web.dll usage-log status
sudo dotnet AnalysisITC.Web.dll usage-log list --since 24h --limit 100
sudo dotnet AnalysisITC.Web.dll usage-log show REQUEST_ID
sudo dotnet AnalysisITC.Web.dll usage-log summary --since 7d
sudo dotnet AnalysisITC.Web.dll usage-log summary --since 7d --model MODEL --operator ID
sudo dotnet AnalysisITC.Web.dll usage-log export --since 2026-09-01 --output /absolute/path/usage.csv
```

These commands also use the application configuration in `/opt/ftitc-web` and
do not require the protected provider environment file for the default paths.
